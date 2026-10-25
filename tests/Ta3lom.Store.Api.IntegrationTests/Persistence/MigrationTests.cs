using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// The single `InitialSchema` migration is the source of truth of the schema (with the EF model). The deploy job runs the
/// idempotent script on every release, so running it twice must be a no-op.
public sealed class MigrationTests
{
    private static readonly string[] Tables =
    [
        "audit_log", "cart", "cart_line", "category", "customer", "idempotency_key", "media_asset", "message_template", "notification",
        "order_line", "orders", "otp_challenge", "outbox_message", "project", "project_option", "project_slug_history", "refresh_token",
        "setting", "staff_user",
    ];

    [Fact]
    public async Task The_migration_applies_on_postgres_18_and_creates_the_19_tables()
    {
        var cs = await StoreSchema.CreateMigratedAsync("mig");

        var version = await SqlRunner.ScalarAsync<int>(cs, "SELECT current_setting('server_version_num')::int");
        var server = await TestDatabaseServer.GetAsync();
        if (!server.IsExternal)
        {
            version.ShouldBeGreaterThanOrEqualTo(180000);
        }

        var tables = (await SqlRunner.QueryAllAsync(cs,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'store' AND table_type = 'BASE TABLE' AND table_name <> '__ef_migrations_history' ORDER BY 1")).Single();
        tables.ShouldBe(Tables);
        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM information_schema.views WHERE table_schema = 'store' AND table_name = 'v_catalog_stats'")).ShouldBe(1);
        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM pg_sequences WHERE schemaname = 'store' AND sequencename = 'order_ref_seq'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_idempotent_script_runs_twice()
    {
        var cs = await StoreSchema.CreateMigratedAsync("twice");

        await SqlRunner.ExecuteAsync(cs, StoreSchema.IdempotentMigrationScript());

        using var db = StoreSchema.CreateContext(cs);
        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.__ef_migrations_history")).ShouldBe(db.Database.GetMigrations().Count());
        db.Database.GetMigrations().ShouldHaveSingleItem().ShouldEndWith("_InitialSchema");
    }

    /// `dotnet ef migrations has-pending-model-changes`: a configuration change without a migration fails here.
    [Fact]
    public void The_model_has_no_changes_missing_a_migration()
    {
        using var db = StoreSchema.CreateContext("Host=localhost;Database=unused");

        db.Database.HasPendingModelChanges().ShouldBeFalse("Run: dotnet ef migrations add <Intent> -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o Persistence/Migrations");
    }

    [Fact]
    public async Task Ar_normalize_is_immutable_and_folds_arabic_spelling()
    {
        var cs = await StoreSchema.CreateMigratedAsync("arnorm");

        (await SqlRunner.ScalarAsync<string>(cs, "SELECT provolatile::text FROM pg_proc WHERE proname = 'ar_normalize'")).ShouldBe("i");
        (await SqlRunner.ScalarAsync<string>(cs, "SELECT store.ar_normalize('أَحْمَدُ إسلام آمنة ٱلقرى مدرسةٌ مستشفى ـتطويل')"))
            .ShouldBe("احمد اسلام امنه القري مدرسه مستشفي تطويل");
    }

    [Fact]
    public async Task The_search_vector_holds_the_normalised_title_and_summary()
    {
        var cs = await StoreSchema.CreateMigratedAsync("tsv");
        await SqlRunner.ExecuteAsync(cs, $"""
            INSERT INTO store.category (id, slug, name_ar, accepts_general_gifts, sort, is_active, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', 'dawah', 'الدعوة', true, 1, true, now(), now());
            INSERT INTO store.project (id, category_id, slug, status, title_ar, title_en, summary_ar, allow_open_amount, sort, created_at, updated_at)
            SELECT '{Guid.CreateVersion7()}', id, 'umrah', 'draft', 'العُمرة الدعويّة', 'Dawah Umrah', 'تكلفة المعتمر', true, 1, now(), now() FROM store.category;
            """);

        (await SqlRunner.ScalarAsync<string>(cs, "SELECT search_vector::text FROM store.project")).ShouldBe("'dawah':3 'umrah':4 'الدعويه':2 'العمره':1 'المعتمر':6 'تكلفه':5");
        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.project WHERE search_vector @@ plainto_tsquery('simple', store.ar_normalize('العمرة'))")).ShouldBe(1);
    }

    [Fact]
    public async Task The_line_checks_refuse_a_line_with_two_targets()
    {
        var cs = await StoreSchema.CreateMigratedAsync("checks");

        var error = await Should.ThrowAsync<Npgsql.PostgresException>(() => SqlRunner.ExecuteAsync(cs, $"""
            INSERT INTO store.cart (id, created_at, updated_at) VALUES ('{Guid.Empty}', now(), now());
            INSERT INTO store.cart_line (id, cart_id, target_kind, quantity, unit_amount, amount, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', '{Guid.Empty}', 'category', 2, 10, 20, now(), now());
            """));

        error.ConstraintName.ShouldBe("ck_line_target");
    }
}
