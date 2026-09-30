using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Operations;

/// Runs the embedded `db/reference/003_integrity_checks.sql` (one source for staff tooling and the nightly job): each
/// statement becomes `SELECT count(*) FROM (…) q`. Read-only.
internal sealed partial class OperationsStatus(StoreDbContext db) : IOperationsStatus
{
    private static readonly Lazy<IReadOnlyList<(int Number, string Title, string Sql)>> Checks = new(Load);

    public async Task<IReadOnlyList<IntegrityCheckResult>> RunIntegrityChecksAsync(CancellationToken ct = default)
    {
        var results = new List<IntegrityCheckResult>();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET search_path = store, public", ct);            // as the script does
            foreach (var (number, title, sql) in Checks.Value)
            {
                var query = "SELECT count(*)::int AS \"Value\" FROM (" + sql + ") q";              // trusted embedded SQL, no input
                var count = (await db.Database.SqlQueryRaw<int>(query).ToListAsync(ct)).Single();
                results.Add(new IntegrityCheckResult(number, title, count));
            }
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET search_path", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }

        return results;
    }

    public async Task<int> DeadOutboxCountAsync(CancellationToken ct = default) =>
        (await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM store.outbox_message WHERE processed_at IS NULL AND next_attempt_at = 'infinity'::timestamptz")
            .ToListAsync(ct)).Single();

    private static List<(int Number, string Title, string Sql)> Load()
    {
        using var stream = typeof(OperationsStatus).Assembly.GetManifestResourceStream("Integrity.003_integrity_checks.sql")
                           ?? throw new InvalidOperationException("integrity checks resource missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var checks = new List<(int, string, string)>();
        var title = string.Empty;
        var number = 0;
        var sql = new StringBuilder();
        foreach (var raw in reader.ReadToEnd().Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Heading().Match(line) is { Success: true } h)
            {
                number = int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
                title = h.Groups[2].Value.Trim();
                continue;
            }

            var code = line.Contains("--", StringComparison.Ordinal) ? line[..line.IndexOf("--", StringComparison.Ordinal)] : line;
            if (string.IsNullOrWhiteSpace(code) || code.TrimStart().StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sql.Append(code).Append('\n');
            if (code.TrimEnd().EndsWith(';'))
            {
                var statement = sql.ToString().Trim().TrimEnd(';');
                checks.Add((number, title, statement));
                sql.Clear();
            }
        }

        return checks;
    }

    [GeneratedRegex(@"^--\s*(\d+)\.\s*(.+)$")]
    private static partial Regex Heading();
}
