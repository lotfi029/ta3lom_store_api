using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Auth;

/// `/v1/admin/staff` (BR-A12): admin only.
public sealed class StaffAdminTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [Fact]
    public async Task An_admin_creates_lists_updates_and_resets_staff()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var email = $"new-{Guid.NewGuid():N}@test.ta3lom.sa";

        var created = await admin.PostAsync("/v1/admin/staff", new { email, name = "مدير المتجر", roles = new[] { Roles.StoreManager } });
        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Body.Str("oneTimePassword").Length.ShouldBe(16);
        var staff = created.Body.GetProperty("staff");
        staff.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        staff.GetProperty("totpEnrolled").GetBoolean().ShouldBeFalse();
        var id = staff.Str("id");

        (await admin.GetAsync("/v1/admin/staff")).Body.EnumerateArray().Select(s => s.Str("email")).ShouldContain(email);

        var update = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{id}",
            new { name = "Manager", roles = new[] { Roles.StoreManager, Roles.Finance }, isActive = true, version = staff.GetProperty("version").GetUInt32() });
        update.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        update.Body.GetProperty("roles").GetArrayLength().ShouldBe(2);
        var stale = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{id}",
            new { name = "Stale", roles = new[] { Roles.Viewer }, isActive = true, version = staff.GetProperty("version").GetUInt32() });
        stale.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stale.Body.Str("code").ShouldBe(ErrorCodes.ConcurrencyConflict);

        var reset = await admin.PostAsync($"/v1/admin/staff/{id}/reset-password");
        reset.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        reset.Body.Str("oneTimePassword").ShouldNotBe(created.Body.Str("oneTimePassword"));
        (await factory.ScalarAsync<bool>($"SELECT must_change_password FROM store.staff_user WHERE id = '{id}'")).ShouldBeTrue();
        (await admin.PostAsync($"/v1/admin/staff/{id}/reset-totp")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/v1/admin/staff/{id}/unlock")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE entity = 'staff_user' AND entity_id = '{id}' AND actor = '{admin.Email}'")).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Bad_input_is_400_a_duplicate_email_409_and_an_unknown_id_404()
    {
        var admin = await factory.StaffAsync(Roles.Admin);

        var invalid = await admin.PostAsync("/v1/admin/staff", new { email = "not-an-email", name = "", roles = new[] { "allocation_officer" } });
        invalid.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
        invalid.Body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["email", "name", "roles[0]"], ignoreOrder: true);

        (await admin.PostAsync("/v1/admin/staff", new { email = admin.Email, name = "Twin", roles = new[] { Roles.Viewer } })).Body.Str("code").ShouldBe(ErrorCodes.StaffEmailTaken);
        (await admin.PostAsync($"/v1/admin/staff/{Guid.NewGuid()}/unlock")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(Roles.StoreManager)]
    [InlineData(Roles.Finance)]
    [InlineData(Roles.Viewer)]
    public async Task Only_admins_manage_staff(string role)
    {
        var member = await factory.StaffAsync(role);

        var list = await member.GetAsync("/v1/admin/staff");

        list.Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        list.Body.Str("code").ShouldBe(ErrorCodes.Forbidden);
        (await factory.Anonymous().GetAsync("/v1/admin/staff", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
