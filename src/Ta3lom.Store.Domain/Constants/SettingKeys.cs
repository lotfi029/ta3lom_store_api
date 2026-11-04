namespace Ta3lom.Store.Domain.Constants;

/// Keys of the `setting` table (seeded with their defaults; staff edit them through the typed allow-list).
public static class SettingKeys
{
    public const string MinGiftAmount = "min_gift_amount";
    public const string MaxLineAmount = "max_line_amount";
    public const string CartMaxLines = "cart_max_lines";
    public const string CartMaxTotal = "cart_max_total";
    public const string PaymentWindowMinutes = "payment_window_minutes";
    public const string AmountPresets = "amount_presets";
    public const string SiteNameAr = "site_name_ar";
    public const string SiteNameEn = "site_name_en";
    public const string SupportMobile = "support_mobile";

    // Donation receipts (F-D4; texts printed on the DR/DRC PDF, all optional)
    public const string ReceiptOrgNameAr = "receipt_org_name_ar";
    public const string ReceiptOrgNameEn = "receipt_org_name_en";
    public const string ReceiptLicenceNo = "receipt_licence_no";
    public const string ReceiptVatNoteAr = "receipt_vat_note_ar";
    public const string ReceiptVatNoteEn = "receipt_vat_note_en";
    public const string ReceiptSignatureAssetId = "receipt_signature_asset_id";

    /// Minutes a refund may wait for the gateway's answer before the reconciliation fails it (review 04 A-6).
    public const string RefundPendingTimeoutMinutes = "refund_pending_timeout_minutes";

    public static IReadOnlyList<string> All { get; } =
        [MinGiftAmount, MaxLineAmount, CartMaxLines, CartMaxTotal, PaymentWindowMinutes, AmountPresets, SiteNameAr, SiteNameEn, SupportMobile,
         ReceiptOrgNameAr, ReceiptOrgNameEn, ReceiptLicenceNo, ReceiptVatNoteAr, ReceiptVatNoteEn, ReceiptSignatureAssetId, RefundPendingTimeoutMinutes];
}
