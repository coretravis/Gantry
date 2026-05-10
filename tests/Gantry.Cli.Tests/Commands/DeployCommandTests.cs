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
    private readonly CapturingLogger<DeployCommandHandler> _logger = new();

    public DeployCommandTests()
    {
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app", ProjectPath = "test-app.csproj" }
        });
    }

    private DeployCommandHandler CreateSut(Func<CancellationToken, Task<string>> getSha) =>
        new(_ssh.Object, _processManager.Object, _configService.Object,
            Enumerable.Empty<IDeployHook>(), _logger, getSha);

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
