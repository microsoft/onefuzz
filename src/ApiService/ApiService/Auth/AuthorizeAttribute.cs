namespace Microsoft.OneFuzz.Service.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeAttribute : Attribute {
    public AuthorizeAttribute(Allow allow) {
        Allow = allow;
    }

    public Allow Allow { get; set; }
}

public enum Allow {
    Agent,
    User,
    Admin,

}
