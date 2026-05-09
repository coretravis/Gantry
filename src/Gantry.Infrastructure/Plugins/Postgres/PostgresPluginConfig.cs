using Gantry.Core.Models;

namespace Gantry.Infrastructure.Plugins.Postgres;

public class PostgresPluginConfig
{
    public string Version { get; init; } = "16";
    public string Database { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string ConnectionStringKey { get; init; } = "ConnectionStrings__DefaultConnection";

    public static PostgresPluginConfig From(PluginConfig config, string appName) =>
        new()
        {
            Version = config.GetOrDefault("version", "16"),
            Database = config.GetOrDefault("database", $"{appName}_db"),
            User = config.GetOrDefault("user", $"{appName}_user"),
            ConnectionStringKey = config.GetOrDefault("connection_string_key", "ConnectionStrings__DefaultConnection"),
        };
}
