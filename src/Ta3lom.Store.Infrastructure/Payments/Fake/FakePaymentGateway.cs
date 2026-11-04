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

    /// Review 04 test hooks: every read throws (gateway down); a refund answers "no definite answer" (timeout) and is
    /// or is not applied at the gateway; delays to widen races; the first `DelayNextReads` reads wait `ReadDelay`.
    public bool FailReads { get; set; }

    public bool PendingRefunds { get; set; }

    public bool PendingRefundApplies { get; set; }

    public TimeSpan RefundDelay { get; set; }

    public TimeSpan CreateDelay { get; set; }

    public TimeSpan ReadDelay { get; set; }

    private int _delayNextReads;

    public int DelayNextReads
    {
        get => Volatile.Read(ref _delayNextReads);
        set => Volatile.Write(ref _delayNextReads, value);
    }

    internal bool TakeReadDelay() => Interlocked.Decrement(ref _delayNextReads) >= 0;

    /// Every hosted page made for an order (A-4 tests: exactly one may still be payable).
    public IReadOnlyList<FakeInvoice> InvoicesFor(string orderReference) =>
        [.. _invoices.Values.Where(i => i.OrderReference == orderReference)];

    /// A captured payment on a hosted page the store never created (A-3: an orphan).
    public FakePayment OrphanPayment(long amountHalalas)
    {
        var invoice = new FakeInvoice($"fake_inv_{Guid.NewGuid():N}", "unknown", amountHalalas, "about:blank", DateTimeOffset.MaxValue);
        _invoices[invoice.Id] = invoice;
        return Act(invoice.Id, success: true);
    }

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
                ["currency"] = payment.Currency,
                ["invoice_id"] = payment.InvoiceId,
                ["source"] = new JsonObject { ["type"] = "creditcard", ["company"] = "mada", ["message"] = payment.Message, ["number"] = "4201-32XX-XXXX-1010" },
            },
        }.ToJsonString();

    internal static GatewayPayment Map(FakePayment p) =>
        MoyasarGateway.Map(new MoyasarGateway.MoyasarPayment(p.Id, p.Status, p.AmountHalalas, p.Captured, p.Refunded, p.Currency, p.InvoiceId,
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

    public string? Currency { get; set; } = "SAR";

    public string? Message { get; set; }
}

public sealed class FakePaymentGateway(FakePaymentStore store, IOptions<PaymentOptions> options) : IPaymentGateway
{
    public const string ProviderName = "fake";

    public string Name => ProviderName;

    public async Task<HostedPaymentResult> CreateHostedPaymentAsync(HostedPaymentRequest request, CancellationToken ct = default)
    {
        if (store.FailNextCreate)
        {
            store.FailNextCreate = false;
            return HostedPaymentResult.Failed("fake gateway down");
        }

        if (store.CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(store.CreateDelay, ct);
        }

        var invoice = store.AddInvoice(request);
        var api = options.Value.Fake.ApiBaseUrl.TrimEnd('/');
        return HostedPaymentResult.Created(invoice.Id, $"{api}/dev/payments/{invoice.Id}", request.ExpiresAt);
    }

    public async Task<GatewayPayment?> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default)
    {
        await ReadAsync(ct);
        return store.Payment(providerPaymentId) is { } p ? FakePaymentStore.Map(p) : null;
    }

    public async Task<IReadOnlyList<GatewayPayment>> FindPaymentsForHostedPageAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        await ReadAsync(ct);
        return store.Invoice(providerInvoiceId) is { } invoice
            ? [.. invoice.PaymentIds.Select(store.Payment).OfType<FakePayment>().Select(FakePaymentStore.Map)]
            : [];
    }

    private async Task ReadAsync(CancellationToken ct)
    {
        if (store.FailReads)
        {
            throw new PaymentGatewayException("fake gateway down");
        }

        if (store.ReadDelay > TimeSpan.Zero && store.TakeReadDelay())
        {
            await Task.Delay(store.ReadDelay, ct);
        }
    }

    public async Task<GatewayRefundResult> RefundAsync(string providerPaymentId, long amountHalalas, string idempotencyKey, CancellationToken ct = default)
    {
        if (store.RefundDelay > TimeSpan.Zero)
        {
            await Task.Delay(store.RefundDelay, ct);
        }

        var p = store.Payment(providerPaymentId);
        if (store.FailRefunds || p is null || p.Captured - p.Refunded < amountHalalas)
        {
            return GatewayRefundResult.Failed("fake refund refused");
        }

        if (store.PendingRefunds && !store.PendingRefundApplies)
        {
            return GatewayRefundResult.Unknown("fake gateway timeout");
        }

        p.Refunded += amountHalalas;
        if (p.Refunded == p.Captured)
        {
            p.Status = "refunded";
        }

        return store.PendingRefunds ? GatewayRefundResult.Unknown("fake gateway timeout") : GatewayRefundResult.Succeeded($"fake_rf_{Guid.NewGuid():N}");
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
