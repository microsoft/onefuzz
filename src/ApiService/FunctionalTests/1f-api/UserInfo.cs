using System.Text.Json;

namespace FunctionalTests;

public class UserInfo : IFromJsonElement<UserInfo> {

    readonly JsonElement _e;
    public UserInfo(JsonElement e) => _e = e;
    public static UserInfo Convert(JsonElement e) => new(e);

    public Guid? ApplicationId => _e.GetNullableGuidProperty("application_id");
    public Guid? ObjectId => _e.GetNullableGuidProperty("object_id");
    public string? Upn => _e.GetNullableStringProperty("upn");
}
