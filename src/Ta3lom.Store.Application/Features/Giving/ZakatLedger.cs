using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Application.Features.Giving;

/// `GET /v1/admin/zakat` (#115, FR-S61, RUL-20): balances per product (`v_zakat_balance`) and the ledger rows, filtered by
/// product and Riyadh dates; newest first.
public sealed record GetZakatLedgerQuery(Guid? ProductId, DateOnly? From, DateOnly? To, int Page, int PageSize) : IQuery<ZakatLedgerResponse>;

internal sealed class GetZakatLedgerQueryHandler(IApplicationDbContext db) : IQueryHandler<GetZakatLedgerQuery, ZakatLedgerResponse>
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<ZakatLedgerResponse>> HandleAsync(GetZakatLedgerQuery query, CancellationToken ct = default)
    {
        var balances = await (from v in db.ZakatBalances.AsNoTracking()
                              join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                              where query.ProductId == null || v.ProductId == query.ProductId
                              orderby p.Slug
                              select new { v, p.Slug }).ToListAsync(ct);

        var entries = from z in db.ZakatLedger.AsNoTracking()
                      join p in db.Products.AsNoTracking() on z.ProductId equals p.Id
                      select new { z, p.Slug };
        if (query.ProductId is { } productId)
        {
            entries = entries.Where(e => e.z.ProductId == productId);
        }

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();
            entries = entries.Where(e => e.z.At >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();
            entries = entries.Where(e => e.z.At < end);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await entries.CountAsync(ct);
        var rows = await entries.OrderByDescending(e => e.z.At).ThenByDescending(e => e.z.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new ZakatLedgerResponse(
            [.. balances.Select(b => new ZakatBalance(b.v.ProductId, b.Slug, Wire(b.v.Received), Wire(b.v.Disbursed), Wire(b.v.Refunded), Wire(b.v.Balance)))],
            new PagedResult<ZakatEntry>([.. rows.Select(r => new ZakatEntry(r.z.Id, r.z.ProductId, r.Slug, r.z.Direction.ToWire(), r.z.Amount.ToWire(),
                r.z.OrderLineId, r.z.Reference, r.z.At, r.z.Actor))], total, page.SafePage, page.Take));
    }

    private static string Wire(decimal? value) => Money.Round(value ?? 0m).ToWire();
}

/// `POST /v1/admin/zakat/disbursements` (#116, RUL-20): a finance voucher for **cause** zakat (sponsorship zakat goes out at
/// allocation) — `zakat_ledger` `out` with `reference` = voucher number and `at` = noon Riyadh of the disbursement date.
/// Serialised per product; above the balance → `zakat.balance_insufficient` (integrity check 7 never goes negative). The
/// purpose is kept in `audit_log` (`zakat.voucher`), since the ledger has no purpose column.
public sealed record RecordZakatVoucherCommand(ZakatVoucherInput Input) : ICommand<ZakatEntry>;

internal sealed class RecordZakatVoucherCommandHandler(IApplicationDbContext db, IZakatLedgerRepository ledger, IGenericRepository<ZakatLedgerEntry> entries,
    IGenericRepository<AuditLogEntry> audit, ICurrentActor actor, IUnitOfWork uow) : ICommandHandler<RecordZakatVoucherCommand, ZakatEntry>
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<ZakatEntry>> HandleAsync(RecordZakatVoucherCommand command, CancellationToken ct = default)
    {
        var i = command.Input;
        var product = await db.Products.AsNoTracking().Where(p => p.Id == i.ProductId).Select(p => new { p.Slug, p.Type }).FirstOrDefaultAsync(ct);
        if (product is null)
        {
            return Error.NotFound(ErrorCodes.ProductNotFound, "Product not found.");
        }

        var voucherNo = i.VoucherNo!.Trim().ToUpperInvariant();
        if (product.Type != ProductType.Cause)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["productId"] = ["vouchers are for cause zakat; sponsorship zakat goes out at allocation"] });
        }

        if (await db.ZakatLedger.AnyAsync(z => z.Direction == ZakatDirection.Out && z.Reference == voucherNo, ct))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["voucherNo"] = ["voucher number already used"] });
        }

        var amount = Money.Parse(i.Amount!);
        var by = actor.Name ?? "staff";
        var at = new DateTimeOffset(i.DisbursedOn!.Value.ToDateTime(new TimeOnly(12, 0)), Riyadh).ToUniversalTime();
        var result = await uow.ExecuteInTransactionAsync(async token =>
        {
            var balance = await ledger.LockBalanceAsync(i.ProductId, token);
            if (amount > balance)
            {
                return Result.Failure<ZakatLedgerEntry>(GivingErrors.ZakatBalanceInsufficient(balance));
            }

            var entry = ZakatLedgerEntry.Voucher(i.ProductId, amount, voucherNo, at, by);
            entries.Add(entry);
            audit.Add(new AuditLogEntry(by, "zakat.voucher", "zakat_ledger", at, voucherNo,
                JsonSerializer.Serialize(new { productId = i.ProductId, amount = amount.ToWire(), voucherNo, disbursedOn = i.DisbursedOn, purpose = i.Purpose!.Trim() }),
                actor.IpHash));
            await uow.SaveChangesAsync(token);
            return Result.Success(entry);
        }, ct);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var e = result.Value;
        return new ZakatEntry(e.Id, e.ProductId, product.Slug, e.Direction.ToWire(), e.Amount.ToWire(), e.OrderLineId, e.Reference, e.At, e.Actor);
    }
}

internal sealed class RecordZakatVoucherCommandValidator : AbstractValidator<RecordZakatVoucherCommand>
{
    public RecordZakatVoucherCommandValidator()
    {
        RuleFor(c => c.Input.ProductId).NotEmpty();
        RuleFor(c => c.Input.Amount).Must(a => a is not null && decimal.TryParse(a, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
                && v > 0 && decimal.Round(v, 2) == v && v <= 10_000_000m)
            .WithMessage("amount must be a positive money string with up to two decimals, e.g. \"4750.00\".");
        RuleFor(c => c.Input.VoucherNo).NotEmpty().Length(3, 40);
        RuleFor(c => c.Input.DisbursedOn).NotNull();
        RuleFor(c => c.Input.Purpose).NotEmpty().MaximumLength(500);
    }
}
