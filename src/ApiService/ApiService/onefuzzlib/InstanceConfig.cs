using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public interface IConfigOperations : IOrm<InstanceConfig> {
    Task<InstanceConfig> Fetch();

    Async.Task Save(InstanceConfig config, bool isNew, bool requireEtag);
}

public class ConfigOperations : Orm<InstanceConfig>, IConfigOperations {
    private readonly IEvents _events;
    private readonly ILogTracer _log;

    public ConfigOperations(IStorage storage, IEvents events, ILogTracer log, IServiceConfig config) : base(storage, log, config) {
        _events = events;
        _log = log;
    }

    public async Task<InstanceConfig> Fetch() {
        var key = _config.OneFuzzInstanceName ?? throw new Exception("Environment variable ONEFUZZ_INSTANCE_NAME is not set");
        var config = await GetEntityAsync(key, key);
        return config;
    }

    public async Async.Task Save(InstanceConfig config, bool isNew = false, bool requireEtag = false) {
        ResultVoid<(int, string)> r;
        if (isNew) {
            r = await Insert(config);
            if (!r.IsOk) {
                var (status, reason) = r.ErrorV;
                _log.Error($"Failed to save new instance config record with result [{status}] {reason}");
            }
        } else if (requireEtag && config.ETag.HasValue) {
            r = await Update(config);
            if (!r.IsOk) {
                var (status, reason) = r.ErrorV;
                _log.Error($"Failed to update instance config record with result: [{status}] {reason}");
            }
        } else {
            r = await Replace(config);
            if (!r.IsOk) {
                var (status, reason) = r.ErrorV;
                _log.Error($"Failed to replace instance config record with result [{status}] {reason}");
            }
        }

        await _events.SendEvent(new EventInstanceConfigUpdated(config));
    }
}
