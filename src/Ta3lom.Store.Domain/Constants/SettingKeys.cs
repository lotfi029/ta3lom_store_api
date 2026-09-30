namespace Ta3lom.Store.Domain.Constants;

/// Keys of the `setting` table (03 §M, plus seller_vat_no and dedication_templates).
public static class SettingKeys
{
    public const string VatEnabled = "vat_enabled";
    public const string VatRate = "vat_rate";
    public const string SellerVatNo = "seller_vat_no";
    public const string ShippingFlatFee = "shipping_flat_fee";
    public const string MinGiftAmount = "min_gift_amount";
    public const string CartMaxTotal = "cart_max_total";
    public const string OrderPaymentTimeoutMin = "order_payment_timeout_min";
    public const string DownloadLimit = "download_limit";
    public const string AllocationAlertDays = "allocation_alert_days";
    public const string AllocationEscalateDays = "allocation_escalate_days";
    public const string LapseDays = "lapse_days";
    public const string RefundWindowDays = "refund_window_days";
    public const string RefundProgressMaxPct = "refund_progress_max_pct";
    public const string LowStockDefaultThreshold = "low_stock_default_threshold";
    public const string GiftRefundWindowHours = "gift_refund_window_hours";
    public const string DedicationFreeTextEnabled = "dedication_free_text_enabled";
    public const string DedicationTemplates = "dedication_templates";

    public static IReadOnlyList<string> All { get; } =
    [
        VatEnabled, VatRate, SellerVatNo, ShippingFlatFee, MinGiftAmount, CartMaxTotal, OrderPaymentTimeoutMin, DownloadLimit,
        AllocationAlertDays, AllocationEscalateDays, LapseDays, RefundWindowDays, RefundProgressMaxPct, LowStockDefaultThreshold,
        GiftRefundWindowHours, DedicationFreeTextEnabled, DedicationTemplates,
    ];
}

/// Fixed commercial limits of S1 (03 RUL-12, RUL-18; 11 §2.9) that are not settings.
public static class CatalogLimits
{
    public const int CartMaxLines = 50;
    public const int PrintMaxQuantity = 20;
    public const int GiftMaxQuantity = 999;
    public static readonly int[] CourseSponsorPresets = [1, 5, 10, 25];
    public static readonly int[] BookSponsorPresets = [10, 25, 50, 100];
    public static readonly int[] CauseUnitPresets = [1, 2, 5, 10];
    public static readonly string[] AmountPresets = ["50.00", "100.00", "250.00", "500.00"];
    public static readonly string[] PaymentMethods = ["mada", "applepay", "card", "stcpay"];
}
