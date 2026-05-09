using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Gantry.Infrastructure.Plugins.Postgres;

public class PostgresConfigurePhase : PluginPhaseBase
{
    public PostgresConfigurePhase(ISshService ssh, ILogger<PostgresConfigurePhase> logger)
        : base(ssh, logger) { }

    public override string Name => "postgres-configure";
    public override string Description => "Create PostgreSQL database, user, and tune memory settings";
    public override int Order => 35;
    public override string PluginName => "postgres";

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var config = context.Config;
        var pg = PostgresPluginConfig.From(config.GetPlugin(PluginName), config.App.Name);

        Report(context, PhaseStatus.Running, $"Configuring database '{pg.Database}' and user '{pg.User}'...");

        var password = context.IsDryRun
            ? "dryrun-placeholder"
            : await TryGetExistingPasswordAsync(config.App.Name, ct) ?? GeneratePassword();

        if (!context.IsDryRun)
        {
            var sqlPath = $"/tmp/gantry_pg_{Guid.NewGuid():N}.sql";
            var sql = BuildInitSql(pg.Database, pg.User, password);
            await _ssh.UploadStringAsync(sql, sqlPath, ct);
            try
            {
                await RunCommandAsync(_ssh, $"sudo -u postgres psql -f {sqlPath}", context, ct: ct);
            }
            finally
            {
                await _ssh.ExecuteAsync($"rm -f {sqlPath}", ct: ct);
            }
        }

        Report(context, PhaseStatus.Running, "Tuning PostgreSQL memory settings...");
        await TuneMemoryAsync(context, ct);

        Report(context, PhaseStatus.Running, "Writing connection string to server .env...");
        if (!context.IsDryRun)
        {
            await WriteConnectionStringAsync(config, pg, password, ct);
            // Restart (not reload) so shared_buffers and wal_buffers take effect immediately
            await RunCommandAsync(_ssh, "systemctl restart postgresql", context, ct: ct);
        }
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (context.IsDryRun) return;

        var config = context.Config;
        var pg = PostgresPluginConfig.From(config.GetPlugin(PluginName), config.App.Name);
        Logger.LogInformation("Rolling back postgres-configure: dropping database '{Db}' and user '{User}'",
            pg.Database, pg.User);

        await _ssh.ExecuteAsync(
            $"sudo -u postgres psql -c \"DROP DATABASE IF EXISTS \\\"{pg.Database}\\\";\" 2>/dev/null || true",
            ct: ct);
        await _ssh.ExecuteAsync(
            $"sudo -u postgres psql -c \"DROP USER IF EXISTS \\\"{pg.User}\\\";\" 2>/dev/null || true",
            ct: ct);

        // Remove the connection string from .env so the app doesn't start with stale credentials
        var envPath = $"/var/www/{config.App.Name}/.env";
        var key = pg.ConnectionStringKey;
        try
        {
            if (await _ssh.FileExistsAsync(envPath, ct))
            {
                var existing = await _ssh.DownloadStringAsync(envPath, ct);
                var updated = string.Join('\n',
                    existing.Split('\n')
                        .Select(l => l.TrimEnd('\r'))
                        .Where(l => !l.StartsWith($"{key}=") && !string.IsNullOrEmpty(l))) + '\n';
                await _ssh.UploadStringAsync(updated, envPath, ct);
                Logger.LogInformation("Removed {Key} from {EnvPath}", key, envPath);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not remove connection string from {EnvPath}: {Error}", envPath, ex.Message);
        }
    }

    private async Task<string?> TryGetExistingPasswordAsync(string appName, CancellationToken ct)
    {
        var envPath = $"/var/www/{appName}/.env";
        try
        {
            if (!await _ssh.FileExistsAsync(envPath, ct)) return null;

            var content = await _ssh.DownloadStringAsync(envPath, ct);
            const string prefix = "ConnectionStrings__DefaultConnection=";
            var line = content.Split('\n').FirstOrDefault(l => l.StartsWith(prefix));
            if (line == null) return null;

            var connStr = line[prefix.Length..].Trim('\r', ' ');
            var pwdPart = connStr.Split(';')
                .FirstOrDefault(p => p.TrimStart().StartsWith("Password=", StringComparison.OrdinalIgnoreCase));
            return pwdPart?.Split('=', 2).ElementAtOrDefault(1);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Could not read existing password from .env: {Error}", ex.Message);
            return null;
        }
    }

    private static string GeneratePassword()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildInitSql(string database, string user, string password)
    {
        // Use $$ dollar-quoting; hex password contains only [0-9a-f] - safe in any quoting context
        var escapedPwd = password.Replace("'", "''");
        return $"""
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{user}') THEN
                CREATE USER {user} WITH PASSWORD '{escapedPwd}';
              ELSE
                ALTER USER {user} WITH PASSWORD '{escapedPwd}';
              END IF;
            END $$;
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT FROM pg_database WHERE datname = '{database}') THEN
                CREATE DATABASE {database} OWNER {user};
              END IF;
            END $$;
            GRANT ALL PRIVILEGES ON DATABASE {database} TO {user};
            \connect {database}
            GRANT ALL ON SCHEMA public TO {user};
            """;
    }

    private async Task TuneMemoryAsync(ProvisioningContext context, CancellationToken ct)
    {
        var totalMb = context.ServerInfo?.TotalMemoryMb ?? 0;

        var (sharedBuffers, effectiveCacheSize, workMem) = totalMb switch
        {
            >= 4096 => ("1GB", "3GB", "16MB"),
            >= 2048 => ("512MB", "1536MB", "8MB"),
            _       => ("128MB", "384MB", "4MB"),
        };

        var settings = new (string Key, string Value)[]
        {
            ("shared_buffers", sharedBuffers),
            ("effective_cache_size", effectiveCacheSize),
            ("work_mem", workMem),
            ("maintenance_work_mem", "64MB"),
            ("checkpoint_completion_target", "0.9"),
            ("wal_buffers", "16MB"),
        };

        foreach (var (key, value) in settings)
            await RunCommandAsync(_ssh,
                $"sudo -u postgres psql -c \"ALTER SYSTEM SET {key} = '{value}'\"",
                context, ct: ct);
    }

    private async Task WriteConnectionStringAsync(
        DeployConfig config,
        PostgresPluginConfig pg,
        string password,
        CancellationToken ct)
    {
        var envPath = $"/var/www/{config.App.Name}/.env";
        var key = pg.ConnectionStringKey;
        var value = $"Host=localhost;Port=5432;Database={pg.Database};Username={pg.User};Password={password}";

        // Ensure the directory exists - postgres-configure (order 35) runs before
        // process-manager-setup (order 50) which normally creates this directory.
        await _ssh.ExecuteAsync($"mkdir -p /var/www/{config.App.Name}", ct: ct);

        var lines = new List<string>();
        if (await _ssh.FileExistsAsync(envPath, ct))
        {
            var existing = await _ssh.DownloadStringAsync(envPath, ct);
            lines = existing.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !l.StartsWith($"{key}="))
                .ToList();
        }

        lines.Add($"{key}={value}");
        var content = string.Join('\n', lines.Where(l => !string.IsNullOrEmpty(l))) + '\n';

        await _ssh.UploadStringAsync(content, envPath, ct);
        await _ssh.ExecuteAsync(
            $"chmod 600 {envPath} && chown {config.Server.DeployUser}:{config.Server.DeployUser} {envPath}",
            ct: ct);

        Logger.LogInformation("Connection string written to {EnvPath}", envPath);
    }
}
