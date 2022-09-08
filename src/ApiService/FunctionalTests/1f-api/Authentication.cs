using System.Text.Json;

namespace FunctionalTests;

public class Authentication : IFromJsonElement<Authentication> {
    JsonElement _e;

    public Authentication() { }
    public Authentication(JsonElement e) => _e = e;

    public string Password => _e.GetStringProperty("password");

    public string PublicKey => _e.GetStringProperty("public_key");
    public string PrivateKey => _e.GetStringProperty("private_key");

    public Authentication Convert(JsonElement e) => new Authentication(e);

}
