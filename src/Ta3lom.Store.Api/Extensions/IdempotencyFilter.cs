using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Extensions;

/// `Idempotency-Key`: scope `{prefix}:{sub}`; the hash covers the canonical JSON of the bound body.
/// Same key + same body → the stored response (`Idempotent-Replayed: true`); another body → 422; still running → 409;
/// a 5xx releases the key so the client may retry.
internal sealed class IdempotencyFilter(string scopePrefix, bool required) : IEndpointFilter
{
    public const string Header = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return required ? IdempotencyErrors.KeyRequired.ToProblem() : await next(context);
        }

        if (key.Length is < 8 or > 64)
        {
            return Error.Validation(new Dictionary<string, string[]> { [Header] = ["8–64 characters."] }).ToProblem();
        }

        var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var body = context.Arguments.FirstOrDefault(a => a?.GetType().Namespace?.StartsWith("Ta3lom.Store.Application.DTOs", StringComparison.Ordinal) == true);
        var hash = Convert.ToHexStringLower(SHA256.HashData(body is null ? [] : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), json)));
        var scope = $"{scopePrefix}:{http.User.FindFirst("sub")?.Value ?? "anonymous"}";

        var store = http.RequestServices.GetRequiredService<IIdempotencyStore>();
        var ct = http.RequestAborted;
        var claim = await store.ClaimAsync(scope, key, hash, ct);
        switch (claim.Status)
        {
            case IdempotencyClaimStatus.Mismatch:
                return IdempotencyErrors.KeyReused.ToProblem();
            case IdempotencyClaimStatus.InProgress:
                return IdempotencyErrors.InProgress.ToProblem();
            case IdempotencyClaimStatus.Replay:
                http.Response.Headers["Idempotent-Replayed"] = "true";
                var status = claim.StatusCode ?? StatusCodes.Status200OK;
                return Results.Text(claim.ResponseJson ?? "null", status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, status);
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await store.ReleaseAsync(scope, key, CancellationToken.None);
            throw;
        }

        var code = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (code >= 500)
        {
            await store.ReleaseAsync(scope, key, CancellationToken.None);
        }
        else
        {
            var value = (result as IValueHttpResult)?.Value;
            await store.CompleteAsync(scope, key, code, value is null ? "null" : JsonSerializer.Serialize(value, value.GetType(), json), CancellationToken.None);
        }

        return result;
    }
}

public static class IdempotencyExtensions
{
    public static TBuilder RequireIdempotencyKey<TBuilder>(this TBuilder builder, string scopePrefix, bool required = true)
        where TBuilder : IEndpointConventionBuilder => builder.AddEndpointFilter(new IdempotencyFilter(scopePrefix, required));
}
