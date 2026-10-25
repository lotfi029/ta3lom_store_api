using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.Tests.Support;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Donations;

public sealed class CartTests
{
    private readonly Category _category = Make.Category();
    private readonly Cart _cart = Cart.CreateGuest(Make.Now);

    private Project Project(bool openAmount = true, Money? min = null, params decimal[] options) => Make.Published(_category, openAmount, min, options);

    [Fact]
    [Rule("D-01")]
    public void Each_line_has_exactly_one_target()
    {
        var project = Project(true, null, 4m);

        var option = _cart.AddOptionLine(project, _category, project.Options[0].Id, 2, Make.Limits, Make.Now).Value;
        var amount = _cart.AddProjectAmount(project, _category, new Money(50m), Make.Limits, Make.Now).Value;
        var general = _cart.AddCategoryAmount(_category, new Money(20m), Make.Limits, Make.Now).Value;

        (option.TargetKind, option.ProjectId, option.CategoryId, option.OptionId).ShouldBe((TargetKind.Project, project.Id, null, project.Options[0].Id));
        (amount.TargetKind, amount.ProjectId, amount.CategoryId, amount.OptionId).ShouldBe((TargetKind.Project, project.Id, (Guid?)null, (Guid?)null));
        (general.TargetKind, general.ProjectId, general.CategoryId, general.OptionId).ShouldBe((TargetKind.Category, (Guid?)null, _category.Id, (Guid?)null));
        general.IsAmountLine.ShouldBeTrue();
    }

    [Fact]
    [Rule("D-02")]
    public void Amount_is_unit_amount_times_quantity_and_one_for_amount_lines()
    {
        var project = Project(true, null, 4m);

        var option = _cart.AddOptionLine(project, _category, project.Options[0].Id, 3, Make.Limits, Make.Now).Value;
        var amount = _cart.AddProjectAmount(project, _category, new Money(12.50m), Make.Limits, Make.Now).Value;

        option.Amount.ShouldBe(new Money(12.00m));
        option.UnitAmount.ShouldBe(new Money(4.00m));
        amount.Quantity.ShouldBe(1);
        amount.Amount.ShouldBe(new Money(12.50m));
        _cart.Total.ShouldBe(new Money(24.50m));
    }

