using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// The deploy job runs the idempotent script on every release (database/03 §2); running it again must be a no-op.
public sealed class MigrationScriptTests
{
    [Fact]
    public async Task Idempotent_script_runs_twice_without_changes()
    {
        var cs = await StoreSchema.CreateMigratedAsync("twice");
        var first = await SchemaSnapshot.TakeAsync(cs);

        await SqlRunner.ExecuteAsync(cs, StoreSchema.IdempotentMigrationScript());

        (await SchemaSnapshot.TakeAsync(cs)).ShouldBe(first);
        using var db = StoreSchema.CreateContext(cs);
        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.__ef_migrations_history")).ShouldBe(db.Database.GetMigrations().Count());
    }

    /// The EF model and the last migration agree: a configuration change without `dotnet ef migrations add`
    /// fails here, and SchemaParityTests then checks the migrations against the reference DDL.
    [Fact]
    public void Model_has_no_changes_missing_a_migration()
    {
        using var db = StoreSchema.CreateContext("Host=localhost;Database=unused");

        db.Database.HasPendingModelChanges().ShouldBeFalse("Run: dotnet ef migrations add <Intent> -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o Persistence/Migrations");
    }

    [Fact]
    public void Script_never_creates_the_uuidv7_shim() =>
        StoreSchema.IdempotentMigrationScript().ShouldNotContain("FUNCTION public.uuidv7", Case.Insensitive);
}
