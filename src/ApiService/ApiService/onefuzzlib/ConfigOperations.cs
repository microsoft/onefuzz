using System.Net;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;


public interface IConfigOperations : IOrm<InstanceConfig> {
    Task<InstanceConfig> Fetch();

    Async.Task Save(InstanceConfig config, bool isNew, bool requireEtag);
}

public class ConfigOperations : Orm<InstanceConfig>, IConfigOperations {
    private readonly ILogger _log;
    private readonly IMemoryCache _cache;

    public ConfigOperations(ILogger<ConfigOperations> log, IOnefuzzContext context, IMemoryCache cache)
        : base(log, context) {
        _log = log;
        _cache = cache;
    }

    private static readonly object _instanceConfigCacheKey = new(); // singleton key; we only need hashcode/equality
    public Task<InstanceConfig> Fetch()
        => _cache.GetOrCreateAsync(_instanceConfigCacheKey, async entry => {
            entry = entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(1)); // cached for 1 minute
            var key = _context.ServiceConfiguration.OneFuzzInstanceName ?? throw new Exception("Environment variable ONEFUZZ_INSTANCE_NAME is not set");
            return await GetEntityAsync(key, key);
        })!; // NULLABLE: only this class inserts _instanceConfigCacheKey so it cannot be null

    public async Async.Task Save(InstanceConfig config, bool isNew = false, bool requireEtag = false) {
        var newConfig = config with { InstanceName = _context.ServiceConfiguration.OneFuzzInstanceName ?? throw new Exception("Environment variable ONEFUZZ_INSTANCE_NAME is not set") };
        ResultVoid<(HttpStatusCode Status, string Reason)> r;
        if (isNew) {
            r = await Insert(newConfig);
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("Failed to save new instance config record");
            }
        } else if (requireEtag && config.ETag.HasValue) {
            r = await Update(newConfig);
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("Failed to update instance config record");
            }
        } else {
            r = await Replace(newConfig);
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError($"Failed to replace instance config record");
            }
        }

        if (r.IsOk) {
            _ = _cache.Set(_instanceConfigCacheKey, newConfig);
        }

        await _context.Events.SendEvent(new EventInstanceConfigUpdated(newConfig));
    }
}
