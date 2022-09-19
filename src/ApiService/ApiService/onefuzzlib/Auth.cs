namespace Microsoft.OneFuzz.Service;
using System.Diagnostics;
using System.IO;

public class Auth {

    private static ProcessStartInfo SshKeyGenProcConfig(string tempFile) {

        string keyGen = "ssh-keygen";
        var winAzureFunctionPath = "C:\\Program Files\\Git\\usr\\bin\\ssh-keygen.exe";
        if (File.Exists(winAzureFunctionPath)) {
            keyGen = winAzureFunctionPath;
        }
        var p = new ProcessStartInfo() {
            FileName = keyGen,
            CreateNoWindow = false,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        p.ArgumentList.Add("-t");
        p.ArgumentList.Add("rsa");
        p.ArgumentList.Add("-f");
        p.ArgumentList.Add(tempFile);
        p.ArgumentList.Add("-P");
        p.ArgumentList.Add("");
        p.ArgumentList.Add("-b");
        p.ArgumentList.Add("2048");
        return p;
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
