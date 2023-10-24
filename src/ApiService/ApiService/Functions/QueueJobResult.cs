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

    [Function("QueueJobResult")]
    public async Async.Task Run([QueueTrigger("job-result", Connection = "AzureWebJobsStorage")] string msg) {

        var _tasks = _context.TaskOperations;
        var _jobs = _context.JobOperations;

        _log.LogInformation("job result: {msg}", msg);
        var jr = JsonSerializer.Deserialize<TaskJobResultEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var task = await _tasks.GetByTaskId(jr.TaskId);
        if (task == null) {
            _log.LogWarning("invalid {TaskId}", jr.TaskId);
            return;
        }

        var job = await _jobs.Get(task.JobId);
        if (job == null) {
            _log.LogWarning("invalid message {JobId}", task.JobId);
            return;
        }

        if (jr.CreatedAt == null) {
            _log.LogWarning("invalid message, no created_at field {JobId}", task.JobId);
            return;
        }

        JobResultData? data = jr.Data;
        if (data == null) {
            _log.LogWarning($"job result data is empty, throwing out: {jr}");
            return;
        }

        var jobResultType = data.Type;
        _log.LogInformation($"job result data type: {jobResultType}");

        Dictionary<string, double> value;
        if (jr.Value.Count > 0) {
            value = jr.Value;
        } else {
            _log.LogWarning($"job result data is empty, throwing out: {jr}");
            return;
        }

        var jobResult = await _context.JobResultOperations.CreateOrUpdate(job.JobId, jr.TaskId, jr.MachineId, jr.CreatedAt, jobResultType, value);
        if (!jobResult.IsOk) {
            _log.LogError("failed to create or update with job result {JobId}", job.JobId);
        }
    }
}
