using System.IO;
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

    public async Async.Task LogFailedNotification(Report report, Exception error, Guid notificationId) {
        _logTracer.Error($"notification failed: notification_id:{notificationId:Tag:NotificationId} job_id:{report.JobId:Tag:JobId} task_id:{report.TaskId:Tag:TaskId} err:{error.Message:Tag:Error}");
        Error? err = new Error(ErrorCode.NOTIFICATION_FAILURE, new string[] { $"{error}" });
        await _context.Events.SendEvent(new EventNotificationFailed(
            NotificationId: notificationId,
            JobId: report.JobId,
            Error: err)
        );
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

        public static async Async.Task<Renderer> ConstructRenderer(
            IOnefuzzContext context,
            Container container,
            string filename,
            Report report,
            Task? task = null,
            Job? job = null,
            Uri? targetUrl = null,
            Uri? inputUrl = null,
            Uri? reportUrl = null) {

            task ??= await context.TaskOperations.GetByJobIdAndTaskId(report.JobId, report.TaskId);
            var checkedTask = task.EnsureNotNull($"invalid task {report.TaskId}");

            job ??= await context.JobOperations.Get(report.JobId);
            var checkedJob = job.EnsureNotNull($"invalid job {report.JobId}");

            if (targetUrl == null) {
                var setupContainer = Scheduler.GetSetupContainer(checkedTask.Config);
                var exeName = Path.GetFileName(ReplaceFirstSetup(report.Executable));
                targetUrl = new Uri(context.Containers.AuthDownloadUrl(setupContainer, exeName));
            }

            if (reportUrl == null) {
                reportUrl = new Uri(context.Containers.AuthDownloadUrl(container, filename));
            }

            if (inputUrl == null && report.InputBlob != null) {
                inputUrl = new Uri(context.Containers.AuthDownloadUrl(report.InputBlob.Container, report.InputBlob.Name));
            }

            return new Renderer(
                container,
                filename,
                report,
                checkedTask,
                checkedJob,
                targetUrl,
                inputUrl!, // TODO: incorrect
                reportUrl);
        }
        public Renderer(
            Container container,
            string filename,
            Report report,
            Task task,
            Job job,
            Uri targetUrl,
            Uri inputUrl,
            Uri reportUrl) {
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
            templateString = JinjaTemplateAdapter.IsJinjaTemplate(templateString) ? JinjaTemplateAdapter.AdaptForScriban(templateString) : templateString;
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
