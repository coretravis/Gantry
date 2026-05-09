namespace Gantry.Core.Models;

public class CommandResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string Command { get; init; } = string.Empty;

    public bool Success => ExitCode == 0;

    public static CommandResult DryRun(string command) => new()
    {
        ExitCode = 0,
        Stdout = $"[dry-run] {command}",
        Command = command
    };
}
