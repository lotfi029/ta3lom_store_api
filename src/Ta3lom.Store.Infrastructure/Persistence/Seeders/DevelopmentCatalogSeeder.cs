using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Infrastructure.Persistence.Seeders;

/// Development only: a demo catalogue of 3 categories and 6 published projects shaped like the reference site (fixed-price
/// options, open amounts, options only). Covers and category images are real WebP placeholder files written to the file
/// store. Idempotent: an existing slug is left as it is.
public sealed class DevelopmentCatalogSeeder(StoreDbContext db, IFileStorage files, IClock clock)
{
    private sealed record SeedCategory(string Slug, string NameAr, string NameEn, string DescriptionAr, int Sort, SKColor Color);

    private sealed record SeedOption(string LabelAr, string LabelEn, decimal Amount);

    private sealed record SeedProject(string Slug, string Category, string TitleAr, string TitleEn, string SummaryAr, bool OpenAmount,
        ProjectBadge? Badge, int Sort, SKColor Color, SeedOption[] Options);

    private static readonly SeedCategory[] Categories =
    [
        new("dawah", "الدعوة", "Dawah", "مشاريع الدعوة ونشر العلم النافع.", 1, new SKColor(0x1F, 0x6F, 0x5C)),
        new("education", "التعليم", "Education", "مشاريع التعليم والطباعة والمتعلمين.", 2, new SKColor(0x2B, 0x4C, 0x7E)),
        new("relief", "الإغاثة", "Relief", "سقيا الماء وإطعام الصائمين.", 3, new SKColor(0x8A, 0x4B, 0x2A)),
    ];

    private static readonly SeedProject[] Projects =
    [
        new("print-a-title", "education", "طباعة كتاب", "Print a title", "ساهم في طباعة نسخ عربية ومترجمة توزّع مجانًا.", true,
            ProjectBadge.New, 1, new SKColor(0x3E, 0x7C, 0xB1),
            [new("نسخة عربية", "Arabic copy", 4.00m), new("نسخة مترجمة", "Translated copy", 5.00m)]),
        new("dawah-umrah", "dawah", "العمرة الدعوية", "Dawah Umrah", "تكفّل بتكاليف معتمر في رحلة دعوية.", true,
            null, 2, new SKColor(0x2E, 0x8B, 0x57),
            [new("تكلفة المعتمر", "One pilgrim", 150.00m)]),
        new("support-a-daiya", "dawah", "كفالة داعية", "Support a daiya", "اكفل داعية بالساعة أو اليوم أو الأسبوع أو الشهر.", false,
            ProjectBadge.Urgent, 3, new SKColor(0x6A, 0x4C, 0x93),
            [new("ساعة", "One hour", 25.00m), new("يوم", "One day", 100.00m), new("أسبوع", "One week", 500.00m), new("شهر", "One month", 2000.00m)]),
        new("water-wells", "relief", "سقيا الماء", "Water wells", "حفر الآبار وتوفير الماء النظيف.", true, null, 4, new SKColor(0x1E, 0x88, 0xE5), []),
        new("iftar-meals", "relief", "إفطار صائم", "Iftar meals", "وجبات إفطار للصائمين.", true, null, 5, new SKColor(0xC0, 0x7A, 0x2C), []),
        new("learner-support", "education", "دعم المتعلمين", "Learner support", "ادعم المتعلمين في برامج الجمعية.", true, null, 6, new SKColor(0x45, 0x5A, 0x64), []),
    ];

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var created = 0;
        var categories = new Dictionary<string, Category>(StringComparer.Ordinal);
        foreach (var c in Categories)
        {
            var category = await db.Categories.FirstOrDefaultAsync(x => x.Slug == c.Slug, ct);
            if (category is null)
            {
                var image = await PlaceholderAsync($"seed/category-{c.Slug}.webp", c.NameAr, c.Color, now, ct);
                category = Category.Create(new CategoryDetails(c.Slug, c.NameAr, c.NameEn, c.DescriptionAr, null, true, c.Sort, true), image, now);
                db.Categories.Add(category);
                created++;
            }

            categories[c.Slug] = category;
        }

        await db.SaveChangesAsync(ct);
        foreach (var p in Projects)
        {
            if (await db.Projects.AnyAsync(x => x.Slug == p.Slug, ct))
            {
                continue;
            }

            var category = categories[p.Category];
            var cover = await PlaceholderAsync($"seed/project-{p.Slug}.webp", p.TitleAr, p.Color, now, ct);
            var details = new ProjectDetails(p.Slug, p.TitleAr, p.TitleEn, p.SummaryAr, null,
                $"## {p.TitleAr}\n\n{p.SummaryAr}\n\nكل مساهمة تصل كاملة إلى المشروع.", null,
                p.OpenAmount, null, p.Badge, p.Sort, p.TitleAr, p.TitleEn, p.SummaryAr, null);
            var project = Project.CreateDraft(details, category, cover, now);
            project.ReplaceOptions([.. p.Options.Select((o, i) => new ProjectOptionInput(null, o.LabelAr, o.LabelEn, new Money(o.Amount), i + 1, true))],
                new HashSet<Guid>(), now);
            var published = project.Publish(category, cover, now);
            if (published.IsFailure)
            {
                throw new InvalidOperationException($"Seed project {p.Slug} cannot be published: {published.Error.Code}");
            }

            db.Projects.Add(project);
            created++;
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    /// A 1200×630 WebP filled with the colour, saved to the public file store; the media row points at it.
    private async Task<MediaAsset> PlaceholderAsync(string key, string altAr, SKColor color, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.MediaAssets.FirstOrDefaultAsync(m => m.StorageKey == key, ct) is { } existing)
        {
            return existing;
        }

        byte[] bytes;
        using (var bitmap = new SKBitmap(1200, 630))
        {
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(color);
                using var band = new SKPaint { Color = new SKColor(255, 255, 255, 40) };
                canvas.DrawRect(new SKRect(0, 470, 1200, 630), band);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
            bytes = encoded.ToArray();
        }

        await using (var content = new MemoryStream(bytes, writable: false))
        {
            await files.SaveAsync(key, isPublic: true, content, ct);
        }

        var asset = new MediaAsset(MediaKind.Image, key, "image/webp", bytes.LongLength, 1200, 630, altAr, null, "seed", now);
        db.MediaAssets.Add(asset);
        return asset;
    }
}
