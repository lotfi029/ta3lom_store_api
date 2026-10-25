using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.AdminOps;

/// Dashboard, donors, orders, templates and the audit log (BR-A12, BR-A13).
public sealed class AdminOpsTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(DonationCatalog Catalog, ActorClient Donor, string Reference)> OrderAsync(string amount = "40.00")
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, amount });
        var placed = await donor.PostAsync("/v1/checkout", new { fullName = "Donor", email = "donor@example.com" }, HttpJson.Key());
        placed.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (c, donor, placed.Body.Str("reference"));
    }

    [Fact]
    public async Task The_dashboard_counts_pending_orders_and_ranks_projects_by_pending_amount()
    {
        var (c, _, _) = await OrderAsync("99000.00");
        var viewer = await factory.StaffAsync(Roles.Viewer);

        var dashboard = await viewer.GetAsync("/v1/admin/dashboard");

        dashboard.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        dashboard.Body.GetProperty("pendingOrdersToday").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        dashboard.Body.GetProperty("pendingOrdersThisWeek").GetInt32().ShouldBeGreaterThanOrEqualTo(dashboard.Body.GetProperty("pendingOrdersToday").GetInt32());
        dashboard.Body.GetProperty("publishedProjects").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        var top = dashboard.Body.GetProperty("topProjectsByPendingAmount")[0];
        top.Str("projectId").ShouldBe(c.ProjectId.ToString());
        top.Str("pendingAmount").ShouldBe("99000.00");
    }

    [Fact]
    [Rule("D-13")]
    public async Task Blocking_a_donor_revokes_their_sessions_and_is_audited()
    {
        var manager = await factory.StaffAsync(Roles.StoreManager);
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        var session = await factory.SignInAsync(browser, mobile);
        var id = session.GetProperty("customer").Str("id");

        var list = await manager.GetAsync($"/v1/admin/customers?q={mobile}");
        list.Body.GetProperty("items").EnumerateArray().Single().Str("id").ShouldBe(id);
        (await manager.PostAsync($"/v1/admin/customers/{id}/block")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await manager.GetAsync($"/v1/admin/customers/{id}")).Body.Str("status").ShouldBe("blocked");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refresh_token WHERE subject_id = '{id}' AND revoked_at IS NULL")).ShouldBe(0);
        (await browser.SendJsonAsync(HttpMethod.Post, "/v1/auth/refresh")).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'customer.blocked' AND entity_id = '{id}' AND actor = '{manager.Email}'"))
            .ShouldBe(1);
        (await manager.GetAsync("/v1/admin/customers?status=blocked")).Body.GetProperty("items").EnumerateArray().Select(c => c.Str("id")).ShouldContain(id);

        (await manager.PostAsync($"/v1/admin/customers/{id}/unblock")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await manager.GetAsync($"/v1/admin/customers/{id}")).Body.Str("status").ShouldBe("active");
        (await manager.PostAsync($"/v1/admin/customers/{Guid.NewGuid()}/block")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await manager.GetAsync("/v1/admin/customers?status=gone")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Rule("D-09")]
    public async Task Finance_finds_and_cancels_a_pending_order_once()
    {
        var (_, donor, reference) = await OrderAsync();
        var finance = await factory.StaffAsync(Roles.Finance);

        (await finance.GetAsync($"/v1/admin/orders?reference={reference.ToLowerInvariant()}")).Body.GetProperty("total").GetInt32().ShouldBe(1);
        (await finance.GetAsync($"/v1/admin/orders?mobile={donor.Mobile}")).Body.GetProperty("items")[0].Str("reference").ShouldBe(reference);
        var today = factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        (await finance.GetAsync($"/v1/admin/orders?status=pending_payment&from={today}&to={today}")).Body.GetProperty("items").EnumerateArray()
            .Select(o => o.Str("reference")).ShouldContain(reference);
        (await finance.GetAsync("/v1/admin/orders?from=2026-02-01&to=2026-01-01")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var detail = (await finance.GetAsync($"/v1/admin/orders/{reference}")).Body;
        detail.Str("donorEmail").ShouldBe("donor@example.com");
        detail.GetProperty("lines").GetArrayLength().ShouldBe(1);

        var cancelled = await finance.PostAsync($"/v1/admin/orders/{reference}/cancel");
        cancelled.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        cancelled.Body.Str("status").ShouldBe("cancelled");
        var again = await finance.PostAsync($"/v1/admin/orders/{reference}/cancel");
        again.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        again.Body.Str("code").ShouldBe(ErrorCodes.OrderCancelNotAllowed);
        (await finance.GetAsync($"/v1/admin/orders/{reference}")).Body.Str("cancelledBy").ShouldBe(finance.Email);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderCancelled' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'order.cancelled' AND actor = '{finance.Email}'")).ShouldBe(1);
        (await finance.PostAsync("/v1/admin/orders/T3-00-999999/cancel")).Body.Str("code").ShouldBe(ErrorCodes.OrderNotFound);
    }

    [Fact]
    public async Task Templates_are_listed_edited_checked_and_test_sent()
    {
        var manager = await factory.StaffAsync(Roles.StoreManager);

        var list = (await manager.GetAsync("/v1/admin/templates")).Body;
        list.EnumerateArray().Select(t => $"{t.Str("key")}/{t.Str("channel")}/{t.Str("locale")}").ShouldContain("staff_alert/email/en");

        var edited = await manager.SendAsync(HttpMethod.Put, "/v1/admin/templates/staff_alert/email/en", new { subject = "[store] {{ subject }}", body = "Alert: {{ body }}" });
        edited.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        edited.Body.Str("updatedBy").ShouldBe(manager.Email);
        var broken = await manager.SendAsync(HttpMethod.Put, "/v1/admin/templates/staff_alert/email/en", new { subject = "x", body = "{{ if }}" });
        broken.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        broken.Body.Str("code").ShouldBe(ErrorCodes.TemplateRenderFailed);
        broken.Body.GetProperty("line").GetInt32().ShouldBe(1);
        (await manager.SendAsync(HttpMethod.Put, "/v1/admin/templates/nope/email/en", new { subject = "x", body = "y" })).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await manager.SendAsync(HttpMethod.Put, "/v1/admin/templates/otp/sms/ar", new { body = "" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var mail = await manager.PostAsync("/v1/admin/templates/test", new { channel = "email", subject = "Hi {{ name }}", body = "Code {{ code }}" });
        mail.Body.Str("body").ShouldBe("Code 123456");
        mail.Body.GetProperty("sent").GetBoolean().ShouldBeTrue();
        (await factory.MailAsync()).ShouldContain(m => m.To == manager.Email && m.Subject == "Hi Test");
        var mobile = ScenarioFactory.NewMobile();
        (await manager.PostAsync("/v1/admin/templates/test", new { channel = "sms", body = "رمز {{ code }}", to = mobile })).Body.GetProperty("sent").GetBoolean().ShouldBeTrue();
        factory.SmsTo(mobile).Single().Body.ShouldBe("رمز 123456");
        (await manager.PostAsync("/v1/admin/templates/test", new { channel = "sms", body = "x" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_audit_log_is_filtered_and_admin_only()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var (_, _, reference) = await OrderAsync();
        await admin.PostAsync($"/v1/admin/orders/{reference}/cancel");

        var audit = await admin.GetAsync($"/v1/admin/audit?action=order.cancelled&actor={admin.Email}");

        audit.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = audit.Body.GetProperty("items").EnumerateArray().Single();
        row.Str("entity").ShouldBe("orders");
        row.Str("diff").ShouldContain(reference);
        (await (await factory.StaffAsync(Roles.StoreManager)).GetAsync("/v1/admin/audit")).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
