using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;

public class JobTaskInfo : IFromJsonElement<JobTaskInfo> {
    readonly JsonElement _e;

    public JobTaskInfo(JsonElement e) => _e = e;

    public static JobTaskInfo Convert(JsonElement e) => new(e);

    public Guid TaskId => _e.GetGuidProperty("task_id");

    public string Type => _e.GetStringProperty("type");

    public string State => _e.GetStringProperty("state");
}


public class Job : IFromJsonElement<Job> {
    readonly JsonElement _e;

    public Job(JsonElement e) => _e = e;

    public static Job Convert(JsonElement e) => new(e);

    public Guid JobId => _e.GetGuidProperty("job_id");

    public string State => _e.GetStringProperty("state");

    public string? Error => _e.GetNullableStringProperty("error");

    public DateTimeOffset? EndTime => _e.GetNullableDateTimeOffsetProperty("end_time");

    public IEnumerable<JobTaskInfo>? TaskInfo => _e.GetEnumerableNullableProperty<JobTaskInfo>("task_info");
}

public class Jobs : ApiBase {

    public Jobs(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Jobs", request, output) {
    }

    public async Task<Result<IEnumerable<Job>, Error>> Get(Guid? jobId = null, List<string>? state = null, List<string>? taskState = null, bool? withTasks = null) {
        var n = new JsonObject()
            .AddIfNotNullV("job_id", jobId)
            .AddIfNotNullV("state", state)
            .AddIfNotNullV("task_state", taskState)
            .AddIfNotNullV("with_tasks", withTasks);


        var r = await Get(n);
        return IEnumerableResult<Job>(r);
    }


    public async Task<Result<Job, Error>> Post(string project, string name, string build, long duration, string? logs = null) {
        var n = new JsonObject()
            .AddV("project", project)
            .AddV("name", name)
            .AddV("build", build)
            .AddV("duration", duration)
            .AddIfNotNullV("logs", logs);

        var r = await Post(n);
        return Result<Job>(r);
    }

    public async Task<Result<Job, Error>> Delete(Guid jobId) {
        var n = new JsonObject().AddV("job_id", jobId);
        var r = await Delete(n);
        return Result<Job>(r);
    }
}
