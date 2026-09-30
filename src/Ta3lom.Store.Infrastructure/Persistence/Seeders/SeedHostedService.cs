using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ta3lom.Store.Infrastructure.Persistence.Seeders;

/// Runs SeedRunner in the background after start-up (`Store:Seed:RunOnStartup`, default true).
/// A database outage does not stop the API: readiness reports it and the next start seeds.
internal sealed partial class SeedHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SeedHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Store:Seed:RunOnStartup", true))
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSeedFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding reference data failed")]
    private static partial void LogSeedFailed(ILogger logger, Exception exception);
}
