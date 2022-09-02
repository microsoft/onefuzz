using Scriban;

namespace Microsoft.OneFuzz.Service;

public abstract class NotificationsBase {

#pragma warning disable CA1051 // permit visible instance fields
    protected readonly ILogTracer _logTracer;
    protected readonly IOnefuzzContext _context;
#pragma warning restore CA1051
    public NotificationsBase(ILogTracer logTracer, IOnefuzzContext context) {
        _logTracer = logTracer;
        _context = context;
    }

    public async Async.Task FailTask(Report report, Exception error) {
        _logTracer.Error($"notification failed: job_id:{report.JobId} task_id:{report.TaskId} err:{error}");

        var task = await _context.TaskOperations.GetByJobIdAndTaskId(report.JobId, report.TaskId);
        if (task != null) {
            await _context.TaskOperations.MarkFailed(task, new Error(ErrorCode.NOTIFICATION_FAILURE, new string[] {
                "notification failed",
                error.ToString()
            }));
        }
    }

    public static string ReplaceFirstSetup(string executable) {
        var setup = "setup/";
        int index = executable.IndexOf(setup, StringComparison.InvariantCultureIgnoreCase);
        var setupFileName = (index < 0) ? executable : executable.Remove(index, setup.Length);
        return setupFileName;
    }

    protected class Renderer {
        private readonly Report _report;
        private readonly Container _container;
        private readonly string _filename;
        private readonly TaskConfig _taskConfig;
        private readonly JobConfig _jobConfig;
        private readonly Uri _targetUrl;
        private readonly Uri _inputUrl;
        private readonly Uri _reportUrl;

        public static async Async.Task<Renderer> ConstructRenderer(IOnefuzzContext context, Container container, string filename, Report report, Task? task = null, Job? job = null, Uri? targetUrl = null, Uri? inputUrl = null, Uri? reportUrl = null) {
            task ??= await context.TaskOperations.GetByJobIdAndTaskId(report.JobId, report.TaskId);
            task.EnsureNotNull($"invalid task {report.TaskId}");

            job ??= await context.JobOperations.Get(report.JobId);
            job.EnsureNotNull($"invalid job {report.JobId}");

            if (targetUrl == null) {
                var setupContainer = Scheduler.GetSetupContainer(task?.Config!);
                targetUrl = new Uri(await context.Containers.AuthDownloadUrl(setupContainer, ReplaceFirstSetup(report.Executable)));
            }

            if (reportUrl == null) {
                reportUrl = new Uri(await context.Containers.AuthDownloadUrl(container, filename));
            }

            if (inputUrl == null && report.InputBlob != null) {
                inputUrl = new Uri(await context.Containers.AuthDownloadUrl(report.InputBlob.Container, report.InputBlob.Name));
            }

            return new Renderer(container, filename, report, task!, job!, targetUrl, inputUrl!, reportUrl);
        }
        public Renderer(Container container, string filename, Report report, Task task, Job job, Uri targetUrl, Uri inputUrl, Uri reportUrl) {
            _report = report;
            _container = container;
            _filename = filename;
            _taskConfig = task.Config;
            _jobConfig = job.Config;
            _reportUrl = reportUrl;
            _targetUrl = targetUrl;
            _inputUrl = inputUrl;
        }

        // TODO: This function is fallible but the python
        // implementation doesn't have that so I'm trying to match it.
        // We should probably propagate any errors up 
        public async Async.Task<string> Render(string templateString, Uri instanceUrl) {
            var template = Template.Parse(templateString);
            if (template != null) {
                return await template.RenderAsync(new {
                    Report = this._report,
                    Task = this._taskConfig,
                    Job = this._jobConfig,
                    ReportUrl = this._reportUrl,
                    InputUrl = _inputUrl,
                    TargetUrl = _targetUrl,
                    ReportContainer = _container,
                    ReportFilename = _filename,
                    ReproCmd = $"onefuzz --endpoint {instanceUrl} repro create_and_connect {_container} {_filename}"
                });
            }
            return string.Empty;
        }
    }
}
