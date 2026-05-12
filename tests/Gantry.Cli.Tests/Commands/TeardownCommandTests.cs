using FluentAssertions;
using Gantry.Cli.Commands;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace Gantry.Cli.Tests.Commands;

public class TeardownCommandTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly Mock<IConfigService> _configService = new();
    private readonly CapturingLogger<TeardownCommandHandler> _logger = new();

    public TeardownCommandTests()
    {
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4", DeployKeyPath = "~/.ssh/gantry_deploy_ed25519" },
            App = new AppConfig { Name = "test-app" }
        });
    }

    private TeardownCommandHandler CreateSut(IEnumerable<IPhase>? phases = null) =>
        new(_ssh.Object, _configService.Object, phases ?? [], _logger, _ => true);

    private static Mock<IPhase> BuildPhase(string name, int order)
    {
        var mock = new Mock<IPhase>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.Order).Returns(order);
        mock.Setup(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public async Task Teardown_CallsRollbackInReversePhaseOrder()
    {
        var callOrder = new List<string>();

        var phaseA = BuildPhase("phase-a", 10);
        var phaseB = BuildPhase("phase-b", 20);
        var phaseC = BuildPhase("phase-c", 30);

        foreach (var (mock, name) in new[] { (phaseA, "phase-a"), (phaseB, "phase-b"), (phaseC, "phase-c") })
        {
            var captured = name;
            mock.Setup(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add(captured))
                .Returns(Task.CompletedTask);
        }

        var sut = CreateSut([phaseA.Object, phaseB.Object, phaseC.Object]);

        await sut.ExecuteAsync(".deploy.yml", dryRun: false);

        callOrder.Should().Equal("phase-c", "phase-b", "phase-a");
    }

    [Fact]
    public async Task Teardown_SkipsMetaPhases()
    {
        var ciGeneration = BuildPhase("ci-generation", 90);
        var configPersistence = BuildPhase("config-persistence", 95);
        var connectAndVerify = BuildPhase("connect-and-verify", 10);
        var normalPhase = BuildPhase("os-hardening", 20);

        var sut = CreateSut([ciGeneration.Object, configPersistence.Object, connectAndVerify.Object, normalPhase.Object]);

        await sut.ExecuteAsync(".deploy.yml", dryRun: false);

        ciGeneration.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
        configPersistence.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
        connectAndVerify.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
        normalPhase.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Teardown_WhenPhaseRollbackFails_ContinuesToNextPhase()
    {
        var phaseA = BuildPhase("phase-a", 10);
        var phaseB = BuildPhase("phase-b", 20);

        phaseB.Setup(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("rollback failed"));

        var sut = CreateSut([phaseA.Object, phaseB.Object]);

        var result = await sut.ExecuteAsync(".deploy.yml", dryRun: false);

        result.Should().Be(0);
        phaseA.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Teardown_DryRun_DoesNotExecuteRollback()
    {
        var phaseA = BuildPhase("phase-a", 10);
        var phaseB = BuildPhase("phase-b", 20);

        var sut = CreateSut([phaseA.Object, phaseB.Object]);

        var result = await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        result.Should().Be(0);
        phaseA.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
        phaseB.Verify(p => p.RollbackAsync(It.IsAny<ProvisioningContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Teardown_ShowsManualStepReminders()
    {
        var sut = CreateSut();

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        _logger.Messages.Should().Contain(m =>
            m.Contains("Manual steps remaining") &&
            m.Contains("DO_HOST") &&
            m.Contains("DO_SSH_KEY"));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
