using MapsterMapper;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.VerifyOtp;

/// RUL-60/61 (09 §5.7): verifies the newest open code (row lock), creates the customer on first login,
/// refuses a blocked customer, merges the guest cart (RUL-17 hook) and opens a session (09 §5.8).
public sealed record VerifyOtpCommand(string Mobile, string Code, Guid? GuestCartId, string? UserAgent) : ICommand<CustomerSession>;

internal sealed class VerifyOtpCommandHandler(
    IOtpChallengeRepository otps,
    ICustomerRepository customers,
    IRefreshTokenRepository refreshTokens,
    IOtpHasher hasher,
    IRefreshTokenCodec codec,
    IAccessTokenIssuer issuer,
    ICartMergeHook cartMerge,
    IMapper mapper,
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
        return await uow.ExecuteInTransactionAsync(
            async token =>
            {
                var now = clock.UtcNow;
                var challenge = await otps.LatestOpenForUpdateAsync(mobile, OtpPurpose.Login, token);
                if (challenge is null || !challenge.IsOpen(now))
                {
                    return Result.Failure<CustomerSession>(OtpErrors.Expired);
                }

                if (challenge.IsExhausted)
                {
                    return Result.Failure<CustomerSession>(OtpErrors.TooManyAttempts);
                }

                if (!hasher.Matches(challenge.Id, challenge.Mobile, challenge.CodeHash, command.Code))
                {
                    challenge.RecordFailure();
                    return Result.Failure<CustomerSession>(challenge.IsExhausted ? OtpErrors.TooManyAttempts : OtpErrors.Invalid(challenge.AttemptsLeft));
                }

                challenge.Consume(now);

                var customer = await customers.FindByMobileAsync(mobile, token);
                if (customer is { IsAnonymised: true })
                {
                    customer.ReleaseMobile(now);                                                // a deleted account never signs in again (RUL-61)
                    await uow.SaveChangesAsync(token);
                    customer = null;
                }

                var isNew = customer is null;
                if (customer is null)
                {
                    customer = Customer.RegisterOnFirstLogin(mobile, now);
                    customers.Add(customer);
                }

                if (customer.IsBlocked)
                {
                    return Result.Failure<CustomerSession>(AuthErrors.AccountBlocked);                // RUL-61
                }

                customer.RecordLogin(now);
                var notices = await cartMerge.MergeGuestCartAsync(command.GuestCartId, customer.Id, token);   // RUL-17

                var (raw, hash) = codec.Create();
                var expiresAt = now + SessionPolicy.CustomerRefreshLifetime;
                refreshTokens.Add(RefreshToken.StartFamily(TokenSubjectType.Customer, customer.Id, hash, SessionPolicy.CustomerFamilyLifetime, SessionPolicy.CustomerRefreshLifetime, command.UserAgent, now));

                var access = issuer.IssueCustomer(customer.Id);
                var profile = mapper.Map<CustomerSessionResponse>(customer) with { IsNew = isNew };
                var body = new SessionResponse(access.Token, access.ExpiresInSeconds, profile,
                    [.. notices.Select(n => new CartNoticeResponse(n.Code, n.LineId))]);
                return Result.Success(new CustomerSession(body, raw, expiresAt));
            },
            commitOnFailure: true,
            ct);
    }
}
