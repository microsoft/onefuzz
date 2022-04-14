using System.Linq;
using System.Collections.Generic;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class NotificationOperations: Orm<Notification> {
    public NotificationOperations(IStorage storage)
        :base(storage)
    {
    }
    public void NewFiles(Container container, string filename, bool failTaskOnTransientError)
    {
        var notifications = GetNotifications(container);
    }

    public IAsyncEnumerable<Notification> GetNotifications(Container container)
    {
        return QueryAsync(filter: $"container eq '{container.ContainerName}'");
    }
}
