using System.Net;
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
        var targetUrl = "https://example.com/targetUrl";
        var inputUrl = "https://example.com/inputUrl";
        var reportUrl = "https://example.com/reportUrl";
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
        var os = Os.Linux;
        var taskType = TaskType.LibfuzzerFuzz;
        var duration = 100;

        var renderer = await NotificationsBase.Renderer.ConstructRenderer(
            _context,
            Container.Parse("exampleContainerName"),
            "example file name",
            new Report(
                inputUrl,
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
                null
            ),
            new Task(
                jobId,
                taskId,
                taskState,
                os,
                new TaskConfig(
                    jobId,
                    null,
                    new TaskDetails()
                )
            ),
            new Job(
                ...
            ),
            new Uri(targetUrl),
            new Uri(inputUrl),
            new Uri(reportUrl)
        );

        // return await renderer.Render()
    }

    [Function("ValidateScriban")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "POST")] HttpRequestData req) {
        return req.Method switch {
            "POST" => Post(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };
    }
}
