using System.Runtime.Serialization;

namespace Ta3lom.Store.Domain.Enums;

/// Gapless document series (F-D4): DR = donation receipt (one per paid order), DRC = cancellation receipt (voids a DR
/// on a full refund). Numbers restart every Riyadh year.
public enum ReceiptSeries
{
    [EnumMember(Value = "DR")]
    Dr,

    [EnumMember(Value = "DRC")]
    Drc,
}
