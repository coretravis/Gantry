using FluentAssertions;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Core.Tests.Orchestration;

public class PhaseOrchestratorTests
{
    private static ProvisioningContext BuildContext() => new()
    {
        Config = new DeployConfig(),
        IsDryRun = true
    };

    private static Mock<IPhase> BuildPhase(string name, int order, bool throws = false)
    {
        var mock = new Mock<IPhase>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.Description).Returns($"Phase {name}");
        mock.Setup(p => p.Order).Returns(order);
        mock.Setup(p => p.IsRequired).Returns(true);

        if (throws)
            mock.Setup(p => p.ExecuteAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PhaseException(name, "Simulated failure"));
        else
            mock.Setup(p => p.ExecuteAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
                .Callback<ProvisioningContext, CancellationToken>((ctx, _) => ctx.CompletedPhases.Add(name))
                .Returns(Task.CompletedTask);

        mock.Setup(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    [Fact]
    public async Task RunAsync_AllPhasesSucceed_ExecutesInOrder()
    {
        var phase1 = BuildPhase("phase-1", 1);
        var phase2 = BuildPhase("phase-2", 2);
        var phase3 = BuildPhase("phase-3", 3);

        var sut = new PhaseOrchestrator(
            [phase3.Object, phase1.Object, phase2.Object],
            NullLogger<PhaseOrchestrator>.Instance);

        var context = BuildContext();
        await sut.RunAsync(context);

        context.CompletedPhases.Should().Equal("phase-1", "phase-2", "phase-3");
    }

    [Fact]
    public async Task RunAsync_PhaseFailure_RollsBackCompletedPhases()
    {
        var phase1 = BuildPhase("phase-1", 1);
        var phase2 = BuildPhase("phase-2", 2, throws: true);
        var phase3 = BuildPhase("phase-3", 3);

        var sut = new PhaseOrchestrator(
            [phase1.Object, phase2.Object, phase3.Object],
            NullLogger<PhaseOrchestrator>.Instance);

        var context = BuildContext();
        await Assert.ThrowsAsync<PhaseException>(() => sut.RunAsync(context));

        phase1.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Once);
        phase3.Verify(p => p.ExecuteAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SkippedPhases_AreNotExecuted()
    {
        var phase1 = BuildPhase("phase-1", 1);
        var phase2 = BuildPhase("phase-2", 2);

        var sut = new PhaseOrchestrator(
            [phase1.Object, phase2.Object],
            NullLogger<PhaseOrchestrator>.Instance);

        var context = BuildContext();
        await sut.RunAsync(context, ["phase-2"]);

        phase1.Verify(p => p.ExecuteAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Once);
        phase2.Verify(p => p.ExecuteAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_RollbackPhaseThrows_ContinuesRollingBack()
    {
        var phase1 = BuildPhase("phase-1", 1);
        phase1.Setup(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Rollback failed"));

        var phase2 = BuildPhase("phase-2", 2, throws: true);

        var sut = new PhaseOrchestrator(
            [phase1.Object, phase2.Object],
            NullLogger<PhaseOrchestrator>.Instance);

        var context = BuildContext();
        var act = () => sut.RunAsync(context);

        await act.Should().ThrowAsync<PhaseException>();
        phase1.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_EmptyPhaseList_CompletesSuccessfully()
    {
        var sut = new PhaseOrchestrator([], NullLogger<PhaseOrchestrator>.Instance);
        var context = BuildContext();
        await sut.RunAsync(context);
        context.CompletedPhases.Should().BeEmpty();
    }
}
