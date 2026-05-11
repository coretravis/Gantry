using Gantry.Core.Interfaces;
using Gantry.Core.Orchestration;
using Gantry.Core.Phases;
using Gantry.Core.Validation;
using Gantry.Infrastructure.CiGenerators;
using Gantry.Infrastructure.Config;
using Gantry.Infrastructure.Github;
using Gantry.Infrastructure.Plugins.Postgres;
using Gantry.Infrastructure.ProcessManagers;
using Gantry.Infrastructure.Runtimes;
using Gantry.Infrastructure.SslProviders;
using Gantry.Infrastructure.Ssh;
using Gantry.Infrastructure.State;
using Gantry.Infrastructure.Templates;
using Gantry.Infrastructure.WebServers;
using Microsoft.Extensions.DependencyInjection;

namespace Gantry.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGantryInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISshService, SshService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddTransient<IStateService, StateService>();
        services.AddTransient<IGithubService, GithubService>();
        services.AddSingleton<ITemplateEngine, TemplateEngine>();
        services.AddSingleton<DeployConfigValidator>();

        services.AddTransient<IRuntimeProvider, DotNetRuntimeProvider>();
        services.AddTransient<IWebServer, NginxWebServer>();
        services.AddTransient<IProcessManager, SystemdProcessManager>();
        services.AddTransient<ISslProvider, CertbotSslProvider>();
        services.AddTransient<ICiGenerator, GithubActionsGenerator>();

        // Core provisioning phases (orders 10–80)
        services.AddTransient<IPhase, ConnectAndVerifyPhase>();
        services.AddTransient<IPhase, OsHardeningPhase>();
        services.AddTransient<IPhase, RuntimeInstallationPhase>();
        services.AddTransient<IPhase, WebServerConfigurationPhase>();
        services.AddTransient<IPhase, ProcessManagerSetupPhase>();
        services.AddTransient<IPhase, SslProvisioningPhase>();
        services.AddTransient<IPhase, CiGenerationPhase>();
        services.AddTransient<IPhase, ConfigPersistencePhase>();

        // PostgreSQL plugin (orders 25, 35)
        services.AddTransient<IPhase, PostgresInstallPhase>();
        services.AddTransient<IPhase, PostgresConfigurePhase>();
        services.AddTransient<IStatusContributor, PostgresStatusContributor>();
        services.AddTransient<IPreDeployHook, PostgresMigrationHook>();

        services.AddTransient<IPhaseOrchestrator, PhaseOrchestrator>();

        return services;
    }
}
