using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Catalog;

/// Course, book and cause details (RUL-04, RUL-05, RUL-07, RUL-09, RUL-10, RUL-42).
public sealed class CatalogDetailTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProductId = Guid.CreateVersion7();

    [Fact]
    public void A_new_course_detail_has_one_price_and_no_content_yet()
    {
        var c = new CourseDetail(ProductId, new Money(350m), "د. سارة");

        c.ProductId.ShouldBe(ProductId);
        c.Price.ShouldBe(new Money(350m));
        c.CompareAtPrice.ShouldBeNull();
        c.Lessons.ShouldBe(0);
        c.DurationMinutes.ShouldBe(0);
        c.InstructorAr.ShouldBe("د. سارة");
        c.LearnerCount.ShouldBe(0);
        c.AccessDays.ShouldBeNull();
        Should.Throw<DomainException>(() => new CourseDetail(Guid.Empty, Money.Zero, "x")).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-04")]
    public void A_course_compare_at_price_must_be_higher_than_the_price()
    {
        var c = new CourseDetail(ProductId, new Money(350m), "د. سارة");

        Should.Throw<DomainException>(() => c.Update(new Money(350m), new Money(350m), 1, 1, null, null, "x", null, null, null, 0))
            .Code.ShouldBe("course.compare_at_not_higher");
        Should.Throw<DomainException>(() => c.Update(new Money(350m), new Money(300m), 1, 1, null, null, "x", null, null, null, 0))
            .Code.ShouldBe("course.compare_at_not_higher");

        c.Update(new Money(350m), new Money(450m), 1, 1, null, null, "x", null, null, null, 0);
        c.CompareAtPrice.ShouldBe(new Money(450m));
    }

    [Fact]
    [Rule("RUL-42")]
    public void Course_update_sets_the_content_and_access_days_and_floors_counters_at_zero()
    {
        var c = new CourseDetail(ProductId, new Money(350m), "د. سارة");

        c.Update(new Money(299m), null, -3, -10, "مبتدئ", "Beginner", " د. خالد ", "Dr Khalid", 90, "lms-42", -1);

        c.Price.ShouldBe(new Money(299m));
        c.CompareAtPrice.ShouldBeNull();
        c.Lessons.ShouldBe(0);
        c.DurationMinutes.ShouldBe(0);
        c.LevelAr.ShouldBe("مبتدئ");
        c.LevelEn.ShouldBe("Beginner");
        c.InstructorAr.ShouldBe(" د. خالد ");
        c.InstructorEn.ShouldBe("Dr Khalid");
        c.AccessDays.ShouldBe(90);
        c.LmsCourseRef.ShouldBe("lms-42");
        c.LearnerCount.ShouldBe(0);

        c.Update(new Money(299m), null, 12, 340, null, null, "د. خالد", null, null, null, 1_250);
        c.Lessons.ShouldBe(12);
        c.DurationMinutes.ShouldBe(340);
        c.AccessDays.ShouldBeNull();
        c.LearnerCount.ShouldBe(1_250);
        Should.Throw<DomainException>(() => c.Update(new Money(1m), null, 0, 0, null, null, " ", null, null, null, 0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void Course_modules_keep_order_title_and_non_negative_duration()
    {
        var m = CourseModule.Create(ProductId, 2, "المقدمة", "Introduction", -5);
        var plain = new CourseModule(ProductId, 1, "الوحدة الأولى");

        m.ProductId.ShouldBe(ProductId);
        m.Sort.ShouldBe(2);
        m.TitleAr.ShouldBe("المقدمة");
        m.TitleEn.ShouldBe("Introduction");
        m.DurationSeconds.ShouldBe(0);
        CourseModule.Create(ProductId, 3, "ب", null, 600).DurationSeconds.ShouldBe(600);
        plain.TitleEn.ShouldBeNull();
        plain.DurationSeconds.ShouldBe(0);
        Should.Throw<DomainException>(() => new CourseModule(Guid.Empty, 1, "x")).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void Book_detail_needs_an_arabic_author()
    {
        var b = new BookDetail(ProductId, "ابن هشام");

        b.ProductId.ShouldBe(ProductId);
        b.AuthorAr.ShouldBe("ابن هشام");
        b.AuthorEn.ShouldBeNull();

        b.Update("ابن مالك", "Ibn Malik", 320, "978-0-00-000000-0", 2024);

        b.AuthorAr.ShouldBe("ابن مالك");
        b.AuthorEn.ShouldBe("Ibn Malik");
        b.Pages.ShouldBe(320);
        b.Isbn.ShouldBe("978-0-00-000000-0");
        b.PublishedYear.ShouldBe(2024);
        Should.Throw<DomainException>(() => b.Update(" ", null, null, null, null)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => new BookDetail(ProductId, null!)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-05")]
    public void Only_print_formats_have_stock_and_a_new_one_starts_at_zero()
    {
        var print = BookFormat.Create(ProductId, BookFormatKind.Print, "SKU-P", new Money(80m));
        var digital = BookFormat.Create(ProductId, BookFormatKind.Digital, "SKU-D", new Money(40m));

        print.StockOnHand.ShouldBe(0);
        print.StockReserved.ShouldBe(0);
        print.Available.ShouldBe(0);
        print.IsActive.ShouldBeTrue();
        print.Kind.ShouldBe(BookFormatKind.Print);
        print.Sku.ShouldBe("SKU-P");
        print.Price.ShouldBe(new Money(80m));
        print.ProductId.ShouldBe(ProductId);
        digital.StockOnHand.ShouldBeNull();
        digital.Available.ShouldBe(0);
        new BookFormat(ProductId, BookFormatKind.Print, "SKU", Money.Zero).StockOnHand.ShouldBeNull();
        Should.Throw<DomainException>(() => new BookFormat(Guid.Empty, BookFormatKind.Print, "SKU", Money.Zero)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-05")]
    public void Print_weight_and_threshold_apply_only_to_print_and_the_file_only_to_digital()
    {
        var print = BookFormat.Create(ProductId, BookFormatKind.Print, "SKU-P", new Money(80m));
        var digital = BookFormat.Create(ProductId, BookFormatKind.Digital, "SKU-D", new Money(40m));
        var file = Guid.CreateVersion7();

        print.Update("SKU-P2", new Money(90m), new Money(120m), false, 450, 10, file);
        digital.Update("SKU-D2", new Money(45m), null, true, 450, 10, file);

        print.Sku.ShouldBe("SKU-P2");
        print.Price.ShouldBe(new Money(90m));
        print.CompareAtPrice.ShouldBe(new Money(120m));
        print.IsActive.ShouldBeFalse();
        print.WeightGrams.ShouldBe(450);
        print.LowStockThreshold.ShouldBe(10);
        print.FileAssetId.ShouldBeNull();
        digital.WeightGrams.ShouldBeNull();
        digital.LowStockThreshold.ShouldBeNull();
        digital.FileAssetId.ShouldBe(file);
    }

    [Fact]
    [Rule("RUL-05")]
    public void Format_update_guards()
    {
        var print = BookFormat.Create(ProductId, BookFormatKind.Print, "SKU-P", new Money(80m));
        var digital = BookFormat.Create(ProductId, BookFormatKind.Digital, "SKU-D", new Money(40m));

        Should.Throw<DomainException>(() => print.Update("S", new Money(80m), new Money(80m), true, null, null, null)).Code.ShouldBe("format.compare_at_not_higher");
        Should.Throw<DomainException>(() => digital.Update("S", new Money(40m), null, true, null, null, null)).Code.ShouldBe("format.digital_file_required");
        Should.Throw<DomainException>(() => print.Update(" ", new Money(80m), null, true, null, null, null)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-07")]
    public void A_new_cause_accepts_custom_amounts_from_ten_riyals()
    {
        var c = new CauseDetail(ProductId, 50_000m, T0);

        c.ProductId.ShouldBe(ProductId);
        c.TargetAmount.ShouldBe(50_000m);
        c.AllowCustomAmount.ShouldBeTrue();
        c.MinAmount.ShouldBe(new Money(10m));
        c.ZakatEligible.ShouldBeFalse();
        c.CloseOnTarget.ShouldBeFalse();
        c.IsGeneralFund.ShouldBeFalse();
        c.UnitAmount.ShouldBeNull();
        c.StartsAt.ShouldBe(T0);
        c.EndsAt.ShouldBeNull();
        Should.Throw<DomainException>(() => new CauseDetail(Guid.Empty, 1m, T0)).Code.ShouldBe("guard.empty");
    }

    private static void UpdateCause(CauseDetail c, Money? unit = null, string? labelAr = null, decimal target = 50_000m, bool custom = true,
        bool closeOnTarget = false, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null) =>
        c.Update(unit, labelAr, "cooler", target, custom, new Money(20m), true, "الطلاب", "Students", closeOnTarget, false, startsAt ?? T0, endsAt);

    [Fact]
    [Rule("RUL-07")]
    public void A_cause_unit_amount_needs_a_label_and_without_units_custom_amounts_are_required()
    {
        var c = new CauseDetail(ProductId, 50_000m, T0);

        Should.Throw<DomainException>(() => UpdateCause(c, new Money(1_500m), labelAr: " ")).Code.ShouldBe("cause.unit_label_required");
        Should.Throw<DomainException>(() => UpdateCause(c, custom: false)).Code.ShouldBe("cause.mode_required");
        Should.Throw<DomainException>(() => UpdateCause(c, target: 0m)).Code.ShouldBe("cause.target_invalid");
        Should.Throw<DomainException>(() => UpdateCause(c, endsAt: T0)).Code.ShouldBe("cause.dates_invalid");
    }

    [Fact]
    [Rule("RUL-07")]
    public void Cause_update_sets_every_field_and_drops_labels_without_a_unit_amount()
    {
        var c = new CauseDetail(ProductId, 50_000m, T0);

        UpdateCause(c, new Money(1_500m), "مبرد", target: 60_000m, custom: false, closeOnTarget: true, endsAt: T0.AddDays(30));

        c.UnitAmount.ShouldBe(new Money(1_500m));
        c.UnitLabelAr.ShouldBe("مبرد");
        c.UnitLabelEn.ShouldBe("cooler");
        c.TargetAmount.ShouldBe(60_000m);
        c.AllowCustomAmount.ShouldBeFalse();
        c.MinAmount.ShouldBe(new Money(20m));
        c.ZakatEligible.ShouldBeTrue();
        c.BeneficiariesAr.ShouldBe("الطلاب");
        c.BeneficiariesEn.ShouldBe("Students");
        c.CloseOnTarget.ShouldBeTrue();
        c.IsGeneralFund.ShouldBeFalse();
        c.EndsAt.ShouldBe(T0.AddDays(30));

        UpdateCause(c, null, "مبرد");
        c.UnitLabelAr.ShouldBeNull();
        c.UnitLabelEn.ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-09")]
    public void A_cause_is_open_from_its_start_until_its_end()
    {
        var c = new CauseDetail(ProductId, 50_000m, T0);
        UpdateCause(c, startsAt: T0, endsAt: T0.AddDays(30));

        c.IsOpen(T0.AddTicks(-1), 0m).ShouldBeFalse();
        c.IsOpen(T0, 0m).ShouldBeTrue();
        c.IsOpen(T0.AddDays(30).AddTicks(-1), 0m).ShouldBeTrue();
        c.IsOpen(T0.AddDays(30), 0m).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-10")]
    public void Reaching_the_target_closes_a_cause_only_with_close_on_target()
    {
        var open = new CauseDetail(ProductId, 50_000m, T0);
        var closing = new CauseDetail(ProductId, 50_000m, T0);
        UpdateCause(closing, closeOnTarget: true);

        open.IsOpen(T0.AddYears(3), 80_000m).ShouldBeTrue();
        closing.IsOpen(T0, 49_999.99m).ShouldBeTrue();
        closing.IsOpen(T0, 50_000m).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-08")]
    public void Goal_progress_starts_at_zero_and_takes_non_negative_seeds()
    {
        var g = new GoalProgress(ProductId, T0);

        g.ProductId.ShouldBe(ProductId);
        g.RaisedAmount.ShouldBe(0m);
        g.FundedUnits.ShouldBe(0);
        g.DonorCount.ShouldBe(0);
        g.ZakatAmount.ShouldBe(0m);
        g.LastGiftAt.ShouldBeNull();
        g.UpdatedAt.ShouldBe(T0);

        g.SetSeed(12_000m, 4, 30, T0.AddDays(1));
        g.SeedRaised.ShouldBe(12_000m);
        g.SeedUnits.ShouldBe(4);
        g.SeedDonors.ShouldBe(30);
        g.UpdatedAt.ShouldBe(T0.AddDays(1));

        Should.Throw<DomainException>(() => g.SetSeed(-1m, 0, 0, T0)).Code.ShouldBe("goal.seed_negative");
        Should.Throw<DomainException>(() => g.SetSeed(0m, -1, 0, T0)).Code.ShouldBe("goal.seed_negative");
        Should.Throw<DomainException>(() => g.SetSeed(0m, 0, -1, T0)).Code.ShouldBe("goal.seed_negative");
        Should.Throw<DomainException>(() => new GoalProgress(Guid.Empty, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-46")]
    public void A_pool_sweep_is_a_signed_pair_between_the_offer_product_and_the_general_fund()
    {
        var offer = Guid.CreateVersion7();
        var pair = Guid.CreateVersion7();
        var fund = Guid.CreateVersion7();

        var o = GoalAdjustment.PoolSweepOut(ProductId, new Money(120m), offer, pair, "system", T0);
        var i = GoalAdjustment.PoolSweepIn(fund, new Money(120m), offer, pair, "system", T0);

        o.Kind.ShouldBe(GoalAdjustmentKind.PoolSweepOut);
        o.ProductId.ShouldBe(ProductId);
        o.Amount.ShouldBe(new Money(-120m));
        o.Units.ShouldBe(0);
        o.SponsorshipOfferId.ShouldBe(offer);
        o.PairId.ShouldBe(pair);
        o.At.ShouldBe(T0);
        o.Actor.ShouldBe("system");
        i.Kind.ShouldBe(GoalAdjustmentKind.PoolSweepIn);
        i.ProductId.ShouldBe(fund);
        i.Amount.ShouldBe(new Money(120m));
        i.PairId.ShouldBe(pair);
        (o.Amount + i.Amount).ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_finance_correction_carries_units_and_a_note()
    {
        var c = GoalAdjustment.Correction(ProductId, new Money(-50m), -1, "duplicate import", "finance@ta3lom.sa", T0);

        c.Kind.ShouldBe(GoalAdjustmentKind.Correction);
        c.Amount.ShouldBe(new Money(-50m));
        c.Units.ShouldBe(-1);
        c.Note.ShouldBe("duplicate import");
        c.SponsorshipOfferId.ShouldBeNull();
        c.PairId.ShouldBeNull();
        Should.Throw<DomainException>(() => GoalAdjustment.Correction(ProductId, Money.Zero, 0, null!, "a", T0)).Code.ShouldBe("guard.null");
        Should.Throw<DomainException>(() => new GoalAdjustment(ProductId, GoalAdjustmentKind.Correction, Money.Zero, null!, T0)).Code.ShouldBe("guard.null");
    }
}
