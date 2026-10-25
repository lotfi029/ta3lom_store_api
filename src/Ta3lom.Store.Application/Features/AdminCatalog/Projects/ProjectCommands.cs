using FluentValidation;
using Ta3lom.Store.Application.Abstractions.Wire;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Projects;

/// Loads what a project write needs: its category and cover (R-8/D-06: the cover must be a public image).
internal sealed class ProjectRefs(ICategoryRepository categories, IGenericRepository<MediaAsset> media)
{
    public async Task<Result<(Category Category, MediaAsset? Cover)>> ResolveAsync(Guid categoryId, Guid? coverAssetId, CancellationToken ct)
    {
        var category = await categories.GetByIdAsync(categoryId, ct);
        if (category is null)
        {
            return CatalogErrors.CategoryNotFound;
        }

        MediaAsset? cover = null;
        if (coverAssetId is { } id)
        {
            cover = await media.GetByIdAsync(id, ct);
            if (cover is not { IsPublicImage: true })
            {
                return CatalogErrors.ImageRequired;
            }
        }

        return (category, cover);
    }

    public static ProjectDetails Details(ProjectInput i) =>
        new(i.Slug, i.TitleAr, i.TitleEn, i.SummaryAr, i.SummaryEn, i.BodyAr, i.BodyEn, i.AllowOpenAmount,
            i.MinAmount is null ? null : MoneyInput.Parse(i.MinAmount), i.Badge is null ? null : EnumWire.Parse<ProjectBadge>(i.Badge), i.Sort,
            i.SeoTitleAr, i.SeoTitleEn, i.SeoDescAr, i.SeoDescEn);
}

/// `POST /v1/admin/projects` (BR-A02): a new draft.
public sealed record CreateProjectCommand(ProjectInput Input) : ICommand<AdminProjectResponse>;

