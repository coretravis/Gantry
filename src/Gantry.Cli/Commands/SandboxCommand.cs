using Gantry.Cli.UI;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;
using System.Diagnostics;

namespace Gantry.Cli.Commands;

public class SandboxCommand : Command
{
    public SandboxCommand() : base("sandbox", "Manage a local Docker test server") { }
}

public class SandboxCommandHandler
{
    private readonly ILogger<SandboxCommandHandler> _logger;

    private static readonly string SandboxDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gantry", "sandbox");

    private static readonly string KeyPath = Path.Combine(SandboxDir, "id_ed25519");

    public SandboxCommandHandler(ILogger<SandboxCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task<int> UpAsync(string name, int port, string ubuntuVersion, CancellationToken ct)
    {
        if (!IsDockerAvailable())
        {
            ConsoleRenderer.ShowError("Docker is not available. Install Docker Desktop and ensure it is running.");
            return Task.FromResult(1);
        }

        var (inspectExit, inspectStatus, _) = Run("docker", $"inspect --format {{{{.State.Status}}}} {name}");
        if (inspectExit == 0 && inspectStatus == "running")
        {
            ConsoleRenderer.ShowWarning($"Container '{name}' is already running.");
            PrintConnectionDetails(port, KeyPath);
            return Task.FromResult(0);
        }

        if (inspectExit == 0)
        {
            ConsoleRenderer.ShowInfo($"Removing stopped container '{name}'...");
            Run("docker", $"rm {name}");
        }

        Directory.CreateDirectory(SandboxDir);
        if (!File.Exists(KeyPath))
        {
            ConsoleRenderer.ShowInfo("Generating sandbox SSH key...");
            var (keyExit, _, keyErr) = Run("ssh-keygen", $"-t ed25519 -f \"{KeyPath}\" -N \"\" -C \"gantry-sandbox\"");
            if (keyExit != 0)
            {
                ConsoleRenderer.ShowError($"Failed to generate SSH key: {keyErr}");
                return Task.FromResult(1);
            }
        }

        ConsoleRenderer.ShowInfo($"Starting Ubuntu {ubuntuVersion} container '{name}' on port {port}...");
        var (runExit, _, runErr) = Run("docker", $"run -d --name {name} -p {port}:22 ubuntu:{ubuntuVersion} tail -f /dev/null");
        if (runExit != 0)
        {
            ConsoleRenderer.ShowError($"Failed to start container: {runErr}");
            return Task.FromResult(1);
        }

        ConsoleRenderer.ShowInfo("Installing OpenSSH server (this takes about 30 seconds)...");
        var (setupExit, _, setupErr) = Run("docker",
            $"exec {name} bash -c \"apt-get update -qq && apt-get install -y openssh-server sudo -qq 2>/dev/null\"");
        if (setupExit != 0)
        {
            ConsoleRenderer.ShowError($"Failed to install SSH server: {setupErr}");
            Run("docker", $"rm -f {name}");
            return Task.FromResult(1);
        }

        var tempKey = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempKey, File.ReadAllText(KeyPath + ".pub").Trim());
            Run("docker", $"cp \"{tempKey}\" {name}:/tmp/gantry_key.pub");
        }
        finally
        {
            File.Delete(tempKey);
        }

        const string authScript =
            "mkdir -p /root/.ssh && " +
            "cat /tmp/gantry_key.pub > /root/.ssh/authorized_keys && " +
            "chmod 700 /root/.ssh && " +
            "chmod 600 /root/.ssh/authorized_keys && " +
            "service ssh start";

        var (authExit, _, authErr) = Run("docker", $"exec {name} bash -c \"{authScript}\"");
        if (authExit != 0)
        {
            ConsoleRenderer.ShowError($"Failed to configure SSH: {authErr}");
            Run("docker", $"rm -f {name}");
            return Task.FromResult(1);
        }

        _logger.LogInformation("Sandbox container {Name} started on port {Port}", name, port);
        ConsoleRenderer.ShowSuccess($"Sandbox '{name}' is ready.");
        PrintConnectionDetails(port, KeyPath);
        return Task.FromResult(0);
    }

    public Task<int> DownAsync(string name, CancellationToken ct)
    {
        if (!IsDockerAvailable())
        {
            ConsoleRenderer.ShowError("Docker is not available.");
            return Task.FromResult(1);
        }

        var (inspectExit, _, _) = Run("docker", $"inspect {name}");
        if (inspectExit != 0)
        {
            ConsoleRenderer.ShowWarning($"Container '{name}' does not exist.");
            return Task.FromResult(0);
        }

        ConsoleRenderer.ShowInfo($"Removing container '{name}'...");
        var (exit, _, err) = Run("docker", $"rm -f {name}");
        if (exit != 0)
        {
            ConsoleRenderer.ShowError($"Failed to remove container: {err}");
            return Task.FromResult(1);
        }

        _logger.LogInformation("Sandbox container {Name} removed", name);
        ConsoleRenderer.ShowSuccess($"Sandbox '{name}' removed.");
        return Task.FromResult(0);
    }

    public Task<int> StatusAsync(string name, CancellationToken ct)
    {
        if (!IsDockerAvailable())
        {
            ConsoleRenderer.ShowError("Docker is not available.");
            return Task.FromResult(1);
        }

        var (exit, status, _) = Run("docker", $"inspect --format {{{{.State.Status}}}} {name}");
        if (exit != 0)
        {
            ConsoleRenderer.ShowInfo($"Sandbox '{name}' does not exist. Run 'gantry sandbox up' to create one.");
            return Task.FromResult(0);
        }

        var isRunning = status.Trim() == "running";
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("Container", Markup.Escape(name));
        table.AddRow("Status", isRunning ? "[green]running[/]" : $"[yellow]{Markup.Escape(status.Trim())}[/]");
        table.AddRow("SSH host", "localhost");
        table.AddRow("SSH key", Markup.Escape(KeyPath));
        AnsiConsole.Write(table);

        return Task.FromResult(isRunning ? 0 : 1);
    }

    private static void PrintConnectionDetails(int port, string keyPath)
    {
        ConsoleRenderer.ShowSummary("Sandbox Connection Details", new[]
        {
            ("Host", "localhost"),
            ("SSH port", port.ToString()),
            ("User", "root"),
            ("SSH key", keyPath),
        });
        AnsiConsole.MarkupLine("  [grey]Run [blue]gantry init[/] and enter the above values when prompted.[/]");
    }

    private static bool IsDockerAvailable()
    {
        var (exit, _, _) = Run("docker", "info");
        return exit == 0;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string command, string args)
    {
        var psi = new ProcessStartInfo(command, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return (-1, string.Empty, "Failed to start process");
            var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());
            proc.WaitForExit();
            return (proc.ExitCode, stdoutTask.Result.Trim(), stderrTask.Result.Trim());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
