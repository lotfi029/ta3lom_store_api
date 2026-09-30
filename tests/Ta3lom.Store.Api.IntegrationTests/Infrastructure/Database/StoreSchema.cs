using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.Infrastructure.Persistence.Seed;
using Ta3lom.Store.Infrastructure.Security;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

/// Builds databases the way environments do: from the EF migrations (the idempotent script, the same
/// artifact as `dotnet ef migrations script --idempotent`) or from db/reference/001_store_schema.sql.
public static class StoreSchema
{
    /// Fixed for the whole run so every host and context can read what another encrypted.
    public static readonly string PiiHashKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

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

    public static async Task<string> CreateFromReferenceAsync(string purpose)
    {
        var server = await TestDatabaseServer.GetAsync();
        var cs = await server.CreateDatabaseAsync(purpose);
        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("001_store_schema.sql"));
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

        return new StoreDbContext(options.Options, CreatePiiProtector());
    }

    public static IPiiProtector CreatePiiProtector() =>
        new PiiProtector(
            DataProtectionProvider.Create(new DirectoryInfo(DataProtectionKeysPath), b => b.SetApplicationName("ta3lom-store")),
            Microsoft.Extensions.Options.Options.Create(new PiiOptions { MobileHashKeyBase64 = PiiHashKeyBase64 }));

    public static async Task<int> SeedAsync(string connectionString, FakeClock? clock = null)
    {
        await using var context = CreateContext(connectionString);
        return await new SeedRunner(context, clock ?? new FakeClock(), NullLogger<SeedRunner>.Instance).RunAsync(TestContext.Current.CancellationToken);
    }
}

/// The database every WebApplicationFactory uses: migrated, catalogue from 002, reference data from SeedRunner.
public static class SharedStoreDatabase
{
    private static readonly Lazy<Task<string>> Instance = new(async () =>
    {
        var cs = await StoreSchema.CreateMigratedAsync("app");
        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("002_store_seed.sql"));
        await StoreSchema.SeedAsync(cs);
        return cs;
    });

    public static string ConnectionString => Instance.Value.GetAwaiter().GetResult();
}
