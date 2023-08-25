using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface ITeams {
    Async.Task NotifyTeams(TeamsTemplate config, Container container, IReport reportOrRegression, Guid notificationId);
}

public class Teams : ITeams {
    private readonly ILogger _logTracer;
    private readonly IOnefuzzContext _context;
    private readonly IHttpClientFactory _httpFactory;

    public Teams(IHttpClientFactory httpFactory, ILogger<Teams> logTracer, IOnefuzzContext context) {
        _logTracer = logTracer;
        _context = context;
        _httpFactory = httpFactory;
    }

    private static string CodeBlock(string data) {
        data = data.Replace("`", "``");
        return $"\n```\n{data}\n```\n";
    }

    private async Async.Task SendTeamsWebhook(TeamsTemplate config, string title, IList<Dictionary<string, string>> facts, string? text, Guid notificationId) {
        title = MarkdownEscape(title);

        var sections = new List<Dictionary<string, object>>() {
            new() {
                {"activityTitle", title},
                {"facts", facts}
            }
        };
        if (text != null) {
            sections.Add(new() {
                { "text", text }
            });
        }

        var message = new Dictionary<string, object>() {
            {"@type", "MessageCard"},
            {"@context", "https://schema.org/extensions"},
            {"summary", title},
            {"sections", sections}
        };

        var configUrl = await _context.SecretsOperations.GetSecretValue(config.Url.Secret);
        var client = new Request(_httpFactory.CreateClient());
        var response = await client.Post(url: new Uri(configUrl!), JsonSerializer.Serialize(message));
        if (response == null || !response.IsSuccessStatusCode) {
            _logTracer.LogError("webhook failed {NotificationId} {StatusCode} {content}", notificationId, response?.StatusCode, response?.Content);
        }
    }

    public async Async.Task NotifyTeams(TeamsTemplate config, Container container, IReport reportOrRegression, Guid notificationId) {
        var facts = new List<Dictionary<string, string>>();
        string? text = null;
        var title = string.Empty;
        var filename = reportOrRegression.FileName();

        if (reportOrRegression is Report report) {
            var task = await _context.TaskOperations.GetByJobIdAndTaskId(report.JobId, report.TaskId);
            if (task == null) {
                _logTracer.LogError("report with invalid task {JobId}:{TaskId}", report.JobId, report.TaskId);
                return;
            }

            title = $"new crash in {report.Executable}: {report.CrashType} @ {report.CrashSite}";

            var links = new List<string> {
                $"[report]({_context.Containers.AuthDownloadUrl(container, filename)})"
            };

            var setupContainer = Scheduler.GetSetupContainer(task.Config);
            if (setupContainer != null) {
                var setupFileName = NotificationsBase.ReplaceFirstSetup(report.Executable);
                links.Add(
                    $"[executable]({_context.Containers.AuthDownloadUrl(setupContainer, setupFileName)})"
                );
            }

            if (report.InputBlob != null) {
                links.Add(
                    $"[input]({_context.Containers.AuthDownloadUrl(report.InputBlob.Container, report.InputBlob.Name)})"
                );
            }

            facts.AddRange(new List<Dictionary<string, string>> {
                new() {{"name", "Files"}, {"value", string.Join(" | ", links)}},
                new() {
                    {"name", "Task"},
                    {"value", MarkdownEscape(
                        $"job_id: {report.JobId} task_id: {report.TaskId}"
                    )}
                },
                new() {
                    {"name", "Repro"},
                    {"value", CodeBlock($"onefuzz repro create_and_connect {container} {filename}")}
                }
            });

            text = "## Call Stack\n" + string.Join("\n", report.CallStack.Select(cs => CodeBlock(cs)));
        } else {
            title = "new file found";
            var fileUrl = _context.Containers.AuthDownloadUrl(container, filename);

            facts.Add(new Dictionary<string, string>() {
                {"name", "file"},
                {"value", $"[{MarkdownEscape(container.String)}/{MarkdownEscape(filename)}]({fileUrl})"}
            });
        }

        await SendTeamsWebhook(config, title, facts, text, notificationId);
    }

    private static string MarkdownEscape(string data) {
        var values = "\\*_{}[]()#+-.!";
        foreach (var c in values) {
            data = data.Replace(c.ToString(), "\\" + c);
        }
        data = data.Replace("`", "``");
        return data;
    }
}
