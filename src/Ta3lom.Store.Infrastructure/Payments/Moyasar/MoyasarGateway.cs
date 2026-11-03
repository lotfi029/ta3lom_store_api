using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Enums;

namespace Ta3lom.Store.Infrastructure.Payments.Moyasar;

/// Moyasar adapter (DECISIONS F2a: endpoints as read in docs.moyasar.com). Hosted page = an invoice; HTTP Basic with the
/// secret key as user name and an empty password; amounts in halalas. GETs are retried twice on a transport error or a
/// 5xx (0.5 s, 1.5 s); writes are never retried (a duplicate invoice or refund would be worse than an error).
internal sealed partial class MoyasarGateway(HttpClient http, IOptions<PaymentOptions> options, ILogger<MoyasarGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "moyasar";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan[] GetBackoff = [TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500)];

    public string Name => ProviderName;

    private MoyasarOptions O => options.Value.Moyasar;

    public async Task<HostedPaymentResult> CreateHostedPaymentAsync(HostedPaymentRequest request, CancellationToken ct = default)
    {
        var body = new CreateInvoice(request.AmountHalalas, "SAR", request.Description, request.ReturnUrl, request.ReturnUrl,
            request.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        try
        {
            using var message = Request(HttpMethod.Post, "invoices");
            message.Content = JsonContent.Create(body, options: Json);
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, "create invoice", (int)response.StatusCode, request.OrderReference);
                return HostedPaymentResult.Failed($"moyasar status {(int)response.StatusCode}");
            }

            var invoice = await response.Content.ReadFromJsonAsync<Invoice>(Json, ct);
            return invoice is { Id.Length: > 0, Url.Length: > 0 }
                ? HostedPaymentResult.Created(invoice.Id, invoice.Url, ParseTime(invoice.ExpiredAt) ?? request.ExpiresAt)
                : HostedPaymentResult.Failed("moyasar invoice without id or url");
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            LogUnreachable(logger, "create invoice", ex.GetType().Name);
            return HostedPaymentResult.Failed(ex.GetType().Name);
        }
    }

    public async Task<GatewayPayment?> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default)
    {
        var payment = await GetAsync<MoyasarPayment>($"payments/{Uri.EscapeDataString(providerPaymentId)}", ct);
        return payment is null ? null : Map(payment);
    }

    public async Task<IReadOnlyList<GatewayPayment>> FindPaymentsForHostedPageAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        var invoice = await GetAsync<Invoice>($"invoices/{Uri.EscapeDataString(providerInvoiceId)}", ct);
        return invoice?.Payments is { } list ? [.. list.Select(p => Map(p with { InvoiceId = p.InvoiceId ?? invoice.Id }))] : [];
    }

    public async Task<GatewayRefundResult> RefundAsync(string providerPaymentId, long amountHalalas, string idempotencyKey, CancellationToken ct = default)
    {
        // Moyasar documents no refund idempotency: the `refund` row (requested before this call, unique per Idempotency-Key)
        // and the order row lock keep a refund from being sent twice.
        try
        {
            using var message = Request(HttpMethod.Post, $"payments/{Uri.EscapeDataString(providerPaymentId)}/refund");
            message.Content = JsonContent.Create(new RefundBody(amountHalalas), options: Json);
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, ct);
                LogRefused(logger, "refund", (int)response.StatusCode, providerPaymentId);
                return GatewayRefundResult.Failed(error);
            }

            var payment = await response.Content.ReadFromJsonAsync<MoyasarPayment>(Json, ct);
            return GatewayRefundResult.Succeeded(payment?.Id);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            LogUnreachable(logger, "refund", ex.GetType().Name);
            return GatewayRefundResult.Failed(ex.GetType().Name);
        }
    }

    public async Task CancelHostedPaymentAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        try
        {
            using var message = Request(HttpMethod.Put, $"invoices/{Uri.EscapeDataString(providerInvoiceId)}/cancel");
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, "cancel invoice", (int)response.StatusCode, providerInvoiceId);
            }
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            LogUnreachable(logger, "cancel invoice", ex.GetType().Name);
        }
    }

    public WebhookVerification VerifyWebhook(ReadOnlySpan<byte> rawBody, IReadOnlyDictionary<string, string> headers) =>
        Verify(rawBody, O.WebhookSecret, expectLive: O.IsLiveKey);

    /// Shared with the fake: same body scheme, its own secret. `expectLive` null = either mode.
    internal static WebhookVerification Verify(ReadOnlySpan<byte> rawBody, string secret, bool? expectLive)
    {
        var root = WebhookSecrets.Parse(rawBody);
        if (root is null)
        {
            return WebhookVerification.Invalid(null, "{}");
        }

        var redacted = WebhookSecrets.Redact(root);
        var eventId = Str(root["id"]);
        var presented = Str(root["secret_token"]);
        var live = root["live"] is JsonValue lv && lv.TryGetValue<bool>(out var l) ? l : (bool?)null;
        if (!WebhookSecrets.Matches(presented, secret) || eventId is null || (expectLive is { } want && live != want))
        {
            return WebhookVerification.Invalid(eventId, redacted);
        }

        var data = root["data"] as JsonObject;
        return new WebhookVerification(true, eventId, Str(root["type"]), Str(data?["id"]), Str(data?["invoice_id"]), redacted);

        static string? Str(JsonNode? node) =>
            node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    internal static GatewayPayment Map(MoyasarPayment p)
    {
        var status = p.Status switch
        {
            "paid" or "captured" => p.Refunded > 0 && p.Refunded >= Math.Max(p.Captured, p.Amount) ? GatewayPaymentStatus.Refunded : GatewayPaymentStatus.Captured,
            "refunded" => GatewayPaymentStatus.Refunded,
            "authorized" => GatewayPaymentStatus.Authorized,
            "failed" or "expired" => GatewayPaymentStatus.Failed,
            "voided" => GatewayPaymentStatus.Voided,
            _ => GatewayPaymentStatus.Initiated,   // initiated, verified (3DS pending)
        };
        // A paid invoice payment is captured automatically; `captured` can be 0 on older payloads → the amount.
        var captured = status is GatewayPaymentStatus.Captured or GatewayPaymentStatus.Refunded ? (p.Captured > 0 ? p.Captured : p.Amount) : 0;
        var method = (p.Source?.Type, p.Source?.Company) switch
        {
            (_, "mada") => PaymentMethod.Mada,
            ("applepay", _) => PaymentMethod.Applepay,
            ("stcpay", _) => PaymentMethod.Stcpay,
            ("creditcard", _) => PaymentMethod.Creditcard,
            _ => PaymentMethod.Unknown,
        };
        var failed = status is GatewayPaymentStatus.Failed or GatewayPaymentStatus.Voided;
        return new GatewayPayment(p.Id, p.InvoiceId, status, p.Amount, captured, p.Refunded, p.Currency ?? "SAR", method,
            failed ? p.Status : null, failed ? p.Source?.Message : null, p.Live ?? false);
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, new Uri(new Uri(O.BaseUrl.EndsWith('/') ? O.BaseUrl : O.BaseUrl + "/"), path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{O.SecretKey}:")));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
        where T : class
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var message = Request(HttpMethod.Get, path);
                using var response = await http.SendAsync(message, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if ((int)response.StatusCode >= 500 && attempt < GetBackoff.Length)
                {
                    await Task.Delay(GetBackoff[attempt], ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new PaymentGatewayException($"moyasar GET {path.Split('/')[0]} answered {(int)response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<T>(Json, ct);
            }
            catch (Exception ex) when (IsTransport(ex, ct))
            {
                if (attempt >= GetBackoff.Length)
                {
                    throw new PaymentGatewayException("moyasar unreachable", ex);
                }

                await Task.Delay(GetBackoff[attempt], ct);
            }
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MoyasarError>(Json, ct);
            return error?.Message ?? error?.Type ?? $"moyasar status {(int)response.StatusCode}";
        }
        catch (JsonException)
        {
            return $"moyasar status {(int)response.StatusCode}";
        }
    }

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested) || ex is JsonException;

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moyasar refused {Operation} with status {Status} for {Reference}")]
    private static partial void LogRefused(ILogger logger, string operation, int status, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moyasar unreachable during {Operation}: {Error}")]
    private static partial void LogUnreachable(ILogger logger, string operation, string error);

    private sealed record CreateInvoice(long Amount, string Currency, string Description, string SuccessUrl, string BackUrl, string ExpiredAt);

    private sealed record RefundBody(long Amount);

    internal sealed record Invoice(string Id, string? Status, long Amount, string? Url, string? ExpiredAt, IReadOnlyList<MoyasarPayment>? Payments);

    internal sealed record MoyasarPayment(string Id, string? Status, long Amount, long Captured, long Refunded, string? Currency,
        string? InvoiceId, MoyasarSource? Source, bool? Live);

    internal sealed record MoyasarSource(string? Type, string? Company, string? Message);

    private sealed record MoyasarError(string? Type, string? Message);
}
