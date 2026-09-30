using FluentValidation;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Application.Tests.Abstractions;

public sealed record RegisterMobile(string MobileNumber, AddressInput Address) : ICommand<Unit>;

public sealed record AddressInput(string CityName);

public sealed class RegisterMobileValidator : AbstractValidator<RegisterMobile>
{
    public RegisterMobileValidator()
    {
        RuleFor(c => c.MobileNumber).Matches("^05[0-9]{8}$");
        RuleFor(c => c.Address.CityName).NotEmpty();
    }
}

public sealed class RegisterMobileHandler : ICommandHandler<RegisterMobile, Unit>
{
    public int Calls { get; private set; }

    public Error? FailWith { get; set; }

    public Task<Result<Unit>> HandleAsync(RegisterMobile command, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(FailWith is null ? Result.Success(Unit.Value) : Result.Failure<Unit>(FailWith));
    }
}
