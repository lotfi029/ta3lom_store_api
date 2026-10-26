using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// `/v1/catalog/*` (BR-A06): anonymous, filtered in SQL, cached with an ETag.
public sealed class PublicCatalogTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Admin, CatalogBuilder Build)> AdminAsync()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        return (admin, new CatalogBuilder(admin));
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> GetAsync(string path, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        var response = await factory.Anonymous().SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    private static List<string> Slugs(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(i => i.Str("slug"))];

    [Fact]
    [Rule("D-05")]
    [Rule("D-07")]
    public async Task Only_published_projects_of_active_categories_are_listed()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        var published = await build.PublishAsync(await build.DraftAsync(categoryId));
        var draft = await build.DraftAsync(categoryId);
        var listing = $"/v1/catalog/projects?category={category.Str("slug")}";

        var page = (await GetAsync(listing)).Body;
        Slugs(page).ShouldBe([published.Str("slug")]);
        page.GetProperty("items")[0].Str("minAmount").ShouldBe("10.00");
        (await GetAsync($"/v1/catalog/projects/{draft.Str("slug")}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.PostAsync($"/v1/admin/projects/{published.Str("id")}/archive")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Slugs((await GetAsync(listing)).Body).ShouldBeEmpty();
        (await GetAsync($"/v1/catalog/projects/{published.Str("slug")}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.PostAsync($"/v1/admin/projects/{published.Str("id")}/publish")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Slugs((await GetAsync(listing)).Body).ShouldBe([published.Str("slug")]);

        var off = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{categoryId}",
            CatalogBuilder.CategoryBody(category.Str("slug"), isActive: false, version: category.GetProperty("version").GetUInt32()));
        off.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Slugs((await GetAsync(listing)).Body).ShouldBeEmpty();
        (await GetAsync($"/v1/catalog/projects/{published.Str("slug")}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync("/v1/catalog/categories")).Body.EnumerateArray().Select(c => c.Str("slug")).ShouldNotContain(category.Str("slug"));
    }

    [Fact]
    public async Task A_project_page_shows_its_active_options_and_category()
    {
        var (_, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var draft = await build.DraftAsync(Guid.Parse(category.Str("id")), allowOpenAmount: false);
        draft = await build.OptionsAsync(draft, ("نسخة", "4.00", true), ("خمس نسخ", "20.00", true), ("قديم", "9.00", false));
        var project = await build.PublishAsync(draft);

        var (response, body) = await GetAsync($"/v1/catalog/projects/{project.Str("slug")}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.Str("status").ShouldBe("published");
        body.GetProperty("category").Str("slug").ShouldBe(category.Str("slug"));
        body.GetProperty("options").EnumerateArray().Select(o => o.Str("amount")).ShouldBe(["4.00", "20.00"]);
        body.Str("coverUrl").ShouldEndWith(".webp");
        var card = (await GetAsync($"/v1/catalog/projects?category={category.Str("slug")}")).Body.GetProperty("items")[0];
        card.Str("fromAmount").ShouldBe("4.00");
        card.GetProperty("allowOpenAmount").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_category_is_an_empty_page_and_bad_parameters_are_400()
    {
        var (_, build) = await AdminAsync();
        await build.PublishedAsync();

        var empty = await GetAsync("/v1/catalog/projects?category=no-such-category");
        empty.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        empty.Body.GetProperty("total").GetInt32().ShouldBe(0);

        var badSort = await GetAsync("/v1/catalog/projects?sort=price");
        badSort.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        badSort.Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
        (await GetAsync("/v1/catalog/projects?pageSize=1000")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync("/v1/catalog/projects?fields=title")).Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Fields_slug_lists_every_published_slug_for_prerendering()
    {
        var (_, build) = await AdminAsync();
        var (category, project) = await build.PublishedAsync();
        var draft = await build.DraftAsync(Guid.Parse(category.Str("id")));

        var (response, body) = await GetAsync("/v1/catalog/projects?fields=slug");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var slugs = body.GetProperty("slugs").EnumerateArray().Select(s => s.GetString()).ToList();
        slugs.ShouldContain(project.Str("slug"));
        slugs.ShouldNotContain(draft.Str("slug"));
    }

    [Fact]
    public async Task Search_normalises_Arabic_and_treats_LIKE_wildcards_as_text()
    {
        var (_, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        var iftar = await build.PublishAsync(await build.DraftAsync(categoryId, titleAr: "إفطار صائم في رمضان"));
        var percent = await build.PublishAsync(await build.DraftAsync(categoryId, titleAr: "خصم 50% للطلاب", titleEn: "Half_price"));
        var scope = $"&category={category.Str("slug")}";

        Slugs((await GetAsync($"/v1/catalog/projects?q=افطار{scope}")).Body).ShouldBe([iftar.Str("slug")]);
        Slugs((await GetAsync($"/v1/catalog/projects?q=%25{scope}")).Body).ShouldBe([percent.Str("slug")]);
        Slugs((await GetAsync($"/v1/catalog/projects?q=a_f{scope}")).Body).ShouldBeEmpty();
        Slugs((await GetAsync($"/v1/catalog/projects?q=Half_price{scope}")).Body).ShouldBe([percent.Str("slug")]);
    }

    [Fact]
    public async Task Search_filters_and_pages_in_one_query_with_a_correct_total()
    {
        var (admin, build) = await AdminAsync();
        var one = await build.CategoryAsync();
        var two = await build.CategoryAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.project (id, category_id, slug, status, title_ar, title_en, allow_open_amount, sort, created_at, updated_at, published_at)
            SELECT gen_random_uuid(), CASE WHEN n <= 150 THEN '{one.Str("id")}'::uuid ELSE '{two.Str("id")}'::uuid END,
                   'bulk-{tag}-' || n, 'published', 'بئر ماء ' || n, 'well{tag} ' || n, true, n, now(), now(), now()
              FROM generate_series(1, 250) AS n
            """);

        var all = (await GetAsync($"/v1/catalog/projects?q=well{tag}&pageSize=100")).Body;
        all.GetProperty("total").GetInt32().ShouldBe(250);
        var page1 = (await GetAsync($"/v1/catalog/projects?q=well{tag}&category={one.Str("slug")}&page=1&pageSize=50")).Body;
        var page2 = (await GetAsync($"/v1/catalog/projects?q=well{tag}&category={one.Str("slug")}&page=2&pageSize=50")).Body;
        var page4 = (await GetAsync($"/v1/catalog/projects?q=well{tag}&category={one.Str("slug")}&page=4&pageSize=50")).Body;

        page1.GetProperty("total").GetInt32().ShouldBe(150);
        page2.GetProperty("total").GetInt32().ShouldBe(150);
        Slugs(page2).Count.ShouldBe(50);
        Slugs(page1).Intersect(Slugs(page2)).ShouldBeEmpty();
        Slugs(page4).ShouldBeEmpty();
        page4.GetProperty("total").GetInt32().ShouldBe(150);
        (await admin.GetAsync($"/v1/admin/projects?q=well{tag}&category={two.Str("id")}&page=2&pageSize=40")).Body.GetProperty("total").GetInt32().ShouldBe(100);
    }

    /// API-10: lists carry the English alt text too (cards: cover; categories: image), like the detail.
    [Fact]
    public async Task Cards_and_categories_carry_the_English_alt_text()
    {
        var (admin, build) = await AdminAsync();
        var image = await build.ImageAsync();
        (await admin.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{image}", new { altAr = "صورة الفئة", altEn = "Category image" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var category = (await admin.PostAsync("/v1/admin/categories", CatalogBuilder.CategoryBody(CatalogBuilder.NewSlug("cat"), image: image))).Body;
        var project = await build.PublishAsync(await build.DraftAsync(Guid.Parse(category.Str("id"))));
        (await admin.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{project.Str("coverAssetId")}", new { altAr = "غلاف", altEn = "Cover" }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var card = (await GetAsync($"/v1/catalog/projects?category={category.Str("slug")}")).Body.GetProperty("items")[0];
        var listed = (await GetAsync("/v1/catalog/categories")).Body.EnumerateArray().Single(c => c.Str("id") == category.Str("id"));
        var home = (await GetAsync("/v1/catalog/home")).Body.GetProperty("categories").EnumerateArray().Single(c => c.Str("id") == category.Str("id"));

        card.Str("coverAltEn").ShouldBe("Cover");
        card.Str("coverAltAr").ShouldBe("غلاف");
        listed.Str("imageAltEn").ShouldBe("Category image");
        home.Str("imageAltEn").ShouldBe("Category image");
    }

    /// API-9: a card lists the active options of its project (sort, id), the same as the detail; none → [].
    [Fact]
    public async Task Cards_carry_the_active_options_of_the_detail()
    {
        var (_, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        var withOptions = await build.PublishAsync(await build.OptionsAsync(await build.DraftAsync(categoryId),
            ("نسخة", "4.00", true), ("مخفي", "7.00", false), ("مئة نسخة", "400.00", true)));
        var openOnly = await build.PublishAsync(await build.DraftAsync(categoryId));

        var cards = (await GetAsync($"/v1/catalog/projects?category={category.Str("slug")}")).Body.GetProperty("items").EnumerateArray().ToList();
        var detail = (await GetAsync($"/v1/catalog/projects/{withOptions.Str("slug")}")).Body;

        var card = cards.Single(c => c.Str("id") == withOptions.Str("id"));
        card.GetProperty("options").GetRawText().ShouldBe(detail.GetProperty("options").GetRawText());
        card.GetProperty("options").EnumerateArray().Select(o => o.Str("labelAr")).ShouldBe(["نسخة", "مئة نسخة"]);
        card.Str("fromAmount").ShouldBe("4.00");
        var open = cards.Single(c => c.Str("id") == openOnly.Str("id"));
        open.GetProperty("options").GetArrayLength().ShouldBe(0);
        open.GetProperty("fromAmount").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// API-9: the listing's ETag follows its options (a label change alone changes the card).
    [Fact]
    public async Task The_listing_ETag_changes_when_an_option_changes()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var project = await build.PublishAsync(await build.OptionsAsync(await build.DraftAsync(Guid.Parse(category.Str("id"))), ("نسخة", "4.00", true)));
        var listing = $"/v1/catalog/projects?category={category.Str("slug")}";
        var before = (await GetAsync(listing)).Response.Headers.ETag!.ToString();
        var option = project.GetProperty("options")[0];

        (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{project.Str("id")}/options", new
        {
            options = new[] { new { id = (Guid?)Guid.Parse(option.Str("id")), labelAr = "نسخة واحدة", labelEn = (string?)null, amount = "4.00", sort = 0, isActive = true } },
            version = project.GetProperty("version").GetUInt32(),
        })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetAsync(listing)).Response.Headers.ETag!.ToString().ShouldNotBe(before);
    }

    [Fact]
    public async Task The_default_listing_orders_by_sort_then_newest_published_then_id()
    {
        var (_, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        var ids = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add((await build.PublishAsync(await build.DraftAsync(categoryId))).Str("id"));
        }

        // Created in the order 0..3; published newest first for 2, 1 and 0; 3 shares 0's publish time and sorts later.
        await factory.ExecuteSqlAsync($"""
            UPDATE store.project SET sort = 1, created_at = '2026-01-01'::timestamptz + (array_position(ARRAY['{ids[0]}','{ids[1]}','{ids[2]}','{ids[3]}']::uuid[], id) * interval '1 day'),
                   published_at = CASE id WHEN '{ids[0]}' THEN '2026-02-01'::timestamptz WHEN '{ids[1]}' THEN '2026-02-02'::timestamptz
                                          WHEN '{ids[2]}' THEN '2026-02-03'::timestamptz ELSE '2026-02-01'::timestamptz END
             WHERE id IN ('{ids[0]}', '{ids[1]}', '{ids[2]}', '{ids[3]}')
            """);
        var sameTime = new[] { ids[0], ids[3] }.Order(StringComparer.Ordinal).ToArray();          // uuid order = text order of the hex form

        var listed = (await GetAsync($"/v1/catalog/projects?category={category.Str("slug")}")).Body
            .GetProperty("items").EnumerateArray().Select(i => i.Str("id")).ToList();

        listed.ShouldBe([ids[2], ids[1], sameTime[0], sameTime[1]]);                                   // R11-3
    }

    [Fact]
    public async Task An_old_slug_does_not_redirect_into_an_inactive_category()
    {
        var (admin, build) = await AdminAsync();
        var (category, project) = await build.PublishedAsync();
        var oldSlug = project.Str("slug");
        (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{project.Str("id")}",
            CatalogBuilder.ProjectBody(Guid.Parse(category.Str("id")), CatalogBuilder.NewSlug("moved"), cover: Guid.Parse(project.Str("coverAssetId")),
                version: project.GetProperty("version").GetUInt32()))).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"/v1/catalog/projects/{oldSlug}")).Body.TryGetProperty("redirectTo", out _).ShouldBeTrue();

        (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{category.Str("id")}",
            CatalogBuilder.CategoryBody(category.Str("slug"), isActive: false, version: category.GetProperty("version").GetUInt32())))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetAsync($"/v1/catalog/projects/{oldSlug}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Rule("D-08")]
    public async Task An_old_slug_of_a_published_project_redirects_and_stays_reserved()
    {
        var (admin, build) = await AdminAsync();
        var (category, project) = await build.PublishedAsync();
        var oldSlug = project.Str("slug");
        var newSlug = CatalogBuilder.NewSlug("renamed");

        var renamed = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{project.Str("id")}",
            CatalogBuilder.ProjectBody(Guid.Parse(category.Str("id")), newSlug, cover: Guid.Parse(project.Str("coverAssetId")),
                version: project.GetProperty("version").GetUInt32()));
        renamed.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        renamed.Body.GetProperty("oldSlugs").EnumerateArray().Select(s => s.GetString()).ShouldBe([oldSlug]);

        var redirect = await GetAsync($"/v1/catalog/projects/{oldSlug}");
        redirect.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        redirect.Body.Str("redirectTo").ShouldBe(newSlug);
        (await GetAsync($"/v1/catalog/projects/{newSlug}")).Body.Str("slug").ShouldBe(newSlug);

        var taken = await admin.PostAsync("/v1/admin/projects", CatalogBuilder.ProjectBody(Guid.Parse(category.Str("id")), oldSlug));
        taken.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        taken.Body.Str("code").ShouldBe(ErrorCodes.ProjectSlugTaken);
        var invalid = await admin.PostAsync("/v1/admin/projects", CatalogBuilder.ProjectBody(Guid.Parse(category.Str("id")), "Bad Slug"));
        invalid.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldContain("slug");
    }

    [Fact]
    public async Task Home_categories_and_config_describe_the_catalogue()
    {
        var (_, build) = await AdminAsync();
        var (category, project) = await build.PublishedAsync();

        var home = (await GetAsync("/v1/catalog/home")).Body;
        home.GetProperty("featured").GetArrayLength().ShouldBeGreaterThan(0);
        home.GetProperty("featured").EnumerateArray().ShouldAllBe(card => card.GetProperty("options").ValueKind == JsonValueKind.Array);   // API-9
        home.GetProperty("amountPresets").GetArrayLength().ShouldBeGreaterThan(0);
        var summary = (await GetAsync("/v1/catalog/categories")).Body.EnumerateArray().Single(c => c.Str("slug") == category.Str("slug"));
        summary.GetProperty("projectCount").GetInt32().ShouldBe(1);

        var config = (await GetAsync("/v1/catalog/config")).Body;
        config.Str("minGiftAmount").ShouldBe("10.00");
        config.Str("maxLineAmount").ShouldBe("100000.00");
        config.GetProperty("cartMaxLines").GetInt32().ShouldBe(50);
        project.Str("status").ShouldBe("published");
    }

    [Fact]
    public async Task Responses_carry_an_ETag_answer_304_and_are_evicted_by_a_catalogue_write()
    {
        var (_, build) = await AdminAsync();
        var first = await GetAsync("/v1/catalog/categories");
        first.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = first.Response.Headers.ETag!.ToString();
        first.Response.Headers.CacheControl!.ToString().ShouldContain("max-age=60");

        (await GetAsync("/v1/catalog/categories", etag)).Response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await GetAsync("/v1/catalog/categories?junk=1")).Response.Headers.ETag!.ToString().ShouldBe(etag);

        var category = await build.CategoryAsync();

        var after = await GetAsync("/v1/catalog/categories", etag);
        after.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.Response.Headers.ETag!.ToString().ShouldNotBe(etag);
        after.Body.EnumerateArray().Select(c => c.Str("slug")).ShouldContain(category.Str("slug"));
    }
}
