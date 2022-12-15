using System.IO;
using Scriban;
using Scriban.Runtime;

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

    public void LogFailedNotification(Report report, Exception error, Guid notificationId) {
        _logTracer.Error($"notification failed: notification_id:{notificationId:Tag:NotificationId} job_id:{report.JobId:Tag:JobId} task_id:{report.TaskId:Tag:TaskId} err:{error.Message:Tag:Error}");
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
            Report report,
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

            await context.ConfigurationRefresher.TryRefreshAsync().IgnoreResult();
            var scribanOnly = scribanOnlyOverride ?? await context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableScribanOnly);

            return new Renderer(
                container,
                filename,
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
        public async Async.Task<string> Render(string templateString, Uri instanceUrl, bool strictRendering = false) {
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
                $"onefuzz --endpoint {instanceUrl} repro create_and_connect {_container} {_filename}"
            ));

            var context = strictRendering switch {
                true => new TemplateContext {
                    EnableRelaxedFunctionAccess = false,
                    EnableRelaxedIndexerAccess = false,
                    EnableRelaxedMemberAccess = false,
                    EnableRelaxedTargetAccess = false
                },
                _ => new TemplateContext()
            };

            context.PushGlobal(scriptObject);

            var template = Template.Parse(templateString);
            if (template != null) {
                return await template.RenderAsync(context);
            }
            return string.Empty;
        }
    }
}
