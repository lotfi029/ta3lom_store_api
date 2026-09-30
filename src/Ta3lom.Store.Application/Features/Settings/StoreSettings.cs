namespace Ta3lom.Store.Application.Features.Settings;

/// Typed view of the `setting` table (03 §M + seller_vat_no + dedication_templates; 09 §4.10). Cached 60 s,
/// invalidated when an admin updates a key (S-09).
public sealed record StoreSettings(
    bool VatEnabled,
    decimal VatRate,
    string? SellerVatNo,
    Money ShippingFlatFee,
    Money MinGiftAmount,
    Money CartMaxTotal,
    int OrderPaymentTimeoutMin,
    int DownloadLimit,
    int AllocationAlertDays,
    int AllocationEscalateDays,
    int LapseDays,
    int RefundWindowDays,
    int RefundProgressMaxPct,
    int LowStockDefaultThreshold,
    int GiftRefundWindowHours,
    bool DedicationFreeTextEnabled,
    IReadOnlyList<DedicationTemplate> DedicationTemplates);

public sealed record DedicationTemplate(string Key, string Ar, string En);
