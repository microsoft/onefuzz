namespace Microsoft.OneFuzz.Service;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

public class Auth {

    private static ReadOnlySpan<byte> SSHRSABytes => new byte[] { (byte)'s', (byte)'s', (byte)'h', (byte)'-', (byte)'r', (byte)'s', (byte)'a' };

    private static byte[] BuildPublicKey(RSA rsa) {
        static Span<byte> WriteLengthPrefixedBytes(ReadOnlySpan<byte> src, Span<byte> dest) {
            BinaryPrimitives.WriteInt32BigEndian(dest, src.Length);
            dest = dest[sizeof(int)..];
            src.CopyTo(dest);
            return dest[src.Length..];
        }

        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        // public key format is "ssh-rsa", exponent, modulus, all written
        // as (big-endian) length-prefixed bytes
        var result = new byte[sizeof(int) + SSHRSABytes.Length + sizeof(int) + parameters.Modulus!.Length + sizeof(int) + parameters.Exponent!.Length];
        var spanResult = result.AsSpan();
        spanResult = WriteLengthPrefixedBytes(SSHRSABytes, spanResult);
        spanResult = WriteLengthPrefixedBytes(parameters.Exponent, spanResult);
        spanResult = WriteLengthPrefixedBytes(parameters.Modulus, spanResult);
        Debug.Assert(spanResult.Length == 0);

        return result;
    }
    public static Authentication BuildAuth() {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportRSAPrivateKey();
        var formattedPrivateKey = $"-----BEGIN RSA PRIVATE KEY-----\n{Convert.ToBase64String(privateKey)}\n-----END RSA PRIVATE KEY-----\n";

        var publicKey = BuildPublicKey(rsa);
        var formattedPublicKey = $"ssh-rsa {Convert.ToBase64String(publicKey)} onefuzz-generated-key";

        return new Authentication(
            Password: Guid.NewGuid().ToString(),
            PublicKey: formattedPublicKey,
            PrivateKey: formattedPrivateKey);
    }
}
