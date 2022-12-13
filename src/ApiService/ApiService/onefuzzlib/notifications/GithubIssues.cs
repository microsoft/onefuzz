using Octokit;

namespace Microsoft.OneFuzz.Service;

public interface IGithubIssues {
    Async.Task GithubIssue(GithubIssuesTemplate config, Container container, string filename, IReport? reportable, Guid notificationId);
}

public class GithubIssues : NotificationsBase, IGithubIssues {

    public GithubIssues(ILogTracer logTracer, IOnefuzzContext context)
    : base(logTracer, context) { }

    public async Async.Task GithubIssue(GithubIssuesTemplate config, Container container, string filename, IReport? reportable, Guid notificationId) {
        if (reportable == null) {
            return;
        }

        if (reportable is RegressionReport) {
            _logTracer.Info($"github issue integration does not support regression reports. {container:Tag:Container} - {filename:Tag:Filename}");
            return;
        }

        var report = (Report)reportable;

        try {
            await Process(config, container, filename, report);
        } catch (ApiException e) {
            LogFailedNotification(report, e, notificationId);
        }
    }

    private async Async.Task Process(GithubIssuesTemplate config, Container container, string filename, Report report) {
        var renderer = await Renderer.ConstructRenderer(_context, container, filename, report);
        var handler = await GithubConnnector.GithubConnnectorCreator(config, container, filename, renderer, _context.Creds.GetInstanceUrl(), _context, _logTracer);
        await handler.Process();
    }
    sealed class GithubConnnector {
        private readonly GitHubClient _gh;
        private readonly GithubIssuesTemplate _config;
        private readonly Renderer _renderer;
        private readonly Uri _instanceUrl;
        private readonly ILogTracer _logTracer;

        public static async Async.Task<GithubConnnector> GithubConnnectorCreator(GithubIssuesTemplate config, Container container, string filename, Renderer renderer, Uri instanceUrl, IOnefuzzContext context, ILogTracer logTracer) {
            var auth = config.Auth.Secret switch {
                SecretAddress<GithubAuth> sa => await context.SecretsOperations.GetSecretObj<GithubAuth>(sa.Url),
                SecretValue<GithubAuth> sv => sv.Value,
                _ => throw new ArgumentException($"Unexpected secret type {config.Auth.Secret.GetType()}")
            };
            return new GithubConnnector(config, container, filename, renderer, instanceUrl, auth!, logTracer);
        }

        public GithubConnnector(GithubIssuesTemplate config, Container container, string filename, Renderer renderer, Uri instanceUrl, GithubAuth auth, ILogTracer logTracer) {
            _config = config;
            _gh = new GitHubClient(new ProductHeaderValue("OneFuzz")) {
                Credentials = new Credentials(auth.User, auth.PersonalAccessToken)
            };
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
                _logTracer.Warning($"Failed to render field in strict mode. Falling back to relaxed mode. {field:Field}");
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
            _logTracer.Info($"updating issue: {issue}");
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
            _logTracer.Info($"creating issue");
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
