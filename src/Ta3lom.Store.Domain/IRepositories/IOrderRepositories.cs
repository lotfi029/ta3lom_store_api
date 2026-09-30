using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    /// RUL-28: next value of the gap-tolerant `order_ref_seq`.
    Task<long> NextReferenceNumberAsync(CancellationToken ct = default);

    /// The order with its lines (tracked, not locked).
    Task<Order?> FindByReferenceAsync(string reference, CancellationToken ct = default);
}

public interface IStockRepository
{
    /// RUL-30, database/01 §4.3: locks the print formats (in id order) and reserves each quantity with a conditional
    /// `UPDATE … WHERE stock_on_hand − stock_reserved ≥ q`. Returns the first format that could not be reserved, or null.
    /// Run inside the checkout unit of work: a failure must roll the whole transaction back.
    Task<Guid?> TryReserveAsync(IReadOnlyList<(Guid BookFormatId, int Quantity)> reservations, CancellationToken ct = default);
}
