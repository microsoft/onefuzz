using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;


public class QueueJobResult {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public QueueJobResult(ILogger<QueueJobResult> logTracer, IOnefuzzContext context) {
        _log = logTracer;
        _context = context;
    }

    [Function("QueueTaskJobResult")]
    public async Async.Task Run([QueueTrigger("job-result", Connection = "AzureWebJobsStorage")] string msg) {

        var _tasks = _context.TaskOperations;
        var _jobs = _context.JobOperations;

        _log.LogInformation("heartbeat: {msg}", msg);
        var jr = JsonSerializer.Deserialize<TaskJobResultEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var task = await _tasks.GetByTaskId(jr.TaskId);
        if (task == null) {
            _log.LogWarning("invalid {TaskId}", jr.TaskId);
            return;
        }

        var job = await _jobs.Get(task.JobId);
        if (job == null) {
            _log.LogWarning("invalid {JobId}", task.JobId);
            return;
        }

        JobResultData? data;
        if (jr.Data.Length > 0)
            data = jr.Data[0];
        else {
            _log.LogWarning($"heartbeat data is empty, throwing out: {jr}");
            return;
        }

        var jobResultType = data.Type;
        _log.LogInformation($"heartbeat data type: {jobResultType}");

        var jobResult = await _context.JobResultOperations.CreateOrUpdate(job.JobId, jobResultType);
        if (!jobResult.IsOk) {
            _log.LogError("failed to create or update with job result {JobId}", job.JobId);
        }
    }
}
