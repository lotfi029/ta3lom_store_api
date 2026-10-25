using Ta3lom.Store.Application.Features.StaffAuth;

namespace Ta3lom.Store.Application.Tests.StaffAuth;

/// D-12 / L-8: the common-password list is a public top list (≥ 10,000 entries of 12+ characters), checked case-insensitively.
public sealed class PasswordPolicyTests
{
    [Fact]
    public void The_list_holds_at_least_ten_thousand_long_common_passwords() =>
        PasswordPolicy.CommonPasswordCount.ShouldBeGreaterThanOrEqualTo(10_000);

    [Theory]
    [InlineData("1qaz2wsx3edc")]
    [InlineData("1QAZ2WSX3EDC")]
    [InlineData("QwertyQwerty")]
    [InlineData("  123qweasdzxc  ")]
    [InlineData("aaaaaaaaaaaaaa")]
    [Rule("D-12")]
    public void Common_passwords_are_refused_in_any_case(string password) =>
        PasswordPolicy.Check(password).ShouldContain("common");

    [Fact]
    [Rule("D-12")]
    public void A_long_uncommon_password_passes_and_a_short_one_does_not()
    {
        PasswordPolicy.Check("Ta3lom-Store-Passphrase-2026!").ShouldBeEmpty();
        PasswordPolicy.Check("Short-1").ShouldContain("too_short");
    }
}
