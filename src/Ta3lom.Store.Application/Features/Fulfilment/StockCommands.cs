using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Fulfilment;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Fulfilment;

/// `GET /v1/admin/stock` (#78, FR-S43): print formats with on hand, reserved, available and the effective low-stock threshold.
public sealed record ListStockQuery(bool LowOnly) : IQuery<IReadOnlyList<StockRow>>;

internal sealed class ListStockQueryHandler(FulfilmentReader reader) : IQueryHandler<ListStockQuery, IReadOnlyList<StockRow>>
{
    public async Task<Result<IReadOnlyList<StockRow>>> HandleAsync(ListStockQuery query, CancellationToken ct = default) =>
        Result.Success(await reader.StockAsync(null, query.LowOnly, ct));
}

/// `GET /v1/admin/stock/{formatId}/movements` (#79): the ledger, newest first.
public sealed record ListStockMovementsQuery(Guid FormatId, int Page, int PageSize) : IQuery<PagedResult<StockMovementResponse>>;

internal sealed class ListStockMovementsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListStockMovementsQuery, PagedResult<StockMovementResponse>>
{
    public async Task<Result<PagedResult<StockMovementResponse>>> HandleAsync(ListStockMovementsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.StockMovements.AsNoTracking().Where(m => m.BookFormatId == query.FormatId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(m => m.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<StockMovementResponse>(
            [.. rows.Select(m => new StockMovementResponse(m.Id, m.Delta, m.Reason.ToWire(), m.RefType, m.RefId, m.Note, m.At, m.Actor))], total, page.SafePage, page.Take);
    }
}

/// `POST /v1/admin/stock/{formatId}/movements` (#80, RUL-30): restock, adjustment or return with a reason; never below the
/// reserved quantity (`stock.below_reserved`). `sale`/`sponsor_distribution` are system-only.
public sealed record AddStockMovementCommand(Guid FormatId, int Delta, string Reason, string? Note) : ICommand<StockRow>;

internal sealed class AddStockMovementCommandHandler(IApplicationDbContext db, IStockRepository stock, IGenericRepository<StockMovement> movements,
    FulfilmentReader reader, ICurrentActor actor, IUnitOfWork uow, IClock clock) : ICommandHandler<AddStockMovementCommand, StockRow>
{
    public async Task<Result<StockRow>> HandleAsync(AddStockMovementCommand command, CancellationToken ct = default)
    {
        if (!await db.BookFormats.AnyAsync(f => f.Id == command.FormatId && f.Kind == BookFormatKind.Print, ct))
        {
            return GeneralErrors.NotFound;
        }

        var reason = EnumWire.Parse<StockMovementReason>(command.Reason);
        var done = await uow.ExecuteInTransactionAsync(async token =>
        {
            if (!await stock.TryAdjustAsync(command.FormatId, command.Delta, token))
            {
                return Result.Failure(FulfilmentErrors.StockBelowReserved);
            }

            movements.Add(StockMovement.Record(command.FormatId, command.Delta, reason, "manual", null, command.Note, actor.Name ?? "staff", clock.UtcNow));
            return Result.Success();
        }, ct);
        return done.IsFailure ? done.Error : (await reader.StockAsync(command.FormatId, false, ct))[0];
    }
}

internal sealed class AddStockMovementCommandValidator : AbstractValidator<AddStockMovementCommand>
{
    public AddStockMovementCommandValidator()
    {
        RuleFor(c => c.Delta).NotEqual(0).InclusiveBetween(-100_000, 100_000);
        RuleFor(c => c.Reason).Must(r => r is "restock" or "adjustment" or "return").WithMessage("reason must be restock, adjustment or return.");
        RuleFor(c => c.Note).MaximumLength(500);
    }
}
