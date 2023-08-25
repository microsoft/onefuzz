using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using NSubstitute;
using Xunit;
namespace Tests;

public class TimerReproTests {
    private readonly ILogger<TimerRepro> _log;
    private readonly IOnefuzzContext _mockCtx;
    private readonly IReproOperations _mockReproOperations;

    public TimerReproTests() {

        _mockReproOperations = Substitute.For<IReproOperations>();
        _mockReproOperations.SearchExpired().Returns(AsyncEnumerable.Empty<Repro>());
        _mockReproOperations.SearchStates(VmStateHelper.NeedsWork).Returns(AsyncEnumerable.Empty<Repro>());

        _mockCtx = Substitute.For<IOnefuzzContext>();
        _mockCtx.ReproOperations.Returns(_mockReproOperations);

        _log = Substitute.For<ILogger<TimerRepro>>();
    }

    [Fact]
    public async System.Threading.Tasks.Task NoExpiredRepros() {

        var timerRepro = new TimerRepro(_log, _mockCtx);
        await timerRepro.Run(new TimerInfo());

        _ = await _mockReproOperations.DidNotReceive().Stopping(Arg.Any<Repro>());
    }

    [Fact]
    public async System.Threading.Tasks.Task ExpiredRepro() {
        _mockReproOperations.SearchExpired()
            .Returns(new[] { GenerateRepro() }.ToAsyncEnumerable());

        var timerRepro = new TimerRepro(_log, _mockCtx);
        await timerRepro.Run(new TimerInfo());

        _ = await _mockReproOperations.Received().Stopping(Arg.Any<Repro>());
    }

    [Fact]
    public async System.Threading.Tasks.Task NoNeedsWorkRepros() {
        _mockReproOperations.SearchStates(VmStateHelper.NeedsWork)
            .Returns(AsyncEnumerable.Empty<Repro>());

        var timerRepro = new TimerRepro(_log, _mockCtx);
        await timerRepro.Run(new TimerInfo());

        _ = await _mockReproOperations.DidNotReceive().ProcessStateUpdates(Arg.Any<Repro>(), Arg.Any<int>());
    }

    [Fact]
    public async System.Threading.Tasks.Task DontProcessExpiredVms() {
        var expiredVm = GenerateRepro();
        var notExpiredVm = GenerateRepro();

        _mockReproOperations.SearchExpired()
            .Returns(new[] { expiredVm }.ToAsyncEnumerable());

        _mockReproOperations.SearchStates(VmStateHelper.NeedsWork)
            .Returns(new[] { expiredVm, notExpiredVm }.ToAsyncEnumerable());

        var timerRepro = new TimerRepro(_log, _mockCtx);
        await timerRepro.Run(new TimerInfo());

        _ = await _mockReproOperations.Received().ProcessStateUpdates(Arg.Any<Repro>(), Arg.Any<int>());
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
