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

    public static IReadOnlyList<string> All { get; } =
        [MinGiftAmount, MaxLineAmount, CartMaxLines, CartMaxTotal, PaymentWindowMinutes, AmountPresets, SiteNameAr, SiteNameEn, SupportMobile];
}
