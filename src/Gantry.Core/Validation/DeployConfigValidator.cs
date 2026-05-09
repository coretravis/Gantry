using System.Text.RegularExpressions;
using Gantry.Core.Models;

namespace Gantry.Core.Validation;

public class DeployConfigValidator
{
    public IReadOnlyList<string> Validate(DeployConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Server.Host))
            errors.Add("server.host is required");

        if (string.IsNullOrWhiteSpace(config.Server.SshKeyPath))
            errors.Add("server.ssh_key_path is required");

        if (string.IsNullOrWhiteSpace(config.App.Name))
            errors.Add("app.name is required");
        else if (!Regex.IsMatch(config.App.Name, @"^[a-z0-9\-]+$"))
            errors.Add("app.name must contain only lowercase letters, numbers, and hyphens");
        else if (config.App.Name.Length > 50)
            errors.Add("app.name must be 50 characters or fewer");

        if (config.App.Port is < 1024 or > 65535)
            errors.Add("app.port must be between 1024 and 65535");

        if (string.IsNullOrWhiteSpace(config.Runtime.Ecosystem))
            errors.Add("runtime.ecosystem is required");

        if (string.IsNullOrWhiteSpace(config.Runtime.Version))
            errors.Add("runtime.version is required");

        if (config.Domain.HasDomain && config.Domain.Ssl && string.IsNullOrWhiteSpace(config.Domain.SslEmail))
            errors.Add("domain.ssl_email is required when domain.ssl is true");

        if (!string.IsNullOrWhiteSpace(config.Domain.SslEmail) && !IsValidEmail(config.Domain.SslEmail))
            errors.Add("domain.ssl_email must be a valid email address");

        if (string.IsNullOrWhiteSpace(config.Ci.Branch))
            errors.Add("ci.branch is required");

        if (string.IsNullOrWhiteSpace(config.Ci.Platform))
            errors.Add("ci.platform is required");

        return errors;
    }

    public bool IsValid(DeployConfig config) => Validate(config).Count == 0;

    private static bool IsValidEmail(string email) =>
        Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}
