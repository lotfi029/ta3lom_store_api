using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

namespace Ta3lom.Store.Api.IntegrationTests.Health;

/// L-3: readiness reports a stuck outbox as Degraded (still 200, so the instance keeps its traffic).
public sealed class OutboxHealthTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<JsonElement> OutboxCheckAsync()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();
        return body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "outbox");
    }

    [Fact]
    public async Task A_dead_lettered_or_overdue_message_makes_the_outbox_degraded()
    {
        await factory.ExecuteSqlAsync("UPDATE store.outbox_message SET processed_at = now() WHERE processed_at IS NULL");
        (await OutboxCheckAsync()).GetProperty("status").GetString().ShouldBe("Healthy");

        var dead = Guid.CreateVersion7();
        await factory.ExecuteSqlAsync($$"""
            INSERT INTO store.outbox_message (id, type, payload, occurred_at, next_attempt_at, attempts, dead_lettered)
            VALUES ('{{dead}}', 'StaffAlert', '{"kind":"probe","reference":null,"detail":null}', now(), now(), 10, true)
            """);
        (await OutboxCheckAsync()).GetProperty("status").GetString().ShouldBe("Degraded");

        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET processed_at = now() WHERE id = '{dead}'");
        var stuck = Guid.CreateVersion7();
        await factory.ExecuteSqlAsync($$"""
            INSERT INTO store.outbox_message (id, type, payload, occurred_at, next_attempt_at, attempts)
            VALUES ('{{stuck}}', 'StaffAlert', '{"kind":"probe","reference":null,"detail":null}', now() - interval '1 hour', now() - interval '1 hour', 0)
            """);
        (await OutboxCheckAsync()).GetProperty("status").GetString().ShouldBe("Degraded");
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET processed_at = now() WHERE id = '{stuck}'");
    }
}
