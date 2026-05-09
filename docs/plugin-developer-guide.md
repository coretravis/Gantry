# Gantry Plugin Developer Guide

This guide covers everything needed to add a new built-in plugin to Gantry. It assumes familiarity with the existing codebase - specifically `PhaseBase`, the DI registration pattern, and how `ISshService` is used inside phases.

---

## What a Plugin Is

A plugin is a named, opt-in capability that extends Gantry's provisioning pipeline. It is made up of:

- **One or more provisioning phases** - extend `PluginPhaseBase`, live in the pipeline, handle install and configure.
- **A status contributor** - implements `IStatusContributor`, provides health checks to `gantry status`.
- **A deploy hook** (optional) - implements `IDeployHook`, runs after service restart during `gantry deploy`.
- **A typed config class** - parses the plugin's slice of `.deploy.yml`.

Plugins are registered in `ServiceCollectionExtensions` and are built into the Gantry binary. There is no dynamic plugin loading.

---

## File Layout

All plugin code lives under `Gantry.Infrastructure/Plugins/<name>/`. Each class has its own file.

```text
Gantry.Infrastructure/Plugins/
└── YourPlugin/
    ├── YourPluginConfig.cs          required - typed config
    ├── YourPluginInstallPhase.cs    required - installs the service
    ├── YourPluginConfigurePhase.cs  required if setup needs a second phase
    ├── YourPluginStatusContributor.cs  required - health checks
    └── YourPluginDeployHook.cs      optional - post-deploy actions
```

If your plugin needs a server-side script or SQL template, add it as an embedded resource:

```text
Gantry.Infrastructure/Templates/Resources/
└── yourplugin-init.sql    (or .sh, .conf, etc.)
```

Mark it as `EmbeddedResource` in the `.csproj` - the existing wildcard `<EmbeddedResource Include="Templates\Resources\**\*" />` already covers it.

---

## Step 1 - Define the Config Class

Your config class parses the plugin's key-value dictionary from `.deploy.yml` into a typed object. It is the single place where raw string lookups happen.

```csharp
// Gantry.Infrastructure/Plugins/YourPlugin/YourPluginConfig.cs

public class YourPluginConfig
{
    public string Version { get; }
    public string SomeRequiredValue { get; }
    public bool SomeFlag { get; }

    private YourPluginConfig(string version, string someRequiredValue, bool someFlag)
    {
        Version = version;
        SomeRequiredValue = someRequiredValue;
        SomeFlag = someFlag;
    }

    public static YourPluginConfig From(PluginConfig config) => new(
        config.Get("version", "default"),
        config.Get("some_required_value"),
        config.Get("some_flag", "false") == "true"
    );
}
```

`PluginConfig.Get(key, default)` returns the default if the key is absent. `PluginConfig.GetInt(key, default)` does the same for integers. Do not scatter `config.Get(...)` calls across multiple phase files - call `YourPluginConfig.From(GetPluginConfig(context))` once at the top of `RunAsync` and use the typed object throughout.

---

## Step 2 - Write the Install Phase

The install phase puts the software on the server. It should be idempotent - running it twice must be safe.

```csharp
// Gantry.Infrastructure/Plugins/YourPlugin/YourPluginInstallPhase.cs

public class YourPluginInstallPhase : PluginPhaseBase
{
    public YourPluginInstallPhase(ILogger<YourPluginInstallPhase> logger) : base(logger) { }

    public override string Name => "yourplugin-install";
    public override string Description => "Install YourPlugin";
    public override int Order => 25; // see ordering table below

    protected override string PluginName => "yourplugin";

    protected override PluginResourceRequirements? Requirements => new()
    {
        MinRamMb = 512,
        MinDiskGb = 2
    };

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var cfg = YourPluginConfig.From(GetPluginConfig(context));

        // Check idempotency: skip if already installed
        var check = await _ssh.ExecuteAsync("systemctl is-active yourplugin 2>/dev/null", ct: ct);
        if (check.Stdout.Trim() == "active")
        {
            Logger.LogInformation("YourPlugin already active, skipping install");
            return;
        }

        await RunCommandAsync(_ssh, $"apt-get install -y yourplugin-{cfg.Version}",
            context, timeout: TimeSpan.FromMinutes(5), ct: ct);
        await RunCommandAsync(_ssh, "systemctl enable yourplugin", context, ct: ct);
        await RunCommandAsync(_ssh, "systemctl start yourplugin", context, ct: ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        await _ssh.ExecuteAsync("apt-get remove -y yourplugin 2>/dev/null || true", ct: ct);
        await _ssh.ExecuteAsync("apt-get autoremove -y 2>/dev/null || true", ct: ct);
    }
}
```

