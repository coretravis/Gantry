using Gantry.Core.Models;
using Spectre.Console;

namespace Gantry.Cli.UI;

public static class ConsoleRenderer
{
    public static void ShowBanner()
    {
        AnsiConsole.Write(new FigletText("Gantry").Color(Color.SteelBlue1));
        AnsiConsole.MarkupLine("[grey]Server provisioning & CI/CD generation for .NET[/]");
        AnsiConsole.MarkupLine("[grey]v1.0.0[/]");
        AnsiConsole.WriteLine();
    }

    public static void ShowSuccess(string message) =>
        AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(message)}");

    public static void ShowWarning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(message)}");

    public static void ShowError(string message) =>
        AnsiConsole.MarkupLine($"[red]![/] {Markup.Escape(message)}");

    public static void ShowInfo(string message) =>
        AnsiConsole.MarkupLine($"[blue]>[/] {Markup.Escape(message)}");

    public static void ShowPhaseProgress(PhaseProgress progress)
    {
        var (icon, indent) = progress.PhaseNumber > 0
            ? (progress.Status switch
            {
                PhaseStatus.Completed   => "[green]OK[/]",
                PhaseStatus.Failed      => "[red]FAIL[/]",
                PhaseStatus.Skipped     => "[grey]SKIP[/]",
                PhaseStatus.RollingBack => "[yellow]UNDO[/]",
                PhaseStatus.Warning     => "[yellow]WARN[/]",
                _                       => "[blue]....[/]"
            }, $"[grey]({progress.PhaseNumber}/{progress.TotalPhases})[/] ")
            : (progress.Status switch
            {
                PhaseStatus.Completed   => "[green]OK[/]",
                PhaseStatus.Failed      => "[red]FAIL[/]",
                PhaseStatus.RollingBack => "[yellow]UNDO[/]",
                PhaseStatus.Warning     => "[yellow]WARN[/]",
                _                       => "[blue]    [/]"
            }, "     ");

        AnsiConsole.MarkupLine($"  {icon} {indent}{Markup.Escape(progress.Message)}");
    }

    public static void ShowServerInfo(ServerInfo info)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("Hostname", info.Hostname);
        table.AddRow("OS", $"{info.OsName} {info.OsVersion}");
        table.AddRow("Architecture", info.Architecture);
        table.AddRow("Memory", $"{info.AvailableMemoryMb}MB free / {info.TotalMemoryMb}MB total");
        table.AddRow("Disk", $"{info.AvailableDiskGb}GB free / {info.TotalDiskGb}GB total");
        table.AddRow("Connected as", info.ConnectedUser);
        AnsiConsole.Write(table);
    }

    public static void ShowDeployConfig(DeployConfig config)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("Server", config.Server.Host);
        table.AddRow("App name", config.App.Name);
        table.AddRow("Runtime", $"{config.Runtime.Ecosystem} {config.Runtime.Version}");
        table.AddRow("Domain", config.Domain.HasDomain ? config.Domain.Name : "(none)");
        table.AddRow("SSL", config.Domain.Ssl ? "yes" : "no");
        table.AddRow("CI platform", config.Ci.Platform);
        table.AddRow("Branch", config.Ci.Branch);
        AnsiConsole.Write(table);
    }

    public static void ShowSummary(string title, IEnumerable<(string Label, string Value)> rows)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(title)}[/]"));
        foreach (var (label, value) in rows)
            AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(label)}:[/] {Markup.Escape(value)}");
        AnsiConsole.WriteLine();
    }

    public static void ShowHealthReport(HealthReport report, string serviceStatus, string logs)
    {
        AnsiConsole.WriteLine();

        // Summary banner
        var (summaryColor, summaryLabel) = report.Overall switch
        {
            HealthStatus.Healthy  => ("green",  "HEALTHY"),
            HealthStatus.Warning  => ("yellow", "DEGRADED"),
            HealthStatus.Critical => ("red",    "CRITICAL"),
            _                     => ("grey",   "UNKNOWN")
        };

        var warningCount  = report.Checks.Count(c => c.Status == HealthStatus.Warning);
        var criticalCount = report.Checks.Count(c => c.Status == HealthStatus.Critical);
        var detail = report.Overall == HealthStatus.Healthy
            ? "All checks passed"
            : $"{criticalCount} critical, {warningCount} warning";

        AnsiConsole.MarkupLine($"[bold {summaryColor}]Status: {summaryLabel}[/]  [grey]{Markup.Escape(report.AppName)} on {Markup.Escape(report.ServerHost)} - {detail}[/]");
        AnsiConsole.WriteLine();

        // Component table
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("[bold]Component[/]");
        table.AddColumn("[bold]Status[/]");
        table.AddColumn("[bold]Detail[/]");

        foreach (var check in report.Checks)
        {
            var (icon, color) = check.Status switch
            {
                HealthStatus.Healthy       => ("✓", "green"),
                HealthStatus.Warning       => ("!", "yellow"),
                HealthStatus.Critical      => ("✗", "red"),
                HealthStatus.NotApplicable => ("-", "grey"),
                _                          => ("?", "grey")
            };

            var detailMarkup = Markup.Escape(check.Detail);
            if (check.Remediation != null && check.Status != HealthStatus.Healthy)
                detailMarkup += $"\n[grey italic]→ {Markup.Escape(check.Remediation)}[/]";

            table.AddRow(
                Markup.Escape(check.Component),
                $"[{color}]{icon}[/]",
                detailMarkup);
        }

        AnsiConsole.Write(table);

        // Service status and logs
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Service Status[/]").LeftJustified());
        AnsiConsole.WriteLine(serviceStatus);

        AnsiConsole.Write(new Rule("[bold]Recent Logs[/]").LeftJustified());
        foreach (var line in logs.Split('\n'))
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("error") || lower.Contains("fail") || lower.Contains("exception"))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(line)}[/]");
            else if (lower.Contains("warn"))
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(line)}[/]");
            else
                AnsiConsole.WriteLine(line);
        }
        AnsiConsole.WriteLine();
    }
}
