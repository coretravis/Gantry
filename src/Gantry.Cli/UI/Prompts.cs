using Gantry.Core.Models;
using Spectre.Console;
using System.Diagnostics;

namespace Gantry.Cli.UI;

public static class Prompts
{
    public static DeployConfig GatherConfig(DeployConfig defaults)
    {
        AnsiConsole.MarkupLine("[bold]Server Configuration[/]");
        AnsiConsole.Write(new Rule());

        var config = new DeployConfig();

        config.Server.Host = Ask("Server IP or hostname", defaults.Server.Host);
        config.Server.Port = AskInt("SSH port", defaults.Server.Port);
        config.Server.SshUser = Ask("SSH user (for initial connection)", defaults.Server.SshUser);
        config.Server.SshKeyPath = Ask("SSH private key path", defaults.Server.SshKeyPath);
        config.Server.DeployUser = Ask("Deploy user to create", defaults.Server.DeployUser);
        config.Server.DeployKeyPath = Ask("Path to save deploy key (generated if missing)", defaults.Server.DeployKeyPath);
        config.Server.Timezone = Ask("Server timezone", defaults.Server.Timezone);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Application Configuration[/]");
        AnsiConsole.Write(new Rule());

        config.App.Name = Ask("App name (lowercase, hyphens only)", defaults.App.Name);
        config.App.ProjectPath = Ask("Path to .csproj (relative to repo root)", string.IsNullOrEmpty(defaults.App.ProjectPath) ? DetectProjectPath() : defaults.App.ProjectPath);
        config.App.Port = AskInt("Local port the app listens on", defaults.App.Port);
        config.App.HealthCheckPath = Ask("Health check URL path", defaults.App.HealthCheckPath);
        config.App.ReleasesToKeep = AskInt("Number of previous releases to keep for rollback", defaults.App.ReleasesToKeep);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Runtime Configuration[/]");
        AnsiConsole.Write(new Rule());

        config.Runtime.Ecosystem = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Ecosystem")
                .AddChoices("dotnet"));

        config.Runtime.Version = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(".NET version")
                .AddChoices("8.0", "9.0", "10.0", "7.0", "6.0"));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Domain & SSL Configuration[/]");
        AnsiConsole.Write(new Rule());

        var hasDomain = AskBool("Do you have a domain name?", defaults.Domain.HasDomain);
        if (hasDomain)
        {
            config.Domain.Name = Ask("Domain name (e.g. myapp.com)", defaults.Domain.Name);
            config.Domain.Www = AskBool("Include www subdomain?", defaults.Domain.Www);
            config.Domain.Ssl = AskBool("Enable SSL (Let's Encrypt)?", defaults.Domain.Ssl);
            if (config.Domain.Ssl)
                config.Domain.SslEmail = Ask("Email for Let's Encrypt registration", defaults.Domain.SslEmail);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]CI/CD Configuration[/]");
        AnsiConsole.Write(new Rule());

        config.Ci.Platform = "github_actions";
        config.Ci.Branch = Ask("Deployment branch", defaults.Ci.Branch);
        config.Ci.RunTests = AskBool("Run tests in CI pipeline?", defaults.Ci.RunTests);

        return config;
    }

    public static string Ask(string prompt, string defaultValue)
    {
        var result = AnsiConsole.Prompt(
            new TextPrompt<string>($"[grey]{Markup.Escape(prompt)}[/]")
                .DefaultValue(defaultValue)
                .DefaultValueStyle("grey"));
        return result;
    }

    public static int AskInt(string prompt, int defaultValue)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<int>($"[grey]{Markup.Escape(prompt)}[/]")
                .DefaultValue(defaultValue)
                .DefaultValueStyle("grey"));
    }

    public static bool AskBool(string prompt, bool defaultValue)
    {
        return AnsiConsole.Prompt(
            new ConfirmationPrompt($"[grey]{Markup.Escape(prompt)}[/]")
            {
                DefaultValue = defaultValue
            });
    }

    public static string AskSecret(string prompt)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"[grey]{Markup.Escape(prompt)}[/]")
                .Secret());
    }

    // Finds a .csproj in the current directory and returns a path relative to the git root,
    // so it works correctly in both `gantry deploy` (run from project dir) and CI (run from repo root).
    private static string DetectProjectPath()
    {
        var csproj = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (csproj == null) return string.Empty;

        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --show-toplevel")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
                proc.WaitForExit();
                var gitRoot = stdoutTask.Result.Trim();
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(gitRoot))
                    return Path.GetRelativePath(gitRoot, csproj).Replace('\\', '/');
            }
        }
        catch { }

        return Path.GetFileName(csproj);
    }
}