internal sealed class CreateProjectCommandHandler(IProjectRepository projects, ProjectRefs refs, AdminProjectReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<CreateProjectCommand, AdminProjectResponse>
{
    public async Task<Result<AdminProjectResponse>> HandleAsync(CreateProjectCommand command, CancellationToken ct = default)
    {
        if (await projects.SlugInUseAsync(command.Input.Slug, null, ct))
        {
            return CatalogErrors.ProjectSlugTaken;
        }

        var resolved = await refs.ResolveAsync(command.Input.CategoryId, command.Input.CoverAssetId, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var project = Project.CreateDraft(ProjectRefs.Details(command.Input), resolved.Value.Category, resolved.Value.Cover, clock.UtcNow);
        projects.Add(project);
        await uow.SaveChangesAsync(ct);
        return (await reader.GetAsync(project.Id, ct))!;
    }
}

/// `PUT /v1/admin/projects/{id}` with `version` (409 on a stale one). A slug change of a published project keeps the old
/// slug for redirects (D-08); a published project must stay publishable (D-06).
public sealed record UpdateProjectCommand(Guid Id, ProjectInput Input) : ICommand<AdminProjectResponse>;

internal sealed class UpdateProjectCommandHandler(IProjectRepository projects, ProjectRefs refs, AdminProjectReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<UpdateProjectCommand, AdminProjectResponse>
{
    public async Task<Result<AdminProjectResponse>> HandleAsync(UpdateProjectCommand command, CancellationToken ct = default)
    {
        var project = await projects.GetForUpdateAsync(command.Id, ct);
        if (project is null)
        {
            return CatalogErrors.ProjectNotFound;
        }

        if (!project.IsVersion(command.Input.Version ?? 0))
        {
            return GeneralErrors.ConcurrencyConflict;
        }

        if (await projects.SlugInUseAsync(command.Input.Slug, project.Id, ct))
        {
            return CatalogErrors.ProjectSlugTaken;
        }

        var resolved = await refs.ResolveAsync(command.Input.CategoryId, command.Input.CoverAssetId, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var updated = project.Update(ProjectRefs.Details(command.Input), resolved.Value.Category, resolved.Value.Cover, clock.UtcNow);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await uow.SaveChangesAsync(ct);
        return (await reader.GetAsync(project.Id, ct))!;
    }
}

/// `PUT /v1/admin/projects/{id}/options` (BR-A03): replaces the list; options already used in an order are deactivated,
/// not deleted.
public sealed record ReplaceProjectOptionsCommand(Guid Id, ProjectOptionsInput Input) : ICommand<AdminProjectResponse>;

internal sealed class ReplaceProjectOptionsCommandHandler(IProjectRepository projects, AdminProjectReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<ReplaceProjectOptionsCommand, AdminProjectResponse>
{
    public async Task<Result<AdminProjectResponse>> HandleAsync(ReplaceProjectOptionsCommand command, CancellationToken ct = default)
    {
        var project = await projects.GetForUpdateAsync(command.Id, ct);
        if (project is null)
        {
            return CatalogErrors.ProjectNotFound;
        }

        if (!project.IsVersion(command.Input.Version))
        {
            return GeneralErrors.ConcurrencyConflict;
        }

        var used = await projects.OptionIdsUsedInOrdersAsync(project.Id, ct);
        var inputs = command.Input.Options.Select(o => new ProjectOptionInput(o.Id, o.LabelAr, o.LabelEn, MoneyInput.Parse(o.Amount), o.Sort, o.IsActive)).ToList();
        var replaced = project.ReplaceOptions(inputs, used, clock.UtcNow);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await uow.SaveChangesAsync(ct);
        return (await reader.GetAsync(project.Id, ct))!;
    }
}

/// `POST /v1/admin/projects/{id}/publish` (D-06, D-07): 422 `project.publish_incomplete` with `missing[]` when a requirement
/// is not met; draft → published and archived → published.
public sealed record PublishProjectCommand(Guid Id) : ICommand<AdminProjectResponse>;

internal sealed class PublishProjectCommandHandler(IProjectRepository projects, ICategoryRepository categories, IGenericRepository<MediaAsset> media,
    AdminProjectReader reader, IUnitOfWork uow, IClock clock) : ICommandHandler<PublishProjectCommand, AdminProjectResponse>
{
    public async Task<Result<AdminProjectResponse>> HandleAsync(PublishProjectCommand command, CancellationToken ct = default)
    {
        var project = await projects.GetForUpdateAsync(command.Id, ct);
        if (project is null)
        {
            return CatalogErrors.ProjectNotFound;
        }

        var category = await categories.GetByIdAsync(project.CategoryId, ct);
        var cover = project.CoverAssetId is { } coverId ? await media.GetByIdAsync(coverId, ct) : null;
        var published = project.Publish(category, cover, clock.UtcNow);
        if (published.IsFailure)
        {
            return published.Error;
        }

        await uow.SaveChangesAsync(ct);
        return (await reader.GetAsync(project.Id, ct))!;
    }
}

/// `POST /v1/admin/projects/{id}/archive` (D-07): published → archived.
public sealed record ArchiveProjectCommand(Guid Id) : ICommand<AdminProjectResponse>;

internal sealed class ArchiveProjectCommandHandler(IProjectRepository projects, AdminProjectReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<ArchiveProjectCommand, AdminProjectResponse>
{
    public async Task<Result<AdminProjectResponse>> HandleAsync(ArchiveProjectCommand command, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(command.Id, ct);
        if (project is null)
        {
            return CatalogErrors.ProjectNotFound;
        }

        var archived = project.Archive(clock.UtcNow);
        if (archived.IsFailure)
        {
            return archived.Error;
        }

        await uow.SaveChangesAsync(ct);
        return (await reader.GetAsync(project.Id, ct))!;
    }
}

internal sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator() => RuleFor(c => c.Input).SetValidator(new ProjectInputValidator());
}

internal sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(c => c.Input).SetValidator(new ProjectInputValidator());
        RuleFor(c => c.Input.Version).NotNull().WithMessage("version is required.");
    }
}

internal sealed class ReplaceProjectOptionsCommandValidator : AbstractValidator<ReplaceProjectOptionsCommand>
{
    public ReplaceProjectOptionsCommandValidator()
    {
        RuleFor(c => c.Input.Options).NotNull().Must(o => o.Count <= 50).WithMessage("At most 50 options.")
            .Must(o => o.Select(x => x.LabelAr.Trim()).Distinct(StringComparer.Ordinal).Count() == o.Count).WithMessage("Option labels must be unique.")
            .Must(o => o.Where(x => x.Id is not null).GroupBy(x => x.Id).All(g => g.Count() == 1)).WithMessage("Option ids must be unique.");
        RuleForEach(c => c.Input.Options).ChildRules(o =>
        {
            o.RuleFor(x => x.LabelAr).NotEmpty().Length(2, ProjectOption.LabelMax);
            o.RuleFor(x => x.LabelEn).MaximumLength(ProjectOption.LabelMax);
            o.RuleFor(x => x.Amount).Must(MoneyInput.IsPositive).WithMessage("amount must be a positive \"50.00\" string.");
            o.RuleFor(x => x.Sort).InclusiveBetween(0, 10_000);
        });
    }
}

internal sealed class ProjectInputValidator : AbstractValidator<ProjectInput>
{
    public ProjectInputValidator()
    {
        RuleFor(i => i.CategoryId).NotEmpty();
        RuleFor(i => i.Slug).NotEmpty().Must(Slug.IsValidProject).WithMessage("slug must match ^[a-z0-9-]{2,80}$.");
        RuleFor(i => i.TitleAr).NotEmpty().Length(2, Project.TitleMax);
        RuleFor(i => i.TitleEn).MaximumLength(Project.TitleMax);
        RuleFor(i => i.SummaryAr).MaximumLength(Project.SummaryMax);
        RuleFor(i => i.SummaryEn).MaximumLength(Project.SummaryMax);
        RuleFor(i => i.BodyAr).MaximumLength(20_000);
        RuleFor(i => i.BodyEn).MaximumLength(20_000);
        RuleFor(i => i.MinAmount).Must(m => m is null || MoneyInput.IsPositive(m)).WithMessage("minAmount must be a positive \"10.00\" string.");
        RuleFor(i => i.Badge).Must(b => b is null or "new" or "urgent").WithMessage("badge must be new or urgent.");
        RuleFor(i => i.Sort).InclusiveBetween(0, 10_000);
        RuleFor(i => i.SeoTitleAr).MaximumLength(160);
        RuleFor(i => i.SeoTitleEn).MaximumLength(160);
        RuleFor(i => i.SeoDescAr).MaximumLength(300);
        RuleFor(i => i.SeoDescEn).MaximumLength(300);
    }
}
