using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Domain.Tests.Support;

/// Builders for the catalogue and cart tests.
internal static class Make
{
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    public static readonly CartLimits Limits = new(new Money(10.00m), new Money(100_000.00m), 50, new Money(100_000.00m));

    public static MediaAsset Image() => new(MediaKind.Image, $"images/{Guid.NewGuid():N}.webp", "image/webp", 1024, 1200, 630, "غلاف", null, "test", Now);

    public static MediaAsset Pdf() => new(MediaKind.Pdf, $"pdf/{Guid.NewGuid():N}.pdf", "application/pdf", 1024, null, null, null, null, "test", Now);

    public static Category Category(bool active = true, bool generalGifts = true) =>
        Entities.Catalog.Category.Create(new CategoryDetails($"cat-{Guid.NewGuid():N}"[..20], "الدعوة", "Dawah", null, null, generalGifts, 1, active), null, Now);

    public static ProjectDetails Details(string? slug = null, bool openAmount = true, Money? min = null, string? summary = "ملخص", string? body = "نص") =>
        new(slug ?? $"p-{Guid.NewGuid():N}"[..20], "مشروع", "Project", summary, null, body, null, openAmount, min, null, 1, null, null, null, null);

    public static Project Draft(Category category, bool openAmount = true, Money? min = null, MediaAsset? cover = null) =>
        Project.CreateDraft(Details(openAmount: openAmount, min: min), category, cover ?? Image(), Now);

    /// A published project with the given options (labels "خيار 1" …).
    public static Project Published(Category category, bool openAmount = true, Money? min = null, params decimal[] options)
    {
        var cover = Image();
        var project = Project.CreateDraft(Details(openAmount: openAmount, min: min), category, cover, Now);
        project.ReplaceOptions([.. options.Select((a, i) => new ProjectOptionInput(null, $"خيار {i + 1}", null, new Money(a), i, true))], new HashSet<Guid>(), Now)
            .IsSuccess.ShouldBeTrue();
        project.Publish(category, cover, Now).IsSuccess.ShouldBeTrue();
        project.ClearDomainEvents();
        return project;
    }
}
