using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Catalog;

/// Products and categories (RUL-01 … RUL-03).
public sealed class ProductTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(1);

    private static Product Draft(ProductType type = ProductType.Book) => new(type, "arabic-grammar", "النحو", "ملخص", T0);

    private static void Update(Product p, ProductType type, bool hasOrders, string slug = "new-slug", string titleAr = "عنوان", string summaryAr = "ملخص")
    {
        p.Update(type, hasOrders, slug, null, null, titleAr, null, summaryAr, null, null, null, null, 0, null, null, null, null, T1);
    }

    [Fact]
    [Rule("RUL-02")]
    public void A_new_product_is_an_unpublished_draft()
    {
        var p = Draft();

        p.Status.ShouldBe(ProductStatus.Draft);
        p.IsPublished.ShouldBeFalse();
        p.PublishedAt.ShouldBeNull();
        p.Type.ShouldBe(ProductType.Book);
        p.Slug.ShouldBe("arabic-grammar");
        p.TitleAr.ShouldBe("النحو");
        p.SummaryAr.ShouldBe("ملخص");
        p.Sort.ShouldBe(0);
        p.CreatedAt.ShouldBe(T0);
        p.UpdatedAt.ShouldBe(T0);
        Should.Throw<DomainException>(() => new Product(ProductType.Book, null!, "t", "s", T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-01")]
    public void The_type_is_locked_once_the_product_has_an_order()
    {
        var p = Draft(ProductType.Book);

        Should.Throw<DomainException>(() => Update(p, ProductType.Course, hasOrders: true)).Code.ShouldBe("product.type_locked");
        p.Type.ShouldBe(ProductType.Book);

        Update(p, ProductType.Book, hasOrders: true);
        p.Type.ShouldBe(ProductType.Book);

        Update(p, ProductType.Course, hasOrders: false);
        p.Type.ShouldBe(ProductType.Course);
    }

    [Fact]
    [Rule("RUL-03")]
    public void Arabic_title_summary_and_slug_are_mandatory_english_is_optional()
    {
        var p = Draft();

        Should.Throw<DomainException>(() => Update(p, ProductType.Book, false, titleAr: " ")).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => Update(p, ProductType.Book, false, summaryAr: "")).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => Update(p, ProductType.Book, false, slug: " ")).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void Update_sets_every_common_field()
    {
        var p = Draft();
        var category = Guid.CreateVersion7();
        var cover = Guid.CreateVersion7();

        p.Update(ProductType.Book, false, "grammar", category, cover, "النحو الواضح", "Clear Grammar", "ملخص", "Summary", "نص", "Body",
            ProductBadge.New, 7, "سيو", "SEO", "وصف", "Description", T1);

        p.Slug.ShouldBe("grammar");
        p.CategoryId.ShouldBe(category);
        p.CoverAssetId.ShouldBe(cover);
        p.TitleAr.ShouldBe("النحو الواضح");
        p.TitleEn.ShouldBe("Clear Grammar");
        p.SummaryAr.ShouldBe("ملخص");
        p.SummaryEn.ShouldBe("Summary");
        p.BodyAr.ShouldBe("نص");
        p.BodyEn.ShouldBe("Body");
        p.Badge.ShouldBe(ProductBadge.New);
        p.Sort.ShouldBe(7);
        p.SeoTitleAr.ShouldBe("سيو");
        p.SeoTitleEn.ShouldBe("SEO");
        p.SeoDescAr.ShouldBe("وصف");
        p.SeoDescEn.ShouldBe("Description");
        p.UpdatedAt.ShouldBe(T1);
    }

    [Fact]
    [Rule("RUL-02")]
    public void Publishing_keeps_the_first_publication_time_and_archiving_hides_it()
    {
        var p = Draft();

        p.Publish(T1);
        p.IsPublished.ShouldBeTrue();
        p.PublishedAt.ShouldBe(T1);

        p.Archive(T1.AddDays(1));
        p.Status.ShouldBe(ProductStatus.Archived);
        p.IsPublished.ShouldBeFalse();

        p.Publish(T1.AddDays(2));
        p.PublishedAt.ShouldBe(T1);
        p.UpdatedAt.ShouldBe(T1.AddDays(2));

        p.Touch(T1.AddDays(3));
        p.UpdatedAt.ShouldBe(T1.AddDays(3));
    }

    [Fact]
    public void A_category_is_active_and_editable()
    {
        var c = new Category("books", "كتب");

        c.Slug.ShouldBe("books");
        c.NameAr.ShouldBe("كتب");
        c.NameEn.ShouldBeNull();
        c.Sort.ShouldBe(0);
        c.IsActive.ShouldBeTrue();

        c.Update("courses", "دورات", "Courses", 3, false);

        c.Slug.ShouldBe("courses");
        c.NameAr.ShouldBe("دورات");
        c.NameEn.ShouldBe("Courses");
        c.Sort.ShouldBe(3);
        c.IsActive.ShouldBeFalse();
        Should.Throw<DomainException>(() => c.Update(" ", "دورات", null, 0, true)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => c.Update("x", " ", null, 0, true)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => new Category("x", null!)).Code.ShouldBe("guard.null");
    }
}
