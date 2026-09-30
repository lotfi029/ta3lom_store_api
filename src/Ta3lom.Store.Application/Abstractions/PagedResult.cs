namespace Ta3lom.Store.Application.Abstractions;

/// A page of results returned by queries. The Api maps it to Contracts.Common.PagedResponse.
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
