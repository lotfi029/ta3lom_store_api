using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Allocation.Commands;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-3 / T-4 — units, pooling and allocation through the API (plan 14 S-23, S-25). Each test uses its own product so the
/// FIFO queues do not interfere.
public sealed class GivingScenarioTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private ActorClient? _admin;
    private ActorClient? _officer;

    private async Task<ActorClient> AdminAsync() => _admin ??= await factory.StaffAsync(Roles.Admin);

    private async Task<ActorClient> OfficerAsync() => _officer ??= await factory.StaffAsync(Roles.AllocationOfficer);

    private async Task<(Guid Id, string Code, string Mobile)> ApprovedBeneficiaryAsync(string kind, string group, bool approve = true, bool zakat = false)
    {
        var admin = await AdminAsync();
        var mobile = ScenarioFactory.NewMobile();
        var (created, b) = await admin.PostAsync("/v1/admin/beneficiaries", new
        {
            kind,
            beneficiaryGroup = group,
            region = "القصيم",
            fullName = "مستفيد تجربة",
            mobile,
            guardianName = kind == "student" ? "ولي أمر" : null,
            organisationName = "جهة تجربة",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, b.ToString());
        var id = b.GetProperty("id").GetGuid();
        if (approve)
        {
            var (ok, e) = await admin.PostAsync($"/v1/admin/beneficiaries/{id}/eligibility", new { status = "approved", zakatEligible = zakat, notes = "T-4" });
            ok.StatusCode.ShouldBe(HttpStatusCode.OK, e.ToString());
        }

        return (id, b.GetProperty("code").GetString()!, mobile);
    }

    private static Task<(HttpResponseMessage Response, JsonElement Body)> AllocateAsync(ActorClient officer, Guid product, params Guid[] ids) =>
        officer.PostAsync($"/v1/admin/allocation/products/{product}/allocate", new { beneficiaryIds = ids, note = "T-4" });

    [Fact]
    [Rule("RUL-43")]
    public async Task A_units_line_of_quantity_N_creates_N_units_linked_to_the_line()
    {
        var product = await factory.ProductIdAsync("letters-tales");
        var order = await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(product, 3));

        (await factory.ScalarAsync<long>($"""
            SELECT count(*) FROM store.sponsored_unit u JOIN store.order_line l ON l.id = u.order_line_id
             WHERE l.order_id = '{order.OrderId}' AND u.source = 'full' AND u.status = 'awaiting_allocation'
            """)).ShouldBe(3);
    }

    [Fact]
    [Rule("RUL-45")]
    public async Task Parallel_contributions_to_the_same_pool_lose_no_update()
    {
        var product = await factory.ProductIdAsync("digital-teacher");               // seat 350.00
        var a = await (await factory.CustomerAsync()).OrderAsync(Shop.SponsorAmount(product, "200.00"));
        var b = await (await factory.CustomerAsync()).OrderAsync(Shop.SponsorAmount(product, "200.00"));

        await Task.WhenAll(factory.SettleAsync(a.Reference), factory.SettleAsync(b.Reference));

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.sponsored_unit WHERE product_id = '{product}' AND source = 'pooled'")).ShouldBe(1);
        (await factory.ScalarAsync<decimal>($"""
            SELECT p.balance FROM store.sponsorship_pool p JOIN store.sponsorship_offer o ON o.id = p.sponsorship_offer_id
             WHERE o.product_id = '{product}'
            """)).ShouldBe(50.00m);
        (await factory.ScalarAsync<decimal>($"""
            SELECT sum(f.amount) FROM store.unit_funding f JOIN store.sponsored_unit u ON u.id = f.sponsored_unit_id WHERE u.product_id = '{product}'
            """)).ShouldBe(350.00m);
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-47")]
    [Rule("RUL-62")]
    public async Task A_batch_is_all_or_nothing_and_only_allocation_officers_allocate()
    {
        var product = await factory.ProductIdAsync("python-kids");                   // orphan_home_students
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(product, 2));
        var ok = await ApprovedBeneficiaryAsync("student", "orphan_home_students");
        var pending = await ApprovedBeneficiaryAsync("student", "orphan_home_students", approve: false);

        (await AllocateAsync(await AdminAsync(), product, ok.Id)).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var (rejected, problem) = await AllocateAsync(await OfficerAsync(), product, ok.Id, pending.Id);

        rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        problem.GetProperty("code").GetString().ShouldBe("allocation.rejected");
        var errors = problem.GetProperty("errors").EnumerateArray().ToList();
        errors.Count.ShouldBe(1);
        errors[0].GetProperty("beneficiaryCode").GetString().ShouldBe(pending.Code);
        errors[0].GetProperty("code").GetString().ShouldBe("allocation.not_approved");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.sponsored_unit WHERE product_id = '{product}' AND status = 'allocated'")).ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-48")]
    public async Task Two_officers_in_parallel_never_get_the_same_unit()
    {
        var product = await factory.ProductIdAsync("early-childhood");               // family_program_mothers
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(product, 2));
        var b1 = await ApprovedBeneficiaryAsync("parent", "family_program_mothers");
        var b2 = await ApprovedBeneficiaryAsync("parent", "family_program_mothers");
        var officer1 = await factory.StaffAsync(Roles.AllocationOfficer);
        var officer2 = await factory.StaffAsync(Roles.AllocationOfficer);

        var results = await Task.WhenAll(AllocateAsync(officer1, product, b1.Id), AllocateAsync(officer2, product, b2.Id));

        results.Count(r => r.Response.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(1);
        var units = results.Where(r => r.Response.StatusCode == HttpStatusCode.OK)
            .SelectMany(r => r.Body.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("unitId").GetGuid())).ToList();
        units.Distinct().Count().ShouldBe(units.Count);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.sponsored_unit WHERE product_id = '{product}' AND status = 'allocated'")).ShouldBe(units.Count);
        (await factory.ScalarAsync<long>($"""
            SELECT count(*) FROM (SELECT beneficiary_id FROM store.sponsored_unit WHERE product_id = '{product}' AND status = 'allocated'
                                   GROUP BY beneficiary_id HAVING count(*) > 1) d
            """)).ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-48")]
    [Rule("RUL-50")]
    [Rule("RUL-52")]
    public async Task Allocation_is_FIFO_invites_the_beneficiary_and_a_lapsed_seat_goes_back_to_the_front()
    {
        var product = await factory.ProductIdAsync("smart-study");                   // social_security_students
        var first = await factory.CustomerAsync();
        var second = await factory.CustomerAsync();                                  // tokens minted before the clock moves
        var older = await factory.PaidOrderAsync(first, Shop.SponsorUnits(product, 1));
        var funded = factory.Clock.UtcNow;
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await factory.PaidOrderAsync(second, Shop.SponsorUnits(product, 1));
        factory.Clock.Set(funded);
        var oldest = await factory.ScalarAsync<Guid>($"""
            SELECT u.id FROM store.sponsored_unit u JOIN store.order_line l ON l.id = u.order_line_id WHERE l.order_id = '{older.OrderId}'
            """);
        var beneficiary = await ApprovedBeneficiaryAsync("student", "social_security_students");

        var (allocated, result) = await AllocateAsync(await OfficerAsync(), product, beneficiary.Id);
        allocated.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("units")[0].GetProperty("unitId").GetGuid().ShouldBe(oldest);                               // RUL-48
        await factory.DrainOutboxAsync();

        var customer = await factory.ScalarAsync<Guid>($"SELECT id FROM store.customer WHERE mobile = '{ScenarioFactory.E164(beneficiary.Mobile)}'");
        (await factory.ScalarAsync<Guid>($"SELECT customer_id FROM store.beneficiary WHERE id = '{beneficiary.Id}'")).ShouldBe(customer);  // RUL-52
        (await factory.ScalarAsync<string>($"SELECT status FROM store.enrolment WHERE sponsored_unit_id = '{oldest}'")).ShouldBe("active");
        factory.SmsTo(beneficiary.Mobile).Count.ShouldBe(1);                                                            // the invitation
        var fundedAt = await factory.ScalarAsync<DateTime>($"SELECT funded_at FROM store.sponsored_unit WHERE id = '{oldest}'");

        var start = factory.Clock.UtcNow;
        var neverStarted = await factory.ScalarAsync<long>("""
            SELECT count(*) FROM store.sponsored_unit u JOIN store.enrolment e ON e.sponsored_unit_id = u.id
            WHERE u.status = 'allocated' AND e.started_at IS NULL AND e.status IN ('pending_sync', 'active')
            """);                                                                                                       // other tests of the class may leave some
        neverStarted.ShouldBeGreaterThanOrEqualTo(1);
        try
        {
            factory.Clock.Advance(TimeSpan.FromDays(61));                                                               // never started
            (await factory.RunAsync<LapseUnitsCommand, int>(new LapseUnitsCommand())).Value.ShouldBe((int)neverStarted);
        }
        finally
        {
            factory.Clock.Set(start);
        }

        await factory.DrainOutboxAsync();
        (await factory.ScalarAsync<string>($"SELECT status FROM store.sponsored_unit WHERE id = '{oldest}'")).ShouldBe("awaiting_allocation");
        (await factory.ScalarAsync<DateTime>($"SELECT funded_at FROM store.sponsored_unit WHERE id = '{oldest}'")).ShouldBe(fundedAt);   // front of the queue
        (await factory.ScalarAsync<string>($"SELECT status FROM store.enrolment WHERE sponsored_unit_id = '{oldest}'")).ShouldBe("revoked");
        factory.SmsTo(beneficiary.Mobile).Count.ShouldBe(2);                                                            // the lapse SMS
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }
}
