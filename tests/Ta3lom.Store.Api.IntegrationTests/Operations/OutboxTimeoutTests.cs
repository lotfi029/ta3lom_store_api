using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Services.Outbox;
using Ta3lom.Store.Infrastructure.Services.Sms;

namespace Ta3lom.Store.Api.IntegrationTests.Operations;

/// A rebuild trigger whose HTTP call runs into the client timeout (a `TaskCanceledException` that is not a shutdown).
public sealed class TimingOutRebuildFactory : ScenarioFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<ISiteRebuildTrigger>();
        services.AddSingleton<ISiteRebuildTrigger, TimingOutTrigger>();
    }

    private sealed class TimingOutTrigger : ISiteRebuildTrigger
    {
        public Task<bool> TriggerAsync(string reason, CancellationToken ct = default) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
    }
}

/// A-1: an HTTP timeout inside a handler is a failed attempt (retried with backoff), never an exception that leaves the
/// dispatcher (it would stop the host); an SMS timeout is a failed send, not a 500.
public sealed class OutboxTimeoutTests(TimingOutRebuildFactory factory) : IClassFixture<TimingOutRebuildFactory>
{
    private OutboxDispatcher Dispatcher => factory.Services.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

    [Fact]
    public async Task A_handler_timeout_is_a_failed_attempt_and_the_rest_of_the_batch_runs()
    {
        await factory.ExecuteSqlAsync("UPDATE store.outbox_message SET processed_at = now() WHERE processed_at IS NULL");
        var rebuild = Guid.CreateVersion7();
        var order = Guid.CreateVersion7();
        var placed = $$"""{"orderId":"{{Guid.NewGuid()}}","reference":"T3-26-999998","totalAmount":"10.00","lineCount":1}""";
        await factory.ExecuteSqlAsync($$"""
            INSERT INTO store.outbox_message (id, type, payload, occurred_at, next_attempt_at, attempts)
            VALUES ('{{rebuild}}', 'CatalogChanged', '{"entity":"project","id":"{{Guid.NewGuid()}}","change":"published"}', now() - interval '2 minutes', now() - interval '2 minutes', 0),
                   ('{{order}}', 'OrderPlaced', '{{placed}}', now() - interval '1 minute', now() - interval '1 minute', 0)
            """);

        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);   // an escaping exception would stop the host

        (await factory.ScalarAsync<bool>($"""
            SELECT processed_at IS NULL AND claimed_until IS NULL AND last_error IS NOT NULL AND next_attempt_at > now() AND NOT dead_lettered
              FROM store.outbox_message WHERE id = '{rebuild}'
            """)).ShouldBeTrue();
        (await factory.ScalarAsync<bool>($"SELECT processed_at IS NOT NULL FROM store.outbox_message WHERE id = '{order}'")).ShouldBeTrue();
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET processed_at = now() WHERE id = '{rebuild}'");
    }

    [Fact]
    public async Task An_sms_provider_timeout_is_a_failed_send()
    {
        using var http = new HttpClient(new TimingOutHandler());
        var sender = new UnifonicSmsSender(http,
            Microsoft.Extensions.Options.Options.Create(new SmsOptions { Provider = "unifonic", AppSid = "app", SenderId = "ta3lom", BaseUrl = "https://sms.example.test" }),
            NullLogger<UnifonicSmsSender>.Instance);

        var result = await sender.SendAsync("+966512345678", "code", TestContext.Current.CancellationToken);

        result.Sent.ShouldBeFalse();
    }

    private sealed class TimingOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
    }
}
