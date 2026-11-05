using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Api.IntegrationTests.Logging;

namespace Ta3lom.Store.Api.IntegrationTests.Showcase;

/// Showcase mode (deploy without payments): `Store:Payments:Provider = none`, `Store:Features:CustomerSignIn = false`.
/// Its own database (ScenarioFactory), so the "nothing was written" checks see only what this class did.
public sealed class ShowcaseScenarioFactory : ScenarioFactory
{
    public LogRedactionTests.InMemoryLogSink Sink { get; } = new();

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Store:Payments:Provider"] = "none",
        ["Store:Features:CustomerSignIn"] = "false",
        ["Serilog:MinimumLevel:Default"] = "Information",
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<ILogEventSink>(Sink);
    }
}

/// Production as the owner deploys it before Moyasar: provider none, donor sign-in off, and NO Moyasar or SMS settings
/// at all (no secret files for them on the VPS).
public sealed class ShowcaseProductionFactory : ProductionApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings => base.Settings
        .Where(s => !s.Key.StartsWith("Store:Payments:Moyasar", StringComparison.Ordinal) && !s.Key.StartsWith("Store:Sms:", StringComparison.Ordinal))
        .Concat([new("Store:Payments:Provider", "none"), new("Store:Features:CustomerSignIn", "false")])
        .GroupBy(s => s.Key).ToDictionary(g => g.Key, g => g.Last().Value);
}
