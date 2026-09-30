using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ta3lom.Store.Api.Endpoints;

/// #134 GET /health/live and #135 GET /health/ready (11 §10, 09 §8.3). Unversioned, no CORS.
public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: process up, no dependencies.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteLiveAsync,
        });

        // Readiness: checks tagged "ready" (PostgreSQL from S-02, outbox, key ring, files, scheduler).
        // Degraded → 200 so a slow dependency does not take the API out of rotation; Unhealthy → 503.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReadyAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });
        return app;
    }

    private static Task WriteLiveAsync(HttpContext http, HealthReport report) =>
        WriteJsonAsync(http, w => w.WriteString("status", report.Status.ToString()));

    /// `{ status, totalDurationMs, checks: [ { name, status, durationMs, data? } ] }` — never exception text.
    private static Task WriteReadyAsync(HttpContext http, HealthReport report) =>
        WriteJsonAsync(http, w =>
        {
            w.WriteString("status", report.Status.ToString());
            w.WriteNumber("totalDurationMs", (long)Math.Round(report.TotalDuration.TotalMilliseconds));
            w.WriteStartArray("checks");
            foreach (var (name, entry) in report.Entries)
            {
                w.WriteStartObject();
                w.WriteString("name", name);
                w.WriteString("status", entry.Status.ToString());
                w.WriteNumber("durationMs", (long)Math.Round(entry.Duration.TotalMilliseconds));
                if (entry.Data.Count > 0)
                {
                    w.WritePropertyName("data");
                    JsonSerializer.Serialize(w, entry.Data);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
        });

    /// Buffers the JSON, then writes it asynchronously (synchronous response I/O is disallowed).
    private static async Task WriteJsonAsync(HttpContext http, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.Body.WriteAsync(buffer.WrittenMemory, http.RequestAborted);
    }
}
