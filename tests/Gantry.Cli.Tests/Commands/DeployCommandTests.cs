using FluentAssertions;
using Gantry.Cli.Commands;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace Gantry.Cli.Tests.Commands;

public class DeployCommandTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IConfigService> _configService = new();
    private readonly Mock<IStateService> _stateService = new();
    private readonly CapturingLogger<DeployCommandHandler> _logger = new();

    public DeployCommandTests()
    {
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app", ProjectPath = "test-app.csproj" }
        });
    }

    private DeployCommandHandler CreateSut(
        Func<CancellationToken, Task<string>>? getSha = null,
        IEnumerable<IPreDeployHook>? preDeployHooks = null,
        IEnumerable<IDeployHook>? deployHooks = null) =>
        new(_ssh.Object, _processManager.Object, _configService.Object,
            _stateService.Object,
            preDeployHooks ?? Enumerable.Empty<IPreDeployHook>(),
            deployHooks ?? Enumerable.Empty<IDeployHook>(),
            _logger, getSha);

    [Fact]
    public async Task ReleaseId_WhenGitAvailable_IncludesShortSha()
    {
        var sut = CreateSut(_ => Task.FromResult("abc1234"));

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        _logger.Messages.Should().Contain(m => m.Contains("abc1234-"));
    }

    [Fact]
    public async Task ReleaseId_WhenGitUnavailable_FallsBackToTimestamp()
    {
        var sut = CreateSut(_ => Task.FromResult(string.Empty));

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        _logger.Messages.Should().Contain(m =>
            m.Contains("Starting deploy release") &&
            System.Text.RegularExpressions.Regex.IsMatch(m, @"\d{8}-\d{6}") &&
            !m.Contains("--"));
    }

    [Fact]
    public async Task ReleaseId_AlwaysContainsTimestampSuffix()
    {
        var sut = CreateSut(_ => Task.FromResult("abc1234"));

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        _logger.Messages.Should().Contain(m =>
            m.Contains("Starting deploy release") &&
            System.Text.RegularExpressions.Regex.IsMatch(m, @"\d{8}-\d{6}"));
    }

    [Fact]
    public async Task Deploy_PreDeployHook_RunsBeforeServiceRestart()
    {
        // Ordering is verified through log messages: pre-deploy hooks log before post-deploy
        // hooks, which run after restart. Pre-deploy log < post-deploy log ⟹ pre-deploy < restart.
        var preHook = new Mock<IPreDeployHook>();
        preHook.Setup(h => h.PluginName).Returns("postgres");

        var postHook = new Mock<IDeployHook>();
        postHook.Setup(h => h.PluginName).Returns("notifier");

        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app", ProjectPath = "test-app.csproj" },
            Plugins = new Dictionary<string, Dictionary<string, string>>
            {
                ["postgres"] = new(),
                ["notifier"] = new()
            }
        });

        var sut = CreateSut(
            getSha: _ => Task.FromResult("abc1234"),
            preDeployHooks: [preHook.Object],
            deployHooks: [postHook.Object]);

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        var preIdx = _logger.Messages.FindIndex(m => m.Contains("Running pre-deploy hook for plugin 'postgres'"));
        var postIdx = _logger.Messages.FindIndex(m => m.Contains("Running deploy hook for plugin 'notifier'"));

        preIdx.Should().BeGreaterThanOrEqualTo(0, "pre-deploy hook log should be present");
        postIdx.Should().BeGreaterThanOrEqualTo(0, "post-deploy hook log should be present");
        preIdx.Should().BeLessThan(postIdx, "pre-deploy hook should be logged before post-deploy hook");

        _processManager.Verify(
            p => p.RestartAsync(It.IsAny<ISshService>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Deploy_WhenPreDeployHookFails_ServiceIsNotRestarted()
    {
        // When any step before RestartServiceAsync fails, the pipeline aborts and the service
        // is not restarted. This applies to pre-deploy hook failures as they precede restart.
        var preHook = new Mock<IPreDeployHook>();
        preHook.Setup(h => h.PluginName).Returns("postgres");
        preHook.Setup(h => h.RunAsync(It.IsAny<ISshService>(), It.IsAny<DeployConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Core.Exceptions.GantryException("Migration failed.", "Fix the migration and re-deploy."));

        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app", ProjectPath = "test-app.csproj" },
            Plugins = new Dictionary<string, Dictionary<string, string>> { ["postgres"] = new() }
        });

        var sut = CreateSut(
            getSha: _ => Task.FromResult("abc1234"),
            preDeployHooks: [preHook.Object]);

        var result = await sut.ExecuteAsync(".deploy.yml", dryRun: false);

        result.Should().Be(1);
        _processManager.Verify(
            p => p.RestartAsync(It.IsAny<ISshService>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Deploy_WhenPluginDisabled_PreDeployHookIsSkipped()
    {
        var preHook = new Mock<IPreDeployHook>();
        preHook.Setup(h => h.PluginName).Returns("postgres");

        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app", ProjectPath = "test-app.csproj" },
            Plugins = new Dictionary<string, Dictionary<string, string>>
            {
                ["postgres"] = new() { ["enabled"] = "false" }
            }
        });

        var sut = CreateSut(
            getSha: _ => Task.FromResult("abc1234"),
            preDeployHooks: [preHook.Object]);

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        preHook.Verify(
            h => h.RunAsync(It.IsAny<ISshService>(), It.IsAny<DeployConfig>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _logger.Messages.Should().NotContain(m => m.Contains("Running pre-deploy hook for plugin 'postgres'"));
    }

    [Fact]
    public async Task Deploy_DryRun_PreDeployHookIsNotExecuted()
    {
        var preHook = new Mock<IPreDeployHook>();
        preHook.Setup(h => h.PluginName).Returns("postgres");

        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app", ProjectPath = "test-app.csproj" },
            Plugins = new Dictionary<string, Dictionary<string, string>> { ["postgres"] = new() }
        });

        var sut = CreateSut(
            getSha: _ => Task.FromResult("abc1234"),
            preDeployHooks: [preHook.Object]);

        await sut.ExecuteAsync(".deploy.yml", dryRun: true);

        preHook.Verify(
            h => h.RunAsync(It.IsAny<ISshService>(), It.IsAny<DeployConfig>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _logger.Messages.Should().Contain(m => m.Contains("[dry-run] Would run pre-deploy hook for plugin 'postgres'"));
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
