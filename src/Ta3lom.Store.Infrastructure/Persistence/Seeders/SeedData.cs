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
        ("receipt_org_name_ar", "\"جمعية التعلم الإلكتروني\""),
        ("receipt_org_name_en", "\"E-Learning Association\""),
        ("receipt_licence_no", "null"),
        ("receipt_vat_note_ar", "\"لا تخضع لضريبة القيمة المضافة\""),
        ("receipt_vat_note_en", "\"Not subject to VAT\""),
        ("receipt_signature_asset_id", "null"),
        ("refund_pending_timeout_minutes", "30"),
    ];

    /// Message templates (Scriban). The final wording is approved by the owner before launch.
    public static readonly IReadOnlyList<(string Key, string Channel, string Locale, string? Subject, string Body)> Templates =
    [
        ("otp", "sms", "ar", null, "رمز الدخول لمتجر تعلّم: {{ code }} — صالح 5 دقائق. لا تشاركه مع أحد."),
        ("otp", "sms", "en", null, "Your ta3lom store code: {{ code }} — valid 5 minutes. Do not share it."),
        ("staff_alert", "email", "ar", "[متجر تعلّم] {{ subject }}", "{{ body }}"),
        ("staff_alert", "email", "en", "[ta3lom store] {{ subject }}", "{{ body }}"),
        // R5-Payments (F3a): donor messages after settlement and refunds — wording for owner approval (Q-12).
        ("order_paid", "sms", "ar", null, "شكرًا لتبرعك {{ amount }} ر.س — الطلب {{ reference }}، إيصال {{ receipt_number }}: {{ link }}"),
        ("order_paid", "sms", "en", null, "Thank you for your donation of SAR {{ amount }} — order {{ reference }}, receipt {{ receipt_number }}: {{ link }}"),
        ("order_paid", "email", "ar", "[متجر تعلّم] إيصال تبرعك {{ receipt_number }}",
            "شكرًا لتبرعك.\nالطلب: {{ reference }}\nالمبلغ: {{ amount }} ر.س\nرقم الإيصال: {{ receipt_number }}\nالإيصال مرفق بصيغة PDF، ويمكنك متابعة الطلب من: {{ link }}"),
        ("order_paid", "email", "en", "[ta3lom store] Your donation receipt {{ receipt_number }}",
            "Thank you for your donation.\nOrder: {{ reference }}\nAmount: SAR {{ amount }}\nReceipt: {{ receipt_number }}\nThe receipt is attached as a PDF. Follow the order at: {{ link }}"),
        ("refund_processed", "sms", "ar", null, "تم استرداد {{ amount }} ر.س من تبرعك {{ reference }}. {{ link }}"),
        ("refund_processed", "sms", "en", null, "SAR {{ amount }} of your donation {{ reference }} has been refunded. {{ link }}"),
        ("refund_processed", "email", "ar", "[متجر تعلّم] استرداد من تبرعك {{ reference }}",
            "تم استرداد {{ amount }} ر.س من تبرعك {{ reference }}.{{ if receipt_number }}\nإيصال الإلغاء: {{ receipt_number }} (مرفق).{{ end }}\n{{ link }}"),
        ("refund_processed", "email", "en", "[ta3lom store] Refund of your donation {{ reference }}",
            "SAR {{ amount }} of your donation {{ reference }} has been refunded.{{ if receipt_number }}\nCancellation receipt: {{ receipt_number }} (attached).{{ end }}\n{{ link }}"),
    ];
}
