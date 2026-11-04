namespace Ta3lom.Store.Application.Interfaces;

/// Gapless receipt numbers per series and Riyadh year (F-D4). Must be called inside the transaction that stores the
/// receipt (Infrastructure locks the counter row until it commits or rolls back).
public interface IDocumentNumbers
{
    Task<int> NextAsync(ReceiptSeries series, int year, CancellationToken ct = default);
}
