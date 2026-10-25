using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.Tests.Support;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Catalog;

public sealed class CategoryTests
{
    [Fact]
    [Rule("D-05")]
    public void Only_an_active_category_with_general_gifts_takes_category_lines()
    {
        Make.Category().AcceptsCategoryGifts.ShouldBeTrue();
        Make.Category(active: false).AcceptsCategoryGifts.ShouldBeFalse();
        Make.Category(generalGifts: false).AcceptsCategoryGifts.ShouldBeFalse();
    }

    [Fact]
    public void Create_and_update_trim_and_raise_catalog_changed()
    {
        var image = Make.Image();
        var category = Category.Create(new CategoryDetails("relief", " الإغاثة ", " ", "وصف", null, true, 3, true), image, Make.Now);

        category.NameAr.ShouldBe("الإغاثة");
        category.NameEn.ShouldBeNull();
        category.ImageAssetId.ShouldBe(image.Id);

        category.Update(new CategoryDetails("relief-2", "إغاثة", "Relief", null, null, false, 4, false), null, Make.Now.AddHours(1));
        category.MoveTo(9, Make.Now.AddHours(2));
        category.MoveTo(9, Make.Now.AddHours(3));

        category.Slug.ShouldBe("relief-2");
        category.ImageAssetId.ShouldBeNull();
        category.Sort.ShouldBe(9);
        category.UpdatedAt.ShouldBe(Make.Now.AddHours(2));
        category.DomainEvents.OfType<CatalogChanged>().Select(e => e.Change).ShouldBe(["created", "updated", "reordered"]);
        category.IsVersion(0).ShouldBeTrue();
    }

    [Fact]
    [Rule("D-06")]
    public void A_category_image_must_be_a_public_image()
    {
        Should.Throw<DomainException>(() => Category.Create(new CategoryDetails("relief", "إغاثة", null, null, null, true, 1, true), Make.Pdf(), Make.Now))
            .Code.ShouldBe("media.image_required");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("Relief")]
    public void Category_slugs_follow_the_pattern(string slug) =>
        Should.Throw<DomainException>(() => Category.Create(new CategoryDetails(slug, "إغاثة", null, null, null, true, 1, true), null, Make.Now));
}
