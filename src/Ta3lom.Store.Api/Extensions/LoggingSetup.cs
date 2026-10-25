using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Ta3lom.Store.Api.Extensions;

/// Serilog: compact JSON to stdout, redaction, request logging with headers (redacted).
public static class LoggingSetup
{
    public static IServiceCollection AddStoreLogging(this IServiceCollection services)
    {
        services.AddSerilog((sp, lc) => lc
            .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
            .ReadFrom.Services(sp)                       // extra sinks registered as ILogEventSink (tests)
            .Enrich.FromLogContext()
            .Enrich.With<RedactingEnricher>()
            .WriteTo.Console(new CompactJsonFormatter()),
            preserveStaticLogger: true);                 // each host owns its logger; Log.Logger stays untouched
        return services;
    }

    public static IApplicationBuilder UseStoreRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(o =>
        {
            o.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();   // not the static Log.Logger
            o.GetLevel = (http, _, ex) =>
                ex is BadHttpRequestException { StatusCode: < 500 } ? LogEventLevel.Information   // unbindable body → 4xx (GlobalExceptionHandler)
                : ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
                : http.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            o.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                // Values of sensitive headers (Authorization, Cookie, Set-Cookie …) are masked by RedactingEnricher.
                diagnostics.Set("RequestHeaders", http.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase));
                diagnostics.Set("ResponseHeaders", http.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase));
            };
        });
}
