namespace Ta3lom.Store.Domain.Enums;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum StockMovementReason
{
    Restock,
    Sale,
    Return,
    SponsorDistribution,
    Adjustment,
}
