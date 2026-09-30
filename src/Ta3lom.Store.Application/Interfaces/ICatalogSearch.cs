namespace Ta3lom.Store.Application.Interfaces;

/// Arabic-aware product search (FR-S03, 11 §2.3): `search_vector @@ plainto_tsquery('simple', ar_normalize(q))`
/// with a trigram fallback on `ar_normalize(title_ar)`. Raw SQL, so it lives in Infrastructure (K-c).
public interface ICatalogSearch
{
    /// Ids of published products matching <paramref name="query"/>, best match first.
    Task<IReadOnlyList<Guid>> SearchPublishedAsync(string query, CancellationToken ct = default);
}

/// Public URLs of stored files (covers, update images): `{Store:Files:PublicBaseUrl}/{storage_key}`.
public interface IFileUrls
{
    string Public(string storageKey);
}
