using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IAdoNotificationEntryOperations : IOrm<AdoNotificationEntry> {

    public IAsyncEnumerable<AdoNotificationEntry> GetByJobId(Guid jobId);

    public Async.Task<bool> WasNotfied(Guid jobId);

}
public class AdoNotificationEntryOperations : Orm<AdoNotificationEntry>, IAdoNotificationEntryOperations {

    public AdoNotificationEntryOperations(ILogger<AdoNotificationEntryOperations> log, IOnefuzzContext context)
        : base(log, context) {

    }

    public IAsyncEnumerable<AdoNotificationEntry> GetByJobId(Guid jobId) {
        return QueryAsync(filter: Query.PartitionKey(jobId.ToString()));
    }

    public async Async.Task<bool> WasNotfied(Guid jobId) {
        return await QueryAsync(filter: Query.PartitionKey(jobId.ToString()), maxPerPage: 1).AnyAsync();
    }
}
