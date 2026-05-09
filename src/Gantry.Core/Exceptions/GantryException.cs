namespace Gantry.Core.Exceptions;

public class GantryException : Exception
{
    public string? Remediation { get; }

    public GantryException(string message, string? remediation = null)
        : base(message)
    {
        Remediation = remediation;
    }

    public GantryException(string message, Exception inner, string? remediation = null)
        : base(message, inner)
    {
        Remediation = remediation;
    }
}

public class PhaseException : GantryException
{
    public string PhaseName { get; }

    public PhaseException(string phaseName, string message, string? remediation = null)
        : base(message, remediation)
    {
        PhaseName = phaseName;
    }

    public PhaseException(string phaseName, string message, Exception inner, string? remediation = null)
        : base(message, inner, remediation)
    {
        PhaseName = phaseName;
    }
}

public class SshException : GantryException
{
    public string Command { get; }
    public int ExitCode { get; }

    public SshException(string command, int exitCode, string stderr, string? remediation = null)
        : base($"Command failed (exit {exitCode}): {command}\n{stderr}", remediation)
    {
        Command = command;
        ExitCode = exitCode;
    }
}

public class ConfigurationException : GantryException
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public ConfigurationException(IReadOnlyList<string> errors)
        : base($"Configuration is invalid: {string.Join("; ", errors)}")
    {
        ValidationErrors = errors;
    }
}
