using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Moq;
using Xunit;
namespace Tests;

public class TimerReproTests {
    private readonly ILogger<TimerRepro> _log;
    private readonly Mock<IOnefuzzContext> _mockCtx;
    private readonly Mock<IReproOperations> _mockReproOperations;

    public TimerReproTests() {
        _mockCtx = new Mock<IOnefuzzContext>();

        _mockReproOperations = new Mock<IReproOperations>();

        _mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(AsyncEnumerable.Empty<Repro>());
        _mockReproOperations.Setup(x => x.SearchStates(VmStateHelper.NeedsWork))
            .Returns(AsyncEnumerable.Empty<Repro>());

        _log = new Mock<ILogger<TimerRepro>>().Object;
    }

    [Fact]
    public async System.Threading.Tasks.Task NoExpiredRepros() {
        _mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(AsyncEnumerable.Empty<Repro>());

        _mockCtx.Setup(x => x.ReproOperations)
            .Returns(_mockReproOperations.Object);

        var timerRepro = new TimerRepro(_log, _mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        _mockReproOperations.Verify(x => x.Stopping(It.IsAny<Repro>()), Times.Never());
    }

    [Fact]
    public async System.Threading.Tasks.Task ExpiredRepro() {
        _mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(new List<Repro> {
                GenerateRepro()
            }.ToAsyncEnumerable());

        _mockCtx.Setup(x => x.ReproOperations)
            .Returns(_mockReproOperations.Object);

        var timerRepro = new TimerRepro(_log, _mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        _mockReproOperations.Verify(x => x.Stopping(It.IsAny<Repro>()), Times.Once());
    }

    [Fact]
    public async System.Threading.Tasks.Task NoNeedsWorkRepros() {
        _mockReproOperations.Setup(x => x.SearchStates(VmStateHelper.NeedsWork))
            .Returns(AsyncEnumerable.Empty<Repro>());

        _mockCtx.Setup(x => x.ReproOperations)
            .Returns(_mockReproOperations.Object);

        var timerRepro = new TimerRepro(_log, _mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        _mockReproOperations.Verify(x => x.ProcessStateUpdates(It.IsAny<Repro>(), It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public async System.Threading.Tasks.Task DontProcessExpiredVms() {
        var expiredVm = GenerateRepro();
        var notExpiredVm = GenerateRepro();

        _mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(new List<Repro> {
                expiredVm
            }.ToAsyncEnumerable());

        _mockReproOperations.Setup(x => x.SearchStates(VmStateHelper.NeedsWork))
            .Returns(new List<Repro> {
                expiredVm,
                notExpiredVm
            }.ToAsyncEnumerable());

        _mockCtx.Setup(x => x.ReproOperations)
            .Returns(_mockReproOperations.Object);

        var timerRepro = new TimerRepro(_log, _mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        _mockReproOperations.Verify(x => x.ProcessStateUpdates(It.IsAny<Repro>(), It.IsAny<int>()), Times.Once());
    }

    private static Repro GenerateRepro() {
        return new Repro(
            Guid.NewGuid(),
            Guid.Empty,
            new ReproConfig(
                Container.Parse("container"),
                "",
                0),
            new SecretValue<Authentication>(new Authentication("", "", "")),
            Os.Linux,
            VmState.Init,
            null,
            null,
            null,
            null);
    }
}
