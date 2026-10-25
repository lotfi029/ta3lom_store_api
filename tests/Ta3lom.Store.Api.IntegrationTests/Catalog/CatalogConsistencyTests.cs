using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// Option labels (L-13), site-visible changes queue a rebuild (L-14), settings stay consistent with each other (L-15).
public sealed class CatalogConsistencyTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static object Option(Guid? id, string label, string amount, int sort, bool active = true) =>
        new { id, labelAr = label, labelEn = (string?)null, amount, sort, isActive = active };

    private Task<long> PendingChangesAsync(string change) =>
        factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'CatalogChanged' AND processed_at IS NULL AND payload->>'change' = '{change}'");

    [Fact]
    public async Task Two_options_swap_their_labels_in_one_save()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var build = new CatalogBuilder(admin);
        var draft = await build.OptionsAsync(await build.DraftAsync(Guid.Parse((await build.CategoryAsync()).Str("id"))), ("نسخة", "4.00", true), ("نسختان", "8.00", true));
        var options = draft.GetProperty("options").EnumerateArray().ToList();
        Guid a = Guid.Parse(options[0].Str("id")), b = Guid.Parse(options[1].Str("id"));

        var swapped = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{draft.Str("id")}/options",
            new { options = new[] { Option(a, "نسختان", "4.00", 0), Option(b, "نسخة", "8.00", 1) }, version = draft.GetProperty("version").GetUInt32() });

        swapped.Response.StatusCode.ShouldBe(HttpStatusCode.OK, swapped.Body.ToString());
        swapped.Body.GetProperty("options").EnumerateArray().Select(o => (o.Str("id"), o.Str("labelAr"))).ShouldBe([(a.ToString(), "نسختان"), (b.ToString(), "نسخة")]);
    }

    [Fact]
    public async Task A_label_kept_by_a_deactivated_option_cannot_be_given_to_another_one()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 });
        (await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "d@example.com" }, HttpJson.Key())).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var project = (await c.Admin.GetAsync($"/v1/admin/projects/{c.ProjectId}")).Body;
        var usedLabel = project.GetProperty("options").EnumerateArray().Single(o => o.Str("id") == c.OptionId.ToString()).Str("labelAr");

        // The used option leaves the list (deactivated, keeps its label); the other option takes that label.
        var taken = await c.Admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{c.ProjectId}/options",
            new { options = new[] { Option(c.BigOptionId, usedLabel, "400.00", 0) }, version = project.GetProperty("version").GetUInt32() });

        taken.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        taken.Body.Str("code").ShouldBe(ErrorCodes.ProjectOptionLabelTaken);
    }

    [Fact]
    public async Task Alt_text_and_site_visible_settings_queue_a_rebuild()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var image = await new CatalogBuilder(admin).ImageAsync();
        var altBefore = await PendingChangesAsync("alt");
        var settingBefore = await PendingChangesAsync("site_name_ar");

        (await admin.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{image}", new { altAr = "غلاف جديد", altEn = (string?)null })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/site_name_ar", new { value = "متجر تعلّم" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/payment_window_minutes", new { value = 60 })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PendingChangesAsync("alt")).ShouldBe(altBefore + 1);
        (await PendingChangesAsync("site_name_ar")).ShouldBe(settingBefore + 1);
        (await PendingChangesAsync("payment_window_minutes")).ShouldBe(0);
    }

    [Fact]
    public async Task Money_settings_must_stay_consistent_with_each_other()
    {
        var admin = await factory.StaffAsync(Roles.Admin);

        async Task<HttpStatusCode> PutAsync(string key, object value) =>
            (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/settings/{key}", new { value })).Response.StatusCode;

        (await PutAsync("min_gift_amount", "100000.00")).ShouldBe(HttpStatusCode.UnprocessableEntity);         // above the presets
        (await PutAsync("amount_presets", new[] { "5.00", "50.00" })).ShouldBe(HttpStatusCode.UnprocessableEntity);   // a preset below the minimum
        (await PutAsync("max_line_amount", "5.00")).ShouldBe(HttpStatusCode.UnprocessableEntity);              // below the minimum gift
        (await PutAsync("cart_max_total", "50.00")).ShouldBe(HttpStatusCode.UnprocessableEntity);              // below the line maximum
        (await PutAsync("max_line_amount", "50000.00")).ShouldBe(HttpStatusCode.OK);
        (await PutAsync("cart_max_total", "60000.00")).ShouldBe(HttpStatusCode.OK);
        var refused = await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/max_line_amount", new { value = "70000.00" });
        refused.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Body.Str("code").ShouldBe(ErrorCodes.SettingInvalidValue);
    }

    [Fact]
    public async Task Parallel_setting_updates_cannot_together_break_the_money_rules()
    {
        var admin = await factory.StaffAsync(Roles.Admin);

        async Task<HttpStatusCode> PutAsync(string key, object value) =>
            (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/settings/{key}", new { value })).Response.StatusCode;

        for (var round = 0; round < 5; round++)
        {
            (await PutAsync("min_gift_amount", "10.00")).ShouldBe(HttpStatusCode.OK);
            (await PutAsync("cart_max_total", "100000.00")).ShouldBe(HttpStatusCode.OK);
            (await PutAsync("max_line_amount", "50000.00")).ShouldBe(HttpStatusCode.OK);

            // Each alone is fine; together the minimum gift would exceed the line maximum (L-7).
            var both = await Task.WhenAll(PutAsync("min_gift_amount", "40.00"), PutAsync("max_line_amount", "20.00"));

            both.Count(s => s == HttpStatusCode.UnprocessableEntity).ShouldBe(1, $"round {round}");
        }

        (await PutAsync("min_gift_amount", "10.00")).ShouldBe(HttpStatusCode.OK);                                 // back to the seeded values
        (await PutAsync("cart_max_total", "100000.00")).ShouldBe(HttpStatusCode.OK);
        (await PutAsync("max_line_amount", "100000.00")).ShouldBe(HttpStatusCode.OK);
    }
}
