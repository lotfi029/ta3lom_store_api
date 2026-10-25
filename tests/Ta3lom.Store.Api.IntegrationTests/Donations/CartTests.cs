using System.Net;
using Npgsql;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Donations;

/// `/v1/cart` (BR-A07, D-01 … D-05): guest carts by cookie, donor carts by bearer, priced from the catalogue now.
public sealed class CartTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [Fact]
    [Rule("D-01")]
    [Rule("D-04")]
    public async Task A_guest_cart_holds_option_project_and_category_lines_and_option_lines_merge()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var guest = factory.Browser();

        var first = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 2 });
        first.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Response.Headers.GetValues("Set-Cookie").ShouldContain(v => v.StartsWith("t3s_cart=", StringComparison.Ordinal) && v.Contains("httponly"));
        var again = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 3 });
        again.Response.Headers.GetValues("Set-Cookie").ShouldContain(v => v.StartsWith("t3s_cart=", StringComparison.Ordinal) && v.Contains("max-age=2592000"));   // L-8: renewed
        await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, amount = "50.00" });
        await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, amount = "50.00" });
        var cart = (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = "15.50" })).Body;

        again.Body.GetProperty("lines").GetArrayLength().ShouldBe(1);
        again.Body.GetProperty("lines")[0].GetProperty("quantity").GetInt32().ShouldBe(5);
        cart.GetProperty("lineCount").GetInt32().ShouldBe(4);
        cart.Str("totalAmount").ShouldBe("135.50");
        var lines = cart.GetProperty("lines").EnumerateArray().ToList();
        lines[0].Str("amount").ShouldBe("20.00");
        lines[0].Str("optionLabelAr").ShouldBe("نسخة");
        lines[3].Str("targetKind").ShouldBe("category");
        cart.GetProperty("problems").GetArrayLength().ShouldBe(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart_line WHERE option_id = '{c.OptionId}'")).ShouldBe(1);

        var summary = (await guest.SendJsonAsync(HttpMethod.Get, "/v1/cart/summary")).Body;
        summary.GetProperty("lineCount").GetInt32().ShouldBe(4);
        summary.Str("totalAmount").ShouldBe("135.50");
        (await factory.Browser().SendJsonAsync(HttpMethod.Get, "/v1/cart")).Body.GetProperty("lineCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    [Rule("D-03")]
    public async Task Amounts_and_quantities_respect_the_limits()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var guest = factory.Browser();

        var below = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, amount = "9.99" });
        below.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        below.Body.Str("code").ShouldBe(ErrorCodes.CartAmountBelowMinimum);
        below.Body.Str("min").ShouldBe("10.00");
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = "100000.01" }))
            .Body.Str("code").ShouldBe(ErrorCodes.CartAmountAboveMaximum);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.OptionsOnlyProjectId, amount = "50.00" }))
            .Body.Str("code").ShouldBe(ErrorCodes.CartOpenAmountNotAllowed);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionsOnlyOptionId, quantity = 1 }))
            .Body.Str("code").ShouldBe(ErrorCodes.CartOptionUnavailable);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = Guid.NewGuid(), amount = "50.00" }))
            .Body.Str("code").ShouldBe(ErrorCodes.CartTargetUnavailable);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1000 }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 999 }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 }))
            .Body.Str("code").ShouldBe(ErrorCodes.CartQuantityLimit);
    }

    [Fact]
    [Rule("D-04")]
    public async Task The_cart_total_is_capped()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var guest = factory.Browser();

        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = "60000.00" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var over = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, amount = "40000.01" });

        over.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        over.Body.Str("code").ShouldBe(ErrorCodes.CartTotalExceeded);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, amount = "40000.00" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Arabic_digits_are_accepted_and_malformed_amounts_are_400_never_500()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var guest = factory.Browser();

        var arabic = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, amount = "٥٠" });
        arabic.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        arabic.Body.GetProperty("lines")[0].Str("amount").ShouldBe("50.00");
        var lineId = arabic.Body.GetProperty("lines")[0].Str("id");
        (await guest.SendJsonAsync(HttpMethod.Patch, $"/v1/cart/lines/{lineId}", new { amount = "١٢٫٥" })).Body.Str("totalAmount").ShouldBe("12.50");

        foreach (var bad in new[] { "5x", "٥x", "1,000", "١٬٠٠٠" })
        {
            var refused = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = bad });
            refused.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
            refused.Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
            (await guest.SendJsonAsync(HttpMethod.Patch, $"/v1/cart/lines/{lineId}", new { amount = bad })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
        }
    }

    [Fact]
    public async Task Parallel_first_writes_of_a_donor_end_up_in_one_cart()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();

        var responses = await Task.WhenAll(
            donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" }),
            donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, amount = "30.00" }));

        responses.Select(r => r.Response.StatusCode).ShouldAllBe(s => s == HttpStatusCode.OK);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart WHERE customer_id = '{donor.Id}'")).ShouldBe(1);
        (await donor.GetAsync("/v1/cart")).Body.GetProperty("lineCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    [Rule("D-04")]
    public async Task Parallel_adds_of_the_same_option_are_one_line_with_the_quantities_summed()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" });

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 2 })));

        responses.Select(r => r.Response.StatusCode).ShouldAllBe(s => s == HttpStatusCode.OK);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart_line WHERE option_id = '{c.OptionId}'")).ShouldBe(1);
        (await factory.ScalarAsync<int>($"SELECT quantity FROM store.cart_line WHERE option_id = '{c.OptionId}'")).ShouldBe(10);
    }

    [Fact]
    public async Task The_request_body_has_one_of_three_shapes()
    {
        var guest = factory.Browser();

        var both = await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = Guid.NewGuid(), categoryId = Guid.NewGuid(), amount = "20.00" });
        both.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        both.Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = Guid.NewGuid(), amount = "20.5.0" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = Guid.NewGuid() })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lines_are_changed_removed_and_the_cart_cleared()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        var option = (await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 })).Body.GetProperty("lines")[0].Str("id");
        var amount = (await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, amount = "30.00" })).Body.GetProperty("lines")[1].Str("id");

        (await donor.SendAsync(HttpMethod.Patch, $"/v1/cart/lines/{option}", new { quantity = 4 })).Body.Str("totalAmount").ShouldBe("46.00");
        (await donor.SendAsync(HttpMethod.Patch, $"/v1/cart/lines/{amount}", new { amount = "70.00" })).Body.Str("totalAmount").ShouldBe("86.00");
        var wrongShape = await donor.SendAsync(HttpMethod.Patch, $"/v1/cart/lines/{option}", new { amount = "70.00" });
        wrongShape.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await donor.SendAsync(HttpMethod.Patch, $"/v1/cart/lines/{amount}", new { amount = "5.00" })).Body.Str("code").ShouldBe(ErrorCodes.CartAmountBelowMinimum);
        (await donor.SendAsync(HttpMethod.Delete, $"/v1/cart/lines/{option}")).Body.GetProperty("lineCount").GetInt32().ShouldBe(1);
        var missing = await donor.SendAsync(HttpMethod.Delete, $"/v1/cart/lines/{Guid.NewGuid()}");
        missing.Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.Body.Str("code").ShouldBe(ErrorCodes.CartLineNotFound);

        (await donor.SendAsync(HttpMethod.Delete, "/v1/cart")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await donor.GetAsync("/v1/cart")).Body.GetProperty("lineCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    [Rule("D-05")]
    public async Task Lines_that_cannot_be_donated_to_now_carry_problems()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 });
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" });

        (await c.Admin.PostAsync($"/v1/admin/projects/{c.ProjectId}/archive")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cart = (await donor.GetAsync("/v1/cart")).Body;
        cart.GetProperty("lines")[0].GetProperty("problems").EnumerateArray().Select(p => p.GetString()).ShouldBe([ErrorCodes.CartTargetUnavailable]);
        cart.GetProperty("lines")[1].GetProperty("problems").GetArrayLength().ShouldBe(0);
        var quote = await donor.PostAsync("/v1/checkout/quote");
        quote.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        quote.Body.GetProperty("problems").EnumerateArray().Single().Str("code").ShouldBe(ErrorCodes.CartTargetUnavailable);
        (await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, amount = "30.00" })).Body.Str("code").ShouldBe(ErrorCodes.CartTargetUnavailable);
    }

    [Fact]
    public async Task Signing_in_merges_the_guest_cart_into_the_donor_cart()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await factory.SignInAsync(browser, mobile);
        await browser.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 2 });
        await browser.SendJsonAsync(HttpMethod.Post, "/v1/auth/logout");

        var guest = factory.Browser();
        await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 3 });
        await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = "25.00" });
        var session = await factory.SignInAsync(guest, mobile);

        session.GetProperty("cartNotices").EnumerateArray().Select(n => n.Str("code")).ShouldBe(["cart.merge_combined"]);
        var cart = (await guest.SendJsonAsync(HttpMethod.Get, "/v1/cart")).Body;
        cart.GetProperty("lineCount").GetInt32().ShouldBe(2);
        cart.GetProperty("lines")[0].GetProperty("quantity").GetInt32().ShouldBe(5);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart c JOIN store.customer u ON u.id = c.customer_id WHERE u.mobile = '{ScenarioFactory.E164(mobile)}'"))
            .ShouldBe(1);
        guest.DefaultRequestHeaders.Authorization = null;
        (await guest.SendJsonAsync(HttpMethod.Get, "/v1/cart")).Body.GetProperty("lineCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    [Rule("D-04")]
    public async Task A_guest_line_that_would_break_the_total_limit_is_dropped_at_sign_in_and_reported()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await factory.SignInAsync(browser, mobile);
        await browser.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = "99000.00" });
        await browser.SendJsonAsync(HttpMethod.Post, "/v1/auth/logout");

        var guest = factory.Browser();
        await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = c.CategoryId, amount = "5000.00" });
        var session = await factory.SignInAsync(guest, mobile);

        session.GetProperty("cartNotices").EnumerateArray().Select(n => n.Str("code")).ShouldBe(["cart.merge_dropped"]);
        var cart = (await guest.SendJsonAsync(HttpMethod.Get, "/v1/cart")).Body;
        cart.GetProperty("lineCount").GetInt32().ShouldBe(1);
        cart.Str("totalAmount").ShouldBe("99000.00");
    }

    [Fact]
    [Rule("D-13")]
    public async Task A_blocked_donor_cannot_change_the_cart()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await factory.ExecuteSqlAsync($"UPDATE store.customer SET status = 'blocked' WHERE id = '{donor.Id}'");

        var add = await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "25.00" });

        add.Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        add.Body.Str("code").ShouldBe(ErrorCodes.AccountBlocked);
    }

    [Fact]
    [Rule("D-14")]
    public async Task A_cart_write_racing_the_account_deletion_leaves_no_cart_behind()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        (await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ct = TestContext.Current.CancellationToken;

        // DELETE /v1/me in flight: the donor's cart is locked and deleted and the donor anonymised, not committed yet.
        await using var deletion = new NpgsqlConnection(factory.DatabaseConnectionString);
        await deletion.OpenAsync(ct);
        await using var tx = await deletion.BeginTransactionAsync(ct);
        await using (var command = new NpgsqlCommand($"""
            SELECT id FROM store.cart WHERE customer_id = '{donor.Id}' FOR UPDATE;
            UPDATE store.customer SET status = 'anonymised', mobile = NULL, full_name = NULL, email = NULL WHERE id = '{donor.Id}';
            DELETE FROM store.cart WHERE customer_id = '{donor.Id}';
            """, deletion, tx))
        {
            await command.ExecuteNonQueryAsync(ct);
        }

        var write = donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 });
        await Task.Delay(500, ct);
        await tx.CommitAsync(ct);
        var response = await write;

        response.Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart WHERE customer_id = '{donor.Id}'")).ShouldBe(0);
    }
}
