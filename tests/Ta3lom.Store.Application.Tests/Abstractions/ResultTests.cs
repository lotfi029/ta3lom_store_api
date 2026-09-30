using Ta3lom.Store.Application.Abstractions;

namespace Ta3lom.Store.Application.Tests.Abstractions;

public sealed class ResultTests
{
    [Fact]
    public void Ok_and_implicit_value_are_success()
    {
        Result<int> fromValue = 42;

        fromValue.IsSuccess.ShouldBeTrue();
        fromValue.Value.ShouldBe(42);
        fromValue.Error.ShouldBeNull();
        Result<int>.Ok(7).Value.ShouldBe(7);
    }

    [Fact]
    public void Fail_and_implicit_error_carry_the_error()
    {
        var error = new Error("cart.campaign_ended", "Campaign ended", ErrorKind.Conflict);

        Result<int> fromError = error;

        fromError.IsSuccess.ShouldBeFalse();
        fromError.Error.ShouldBe(error);
        Should.Throw<ArgumentNullException>(() => Result<int>.Fail(null!));
    }

    [Fact]
    public void Validation_error_has_the_standard_code_and_field_errors()
    {
        var error = Error.Validation(new Dictionary<string, string[]> { ["mobile"] = ["invalid"] });

        error.Code.ShouldBe(ErrorCodes.ValidationFailed);
        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Extensions!["errors"].ShouldBeAssignableTo<IReadOnlyDictionary<string, string[]>>();
    }
}