### What you get for free from `PluginPhaseBase`

- **Enabled check** - `ExecuteAsync` returns immediately if the plugin is not in `.deploy.yml`. Your `RunAsync` is never called.
- **Resource warnings** - `Requirements` is checked against `context.ServerInfo` before `RunAsync`, emitting warnings without aborting.
- **Dry-run** - `RunCommandAsync` short-circuits and logs the command without executing it.
- **Rollback registration** - `context.CompletedPhases` is updated by the base class; the orchestrator handles rollback ordering automatically.

### What you must implement

- `RunAsync` - the install logic.
- `RunRollbackAsync` - best-effort undo. Use `|| true` on destructive commands so a partial rollback does not halt the rest of the rollback sequence.

### Phase ordering

Plugin phases slot into the gaps left by renumbering the core phases to multiples of 10:

| Order range | Position in pipeline |
|---|---|
| 21–29 | After OS hardening, before runtime |
| 31–39 | After runtime, before web server config |
| 41–49 | After web server config, before process manager |
| 51–59 | After process manager, before SSL |

Use the lowest range that satisfies your dependencies. Most plugins only need apt-get, which is available after OS hardening (20), so 25 is the standard install slot.

---

## Step 3 - Write the Configure Phase (if needed)

Split install and configure when the configuration step is meaningfully different from installation - for example, creating a database and user after the server is running, or writing app-specific config files. If your plugin is simple (install + enable, nothing else), a single phase is fine.

```csharp
// Gantry.Infrastructure/Plugins/YourPlugin/YourPluginConfigurePhase.cs

public class YourPluginConfigurePhase : PluginPhaseBase
{
    private readonly ITemplateEngine _templates;

    public YourPluginConfigurePhase(ITemplateEngine templates, ILogger<YourPluginConfigurePhase> logger)
        : base(logger) => _templates = templates;

    public override string Name => "yourplugin-configure";
    public override string Description => "Configure YourPlugin for the application";
    public override int Order => 35;

    protected override string PluginName => "yourplugin";
    protected override PluginResourceRequirements? Requirements => null;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var cfg = YourPluginConfig.From(GetPluginConfig(context));

        var script = _templates.Render("yourplugin-init.sh", new Dictionary<string, string>
        {
            ["some_value"] = cfg.SomeRequiredValue,
        });

        await UploadIfChangedAsync(_ssh, script, "/tmp/gantry-yourplugin-init.sh", context, ct);
        await RunCommandAsync(_ssh, "bash /tmp/gantry-yourplugin-init.sh", context, ct: ct);
        await RunCommandAsync(_ssh, "rm -f /tmp/gantry-yourplugin-init.sh", context, ct: ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        // Undo what configure did
    }
}
```

`UploadIfChangedAsync` compares the rendered content to what is already on the server and skips the upload if they match - keeping configure idempotent without an extra roundtrip on unchanged runs.

### Writing secrets to the server

If your plugin generates credentials, write them to the server's `.env` file - never to `.deploy.yml` or any local file. Use the same underlying write that `gantry env set` uses:

```csharp
var envLine = $"YourPlugin__ConnectionString=...";
var envPath = $"/var/www/{context.Config.App.Name}/.env";

var existing = await _ssh.FileExistsAsync(envPath, ct)
    ? await _ssh.DownloadStringAsync(envPath, ct)
    : string.Empty;

var updated = ApplyEnvSet(existing, "YourPlugin__ConnectionString", value);
await _ssh.UploadStringAsync(updated, envPath, ct);
await _ssh.ExecuteAsync(
    $"chmod 600 {envPath} && sudo systemctl restart {context.Config.App.Name}.service", ct: ct);
```

