using System.Diagnostics;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.Github;

public class GithubService : IGithubService
{
    private readonly ILogger<GithubService> _logger;

    public GithubService(ILogger<GithubService> logger) => _logger = logger;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await RunGhAsync("--version", ct);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task SetSecretAsync(string name, string value, CancellationToken ct = default)
    {
        _logger.LogDebug("Setting GitHub secret: {Name}", name);
        var result = await RunGhAsync($"secret set {name}", ct, value);
        if (result.ExitCode != 0)
            throw new GantryException($"Failed to set GitHub secret '{name}': {result.Stderr}",
                "Ensure you are authenticated with 'gh auth login' and have write access to the repository.");
        _logger.LogInformation("GitHub secret '{Name}' set successfully", name);
    }

    public async Task<bool> SecretExistsAsync(string name, CancellationToken ct = default)
    {
        var result = await RunGhAsync($"secret list", ct);
        return result.ExitCode == 0 && result.Stdout.Contains(name);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunGhAsync(string args, CancellationToken ct, string? stdin = null)
    {
        var psi = new ProcessStartInfo("gh", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new GantryException("Failed to start gh process.");

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }
}
