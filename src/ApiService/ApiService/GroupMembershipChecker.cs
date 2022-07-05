using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

abstract class GroupMembershipChecker {
    protected abstract Async.Task<IEnumerable<Guid>> GetGroups(Guid memberId);

    public async Async.Task<bool> IsMember(IEnumerable<Guid> groupIds, Guid memberId) {
        if (groupIds.Contains(memberId)) {
            return true;
        }

        var memberGroups = await GetGroups(memberId);
        if (groupIds.Any(memberGroups.Contains)) {
            return true;
        }

        return false;
    }
}

class AzureADGroupMembership : GroupMembershipChecker {
    private readonly ICreds _creds;
    public AzureADGroupMembership(ICreds creds) => _creds = creds;
    protected override async Task<IEnumerable<Guid>> GetGroups(Guid memberId) =>
        await _creds.QueryMicrosoftGraph<List<Guid>>(HttpMethod.Get, $"users/{memberId}/transitiveMemberOf");
}

class StaticGroupMembership : GroupMembershipChecker {
    private readonly Dictionary<Guid, List<Guid>> _memberships;
    public StaticGroupMembership(IDictionary<Guid, Guid[]> memberships) {
        _memberships = memberships.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
    }

    protected override Task<IEnumerable<Guid>> GetGroups(Guid memberId) {
        var result = Enumerable.Empty<Guid>();
        if (_memberships.TryGetValue(memberId, out var found)) {
            result = found;
        }

        return Async.Task.FromResult(result);
    }
}
