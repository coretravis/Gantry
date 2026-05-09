using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Orchestration;

public class PhaseOrchestrator : IPhaseOrchestrator
{
    private readonly IEnumerable<IPhase> _phases;
    private readonly ILogger<PhaseOrchestrator> _logger;

    public PhaseOrchestrator(IEnumerable<IPhase> phases, ILogger<PhaseOrchestrator> logger)
    {
        _phases = phases.OrderBy(p => p.Order).ToList();
        _logger = logger;
    }

    public async Task RunAsync(ProvisioningContext context, IReadOnlyList<string>? skipPhases = null, CancellationToken ct = default)
    {
        var phases = _phases.ToList();
        var skipped = skipPhases?.Select(s => s.ToLowerInvariant()).ToHashSet() ?? [];
        var total = phases.Count(p => !skipped.Contains(p.Name.ToLowerInvariant()));
        var current = 0;

        // Build a lookup of phase name -> number for use during rollback
        var phaseNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Starting provisioning pipeline: {Count} phases", total);

        foreach (var phase in phases)
        {
            ct.ThrowIfCancellationRequested();

            if (skipped.Contains(phase.Name.ToLowerInvariant()))
            {
                _logger.LogDebug("Skipping phase: {Phase}", phase.Name);
                continue;
            }

            current++;
            phaseNumbers[phase.Name] = current;
            context.CurrentPhaseNumber = current;
            context.TotalPhases = total;
            context.Progress?.Report(new PhaseProgress
            {
                PhaseName = phase.Name,
                Status = PhaseStatus.Starting,
                Message = phase.Description,
                PhaseNumber = current,
                TotalPhases = total
            });

            try
            {
                await phase.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Pipeline cancelled during phase {Phase}", phase.Name);
                await RollbackCompletedPhasesAsync(context, phases, phaseNumbers, ct);
                throw;
            }
            catch (GantryException ex) when (!phase.IsRequired)
            {
                // Optional phases (e.g. SSL) failing should not abort provisioning.
                // Record the failure and continue so remaining phases still run.
                _logger.LogWarning(ex, "Optional phase {Phase} failed: {Message}", phase.Name, ex.Message);
                context.FailedOptionalPhases.Add((phase.Name, ex.Message));
            }
            catch (GantryException ex)
            {
                _logger.LogError(ex, "Phase {Phase} failed: {Message}", phase.Name, ex.Message);
                if (ex.Remediation != null)
                    _logger.LogInformation("Suggested fix: {Remediation}", ex.Remediation);

                await RollbackCompletedPhasesAsync(context, phases, phaseNumbers, ct);
                throw;
            }
        }

        _logger.LogInformation("Provisioning pipeline completed successfully");
    }

    private async Task RollbackCompletedPhasesAsync(
        ProvisioningContext context,
        List<IPhase> allPhases,
        Dictionary<string, int> phaseNumbers,
        CancellationToken ct)
    {
        var completed = context.CompletedPhases.ToHashSet();
        var toRollback = allPhases
            .Where(p => completed.Contains(p.Name))
            .OrderByDescending(p => p.Order)
            .ToList();

        if (toRollback.Count == 0) return;

        _logger.LogInformation("Rolling back {Count} completed phases", toRollback.Count);

        foreach (var phase in toRollback)
        {
            // Set phase number so the RollingBack report shows the correct (X/Y) counter
            if (phaseNumbers.TryGetValue(phase.Name, out var num))
                context.CurrentPhaseNumber = num;

            try
            {
                await phase.RollbackAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback of phase {Phase} failed (best-effort, continuing)", phase.Name);
            }
        }
    }
}
