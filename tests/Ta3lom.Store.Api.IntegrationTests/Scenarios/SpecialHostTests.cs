using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Api.IntegrationTests.Logging;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — rules that need a differently configured host: the OTP code never reaches the logs (RUL-60), and a failing LMS
/// leaves the enrolment `pending_sync` without charging twice (RUL-42).
public sealed class OtpLoggingTests(OtpLoggingTests.Factory factory) : IClassFixture<OtpLoggingTests.Factory>
{
    [Fact]
    [Rule("RUL-60")]
    public async Task The_OTP_code_and_the_full_mobile_are_never_logged()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await factory.SignInAsync(browser, mobile);
        var code = Regex.Match(factory.SmsTo(mobile)[0].Body, @"\d{6}").Value;
        await Task.Delay(200, TestContext.Current.CancellationToken);                                   // request logs complete after the response

        var everything = string.Join("\n", factory.Sink.Events.Select(e =>
            e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => $"{p.Key}={p.Value}"))));
        everything.ShouldNotBeEmpty();
        everything.ShouldNotContain(code);
        everything.ShouldNotContain(mobile[1..]);                                                        // 5XXXXXXXX never in clear
    }

    public sealed class Factory : ScenarioFactory
    {
        public LogRedactionTests.InMemoryLogSink Sink { get; } = new();

        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
        {
            ["Serilog:MinimumLevel:Default"] = "Debug",
        };

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);
            services.AddSingleton<ILogEventSink>(Sink);
        }
    }
}

public sealed class LmsDownTests(LmsDownTests.Factory factory) : IClassFixture<LmsDownTests.Factory>
{
    [Fact]
    [Rule("RUL-42")]
    public async Task A_failing_LMS_leaves_the_enrolment_pending_sync_and_retries_without_a_second_charge()
    {
        var customer = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(customer, Shop.Buy(await factory.ProductIdAsync("python-kids")));

        (await factory.ScalarAsync<string>($"SELECT status FROM store.enrolment WHERE customer_id = '{customer.Id}'")).ShouldBe("pending_sync");
        (await factory.ScalarAsync<string>($"SELECT fulfilment_status FROM store.order_line WHERE order_id = '{order.OrderId}'")).ShouldBe("pending_sync");
        (await factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE id = '{order.OrderId}'")).ShouldBe("paid");
        var (attempts, pending) = (await factory.ScalarAsync<int>("SELECT max(attempts) FROM store.outbox_message WHERE type = 'LmsEnrol'"),
            await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'LmsEnrol' AND processed_at IS NULL AND last_error IS NOT NULL"));
        attempts.ShouldBeGreaterThanOrEqualTo(1);
        pending.ShouldBe(1);                                                                              // kept for a retry with backoff
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.payment WHERE order_id = '{order.OrderId}'")).ShouldBe(1);
        var enrolment = await factory.ScalarAsync<Guid>($"SELECT id FROM store.enrolment WHERE customer_id = '{customer.Id}'");
        var (launch, problem) = await customer.PostAsync($"/v1/me/enrolments/{enrolment}/launch");
        launch.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("enrolment.not_active");
    }

    public sealed class Factory : ScenarioFactory
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
        {
            ["Store:Lms:SimulateFailure"] = "true",
        };
    }
}
