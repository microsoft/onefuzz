namespace Microsoft.OneFuzz.Service;

using System.Security.Cryptography;

public class Auth {
    public static Authentication BuildAuth() {
        var rsa = RSA.Create(2048);
        string header = "-----BEGIN RSA PRIVATE KEY-----";
        string footer = "-----END RSA PRIVATE KEY-----";
        var privateKey = $"{header}\n{Convert.ToBase64String(rsa.ExportRSAPrivateKey())}\n{footer}";
        var publiceKey = $"{header}\n{Convert.ToBase64String(rsa.ExportRSAPublicKey())}\n{footer}";
        return new Authentication(Guid.NewGuid().ToString(), publiceKey, privateKey);
    }
}
