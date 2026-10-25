using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.RefreshSession;

/// D-11: refresh rotation inside the family's absolute lifetime. Reuse of a rotated token revokes the whole family (the
/// revocation commits although the result is a failure) and is audited; a parallel refresh of the same token within the
/// grace window gets 409 instead. A blocked or anonymised donor loses the family (D-13).
public sealed record RefreshSessionCommand(string? RefreshToken, string? UserAgent) : ICommand<RotatedSession>;

internal sealed partial class RefreshSessionCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ICustomerRepository customers,
    IRefreshTokenCodec codec,
    IAccessTokenIssuer issuer,
    IAuditTrail audit,
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
        return await uow.ExecuteInTransactionAsync(token => RotateAsync(hash, command.UserAgent, token), commitOnFailure: true, ct);
    }

    private async Task<Result<RotatedSession>> RotateAsync(string hash, string? userAgent, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var current = await refreshTokens.FindByHashForUpdateAsync(hash, ct);
        if (current is null || current.SubjectType != TokenSubjectType.Customer)
        {
            return AuthErrors.RefreshInvalid;
        }

        if (current.RevokedAt is { } revokedAt)
        {
            if (current.ReplacedBy is not null && now - revokedAt < SessionPolicy.RefreshRaceGrace)
            {
                return AuthErrors.RefreshRace;
            }

            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, ct);
            audit.Record(AuditActions.TokenReuseDetected, "refresh_token", current.FamilyId.ToString(), $"customer:{current.SubjectId}");
            LogReuse(logger, current.FamilyId);
            return AuthErrors.RefreshReused;
        }

        if (current.IsExpired(now))
        {
            return AuthErrors.RefreshExpired;
        }

        var customer = await customers.GetByIdAsync(current.SubjectId, ct);
        if (customer is not { IsActive: true })
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, ct);
            return customer is null ? AuthErrors.RefreshInvalid : AuthErrors.AccountBlocked;
        }

        var (raw, nextHash) = codec.Create();
        var next = current.Rotate(nextHash, SessionPolicy.CustomerRefreshLifetime, userAgent, now);
        refreshTokens.Add(next);
        var access = issuer.IssueCustomer(customer.Id);
        return new RotatedSession(new AccessTokenResponse(access.Token, access.ExpiresInSeconds), raw, next.ExpiresAt);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected; family {FamilyId} revoked")]
    private static partial void LogReuse(ILogger logger, Guid familyId);
}
