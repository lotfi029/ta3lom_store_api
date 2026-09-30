using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Application.Features.Giving;

/// Cause delivery updates (#81–#84; FR-S59, RUL-54). Public ones show on the product page (#6); `notifyDonors` sends
/// `CauseUpdatePublished` once, then the flag is read-only and the update cannot be deleted. Audited (staff actor).
public sealed record ListCauseUpdatesQuery(Guid ProductId, int Page, int PageSize) : IQuery<PagedResult<AdminCauseUpdate>>;

public sealed record PostCauseUpdateCommand(Guid ProductId, CauseUpdateInput Input) : ICommand<AdminCauseUpdate>;

public sealed record EditCauseUpdateCommand(Guid UpdateId, CauseUpdateInput Input) : ICommand<AdminCauseUpdate>;

public sealed record DeleteCauseUpdateCommand(Guid UpdateId) : ICommand;

internal sealed class CauseUpdateHandlers(IApplicationDbContext db, IGenericRepository<CauseUpdate> updates, IFileUrls files, ICurrentActor actor,
    IUnitOfWork uow, IClock clock) :
    IQueryHandler<ListCauseUpdatesQuery, PagedResult<AdminCauseUpdate>>,
    ICommandHandler<PostCauseUpdateCommand, AdminCauseUpdate>,
    ICommandHandler<EditCauseUpdateCommand, AdminCauseUpdate>,
    ICommandHandler<DeleteCauseUpdateCommand>
{
    public async Task<Result<PagedResult<AdminCauseUpdate>>> HandleAsync(ListCauseUpdatesQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.CauseUpdates.AsNoTracking().Where(u => u.ProductId == query.ProductId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(u => u.OccurredOn).ThenByDescending(u => u.CreatedAt).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        var result = new List<AdminCauseUpdate>();
        foreach (var u in rows)
        {
            result.Add(await ToResponseAsync(u, ct));
        }

        return new PagedResult<AdminCauseUpdate>(result, total, page.SafePage, page.Take);
    }

    public async Task<Result<AdminCauseUpdate>> HandleAsync(PostCauseUpdateCommand command, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == command.ProductId, ct))
        {
            return Error.NotFound(ErrorCodes.ProductNotFound, "Product not found.");
        }

        if (await DetailsAsync(command.Input, ct) is not { } details)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["assetId"] = ["unknown media asset"] });
        }

        var u = CauseUpdate.Post(command.ProductId, details, actor.Name ?? "staff", clock.UtcNow);
        updates.Add(u);
        await uow.SaveChangesAsync(ct);
        return await ToResponseAsync(u, ct);
    }

    public async Task<Result<AdminCauseUpdate>> HandleAsync(EditCauseUpdateCommand command, CancellationToken ct = default)
    {
        var u = await updates.GetByIdAsync(command.UpdateId, ct);
        if (u is null)
        {
            return GivingErrors.CauseUpdateNotFound;
        }

        if (await DetailsAsync(command.Input, ct) is not { } details)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["assetId"] = ["unknown media asset"] });
        }

        u.Edit(details, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return await ToResponseAsync(u, ct);
    }

    public async Task<Result> HandleAsync(DeleteCauseUpdateCommand command, CancellationToken ct = default)
    {
        var u = await updates.GetByIdAsync(command.UpdateId, ct);
        if (u is null)
        {
            return GivingErrors.CauseUpdateNotFound;
        }

        if (!u.CanDelete)
        {
            return GivingErrors.CauseUpdateAlreadyNotified;
        }

        updates.Remove(u);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<CauseUpdateDetails?> DetailsAsync(CauseUpdateInput i, CancellationToken ct)
    {
        if (i.AssetId is { } asset && !await db.MediaAssets.AnyAsync(m => m.Id == asset && m.IsPublic, ct))
        {
            return null;
        }

        return new CauseUpdateDetails(i.TitleAr!.Trim(), Clean(i.TitleEn), Clean(i.BodyAr), Clean(i.BodyEn), i.AssetId, i.UnitsDelivered,
            i.OccurredOn!.Value, i.IsPublic, i.NotifyDonors);
    }

    private async Task<AdminCauseUpdate> ToResponseAsync(CauseUpdate u, CancellationToken ct)
    {
        ImageResponse? image = null;
        if (u.AssetId is { } id && await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct) is { } m)
        {
            image = new ImageResponse(files.Public(m.StorageKey), m.AltAr, m.AltEn, m.Width, m.Height);
        }

        return new AdminCauseUpdate(u.Id, u.ProductId, u.TitleAr, u.TitleEn, u.BodyAr, u.BodyEn, image, u.UnitsDelivered, u.OccurredOn, u.IsPublic,
            u.NotifyDonors, u.NotifiedAt, u.CreatedAt, u.CreatedBy);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

internal sealed class CauseUpdateInputValidator : AbstractValidator<CauseUpdateInput>
{
    public CauseUpdateInputValidator()
    {
        RuleFor(i => i.TitleAr).NotEmpty().Length(2, 200);
        RuleFor(i => i.TitleEn).MaximumLength(200);
        RuleFor(i => i.BodyAr).MaximumLength(2000);
        RuleFor(i => i.BodyEn).MaximumLength(2000);
        RuleFor(i => i.UnitsDelivered).GreaterThanOrEqualTo(0);
        RuleFor(i => i.OccurredOn).NotNull();
    }
}

internal sealed class PostCauseUpdateCommandValidator : AbstractValidator<PostCauseUpdateCommand>
{
    public PostCauseUpdateCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new CauseUpdateInputValidator());
}

internal sealed class EditCauseUpdateCommandValidator : AbstractValidator<EditCauseUpdateCommand>
{
    public EditCauseUpdateCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new CauseUpdateInputValidator());
}
