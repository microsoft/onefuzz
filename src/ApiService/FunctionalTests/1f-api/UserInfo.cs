using System.Text.Json;

namespace FunctionalTests;

public class UserInfo : IFromJsonElement<UserInfo> {

    JsonElement _e;
    public UserInfo() { }
    public UserInfo(JsonElement e) => _e = e;
    public UserInfo Convert(JsonElement e) => new UserInfo(e);

    public Guid? ApplicationId => _e.GetNullableGuidProperty("application_id");
    public Guid? ObjectId => _e.GetNullableGuidProperty("object_id");
    public string? Upn => _e.GetNullableStringProperty("upn");
}

