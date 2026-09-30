using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Fulfilment;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Fulfilment;

/// `GET /v1/admin/shipments` (#95).
public sealed record ListShipmentsQuery(string? Status, int Page, int PageSize) : IQuery<PagedResult<ShipmentResponse>>;

internal sealed class ListShipmentsQueryHandler(IApplicationDbContext db, FulfilmentReader reader) : IQueryHandler<ListShipmentsQuery, PagedResult<ShipmentResponse>>
{
    public async Task<Result<PagedResult<ShipmentResponse>>> HandleAsync(ListShipmentsQuery query, CancellationToken ct = default)
    {
        var q = db.Shipments.AsNoTracking();
        if (query.Status is not null)
        {
            if (!EnumWire.TryParse<ShipmentStatus>(query.Status, out var status))
            {
                return Error.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown shipment status."] });
            }

            q = q.Where(s => s.Status == status);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(s => s.Status).ThenBy(s => s.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<ShipmentResponse>(await reader.ShipmentsAsync(rows, ct), total, page.SafePage, page.Take);
    }
}

/// `POST /v1/admin/shipments/{id}/ship|deliver|return` (#97–#99, RUL-40): the shipment moves, the order's print lines follow
/// (`shipped` → `delivered` | `returned`), the order rolls up to `fulfilled`; ship/deliver queue an SMS (outbox).
public sealed record ShipCommand(Guid ShipmentId, string Carrier, string TrackingNo) : ICommand<ShipmentResponse>;

public sealed record DeliverCommand(Guid ShipmentId, DateOnly? DeliveredOn) : ICommand<ShipmentResponse>;

public sealed record ReturnShipmentCommand(Guid ShipmentId, string Note, bool Restock) : ICommand<ShipmentResponse>;

internal sealed class ShipmentTransitions(
    IGenericRepository<Shipment> shipments,
    IOrderRepository orders,
    IStockRepository stock,
    IGenericRepository<StockMovement> movements,
    IApplicationDbContext db,
    FulfilmentReader reader,
    ICurrentActor actor,
    IUnitOfWork uow,
    IClock clock)
    : ICommandHandler<ShipCommand, ShipmentResponse>, ICommandHandler<DeliverCommand, ShipmentResponse>, ICommandHandler<ReturnShipmentCommand, ShipmentResponse>
{
    private string Actor => actor.Name ?? "staff";

    public Task<Result<ShipmentResponse>> HandleAsync(ShipCommand command, CancellationToken ct = default) =>
        MoveAsync(command.ShipmentId, (s, _) => Task.FromResult(s.Ship(command.Carrier, command.TrackingNo, Actor, clock.UtcNow)), FulfilmentStatus.Shipped, ct);

    public Task<Result<ShipmentResponse>> HandleAsync(DeliverCommand command, CancellationToken ct = default)
    {
        var at = command.DeliveredOn is { } d ? new DateTimeOffset(d.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3)).ToUniversalTime() : clock.UtcNow;
        return MoveAsync(command.ShipmentId, (s, _) => Task.FromResult(s.Deliver(at, Actor)), FulfilmentStatus.Delivered, ct);
    }

    public Task<Result<ShipmentResponse>> HandleAsync(ReturnShipmentCommand command, CancellationToken ct = default) =>
        MoveAsync(command.ShipmentId, async (s, printLines) =>
        {
            if (!s.Return(Actor))
            {
                return false;
            }

            if (command.Restock)
            {
                var reference = await db.Orders.AsNoTracking().Where(o => o.Id == s.OrderId).Select(o => o.Reference).FirstAsync(ct);
                foreach (var line in printLines)
                {
                    await stock.TryAdjustAsync(line.BookFormatId!.Value, line.Quantity, ct);
                    movements.Add(StockMovement.Record(line.BookFormatId.Value, line.Quantity, StockMovementReason.Return, "order", reference, command.Note,
                        Actor, clock.UtcNow));
                }
            }

            return true;
        }, FulfilmentStatus.Returned, ct);

    private async Task<Result<ShipmentResponse>> MoveAsync(Guid shipmentId, Func<Shipment, IReadOnlyList<OrderLine>, Task<bool>> move, FulfilmentStatus lineStatus,
        CancellationToken ct)
    {
        var moved = await uow.ExecuteInTransactionAsync(async token =>
        {
            var shipment = await shipments.GetByIdAsync(shipmentId, token);
            if (shipment is null)
            {
                return Result.Failure(GeneralErrors.NotFound);
            }

            var order = (await orders.LoadForUpdateAsync(shipment.OrderId, token))!;
            var formatIds = order.Lines.Where(l => l.BookFormatId is not null).Select(l => l.BookFormatId!.Value).ToList();
            var print = await db.BookFormats.AsNoTracking().Where(f => formatIds.Contains(f.Id) && f.Kind == BookFormatKind.Print).Select(f => f.Id).ToListAsync(token);
            var printLines = order.Lines.Where(l => l.Kind == LineKind.Buy && l.BookFormatId is { } f && print.Contains(f)).ToList();
            if (!await move(shipment, printLines))
            {
                return Result.Failure(FulfilmentErrors.ShipmentInvalidTransition);
            }

            foreach (var line in printLines)
            {
                line.SetFulfilment(lineStatus);
            }

            order.RecomputeFulfilment(clock.UtcNow);                                       // RUL-40
            return Result.Success();
        }, ct);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        return await reader.ShipmentAsync((await db.Shipments.AsNoTracking().FirstAsync(s => s.Id == shipmentId, ct)), ct);
    }
}

internal sealed class ShipCommandValidator : AbstractValidator<ShipCommand>
{
    public ShipCommandValidator()
    {
        RuleFor(c => c.Carrier).NotEmpty().MaximumLength(60);
        RuleFor(c => c.TrackingNo).NotEmpty().MaximumLength(60);
    }
}

internal sealed class ReturnShipmentCommandValidator : AbstractValidator<ReturnShipmentCommand>
{
    public ReturnShipmentCommandValidator() => RuleFor(c => c.Note).NotEmpty().MaximumLength(500);
}

/// `GET /v1/admin/shipments/{id}/packing-slip` (#96, FR-S42): rendered on demand, never stored; no prices.
public sealed record PackingSlipQuery(Guid ShipmentId) : IQuery<PackingSlipFile>;

public sealed record PackingSlipFile(byte[] Content, string FileName);

internal sealed class PackingSlipQueryHandler(IApplicationDbContext db, FulfilmentReader reader, IDocumentRenderer renderer, IClock clock)
    : IQueryHandler<PackingSlipQuery, PackingSlipFile>
{
    public async Task<Result<PackingSlipFile>> HandleAsync(PackingSlipQuery query, CancellationToken ct = default)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.ShipmentId, ct);
        if (shipment is null)
        {
            return GeneralErrors.NotFound;
        }

        var view = await reader.ShipmentAsync(shipment, ct);
        var a = view.Address;
        var pdf = renderer.RenderPackingSlip(new PackingSlipDocument(view.OrderRef, clock.UtcNow, a.RecipientName, a.RecipientMobile,
            string.Join("، ", new[] { a.City, a.District, a.Street, a.BuildingNo, a.ShortAddress, a.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x))),
            [.. view.Lines.Select(l => new PackingSlipLine(l.Sku, l.TitleAr, l.Quantity))]));
        return new PackingSlipFile(pdf, $"packing-slip-{view.OrderRef}.pdf");
    }
}
