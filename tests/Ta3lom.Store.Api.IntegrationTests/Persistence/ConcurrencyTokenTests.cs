using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// xmin is the optimistic concurrency token on the hot rows (database/01 §2).
public sealed class ConcurrencyTokenTests
{
    [Fact]
    public async Task Concurrent_update_of_the_same_goal_counter_is_detected()
    {
        var cs = await StoreSchema.CreateMigratedAsync("xmin");
        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("002_store_seed.sql"));
        var ct = TestContext.Current.CancellationToken;

        await using var first = StoreSchema.CreateContext(cs);
        await using var second = StoreSchema.CreateContext(cs);
        var a = await first.Set<GoalProgress>().FirstAsync(ct);
        var b = await second.Set<GoalProgress>().SingleAsync(g => g.ProductId == a.ProductId, ct);

        first.Entry(a).Property(x => x.DonorCount).CurrentValue = a.DonorCount + 1;
        await first.SaveChangesAsync(ct);
        second.Entry(b).Property(x => x.DonorCount).CurrentValue = b.DonorCount + 1;

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData(typeof(Order))]
    [InlineData(typeof(BookFormat))]
    [InlineData(typeof(SponsoredUnit))]
    [InlineData(typeof(SponsorshipPool))]
    [InlineData(typeof(GoalProgress))]
    public void Hot_rows_use_xmin(Type entity)
    {
        using var db = StoreSchema.CreateContext("Host=localhost;Database=unused");

        var token = db.Model.FindEntityType(entity)!.GetProperties().Single(p => p.IsConcurrencyToken);

        token.GetColumnName().ShouldBe("xmin");
    }
}
