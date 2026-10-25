using FluentValidation;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.VerifyOtp;

/// BR-A09 / D-10: verifies the newest open code (row lock), creates the donor on the first sign-in with the PDPL consent
/// (D-14), refuses a blocked donor (D-13), merges the guest cart (BR-A07) and starts a refresh-token family (D-11).
public sealed record VerifyOtpCommand(string Mobile, string Code, Guid? GuestCartId, string? UserAgent) : ICommand<CustomerSession>;

internal sealed class VerifyOtpCommandHandler(
    IOtpChallengeRepository otps,
    ICustomerRepository customers,
    IRefreshTokenRepository refreshTokens,
    IOtpHasher hasher,
    IRefreshTokenCodec codec,
    IAccessTokenIssuer issuer,
    ICartMergeHook cartMerge,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<VerifyOtpCommand, CustomerSession>
{
    public async Task<Result<CustomerSession>> HandleAsync(VerifyOtpCommand command, CancellationToken ct = default)
    {
        if (!SaudiMobile.TryNormalize(command.Mobile, out var mobile))
        {
            return OtpErrors.MobileInvalid;
        }

        // A wrong code must still persist attempts + 1, so this unit of work commits on failure.
        return await uow.ExecuteInTransactionAsync(token => VerifyAsync(command, mobile, token), commitOnFailure: true, ct);
    }

    private async Task<Result<CustomerSession>> VerifyAsync(VerifyOtpCommand command, string mobile, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var challenge = await otps.LatestOpenForUpdateAsync(mobile, OtpPurpose.Login, ct);
        if (challenge is null || !challenge.IsOpen(now))
        {
            return OtpErrors.Expired;
        }

        if (challenge.IsExhausted)
        {
            return OtpErrors.TooManyAttempts;
        }

        if (!hasher.Matches(challenge.Id, challenge.Mobile, challenge.CodeHash, command.Code))
        {
            challenge.RecordFailure();
            return challenge.IsExhausted ? OtpErrors.TooManyAttempts : OtpErrors.Invalid(challenge.AttemptsLeft);
        }

        challenge.Consume(now);
        var customer = await customers.FindByMobileAsync(mobile, ct);         // an anonymised donor has no mobile (L-5)

        var isNew = customer is null;
        if (customer is null)
        {
            customer = Customer.RegisterOnFirstLogin(mobile, now);
            customers.Add(customer);
        }

        if (!customer.IsActive)
        {
            return AuthErrors.AccountBlocked;                    // D-13; the consumed code still commits
        }

        customer.RecordLogin(now);
        await uow.SaveChangesAsync(ct);                          // the donor row exists before the cart merge points at it
        await customers.LockForUpdateAsync(customer.Id, ct);     // R12-3: customer → cart before the merge touches either cart
        var notices = await cartMerge.MergeGuestCartAsync(command.GuestCartId, customer.Id, ct);
        var (raw, hash) = codec.Create();
        var refresh = RefreshToken.StartFamily(TokenSubjectType.Customer, customer.Id, hash,
            SessionPolicy.CustomerFamilyLifetime, SessionPolicy.CustomerRefreshLifetime, command.UserAgent, now);
        refreshTokens.Add(refresh);
        var access = issuer.IssueCustomer(customer.Id);
        var profile = new CustomerSessionResponse(customer.Id, customer.Mobile!, customer.FullName, customer.Email, customer.Locale, isNew,
            !string.IsNullOrWhiteSpace(customer.FullName) && !string.IsNullOrWhiteSpace(customer.Email));
        var body = new SessionResponse(access.Token, access.ExpiresInSeconds, profile, [.. notices.Select(n => new CartNoticeResponse(n.Code, n.LineId))]);
        return new CustomerSession(body, raw, refresh.ExpiresAt);
    }
}

internal sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(c => c.Mobile).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("code must be 6 digits.");
    }
}
