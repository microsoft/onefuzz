using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service;
using Moq;
using Xunit;

namespace Tests;

public class TimerReproTests {
    private ILogTracer log;
    private Mock<IOnefuzzContext> mockCtx;
    private Mock<IReproOperations> mockReproOperations;

    public TimerReproTests() {
        mockCtx = new Mock<IOnefuzzContext>();

        mockReproOperations = new Mock<IReproOperations>();

        mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(AsyncEnumerable.Empty<Repro>());
        mockReproOperations.Setup(x => x.SearchStates(VmStateHelper.NeedsWork))
            .Returns(AsyncEnumerable.Empty<Repro>());

        log = new Mock<ILogTracer>().Object;
    }

    [Fact]
    public async System.Threading.Tasks.Task NoExpiredRepros() {
        mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(AsyncEnumerable.Empty<Repro>());

        mockCtx.Setup(x => x.ReproOperations)
            .Returns(mockReproOperations.Object);

        var timerRepro = new TimerRepro(log, mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        mockReproOperations.Verify(x => x.Stopping(It.IsAny<Repro>()), Times.Never());
    }

    [Fact]
    public async System.Threading.Tasks.Task ExpiredRepro() {
        mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(new List<Repro> {
                GenerateRepro()
            }.ToAsyncEnumerable());

        mockCtx.Setup(x => x.ReproOperations)
            .Returns(mockReproOperations.Object);

        var timerRepro = new TimerRepro(log, mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        mockReproOperations.Verify(x => x.Stopping(It.IsAny<Repro>()), Times.Once());
    }

    [Fact]
    public async System.Threading.Tasks.Task NoNeedsWorkRepros() {
        mockReproOperations.Setup(x => x.SearchStates(VmStateHelper.NeedsWork))
            .Returns(AsyncEnumerable.Empty<Repro>());

        mockCtx.Setup(x => x.ReproOperations)
            .Returns(mockReproOperations.Object);

        var timerRepro = new TimerRepro(log, mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        mockReproOperations.Verify(x => x.ProcessStateUpdates(It.IsAny<Repro>(), It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public async System.Threading.Tasks.Task DontProcessExpiredVms() {
        var expiredVm = GenerateRepro();
        var notExpiredVm = GenerateRepro();

        mockReproOperations.Setup(x => x.SearchExpired())
            .Returns(new List<Repro> {
                expiredVm
            }.ToAsyncEnumerable());

        mockReproOperations.Setup(x => x.SearchStates(VmStateHelper.NeedsWork))
            .Returns(new List<Repro> {
                expiredVm,
                notExpiredVm
            }.ToAsyncEnumerable());

        mockCtx.Setup(x => x.ReproOperations)
            .Returns(mockReproOperations.Object);

        var timerRepro = new TimerRepro(log, mockCtx.Object);
        await timerRepro.Run(new TimerInfo());

        mockReproOperations.Verify(x => x.ProcessStateUpdates(It.IsAny<Repro>(), It.IsAny<int>()), Times.Once());
    }

    private static Repro GenerateRepro() {
        return new Repro(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.Empty,
            new ReproConfig(
                new Container(String.Empty),
                String.Empty,
                0
            ),
            VmState.Init,
            null,
            Os.Linux,
            null,
            null,
            null,
            null);
    }
}
