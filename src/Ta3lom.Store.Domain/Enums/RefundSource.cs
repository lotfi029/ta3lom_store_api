namespace Ta3lom.Store.Domain.Enums;

/// Who made a refund: finance through the store, or someone on the gateway's dashboard (imported, review 04 A-2).
public enum RefundSource
{
    Store,
    Gateway,
}
