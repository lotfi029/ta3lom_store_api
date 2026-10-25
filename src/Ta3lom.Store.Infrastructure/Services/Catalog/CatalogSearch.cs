using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Catalog;

/// Full text on the stored `search_vector` (built with `ar_normalize`), with a trigram similarity and a substring fallback
/// on the normalised Arabic title. The query is a parameter everywhere; for the substring match its LIKE wildcards
/// (`%`, `_`, `\`) are escaped, so "50%" searches for the text "50%" and not for anything.
internal sealed class CatalogSearch(StoreDbContext db) : ICatalogSearch
{
    public const int Limit = 200;

    public async Task<IReadOnlyList<Guid>> SearchAsync(string query, string? status, CancellationToken ct = default)
    {
        var pattern = "%" + EscapeLike(query.Trim()) + "%";
        return await db.Database.SqlQuery<Guid>($"""
            SELECT p.id AS "Value"
              FROM store.project p
             WHERE ({status}::text IS NULL OR p.status = {status})
               AND (p.search_vector @@ plainto_tsquery('simple', store.ar_normalize({query}))
                    OR store.ar_normalize(p.title_ar) OPERATOR(public.%) store.ar_normalize({query})
                    OR store.ar_normalize(p.title_ar) LIKE store.ar_normalize({pattern}) ESCAPE '\'
                    OR lower(coalesce(p.title_en, '')) LIKE lower({pattern}) ESCAPE '\')
             ORDER BY ts_rank(p.search_vector, plainto_tsquery('simple', store.ar_normalize({query}))) DESC,
                      public.similarity(store.ar_normalize(p.title_ar), store.ar_normalize({query})) DESC,
                      p.sort, p.id
             LIMIT {Limit}
            """).ToListAsync(ct);
    }

    public static string EscapeLike(string text) => text.Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
}
