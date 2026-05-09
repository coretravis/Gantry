using Gantry.Cli.Commands;
using Gantry.Cli.DependencyInjection;
using Gantry.Cli.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

var noColor = args.Contains("--no-color");
var verbose = args.Contains("--verbose");

var serilogLogger = SerilogConfiguration.BuildLogger(verbose, noColor);
var services = new ServiceCollection();
services.AddGantryCli(serilogLogger);
var provider = services.BuildServiceProvider();

var configOption = new Option<string>("--config")
{
    Description = "Path to .deploy.yml",
    DefaultValueFactory = _ => ".deploy.yml",
    Recursive = true
};
var dryRunOption = new Option<bool>("--dry-run") { Description = "Simulate without making changes", Recursive = true };
var verboseOption = new Option<bool>("--verbose") { Description = "Enable verbose console logging", Recursive = true };
var noColorOption = new Option<bool>("--no-color") { Description = "Disable ANSI color output", Recursive = true };

var rootCommand = new RootCommand("Gantry - server provisioning and CI/CD generation for .NET");
rootCommand.Options.Add(configOption);
rootCommand.Options.Add(dryRunOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(noColorOption);

// --- init ---
var initCommand = new InitCommand();
var initSkipOption = new Option<string[]>("--skip-phases") { AllowMultipleArgumentsPerToken = true };
initCommand.Options.Add(initSkipOption);
initCommand.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<InitCommandHandler>();
    return await handler.ExecuteAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(dryRunOption),
        parseResult.GetValue(initSkipOption) ?? [],
        ct);
});
rootCommand.Subcommands.Add(initCommand);

// --- provision ---
var provisionCommand = new ProvisionCommand();
var provisionPhaseOption = new Option<string?>("--phase");
var provisionSkipOption = new Option<string[]>("--skip-phases") { AllowMultipleArgumentsPerToken = true };
provisionCommand.Options.Add(provisionPhaseOption);
provisionCommand.Options.Add(provisionSkipOption);
provisionCommand.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<ProvisionCommandHandler>();
    return await handler.ExecuteAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(dryRunOption),
        parseResult.GetValue(provisionPhaseOption),
        parseResult.GetValue(provisionSkipOption) ?? [],
        ct);
});
rootCommand.Subcommands.Add(provisionCommand);

// --- ci ---
var ciCommand = new CiCommand();
ciCommand.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<CiCommandHandler>();
    return await handler.ExecuteAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(dryRunOption),
        ct);
});
rootCommand.Subcommands.Add(ciCommand);

// --- deploy ---
var deployCommand = new DeployCommand();
deployCommand.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<DeployCommandHandler>();
    return await handler.ExecuteAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(dryRunOption),
        ct);
});
rootCommand.Subcommands.Add(deployCommand);

// --- rollback ---
var rollbackCommand = new RollbackCommand();
var rollbackReleaseOption = new Option<string?>("--release");
rollbackCommand.Options.Add(rollbackReleaseOption);
rollbackCommand.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<RollbackCommandHandler>();
    return await handler.ExecuteAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(rollbackReleaseOption),
        ct);
});
rootCommand.Subcommands.Add(rollbackCommand);

// --- status ---
var statusCommand = new StatusCommand();
statusCommand.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<StatusCommandHandler>();
    return await handler.ExecuteAsync(
        parseResult.GetValue(configOption)!,
        ct);
});
rootCommand.Subcommands.Add(statusCommand);

// --- env ---
var envCommand = new EnvCommand();
var envKeyArg = new Argument<string>("key") { Description = "Environment variable name (use __ for nesting, e.g. ConnectionStrings__Default)" };
var envValueArg = new Argument<string>("value") { Description = "Value to set" };
var envUnsetKeyArg = new Argument<string>("key") { Description = "Environment variable name to remove" };

var envSetCmd = new Command("set", "Set or update a secret on the server and restart the service");
envSetCmd.Arguments.Add(envKeyArg);
envSetCmd.Arguments.Add(envValueArg);
envSetCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<EnvCommandHandler>();
    return await handler.SetAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(envKeyArg)!,
        parseResult.GetValue(envValueArg)!,
        ct);
});

var envListCmd = new Command("list", "List all secrets stored on the server");
envListCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<EnvCommandHandler>();
    return await handler.ListAsync(parseResult.GetValue(configOption)!, ct);
});

var envUnsetCmd = new Command("unset", "Remove a secret from the server and restart the service");
envUnsetCmd.Arguments.Add(envUnsetKeyArg);
envUnsetCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<EnvCommandHandler>();
    return await handler.UnsetAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(envUnsetKeyArg)!,
        ct);
});

var envSetManyPairsArg = new Argument<string[]>("pairs")
{
    Description = "One or more KEY=VALUE pairs",
    Arity = ArgumentArity.OneOrMore
};
var envSetManyCmd = new Command("set-many", "Set multiple secrets at once and restart the service once");
envSetManyCmd.Arguments.Add(envSetManyPairsArg);
envSetManyCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<EnvCommandHandler>();
    return await handler.SetManyAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(envSetManyPairsArg)!,
        ct);
});

