using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Services.Outbox;

namespace Ta3lom.Store.Api.IntegrationTests.Operations;

/// An e-mail provider that delivers and then fails in a way that leaves the outcome unknown (a crash, a lost reply), or
/// refuses outright when asked to.
public sealed class FlakyMailFactory : ScenarioFactory
{
    public ConcurrentQueue<string> Delivered { get; } = new();

    public bool Refuse { get; set; }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(new FlakySender(this));
    }

    private sealed class FlakySender(FlakyMailFactory owner) : IEmailSender
    {
        public string StaffAlertsAddress => "alerts@ta3lom.test";

        public Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (owner.Refuse)
            {
                return Task.FromResult(DeliveryResult.Failed("550 refused"));
            }

            owner.Delivered.Enqueue(message.Subject);
            throw new InvalidOperationException("connection lost after DATA");
        }
    }
}

/// A-2: a notification is sent at most once per recipient. A send whose outcome is unknown (the row is still `sending`
/// when the message comes back) is never repeated: it becomes `unknown` and staff get one alert; a definite refusal is
/// retried.
public sealed class NotificationOnceTests(FlakyMailFactory factory) : IClassFixture<FlakyMailFactory>
{
    private OutboxDispatcher Dispatcher => factory.Services.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

    private async Task<Guid> AlertAsync(string kind)
    {
        await factory.ExecuteSqlAsync("UPDATE store.outbox_message SET processed_at = now() WHERE processed_at IS NULL");
        var id = Guid.CreateVersion7();
        await factory.ExecuteSqlAsync($$"""
            INSERT INTO store.outbox_message (id, type, payload, occurred_at, next_attempt_at, attempts)
            VALUES ('{{id}}', 'StaffAlert', '{"kind":"{{kind}}","reference":null,"detail":"test"}', '2000-01-01', '2000-01-01', 0)
            """);
        return id;
    }

    private async Task RetryNowAsync(Guid id)
    {
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET next_attempt_at = '2000-01-01' WHERE id = '{id}'");
        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_send_with_an_unknown_outcome_is_never_repeated_and_staff_are_alerted_once()
    {
        factory.Refuse = false;
        factory.Delivered.Clear();
        var alert = await AlertAsync("integrity_check");

        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);
        await RetryNowAsync(alert);
        await RetryNowAsync(alert);

        factory.Delivered.Count(s => s.Contains("integrity_check", StringComparison.Ordinal)).ShouldBe(1);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.notification WHERE dedupe_key LIKE '{alert:N}:%'")).ShouldBe("unknown");
        (await factory.ScalarAsync<bool>($"SELECT processed_at IS NOT NULL FROM store.outbox_message WHERE id = '{alert}'")).ShouldBeTrue();
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload->>'kind' = 'notification_unknown' AND payload->>'detail' LIKE '%{alert}%'"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_send_is_retried()
    {
        factory.Refuse = true;
        factory.Delivered.Clear();
        var alert = await AlertAsync("dead_letter_probe");

        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.notification WHERE dedupe_key LIKE '{alert:N}:%'")).ShouldBe("failed");

        factory.Refuse = false;
        await RetryNowAsync(alert);

        factory.Delivered.Count(s => s.Contains("dead_letter_probe", StringComparison.Ordinal)).ShouldBe(1);
    }
}
