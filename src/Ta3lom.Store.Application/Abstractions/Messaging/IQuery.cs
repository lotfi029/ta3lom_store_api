namespace Ta3lom.Store.Application.Abstractions.Messaging;

/// A read-only use case (projections through IApplicationDbContext, `AsNoTracking`).
public interface IQuery<TResponse> : IBaseQuery;

public interface IBaseQuery;
