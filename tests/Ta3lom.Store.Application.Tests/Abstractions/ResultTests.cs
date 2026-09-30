using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Application.Tests.Abstractions;

public sealed class ResultTests
{
    [Fact]
    public void Success_and_implicit_value_are_success()
    {
        Result<int> fromValue = 42;

        fromValue.IsSuccess.ShouldBeTrue();
        fromValue.Value.ShouldBe(42);
        fromValue.IsFailure.ShouldBeFalse();
        fromValue.Error.ShouldBe(Error.None);
        Result.Success(7).Value.ShouldBe(7);
    }

    [Fact]
    public void Failure_and_implicit_error_carry_the_error()
    {
        var error = Error.Conflict("cart.campaign_ended", "Campaign ended");

        Result<int> fromError = error;

        fromError.IsSuccess.ShouldBeFalse();
        fromError.IsFailure.ShouldBeTrue();
        fromError.Error.ShouldBe(error);
        fromError.Error.Status.ShouldBe(409);
        Should.Throw<InvalidOperationException>(() => fromError.Value);
        Should.Throw<ArgumentNullException>(() => Result.Failure<int>(null!));
        Should.Throw<ArgumentException>(() => Result.Failure<int>(Error.None));
    }

    [Fact]
    public void Validation_error_has_the_standard_code_and_field_errors()
    {
        var error = Error.Validation(new Dictionary<string, string[]> { ["mobile"] = ["invalid"] });

        error.Code.ShouldBe(ErrorCodes.ValidationFailed);
        error.Status.ShouldBe(400);
        error.Errors["mobile"].ShouldBe(["invalid"]);
        error.Extensions!["errors"].ShouldBeAssignableTo<IReadOnlyDictionary<string, string[]>>();
    }
}
