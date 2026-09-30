using FluentValidation;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;

/// Shape only; an invalid mobile is the business code `otp.mobile_invalid`, returned by the handler.
internal sealed class RequestOtpCommandValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpCommandValidator()
    {
        RuleFor(c => c.Mobile).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Locale).Must(l => l is null or "ar" or "en").WithMessage("locale must be 'ar' or 'en'.");
    }
}
