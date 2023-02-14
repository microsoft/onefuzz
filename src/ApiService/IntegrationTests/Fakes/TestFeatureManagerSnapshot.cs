using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.FeatureManagement;

namespace IntegrationTests.Fakes;

public class TestFeatureManagerSnapshot : IFeatureManagerSnapshot {

    private static ConcurrentDictionary<string, bool> FeatureFlags = new();
    public IAsyncEnumerable<string> GetFeatureNamesAsync() {
        throw new System.NotImplementedException();
    }

    public Task<bool> IsEnabledAsync(string feature) {
        return Task.FromResult(FeatureFlags.ContainsKey(feature) && FeatureFlags.TryGetValue(feature, out var enabled) && enabled);
    }

    public Task<bool> IsEnabledAsync<TContext>(string feature, TContext context) {
        throw new System.NotImplementedException();
    }

    public static void AddFeatureFlag(string featureName, bool enabled = false) {
        var _ = FeatureFlags.TryAdd(featureName, enabled);
    }

    public static void SetFeatureFlag(string featureName, bool enabled) {
        var _ = FeatureFlags.TryUpdate(featureName, enabled, !enabled);
    }
}
