using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Domain.IRepositories;

public interface ICategoryRepository : IGenericRepository<Category>
{
    Task<bool> SlugInUseAsync(string slug, Guid? exceptId, CancellationToken ct = default);
}

public interface IProjectRepository : IGenericRepository<Project>
{
    /// The project with its options and slug history (tracked).
    Task<Project?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    /// D-08: the slug is used by another project, or kept by any project's slug history (except the project's own).
    Task<bool> SlugInUseAsync(string slug, Guid? exceptProjectId, CancellationToken ct = default);

    /// Options of the project that appear on an order line (they are deactivated, never deleted).
    Task<IReadOnlySet<Guid>> OptionIdsUsedInOrdersAsync(Guid projectId, CancellationToken ct = default);
}
