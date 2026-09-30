using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Domain.Tests.ValueObjects;

/// Saudi mobile numbers (RUL-60, RUL-21): accepted in four shapes, stored as E.164, shown masked.
public sealed class SaudiMobileTests
{
    [Theory]
    [Rule("RUL-60")]
    [InlineData("0551234567")]
    [InlineData("551234567")]
    [InlineData("966551234567")]
    [InlineData("+966551234567")]
    [InlineData("00966551234567")]
    [InlineData(" 055 123-4567 ")]
    [InlineData("+966 55 123 4567")]
    public void Accepted_shapes_normalise_to_e164(string input)
    {
        SaudiMobile.TryNormalize(input, out var e164).ShouldBeTrue();

        e164.ShouldBe("+966551234567");
        SaudiMobile.IsValid(input).ShouldBeTrue();
    }

    [Theory]
    [Rule("RUL-60")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0451234567")]
    [InlineData("05512345")]
    [InlineData("055123456789")]
    [InlineData("05512345a7")]
    [InlineData("055+1234567")]
    [InlineData("966451234567")]
    [InlineData("12345678901234567")]
    [InlineData("٠٥٥١٢٣٤٥٦٧")]
    public void Anything_else_is_rejected(string? input)
    {
        SaudiMobile.TryNormalize(input, out var e164).ShouldBeFalse();

        e164.ShouldBeEmpty();
        SaudiMobile.IsValid(input).ShouldBeFalse();
    }

    [Theory]
    [InlineData("+966551234567", "05•••••567")]
    [InlineData("0559876543", "05•••••543")]
    [InlineData(null, "•••")]
    [InlineData("not a mobile", "•••")]
    public void Mask_shows_the_prefix_and_the_last_three_digits(string? input, string expected) =>
        SaudiMobile.Mask(input).ShouldBe(expected);

    [Fact]
    public void To_local_turns_e164_into_the_05_form_and_leaves_other_text_alone()
    {
        SaudiMobile.ToLocal("+966551234567").ShouldBe("0551234567");
        SaudiMobile.ToLocal("deleted").ShouldBe("deleted");
    }
}
