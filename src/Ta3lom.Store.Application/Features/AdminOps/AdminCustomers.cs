using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Domain.Entities.Auditing;

namespace Ta3lom.Store.Application.Features.AdminOps;

/// `GET /v1/admin/customers` (#129, FR-S79): lookup by **exact** mobile or e-mail only — no browsing of the customer base.
public sealed record FindCustomersQuery(string? Mobile, string? Email) : IQuery<IReadOnlyList<CustomerRow>>;

/// `GET /v1/admin/customers/{id}` (#130): profile, orders, giving totals, enrolments.
public sealed record GetCustomer360Query(Guid Id) : IQuery<Customer360>;

/// `POST /v1/admin/customers/{id}/block` (#131, RUL-61): `blocked`, every refresh token revoked, audited with the reason;
/// OTP verification is then refused. `…/unblock` (#132) restores `active`.
public sealed record BlockCustomerCommand(Guid Id, string Reason) : ICommand<CustomerRow>;

public sealed record UnblockCustomerCommand(Guid Id) : ICommand<CustomerRow>;

/// `GET /v1/admin/audit` (#133, FR-S78, BR-S11): newest first; filters are exact matches, dates are Riyadh days.
public sealed record AuditQuery(string? Actor, string? Entity, string? EntityId, string? Action, DateOnly? From, DateOnly? To, int Page, int PageSize)
    : IQuery<PagedResult<AuditEntry>>;

