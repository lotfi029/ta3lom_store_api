using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Common;

public sealed class GuardTests
{
    [Fact]
    public void NotEmpty_rejects_blank_strings_with_the_argument_name()
    {
        string title = "  ";

        var ex = Should.Throw<DomainException>(() => Guard.NotEmpty(title));

        ex.Code.ShouldBe("guard.empty");
        ex.Message.ShouldContain("title");
    }

    [Fact]
    public void InRange_accepts_bounds_and_rejects_outside()
    {
        Guard.InRange(1, 1, 20).ShouldBe(1);
        Guard.InRange(20, 1, 20).ShouldBe(20);
        Should.Throw<DomainException>(() => Guard.InRange(21, 1, 20)).Code.ShouldBe("guard.out_of_range");
    }

    [Fact]
    public void NotNegative_and_Against()
    {
        Guard.NotNegative(0m).ShouldBe(0m);
        Should.Throw<DomainException>(() => Guard.NotNegative(-0.01m)).Code.ShouldBe("guard.negative");
        Should.Throw<DomainException>(() => Guard.Against(true, "sample.code")).Code.ShouldBe("sample.code");
        Should.NotThrow(() => Guard.Against(false, "sample.code"));
    }

    [Fact]
    public void NotNull_returns_the_value()
    {
        var value = new object();

        Guard.NotNull(value).ShouldBeSameAs(value);
        Should.Throw<DomainException>(() => Guard.NotNull<object>(null)).Code.ShouldBe("guard.null");
    }
}
