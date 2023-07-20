using System.IO;
using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Runtime;

namespace Microsoft.OneFuzz.Service;

public abstract class NotificationsBase {

#pragma warning disable CA1051 // permit visible instance fields
    protected readonly ILogger _logTracer;
    protected readonly IOnefuzzContext _context;
#pragma warning restore CA1051
    public NotificationsBase(ILogger<NotificationsBase> logTracer, IOnefuzzContext context) {
        _logTracer = logTracer;
        _context = context;
    }

    public async Async.Task LogFailedNotification(Report report, Exception error, Guid notificationId) {
        _logTracer.LogError("notification failed: notification_id:{NotificationId} job_id:{JobId} task_id:{TaskId} err:{Error}", notificationId, report.JobId, report.TaskId, error.Message);
        Error? err = Error.Create(ErrorCode.NOTIFICATION_FAILURE, $"{error}");
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

    public class Renderer {
        private readonly Report _report;
        private readonly Container _container;
        private readonly string _filename;
        private readonly string _issueTitle;
        private readonly TaskConfig _taskConfig;
        private readonly JobConfig _jobConfig;
        private readonly Uri _targetUrl;
        private readonly Uri _inputUrl;
        private readonly Uri _reportUrl;
        private readonly bool _scribanOnly;

        public static async Async.Task<Renderer> ConstructRenderer(
            IOnefuzzContext context,
            Container container,
            string filename,
            string issueTitle,
            Report report,
            ILogger log,
            Task? task = null,
            Job? job = null,
            Uri? targetUrl = null,
            Uri? inputUrl = null,
            Uri? reportUrl = null,
            bool? scribanOnlyOverride = null) {

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

            var scribanOnlyFeatureFlag = await context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.RenderOnlyScribanTemplates);
            log.LogInformation("ScribanOnlyFeatureFlag: {scribanOnlyFeatureFlag}", scribanOnlyFeatureFlag);

            var scribanOnly = scribanOnlyOverride ?? scribanOnlyFeatureFlag;

            return new Renderer(
                container,
                filename,
                issueTitle,
                report,
                checkedTask,
                checkedJob,
                targetUrl,
                inputUrl!, // TODO: incorrect
                reportUrl,
                scribanOnly);
        }
        public Renderer(
            Container container,
            string filename,
            string issueTitle,
            Report report,
            Task task,
            Job job,
            Uri targetUrl,
            Uri inputUrl,
            Uri reportUrl,
            bool scribanOnly) {
            _report = report;
            _container = container;
            _filename = filename;
            _issueTitle = issueTitle;
            _taskConfig = task.Config;
            _jobConfig = job.Config;
            _reportUrl = reportUrl;
            _targetUrl = targetUrl;
            _inputUrl = inputUrl;
            _scribanOnly = scribanOnly;
        }

        // TODO: This function is fallible but the python
        // implementation doesn't have that so I'm trying to match it.
        // We should probably propagate any errors up 
        public string Render(string templateString, Uri instanceUrl, bool strictRendering = false) {
            if (!_scribanOnly && JinjaTemplateAdapter.IsJinjaTemplate(templateString)) {
                templateString = JinjaTemplateAdapter.AdaptForScriban(templateString);
            }

            var scriptObject = new ScriptObject();
            scriptObject.Import(new TemplateRenderContext(
                this._report,
                this._taskConfig,
                this._jobConfig,
                this._reportUrl,
                _inputUrl,
                _targetUrl,
                _container,
                _filename,
                _issueTitle,
                $"onefuzz --endpoint {instanceUrl} repro create_and_connect {_container} {_filename}"
            ));

            var context = strictRendering switch {
                true => new TemplateContext {
                    EnableRelaxedFunctionAccess = false,
                    EnableRelaxedIndexerAccess = false,
                    EnableRelaxedMemberAccess = true,
                    EnableRelaxedTargetAccess = false
                },
                _ => new TemplateContext()
            };

            context.PushGlobal(scriptObject);

            var template = Template.Parse(templateString);
            if (template != null) {
                return template.Render(context);
            }
            return string.Empty;
        }
    }
}
