using Octokit;

namespace Microsoft.OneFuzz.Service;

public interface IGithubIssues {
    Async.Task GithubIssue(GithubIssuesTemplate config, Container container, string filename, IReport? report);
}

public class GithubIssues : NotificationsBase, IGithubIssues {

    public GithubIssues(ILogTracer logTracer, IOnefuzzContext context)
    : base(logTracer, context) { }

    public async Async.Task GithubIssue(GithubIssuesTemplate config, Container container, string filename, IReport? report) {
        if (report == null) {
            return;
        }

        if (report is RegressionReport) {
            _logTracer.Info($"github issue integration does not support regression reports. container:{container} filename:{filename}");
            return;
        }

        try {
            await Process(config, container, filename, (Report)report);
        } catch (Exception e) { // TODO: You only want to catch "GithubException" and ValueError
            await FailTask((Report)report, e);
        }
    }

    private async Async.Task Process(GithubIssuesTemplate config, Container container, string filename, Report report) {
        var renderer = await Renderer.ConstructRenderer(_context, container, filename, report);
        var handler = new GithubConnnector(config, container, filename, report, renderer, _context.Creds.GetInstanceUrl());
        await handler.Process();
    }
    class GithubConnnector {
        private GitHubClient _gh;
        private GithubIssuesTemplate _config;
        private Report _report;
        private Renderer _renderer;
        private Uri _instanceUrl;

        public GithubConnnector(GithubIssuesTemplate config, Container container, string filename, Report report, Renderer renderer, Uri instanceUrl) {
            _config = config;
            _report = report;
            _gh = new GitHubClient(new ProductHeaderValue("microsoft/OneFuzz"));

            // We have an if/else here in the python code but the type (both C# and python)
            // doesn't allow for anything other than GithubAuth....
            var auth = config.Auth.Value;
            _gh.Credentials = new Credentials(auth.User, auth.PersonalAccessToken);
            _renderer = renderer;
            _instanceUrl = instanceUrl;
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
            return await _renderer.Render(field, _instanceUrl);
        }

        private async Async.Task<List<Issue>> Existing() {
            var query = new List<string>() {
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
            // TODO: logging.info($"updating issue: {issue}");
            if (_config.OnDuplicate.Comment != null) {
                await _gh.Issue.Comment.Create(issue.Repository.Id, issue.Number, await Render(_config.OnDuplicate.Comment));
            }
            if (_config.OnDuplicate.Labels.Any()) {
                var labels = await _config.OnDuplicate.Labels.ToAsyncEnumerable()
                    .SelectAwait(async label => await Render(label))
                    .ToArrayAsync();

                await _gh.Issue.Labels.ReplaceAllForIssue(issue.Repository.Id, issue.Number, labels);
            }
            if (_config.OnDuplicate.Reopen && issue.State != ItemState.Open) {
                await _gh.Issue.Update(issue.Repository.Id, issue.Number, new IssueUpdate() {
                    State = ItemState.Open
                });
            }
        }

        private async Async.Task Create() {
            //TODO: logging.info($"creating issue");
            var assignees = await _config.Assignees.ToAsyncEnumerable()
                .SelectAwait(async assignee => await Render(assignee))
                .ToListAsync();

            var labels = await _config.Labels.ToAsyncEnumerable()
                .SelectAwait(async label => await Render(label))
                .ToHashSetAsync();

            labels.Add("OneFuzz");

            var newIssue = new NewIssue(await Render(_config.Title)) {
                Body = await Render(_config.Body),
            };

            labels.ToList().ForEach(label => newIssue.Labels.Add(label));
            assignees.ForEach(assignee => newIssue.Assignees.Add(assignee));

            await _gh.Issue.Create(
                await Render(_config.Organization),
                await Render(_config.Repository),
                newIssue
            );
        }
    }
}

