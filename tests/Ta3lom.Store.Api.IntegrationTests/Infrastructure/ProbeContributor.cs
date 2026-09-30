using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Api.Endpoints;
using Ta3lom.Store.Api.Http;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure;

/// Test-only /v1 endpoints (A1): exercise CORS, rate limiting, Result mapping, exceptions and log redaction.
public sealed partial class ProbeContributor : IV1EndpointContributor
{
    public const string SecretPassword = "hunter2-password-value";
    public const string SecretClientSecret = "client-secret-value";
    public const string SecretRefreshToken = "refresh-token-value";
    public const string SecretApiToken = "nested-api-token-value";
    public const string SetCookieValue = "t3s_rt=new-refresh-cookie-value; Path=/v1/auth; HttpOnly";

    public void Map(RouteGroupBuilder v1)
    {
        v1.MapGet("/__test/ping", () => TypedResults.Ok(new { pong = true }))
          .RequireRateLimiting(RatePolicies.Public)
          .ExcludeFromDescription();

        v1.MapGet("/__test/error/{kind}", (ErrorKind kind) =>
                Result<string>.Fail(new Error("test.code", "Developer message", kind,
                    new Dictionary<string, object?> { ["max"] = "50" })).ToHttp())
          .ExcludeFromDescription();

        v1.MapGet("/__test/ok", () => Result<string>.Ok("fine").ToHttp()).ExcludeFromDescription();

        v1.MapGet("/__test/throw/domain", IResult () => throw new DomainException("unit.invalid_transition", "internal detail"))
          .ExcludeFromDescription();

        v1.MapGet("/__test/throw/crash", IResult () => throw new InvalidOperationException("secret internals at Ta3lom.Store"))
          .ExcludeFromDescription();

        v1.MapGet("/__test/log", (HttpContext http, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger("Probe");
            LogSecrets(logger, SecretPassword, SecretClientSecret, SecretRefreshToken, "safe-value");
            LogCredentials(logger, new Credentials("visible-name", SecretApiToken));
            http.Response.Headers.SetCookie = SetCookieValue;
            return TypedResults.Ok();
        }).ExcludeFromDescription();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Probe {Password} {ClientSecret} {RefreshToken} {Safe}")]
    private static partial void LogSecrets(ILogger logger, string password, string clientSecret, string refreshToken, string safe);

    [LoggerMessage(Level = LogLevel.Information, Message = "Probe credentials {@Credentials}")]
    private static partial void LogCredentials(ILogger logger, Credentials credentials);

    public sealed record Credentials(string UserName, string ApiToken);
}
