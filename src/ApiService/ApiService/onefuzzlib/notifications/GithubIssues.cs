using Microsoft.Extensions.Logging;
using Octokit;
namespace Microsoft.OneFuzz.Service;

public interface IGithubIssues {
    Async.Task GithubIssue(GithubIssuesTemplate config, Container container, IReport reportable, Guid notificationId);
}

public class GithubIssues : NotificationsBase, IGithubIssues {

    public GithubIssues(ILogger<GithubIssues> logTracer, IOnefuzzContext context)
    : base(logTracer, context) { }

    public async Async.Task GithubIssue(GithubIssuesTemplate config, Container container, IReport reportable, Guid notificationId) {
        var filename = reportable.FileName();

        if (reportable is RegressionReport) {
            _logTracer.LogInformation("github issue integration does not support regression reports. {Container} - {Filename}", container, filename);
            return;
        }

        var report = (Report)reportable;

        try {
            await Process(config, container, filename, report);
        } catch (ApiException e) {
            await LogFailedNotification(report, e, notificationId);
        }
    }

    public static async Async.Task<OneFuzzResultVoid> Validate(GithubIssuesTemplate config) {
        // Validate PAT is valid
        GitHubClient gh;
        if (config.Auth.Secret is SecretValue<GithubAuth> auth) {
            try {
                gh = GetGitHubClient(auth.Value.User, auth.Value.PersonalAccessToken);
                var _ = await gh.User.Get(auth.Value.User);
            } catch (Exception e) {
                return OneFuzzResultVoid.Error(ErrorCode.GITHUB_VALIDATION_INVALID_PAT, new string[] {
                    $"Failed to login to github.com with user {auth.Value.User} and the provided Personal Access Token",
                    $"Exception: {e}"
                });
            }
        } else {
            return OneFuzzResultVoid.Error(ErrorCode.GITHUB_VALIDATION_INVALID_PAT, $"GithubAuth is missing or invalid");
        }

        try {
            var _ = await gh.Repository.Get(config.Organization, config.Repository);
        } catch (Exception e) {
            return OneFuzzResultVoid.Error(ErrorCode.GITHUB_VALIDATION_INVALID_REPOSITORY, new string[] {
                $"Failed to access repository: {config.Organization}/{config.Repository}",
                $"Exception: {e}"
            });
        }

        return OneFuzzResultVoid.Ok;
    }

    private static GitHubClient GetGitHubClient(string user, string pat) {
        return new GitHubClient(new ProductHeaderValue("OneFuzz")) {
            Credentials = new Credentials(user, pat)
        };
    }

    private async Async.Task Process(GithubIssuesTemplate config, Container container, string filename, Report report) {
        var renderer = await Renderer.ConstructRenderer(_context, container, filename, report, _logTracer);
        var handler = await GithubConnnector.GithubConnnectorCreator(config, container, filename, renderer, _context.Creds.GetInstanceUrl(), _context, _logTracer);
        await handler.Process();
    }
    sealed class GithubConnnector {
        private readonly GitHubClient _gh;
        private readonly GithubIssuesTemplate _config;
        private readonly Renderer _renderer;
        private readonly Uri _instanceUrl;
        private readonly ILogger _logTracer;

        public static async Async.Task<GithubConnnector> GithubConnnectorCreator(GithubIssuesTemplate config, Container container, string filename, Renderer renderer, Uri instanceUrl, IOnefuzzContext context, ILogger logTracer) {
            var auth = await context.SecretsOperations.GetSecretValue(config.Auth.Secret);
            if (auth == null) {
                throw new Exception($"Failed to retrieve the auth info for {config}");
            }
            return new GithubConnnector(config, renderer, instanceUrl, auth, logTracer);
        }

        public GithubConnnector(GithubIssuesTemplate config, Renderer renderer, Uri instanceUrl, GithubAuth auth, ILogger logTracer) {
            _config = config;
            _gh = GetGitHubClient(auth.User, auth.PersonalAccessToken);
            _renderer = renderer;
            _instanceUrl = instanceUrl;
            _logTracer = logTracer;
        }

        public async Async.Task Process() {
            var issues = await Existing();
            if (issues.Any()) {
                await Update(issues.First());
            } else {
                await Create();
            }
        }

        private async Async.Task<string> Render(string field) {
            try {
                return await _renderer.Render(field, _instanceUrl, strictRendering: true);
            } catch {
                _logTracer.LogWarning("Failed to render field in strict mode. Falling back to relaxed mode. {Field}", field);
                return await _renderer.Render(field, _instanceUrl, strictRendering: false);
            }
        }

        private async Async.Task<List<Issue>> Existing() {
            var query = new List<string>() {
                "is:issue",
                await Render(_config.UniqueSearch.str),
                $"repo:{_config.Organization}/{_config.Repository}"
            };

            if (_config.UniqueSearch.Author != null) {
                query.Add($"author:{await Render(_config.UniqueSearch.Author)}");
            }

            if (_config.UniqueSearch.State != null) {
                query.Add($"state:{_config.UniqueSearch.State}");
            }

            var title = await Render(_config.Title);
            var body = await Render(_config.Body);
            var issues = new List<Issue>();
            var t = await _gh.Search.SearchIssues(new SearchIssuesRequest(string.Join(' ', query)));
            foreach (var issue in t.Items) {
                var skip = false;
                foreach (var field in _config.UniqueSearch.FieldMatch) {
                    if (field == GithubIssueSearchMatch.Title && issue.Title != title) {
                        skip = true;
                        break;
                    }
                    if (field == GithubIssueSearchMatch.Body && issue.Body != body) {
                        skip = true;
                        break;
                    }
                }
                if (!skip) {
                    issues.Add(issue);
                }
            }
            return issues;
        }

        private async Async.Task Update(Issue issue) {
            _logTracer.LogInformation("updating issue: {Issue}", issue);
            if (_config.OnDuplicate.Comment != null) {
                _ = await _gh.Issue.Comment.Create(issue.Repository.Id, issue.Number, await Render(_config.OnDuplicate.Comment));
            }
            if (_config.OnDuplicate.Labels.Any()) {
                var labels = await _config.OnDuplicate.Labels.ToAsyncEnumerable()
                    .SelectAwait(async label => await Render(label))
                    .ToArrayAsync();

                _ = await _gh.Issue.Labels.ReplaceAllForIssue(issue.Repository.Id, issue.Number, labels);
            }
            if (_config.OnDuplicate.Reopen && issue.State != ItemState.Open) {
                _ = await _gh.Issue.Update(issue.Repository.Id, issue.Number, new IssueUpdate() {
                    State = ItemState.Open
                });
            }
        }

        private async Async.Task Create() {
            _logTracer.LogInformation("creating issue");
            var assignees = await _config.Assignees.ToAsyncEnumerable()
                .SelectAwait(async assignee => await Render(assignee))
                .ToListAsync();

            var labels = await _config.Labels.ToAsyncEnumerable()
                .SelectAwait(async label => await Render(label))
                .ToHashSetAsync();

            _ = labels.Add("OneFuzz");

            var newIssue = new NewIssue(await Render(_config.Title)) {
                Body = await Render(_config.Body),
            };

            labels.ToList().ForEach(label => newIssue.Labels.Add(label));
            assignees.ForEach(assignee => newIssue.Assignees.Add(assignee));

            _ = await _gh.Issue.Create(
                await Render(_config.Organization),
                await Render(_config.Repository),
                newIssue);
        }
    }
}
