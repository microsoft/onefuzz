using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Core.Serialization;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public sealed class CompatibilityTests {

    public CompatibilityTests(ITestOutputHelper output) {
        // reuse the ORM arbitrary instances
        Arb.Register<OrmArb>();
    }

    private static readonly JsonObjectSerializer _serializer = new(EntityConverter.GetJsonSerializerOptions());

    private static void Test<T>(T value, string pythonType) where T : notnull {
        var json = _serializer.Serialize(value);

        var escapedJson = json.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"");
        var startInfo = new ProcessStartInfo {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = {
                "-c",
                $"from onefuzztypes.models import Job; import json; from onefuzz.backend import serialize; print(json.dumps(serialize({pythonType}.parse_obj(json.loads(\"{escapedJson}\")))))",
            }
        };

        var proc = Process.Start(startInfo) ?? throw new InvalidOperationException("unable to start python process");

        var (stdout, stderr) = (
            proc.StandardOutput.ReadToEndAsync(),
            proc.StandardError.ReadToEndAsync()).GetAwaiter().GetResult();

        proc.WaitForExit();
        Assert.Equal("", stderr);
        Assert.Equal(0, proc.ExitCode);

        var fromPython = _serializer.Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(stdout)), typeof(T), default);
        var rejson = _serializer.Serialize(fromPython);
        Assert.Equal(json.ToString(), rejson.ToString());
    }

    [Property(MaxTest = 10)]
    public void CanRoundTrip(JobResponse value) => Test(value, "Job");
}
