using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Entities.Settings;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Common;

/// Media assets (RUL-03, RUL-59), settings (03 §M) and the audit log.
public sealed class AdminRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Rule("RUL-03")]
    public void Images_need_arabic_alt_text()
    {
        Should.Throw<DomainException>(() => MediaAsset.Create(MediaKind.Image, "k", "image/webp", 10, 1, 1, " ", null, T0)).Code.ShouldBe("media.alt_required");

        var image = MediaAsset.Create(MediaKind.Image, "img/1.webp", "image/webp", 2_048, 800, 600, "غلاف", "Cover", T0);

        image.Kind.ShouldBe(MediaKind.Image);
        image.StorageKey.ShouldBe("img/1.webp");
        image.Mime.ShouldBe("image/webp");
        image.SizeBytes.ShouldBe(2_048);
        image.Width.ShouldBe(800);
        image.Height.ShouldBe(600);
        image.AltAr.ShouldBe("غلاف");
        image.AltEn.ShouldBe("Cover");
        image.IsPublic.ShouldBeTrue();
        image.CreatedAt.ShouldBe(T0);

        Should.Throw<DomainException>(() => image.SetAlt(null, "Cover")).Code.ShouldBe("media.alt_required");
        image.SetAlt("صورة", null);
        image.AltAr.ShouldBe("صورة");
        image.AltEn.ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-05")]
    public void Book_files_and_generated_documents_are_private()
    {
        var file = MediaAsset.Create(MediaKind.BookFile, "books/1.pdf", "application/pdf", 1_000, null, null, null, null, T0);
        var pdf = MediaAsset.PrivateDocument("docs/INV-2026-000001.pdf", 5_000, T0);
        var brochure = new MediaAsset(MediaKind.Pdf, "p/1.pdf", "application/pdf", 10, T0);

        file.IsPublic.ShouldBeFalse();
        file.SetAlt(null, null);
        pdf.IsPublic.ShouldBeFalse();
        pdf.Kind.ShouldBe(MediaKind.Pdf);
        pdf.Mime.ShouldBe("application/pdf");
        pdf.SizeBytes.ShouldBe(5_000);
        brochure.IsPublic.ShouldBeTrue();
        Should.Throw<DomainException>(() => new MediaAsset(MediaKind.Pdf, null!, "m", 1, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void A_setting_value_is_json_text_with_its_editor()
    {
        var s = new Setting("vat_rate", "15", T0);

        s.Key.ShouldBe("vat_rate");
        s.Value.ShouldBe("15");
        s.UpdatedAt.ShouldBe(T0);
        s.UpdatedBy.ShouldBeNull();

        s.SetValue("5", "admin@ta3lom.sa", T0.AddDays(1));

        s.Value.ShouldBe("5");
        s.UpdatedBy.ShouldBe("admin@ta3lom.sa");
        s.UpdatedAt.ShouldBe(T0.AddDays(1));
        Should.Throw<DomainException>(() => s.SetValue(" ", null, T0)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => new Setting("k", null!, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void An_audit_entry_records_who_did_what()
    {
        var e = new AuditLogEntry("admin@ta3lom.sa", "update", "product", T0, "p-1", """{"slug":["a","b"]}""", "ip");
        var minimal = new AuditLogEntry("admin@ta3lom.sa", "create", "category", T0);

        e.At.ShouldBe(T0);
        e.Actor.ShouldBe("admin@ta3lom.sa");
        e.Action.ShouldBe("update");
        e.Entity.ShouldBe("product");
        e.EntityId.ShouldBe("p-1");
        e.Diff.ShouldBe("""{"slug":["a","b"]}""");
        e.IpHash.ShouldBe("ip");
        minimal.EntityId.ShouldBeNull();
        minimal.Diff.ShouldBeNull();
        minimal.IpHash.ShouldBeNull();
        Should.Throw<DomainException>(() => new AuditLogEntry("a", null!, "e", T0)).Code.ShouldBe("guard.null");
    }
}
