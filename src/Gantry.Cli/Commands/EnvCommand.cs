using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class EnvCommand : Command
{
    public EnvCommand() : base("env", "Manage server-side environment variables and secrets") { }
}

public class EnvCommandHandler
{
    private readonly ISshService _ssh;
    private readonly IConfigService _configService;
    private readonly IProcessManager _processManager;
    private readonly ILogger<EnvCommandHandler> _logger;

    public EnvCommandHandler(
        ISshService ssh,
        IConfigService configService,
        IProcessManager processManager,
        ILogger<EnvCommandHandler> logger)
    {
        _ssh = ssh;
        _configService = configService;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<int> SetAsync(string configPath, string key, string value, CancellationToken ct = default)
    {
        return await RunAsync(configPath, ct, lines =>
        {
            ApplySet(lines, key, value);
            ConsoleRenderer.ShowInfo($"Set {key}");
        });
    }

    public async Task<int> SetManyAsync(string configPath, string[] pairs, CancellationToken ct = default)
    {
        var parsed = new List<(string Key, string Value)>();
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                ConsoleRenderer.ShowError($"Invalid format '{pair}'. Expected KEY=VALUE.");
                return 1;
            }
            parsed.Add((pair[..eq], pair[(eq + 1)..]));
        }

        return await RunAsync(configPath, ct, lines =>
        {
            foreach (var (k, v) in parsed)
            {
                ApplySet(lines, k, v);
                ConsoleRenderer.ShowInfo($"Set {k}");
            }
        });
    }

    public async Task<int> ListAsync(string configPath, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);
        await ConnectAsync(config, ct);

        try
        {
            var lines = await ReadEnvFileAsync(EnvFilePath(config.App.Name), ct);
            var vars = lines
                .Where(l => !l.StartsWith('#') && l.Contains('=') && !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (vars.Count == 0)
            {
                ConsoleRenderer.ShowInfo("No secrets set. Use: gantry env set KEY VALUE");
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("[bold]Key[/]");
            table.AddColumn("[bold]Value[/]");
            foreach (var line in vars)
            {
                var eq = line.IndexOf('=');
                table.AddRow(Markup.Escape(line[..eq]), Markup.Escape(line[(eq + 1)..]));
            }
            AnsiConsole.Write(table);
            return 0;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }

    public async Task<int> UnsetAsync(string configPath, string key, CancellationToken ct = default)
    {
        return await RunAsync(configPath, ct, lines =>
        {
            var removed = lines.RemoveAll(l => l.StartsWith($"{key}="));
            if (removed == 0)
                ConsoleRenderer.ShowWarning($"Key '{key}' not found.");
            else
                ConsoleRenderer.ShowInfo($"Unset {key}");
        });
    }

    public async Task<int> UnsetManyAsync(string configPath, string[] keys, CancellationToken ct = default)
    {
        return await RunAsync(configPath, ct, lines =>
        {
            foreach (var key in keys)
            {
                var removed = lines.RemoveAll(l => l.StartsWith($"{key}="));
                if (removed == 0)
                    ConsoleRenderer.ShowWarning($"Key '{key}' not found.");
                else
                    ConsoleRenderer.ShowInfo($"Unset {key}");
            }
        });
    }

    // Connects, reads the env file, runs the mutation, writes back, restarts once.
    private async Task<int> RunAsync(string configPath, CancellationToken ct, Action<List<string>> mutate)
    {
        var config = _configService.Load(configPath);
        await ConnectAsync(config, ct);

        try
        {
            var envPath = EnvFilePath(config.App.Name);
            var lines = await ReadEnvFileAsync(envPath, ct);
            mutate(lines);
            await WriteEnvFileAsync(envPath, lines, ct);
            await _processManager.RestartAsync(_ssh, config.App.Name, ct);
            ConsoleRenderer.ShowSuccess($"Restarted {config.App.Name}.service");
            return 0;
        }
        catch (GantryException ex)
        {
            ConsoleRenderer.ShowError(ex.Message);
            return 1;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }

    private static void ApplySet(List<string> lines, string key, string value)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith($"{key}="))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }
        lines.Add($"{key}={value}");
    }

    private static string EnvFilePath(string appName) => $"/var/www/{appName}/shared/.env";

    private async Task ConnectAsync(DeployConfig config, CancellationToken ct)
    {
        var server = config.Server;
        var expandedKey = server.DeployKeyPath
            .Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
        await _ssh.ConnectAsync(server.Host, server.DeployUser, expandedKey, server.Port, ct);
    }

    private async Task<List<string>> ReadEnvFileAsync(string path, CancellationToken ct)
    {
        var result = await _ssh.ExecuteAsync($"cat {path} 2>/dev/null", ct: ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Stdout))
            return [];
        return result.Stdout
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();
    }

    private async Task WriteEnvFileAsync(string path, List<string> lines, CancellationToken ct)
    {
        var content = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l))) + "\n";
        await _ssh.UploadStringAsync(content, path, ct);
        await _ssh.ExecuteAsync($"chmod 600 {path}", ct: ct);
    }
}
