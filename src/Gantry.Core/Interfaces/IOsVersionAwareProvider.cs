namespace Gantry.Core.Interfaces;

/// <summary>
/// Optional capability for runtime providers that have OS-version-specific
/// compatibility constraints. Phases check for this interface to emit early,
/// actionable errors before touching the server.
/// </summary>
public interface IOsVersionAwareProvider
{
    bool IsVersionSupportedOnOs(string runtimeVersion, string osVersion);
    IReadOnlyList<string> SupportedVersionsForOs(string osVersion);
}
