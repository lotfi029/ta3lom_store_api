using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Donations;

/// `/v1/me` (BR-A10, D-13, D-14).
public sealed class MeTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Donor, string Reference)> DonorWithOrderAsync()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 2 });
        var placed = await donor.PostAsync("/v1/checkout", new { fullName = "فاطمة", email = "f@example.com" }, HttpJson.Key());
        return (donor, placed.Body.Str("reference"));
    }

    [Fact]
    public async Task A_donor_reads_and_updates_the_profile()
    {
        var donor = await factory.CustomerAsync();

        var me = await donor.GetAsync("/v1/me");
        me.Body.Str("mobile").ShouldBe(ScenarioFactory.E164(donor.Mobile!));
        me.Body.Str("status").ShouldBe("active");
        var updated = await donor.SendAsync(HttpMethod.Patch, "/v1/me", new { fullName = "  Sara  ", email = "s@example.com", locale = "en", marketingOptIn = true });
        updated.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated.Body.Str("fullName").ShouldBe("Sara");
        updated.Body.Str("locale").ShouldBe("en");
        updated.Body.GetProperty("marketingOptIn").GetBoolean().ShouldBeTrue();
        var invalid = await donor.SendAsync(HttpMethod.Patch, "/v1/me", new { email = "nope", locale = "fr" });
        invalid.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["email", "locale"], ignoreOrder: true);
        (await factory.Anonymous().GetAsync("/v1/me", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_donor_lists_their_own_donations_and_exports_everything()
    {
        var (donor, reference) = await DonorWithOrderAsync();

        var list = (await donor.GetAsync("/v1/me/donations")).Body;
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("items")[0].Str("reference").ShouldBe(reference);
        var detail = (await donor.GetAsync($"/v1/me/donations/{reference}")).Body;
        detail.GetProperty("lines")[0].Str("amount").ShouldBe("8.00");
        detail.Str("donorName").ShouldBe("فاطمة");
        (await (await factory.CustomerAsync()).GetAsync($"/v1/me/donations/{reference}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await donor.GetAsync("/v1/me/donations?pageSize=0")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var export = (await donor.GetAsync("/v1/me/export")).Body;
        export.GetProperty("profile").Str("email").ShouldBe("f@example.com");
        export.GetProperty("donations").EnumerateArray().Single().Str("reference").ShouldBe(reference);
    }

    [Fact]
    [Rule("D-14")]
    public async Task Deleting_the_account_anonymises_the_donor_and_keeps_the_orders()
    {
        var (donor, reference) = await DonorWithOrderAsync();
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.refresh_token (id, family_id, subject_type, subject_id, token_hash, created_at, expires_at, family_expires_at)
            VALUES (gen_random_uuid(), gen_random_uuid(), 'customer', '{donor.Id}', 'hash-{Guid.NewGuid():N}', now(), now() + interval '1 day', now() + interval '2 days')
            """);

        var deleted = await donor.SendAsync(HttpMethod.Delete, "/v1/me");

        deleted.Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.customer WHERE id = '{donor.Id}'")).ShouldBe("anonymised");
        (await factory.ScalarAsync<bool>($"SELECT full_name IS NULL AND email IS NULL AND mobile IS NULL FROM store.customer WHERE id = '{donor.Id}'"))
            .ShouldBeTrue();
        (await factory.ScalarAsync<string>($"SELECT donor_name || '|' || donor_email || '|' || donor_mobile || '|' || total_amount FROM store.orders WHERE reference = '{reference}'"))
            .ShouldBe("—|deleted@invalid|deleted|8.00");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refresh_token WHERE subject_id = '{donor.Id}' AND revoked_at IS NULL")).ShouldBe(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'customer.deleted' AND entity_id = '{donor.Id}'")).ShouldBe(1);
        (await donor.GetAsync("/v1/me")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await donor.SendAsync(HttpMethod.Delete, "/v1/me")).Body.Str("code").ShouldBe(ErrorCodes.AccountBlocked);
    }

    [Fact]
    [Rule("D-14")]
    public async Task After_deletion_the_same_mobile_signs_up_as_a_new_donor_and_admin_search_finds_only_the_new_one()
    {
        var (donor, _) = await DonorWithOrderAsync();
        (await donor.SendAsync(HttpMethod.Delete, "/v1/me")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.ScalarAsync<bool>($"SELECT mobile IS NULL FROM store.customer WHERE id = '{donor.Id}'")).ShouldBeTrue();

        var session = await factory.SignInAsync(factory.Browser(), donor.Mobile!);

        session.GetProperty("customer").GetProperty("isNew").GetBoolean().ShouldBeTrue();
        var newId = session.GetProperty("customer").Str("id");
        newId.ShouldNotBe(donor.Id.ToString());
        var admin = await factory.StaffAsync(Ta3lom.Store.Domain.Constants.Roles.Admin);
        var found = (await admin.GetAsync($"/v1/admin/customers?q={donor.Mobile}")).Body.GetProperty("items").EnumerateArray().Select(c => c.Str("id")).ToList();
        found.ShouldBe([newId]);
        (await admin.GetAsync($"/v1/admin/customers/{donor.Id}")).Body.GetProperty("mobile").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    [Rule("D-13")]
    public async Task A_blocked_donor_cannot_write_but_can_read()
    {
        var donor = await factory.CustomerAsync();
        await factory.ExecuteSqlAsync($"UPDATE store.customer SET status = 'blocked' WHERE id = '{donor.Id}'");

        (await donor.SendAsync(HttpMethod.Patch, "/v1/me", new { fullName = "X" })).Body.Str("code").ShouldBe(ErrorCodes.AccountBlocked);
        (await donor.SendAsync(HttpMethod.Delete, "/v1/me")).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await donor.GetAsync("/v1/me")).Body.Str("status").ShouldBe("blocked");
    }
}
