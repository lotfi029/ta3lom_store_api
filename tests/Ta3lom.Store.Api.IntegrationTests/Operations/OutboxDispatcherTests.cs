using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Infrastructure.Services.Outbox;

namespace Ta3lom.Store.Api.IntegrationTests.Operations;

/// L-10: `attempts` grows at every claim; a message is never claimed again once it reached the cap, and one whose last
/// claim never finished (a crash or a hang past the lease) is dead-lettered once, with one staff alert.
public sealed class OutboxDispatcherTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private OutboxDispatcher Dispatcher => factory.Services.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

    private async Task<Guid> InsertAsync(int attempts, string claimedUntil)
    {
        var id = Guid.CreateVersion7();
        var payload = $$"""{"orderId":"{{Guid.NewGuid()}}","reference":"T3-26-999999","totalAmount":"10.00","lineCount":1}""";
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.outbox_message (id, type, payload, occurred_at, next_attempt_at, attempts, claimed_until)
            VALUES ('{id}', 'OrderPlaced', '{payload}', now() - interval '2 hours', now() - interval '2 hours', {attempts}, {claimedUntil})
            """);
        return id;
    }

    [Fact]
    public async Task A_message_whose_last_claim_never_finished_is_dead_lettered_once_with_one_alert()
    {
        var crashed = await InsertAsync(OutboxDispatcher.MaxAttempts, "now() - interval '1 hour'");
        var alertsBefore = await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload->>'kind' = 'outbox_dead_letter'");

        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);
        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);

        (await factory.ScalarAsync<bool>($"SELECT dead_lettered AND processed_at IS NULL FROM store.outbox_message WHERE id = '{crashed}'")).ShouldBeTrue();
        (await factory.ScalarAsync<int>($"SELECT attempts FROM store.outbox_message WHERE id = '{crashed}'")).ShouldBe(OutboxDispatcher.MaxAttempts);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload->>'kind' = 'outbox_dead_letter'"))
            .ShouldBe(alertsBefore + 1);
    }

    [Fact]
    public async Task A_message_at_the_cap_is_never_claimed_again_while_a_lease_runs()
    {
        var running = await InsertAsync(OutboxDispatcher.MaxAttempts, "now() + interval '1 hour'");

        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);

        (await factory.ScalarAsync<int>($"SELECT attempts FROM store.outbox_message WHERE id = '{running}'")).ShouldBe(OutboxDispatcher.MaxAttempts);
        (await factory.ScalarAsync<bool>($"SELECT processed_at IS NULL AND NOT dead_lettered FROM store.outbox_message WHERE id = '{running}'")).ShouldBeTrue();
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET processed_at = now() WHERE id = '{running}'");
    }

    [Fact]
    public async Task Every_claim_counts_an_attempt()
    {
        var fresh = await InsertAsync(0, "NULL");

        await Dispatcher.RunOnceAsync(TestContext.Current.CancellationToken);

        (await factory.ScalarAsync<int>($"SELECT attempts FROM store.outbox_message WHERE id = '{fresh}'")).ShouldBe(1);
        (await factory.ScalarAsync<bool>($"SELECT processed_at IS NOT NULL FROM store.outbox_message WHERE id = '{fresh}'")).ShouldBeTrue();
    }
}
