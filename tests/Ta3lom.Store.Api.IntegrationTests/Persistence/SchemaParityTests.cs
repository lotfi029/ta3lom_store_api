using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// The EF migrations and db/reference/001_store_schema.sql build the same schema (plan 14 S-02):
/// tables, columns, types, nullability, defaults, CHECKs, keys, indexes, views, functions, sequences.
public sealed class SchemaParityTests
{
    [Fact]
    public async Task Migrations_build_exactly_the_reference_schema()
    {
        var reference = await SchemaSnapshot.TakeAsync(await StoreSchema.CreateFromReferenceAsync("ref"));
        var migrated = await SchemaSnapshot.TakeAsync(await StoreSchema.CreateMigratedAsync("mig"));

        migrated.SequenceEqual(reference, StringComparer.Ordinal).ShouldBeTrue(SchemaSnapshot.Diff(reference, migrated));
    }

    [Fact]
    public async Task Snapshot_covers_the_whole_reference_schema()
    {
        var reference = await SchemaSnapshot.TakeAsync(await StoreSchema.CreateFromReferenceAsync("ref_count"));

        reference.Count(l => l.StartsWith("table ", StringComparison.Ordinal) && l.EndsWith(" r", StringComparison.Ordinal)).ShouldBe(49);
        reference.Count(l => l.StartsWith("view ", StringComparison.Ordinal)).ShouldBe(4);
        reference.ShouldContain(l => l.StartsWith("function ar_normalize(t text) text volatility=i parallel=s", StringComparison.Ordinal));
        reference.ShouldContain("sequence order_ref_seq bigint start=1000 increment=1 min=1 max=9223372036854775807 cycle=false");
        reference.ShouldContain("extension citext schema=store");
        reference.ShouldContain("extension pg_trgm schema=store");
    }
}