var envUnsetManyKeysArg = new Argument<string[]>("keys")
{
    Description = "One or more key names to remove",
    Arity = ArgumentArity.OneOrMore
};
var envUnsetManyCmd = new Command("unset-many", "Remove multiple secrets at once and restart the service once");
envUnsetManyCmd.Arguments.Add(envUnsetManyKeysArg);
envUnsetManyCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<EnvCommandHandler>();
    return await handler.UnsetManyAsync(
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(envUnsetManyKeysArg)!,
        ct);
});

envCommand.Subcommands.Add(envSetCmd);
envCommand.Subcommands.Add(envSetManyCmd);
envCommand.Subcommands.Add(envListCmd);
envCommand.Subcommands.Add(envUnsetCmd);
envCommand.Subcommands.Add(envUnsetManyCmd);
rootCommand.Subcommands.Add(envCommand);

// --- config ---
var configCmd = new ConfigCommand();
var configShowCmd = new Command("show");
configShowCmd.SetAction((parseResult) =>
    Task.FromResult(provider.GetRequiredService<ConfigCommandHandler>().Show(parseResult.GetValue(configOption)!)));
var configValidateCmd = new Command("validate");
configValidateCmd.SetAction((parseResult) =>
    Task.FromResult(provider.GetRequiredService<ConfigCommandHandler>().Validate(parseResult.GetValue(configOption)!)));
configCmd.Subcommands.Add(configShowCmd);
configCmd.Subcommands.Add(configValidateCmd);
rootCommand.Subcommands.Add(configCmd);

// --- plugin ---
var pluginCommand = new PluginCommand();

var pluginListCmd = new Command("list", "Show all available plugins and their enabled status");
pluginListCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<PluginCommandHandler>();
    return await handler.ListAsync(parseResult.GetValue(configOption)!, ct);
});

var pluginAddNameArg = new Argument<string>("name") { Description = "Plugin name to enable (e.g. postgres)" };
var pluginAddSetOption = new Option<string[]>("--set")
{
    Description = "Set a plugin option as key=value (e.g. --set database=myapp --set version=15)",
    AllowMultipleArgumentsPerToken = false
};
pluginAddSetOption.Arity = ArgumentArity.ZeroOrMore;
var pluginAddCmd = new Command("add", "Enable and install a plugin on the server");
pluginAddCmd.Arguments.Add(pluginAddNameArg);
pluginAddCmd.Options.Add(pluginAddSetOption);
pluginAddCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<PluginCommandHandler>();
    return await handler.AddAsync(
        parseResult.GetValue(pluginAddNameArg)!,
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(dryRunOption),
        parseResult.GetValue(pluginAddSetOption) ?? [],
        ct);
});

var pluginRemoveNameArg = new Argument<string>("name") { Description = "Plugin name to remove" };
var pluginRemoveCmd = new Command("remove", "Disable and uninstall a plugin from the server");
pluginRemoveCmd.Arguments.Add(pluginRemoveNameArg);
pluginRemoveCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<PluginCommandHandler>();
    return await handler.RemoveAsync(
        parseResult.GetValue(pluginRemoveNameArg)!,
        parseResult.GetValue(configOption)!,
        parseResult.GetValue(dryRunOption),
        ct);
});

pluginCommand.Subcommands.Add(pluginListCmd);
pluginCommand.Subcommands.Add(pluginAddCmd);
pluginCommand.Subcommands.Add(pluginRemoveCmd);
rootCommand.Subcommands.Add(pluginCommand);

// --- sandbox ---
var sandboxCommand = new SandboxCommand();
var sandboxNameOption = new Option<string>("--name")
{
    Description = "Container name",
    DefaultValueFactory = _ => "gantry-sandbox",
    Recursive = true
};
sandboxCommand.Options.Add(sandboxNameOption);

var sandboxPortOption = new Option<int>("--port")
{
    Description = "SSH port to expose on localhost",
    DefaultValueFactory = _ => 2222
};
var sandboxUbuntuOption = new Option<string>("--ubuntu")
{
    Description = "Ubuntu version to use (22.04 or 24.04)",
    DefaultValueFactory = _ => "24.04"
};

var sandboxUpCmd = new Command("up", "Start a local Ubuntu SSH container for testing");
sandboxUpCmd.Options.Add(sandboxPortOption);
sandboxUpCmd.Options.Add(sandboxUbuntuOption);
sandboxUpCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<SandboxCommandHandler>();
    return await handler.UpAsync(
        parseResult.GetValue(sandboxNameOption)!,
        parseResult.GetValue(sandboxPortOption),
        parseResult.GetValue(sandboxUbuntuOption)!,
        ct);
});

var sandboxDownCmd = new Command("down", "Stop and remove the sandbox container");
sandboxDownCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<SandboxCommandHandler>();
    return await handler.DownAsync(parseResult.GetValue(sandboxNameOption)!, ct);
});

var sandboxStatusCmd = new Command("status", "Show sandbox container status");
sandboxStatusCmd.SetAction(async (parseResult, ct) =>
{
    var handler = provider.GetRequiredService<SandboxCommandHandler>();
    return await handler.StatusAsync(parseResult.GetValue(sandboxNameOption)!, ct);
});

sandboxCommand.Subcommands.Add(sandboxUpCmd);
sandboxCommand.Subcommands.Add(sandboxDownCmd);
sandboxCommand.Subcommands.Add(sandboxStatusCmd);
rootCommand.Subcommands.Add(sandboxCommand);

return rootCommand.Parse(args).Invoke();
