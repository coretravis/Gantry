using FluentAssertions;
using Gantry.Cli.Commands;
using Gantry.Core.Exceptions;
using Gantry.Core.Models;
using Gantry.Infrastructure.Plugins;

namespace Gantry.Cli.Tests.Commands;

public class PluginCommandTests
{
    [Fact]
    public void ValidatePluginCompatibility_UnsupportedOs_ThrowsWithRemediation()
    {
        var metadata = PluginRegistry.All["postgres"];
        var config = new DeployConfig
        {
            Server = new ServerConfig { OsVersion = "20.04" }
        };

        var act = () => PluginCommandHandler.ValidatePluginCompatibility(metadata, config);

        act.Should().Throw<GantryException>()
            .Which.Remediation.Should().NotBeNull();
    }

    [Fact]
    public void ValidatePluginCompatibility_SupportedOs_DoesNotThrow()
    {
        var metadata = PluginRegistry.All["postgres"];
        var config = new DeployConfig
        {
            Server = new ServerConfig { OsVersion = "22.04" }
        };

        var act = () => PluginCommandHandler.ValidatePluginCompatibility(metadata, config);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidatePluginCompatibility_WhenOsVersionUnknown_SkipsCheck()
    {
        var metadata = PluginRegistry.All["postgres"];
        var config = new DeployConfig
        {
            Server = new ServerConfig { OsVersion = string.Empty }
        };

        var act = () => PluginCommandHandler.ValidatePluginCompatibility(metadata, config);

        act.Should().NotThrow();
    }
}
