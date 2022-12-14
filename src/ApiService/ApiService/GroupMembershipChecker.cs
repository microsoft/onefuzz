using System.Threading.Tasks;
using Microsoft.Graph;

namespace Microsoft.OneFuzz.Service;

abstract class GroupMembershipChecker {
    protected abstract IAsyncEnumerable<Guid> GetGroups(Guid memberId);

    public async ValueTask<bool> IsMember(IEnumerable<Guid> groupIds, Guid memberId) {
        if (groupIds.Contains(memberId)) {
            return true;
        }

        return await GetGroups(memberId).AnyAsync(memberGroup => groupIds.Contains(memberGroup));
    }
}

sealed class AzureADGroupMembership : GroupMembershipChecker {
    private readonly GraphServiceClient _graphClient;
    public AzureADGroupMembership(GraphServiceClient graphClient) => _graphClient = graphClient;
    protected override async IAsyncEnumerable<Guid> GetGroups(Guid memberId) {
        var page = await _graphClient.Users[memberId.ToString()].TransitiveMemberOf.Request().GetAsync();
        while (page is not null) {
            foreach (var obj in page) {
                yield return Guid.Parse(obj.Id);
            }

            page = await page.NextPageRequest.GetAsync();
        }
    }
}

sealed class StaticGroupMembership : GroupMembershipChecker {
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> _memberships;
    public StaticGroupMembership(IDictionary<Guid, Guid[]> memberships) {
        _memberships = memberships.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<Guid>)kvp.Value.ToList());
    }

    protected override IAsyncEnumerable<Guid> GetGroups(Guid memberId) {
        if (_memberships.TryGetValue(memberId, out var groups)) {
            return groups.ToAsyncEnumerable();
        }

        return AsyncEnumerable.Empty<Guid>();
    }
}
