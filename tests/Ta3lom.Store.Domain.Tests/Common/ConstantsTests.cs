using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.ReadModels;

namespace Ta3lom.Store.Domain.Tests.Common;

/// Roles and policies (RUL-62), session limits (RUL-60), settings keys (03 §M) and fixed limits (RUL-12, RUL-18).
public sealed class ConstantsTests
{
    [Fact]
    [Rule("RUL-62")]
    public void There_are_six_staff_roles()
    {
        Roles.All.ShouldBe([Roles.Admin, Roles.StoreManager, Roles.Finance, Roles.AllocationOfficer, Roles.Fulfilment, Roles.Viewer]);
        Roles.IsValid("allocation_officer").ShouldBeTrue();
        Roles.IsValid("Admin").ShouldBeFalse();
        Roles.IsValid("root").ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-62")]
    public void Admin_does_not_implicitly_refund_allocate_or_reveal_beneficiary_pii()
    {
        Policies.StaffRoles[Policies.StaffRefund].ShouldBe([Roles.Finance]);
        Policies.StaffRoles[Policies.StaffAllocate].ShouldBe([Roles.AllocationOfficer]);
        Policies.StaffRoles[Policies.StaffRevealPii].ShouldBe([Roles.AllocationOfficer]);
        Policies.StaffRoles[Policies.StaffAdmin].ShouldBe([Roles.Admin]);
        Policies.StaffRoles[Policies.StaffRead].ShouldBe(Roles.All);
        Policies.StaffRoles.Keys.ShouldBe(
            [Policies.StaffRead, Policies.StaffCatalogue, Policies.StaffStock, Policies.StaffFulfilment, Policies.StaffFinance, Policies.StaffRefund,
             Policies.StaffBeneficiaries, Policies.StaffAllocate, Policies.StaffRevealPii, Policies.StaffResend, Policies.StaffAdmin],
            ignoreOrder: true);
        Policies.StaffRoles.Values.SelectMany(r => r).ShouldAllBe(r => Roles.IsValid(r));
        new[] { Policies.Customer, Policies.CustomerOptional, AuthSchemes.Customer, AuthSchemes.Staff, AuthSchemes.StaffMfa }.ShouldAllBe(s => s.Length > 0);
    }

    [Fact]
    [Rule("RUL-60")]
    public void Otp_limits_match_the_rule()
    {
        SessionPolicy.OtpLifetime.ShouldBe(TimeSpan.FromMinutes(5));
        SessionPolicy.OtpMaxPerMobile.ShouldBe(3);
        SessionPolicy.OtpMobileWindow.ShouldBe(TimeSpan.FromMinutes(15));
        SessionPolicy.OtpMaxPerIp.ShouldBe(10);
        SessionPolicy.OtpIpWindow.ShouldBe(TimeSpan.FromHours(1));
        SessionPolicy.OtpRetention.ShouldBe(TimeSpan.FromHours(24));
        SessionPolicy.CustomerAccessLifetime.ShouldBeLessThan(SessionPolicy.CustomerRefreshLifetime);
        SessionPolicy.StaffAccessLifetime.ShouldBeLessThan(SessionPolicy.StaffRefreshLifetime);
        SessionPolicy.StaffMfaLifetime.ShouldBe(TimeSpan.FromMinutes(5));
        SessionPolicy.RefreshRaceGrace.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Every_setting_key_of_the_admin_table_is_listed_once()
    {
        SettingKeys.All.Count.ShouldBe(17);
        SettingKeys.All.Distinct(StringComparer.Ordinal).Count().ShouldBe(17);
        SettingKeys.All.ShouldContain(SettingKeys.CartMaxTotal);
        SettingKeys.All.ShouldContain(SettingKeys.DedicationFreeTextEnabled);
    }

    [Fact]
    [Rule("RUL-18")]
    public void Fixed_limits_match_the_cart_rules()
    {
        CatalogLimits.CartMaxLines.ShouldBe(50);
        CartLimits.MaxLines.ShouldBe(CatalogLimits.CartMaxLines);
        CatalogLimits.PrintMaxQuantity.ShouldBe(20);
        CatalogLimits.GiftMaxQuantity.ShouldBe(999);
        CartLineRules.GiftMaxQuantity.ShouldBe(CatalogLimits.GiftMaxQuantity);
        CatalogLimits.CourseSponsorPresets.ShouldAllBe(q => q >= 1 && q <= 999);
        CatalogLimits.BookSponsorPresets.ShouldAllBe(q => q >= 1 && q <= 999);
        CatalogLimits.CauseUnitPresets.ShouldAllBe(q => q >= 1 && q <= 999);
        CatalogLimits.AmountPresets.ShouldBe(["50.00", "100.00", "250.00", "500.00"]);
        CatalogLimits.PaymentMethods.ShouldBe(["mada", "applepay", "card", "stcpay"]);
    }

    [Fact]
    public void Read_models_are_plain_records()
    {
        var id = Guid.CreateVersion7();

        new GoalView(id, "s", "cause", 100m, null, 10m, null, 1, null).RaisedAmount.ShouldBe(10m);
        new SalesDailyView(new DateOnly(2026, 9, 30), "buy", "course", 3, 1_050m, 136.95m, 0m).Orders.ShouldBe(3);
        new AllocationBacklogView(id, "s", 4, 1, 2, 3, null).Awaiting.ShouldBe(4);
        new ZakatBalanceView(id, 500m, 350m, 0m, 150m).Balance.ShouldBe(150m);
    }

    [Fact]
    public void Add_domain_registers_nothing_yet()
    {
        var services = new ServiceCollection();

        Ta3lom.Store.Domain.DependencyInjection.AddDomain(services).ShouldBeSameAs(services);
        services.ShouldBeEmpty();
    }
}
