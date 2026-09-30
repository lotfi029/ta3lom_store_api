using Microsoft.EntityFrameworkCore;

namespace Ta3lom.Store.Infrastructure.Persistence;

/// One place for the provider settings, shared by the app, the design-time factory and the tests.
public static class StoreDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(StoreDbContext.MigrationsHistoryTable, StoreDbContext.Schema)
                .MigrationsAssembly(typeof(StoreDbContext).Assembly.GetName().Name)
                .EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention();
    }
}
