namespace Ta3lom.Store.Application.Interfaces;

/// Arabic-aware project search (BR-A06): full text on `search_vector` with a trigram and substring fallback on the
/// normalised Arabic title, so ة/ه, ى/ي, alef forms and tashkeel all match. Raw SQL, so it lives in Infrastructure.
public interface ICatalogSearch
{
    /// Ids of matching projects with the given status (`published` for the public listing), best match first, at most 200.
    Task<IReadOnlyList<Guid>> SearchAsync(string query, string? status, CancellationToken ct = default);
}
