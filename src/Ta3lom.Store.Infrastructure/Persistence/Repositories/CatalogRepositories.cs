using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository(StoreDbContext db) : GenericRepository<Category>(db), ICategoryRepository
{
    public Task<bool> SlugInUseAsync(string slug, Guid? exceptId, CancellationToken ct = default) =>
        Set.AnyAsync(c => c.Slug == slug && c.Id != exceptId, ct);
}

internal sealed class ProjectRepository(StoreDbContext db) : GenericRepository<Project>(db), IProjectRepository
{
    public Task<Project?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(p => p.Options).Include(p => p.SlugHistory).AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<bool> SlugInUseAsync(string slug, Guid? exceptProjectId, CancellationToken ct = default) =>
        await Set.AnyAsync(p => p.Slug == slug && p.Id != exceptProjectId, ct)
        || await Db.ProjectSlugHistory.AnyAsync(h => h.OldSlug == slug && h.ProjectId != exceptProjectId, ct);

    public async Task<IReadOnlySet<Guid>> OptionIdsUsedInOrdersAsync(Guid projectId, CancellationToken ct = default) =>
        (await Db.OrderLines.AsNoTracking().Where(l => l.ProjectId == projectId && l.OptionId != null)
            .Select(l => l.OptionId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
}
