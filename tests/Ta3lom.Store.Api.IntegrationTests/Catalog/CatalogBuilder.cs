using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// Builds catalogue data through the admin API (an admin client), so the tests cover the same path staff use.
internal sealed class CatalogBuilder(ActorClient admin)
{
    /// A 1×1 PNG.
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static string NewSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 60)];

    public async Task<(HttpResponseMessage Response, JsonElement Body)> UploadAsync(byte[] bytes, string fileName, string contentType,
        string kind = "image", string? altAr = "صورة")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        if (altAr is not null)
        {
            form.Add(new StringContent(altAr), "altAr");
        }

        var response = await admin.Http.PostAsync("/v1/admin/media", form, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    public async Task<Guid> ImageAsync()
    {
        var (response, body) = await UploadAsync(TinyPng, "cover.png", "image/png");
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Guid.Parse(body.Str("id"));
    }

    public static object CategoryBody(string slug, string nameAr = "تعليم", Guid? image = null, bool acceptsGeneralGifts = true, bool isActive = true,
        int sort = 0, uint? version = null) =>
        new { slug, nameAr, nameEn = "Education", descriptionAr = "وصف", descriptionEn = (string?)null, imageAssetId = image, acceptsGeneralGifts, sort, isActive, version };

    public async Task<JsonElement> CategoryAsync(string? slug = null, bool acceptsGeneralGifts = true, bool isActive = true)
    {
        var created = await admin.PostAsync("/v1/admin/categories", CategoryBody(slug ?? NewSlug("cat"), acceptsGeneralGifts: acceptsGeneralGifts, isActive: isActive));
        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return created.Body;
    }

    public static object ProjectBody(Guid categoryId, string slug, string titleAr = "طباعة كتاب", Guid? cover = null, bool allowOpenAmount = true,
        string? minAmount = null, string? summaryAr = "ملخص", string? bodyAr = "القصة", string? badge = null, int sort = 0, uint? version = null,
        string? titleEn = null) =>
        new
        {
            categoryId,
            slug,
            titleAr,
            titleEn,
            summaryAr,
            summaryEn = (string?)null,
            bodyAr,
            bodyEn = (string?)null,
            coverAssetId = cover,
            allowOpenAmount,
            minAmount,
            badge,
            sort,
            seoTitleAr = (string?)null,
            seoTitleEn = (string?)null,
            seoDescAr = (string?)null,
            seoDescEn = (string?)null,
            version,
        };

    public async Task<JsonElement> DraftAsync(Guid categoryId, string? slug = null, string titleAr = "طباعة كتاب", bool withCover = true, bool allowOpenAmount = true,
        string? titleEn = null)
    {
        var cover = withCover ? await ImageAsync() : (Guid?)null;
        var created = await admin.PostAsync("/v1/admin/projects",
            ProjectBody(categoryId, slug ?? NewSlug("prj"), titleAr, cover, allowOpenAmount, titleEn: titleEn));
        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return created.Body;
    }

    public async Task<JsonElement> OptionsAsync(JsonElement project, params (string Label, string Amount, bool Active)[] options)
    {
        var replaced = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/projects/{project.Str("id")}/options", new
        {
            options = options.Select((o, i) => new { id = (Guid?)null, labelAr = o.Label, labelEn = (string?)null, amount = o.Amount, sort = i, isActive = o.Active }),
            version = project.GetProperty("version").GetUInt32(),
        });
        replaced.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return replaced.Body;
    }

    public async Task<JsonElement> PublishAsync(JsonElement project)
    {
        var published = await admin.PostAsync($"/v1/admin/projects/{project.Str("id")}/publish");
        published.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return published.Body;
    }

    /// A published open-amount project in a new active category.
    public async Task<(JsonElement Category, JsonElement Project)> PublishedAsync(string titleAr = "طباعة كتاب", string? titleEn = null)
    {
        var category = await CategoryAsync();
        var project = await PublishAsync(await DraftAsync(Guid.Parse(category.Str("id")), titleAr: titleAr, titleEn: titleEn));
        return (category, project);
    }

    /// A PNG whose header claims <paramref name="width"/> × <paramref name="height"/> (one empty IDAT): enough for the
    /// header check, never decoded.
    public static byte[] PngHeader(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 0;   // grayscale
        Chunk(stream, "IHDR", ihdr);
        using (var idat = new MemoryStream())
        {
            using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            {
                z.Write(new byte[width + 1]);
            }

            Chunk(stream, "IDAT", idat.ToArray());
        }

        Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
