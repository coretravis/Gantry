using Gantry.Cli.UI;
using Gantry.Core.Interfaces;
using Gantry.Core.Validation;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class ConfigCommand : Command
{
    public ConfigCommand() : base("config", "View or modify deployment configuration") { }
}

public class ConfigCommandHandler
{
    private readonly IConfigService _configService;
    private readonly DeployConfigValidator _validator;
    private readonly ILogger<ConfigCommandHandler> _logger;

    public ConfigCommandHandler(IConfigService configService, DeployConfigValidator validator, ILogger<ConfigCommandHandler> logger)
    {
        _configService = configService;
        _validator = validator;
        _logger = logger;
    }

    public int Show(string configPath)
    {
        if (!_configService.Exists(configPath))
        {
            ConsoleRenderer.ShowError($"No configuration found at {configPath}. Run 'gantry init' first.");
            return 1;
        }

        var config = _configService.Load(configPath);
        ConsoleRenderer.ShowDeployConfig(config);
        return 0;
    }

    public int Validate(string configPath)
    {
        if (!_configService.Exists(configPath))
        {
            ConsoleRenderer.ShowError($"No configuration found at {configPath}.");
            return 1;
        }

        var config = _configService.Load(configPath);
        var errors = _validator.Validate(config);

        if (errors.Count == 0)
        {
            ConsoleRenderer.ShowSuccess("Configuration is valid.");
            return 0;
        }

        ConsoleRenderer.ShowError($"Configuration has {errors.Count} error(s):");
        foreach (var error in errors)
            AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(error)}");
        return 1;
    }
}