    [Fact]
    [Rule("D-03")]
    public void Open_amounts_respect_the_minimum_and_the_line_maximum()
    {
        var project = Project();
        var strict = Project(true, new Money(100m));

        _cart.AddProjectAmount(project, _category, new Money(9.99m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartAmountBelowMinimum);
        _cart.AddProjectAmount(project, _category, new Money(10.00m), Make.Limits, Make.Now).IsSuccess.ShouldBeTrue();
        _cart.AddProjectAmount(strict, _category, new Money(99.99m), Make.Limits, Make.Now).Error.Extensions!["min"].ShouldBe("100.00");
        _cart.AddCategoryAmount(_category, new Money(100_000.01m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartAmountAboveMaximum);
        _cart.AddCategoryAmount(_category, new Money(5m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartAmountBelowMinimum);
    }

    [Theory]
    [Rule("D-03")]
    [InlineData(0)]
    [InlineData(1000)]
    public void Quantities_are_1_to_999(int quantity)
    {
        var project = Project(true, null, 1m);

        _cart.AddOptionLine(project, _category, project.Options[0].Id, quantity, Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
    }

    [Fact]
    [Rule("D-04")]
    public void Identical_option_lines_merge_and_amount_lines_never_do()
    {
        var project = Project(true, null, 4m);
        var optionId = project.Options[0].Id;

        var first = _cart.AddOptionLine(project, _category, optionId, 2, Make.Limits, Make.Now).Value;
        var second = _cart.AddOptionLine(project, _category, optionId, 5, Make.Limits, Make.Now).Value;
        _cart.AddProjectAmount(project, _category, new Money(50m), Make.Limits, Make.Now);
        _cart.AddProjectAmount(project, _category, new Money(50m), Make.Limits, Make.Now);

        second.ShouldBeSameAs(first);
        first.Quantity.ShouldBe(7);
        _cart.Lines.Count.ShouldBe(3);
        _cart.AddOptionLine(project, _category, optionId, 993, Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
    }

    [Fact]
    [Rule("D-04")]
    public void A_cart_holds_at_most_50_lines_and_100000()
    {
        var project = Project();
        for (var i = 0; i < 50; i++)
        {
            _cart.AddProjectAmount(project, _category, new Money(10m), Make.Limits, Make.Now).IsSuccess.ShouldBeTrue();
        }

        _cart.AddProjectAmount(project, _category, new Money(10m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTooManyLines);

        var other = Cart.CreateGuest(Make.Now);
        other.AddCategoryAmount(_category, new Money(99_990m), Make.Limits, Make.Now).IsSuccess.ShouldBeTrue();
        other.AddCategoryAmount(_category, new Money(10.01m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTotalExceeded);
        other.AddCategoryAmount(_category, new Money(10.00m), Make.Limits, Make.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Rule("D-05")]
    public void Only_published_projects_in_active_categories_and_open_categories_accept_lines()
    {
        var draft = Make.Draft(_category);
        var archived = Project();
        archived.Archive(Make.Now);
        var inactive = Make.Category(active: false);
        var inInactive = Make.Published(_category, true, null);
        var closed = Make.Category(generalGifts: false);

        _cart.AddProjectAmount(draft, _category, new Money(50m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTargetUnavailable);
        _cart.AddProjectAmount(archived, _category, new Money(50m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTargetUnavailable);
        _cart.AddCategoryAmount(inactive, new Money(50m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTargetUnavailable);
        _cart.AddCategoryAmount(closed, new Money(50m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTargetUnavailable);
        Should.Throw<DomainException>(() => _cart.AddProjectAmount(inInactive, inactive, new Money(50m), Make.Limits, Make.Now));
        _cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    [Rule("D-05")]
    public void Inactive_options_and_options_only_projects_refuse_the_wrong_line()
    {
        var project = Project(false, null, 25m, 100m);
        project.ReplaceOptions([new ProjectOptionInput(project.Options[0].Id, "خيار 1", null, new Money(25m), 1, IsActive: false),
            new ProjectOptionInput(project.Options[1].Id, "خيار 2", null, new Money(100m), 2, true)], new HashSet<Guid>(), Make.Now);

        _cart.AddOptionLine(project, _category, project.Options[0].Id, 1, Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartOptionUnavailable);
        _cart.AddOptionLine(project, _category, Guid.NewGuid(), 1, Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartOptionUnavailable);
        _cart.AddProjectAmount(project, _category, new Money(50m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartOpenAmountNotAllowed);
    }

    [Fact]
    [Rule("D-03")]
    public void Changing_a_line_keeps_the_limits()
    {
        var project = Project(true, null, 4m);
        var option = _cart.AddOptionLine(project, _category, project.Options[0].Id, 1, Make.Limits, Make.Now).Value;
        var amount = _cart.AddProjectAmount(project, _category, new Money(50m), Make.Limits, Make.Now).Value;

        _cart.ChangeQuantity(option.Id, 10, Make.Limits, Make.Now).IsSuccess.ShouldBeTrue();
        option.Amount.ShouldBe(new Money(40m));
        _cart.ChangeQuantity(option.Id, 0, Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
        _cart.ChangeQuantity(amount.Id, 2, Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartLineNotFound);
        _cart.ChangeAmount(amount.Id, new Money(75m), new Money(10m), Make.Limits, Make.Now).IsSuccess.ShouldBeTrue();
        amount.Amount.ShouldBe(new Money(75m));
        _cart.ChangeAmount(amount.Id, new Money(5m), new Money(10m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartAmountBelowMinimum);
        _cart.ChangeAmount(option.Id, new Money(75m), new Money(10m), Make.Limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartLineNotFound);
        _cart.ChangeQuantity(option.Id, 999, new CartLimits(new Money(10m), new Money(100m), 50, new Money(100m)), Make.Now)
            .Error.Code.ShouldBe(ErrorCodes.CartTotalExceeded);

        _cart.RemoveLine(amount.Id, Make.Now).IsSuccess.ShouldBeTrue();
        _cart.RemoveLine(amount.Id, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartLineNotFound);
        _cart.Clear(Make.Now);
        _cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    [Rule("D-14")]
    public void A_guest_cart_expires_30_days_after_its_last_change_and_a_donor_cart_never()
    {
        var guest = Cart.CreateGuest(Make.Now);
        guest.AddCategoryAmount(_category, new Money(10m), Make.Limits, Make.Now.AddDays(3));

        guest.ExpiresAt.ShouldBe(Make.Now.AddDays(33));
        Cart.CreateFor(Guid.NewGuid(), Make.Now).ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void Merging_moves_combines_and_drops_over_the_limits()
    {
        var project = Project(true, null, 4m);
        var optionId = project.Options[0].Id;
        var donor = Cart.CreateFor(Guid.NewGuid(), Make.Now);
        var existing = donor.AddOptionLine(project, _category, optionId, 1, Make.Limits, Make.Now).Value;
        donor.AddCategoryAmount(_category, new Money(99_000m), Make.Limits, Make.Now);
        var guest = Cart.CreateGuest(Make.Now);
        guest.AddOptionLine(project, _category, optionId, 2, Make.Limits, Make.Now);
        guest.AddProjectAmount(project, _category, new Money(100m), Make.Limits, Make.Now.AddSeconds(1));
        guest.AddCategoryAmount(_category, new Money(5_000m), Make.Limits, Make.Now.AddSeconds(2));

        var outcomes = donor.MergeFrom(guest, Make.Limits, Make.Now.AddMinutes(1));

        outcomes.Select(o => o.Kind).ShouldBe([CartMergeOutcome.Combined, CartMergeOutcome.Moved, CartMergeOutcome.Dropped]);
        existing.Quantity.ShouldBe(3);
        donor.Lines.Count.ShouldBe(3);
        donor.Lines.ShouldAllBe(l => l.CartId == donor.Id);
        guest.Lines.ShouldBeEmpty();
        Should.Throw<DomainException>(() => guest.MergeFrom(donor, Make.Limits, Make.Now));
    }

    [Fact]
    [Rule("D-04")]
    public void Adding_to_an_option_line_checks_the_total_with_the_line_at_its_new_price()
    {
        var limits = new CartLimits(new Money(10m), new Money(100_000m), 50, new Money(10_000m));
        var project = Project(true, null, 1000m);
        var option = project.Options[0];
        _cart.AddOptionLine(project, _category, option.Id, 5, limits, Make.Now).IsSuccess.ShouldBeTrue();
        project.ReplaceOptions([new ProjectOptionInput(option.Id, option.LabelAr, null, new Money(1900m), 1, true)], new HashSet<Guid>(), Make.Now);

        _cart.AddOptionLine(project, _category, option.Id, 1, limits, Make.Now).Error.Code.ShouldBe(ErrorCodes.CartTotalExceeded);   // 6 × 1900 = 11 400

        _cart.Total.ShouldBe(new Money(5000m));
    }

    [Fact]
    [Rule("D-04")]
    public void Merging_into_an_option_line_checks_the_total_at_the_donor_line_price()
    {
        var limits = new CartLimits(new Money(10m), new Money(100_000m), 50, new Money(10_000m));
        var project = Project(true, null, 1900m);
        var option = project.Options[0];
        var donor = Cart.CreateFor(Guid.NewGuid(), Make.Now);
        donor.AddOptionLine(project, _category, option.Id, 1, limits, Make.Now);
        project.ReplaceOptions([new ProjectOptionInput(option.Id, option.LabelAr, null, new Money(1000m), 1, true)], new HashSet<Guid>(), Make.Now);
        var guest = Cart.CreateGuest(Make.Now);
        guest.AddOptionLine(project, _category, option.Id, 5, limits, Make.Now);

        donor.MergeFrom(guest, limits, Make.Now.AddMinutes(1)).Single().Kind.ShouldBe(CartMergeOutcome.Dropped);   // 6 × 1900 = 11 400

        donor.Total.ShouldBe(new Money(1900m));
    }
}
