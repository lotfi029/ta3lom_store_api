namespace Ta3lom.Store.Application.Interfaces;

/// Arabic-aware project search (BR-A06): full text on `search_vector` with a trigram and substring fallback on the
/// normalised Arabic title, so ة/ه, ى/ي, alef forms and tashkeel all match. Raw SQL, so it lives in Infrastructure.
public interface ICatalogSearch
{
    /// One page of matching project ids, best match first, with the total of all matches. Every filter and the paging are
    /// applied in the same SQL (L-11), so the total and the pages are right whatever the number of matches.
    Task<CatalogSearchPage> SearchPageAsync(CatalogSearchRequest request, CancellationToken ct = default);
}

/// `Status` null = any status; `CategorySlug` / `CategoryId` filter one category; `ActiveCategoryOnly` keeps projects of
/// active categories (the public listing).
public sealed record CatalogSearchRequest(string Query, string? Status, string? CategorySlug, Guid? CategoryId, bool ActiveCategoryOnly, int Skip, int Take);

public sealed record CatalogSearchPage(IReadOnlyList<Guid> Ids, int Total);