internal sealed class AdminCustomerHandlers(IApplicationDbContext db, ICustomerRepository customers, IRefreshTokenRepository tokens,
    IGenericRepository<AuditLogEntry> audit, ICurrentActor actor, IUnitOfWork uow, IClock clock) :
    IQueryHandler<FindCustomersQuery, IReadOnlyList<CustomerRow>>,
    IQueryHandler<GetCustomer360Query, Customer360>,
    ICommandHandler<BlockCustomerCommand, CustomerRow>,
    ICommandHandler<UnblockCustomerCommand, CustomerRow>,
    IQueryHandler<AuditQuery, PagedResult<AuditEntry>>
{
    public async Task<Result<IReadOnlyList<CustomerRow>>> HandleAsync(FindCustomersQuery query, CancellationToken ct = default)
    {
        var q = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Mobile))
        {
            if (!SaudiMobile.TryNormalize(query.Mobile.Trim(), out var mobile))
            {
                return Error.Validation(new Dictionary<string, string[]> { ["mobile"] = ["must be a Saudi mobile (05XXXXXXXX)"] });
            }

            q = q.Where(c => c.Mobile == mobile);
        }
        else if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var email = query.Email.Trim();
            q = q.Where(c => c.Email == email);                                                     // citext: case-insensitive
        }
        else
        {
            return Error.Validation(new Dictionary<string, string[]> { ["mobile"] = ["mobile or email is required (exact)"] });
        }

        var rows = new List<CustomerRow>();
        foreach (var c in await q.Take(20).ToListAsync(ct))
        {
            rows.Add(await RowAsync(c, ct));
        }

        return rows;
    }

    public async Task<Result<Customer360>> HandleAsync(GetCustomer360Query query, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct);
        if (c is null)
        {
            return GeneralErrors.NotFound;
        }

        var orders = await db.Orders.AsNoTracking().Where(o => o.CustomerId == c.Id).OrderByDescending(o => o.PlacedAt).Take(50)
            .Select(o => new
            {
                o.Reference,
                o.PlacedAt,
                o.Status,
                o.GrandTotal,
                Lines = db.OrderLines.Count(l => l.OrderId == o.Id),
                Invoice = db.Invoices.Any(i => i.OrderId == o.Id),
                Receipt = db.DonationReceipts.Any(r => r.OrderId == o.Id),
            }).ToListAsync(ct);
        var gifts = await (from l in db.OrderLines.AsNoTracking()
                           join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                           where o.CustomerId == c.Id && o.PaidAt != null && l.Kind != LineKind.Buy
                           select new { l.Id, l.Amount, l.RefundedAmount, l.Intent, l.Kind, l.Mode, l.Quantity }).ToListAsync(ct);
        var giftIds = gifts.Select(g => g.Id).ToList();
        var giving = new GivingTotals(
            gifts.Select(g => g.Amount - g.RefundedAmount).Sum().ToWire(),
            gifts.Where(g => g.Intent == GiftIntent.Zakat).Select(g => g.Amount).Sum().ToWire(),
            gifts.Where(g => g.Kind == LineKind.Sponsor && g.Mode == LineMode.Units).Sum(g => g.Quantity),
            gifts.Count(g => g.Kind == LineKind.Sponsor && g.Mode == LineMode.Amount),
            await db.Dedications.CountAsync(d => giftIds.Contains(d.OrderLineId), ct));
        var now = clock.UtcNow;
        var enrolments = (await (from e in db.Enrolments.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on e.ProductId equals p.Id
                                 where e.CustomerId == c.Id
                                 orderby e.GrantedAt descending
                                 select new { e, p.Slug, p.TitleAr, p.TitleEn }).ToListAsync(ct))
            .Select(r => new EnrolmentItem(r.e.Id, new EnrolmentProduct(r.Slug, r.TitleAr, r.TitleEn, null), r.e.Source.ToWire(), r.e.Status.ToWire(),
                r.e.ProgressPct, r.e.GrantedAt, r.e.ExpiresAt, r.e.StartedAt, r.e.CompletedAt, r.e.CertificateNo, r.e.CanLaunch(now))).ToList();
        return new Customer360(await RowAsync(c, ct),
            [.. orders.Select(o => new OrderSummary(o.Reference, o.PlacedAt, o.Status.ToWire(), o.GrandTotal.ToWire(), o.Lines, o.Invoice, o.Receipt))],
            giving, enrolments);
    }

    public async Task<Result<CustomerRow>> HandleAsync(BlockCustomerCommand command, CancellationToken ct = default)
    {
        var c = await customers.GetByIdAsync(command.Id, ct);
        if (c is null || c.IsAnonymised)
        {
            return GeneralErrors.NotFound;                                                            // a deleted account is gone
        }

        var now = clock.UtcNow;
        await uow.ExecuteInTransactionAsync(async token =>
        {
            c.Block(now);
            await tokens.RevokeAllForSubjectAsync(TokenSubjectType.Customer, c.Id, now, token);
            audit.Add(new AuditLogEntry(actor.Name ?? "staff", "customer.block", "customer", now, c.Id.ToString(),
                JsonSerializer.Serialize(new { reason = command.Reason.Trim() }), actor.IpHash));
            await uow.SaveChangesAsync(token);
        }, ct);
        return await RowAsync(c, ct);
    }

    public async Task<Result<CustomerRow>> HandleAsync(UnblockCustomerCommand command, CancellationToken ct = default)
    {
        var c = await customers.GetByIdAsync(command.Id, ct);
        if (c is null || c.IsAnonymised)
        {
            return GeneralErrors.NotFound;                                                            // a deleted account is gone
        }

        c.Unblock(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return await RowAsync(c, ct);
    }

    public async Task<Result<PagedResult<AuditEntry>>> HandleAsync(AuditQuery query, CancellationToken ct = default)
    {
        var q = db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            q = q.Where(a => a.Actor == query.Actor.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            q = q.Where(a => a.Entity == query.Entity.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            q = q.Where(a => a.EntityId == query.EntityId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(a => a.Action == query.Action.Trim());
        }

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), DashboardQueryHandler.Riyadh).ToUniversalTime();
            q = q.Where(a => a.At >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DashboardQueryHandler.Riyadh).ToUniversalTime();
            q = q.Where(a => a.At < end);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<AuditEntry>([.. rows.Select(a => new AuditEntry(a.Id, a.At, a.Actor, a.Action, a.Entity, a.EntityId,
            a.Diff is null ? null : JsonDocument.Parse(a.Diff).RootElement.Clone()))], total, page.SafePage, page.Take);
    }

    private async Task<CustomerRow> RowAsync(Domain.Entities.Identity.Customer c, CancellationToken ct) =>
        new(c.Id, c.Mobile, c.FullName, c.Email, c.Status.ToWire(), c.CreatedAt, c.LastLoginAt, await db.Orders.CountAsync(o => o.CustomerId == c.Id, ct));
}

internal sealed class BlockCustomerCommandValidator : AbstractValidator<BlockCustomerCommand>
{
    public BlockCustomerCommandValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}
