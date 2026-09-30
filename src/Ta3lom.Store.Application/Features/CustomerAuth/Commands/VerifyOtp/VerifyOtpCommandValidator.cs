using FluentValidation;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.VerifyOtp;

internal sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(c => c.Mobile).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("code must be 6 digits.");
    }
}
