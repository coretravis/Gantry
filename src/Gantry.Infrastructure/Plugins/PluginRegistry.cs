namespace Gantry.Infrastructure.Plugins;

public record PluginMetadata(
    string Name,
    string Description,
    string DefaultVersion,
    string ConfigExample);

/// <summary>Static catalogue of all known Gantry plugins. Used by <c>gantry plugin list</c>.</summary>
public static class PluginRegistry
{
    public static readonly IReadOnlyDictionary<string, PluginMetadata> All =
        new Dictionary<string, PluginMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["postgres"] = new(
                Name: "postgres",
                Description: "PostgreSQL relational database (v16)",
                DefaultVersion: "16",
                ConfigExample: """
                  postgres:
                    enabled: true
                    version: "16"
                    database: myapp_db
                    user: myapp_user
                    # run_migrations: "true"
                    # migration_command: "dotnet myapp.dll migrate"
                  """),
        };
}
