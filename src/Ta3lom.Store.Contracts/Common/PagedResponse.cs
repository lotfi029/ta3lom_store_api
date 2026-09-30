namespace Ta3lom.Store.Contracts.Common;

/// List response shape (11 §1.4): `{ "items": [...], "total": 57, "page": 1, "pageSize": 20 }`.
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