The `ApplyEnvSet` logic (replace existing line or append) mirrors `EnvCommandHandler.ApplySet` - consider extracting it to a shared helper in `Gantry.Infrastructure` if more than one plugin needs it.

---

## Step 4 - Write the Status Contributor

The status contributor adds health checks to `gantry status`. It runs on-demand (read-only) and must never mutate server state.

```csharp
// Gantry.Infrastructure/Plugins/YourPlugin/YourPluginStatusContributor.cs

public class YourPluginStatusContributor : IStatusContributor
{
    public string Name => "yourplugin";

    public bool IsApplicable(DeployConfig config) =>
        config.GetPlugin("yourplugin").IsEnabled;

    public async Task<IReadOnlyList<PluginHealthResult>> CheckAsync(
        ISshService ssh, DeployConfig config, CancellationToken ct)
    {
        var cfg = YourPluginConfig.From(config.GetPlugin("yourplugin"));
        var results = new List<PluginHealthResult>();

        // Service running?
        var active = await ssh.ExecuteAsync("systemctl is-active yourplugin", ct: ct);
        results.Add(active.Stdout.Trim() == "active"
            ? Pass("Service running")
            : Fail("Service not running", "sudo systemctl start yourplugin"));

        // Port accepting connections?
        var port = await ssh.ExecuteAsync("nc -z localhost 1234 && echo ok", ct: ct);
        results.Add(port.Success && port.Stdout.Contains("ok")
            ? Pass("Accepting connections on 1234")
            : Fail("Not accepting connections on 1234", "Check logs: journalctl -u yourplugin -n 50"));

        // Disk space?
        var disk = await ssh.ExecuteAsync("df /var/lib/yourplugin --output=pcent | tail -1", ct: ct);
        if (int.TryParse(disk.Stdout.Trim().TrimEnd('%'), out var pct) && pct > 85)
            results.Add(Warn("Disk usage high", $"/var/lib/yourplugin: {pct}% used"));
        else
            results.Add(Pass($"Disk usage normal ({pct}%)"));

        return results;
    }

    private static PluginHealthResult Pass(string label) =>
        new() { Label = label, Status = HealthStatus.Pass };

    private static PluginHealthResult Warn(string label, string detail) =>
        new() { Label = label, Status = HealthStatus.Warning, Detail = detail };

    private static PluginHealthResult Fail(string label, string remediation) =>
        new() { Label = label, Status = HealthStatus.Fail, Remediation = remediation };
}
```

### Rules for status contributors

- No mutations. No `systemctl restart`, no file writes, no `apt-get`.
- Every check must be independently catchable - do not let one failed SSH command abort the rest of the checks. Wrap individual checks in try/catch if the command might fail on a degraded server.
- A `Fail` result sets the exit code of `gantry status` to `1`. A `Warn` result sets it to `2`. Design your thresholds accordingly.

---

## Step 5 - Write the Deploy Hook (optional)

A deploy hook runs after the app service restarts and before the HTTP health check, on every `gantry deploy`. Only add one if your plugin has genuine post-deploy work - running database migrations, warming a cache, etc.

```csharp
// Gantry.Infrastructure/Plugins/YourPlugin/YourPluginDeployHook.cs

public class YourPluginDeployHook : IDeployHook
{
    private readonly ILogger<YourPluginDeployHook> _logger;

    public YourPluginDeployHook(ILogger<YourPluginDeployHook> logger) => _logger = logger;

    public string Name => "yourplugin";

    public bool IsApplicable(DeployConfig config)
    {
        var cfg = config.GetPlugin("yourplugin");
        return cfg.IsEnabled && cfg.Get("run_migrations", "false") == "true";
    }

    public async Task ExecuteAsync(ISshService ssh, DeployConfig config, bool isDryRun, CancellationToken ct)
    {
        if (isDryRun)
        {
            _logger.LogDebug("[dry-run] Would run yourplugin post-deploy hook");
            return;
        }

        var deployPath = config.App.DeployPath;
        var result = await ssh.ExecuteAsync(
            $"cd {deployPath} && dotnet YourApp.dll migrate", ct: ct);

        if (!result.Success)
            throw new GantryException(
                $"YourPlugin post-deploy hook failed: {result.Stderr}",
                "Check the migration output above and run gantry rollback if needed.");
    }
}
```

