using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.Logging;

namespace Ta3lom.Store.Api.IntegrationTests.Logging;

/// A3: sensitive values never reach a sink (09 §8.1, plan 14 §4.5 log-redaction test).
public sealed class LogRedactionTests(LogRedactionTests.Factory factory) : IClassFixture<LogRedactionTests.Factory>
{
    private const string BearerToken = "eyJhbGciOi.secret-access-token";
    private const string CookieValue = "t3s_rt=raw-refresh-cookie-value";

    [Fact]
    public async Task Sensitive_headers_and_properties_are_redacted_in_request_logs()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/__test/log");
        request.Headers.Add("Authorization", $"Bearer {BearerToken}");
        request.Headers.Add("Cookie", CookieValue);
        request.Headers.Add("X-Refresh-Token", "header-token-value");
        request.Headers.Add("User-Agent", "redaction-test");

        var response = await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var requestLog = await factory.Sink.WaitForAsync(e => e.Properties.ContainsKey("RequestHeaders")
            && e.Properties["RequestPath"].ToString().Contains("__test/log", StringComparison.Ordinal));

        var everything = string.Join("\n", factory.Sink.Events.Select(Render));
        foreach (var secret in new[]
                 {
                     BearerToken, "raw-refresh-cookie-value", "header-token-value", "new-refresh-cookie-value",
                     ProbeContributor.SecretPassword, ProbeContributor.SecretClientSecret,
                     ProbeContributor.SecretRefreshToken, ProbeContributor.SecretApiToken,
                 })
        {
            everything.ShouldNotContain(secret);
        }

        var requestHeaders = (DictionaryValue)requestLog.Properties["RequestHeaders"];
        Value(requestHeaders, "Authorization").ShouldBe(RedactingEnricher.Mask);
        Value(requestHeaders, "Cookie").ShouldBe(RedactingEnricher.Mask);
        Value(requestHeaders, "X-Refresh-Token").ShouldBe(RedactingEnricher.Mask);
        Value(requestHeaders, "User-Agent").ShouldBe("redaction-test");
        Value((DictionaryValue)requestLog.Properties["ResponseHeaders"], "Set-Cookie").ShouldBe(RedactingEnricher.Mask);

        var probe = factory.Sink.Events.Single(e => e.Properties.ContainsKey("Password"));
        probe.Properties["Safe"].ToString().ShouldBe("\"safe-value\"");
        var credentials = factory.Sink.Events.Single(e => e.Properties.ContainsKey("Credentials"));
        credentials.Properties["Credentials"].ToString().ShouldContain("visible-name");
    }

    private static string Render(LogEvent e) =>
        e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " +
        string.Join(" ", e.Properties.Select(p => $"{p.Key}={p.Value}"));

    private static string? Value(DictionaryValue dictionary, string key) =>
        dictionary.Elements.Where(e => string.Equals(e.Key.Value as string, key, StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Value as ScalarValue)?.Value as string).SingleOrDefault();

    public sealed class Factory : ProbeApiFactory
    {
        public InMemoryLogSink Sink { get; } = new();

        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
        {
            ["Serilog:MinimumLevel:Default"] = "Information",
        };

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);
            services.AddSingleton<ILogEventSink>(Sink);
        }
    }

    /// Serilog in-memory sink, collected through ReadFrom.Services.
    public sealed class InMemoryLogSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

        /// Request logs are written when the pipeline completes, which can be just after the client has the response.
        public async Task<LogEvent> WaitForAsync(Func<LogEvent, bool> predicate)
        {
            for (var i = 0; i < 100; i++)
            {
                var match = _events.FirstOrDefault(predicate);
                if (match is not null)
                {
                    return match;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException("Expected log event was not written");
        }
    }
}
