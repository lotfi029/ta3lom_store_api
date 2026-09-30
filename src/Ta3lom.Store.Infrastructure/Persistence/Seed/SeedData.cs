namespace Ta3lom.Store.Infrastructure.Persistence.Seed;

/// Reference data every environment needs (database/03 §3). The catalogue is not here: staging loads
/// db/reference/002_store_seed.sql, production staff enter it.
internal static class SeedData
{
    /// 03-business-rules.md §M, plus seller_vat_no (002 seed) and dedication_templates (002 seed). JSON values.
    public static readonly IReadOnlyList<(string Key, string Json)> Settings =
    [
        ("vat_enabled", "false"),
        ("vat_rate", "15"),
        ("seller_vat_no", "null"),
        ("shipping_flat_fee", "25.00"),
        ("min_gift_amount", "10.00"),
        ("cart_max_total", "100000.00"),
        ("order_payment_timeout_min", "60"),
        ("download_limit", "5"),
        ("allocation_alert_days", "30"),
        ("allocation_escalate_days", "90"),
        ("lapse_days", "60"),
        ("refund_window_days", "7"),
        ("refund_progress_max_pct", "20"),
        ("low_stock_default_threshold", "20"),
        ("gift_refund_window_hours", "48"),
        ("dedication_free_text_enabled", "false"),
        ("dedication_templates", """
            [
             {"key":"hasanat","ar":"اللهم اجعله في ميزان حسناتك","en":"May it weigh in your good deeds"},
             {"key":"departed","ar":"صدقة جارية عن روح الفقيد","en":"A lasting charity for the departed"},
             {"key":"success","ar":"هدية بمناسبة نجاحك","en":"A gift for your success"},
             {"key":"eid","ar":"عيدكم مبارك","en":"Eid Mubarak"}]
            """),
    ];

    /// Message templates (RUL-63, 09 §4.9). The first ten rows are the 002 seed; the rest fill the keys
    /// 09 §4.9 lists as "added by SeedRunner". Their wording needs owner review before launch.
    public static readonly IReadOnlyList<(string Key, string Channel, string Locale, string? Subject, string Body)> Templates =
    [
        ("otp", "sms", "ar", null, "رمز الدخول لمتجر تعلّم: {{ code }} — صالح 5 دقائق. لا تشاركه مع أحد."),
        ("otp", "sms", "en", null, "Your ta3lom store code: {{ code }} — valid 5 minutes. Do not share it."),
        ("order_paid", "sms", "ar", null, "تم استلام طلبك {{ reference }} بمبلغ {{ total }} ر.س. الإيصال: {{ link }}"),
        ("order_paid", "sms", "en", null, "Order {{ reference }} received, {{ total }} SAR. Receipt: {{ link }}"),
        ("dedication_card", "sms", "ar", null, "{{ to_name }}، تبرّع {{ from_name }} عنك في «{{ product }}». بطاقتك: {{ link }}"),
        ("dedication_card", "sms", "en", null, "{{ to_name }}, {{ from_name }} gave on your behalf to \"{{ product }}\". Your card: {{ link }}"),
        ("beneficiary_invite", "sms", "ar", null, "مرحباً، خُصّص لك مقعد في دورة «{{ product }}» من جمعية التعلم الإلكتروني. ابدأ: {{ link }}"),
        ("shipment_shipped", "sms", "ar", null, "طلبك {{ reference }} في الطريق. رقم الشحنة {{ tracking }}"),
        ("refund_done", "sms", "ar", null, "تم استرداد {{ amount }} ر.س من الطلب {{ reference }}."),
        ("cause_update", "sms", "ar", null, "تحديث من «{{ product }}»: {{ title }} {{ link }}"),

        ("order_paid", "email", "ar", "تم استلام طلبك {{ reference }}", "شكراً لك. استلمنا طلبك {{ reference }} بمبلغ {{ total }} ر.س.\nالمستندات: {{ link }}"),
        ("order_paid", "email", "en", "Order {{ reference }} received", "Thank you. We received order {{ reference }} for {{ total }} SAR.\nDocuments: {{ link }}"),
        ("beneficiary_invite", "sms", "en", null, "Hello, a seat in \"{{ product }}\" from the E-Learning Association is reserved for you. Start: {{ link }}"),
        ("beneficiary_lapsed", "sms", "ar", null, "انتهت مهلة البدء في دورة «{{ product }}» وأُعيد المقعد لمستفيد آخر."),
        ("beneficiary_lapsed", "sms", "en", null, "The start period for \"{{ product }}\" has ended and the seat was passed to another beneficiary."),
        ("shipment_shipped", "sms", "en", null, "Your order {{ reference }} is on its way. Tracking {{ tracking }}"),
        ("shipment_delivered", "sms", "ar", null, "تم تسليم طلبك {{ reference }}. شكراً لك."),
        ("shipment_delivered", "sms", "en", null, "Your order {{ reference }} has been delivered. Thank you."),
        ("refund_done", "sms", "en", null, "{{ amount }} SAR refunded from order {{ reference }}."),
        ("refund_done", "email", "ar", "استرداد من الطلب {{ reference }}", "تم استرداد {{ amount }} ر.س من الطلب {{ reference }}."),
        ("refund_done", "email", "en", "Refund for order {{ reference }}", "{{ amount }} SAR has been refunded from order {{ reference }}."),
        ("cause_update", "sms", "en", null, "Update from \"{{ product }}\": {{ title }} {{ link }}"),
        ("cause_update", "email", "ar", "تحديث من «{{ product }}»", "{{ title }}\n{{ body }}\n{{ link }}"),
        ("cause_update", "email", "en", "Update from \"{{ product }}\"", "{{ title }}\n{{ body }}\n{{ link }}"),
        ("staff_alert", "email", "ar", "[متجر تعلّم] {{ subject }}", "{{ body }}"),
        ("staff_alert", "email", "en", "[ta3lom store] {{ subject }}", "{{ body }}"),
        ("sponsor_digest", "email", "ar", "أثر رعايتك هذا الشهر", "{{ body }}"),
        ("sponsor_digest", "email", "en", "Your sponsorship this month", "{{ body }}"),
    ];

    /// Gapless document series (RUL-59); rows for other years are created lazily by the numbering transaction.
    public static readonly IReadOnlyList<string> DocumentSeries = ["INV", "CN", "DR", "DRC"];
}
