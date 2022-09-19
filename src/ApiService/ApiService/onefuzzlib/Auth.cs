namespace Microsoft.OneFuzz.Service;
using System.Diagnostics;
using System.IO;

public class Auth {

    private static ProcessStartInfo SshKeyGenProcConfig(string tempFile) {
        return new ProcessStartInfo() {
            FileName = "ssh-keygen",
            Arguments = $"-t rsa -f {tempFile} -N '\"\"' -b 2048",
            CreateNoWindow = false,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    // This works both on Windows and Linux azure function hosts
    private static async Async.Task<(string, string)> GenerateKeyValuePair() {
        var tmpFile = Path.GetTempFileName();
        File.Delete(tmpFile);
        tmpFile = tmpFile + ".key";
        var startInfo = SshKeyGenProcConfig(tmpFile);
        var proc = new Process() { StartInfo = startInfo };
        if (proc is null) {
            throw new Exception($"ssh-keygen process could start (got null)");
        }
        if (proc.Start()) {
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) {
                throw new Exception($"ssh-keygen failed due to {await proc.StandardError.ReadToEndAsync()}");
            }

            var priv = File.ReadAllText(tmpFile);
            var pub = File.ReadAllText(tmpFile + ".pub");
            File.Delete(tmpFile);
            return (priv, pub.Trim());
        } else {
            throw new Exception("failed to start new ssh-keygen");
        }
    }


    public static async Async.Task<Authentication> BuildAuth() {
        var (priv, pub) = await GenerateKeyValuePair();
        return new Authentication(
            Password: Guid.NewGuid().ToString(),
            PublicKey: pub,
            PrivateKey: priv);
    }
}
