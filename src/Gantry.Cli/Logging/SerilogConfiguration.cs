using Serilog;
using Serilog.Events;

namespace Gantry.Cli.Logging;

public static class SerilogConfiguration
{
    public static ILogger BuildLogger(bool verbose, bool noColor)
    {
        var logDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".gantry", "logs");
        Directory.CreateDirectory(logDir);

        var consoleLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Warning;

        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDir, "gantry-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(
                restrictedToMinimumLevel: consoleLevel,
                outputTemplate: noColor
                    ? "[{Level:u3}] {Message:lj}{NewLine}{Exception}"
                    : "[{Level:u3}] {Message:lj}{NewLine}{Exception}");

        return config.CreateLogger();
    }
}
