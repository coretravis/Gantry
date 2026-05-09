using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public abstract class PhaseBase : IPhase
{
    protected readonly ILogger Logger;

    protected PhaseBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract int Order { get; }
    public virtual bool IsRequired => true;

    public virtual async Task ExecuteAsync(ProvisioningContext context, CancellationToken ct = default)
    {
        Logger.LogInformation("Phase {Phase} starting", Name);

        try
        {
            await ValidatePrerequisitesAsync(context, ct);
            await RunAsync(context, ct);
            context.CompletedPhases.Add(Name);
            Report(context, PhaseStatus.Completed, Name);
            Logger.LogInformation("Phase {Phase} completed", Name);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Phase {Phase} cancelled", Name);
            throw;
        }
        catch (GantryException)
        {
            Report(context, PhaseStatus.Failed, Name);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Phase {Phase} failed unexpectedly", Name);
            Report(context, PhaseStatus.Failed, Name);
            throw new PhaseException(Name, $"Phase '{Name}' failed: {ex.Message}", ex);
        }
    }

    public virtual async Task RollbackAsync(ProvisioningContext context, CancellationToken ct = default)
    {
        Logger.LogInformation("Phase {Phase} rollback starting", Name);
        Report(context, PhaseStatus.RollingBack, $"Rolling back: {Name}");
        await RunRollbackAsync(context, ct);
        Logger.LogInformation("Phase {Phase} rollback completed", Name);
    }

    protected abstract Task RunAsync(ProvisioningContext context, CancellationToken ct);

    protected virtual Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Override to validate that server-side prerequisites exist before RunAsync is entered.
    /// Only meaningful when a phase is run in isolation via --phase; during normal pipeline
    /// flow the preceding phases guarantee the prerequisites.
    /// </summary>
    protected virtual Task ValidatePrerequisitesAsync(ProvisioningContext context, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Downloads the current content of <paramref name="remotePath"/>, compares it to
    /// <paramref name="content"/>, and uploads only when the content differs.
    /// Returns <c>true</c> if the file was uploaded (caller should trigger any necessary reload),
    /// <c>false</c> if the file was identical and the upload was skipped.
    /// </summary>
    protected async Task<bool> UploadIfChangedAsync(
        ISshService ssh,
        string content,
        string remotePath,
        ProvisioningContext context,
        CancellationToken ct)
    {
        if (context.IsDryRun)
        {
            Logger.LogDebug("[dry-run] {File} - would upload", remotePath);
            return true;
        }

        if (await ssh.FileExistsAsync(remotePath, ct))
        {
            var current = await ssh.DownloadStringAsync(remotePath, ct);
            if (current == content)
            {
                Logger.LogDebug("{File} - content unchanged, skipping upload", remotePath);
                return false;
            }
        }

        await ssh.UploadStringAsync(content, remotePath, ct);
        Logger.LogDebug("{File} - uploaded ({Bytes} bytes)", remotePath, System.Text.Encoding.UTF8.GetByteCount(content));
        return true;
    }

    protected void Report(ProvisioningContext context, PhaseStatus status, string message)
    {
        context.Progress?.Report(new PhaseProgress
        {
            PhaseName = Name,
            Status = status,
            Message = message,
            PhaseNumber = status is PhaseStatus.Completed or PhaseStatus.Failed or PhaseStatus.RollingBack
                ? context.CurrentPhaseNumber
                : 0,
            TotalPhases = context.TotalPhases
        });
    }

    protected async Task<CommandResult> RunCommandAsync(
        ISshService ssh,
        string command,
        ProvisioningContext context,
        string? errorMessage = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (context.IsDryRun)
        {
            Logger.LogDebug("[dry-run] {Command}", command);
            return CommandResult.DryRun(command);
        }

        Logger.LogDebug("Executing: {Command}", command);
        var result = await ssh.ExecuteAsync(command, timeout, ct);
        Logger.LogDebug("Exit {ExitCode} ({Duration}ms): {Command}", result.ExitCode, result.Duration.TotalMilliseconds, command);

        if (!result.Success)
        {
            var msg = errorMessage ?? $"Command failed: {command}";
            Logger.LogError("Command failed (exit {ExitCode}): {Command}\nStderr: {Stderr}", result.ExitCode, command, result.Stderr);
            throw new SshException(command, result.ExitCode, result.Stderr, $"Check server logs for details.");
        }

        return result;
    }
}
