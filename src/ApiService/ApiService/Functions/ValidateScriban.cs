using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class ValidateScriban {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;
    public ValidateScriban(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<TemplateValidationPost>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ValidateTemplate");
        }

        var instanceUrl = _context.ServiceConfiguration.OneFuzzInstance!;

        try {
            var (renderer, templateRenderContext) = await GenerateTemplateRenderContext(request.OkV.Context);

            var renderedTemaplate = await renderer.Render(request.OkV.Template, new Uri(instanceUrl), strictRendering: true);

            var response = new TemplateValidationResponse(
                renderedTemaplate,
                templateRenderContext
            );

            return await RequestHandling.Ok(req, response);
        } catch (Exception e) {
            return await new RequestHandling(_log).NotOk(
                req,
                RequestHandling.ConvertError(e),
                $"Template failed to render due to: `{e.Message}`"
            );
        }

    }

    [Function("ValidateScriban")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "POST")] HttpRequestData req) {
        return req.Method switch {
            "POST" => Post(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };
    }

    private async Async.Task<(NotificationsBase.Renderer, TemplateRenderContext)> GenerateTemplateRenderContext(TemplateRenderContext? templateRenderContext) {
        if (templateRenderContext != null) {
            _log.Info($"Using the request's TemplateRenderContext");
        } else {
            _log.Info($"Generating TemplateRenderContext");
        }

        var targetUrl = templateRenderContext?.TargetUrl ?? new Uri("https://example.com/targetUrl");
        var inputUrl = templateRenderContext?.InputUrl ?? new Uri("https://example.com/inputUrl");
        var reportUrl = templateRenderContext?.ReportUrl ?? new Uri("https://example.com/reportUrl");
        var executable = "target.exe";
        var crashType = "some crash type";
        var crashSite = "some crash site";
        var callStack = new List<string>()
        {
            "stack frame 0",
            "stack frame 1"
        };
        var callStackSha = "call stack sha";
        var inputSha = "input sha";
        var taskId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var taskState = TaskState.Running;
        var jobState = JobState.Enabled;
        var os = Os.Linux;
        var taskType = TaskType.LibfuzzerFuzz;
        var duration = 100;
        var project = "some project";
        var jobName = "job name";
        var buildName = "build name";
        var reportContainer = templateRenderContext?.ReportContainer ?? Container.Parse("example-container-name");
        var reportFileName = templateRenderContext?.ReportFilename ?? "example file name";
        var reproCmd = templateRenderContext?.ReproCmd ?? "onefuzz command to create a repro";
        var report = templateRenderContext?.Report ?? new Report(
                inputUrl.ToString(),
                null,
                executable,
                crashType,
                crashSite,
                callStack,
                callStackSha,
                inputSha,
                null,
                taskId,
                jobId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            );

        var task = new Task(
                jobId,
                taskId,
                taskState,
                os,
                templateRenderContext?.Task ?? new TaskConfig(
                    jobId,
                    null,
                    new TaskDetails(
                        taskType,
                        duration
                    )
                )
            );

        var job = new Job(
                jobId,
                jobState,
                templateRenderContext?.Job ?? new JobConfig(
                    project,
                    jobName,
                    buildName,
                    duration,
                    null
                )
            );

        var renderer = await NotificationsBase.Renderer.ConstructRenderer(
            _context,
            reportContainer,
            reportFileName,
            report,
            task,
            job,
            targetUrl,
            inputUrl,
            reportUrl,
            scribanOnlyOverride: true
        );

        templateRenderContext ??= new TemplateRenderContext(
            report,
            task.Config,
            job.Config,
            reportUrl,
            inputUrl,
            targetUrl,
            reportContainer,
            reportFileName,
            reproCmd
        );

        return (renderer, templateRenderContext);
    }
}
