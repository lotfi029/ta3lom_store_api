using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Infrastructure.Payments;
using Ta3lom.Store.Infrastructure.Payments.Moyasar;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Moyasar adapter contract (DECISIONS F2a) on recorded fixtures: request shape, auth, halalas, mapping, retries, webhook
/// verification. No network.
public sealed class MoyasarGatewayTests
{
    private const string Secret = "whsec_test_0123456789abcdef";

    private static (MoyasarGateway Gateway, StubHandler Handler) Make(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new StubHandler(responses);
        var options = Microsoft.Extensions.Options.Options.Create(new PaymentOptions
        {
            Provider = "moyasar",
            Moyasar = new MoyasarOptions { SecretKey = "sk_test_abc", WebhookSecret = Secret, BaseUrl = "https://api.moyasar.com/v1/" },
        });
        return (new MoyasarGateway(new HttpClient(handler), options, NullLogger<MoyasarGateway>.Instance), handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(HttpStatusCode code, string body) =>
        _ => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Create_invoice_posts_halalas_with_basic_auth_and_returns_the_checkout_url()
    {
        var (gateway, handler) = Make(Json(HttpStatusCode.Created, MoyasarFixtures.CreatedInvoice));
        var result = await gateway.CreateHostedPaymentAsync(new HostedPaymentRequest(Guid.NewGuid(), "T3-26-000001", 35000, "T3-26-000001",
            "https://store.ta3lom.sa/ar/orders/T3-26-000001?t=x", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue();
        result.ProviderInvoiceId.ShouldBe(MoyasarFixtures.InvoiceId);
        result.RedirectUrl!.ShouldStartWith("https://checkout.moyasar.com/invoices/");
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe("https://api.moyasar.com/v1/invoices");
        request.Authorization.ShouldBe("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("sk_test_abc:")));
        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("amount").GetInt64().ShouldBe(35000);
        body.RootElement.GetProperty("currency").GetString().ShouldBe("SAR");
        body.RootElement.GetProperty("success_url").GetString()!.ShouldContain("/ar/orders/T3-26-000001");
        body.RootElement.GetProperty("back_url").GetString()!.ShouldContain("/ar/orders/T3-26-000001");
        body.RootElement.GetProperty("expired_at").GetString().ShouldBe("2026-11-04T10:00:00.000Z");
        // Review 04 P-5: every page carries our ids.
        body.RootElement.GetProperty("metadata").GetProperty("order_ref").GetString().ShouldBe("T3-26-000001");
        Guid.TryParse(body.RootElement.GetProperty("metadata").GetProperty("payment_id").GetString(), out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_or_failing_create_is_not_retried_and_reports_failure(HttpStatusCode code)
    {
        var (gateway, handler) = Make(Json(code, """{"type":"invalid_request","message":"bad"}"""));
        var result = await gateway.CreateHostedPaymentAsync(new HostedPaymentRequest(Guid.NewGuid(), "T3-26-000001", 35000, "d", "https://x/", DateTimeOffset.UnixEpoch), TestContext.Current.CancellationToken);
        result.Ok.ShouldBeFalse();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_transport_failure_on_create_is_a_failure_not_an_exception()
    {
        var (gateway, _) = Make(_ => throw new HttpRequestException("down"));
        (await gateway.CreateHostedPaymentAsync(new HostedPaymentRequest(Guid.NewGuid(), "T3", 100, "d", "https://x/", DateTimeOffset.UnixEpoch), TestContext.Current.CancellationToken)).Ok.ShouldBeFalse();
    }

    [Theory]
    [InlineData("paid", "mada", "creditcard", GatewayPaymentStatus.Captured, PaymentMethod.Mada)]
    [InlineData("captured", "visa", "creditcard", GatewayPaymentStatus.Captured, PaymentMethod.Creditcard)]
    [InlineData("paid", "visa", "applepay", GatewayPaymentStatus.Captured, PaymentMethod.Applepay)]
    [InlineData("paid", "", "stcpay", GatewayPaymentStatus.Captured, PaymentMethod.Stcpay)]
    [InlineData("failed", "mada", "creditcard", GatewayPaymentStatus.Failed, PaymentMethod.Mada)]
    [InlineData("initiated", "mada", "creditcard", GatewayPaymentStatus.Initiated, PaymentMethod.Mada)]
    [InlineData("authorized", "mada", "creditcard", GatewayPaymentStatus.Authorized, PaymentMethod.Mada)]
    [InlineData("voided", "mada", "creditcard", GatewayPaymentStatus.Voided, PaymentMethod.Mada)]
    [InlineData("refunded", "mada", "creditcard", GatewayPaymentStatus.Refunded, PaymentMethod.Mada)]
    public async Task Fetch_payment_maps_status_and_method(string status, string company, string type, GatewayPaymentStatus expected, PaymentMethod method)
    {
        var captured = status is "paid" or "captured" or "refunded" ? 35000 : 0;
        var (gateway, handler) = Make(Json(HttpStatusCode.OK, MoyasarFixtures.Payment(status, captured: captured, company: company, type: type,
            message: status == "failed" ? "INSUFFICIENT FUNDS" : "APPROVED")));
        var payment = await gateway.GetPaymentAsync(MoyasarFixtures.PaymentId, TestContext.Current.CancellationToken);

        payment.ShouldNotBeNull();
        payment.Status.ShouldBe(expected);
        payment.Method.ShouldBe(method);
        payment.ProviderInvoiceId.ShouldBe(MoyasarFixtures.InvoiceId);
        handler.Requests.ShouldHaveSingleItem().Uri.ShouldBe($"https://api.moyasar.com/v1/payments/{MoyasarFixtures.PaymentId}");
        if (expected == GatewayPaymentStatus.Captured)
        {
            payment.CapturedHalalas.ShouldBe(35000);
        }

        if (expected == GatewayPaymentStatus.Failed)
        {
            payment.FailureMessage.ShouldBe("INSUFFICIENT FUNDS");
            payment.CapturedHalalas.ShouldBe(0);
        }
    }

    /// Review 04 A-2 (replaces "a partially refunded paid payment stays captured", which let settlement ignore the
    /// refunded part): the payment is still captured and carries what the gateway refunded, so settlement imports it.
    [Theory]
    [InlineData("paid", 10000, GatewayPaymentStatus.Captured)]
    [InlineData("paid", 35000, GatewayPaymentStatus.Refunded)]
    [InlineData("refunded", 35000, GatewayPaymentStatus.Refunded)]
    public async Task A_refunded_amount_is_always_carried_for_settlement_to_import(string status, long refunded, GatewayPaymentStatus expected)
    {
        var (gateway, _) = Make(Json(HttpStatusCode.OK, MoyasarFixtures.Payment(status, refunded: refunded)));
        var payment = (await gateway.GetPaymentAsync(MoyasarFixtures.PaymentId, TestContext.Current.CancellationToken))!;
        payment.Status.ShouldBe(expected);
        payment.CapturedHalalas.ShouldBe(35000);
        payment.RefundedHalalas.ShouldBe(refunded);
    }

    [Fact]
    public async Task S2_a_payment_without_a_currency_has_none_never_SAR()
    {
        var (gateway, _) = Make(Json(HttpStatusCode.OK, MoyasarFixtures.Payment("paid").Replace("\"currency\":\"SAR\",", "", StringComparison.Ordinal)));
        (await gateway.GetPaymentAsync(MoyasarFixtures.PaymentId, TestContext.Current.CancellationToken))!.Currency.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task P3_a_refund_answered_5xx_is_unknown_not_failed(HttpStatusCode code)
    {
        var (gateway, handler) = Make(Json(code, "{}"));
        var result = await gateway.RefundAsync(MoyasarFixtures.PaymentId, 100, "key-1", TestContext.Current.CancellationToken);
        result.Ok.ShouldBeFalse();
        result.Pending.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(1);                     // writes are never retried
    }

    [Fact]
    public async Task P3_a_refund_whose_connection_drops_or_times_out_is_unknown_not_failed()
    {
        var (dropped, _) = Make(_ => throw new HttpRequestException("reset"));
        (await dropped.RefundAsync(MoyasarFixtures.PaymentId, 100, "key-1", TestContext.Current.CancellationToken)).Pending.ShouldBeTrue();

        var (slow, _) = Make(_ => throw new TaskCanceledException("timeout"));
        using var budget = new CancellationTokenSource();
        await budget.CancelAsync();
        (await slow.RefundAsync(MoyasarFixtures.PaymentId, 100, "key-2", budget.Token)).Pending.ShouldBeTrue();
    }

    [Fact]
    public async Task GETs_are_retried_on_5xx_and_a_404_is_null()
    {
        var (gateway, handler) = Make(Json(HttpStatusCode.BadGateway, "{}"), Json(HttpStatusCode.OK, MoyasarFixtures.Payment("paid")));
        (await gateway.GetPaymentAsync(MoyasarFixtures.PaymentId, TestContext.Current.CancellationToken))!.Status.ShouldBe(GatewayPaymentStatus.Captured);
        handler.Requests.Count.ShouldBe(2);

        var (missing, _) = Make(Json(HttpStatusCode.NotFound, """{"type":"record_not_found"}"""));
        (await missing.GetPaymentAsync("nope", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task An_unreachable_gateway_throws_after_the_retries()
    {
        var (gateway, handler) = Make(_ => throw new HttpRequestException("down"));
        await Should.ThrowAsync<PaymentGatewayException>(() => gateway.GetPaymentAsync(MoyasarFixtures.PaymentId, TestContext.Current.CancellationToken));
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Payments_of_an_invoice_come_from_show_invoice()
    {
        var (gateway, handler) = Make(Json(HttpStatusCode.OK, MoyasarFixtures.InvoiceWithPayment("paid")));
        var list = await gateway.FindPaymentsForHostedPageAsync(MoyasarFixtures.InvoiceId, TestContext.Current.CancellationToken);
        list.ShouldHaveSingleItem().Status.ShouldBe(GatewayPaymentStatus.Captured);
        handler.Requests.ShouldHaveSingleItem().Uri.ShouldBe($"https://api.moyasar.com/v1/invoices/{MoyasarFixtures.InvoiceId}");
    }

    [Fact]
    public async Task Refund_posts_the_amount_in_halalas_once()
    {
        var (gateway, handler) = Make(Json(HttpStatusCode.OK, MoyasarFixtures.Payment("refunded", refunded: 35000)));
        var result = await gateway.RefundAsync(MoyasarFixtures.PaymentId, 12345, "key-1", TestContext.Current.CancellationToken);
        result.Ok.ShouldBeTrue();
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe($"https://api.moyasar.com/v1/payments/{MoyasarFixtures.PaymentId}/refund");
        request.Body.ShouldBe("""{"amount":12345}""");

        var (refused, refusedHandler) = Make(Json(HttpStatusCode.BadRequest, """{"type":"invalid_request","message":"Amount exceeds"}"""));
        var failed = await refused.RefundAsync(MoyasarFixtures.PaymentId, 1, "key-2", TestContext.Current.CancellationToken);
        failed.Ok.ShouldBeFalse();
        failed.Pending.ShouldBeFalse();                         // a 4xx is a definite refusal
        failed.Error.ShouldBe("Amount exceeds");
        refusedHandler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_is_a_PUT_and_never_throws()
    {
        var (gateway, handler) = Make(Json(HttpStatusCode.OK, """{"status":"canceled"}"""));
        await gateway.CancelHostedPaymentAsync(MoyasarFixtures.InvoiceId, TestContext.Current.CancellationToken);
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Put);
        request.Uri.ShouldBe($"https://api.moyasar.com/v1/invoices/{MoyasarFixtures.InvoiceId}/cancel");
        var (down, _) = Make(_ => throw new HttpRequestException("down"));
        await down.CancelHostedPaymentAsync("x", TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_webhook_with_the_shared_secret_is_valid_and_its_stored_copy_is_redacted()
    {
        var (gateway, _) = Make();
        var v = gateway.VerifyWebhook(Encoding.UTF8.GetBytes(MoyasarFixtures.Webhook(Secret)), new Dictionary<string, string>());
        v.Valid.ShouldBeTrue();
        v.EventId.ShouldBe("evt-1");
        v.Type.ShouldBe("payment_paid");
        v.ProviderPaymentId.ShouldBe(MoyasarFixtures.PaymentId);
        v.ProviderInvoiceId.ShouldBe(MoyasarFixtures.InvoiceId);
        v.RedactedPayload.ShouldNotContain(Secret);
        v.RedactedPayload.ShouldNotContain("4201-32XX");
        v.RedactedPayload.ShouldNotContain("10.0.0.1");
        v.RedactedPayload.ShouldContain("\"company\":\"mada\"");
    }

    [Theory]
    [InlineData("wrong-secret-0123456789", false)]
    [InlineData(Secret, true)]                  // live event against a test key
    public void A_wrong_secret_or_a_live_mode_mismatch_is_invalid(string secret, bool live)
    {
        var (gateway, _) = Make();
        var v = gateway.VerifyWebhook(Encoding.UTF8.GetBytes(MoyasarFixtures.Webhook(secret, live: live)), new Dictionary<string, string>());
        v.Valid.ShouldBeFalse();
        v.EventId.ShouldBe("evt-1");
        v.RedactedPayload.ShouldNotContain(secret);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":"payment_paid","secret_token":"whsec_test_0123456789abcdef"}""")]   // no event id
    public void Malformed_webhooks_are_invalid(string body)
    {
        var (gateway, _) = Make();
        gateway.VerifyWebhook(Encoding.UTF8.GetBytes(body), new Dictionary<string, string>()).Valid.ShouldBeFalse();
    }

    internal sealed record Seen(HttpMethod Method, string Uri, string? Authorization, string? Body);

    internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            var respond = responses[Math.Min(_next++, responses.Length - 1)];
            return respond(request);
        }
    }
}
