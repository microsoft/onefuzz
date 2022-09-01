using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.OneFuzz.Service;


public interface IConfigOperations : IOrm<InstanceConfig> {
    Task<InstanceConfig> Fetch();

    Async.Task Save(InstanceConfig config, bool isNew, bool requireEtag);
}

public class ConfigOperations : Orm<InstanceConfig>, IConfigOperations {
    private readonly ILogTracer _log;
    private readonly IMemoryCache _cache;

    public ConfigOperations(ILogTracer log, IOnefuzzContext context, IMemoryCache cache)
        : base(log, context) {
        _log = log;
        _cache = cache;
    }

    private sealed record InstanceConfigCacheKey();
    private static readonly InstanceConfigCacheKey _key = new(); // singleton key
    public Task<InstanceConfig> Fetch()
        => _cache.GetOrCreateAsync(_key, async entry => {
            entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(10)); // cached for 10 minutes
            var key = _context.ServiceConfiguration.OneFuzzInstanceName ?? throw new Exception("Environment variable ONEFUZZ_INSTANCE_NAME is not set");
            return await GetEntityAsync(key, key);
        });

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

        if (r.IsOk) {
            _cache.Set(_key, config);
        }

        await _context.Events.SendEvent(new EventInstanceConfigUpdated(config));
    }
}
