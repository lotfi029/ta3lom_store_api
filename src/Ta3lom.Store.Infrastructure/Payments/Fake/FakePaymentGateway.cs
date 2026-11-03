using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Payments.Moyasar;

namespace Ta3lom.Store.Infrastructure.Payments.Fake;

/// In-memory hosted pages and payments for Development, CI and e2e (C-2: refused in Staging and Production by
/// `PaymentOptionsValidator`). The hosted page is the API's own `/dev/payments/{id}` (Pay / Fail / Pay a wrong amount);
/// each action produces a webhook body in the Moyasar shape, signed with `Store:Payments:Fake:WebhookSecret`, which the
/// dev endpoint posts through the real webhook handler. State lives as long as the process (a restart loses open pages;
/// reconciliation then finds nothing and the order expires).
public sealed class FakePaymentStore
{
    private readonly ConcurrentDictionary<string, FakeInvoice> _invoices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FakePayment> _payments = new(StringComparer.Ordinal);

    /// Test hooks: make the next create fail (gateway down) / a refund fail.
    public bool FailNextCreate { get; set; }

    public bool FailRefunds { get; set; }

    public FakeInvoice? Invoice(string id) => _invoices.GetValueOrDefault(id);

    public FakePayment? Payment(string id) => _payments.GetValueOrDefault(id);

    internal FakeInvoice AddInvoice(HostedPaymentRequest request)
    {
        var invoice = new FakeInvoice($"fake_inv_{Guid.NewGuid():N}", request.OrderReference, request.AmountHalalas, request.ReturnUrl, request.ExpiresAt);
        _invoices[invoice.Id] = invoice;
        return invoice;
    }

    /// The donor's action on the hosted page: pay the amount (or `amountOverride`, "pay a wrong amount"), or fail.
    public FakePayment Act(string invoiceId, bool success, long? amountOverride = null)
    {
        var invoice = _invoices.GetValueOrDefault(invoiceId) ?? throw new KeyNotFoundException(invoiceId);
        var amount = amountOverride ?? invoice.AmountHalalas;
        var payment = new FakePayment($"fake_pay_{Guid.NewGuid():N}", invoiceId, amount)
        {
            Status = success ? "paid" : "failed",
            Captured = success ? amount : 0,
            Message = success ? "APPROVED" : "INSUFFICIENT FUNDS",
        };
        _payments[payment.Id] = payment;
        invoice.PaymentIds.Add(payment.Id);
        if (success)
        {
            invoice.Status = "paid";
        }

        return payment;
    }

    /// The webhook body for a payment, in Moyasar's shape (`secret_token` in the body).
    public static string WebhookBody(FakePayment payment, string secret, DateTimeOffset now, string? type = null) =>
        new JsonObject
        {
            ["id"] = $"fake_evt_{Guid.NewGuid():N}",
            ["type"] = type ?? (payment.Status == "paid" ? "payment_paid" : payment.Status == "refunded" ? "payment_refunded" : "payment_failed"),
            ["created_at"] = now.ToString("O"),
            ["secret_token"] = secret,
            ["account_name"] = "ta3lom dev",
            ["live"] = false,
            ["data"] = new JsonObject
            {
                ["id"] = payment.Id,
                ["status"] = payment.Status,
                ["amount"] = payment.AmountHalalas,
                ["captured"] = payment.Captured,
                ["refunded"] = payment.Refunded,
                ["currency"] = "SAR",
                ["invoice_id"] = payment.InvoiceId,
                ["source"] = new JsonObject { ["type"] = "creditcard", ["company"] = "mada", ["message"] = payment.Message, ["number"] = "4201-32XX-XXXX-1010" },
            },
        }.ToJsonString();

    internal static GatewayPayment Map(FakePayment p) =>
        MoyasarGateway.Map(new MoyasarGateway.MoyasarPayment(p.Id, p.Status, p.AmountHalalas, p.Captured, p.Refunded, "SAR", p.InvoiceId,
            new MoyasarGateway.MoyasarSource("creditcard", "mada", p.Message), false));
}

public sealed class FakeInvoice(string id, string orderReference, long amountHalalas, string returnUrl, DateTimeOffset expiresAt)
{
    public string Id { get; } = id;

    public string OrderReference { get; } = orderReference;

    public long AmountHalalas { get; } = amountHalalas;

    public string ReturnUrl { get; } = returnUrl;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public string Status { get; set; } = "initiated";

    public ConcurrentBag<string> PaymentIds { get; } = [];
}

public sealed class FakePayment(string id, string invoiceId, long amountHalalas)
{
    public string Id { get; } = id;

    public string InvoiceId { get; } = invoiceId;

    public long AmountHalalas { get; } = amountHalalas;

    public string Status { get; set; } = "initiated";

    public long Captured { get; set; }

    public long Refunded { get; set; }

    public string? Message { get; set; }
}

internal sealed class FakePaymentGateway(FakePaymentStore store, IOptions<PaymentOptions> options) : IPaymentGateway
{
    public const string ProviderName = "fake";

    public string Name => ProviderName;

    public Task<HostedPaymentResult> CreateHostedPaymentAsync(HostedPaymentRequest request, CancellationToken ct = default)
    {
        if (store.FailNextCreate)
        {
            store.FailNextCreate = false;
            return Task.FromResult(HostedPaymentResult.Failed("fake gateway down"));
        }

        var invoice = store.AddInvoice(request);
        var api = options.Value.Fake.ApiBaseUrl.TrimEnd('/');
        return Task.FromResult(HostedPaymentResult.Created(invoice.Id, $"{api}/dev/payments/{invoice.Id}", request.ExpiresAt));
    }

    public Task<GatewayPayment?> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default) =>
        Task.FromResult(store.Payment(providerPaymentId) is { } p ? FakePaymentStore.Map(p) : null);

    public Task<IReadOnlyList<GatewayPayment>> FindPaymentsForHostedPageAsync(string providerInvoiceId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GatewayPayment>>(store.Invoice(providerInvoiceId) is { } invoice
            ? [.. invoice.PaymentIds.Select(store.Payment).OfType<FakePayment>().Select(FakePaymentStore.Map)]
            : []);

    public Task<GatewayRefundResult> RefundAsync(string providerPaymentId, long amountHalalas, string idempotencyKey, CancellationToken ct = default)
    {
        var p = store.Payment(providerPaymentId);
        if (store.FailRefunds || p is null || p.Captured - p.Refunded < amountHalalas)
        {
            return Task.FromResult(GatewayRefundResult.Failed("fake refund refused"));
        }

        p.Refunded += amountHalalas;
        if (p.Refunded == p.Captured)
        {
            p.Status = "refunded";
        }

        return Task.FromResult(GatewayRefundResult.Succeeded($"fake_rf_{Guid.NewGuid():N}"));
    }

    public Task CancelHostedPaymentAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        if (store.Invoice(providerInvoiceId) is { Status: "initiated" } invoice)
        {
            invoice.Status = "canceled";
        }

        return Task.CompletedTask;
    }

    public WebhookVerification VerifyWebhook(ReadOnlySpan<byte> rawBody, IReadOnlyDictionary<string, string> headers) =>
        MoyasarGateway.Verify(rawBody, options.Value.Fake.WebhookSecret, expectLive: false);
}
