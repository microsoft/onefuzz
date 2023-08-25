using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageToolsTest : ToolsTestBase {
    public AzureStorageToolsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteToolsTest : ToolsTestBase {
    public AzuriteToolsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class ToolsTestBase : FunctionTestBase {
    private readonly IStorage _storage;

    public ToolsTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) {
        _storage = storage;
    }

    [Fact]
    public async Async.Task CanDownload() {

        const int NUMBER_OF_FILES = 20;

        var toolsContainerClient = GetContainerClient(WellKnownContainers.Tools);
        _ = await toolsContainerClient.CreateIfNotExistsAsync();

        // generate random content 
        var files = Enumerable.Range(0, NUMBER_OF_FILES).Select((x, i) => (path: i, content: Guid.NewGuid())).ToList();

        // upload each files
        foreach (var (path, content) in files) {
            var r = await toolsContainerClient.UploadBlobAsync(path.ToString(), BinaryData.FromString(content.ToString()));
            Assert.False(r.GetRawResponse().IsError);
        }
        var func = new Tools(Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", ""));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        using var zipArchive = new ZipArchive(result.Body);
        foreach (var entry in zipArchive.Entries) {
            Assert.True(int.TryParse(entry.Name, out var index));
            Assert.True(index >= 0 && index < files.Count);
            using var entryStream = entry.Open();
            using var sr = new StreamReader(entryStream);
            var actualContent = sr.ReadToEnd();
            Assert.Equal(files[index].content.ToString(), actualContent);
        }
    }
}
