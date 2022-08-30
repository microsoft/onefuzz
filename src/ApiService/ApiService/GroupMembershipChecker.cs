using System.Threading.Tasks;
using Microsoft.Graph;

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
    private readonly GraphServiceClient _graphClient;
    public AzureADGroupMembership(GraphServiceClient graphClient) => _graphClient = graphClient;
    protected override async Task<IEnumerable<Guid>> GetGroups(Guid memberId)
    {
        var result = new List<DirectoryObject>();
        var page = await _graphClient.Users[memberId.ToString()].TransitiveMemberOf.Request().GetAsync();
        while (page is not null) {
            result.AddRange(page);
            page = await page.NextPageRequest.GetAsync();
        }

        return result.Select(x => Guid.Parse(x.Id));
    }
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
