using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IJobOperations : IStatefulOrm<Job, JobState>
{

}


public class JobOperations : StatefulOrm<Job, JobState>, IJobOperations
{

    public JobOperations(IStorage storage, ILogTracer log, IServiceConfig config)
        : base(storage, log, config)
    {
    }

}
