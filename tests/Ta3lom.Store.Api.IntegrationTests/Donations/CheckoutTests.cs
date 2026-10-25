using System.Net;
using System.Text.RegularExpressions;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Donations;

/// `POST /v1/checkout` (BR-A08, D-09): idempotent, re-priced, `pending_payment`, `payment: null`.
public sealed partial class CheckoutTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [GeneratedRegex(@"^T3-\d{2}-\d{6}$")]
    private static partial Regex ReferencePattern();

    private async Task<(DonationCatalog Catalog, ActorClient Donor)> DonorWithCartAsync()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 3 });
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" });
        return (c, donor);
    }

    [Fact]
    [Rule("D-09")]
    [Rule("D-02")]
    public async Task Checkout_creates_a_pending_order_with_line_snapshots_and_empties_the_cart()
    {
        var (c, donor) = await DonorWithCartAsync();

        var placed = await donor.PostAsync("/v1/checkout", new { fullName = "عبدالله", email = "donor@example.com" }, HttpJson.Key());

        placed.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reference = placed.Body.Str("reference");
        ReferencePattern().IsMatch(reference).ShouldBeTrue();
        reference[3..5].ShouldBe((factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(3)).Year % 100).ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        placed.Body.Str("status").ShouldBe("pending_payment");
        placed.Body.Str("totalAmount").ShouldBe("32.00");
        placed.Body.GetProperty("payment").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await factory.ScalarAsync<string>($"SELECT string_agg(title_ar || ':' || quantity || ':' || unit_amount || ':' || amount, '|' ORDER BY line_no) FROM store.order_line l JOIN store.orders o ON o.id = l.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe($"{c.Project.Str("titleAr")}:3:4.00:12.00|تعليم:1:20.00:20.00");
        (await factory.ScalarAsync<int>($"SELECT round(extract(epoch FROM payment_due_at - created_at) / 60)::int FROM store.orders WHERE reference = '{reference}'")).ShouldBe(60);
        (await factory.ScalarAsync<string>($"SELECT donor_name FROM store.orders WHERE reference = '{reference}'")).ShouldBe("عبدالله");
        (await donor.GetAsync("/v1/cart")).Body.GetProperty("lineCount").GetInt32().ShouldBe(0);
        (await donor.GetAsync("/v1/me")).Body.Str("email").ShouldBe("donor@example.com");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderPlaced' AND payload->>'reference' = '{reference}'")).ShouldBe(1);

        await factory.DrainOutboxAsync();
        // D-a: until R5-Payments an order is only logged, no staff e-mail; the message is still processed.
        (await factory.MailAsync()).ShouldNotContain(m => m.Subject.Contains(reference, StringComparison.Ordinal));
        (await factory.ScalarAsync<bool>($"SELECT processed_at IS NOT NULL FROM store.outbox_message WHERE type = 'OrderPlaced' AND payload->>'reference' = '{reference}'"))
            .ShouldBeTrue();
    }

    [Fact]
    [Rule("D-09")]
    public async Task The_same_key_replays_the_order_and_another_body_is_refused()
    {
        var (_, donor) = await DonorWithCartAsync();
        var key = HttpJson.Key();
        var body = new { fullName = "Donor", email = "d@example.com" };

        var first = await donor.PostAsync("/v1/checkout", body, key);
        var replay = await donor.PostAsync("/v1/checkout", body, key);
        var other = await donor.PostAsync("/v1/checkout", new { fullName = "Other", email = "d@example.com" }, key);

        first.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        replay.Body.Str("reference").ShouldBe(first.Body.Str("reference"));
        other.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        other.Body.Str("code").ShouldBe(ErrorCodes.IdempotencyKeyReused);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.orders WHERE customer_id = '{donor.Id}'")).ShouldBe(1);

        var missing = await donor.PostAsync("/v1/checkout", body);
        missing.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.Body.Str("code").ShouldBe(ErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task A_refused_checkout_releases_its_key_so_the_same_key_works_after_fixing_the_cart()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        var key = HttpJson.Key();
        var body = new { fullName = "D", email = "d@example.com" };

        var empty = await donor.PostAsync("/v1/checkout", body, key);
        empty.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        empty.Body.Str("code").ShouldBe(ErrorCodes.CheckoutCartEmpty);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.idempotency_key WHERE key = '{key["Idempotency-Key"]}'")).ShouldBe(0);

        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" });
        var placed = await donor.PostAsync("/v1/checkout", body, key);

        placed.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_stored_replay_holds_no_status_token_and_the_replay_still_returns_a_working_one()
    {
        var (_, donor) = await DonorWithCartAsync();
        var key = HttpJson.Key();
        var body = new { fullName = "D", email = "d@example.com" };

        var first = await donor.PostAsync("/v1/checkout", body, key);
        var stored = await factory.ScalarAsync<string>($"SELECT coalesce(response_body->>'statusToken', '') FROM store.idempotency_key WHERE key = '{key["Idempotency-Key"]}'");
        var replay = await donor.PostAsync("/v1/checkout", body, key);

        stored.ShouldBeEmpty();
        replay.Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        replay.Body.Str("statusToken").ShouldBe(first.Body.Str("statusToken"));
        var status = await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"/v1/orders/{replay.Body.Str("reference")}/status?t={replay.Body.Str("statusToken")}");
        status.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Parallel_requests_with_one_key_never_fail_with_500()
    {
        var (_, donor) = await DonorWithCartAsync();
        var key = HttpJson.Key();
        var body = new { fullName = "D", email = "d@example.com" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => donor.PostAsync("/v1/checkout", body, key)));

        responses.Select(r => (int)r.Response.StatusCode).ShouldAllBe(s => s == 201 || s == 409);
        responses.Count(r => r.Response.StatusCode == HttpStatusCode.Created && !r.Response.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.orders WHERE customer_id = '{donor.Id}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("D-09")]
    public async Task Two_parallel_checkouts_of_one_cart_with_different_keys_make_one_order()
    {
        var (_, donor) = await DonorWithCartAsync();
        var body = new { fullName = "D", email = "d@example.com" };

        var responses = await Task.WhenAll(donor.PostAsync("/v1/checkout", body, HttpJson.Key()), donor.PostAsync("/v1/checkout", body, HttpJson.Key()));

        // The cart row lock serialises them: the first places the order and empties the cart, the second finds it empty.
        responses.Select(r => (int)r.Response.StatusCode).OrderBy(s => s).ShouldBe([201, 409]);
        responses.Single(r => r.Response.StatusCode == HttpStatusCode.Conflict).Body.Str("code").ShouldBe(ErrorCodes.CheckoutCartEmpty);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.orders WHERE customer_id = '{donor.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_key_still_in_progress_is_409_and_stays_claimed()
    {
        var (_, donor) = await DonorWithCartAsync();
        var key = HttpJson.Key();
        var body = new { fullName = "Donor", email = "d@example.com" };
        var done = HttpJson.Key();
        (await donor.PostAsync("/v1/checkout", body, done)).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.idempotency_key (scope, key, request_hash, status, created_at, expires_at)
            SELECT scope, '{key["Idempotency-Key"]}', request_hash, 'in_progress', now(), now() + interval '1 day'
              FROM store.idempotency_key WHERE key = '{done["Idempotency-Key"]}'
            """);

        var busy = await donor.PostAsync("/v1/checkout", body, key);

        busy.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        busy.Body.Str("code").ShouldBe(ErrorCodes.IdempotencyInProgress);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.idempotency_key WHERE key = '{key["Idempotency-Key"]}'")).ShouldBe("in_progress");
    }

    [Fact]
    public async Task Checkout_needs_lines_a_contact_and_valid_lines()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();

        (await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "d@example.com" }, HttpJson.Key())).Body.Str("code").ShouldBe(ErrorCodes.CheckoutCartEmpty);
        (await donor.PostAsync("/v1/checkout/quote")).Body.Str("code").ShouldBe(ErrorCodes.CheckoutCartEmpty);
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.BigOptionId, quantity = 1 });
        var contact = await donor.PostAsync("/v1/checkout", new { }, HttpJson.Key());
        contact.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        contact.Body.Str("code").ShouldBe(ErrorCodes.CheckoutContactRequired);
        (await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "not-an-email" }, HttpJson.Key())).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await c.Admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{c.ProjectId}/options", new
        {
            options = new[]
            {
                new { id = (Guid?)c.OptionId, labelAr = "نسخة", labelEn = (string?)null, amount = "4.00", sort = 0, isActive = true },
                new { id = (Guid?)c.BigOptionId, labelAr = "مئة نسخة", labelEn = (string?)null, amount = "400.00", sort = 1, isActive = false },
            },
            version = (await c.Admin.GetAsync($"/v1/admin/projects/{c.ProjectId}")).Body.GetProperty("version").GetUInt32(),
        });
        var invalid = await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "d@example.com" }, HttpJson.Key());
        invalid.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        invalid.Body.Str("code").ShouldBe(ErrorCodes.CheckoutLinesInvalid);
        invalid.Body.GetProperty("problems")[0].Str("code").ShouldBe(ErrorCodes.CartOptionUnavailable);
        (await factory.Anonymous().PostAsync("/v1/checkout", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Checkout_reprices_from_the_catalogue_and_used_options_are_deactivated_not_deleted()
    {
        var (c, donor) = await DonorWithCartAsync();
        var version = (await c.Admin.GetAsync($"/v1/admin/projects/{c.ProjectId}")).Body.GetProperty("version").GetUInt32();
        await c.Admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{c.ProjectId}/options", new
        {
            options = new[]
            {
                new { id = (Guid?)c.OptionId, labelAr = "نسخة", labelEn = (string?)null, amount = "5.00", sort = 0, isActive = true },
                new { id = (Guid?)c.BigOptionId, labelAr = "مئة نسخة", labelEn = (string?)null, amount = "400.00", sort = 1, isActive = true },
            },
            version,
        });

        var placed = await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "d@example.com" }, HttpJson.Key());
        placed.Body.Str("totalAmount").ShouldBe("35.00");

        var project = (await c.Admin.GetAsync($"/v1/admin/projects/{c.ProjectId}")).Body;
        var dropped = await c.Admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{c.ProjectId}/options", new
        {
            options = new[] { new { id = (Guid?)c.BigOptionId, labelAr = "مئة نسخة", labelEn = (string?)null, amount = "400.00", sort = 0, isActive = true } },
            version = project.GetProperty("version").GetUInt32(),
        });
        dropped.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var used = dropped.Body.GetProperty("options").EnumerateArray().Single(o => o.Str("id") == c.OptionId.ToString());
        used.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    [Rule("D-13")]
    public async Task A_blocked_donor_cannot_check_out()
    {
        var (_, donor) = await DonorWithCartAsync();
        await factory.ExecuteSqlAsync($"UPDATE store.customer SET status = 'blocked' WHERE id = '{donor.Id}'");

        var placed = await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "d@example.com" }, HttpJson.Key());

        placed.Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        placed.Body.Str("code").ShouldBe(ErrorCodes.AccountBlocked);
    }

    [Fact]
    public async Task The_status_page_needs_the_donor_or_the_status_token()
    {
        var (_, donor) = await DonorWithCartAsync();
        var placed = (await donor.PostAsync("/v1/checkout", new { fullName = "D", email = "d@example.com" }, HttpJson.Key())).Body;
        var path = $"/v1/orders/{placed.Str("reference")}/status";
        var anonymous = factory.Anonymous();

        var byToken = await anonymous.SendJsonAsync(HttpMethod.Get, $"{path}?t={placed.Str("statusToken")}");
        byToken.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        byToken.Body.Str("status").ShouldBe("pending_payment");
        (await donor.GetAsync(path)).Body.Str("totalAmount").ShouldBe("32.00");
        (await anonymous.SendJsonAsync(HttpMethod.Get, $"{path}?t=wrong")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.SendJsonAsync(HttpMethod.Get, path)).Body.Str("code").ShouldBe(ErrorCodes.OrderNotFound);
        (await (await factory.CustomerAsync()).GetAsync(path)).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
