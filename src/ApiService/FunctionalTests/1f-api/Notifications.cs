using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;

public class Notification : IFromJsonElement<Notification> {
    readonly JsonElement _e;

    public Notification(JsonElement e) => _e = e;

    public static Notification Convert(JsonElement e) => new(e);

    public Guid NotificationId => _e.GetGuidProperty("notification_id");

    public string Container => _e.GetStringProperty("container");

    public string Config => _e.GetRawTextProperty("config");
}

public class NotificationsApi : ApiBase {
    public NotificationsApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Notifications", request, output) {
    }

    public async Task<Result<IEnumerable<Notification>, Error>> Get(List<string>? containers = null) {
        var n = new JsonObject()
            .AddIfNotNullEnumerableV("container", containers);

        var r = await Get(n);
        return IEnumerableResult<Notification>(r);
    }


    public async Task<Result<Notification, Error>> Post(string container, bool replaceExisting, string config) {
        var n = new JsonObject()
            .AddV("container", container)
            .AddV("replace_existing", replaceExisting)
            .AddV("config", config);

        var r = await Post(n);
        return Result<Notification>(r);
    }


    public async Task<Result<Notification, Error>> Delete(Guid notificationId) {
        var n = new JsonObject()
            .AddV("notification_id", notificationId);

        var r = await Delete(n);
        return Result<Notification>(r);
    }
}
