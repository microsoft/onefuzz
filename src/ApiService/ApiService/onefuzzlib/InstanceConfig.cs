using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public interface IConfigOperations : IOrm<InstanceConfig> {
    Task<InstanceConfig> Fetch();

    Async.Task Save(InstanceConfig config, bool isNew, bool requireEtag);
}

public class ConfigOperations : Orm<InstanceConfig>, IConfigOperations {
    private readonly ILogTracer _log;

    public ConfigOperations(ILogTracer log, IOnefuzzContext context) : base(log, context) {
        _log = log;
    }

    public async Task<InstanceConfig> Fetch() {
        var key = _context.ServiceConfiguration.OneFuzzInstanceName ?? throw new Exception("Environment variable ONEFUZZ_INSTANCE_NAME is not set");
        var config = await GetEntityAsync(key, key);
        return config;
    }

    public async Async.Task Save(InstanceConfig config, bool isNew = false, bool requireEtag = false) {
        ResultVoid<(int, string)> r;
        if (isNew) {
            r = await Insert(config);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to save new instance config record");
            }
        } else if (requireEtag && config.ETag.HasValue) {
            r = await Update(config);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to update instance config record");
            }
        } else {
            r = await Replace(config);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to replace instance config record");
            }
        }

        await _context.Events.SendEvent(new EventInstanceConfigUpdated(config));
    }
}
