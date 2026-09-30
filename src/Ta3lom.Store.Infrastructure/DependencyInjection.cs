using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Infrastructure.Time;

namespace Ta3lom.Store.Infrastructure;

public static class DependencyInjection
{
    /// Adapters for the Application ports. S-01 wires the clock and an empty Quartz scheduler;
    /// persistence (S-02), SMS (S-04), email and documents (S-17) and the others follow in their sessions.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, SystemClock>();

        // Jobs (09 §6) are registered by the sessions that own them. RAM store, in-process.
        services.AddQuartz();
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        return services;
    }
}
