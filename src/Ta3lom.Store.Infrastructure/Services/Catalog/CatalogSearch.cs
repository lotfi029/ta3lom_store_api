using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Catalog;

/// Full text on the stored `search_vector` (built with `ar_normalize`), with a trigram similarity and a substring fallback
/// on the normalised Arabic title. The query is a parameter everywhere; for the substring match its LIKE wildcards
/// (`%`, `_`, `\`) are escaped, so "50%" searches for the text "50%" and not for anything. Status, category and paging are
/// applied in the same statement (L-11): the count and the page come from one filtered set.
internal sealed class CatalogSearch(StoreDbContext db) : ICatalogSearch
{
    private sealed record Hit(Guid Id, long Total);

    public async Task<CatalogSearchPage> SearchPageAsync(CatalogSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.Query.Trim();
        var pattern = "%" + EscapeLike(query) + "%";
        var hits = await db.Database.SqlQuery<Hit>($"""
            WITH matches AS (
                SELECT p.id, p.sort, p.created_at,
                       ts_rank(p.search_vector, plainto_tsquery('simple', store.ar_normalize({query}))) AS rank,
                       public.similarity(store.ar_normalize(p.title_ar), store.ar_normalize({query})) AS similarity
                  FROM store.project p
                  JOIN store.category c ON c.id = p.category_id
                 WHERE ({request.Status}::text IS NULL OR p.status = {request.Status})
                   AND ({request.CategorySlug}::text IS NULL OR c.slug = {request.CategorySlug})
                   AND ({request.CategoryId}::uuid IS NULL OR p.category_id = {request.CategoryId})
                   AND (NOT {request.ActiveCategoryOnly} OR c.is_active)
                   AND (p.search_vector @@ plainto_tsquery('simple', store.ar_normalize({query}))
                        OR store.ar_normalize(p.title_ar) OPERATOR(public.%) store.ar_normalize({query})
                        OR store.ar_normalize(p.title_ar) LIKE store.ar_normalize({pattern}) ESCAPE '\'
                        OR lower(coalesce(p.title_en, '')) LIKE lower({pattern}) ESCAPE '\'))
            SELECT m.id AS "Id", (SELECT count(*) FROM matches) AS "Total"
              FROM matches m
             ORDER BY m.rank DESC, m.similarity DESC, m.sort, m.created_at, m.id
             OFFSET {request.Skip} LIMIT {request.Take}
            """).ToListAsync(ct);
        var total = hits.Count > 0
            ? (int)hits[0].Total
            : await CountAsync(request, query, pattern, ct);
        return new CatalogSearchPage([.. hits.Select(h => h.Id)], total);
    }

    /// The total when the requested page is past the last match.
    private async Task<int> CountAsync(CatalogSearchRequest request, string query, string pattern, CancellationToken ct) =>
        (await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value"
              FROM store.project p
              JOIN store.category c ON c.id = p.category_id
             WHERE ({request.Status}::text IS NULL OR p.status = {request.Status})
               AND ({request.CategorySlug}::text IS NULL OR c.slug = {request.CategorySlug})
               AND ({request.CategoryId}::uuid IS NULL OR p.category_id = {request.CategoryId})
               AND (NOT {request.ActiveCategoryOnly} OR c.is_active)
               AND (p.search_vector @@ plainto_tsquery('simple', store.ar_normalize({query}))
                    OR store.ar_normalize(p.title_ar) OPERATOR(public.%) store.ar_normalize({query})
                    OR store.ar_normalize(p.title_ar) LIKE store.ar_normalize({pattern}) ESCAPE '\'
                    OR lower(coalesce(p.title_en, '')) LIKE lower({pattern}) ESCAPE '\')
            """).ToListAsync(ct))[0];

    public static string EscapeLike(string text) => text.Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
}
