using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskEventOperations : IOrm<TaskEvent> {
    IAsyncEnumerable<TaskEventSummary> GetSummary(Guid taskId);
}

public sealed class TaskEventOperations : Orm<TaskEvent>, ITaskEventOperations {
    public TaskEventOperations(ILogTracer logTracer, IOnefuzzContext context)
        : base(logTracer, context) { }

    public IAsyncEnumerable<TaskEventSummary> GetSummary(Guid taskId) {
        return
        SearchByPartitionKeys(new[] { $"{taskId}" })
            .OrderBy(x => x.TimeStamp ?? DateTimeOffset.MaxValue)
            .Select(x => new TaskEventSummary(x.TimeStamp, GetEventData(x.EventData), GetEventType(x.EventData)));
    }

    private static string GetEventData(WorkerEvent ev) {
        return ev.Done != null ? $"exit status: {ev.Done.ExitStatus}" :
            ev.Running != null ? string.Empty : "Unrecognized event: {ev}";
    }

    private static string GetEventType(WorkerEvent ev) {
        return ev.GetType().Name;
    }
}
