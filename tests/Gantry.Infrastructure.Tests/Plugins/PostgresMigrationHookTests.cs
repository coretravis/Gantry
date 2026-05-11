using FluentAssertions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Infrastructure.Plugins.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Infrastructure.Tests.Plugins;

public class PostgresMigrationHookTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly PostgresMigrationHook _sut = new(NullLogger<PostgresMigrationHook>.Instance);

    [Fact]
    public void PostgresMigrationHook_ImplementsIPreDeployHook()
    {
        _sut.Should().BeAssignableTo<IPreDeployHook>();
    }

    [Fact]
    public async Task PostgresMigrationHook_WhenMigrationsNotConfigured_DoesNothing()
    {
        var config = new DeployConfig
        {
            App = new AppConfig { Name = "test-app" },
            Plugins = new Dictionary<string, Dictionary<string, string>>
            {
                ["postgres"] = new() // run_migrations not set, defaults to false
            }
        };

        await _sut.RunAsync(_ssh.Object, config);

        _ssh.Verify(
            s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
