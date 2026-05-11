using FluentAssertions;
using Gantry.Infrastructure.Plugins;

namespace Gantry.Infrastructure.Tests.Plugins;

public class PluginRegistryTests
{
    [Fact]
    public void AllPlugins_HaveRequiredPhasesDeclared()
    {
        foreach (var (_, meta) in PluginRegistry.All)
            meta.RequiredPhases.Should().NotBeEmpty(
                because: $"plugin '{meta.Name}' must declare at least one required phase");
    }

    [Fact]
    public void AllPlugins_HaveSupportedOsVersionsDeclared()
    {
        foreach (var (_, meta) in PluginRegistry.All)
            meta.SupportedOsVersions.Should().NotBeEmpty(
                because: $"plugin '{meta.Name}' must declare at least one supported OS version");
    }

    [Fact]
    public void PostgresPlugin_RequiresExpectedPhases()
    {
        var meta = PluginRegistry.All["postgres"];

        meta.RequiredPhases.Should().BeEquivalentTo(
            new[] { "os-hardening", "runtime-installation", "process-manager-setup" });
    }

    [Fact]
    public void PostgresPlugin_SupportedOsVersionsMatchCompatibilityMatrix()
    {
        var meta = PluginRegistry.All["postgres"];

        meta.SupportedOsVersions.Should().BeEquivalentTo(new[] { "22.04", "24.04" });
    }
}
