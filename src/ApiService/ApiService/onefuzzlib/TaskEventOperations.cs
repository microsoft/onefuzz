using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskEventOperations : IOrm<TaskEvent> {
}

public sealed class TaskEventOperations : Orm<TaskEvent>, ITaskEventOperations {
    public TaskEventOperations(ILogTracer logTracer, IOnefuzzContext context)
        : base(logTracer, context) { }
}