Deploy hooks are **fatal on failure** - a failed hook fails the deploy. The previous release remains available via `gantry rollback`. Design hooks to be fast and reliable. If a hook can fail non-fatally (e.g., a cache warm), catch the exception internally and log a warning rather than rethrowing.

Disable hooks by default and require opt-in via plugin config. Users should not be surprised by extra work happening during deploy.

---

## Step 6 - Register in DI

Add the plugin's types to `Gantry.Infrastructure/Extensions/ServiceCollectionExtensions.cs`:

```csharp
// YourPlugin
services.AddTransient<IPhase, YourPluginInstallPhase>();
services.AddTransient<IPhase, YourPluginConfigurePhase>();
services.AddTransient<IStatusContributor, YourPluginStatusContributor>();
services.AddTransient<IDeployHook, YourPluginDeployHook>(); // if applicable
```

Add the plugin to the registry in `Gantry.Infrastructure/Plugins/PluginRegistry.cs`:

```csharp
["yourplugin"] = new PluginMetadata("One-line description of what it does"),
```

This is the only entry point for `gantry plugin list`.

---

## Step 7 - Wire `gantry plugin add`

In `PluginCommandHandler`, add a case for your plugin name in the `add` flow. This is where you define the interactive prompts and the config keys written to `.deploy.yml`:

```csharp
case "yourplugin":
    pluginConfig = new Dictionary<string, string>
    {
        ["enabled"] = "true",
        ["version"] = AnsiConsole.Prompt(
            new TextPrompt<string>("YourPlugin version?").DefaultValue("1.0")),
        ["some_required_value"] = AnsiConsole.Prompt(
            new TextPrompt<string>("Some required value?")),
    };
    break;
```

The resource warning check (comparing against `ServerInfo.TotalMemoryMb` and `AvailableDiskGb`) runs automatically from `PluginResourceRequirements` declared in the install phase. You do not need to duplicate the warning logic in the command handler.

---

## Step 8 - Write Tests

### Core tests - `Gantry.Core.Tests`

`PluginPhaseBase` itself is already tested. You only need to test your phase's specific logic.

### Infrastructure tests - `Gantry.Infrastructure.Tests`

**Install phase:**

```csharp
[Fact]
public async Task RunAsync_WhenPluginDisabled_ExecutesNoSshCommands()
{
    var ssh = new Mock<ISshService>();
    var context = BuildContext(pluginEnabled: false);

    var phase = new YourPluginInstallPhase(Mock.Of<ILogger<YourPluginInstallPhase>>());
    await phase.ExecuteAsync(context);

    ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task RunAsync_WhenAlreadyActive_SkipsInstall()
{
    var ssh = new Mock<ISshService>();
    ssh.Setup(s => s.ExecuteAsync("systemctl is-active yourplugin 2>/dev/null", null, default))
       .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "active" });

    // assert apt-get install is never called
}

[Fact]
public async Task RunRollbackAsync_RemovesPackage()
{
    // assert apt-get remove is called
}
```

**Status contributor:**

```csharp
[Fact]
public async Task CheckAsync_ServiceRunning_ReturnsPass()
{
    // mock systemctl returning "active", assert Pass result
}

[Fact]
public async Task CheckAsync_ServiceDown_ReturnsFail()
{
    // mock systemctl returning "inactive", assert Fail result with remediation
}

[Fact]
public async Task IsApplicable_PluginNotEnabled_ReturnsFalse()
{
    var config = new DeployConfig(); // no plugins section
    new YourPluginStatusContributor().IsApplicable(config).Should().BeFalse();
}
```

Use the existing `BuildContext` helpers in the test project and mock `ISshService` via Moq. Integration tests that run against the sandbox Docker container should live in a separate test class marked with a custom `[RequiresSandbox]` trait so they are skipped in CI unless the sandbox is running.

---

## Naming Conventions

