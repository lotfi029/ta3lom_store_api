using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Time;

namespace Ta3lom.Store.Infrastructure.Persistence.Seeders;

/// Inserts missing reference data only (`ON CONFLICT DO NOTHING`): safe to run on every start and on
/// several containers at once (database/03 §3). Never changes a value staff have edited.
public sealed partial class SeedRunner(StoreDbContext db, IClock clock, ILogger<SeedRunner> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var year = clock.RiyadhToday.Year;
        var inserted = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            inserted = 0;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var (key, json) in SeedData.Settings)
            {
                inserted += await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO store.setting (key, value, updated_by) VALUES ({key}, CAST({json} AS jsonb), 'seed') ON CONFLICT (key) DO NOTHING", ct);
            }

            foreach (var (key, channel, locale, subject, body) in SeedData.Templates)
            {
                inserted += await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO store.message_template (key, channel, locale, subject, body) VALUES ({key}, {channel}, {locale}, {subject}, {body}) ON CONFLICT (key, channel, locale) DO NOTHING", ct);
            }

            foreach (var series in SeedData.DocumentSeries)
            {
                inserted += await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO store.document_counter (series, year) VALUES ({series}, {year}) ON CONFLICT (series, year) DO NOTHING", ct);
            }

            await tx.CommitAsync(ct);
        });

        LogSeeded(logger, inserted);
        return inserted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seed completed: {Inserted} missing rows inserted")]
    private static partial void LogSeeded(ILogger logger, int inserted);
}
