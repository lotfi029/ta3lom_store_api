using System.Net;
using System.Text;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// `/v1/admin/categories`, `/v1/admin/projects` (BR-A01…A05).
public sealed class AdminCatalogTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Admin, CatalogBuilder Build)> AdminAsync()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        return (admin, new CatalogBuilder(admin));
    }

    [Fact]
    public async Task Categories_are_created_updated_with_their_version_and_reordered()
    {
        var (admin, build) = await AdminAsync();
        var image = await build.ImageAsync();
        var slug = CatalogBuilder.NewSlug("cat");
        var created = await admin.PostAsync("/v1/admin/categories", CatalogBuilder.CategoryBody(slug, image: image));
        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Body.Str("imageUrl").ShouldEndWith(".webp");
        var id = created.Body.Str("id");
        var version = created.Body.GetProperty("version").GetUInt32();

        var updated = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{id}", CatalogBuilder.CategoryBody(slug, "تعليم القرآن", image, version: version));
        updated.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated.Body.Str("nameAr").ShouldBe("تعليم القرآن");
        var stale = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{id}", CatalogBuilder.CategoryBody(slug, "قديم", version: version));
        stale.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stale.Body.Str("code").ShouldBe(ErrorCodes.ConcurrencyConflict);

        var duplicate = await admin.PostAsync("/v1/admin/categories", CatalogBuilder.CategoryBody(slug));
        duplicate.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        duplicate.Body.Str("code").ShouldBe(ErrorCodes.CategorySlugTaken);

        var order = await admin.SendAsync(HttpMethod.Put, "/v1/admin/categories/order", new { items = new[] { new { id, sort = 7 } } });
        order.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        order.Body.EnumerateArray().Single(c => c.Str("id") == id).GetProperty("sort").GetInt32().ShouldBe(7);
        (await admin.GetAsync("/v1/admin/categories")).Body.EnumerateArray().Select(c => c.Str("id")).ShouldContain(id);
        (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{Guid.NewGuid()}", CatalogBuilder.CategoryBody(CatalogBuilder.NewSlug("x"), version: 1)))
            .Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reordering_with_a_repeated_category_is_400()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var id = category.Str("id");

        var repeated = await admin.SendAsync(HttpMethod.Put, "/v1/admin/categories/order", new { items = new[] { new { id, sort = 1 }, new { id, sort = 2 } } });

        repeated.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        repeated.Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Category_input_is_validated()
    {
        var (admin, _) = await AdminAsync();

        var invalid = await admin.PostAsync("/v1/admin/categories", CatalogBuilder.CategoryBody("x", nameAr: ""));

        invalid.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Body.Str("code").ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Covers_and_category_images_must_be_public_images()
    {
        var (admin, build) = await AdminAsync();
        var pdf = await build.UploadAsync(Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF\n"), "guide.pdf", "application/pdf", kind: "pdf", altAr: null);
        pdf.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var pdfId = Guid.Parse(pdf.Body.Str("id"));
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));

        var categoryImage = await admin.PostAsync("/v1/admin/categories", CatalogBuilder.CategoryBody(CatalogBuilder.NewSlug("cat"), image: pdfId));
        categoryImage.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        categoryImage.Body.Str("code").ShouldBe(ErrorCodes.MediaImageRequired);
        var categoryUpdate = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{categoryId}",
            CatalogBuilder.CategoryBody(category.Str("slug"), image: pdfId, version: category.GetProperty("version").GetUInt32()));
        categoryUpdate.Body.Str("code").ShouldBe(ErrorCodes.MediaImageRequired);

        var cover = await admin.PostAsync("/v1/admin/projects", CatalogBuilder.ProjectBody(categoryId, CatalogBuilder.NewSlug("prj"), cover: pdfId));
        cover.Body.Str("code").ShouldBe(ErrorCodes.MediaImageRequired);
        var draft = await build.DraftAsync(categoryId);
        var coverUpdate = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{draft.Str("id")}",
            CatalogBuilder.ProjectBody(categoryId, draft.Str("slug"), cover: pdfId, version: draft.GetProperty("version").GetUInt32()));
        coverUpdate.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        coverUpdate.Body.Str("code").ShouldBe(ErrorCodes.MediaImageRequired);
        (await admin.PostAsync("/v1/admin/projects", CatalogBuilder.ProjectBody(Guid.NewGuid(), CatalogBuilder.NewSlug("prj"))))
            .Body.Str("code").ShouldBe(ErrorCodes.CategoryNotFound);
    }

    [Fact]
    [Rule("D-06")]
    public async Task Publishing_an_incomplete_project_is_422_with_what_is_missing()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var created = await admin.PostAsync("/v1/admin/projects",
            CatalogBuilder.ProjectBody(Guid.Parse(category.Str("id")), CatalogBuilder.NewSlug("prj"), allowOpenAmount: false, summaryAr: null, bodyAr: null));
        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Body.GetProperty("missingForPublish").EnumerateArray().Select(m => m.GetString())
            .ShouldBe(["summaryAr", "bodyAr", "cover", "amount"], ignoreOrder: true);

        var publish = await admin.PostAsync($"/v1/admin/projects/{created.Body.Str("id")}/publish");

        publish.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        publish.Body.Str("code").ShouldBe(ErrorCodes.ProjectPublishIncomplete);
        publish.Body.GetProperty("missing").EnumerateArray().Select(m => m.GetString()).ShouldBe(["summaryAr", "bodyAr", "cover", "amount"], ignoreOrder: true);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.project WHERE id = '{created.Body.Str("id")}'")).ShouldBe("draft");
    }

    [Fact]
    [Rule("D-07")]
    public async Task Only_the_allowed_status_changes_are_accepted()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var draft = await build.DraftAsync(Guid.Parse(category.Str("id")));
        var id = draft.Str("id");

        var archiveDraft = await admin.PostAsync($"/v1/admin/projects/{id}/archive");
        archiveDraft.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        archiveDraft.Body.Str("code").ShouldBe(ErrorCodes.ProjectInvalidTransition);

        (await admin.PostAsync($"/v1/admin/projects/{id}/publish")).Body.Str("status").ShouldBe("published");
        (await admin.PostAsync($"/v1/admin/projects/{id}/publish")).Body.Str("code").ShouldBe(ErrorCodes.ProjectInvalidTransition);
        (await admin.PostAsync($"/v1/admin/projects/{id}/archive")).Body.Str("status").ShouldBe("archived");
        (await admin.PostAsync($"/v1/admin/projects/{id}/archive")).Body.Str("code").ShouldBe(ErrorCodes.ProjectInvalidTransition);
        (await admin.PostAsync($"/v1/admin/projects/{id}/publish")).Body.Str("status").ShouldBe("published");
        (await admin.PostAsync($"/v1/admin/projects/{Guid.NewGuid()}/publish")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_project_is_updated_with_its_version_and_listed_with_filters()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        var draft = await build.DraftAsync(categoryId, titleAr: "سقيا الماء");
        var id = draft.Str("id");
        var body = CatalogBuilder.ProjectBody(categoryId, draft.Str("slug"), "سقيا ماء", Guid.Parse(draft.Str("coverAssetId")), minAmount: "25.00",
            badge: "urgent", version: draft.GetProperty("version").GetUInt32());

        var updated = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{id}", body);
        updated.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated.Body.Str("minAmount").ShouldBe("25.00");
        updated.Body.Str("badge").ShouldBe("urgent");
        var stale = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{id}", body);
        stale.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stale.Body.Str("code").ShouldBe(ErrorCodes.ConcurrencyConflict);
        var noVersion = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{id}",
            CatalogBuilder.ProjectBody(categoryId, draft.Str("slug"), minAmount: "-1"));
        noVersion.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noVersion.Body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["minAmount", "version"], ignoreOrder: true);

        var list = await admin.GetAsync($"/v1/admin/projects?status=draft&category={categoryId}");
        list.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = list.Body.GetProperty("items").EnumerateArray().Single();
        row.Str("status").ShouldBe("draft");
        row.Str("categoryNameAr").ShouldBe(category.Str("nameAr"));
        (await admin.GetAsync($"/v1/admin/projects?q=سقيا&category={categoryId}")).Body.GetProperty("total").GetInt32().ShouldBe(1);
        (await admin.GetAsync("/v1/admin/projects?status=deleted")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = await admin.GetAsync($"/v1/admin/projects/{id}");
        detail.Body.Str("titleAr").ShouldBe("سقيا ماء");
        var preview = await admin.GetAsync($"/v1/admin/projects/{id}/preview");
        preview.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        preview.Body.Str("status").ShouldBe("draft");
        preview.Body.Str("minAmount").ShouldBe("25.00");
        (await admin.GetAsync($"/v1/admin/projects/{Guid.NewGuid()}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Options_are_replaced_as_a_list_and_a_published_project_keeps_an_amount()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var draft = await build.DraftAsync(Guid.Parse(category.Str("id")), allowOpenAmount: false);

        var withOptions = await build.OptionsAsync(draft, ("نسخة", "4.00", true), ("خمس", "5.00", true));
        withOptions.GetProperty("options").GetArrayLength().ShouldBe(2);
        var published = await build.PublishAsync(withOptions);

        var none = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{published.Str("id")}/options",
            new { options = Array.Empty<object>(), version = published.GetProperty("version").GetUInt32() });
        none.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        none.Body.Str("code").ShouldBe(ErrorCodes.ProjectPublishIncomplete);

        var zero = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{published.Str("id")}/options",
            new
            {
                options = new[] { new { id = (Guid?)null, labelAr = "صفر", labelEn = (string?)null, amount = "0.00", sort = 0, isActive = true } },
                version = published.GetProperty("version").GetUInt32()
            });
        zero.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var kept = published.GetProperty("options").EnumerateArray().First();
        var replaced = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{published.Str("id")}/options",
            new
            {
                options = new[] { new { id = (Guid?)Guid.Parse(kept.Str("id")), labelAr = kept.Str("labelAr"), labelEn = (string?)null, amount = "6.00", sort = 0, isActive = true } },
                version = published.GetProperty("version").GetUInt32(),
            });
        replaced.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        replaced.Body.GetProperty("options").EnumerateArray().Select(o => (o.Str("id"), o.Str("amount"))).ShouldBe([(kept.Str("id"), "6.00")]);
    }

    [Fact]
    public async Task Admin_money_fields_accept_Arabic_digits_and_refuse_malformed_ones()
    {
        var (admin, build) = await AdminAsync();
        var category = await build.CategoryAsync();
        var draft = await build.DraftAsync(Guid.Parse(category.Str("id")));
        var version = draft.GetProperty("version").GetUInt32();

        var options = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{draft.Str("id")}/options",
            new { options = new[] { new { id = (Guid?)null, labelAr = "سهم", labelEn = (string?)null, amount = "١٢٫٥", sort = 0, isActive = true } }, version });
        options.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        options.Body.GetProperty("options")[0].Str("amount").ShouldBe("12.50");

        var bad = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{draft.Str("id")}/options",
            new
            {
                options = new[] { new { id = (Guid?)null, labelAr = "سهم", labelEn = (string?)null, amount = "٥x", sort = 0, isActive = true } },
                version = options.Body.GetProperty("version").GetUInt32()
            });
        bad.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var minimum = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{draft.Str("id")}",
            CatalogBuilder.ProjectBody(Guid.Parse(category.Str("id")), draft.Str("slug"), minAmount: "١٬٠٠٠", version: options.Body.GetProperty("version").GetUInt32()));
        minimum.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var setting = await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/min_gift_amount", new { value = "١٠٫٠٠" });
        setting.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ScalarAsync<string>("SELECT value #>> '{}' FROM store.setting WHERE key = 'min_gift_amount'")).ShouldBe("10.00");
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/min_gift_amount", new { value = "١٠٫٠x" })).Response.StatusCode
            .ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(Roles.Finance)]
    [InlineData(Roles.Viewer)]
    public async Task Read_only_roles_see_the_catalogue_but_cannot_change_it(string role)
    {
        var member = await factory.StaffAsync(role);

        (await member.GetAsync("/v1/admin/projects")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.GetAsync("/v1/admin/categories")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var write = await member.PostAsync("/v1/admin/categories", CatalogBuilder.CategoryBody(CatalogBuilder.NewSlug("cat")));
        write.Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        write.Body.Str("code").ShouldBe(ErrorCodes.Forbidden);
        (await factory.Anonymous().GetAsync("/v1/admin/projects", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_store_manager_edits_the_catalogue()
    {
        var manager = await factory.StaffAsync(Roles.StoreManager);

        var created = await new CatalogBuilder(manager).CategoryAsync();

        created.Str("slug").ShouldStartWith("cat-");
    }
}
