using Mapster;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Ta3lom.Store.Api;
using Ta3lom.Store.Api.Cli;
using Ta3lom.Store.Api.Endpoints;
using Ta3lom.Store.Api.Extensions;

// CLI verbs run without the web host.
if (args.Length > 0 && args[0] == CreateAdminCommand.Verb)
{
    return CreateAdminCommand.Run(args[1..], Console.Out);
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddStoreSecretFiles();
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

// ── Layers (DMS chaining: AddApi → AddInfrastructure → AddApplication → AddDomain) ─────────────
builder.Services.AddStore(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Explicit Mapster maps must be complete; fails fast on an unmapped member.
    app.Services.GetRequiredService<TypeAdapterConfig>().Compile();
}

// ── Pipeline (order matters: TLS ends at Caddy, so forwarded headers come first) ────────────────
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UsePublicFiles();
app.UseStoreRequestLogging();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOutputCache();

// ── Endpoints ───────────────────────────────────────────────────────────────────────────────────
var v1 = app.MapGroup("/v1").RequireCors(CorsSetup.WebPolicy);
app.MapEndpoints(v1);
app.MapDevEndpoints();
app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();                             // /openapi/v1.json
    app.MapScalarApiReference("/docs").AllowAnonymous();           // Scalar UI
}

await app.RunAsync();
return 0;

/// Entry point; public partial so WebApplicationFactory can host it in tests.
public partial class Program;
