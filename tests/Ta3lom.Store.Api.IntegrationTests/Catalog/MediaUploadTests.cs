using System.Net;
using System.Text;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// `POST /v1/admin/media`: the type comes from the bytes, images are re-encoded as WebP, the pixel count is checked before
/// any decoding.
public sealed class MediaUploadTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Admin, CatalogBuilder Build)> AdminAsync()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        return (admin, new CatalogBuilder(admin));
    }

    [Fact]
    public async Task An_image_is_sniffed_by_its_bytes_and_stored_as_webp()
    {
        var (admin, build) = await AdminAsync();

        var (response, body) = await build.UploadAsync(CatalogBuilder.TinyPng, "photo.jpg", "image/jpeg");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        body.Str("mime").ShouldBe("image/webp");
        body.Str("url").ShouldEndWith(".webp");
        body.GetProperty("width").GetInt32().ShouldBe(1);
        var alt = await admin.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{body.Str("id")}", new { altAr = "غلاف", altEn = "Cover" });
        alt.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        alt.Body.Str("altEn").ShouldBe("Cover");
        (await admin.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{body.Str("id")}", new { altAr = new string('غ', 301), altEn = "Cover" }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);                                                // L-6: same cap as the upload
        (await admin.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{body.Str("id")}", new { altAr = "غلاف", altEn = new string('x', 301) }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/v1/admin/media?kind=image")).Body.GetProperty("items").EnumerateArray().Select(m => m.Str("id")).ShouldContain(body.Str("id"));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "logo.svg", "image/svg+xml")]
    [InlineData("just some text", "cover.png", "image/png")]
    public async Task Svg_and_disguised_files_are_refused(string content, string fileName, string contentType)
    {
        var (_, build) = await AdminAsync();

        var (response, body) = await build.UploadAsync(Encoding.UTF8.GetBytes(content), fileName, contentType);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        body.Str("code").ShouldBe(ErrorCodes.MediaTypeNotAllowed);
    }

    [Fact]
    public async Task An_image_above_40_megapixels_is_refused_before_decoding()
    {
        var (_, build) = await AdminAsync();

        var (response, body) = await build.UploadAsync(CatalogBuilder.PngHeader(8000, 6000), "huge.png", "image/png");

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        body.Str("code").ShouldBe(ErrorCodes.MediaTooLarge);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.media_asset WHERE width = 8000")).ShouldBe(0);
    }

    [Fact]
    public async Task An_image_needs_its_Arabic_alt_text()
    {
        var (_, build) = await AdminAsync();

        var (response, body) = await build.UploadAsync(CatalogBuilder.TinyPng, "cover.png", "image/png", altAr: null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.Str("code").ShouldBe(ErrorCodes.MediaAltRequired);
    }

    [Fact]
    public async Task A_viewer_cannot_upload()
    {
        var viewer = await factory.StaffAsync(Roles.Viewer);

        var (response, _) = await new CatalogBuilder(viewer).UploadAsync(CatalogBuilder.TinyPng, "cover.png", "image/png");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
