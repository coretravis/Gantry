using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class OsHardeningPhase : PhaseBase
{
    private readonly ISshService _ssh;

    public OsHardeningPhase(ISshService ssh, ILogger<OsHardeningPhase> logger)
        : base(logger) => _ssh = ssh;

    public override string Name => "os-hardening";
    public override string Description => "Update OS, create deploy user, configure SSH and firewall";
    public override int Order => 20;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var server = context.Config.Server;

        Report(context, PhaseStatus.Running, "Updating package lists and upgrading OS...");
        await RunCommandAsync(_ssh, "DEBIAN_FRONTEND=noninteractive apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y -qq", context,
            timeout: TimeSpan.FromMinutes(10), ct: ct);

        Report(context, PhaseStatus.Running, $"Creating deploy user '{server.DeployUser}'...");
        var userExists = context.IsDryRun
            ? CommandResult.DryRun($"id {server.DeployUser} 2>/dev/null")
            : await _ssh.ExecuteAsync($"id {server.DeployUser} 2>/dev/null", ct: ct);
        if (!userExists.Success)
        {
            await RunCommandAsync(_ssh, $"adduser --disabled-password --gecos '' {server.DeployUser}", context, ct: ct);
            await RunCommandAsync(_ssh, $"usermod -aG sudo {server.DeployUser}", context, ct: ct);
        }
        else
        {
            Logger.LogDebug("Deploy user '{User}' already exists, skipping creation", server.DeployUser);
        }

        Report(context, PhaseStatus.Running, "Installing deploy SSH key...");
        await RunCommandAsync(_ssh, $"mkdir -p /home/{server.DeployUser}/.ssh && chmod 700 /home/{server.DeployUser}/.ssh", context, ct: ct);
        if (context.GeneratedDeployKeyPublic != null)
        {
            // grep -qF ensures the key is only added once even if provision runs multiple times
            var key = context.GeneratedDeployKeyPublic.Replace("'", "'\\''");
            await RunCommandAsync(_ssh,
                $"grep -qF '{key}' /home/{server.DeployUser}/.ssh/authorized_keys 2>/dev/null || echo '{key}' >> /home/{server.DeployUser}/.ssh/authorized_keys",
                context, ct: ct);
            await RunCommandAsync(_ssh, $"chmod 600 /home/{server.DeployUser}/.ssh/authorized_keys && chown -R {server.DeployUser}:{server.DeployUser} /home/{server.DeployUser}/.ssh", context, ct: ct);
        }

        Report(context, PhaseStatus.Running, "Hardening SSH configuration...");
        await HardenSshAsync(context, ct);

        Report(context, PhaseStatus.Running, "Configuring UFW firewall...");
        await ConfigureFirewallAsync(context, ct);

        Report(context, PhaseStatus.Running, "Installing fail2ban...");
        await RunCommandAsync(_ssh,
            "dpkg -s fail2ban &>/dev/null && systemctl is-enabled fail2ban &>/dev/null || (apt-get install -y fail2ban && systemctl enable --now fail2ban)",
            context, ct: ct);

        Report(context, PhaseStatus.Running, $"Setting timezone to {server.Timezone}...");
        if (!context.IsDryRun)
        {
            var currentTz = await _ssh.ExecuteAsync("timedatectl show --property=Timezone --value", ct: ct);
            if (currentTz.Stdout.Trim() != server.Timezone)
                await RunCommandAsync(_ssh, $"timedatectl set-timezone {server.Timezone}", context, ct: ct);
            else
                Logger.LogDebug("Timezone already set to {Timezone}, skipping", server.Timezone);
        }

        Report(context, PhaseStatus.Running, "Enabling unattended security upgrades...");
        await RunCommandAsync(_ssh,
            "dpkg -s unattended-upgrades &>/dev/null || (apt-get install -y unattended-upgrades && dpkg-reconfigure -f noninteractive unattended-upgrades)",
            context, ct: ct);
    }

    private async Task HardenSshAsync(ProvisioningContext context, CancellationToken ct)
    {
        const string sshdConfig = "/etc/ssh/sshd_config";
        var commands = new[]
        {
            $"sed -i 's/^#\\?PasswordAuthentication.*/PasswordAuthentication no/' {sshdConfig}",
            $"sed -i 's/^#\\?PermitRootLogin.*/PermitRootLogin prohibit-password/' {sshdConfig}",
            $"sed -i 's/^#\\?PubkeyAuthentication.*/PubkeyAuthentication yes/' {sshdConfig}",
            "systemctl reload ssh"
        };
        foreach (var cmd in commands)
            await RunCommandAsync(_ssh, cmd, context, ct: ct);
    }

    private async Task ConfigureFirewallAsync(ProvisioningContext context, CancellationToken ct)
    {
        var sshPort = context.Config.Server.Port;

        // Install UFW if absent (idempotent)
        await RunCommandAsync(_ssh, "dpkg -s ufw &>/dev/null || apt-get install -y ufw", context, ct: ct);

        // Defaults are idempotent; re-applying them is safe and fast
        await RunCommandAsync(_ssh, "ufw default deny incoming 2>/dev/null || true", context, ct: ct);
        await RunCommandAsync(_ssh, "ufw default allow outgoing 2>/dev/null || true", context, ct: ct);

        // Only add rules that are not already present
        await RunCommandAsync(_ssh,
            $"ufw status | grep -q '{sshPort}/tcp' || ufw allow {sshPort}/tcp comment 'SSH'",
            context, ct: ct);
        await RunCommandAsync(_ssh,
            "ufw status | grep -q '80/tcp' || ufw allow 80/tcp",
            context, ct: ct);
        await RunCommandAsync(_ssh,
            "ufw status | grep -q '443/tcp' || ufw allow 443/tcp",
            context, ct: ct);

        // Enable only if not already active
        await RunCommandAsync(_ssh,
            "ufw status | grep -q 'Status: active' || ufw --force enable",
            context, ct: ct);

        Logger.LogDebug("UFW configured: SSH({SshPort}), HTTP(80), HTTPS(443)", sshPort);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        var deployUser = context.Config.Server.DeployUser;
        Logger.LogInformation("Rolling back OS hardening: removing user {User}", deployUser);

        if (!context.IsDryRun)
        {
            await _ssh.ExecuteAsync($"deluser --remove-home {deployUser} 2>/dev/null || true", ct: ct);
            await _ssh.ExecuteAsync("ufw --force disable 2>/dev/null || true", ct: ct);
        }
    }
}
