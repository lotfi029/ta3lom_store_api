namespace Ta3lom.Store.Domain.ReadModels;

/// A row of the view `v_catalog_stats` (staff only): pending donation orders per project and per category. Paid totals arrive
/// with R5-Payments.
public sealed class CatalogStat
{
    private CatalogStat()
    {
    }

    public TargetKind TargetKind { get; private set; }

    public Guid TargetId { get; private set; }

    public int PendingOrders { get; private set; }

    public Money PendingAmount { get; private set; }
}
