using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.Donations;

internal static class MeMapping
{
    public static MeResponse ToMe(Customer c) =>
        new(c.Id, c.Mobile, c.FullName, c.Email, c.Locale, c.MarketingOptIn, c.Status.ToWire(), c.PdplConsentAt, c.CreatedAt);

    public static DonationDetail ToDetail(Order o) => new(o.Reference, o.Status.ToWire(), o.TotalAmount.ToWire(), o.DonorName, o.DonorEmail, o.CreatedAt,
        o.PaymentDueAt, o.CancelledAt,
        [.. o.Lines.OrderBy(l => l.LineNo).Select(l => new DonationLineResponse(l.LineNo, l.TargetKind.ToWire(), l.ProjectId, l.CategoryId, l.TitleAr, l.TitleEn,
            l.OptionLabelAr, l.OptionLabelEn, l.Quantity, l.UnitAmount.ToWire(), l.Amount.ToWire()))]);
}

/// `GET /v1/me` (BR-A10).
public sealed record GetMeQuery(Guid CustomerId) : IQuery<MeResponse>;

internal sealed class GetMeQueryHandler(IApplicationDbContext db) : IQueryHandler<GetMeQuery, MeResponse>
{
    public async Task<Result<MeResponse>> HandleAsync(GetMeQuery query, CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.CustomerId && c.Status != CustomerStatus.Anonymised, ct) is { } customer
            ? MeMapping.ToMe(customer)
            : GeneralErrors.NotFound;
}

/// `PATCH /v1/me`: only an active donor writes (D-13).
public sealed record UpdateMeCommand(Guid CustomerId, UpdateMeRequest Request) : ICommand<MeResponse>;

internal sealed class UpdateMeCommandHandler(ICustomerRepository customers, IUnitOfWork uow, IClock clock) : ICommandHandler<UpdateMeCommand, MeResponse>
{
    public async Task<Result<MeResponse>> HandleAsync(UpdateMeCommand command, CancellationToken ct = default)
    {
        var customer = await customers.GetByIdAsync(command.CustomerId, ct);
        if (customer is not { IsActive: true })
        {
            return AuthErrors.AccountBlocked;
        }

        var r = command.Request;
        customer.UpdateProfile(r.FullName, r.Email, r.Locale, r.MarketingOptIn, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return MeMapping.ToMe(customer);
    }
}

internal sealed class UpdateMeCommandValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeCommandValidator()
    {
        RuleFor(c => c.Request.FullName).MaximumLength(120);
        RuleFor(c => c.Request.Email).EmailAddress().MaximumLength(254).When(c => !string.IsNullOrEmpty(c.Request.Email));
        RuleFor(c => c.Request.Locale).Must(l => l is null or "ar" or "en").WithMessage("locale must be ar or en.");
    }
}

/// `DELETE /v1/me` (D-14): the donor is anonymised, the contact snapshot of their orders too (amounts stay), every session
/// is revoked and the cart is deleted. Only an active donor can do it (D-13).
public sealed record DeleteMeCommand(Guid CustomerId) : ICommand;

internal sealed class DeleteMeCommandHandler(ICustomerRepository customers, IOrderRepository orders, ICartRepository carts,
    IRefreshTokenRepository refreshTokens, IAuditTrail audit, IUnitOfWork uow, IClock clock) : ICommandHandler<DeleteMeCommand>
{
    public Task<Result> HandleAsync(DeleteMeCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => DeleteAsync(command.CustomerId, token), ct);

    private async Task<Result> DeleteAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(customerId, ct);
        if (customer is not { IsActive: true })
        {
            return AuthErrors.AccountBlocked;
        }

        var now = clock.UtcNow;
        customer.Anonymise(now);
        foreach (var order in await orders.ForCustomerAsync(customerId, ct))
        {
            order.AnonymiseDonor(now);
        }

        if (await carts.GetForCustomerForUpdateAsync(customerId, ct) is { } cart)
        {
            carts.Remove(cart);
        }

        await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Customer, customerId, now, ct);
        audit.Record(AuditActions.CustomerDeleted, "customer", customerId.ToString());
        return Result.Success();
    }
}

/// `GET /v1/me/export` (PDPL): the profile and every order with its lines.
public sealed record ExportMeQuery(Guid CustomerId) : IQuery<MeExportResponse>;

internal sealed class ExportMeQueryHandler(IApplicationDbContext db, IClock clock) : IQueryHandler<ExportMeQuery, MeExportResponse>
{
    public async Task<Result<MeExportResponse>> HandleAsync(ExportMeQuery query, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.CustomerId && c.Status != CustomerStatus.Anonymised, ct);
        if (customer is null)
        {
            return GeneralErrors.NotFound;
        }

        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines).Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        return new MeExportResponse(MeMapping.ToMe(customer), [.. orders.Select(MeMapping.ToDetail)], clock.UtcNow);
    }
}

/// `GET /v1/me/donations` (newest first, paged).
public sealed record ListMyDonationsQuery(Guid CustomerId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<DonationSummary>>;

internal sealed class ListMyDonationsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListMyDonationsQuery, PagedResult<DonationSummary>>
{
    public async Task<Result<PagedResult<DonationSummary>>> HandleAsync(ListMyDonationsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var mine = db.Orders.AsNoTracking().Where(o => o.CustomerId == query.CustomerId);
        var total = await mine.CountAsync(ct);
        var rows = await mine.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Skip(page.Skip).Take(page.Take)
            .Select(o => new { o.Reference, o.Status, o.TotalAmount, o.LineCount, o.CreatedAt }).ToListAsync(ct);
        return new PagedResult<DonationSummary>(
            [.. rows.Select(r => new DonationSummary(r.Reference, r.Status.ToWire(), r.TotalAmount.ToWire(), r.LineCount, r.CreatedAt))],
            total, page.SafePage, page.Take);
    }
}

internal sealed class ListMyDonationsQueryValidator : AbstractValidator<ListMyDonationsQuery>
{
    public ListMyDonationsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

/// `GET /v1/me/donations/{reference}`: one of the donor's own orders.
public sealed record GetMyDonationQuery(Guid CustomerId, string Reference) : IQuery<DonationDetail>;

internal sealed class GetMyDonationQueryHandler(IApplicationDbContext db) : IQueryHandler<GetMyDonationQuery, DonationDetail>
{
    public async Task<Result<DonationDetail>> HandleAsync(GetMyDonationQuery query, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Reference == query.Reference && o.CustomerId == query.CustomerId, ct) is { } order
            ? MeMapping.ToDetail(order)
            : OrderErrors.NotFound;
}
