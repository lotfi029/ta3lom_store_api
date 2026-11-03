using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Payments;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Endpoints;

/// `POST /v1/payments/webhooks/{provider}` (ADR-S05, A-2). Mapped on the app, NOT on the `/v1` group: no CORS policy
/// (server-to-server), no same-origin filter, anonymous, its own rate limit. The body is capped at 256 KB BEFORE it is
/// read (Content-Length checked, the stream read with a bound); the raw bytes go to the gateway's verification.
public static class PaymentWebhookEndpoints
{
    public const int MaxBodyBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapPaymentWebhooks(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/v1/payments/webhooks/{provider}", ReceiveAsync).WithTags("Payments").AllowAnonymous()
            .RequireRateLimiting(RatePolicies.Webhook).DisableAntiforgery()
            .Accepts<object>("application/json").Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> ReceiveAsync(string provider, HttpContext http,
        [FromServices] ICommandHandler<PaymentWebhookCommand, string> handler, CancellationToken ct)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return TooLarge();
        }

        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        try
        {
            int read;
            while (total < buffer.Length && (read = await http.Request.Body.ReadAsync(buffer.AsMemory(total), ct)) > 0)
            {
                total += read;
            }
        }
        catch (BadHttpRequestException)
        {
            return TooLarge();
        }

        if (total > MaxBodyBytes)
        {
            return TooLarge();
        }

        var headers = http.Request.Headers
            .Where(h => !h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && !h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var result = await handler.HandleAsync(new PaymentWebhookCommand(provider, buffer.AsMemory(0, total), headers), ct);
        return result.ToHttp(outcome => TypedResults.Ok(new { outcome }));
    }

    private static IResult TooLarge() => Error.TooLarge(ErrorCodes.HttpError, "The webhook body is larger than 256 KB.").ToProblem();
}
