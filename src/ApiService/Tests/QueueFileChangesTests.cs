using Microsoft.OneFuzz.Service;
using Moq;
using Xunit;

namespace Tests;

public class QueueFileChangesTests {
    private ILogTracer log;
    private IStorage storage;
    private IOnefuzzContext ctx;
    private Mock<INotificationOperations> notificationOps;

    public QueueFileChangesTests() {
        var mockCtx = new Mock<IOnefuzzContext>();
     
        var mockStorage = new Mock<IStorage>();
        mockStorage.Setup(x => x.CorpusAccounts())
            .Returns(new [] {"test"});

        notificationOps = new Mock<INotificationOperations>();
        mockCtx.Setup(x => x.NotificationOperations)
            .Returns(notificationOps.Object);

        log = new Mock<ILogTracer>().Object;
        storage = mockStorage.Object;
        ctx = mockCtx.Object;
    }

    [Fact]
    public async System.Threading.Tasks.Task NotifiesNewFiles()
    {
        var qfc = new QueueFileChanges(log, storage, ctx);

        await qfc.Run(validMessage, 0);

        notificationOps.Verify(x => x.NewFiles(new Container("container-name"), "file-name", false), Times.Once());
    }

    [Fact]
    public async System.Threading.Tasks.Task IgnoresInvalidEventTypes()
    {
        var qfc = new QueueFileChanges(log, storage, ctx);

        await qfc.Run(invalidEventType, 0);

        notificationOps.Verify(x => x.NewFiles(It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public async System.Threading.Tasks.Task IgnoresInvalidTopic()
    {
        var qfc = new QueueFileChanges(log, storage, ctx);

        await qfc.Run(invalidTopic, 0);

        notificationOps.Verify(x => x.NewFiles(It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
    }

    private const string validMessage = @"
    {
        ""eventType"": ""Microsoft.Storage.BlobCreated"",
        ""topic"": ""test"",
        ""data"": {
            ""url"": ""https://fuzzstorage.example.com/container-name/file-name""
        }
    }    
";
    private const string invalidEventType = @"
    {
        ""eventType"": ""invalid"",
        ""topic"": ""test"",
        ""data"": {
            ""url"": ""https://fuzzstorage.example.com/container-name/file-name""
        }
    }    
";
    private const string invalidTopic = @"
    {
        ""eventType"": ""Microsoft.Storage.BlobCreated"",
        ""topic"": ""invalid"",
        ""data"": {
            ""url"": ""https://fuzzstorage.example.com/container-name/file-name""
        }
    }    
";
}