| Thing | Convention | Example |
|---|---|---|
| Plugin name (registry, config key) | lowercase, hyphens | `my-plugin` |
| Phase name | `{plugin-name}-{action}` | `my-plugin-install` |
| Phase class | `{PluginName}{Action}Phase` | `MyPluginInstallPhase` |
| Config class | `{PluginName}PluginConfig` | `MyPluginPluginConfig` → prefer `MyPluginConfig` |
| Status contributor class | `{PluginName}StatusContributor` | `MyPluginStatusContributor` |
| Deploy hook class | `{PluginName}DeployHook` | `MyPluginDeployHook` |
| Template files | `{plugin-name}-{purpose}.{ext}` | `my-plugin-init.sql` |

---

## Idempotency Checklist

Before shipping a plugin, verify:

- [ ] Running `gantry provision --phase yourplugin-install` twice does not fail or duplicate anything.
- [ ] Running `gantry provision --phase yourplugin-configure` twice does not recreate users, drop databases, or overwrite passwords.
- [ ] `--dry-run` produces no SSH connections and no file mutations.
- [ ] Rollback of install phase leaves the server in a state where install can be re-run cleanly.
- [ ] Status contributor returns results even when the service is down (no uncaught exceptions).

---

## Full Example: Redis Plugin Skeleton

This is what a minimal Redis plugin looks like - no configure phase needed, just install and status.

**`RedisPluginConfig.cs`**

```csharp
public class RedisPluginConfig
{
    public string Version { get; }
    public int Port { get; }

    private RedisPluginConfig(string version, int port)
    {
        Version = version;
        Port = port;
    }

    public static RedisPluginConfig From(PluginConfig config) => new(
        config.Get("version", "7"),
        config.GetInt("port", 6379)
    );
}
```

**`RedisInstallPhase.cs`**

```csharp
public class RedisInstallPhase : PluginPhaseBase
{
    public RedisInstallPhase(ILogger<RedisInstallPhase> logger) : base(logger) { }

    public override string Name => "redis-install";
    public override string Description => "Install Redis";
    public override int Order => 26;

    protected override string PluginName => "redis";
    protected override PluginResourceRequirements? Requirements => new() { MinRamMb = 256 };

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var cfg = RedisPluginConfig.From(GetPluginConfig(context));

        var check = await _ssh.ExecuteAsync("systemctl is-active redis-server 2>/dev/null", ct: ct);
        if (check.Stdout.Trim() == "active") return;

        await RunCommandAsync(_ssh, "apt-get install -y redis-server", context,
            timeout: TimeSpan.FromMinutes(5), ct: ct);
        await RunCommandAsync(_ssh, "systemctl enable redis-server", context, ct: ct);
        await RunCommandAsync(_ssh, "systemctl start redis-server", context, ct: ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        await _ssh.ExecuteAsync("apt-get remove -y redis-server 2>/dev/null || true", ct: ct);
    }
}
```

**`RedisStatusContributor.cs`**

```csharp
public class RedisStatusContributor : IStatusContributor
{
    public string Name => "redis";
    public bool IsApplicable(DeployConfig config) => config.GetPlugin("redis").IsEnabled;

    public async Task<IReadOnlyList<PluginHealthResult>> CheckAsync(
        ISshService ssh, DeployConfig config, CancellationToken ct)
    {
        var results = new List<PluginHealthResult>();

        var active = await ssh.ExecuteAsync("systemctl is-active redis-server", ct: ct);
        results.Add(active.Stdout.Trim() == "active"
            ? new() { Label = "Service running", Status = HealthStatus.Pass }
            : new() { Label = "Service not running", Status = HealthStatus.Fail,
                      Remediation = "sudo systemctl start redis-server" });

        var ping = await ssh.ExecuteAsync("redis-cli ping", ct: ct);
        results.Add(ping.Stdout.Trim() == "PONG"
            ? new() { Label = "Responding to PING", Status = HealthStatus.Pass }
            : new() { Label = "Not responding to PING", Status = HealthStatus.Fail,
                      Remediation = "Check logs: journalctl -u redis-server -n 50" });

        return results;
    }
}
```

**Registration:**

```csharp
services.AddTransient<IPhase, RedisInstallPhase>();
services.AddTransient<IStatusContributor, RedisStatusContributor>();
```

That is the complete plugin. Three files, two registration lines.
