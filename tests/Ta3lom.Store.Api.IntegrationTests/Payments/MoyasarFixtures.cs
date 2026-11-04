namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Recorded shapes of Moyasar responses and webhooks, taken from the examples in docs.moyasar.com (create/show invoice,
/// fetch payment, refund, webhook reference) and trimmed. Ids are the docs' sample uuids.
internal static class MoyasarFixtures
{
    public const string InvoiceId = "efebc231-7795-4cfd-b69c-980fe4c02c49";
    public const string PaymentId = "a4a144ba-adc3-43bd-98e8-c80f2925fdc4";

    public const string CreatedInvoice = $$$"""
        {"id":"{{{InvoiceId}}}","status":"initiated","amount":35000,"currency":"SAR","description":"T3-26-000001",
         "logo_url":"https://cdn.moyasar.com/x.png","amount_format":"350.00 SAR",
         "url":"https://checkout.moyasar.com/invoices/{{{InvoiceId}}}?lang=ar","callback_url":null,
         "expired_at":"2026-11-04T10:00:00.000Z","created_at":"2026-11-04T09:00:00.000Z","updated_at":"2026-11-04T09:00:00.000Z",
         "back_url":"https://store.ta3lom.sa/ar/orders/T3-26-000001?t=x","success_url":"https://store.ta3lom.sa/ar/orders/T3-26-000001?t=x",
         "payments":[],"metadata":null}
        """;

    public static string Payment(string status, long amount = 35000, long captured = 35000, long refunded = 0, string company = "mada",
        string type = "creditcard", string message = "APPROVED") => $$$"""
        {"id":"{{{PaymentId}}}","status":"{{{status}}}","amount":{{{amount}}},"fee":0,"currency":"SAR","refunded":{{{refunded}}},
         "refunded_at":null,"captured":{{{captured}}},"captured_at":"2026-11-04T09:05:00.000Z","voided_at":null,
         "description":"T3-26-000001","amount_format":"350.00 SAR","invoice_id":"{{{InvoiceId}}}","ip":"10.0.0.1",
         "callback_url":null,"created_at":"2026-11-04T09:04:00.000Z","updated_at":"2026-11-04T09:05:00.000Z","metadata":null,
         "source":{"type":"{{{type}}}","company":"{{{company}}}","name":"Test Donor","number":"4201-32XX-XXXX-1010",
                   "gateway_id":"moyasar_cc_1","token":null,"message":"{{{message}}}","transaction_url":null,
                   "reference_number":"125478454231","authorization_code":"P1234D","response_code":"00"}}
        """;

    public static string InvoiceWithPayment(string paymentStatus) => $$$"""
        {"id":"{{{InvoiceId}}}","status":"paid","amount":35000,"currency":"SAR","description":"T3-26-000001",
         "url":"https://checkout.moyasar.com/invoices/{{{InvoiceId}}}","expired_at":null,"payments":[{{{Payment(paymentStatus)}}}]}
        """;

    public static string Webhook(string secret, string type = "payment_paid", bool live = false, string eventId = "evt-1", string status = "paid") => $$$"""
        {"id":"{{{eventId}}}","type":"{{{type}}}","created_at":"2026-11-04T09:05:01.000Z","secret_token":"{{{secret}}}",
         "account_name":"ta3lom","live":{{{(live ? "true" : "false")}}},"data":{{{Payment(status)}}}}
        """;
}
