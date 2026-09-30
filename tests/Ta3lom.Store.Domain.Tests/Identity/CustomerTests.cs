using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Identity;

/// Customer accounts (RUL-25, RUL-61).
public sealed class CustomerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);

    [Fact]
    [Rule("RUL-61")]
    public void First_login_registers_an_active_arabic_customer()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);

        c.Mobile.ShouldBe("+966551234567");
        c.Status.ShouldBe(CustomerStatus.Active);
        c.Locale.ShouldBe("ar");
        c.AnonymousDefault.ShouldBeFalse();
        c.MarketingOptIn.ShouldBeFalse();
        c.FullName.ShouldBeNull();
        c.Email.ShouldBeNull();
        c.PdplConsentAt.ShouldBeNull();
        c.LastLoginAt.ShouldBeNull();
        c.CreatedAt.ShouldBe(T0);
        c.UpdatedAt.ShouldBe(T0);
        c.IsBlocked.ShouldBeFalse();
        c.IsAnonymised.ShouldBeFalse();
        Should.Throw<DomainException>(() => new Customer(null!, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void Login_is_recorded_only_for_active_customers()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);

        c.RecordLogin(T1);
        c.LastLoginAt.ShouldBe(T1);
        c.UpdatedAt.ShouldBe(T1);

        c.Block(T1);
        Should.Throw<DomainException>(() => c.RecordLogin(T1.AddMinutes(1))).Code.ShouldBe("customer.not_active");
    }

    [Fact]
    [Rule("RUL-25")]
    public void The_profile_is_complete_with_a_name_and_an_email()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);
        c.ProfileComplete.ShouldBeFalse();

        c.UpdateProfile("  نورة العتيبي ", null, null, null, null, T1);
        c.ProfileComplete.ShouldBeFalse();

        c.UpdateProfile(null, " noura@example.com ", null, null, null, T1);
        c.FullName.ShouldBe("نورة العتيبي");
        c.Email.ShouldBe("noura@example.com");
        c.ProfileComplete.ShouldBeTrue();

        c.UpdateProfile("   ", null, null, null, null, T1);
        c.ProfileComplete.ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-61")]
    public void Profile_update_changes_only_what_is_given()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);

        c.UpdateProfile(null, null, "en", true, true, T1);
        c.Locale.ShouldBe("en");
        c.AnonymousDefault.ShouldBeTrue();
        c.MarketingOptIn.ShouldBeTrue();
        c.UpdatedAt.ShouldBe(T1);

        c.UpdateProfile(null, null, null, null, null, T1.AddHours(1));
        c.Locale.ShouldBe("en");
        c.AnonymousDefault.ShouldBeTrue();
        c.MarketingOptIn.ShouldBeTrue();

        c.UpdateProfile(null, null, "ar", false, null, T1);
        c.Locale.ShouldBe("ar");
        c.AnonymousDefault.ShouldBeFalse();
        c.MarketingOptIn.ShouldBeTrue();
    }

    [Fact]
    public void Only_arabic_and_english_locales_are_accepted()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);

        Should.Throw<DomainException>(() => c.UpdateProfile(null, null, "fr", null, null, T1)).Code.ShouldBe("customer.locale_invalid");
        c.Locale.ShouldBe("ar");
    }

    [Fact]
    public void Pdpl_consent_keeps_the_first_time()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);

        c.RecordPdplConsent(T1);
        c.RecordPdplConsent(T1.AddDays(1));

        c.PdplConsentAt.ShouldBe(T1);
        c.UpdatedAt.ShouldBe(T1.AddDays(1));
    }

    [Fact]
    [Rule("RUL-61")]
    public void Staff_block_and_unblock()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);

        c.Block(T1);
        c.IsBlocked.ShouldBeTrue();
        c.Status.ShouldBe(CustomerStatus.Blocked);
        c.UpdatedAt.ShouldBe(T1);

        c.Unblock(T1.AddHours(1));
        c.IsBlocked.ShouldBeFalse();
        c.Status.ShouldBe(CustomerStatus.Active);
    }

    [Fact]
    [Rule("RUL-61")]
    public void Anonymising_clears_personal_fields_and_frees_the_mobile()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);
        c.UpdateProfile("نورة", "n@example.com", "en", true, true, T0);

        c.Anonymise(T1);

        c.IsAnonymised.ShouldBeTrue();
        c.Status.ShouldBe(CustomerStatus.Anonymised);
        c.FullName.ShouldBeNull();
        c.Email.ShouldBeNull();
        c.MarketingOptIn.ShouldBeFalse();
        c.AnonymousDefault.ShouldBeFalse();
        c.Mobile.ShouldNotBe("+966551234567");
        c.Mobile.ShouldStartWith("+9665");
        c.Mobile.Length.ShouldBe(13);
        SaudiMobile.IsValid(c.Mobile).ShouldBeTrue();
        c.UpdatedAt.ShouldBe(T1);
    }

    [Fact]
    public void An_anonymised_account_cannot_be_blocked_or_unblocked()
    {
        var c = Customer.RegisterOnFirstLogin("+966551234567", T0);
        c.Anonymise(T1);

        Should.Throw<DomainException>(() => c.Block(T1)).Code.ShouldBe("customer.anonymised");
        Should.Throw<DomainException>(() => c.Unblock(T1)).Code.ShouldBe("customer.anonymised");
        Should.Throw<DomainException>(() => c.RecordLogin(T1)).Code.ShouldBe("customer.not_active");
    }

    [Fact]
    public void Only_an_anonymised_account_releases_its_placeholder_mobile()
    {
        var active = Customer.RegisterOnFirstLogin("+966551234567", T0);
        Should.Throw<DomainException>(() => active.ReleaseMobile(T1)).Code.ShouldBe("customer.not_anonymised");

        var anonymised = Customer.RegisterOnFirstLogin("+966551234568", T0);
        anonymised.Anonymise(T0);
        var placeholder = anonymised.Mobile;

        anonymised.ReleaseMobile(T1);

        anonymised.Mobile.ShouldNotBe(placeholder);                      // 1 in 10^8 chance of the same random number
        SaudiMobile.IsValid(anonymised.Mobile).ShouldBeTrue();
        anonymised.UpdatedAt.ShouldBe(T1);
    }
}
