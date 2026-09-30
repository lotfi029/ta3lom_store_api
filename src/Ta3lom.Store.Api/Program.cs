using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Ta3lom.Store.Api.Endpoints;
using Ta3lom.Store.Api.Http;
using Ta3lom.Store.Api.Logging;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Api.Security;
using Ta3lom.Store.Application;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

// ── Configuration: validated at start-up (09 §7.3) ─────────────────────────────────────────────
builder.Services.AddOptions<StoreOptions>()
    .BindConfiguration(StoreOptions.Section)
    .ValidateDataAnnotations()
    .Validate(o => o.Validate().Count == 0, "Store options are invalid; see StoreOptions.Validate()")
    .ValidateOnStart();

// ── Layers ──────────────────────────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();

// ── Cross-cutting HTTP concerns ─────────────────────────────────────────────────────────────────
builder.Services.AddStoreLogging();
builder.Services.AddStoreProblemDetails();
builder.Services.AddStoreForwardedHeaders();
builder.Services.AddStoreCors();
builder.Services.AddStoreRateLimiting();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});
builder.Services.AddRequestLocalization(o =>
{
    string[] cultures = ["ar", "en"];
    o.SetDefaultCulture("ar").AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
    o.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
});
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var otlpEndpoint = builder.Configuration["Store:Telemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ta3lom-store-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("Ta3lom.Store");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter("Ta3lom.Store");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();

// ── Pipeline (order matters: TLS ends at Caddy, so forwarded headers come first) ────────────────
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStoreRequestLogging();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseRouting();
app.UseCors();
app.UseRateLimiter();

// ── Endpoints ───────────────────────────────────────────────────────────────────────────────────
var v1 = app.MapGroup("/v1").RequireCors(CorsSetup.WebPolicy);
foreach (var contributor in app.Services.GetServices<IV1EndpointContributor>())
{
    contributor.Map(v1);
}

app.MapHealthEndpoints();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();                              // /openapi/v1.json
    app.MapScalarApiReference("/docs");            // Scalar UI
}

await app.RunAsync();

/// Entry point; public partial so WebApplicationFactory can host it in tests.
public partial class Program;
