namespace Ta3lom.Store.Infrastructure.Persistence.Seeders;

/// Reference data every environment needs. The catalogue is not here: Development gets the demo catalogue of
/// <see cref="DevelopmentCatalogSeeder"/>, production staff enter theirs.
internal static class SeedData
{
    /// The settings with their defaults (JSON values; money as "10.00" strings).
    public static readonly IReadOnlyList<(string Key, string Json)> Settings =
    [
        ("min_gift_amount", "\"10.00\""),
        ("max_line_amount", "\"100000.00\""),
        ("cart_max_lines", "50"),
        ("cart_max_total", "\"100000.00\""),
        ("payment_window_minutes", "60"),
        ("amount_presets", """["50.00","100.00","500.00"]"""),
        ("site_name_ar", "\"متجر تعلّم\""),
        ("site_name_en", "\"ta3lom store\""),
        ("support_mobile", "null"),
    ];

    /// Message templates (Scriban). The final wording is approved by the owner before launch.
    public static readonly IReadOnlyList<(string Key, string Channel, string Locale, string? Subject, string Body)> Templates =
    [
        ("otp", "sms", "ar", null, "رمز الدخول لمتجر تعلّم: {{ code }} — صالح 5 دقائق. لا تشاركه مع أحد."),
        ("otp", "sms", "en", null, "Your ta3lom store code: {{ code }} — valid 5 minutes. Do not share it."),
        ("staff_alert", "email", "ar", "[متجر تعلّم] {{ subject }}", "{{ body }}"),
        ("staff_alert", "email", "en", "[ta3lom store] {{ subject }}", "{{ body }}"),
    ];
}
