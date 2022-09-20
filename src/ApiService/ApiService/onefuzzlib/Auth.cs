﻿namespace Microsoft.OneFuzz.Service;
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
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            ArgumentList = {
                "-t",
                "rsa",
                "-f",
                tempFile,
                "-P",
                "",
                "-b",
                "2048"
            }
        };
        return p;
    }

    // This works both on Windows and Linux azure function hosts
    private static async Async.Task<(string, string)> GenerateKeyValuePair(ILogTracer log) {
        var tmpFile = Path.GetTempFileName();
        try {
            File.Delete(tmpFile);
        } catch (Exception ex) {
            //bad but not worth the failure
            log.Warning($"failed to delete temp file {tmpFile} due to {ex}");
        }
        tmpFile = tmpFile + ".key";
        var startInfo = SshKeyGenProcConfig(tmpFile);
        using var proc = new Process() { StartInfo = startInfo };
        if (proc.Start()) {
            var stdErr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) {
                throw new Exception($"ssh-keygen failed due to {stdErr}");
            }
            var tmpFilePub = tmpFile + ".pub";
            var priv = File.ReadAllText(tmpFile);
            var pub = File.ReadAllText(tmpFilePub);
            try {
                File.Delete(tmpFile);
            } catch (Exception ex) {
                //bad but not worth failing
                log.Warning($"failed to delete temp file {tmpFile} due to {ex}");
            }
            try {
                File.Delete(tmpFilePub);
            } catch (Exception ex) {
                //bad but not worth failing
                log.Warning($"failed to delete temp file {tmpFilePub} due to {ex}");
            }
            return (priv, pub.Trim());
        } else {
            throw new Exception("failed to start new ssh-keygen");
        }
    }


    public static async Async.Task<Authentication> BuildAuth(ILogTracer log) {
        var (priv, pub) = await GenerateKeyValuePair(log);
        return new Authentication(
            Password: Guid.NewGuid().ToString(),
            PublicKey: pub,
            PrivateKey: priv);
    }
}
