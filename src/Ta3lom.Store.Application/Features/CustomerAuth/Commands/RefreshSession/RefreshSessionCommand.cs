using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.RefreshSession;

/// Refresh rotation with reuse detection (09 §5.8, RUL-60). Reuse of a rotated token revokes the whole family
/// (the revocation commits although the result is a failure).
public sealed record RefreshSessionCommand(string? RefreshToken, string? UserAgent) : ICommand<RotatedSession>;

internal sealed partial class RefreshSessionCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ICustomerRepository customers,
    IRefreshTokenCodec codec,
    IAccessTokenIssuer issuer,
    IUnitOfWork uow,
    IClock clock,
    ILogger<RefreshSessionCommandHandler> logger) : ICommandHandler<RefreshSessionCommand, RotatedSession>
{
    public async Task<Result<RotatedSession>> HandleAsync(RefreshSessionCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return AuthErrors.RefreshInvalid;
        }

        var hash = codec.Hash(command.RefreshToken);
        return await uow.ExecuteInTransactionAsync(
            async token =>
            {
                var now = clock.UtcNow;
                var current = await refreshTokens.FindByHashForUpdateAsync(hash, token);
                if (current is null || current.SubjectType != TokenSubjectType.Customer)
                {
                    return Result.Failure<RotatedSession>(AuthErrors.RefreshInvalid);
                }

                if (current.RevokedAt is { } revokedAt)
                {
                    if (current.ReplacedBy is not null && now - revokedAt < SessionPolicy.RefreshRaceGrace)
                    {
                        return Result.Failure<RotatedSession>(AuthErrors.RefreshRace);
                    }

                    // A rotated token came back: theft signal. Revoke the whole family.
                    await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, token);
                    LogReuse(logger, current.FamilyId);
                    return Result.Failure<RotatedSession>(AuthErrors.RefreshReused);
                }

                if (current.ExpiresAt <= now)
                {
                    return Result.Failure<RotatedSession>(AuthErrors.RefreshExpired);
                }

                var customer = await customers.GetByIdAsync(current.SubjectId, token);
                if (customer is null || customer.Status != CustomerStatus.Active)
                {
                    await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, token);
                    return Result.Failure<RotatedSession>(customer?.IsBlocked == true ? AuthErrors.AccountBlocked : AuthErrors.RefreshInvalid);
                }

                var (raw, nextHash) = codec.Create();
                var expiresAt = now + SessionPolicy.CustomerRefreshLifetime;
                var next = RefreshToken.Issue(TokenSubjectType.Customer, customer.Id, nextHash, current.FamilyId, expiresAt, command.UserAgent, now);
                refreshTokens.Add(next);
                current.Revoke(now, next.Id);

                var access = issuer.IssueCustomer(customer.Id);
                return Result.Success(new RotatedSession(new AccessTokenResponse(access.Token, access.ExpiresInSeconds), raw, expiresAt));
            },
            commitOnFailure: true,
            ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected; family {FamilyId} revoked")]
    private static partial void LogReuse(ILogger logger, Guid familyId);
}
