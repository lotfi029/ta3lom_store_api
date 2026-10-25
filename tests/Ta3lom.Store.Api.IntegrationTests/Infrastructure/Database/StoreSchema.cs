using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.Infrastructure.Persistence.Seeders;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

/// Builds databases the way environments do: from the idempotent migration script (the same artifact as
/// `dotnet ef migrations script --idempotent`, what the deploy job runs).
public static class StoreSchema
{
    /// Fixed for the whole run so every host can read what another protected (TOTP secrets, cart cookies).
    public static readonly string DataProtectionKeysPath = Path.Combine(Path.GetTempPath(), $"ta3lom-store-test-keys-{Environment.ProcessId}");

    public static string IdempotentMigrationScript()
    {
        using var context = CreateContext("Host=localhost;Database=unused");
        return context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
    }

    public static async Task<string> CreateMigratedAsync(string purpose)
    {
        var server = await TestDatabaseServer.GetAsync();
        var cs = await server.CreateDatabaseAsync(purpose);
        await SqlRunner.ExecuteAsync(cs, IdempotentMigrationScript());
        await SqlRunner.ReloadTypesAsync(cs);
        return cs;
    }

    public static StoreDbContext CreateContext(string connectionString, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>();
        StoreDbContextOptions.Configure(options, connectionString);
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new StoreDbContext(options.Options);
    }

    public static async Task<int> SeedAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        return await new SeedRunner(context, NullLogger<SeedRunner>.Instance).RunAsync(TestContext.Current.CancellationToken);
    }
}

/// The database every WebApplicationFactory uses by default: migrated and seeded with the reference data.
public static class SharedStoreDatabase
{
    private static readonly Lazy<Task<string>> Instance = new(async () =>
    {
        var cs = await StoreSchema.CreateMigratedAsync("app");
        await StoreSchema.SeedAsync(cs);
        return cs;
    });

    public static string ConnectionString => Instance.Value.GetAwaiter().GetResult();
}
