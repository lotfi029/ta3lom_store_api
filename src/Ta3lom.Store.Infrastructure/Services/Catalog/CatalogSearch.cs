using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Catalog;

/// FR-S03: full-text on the stored `search_vector` (built with `ar_normalize`) with a trigram fallback on the
/// normalised Arabic title, so ة/ه, ى/ي, alef forms and tashkeel all match (11 §2.3).
internal sealed class CatalogSearch(StoreDbContext db) : ICatalogSearch
{
    public async Task<IReadOnlyList<Guid>> SearchPublishedAsync(string query, CancellationToken ct = default) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT p.id AS "Value"
              FROM store.product p
             WHERE p.status = 'published'
               AND (p.search_vector @@ plainto_tsquery('simple', store.ar_normalize({query}))
                    OR store.ar_normalize(p.title_ar) OPERATOR(store.%) store.ar_normalize({query})
                    OR store.ar_normalize(p.title_ar) LIKE '%' || store.ar_normalize({query}) || '%')
             ORDER BY ts_rank(p.search_vector, plainto_tsquery('simple', store.ar_normalize({query}))) DESC,
                      store.similarity(store.ar_normalize(p.title_ar), store.ar_normalize({query})) DESC
             LIMIT 200
            """).ToListAsync(ct);
}
