namespace Gantry.Core.Models;

public class DeployConfig
{
    public string GantryVersion { get; set; } = "1.0.0";
    public ServerConfig Server { get; set; } = new();
    public AppConfig App { get; set; } = new();
    public RuntimeConfig Runtime { get; set; } = new();
    public WebServerConfig WebServer { get; set; } = new();
    public DomainConfig Domain { get; set; } = new();
    public CiConfig Ci { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production"
    };

    public Dictionary<string, Dictionary<string, string>>? Plugins { get; set; }
}

public class ServerConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string SshUser { get; set; } = "root";
    public string SshKeyPath { get; set; } = "~/.ssh/id_ed25519";
    public string DeployUser { get; set; } = "deployer";
    public string DeployKeyPath { get; set; } = "~/.ssh/gantry_deploy_ed25519";
    public string Timezone { get; set; } = "UTC";
    /// <summary>Detected Ubuntu version (e.g. "22.04"). Populated by ConnectAndVerifyPhase and persisted to .deploy.yml.</summary>
    public string OsVersion { get; set; } = string.Empty;
}

public class AppConfig
{
    public string Name { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public int Port { get; set; } = 5000;
    public string DeployPath { get; set; } = string.Empty;
    public int ReleasesToKeep { get; set; } = 3;
    public string HealthCheckPath { get; set; } = "/";
    public int HealthCheckTimeoutSeconds { get; set; } = 30;
}

public class RuntimeConfig
{
    public string Ecosystem { get; set; } = "dotnet";
    public string Version { get; set; } = "8.0";
}

public class WebServerConfig
{
    public string Type { get; set; } = "nginx";
}

public class DomainConfig
{
    public string Name { get; set; } = string.Empty;
    public bool Www { get; set; } = true;
    public bool Ssl { get; set; } = true;
    public string SslEmail { get; set; } = string.Empty;
    public bool HasDomain => !string.IsNullOrWhiteSpace(Name);
}

public class CiConfig
{
    public string Platform { get; set; } = "github_actions";
    public string Branch { get; set; } = "main";
    public string WorkflowPath { get; set; } = ".github/workflows/deploy.yml";
    public bool RunTests { get; set; } = true;
}

public static class DeployConfigExtensions
{
    public static PluginConfig GetPlugin(this DeployConfig config, string name)
    {
        if (config.Plugins == null || !config.Plugins.TryGetValue(name, out var values))
            return PluginConfig.Empty;
        return PluginConfig.From(values);
    }
}
