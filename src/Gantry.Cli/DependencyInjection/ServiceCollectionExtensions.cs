using Gantry.Cli.Commands;
using Gantry.Core.Validation;
using Gantry.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Gantry.Cli.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGantryCli(this IServiceCollection services, Serilog.ILogger serilogLogger)
    {
        services.AddLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSerilog(serilogLogger, dispose: true);
        });

        services.AddGantryInfrastructure();

        services.AddTransient<InitCommandHandler>();
        services.AddTransient<ProvisionCommandHandler>();
        services.AddTransient<CiCommandHandler>();
        services.AddTransient<DeployCommandHandler>();
        services.AddTransient<RollbackCommandHandler>();
        services.AddTransient<StatusCommandHandler>();
        services.AddTransient<ConfigCommandHandler>();
        services.AddTransient<EnvCommandHandler>();
        services.AddTransient<SandboxCommandHandler>();
        services.AddTransient<PluginCommandHandler>();

        return services;
    }
}
