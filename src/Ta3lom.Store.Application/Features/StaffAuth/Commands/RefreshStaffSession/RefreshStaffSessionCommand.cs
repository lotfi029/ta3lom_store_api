using Microsoft.Extensions.Logging;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.RefreshStaffSession;

/// Staff refresh rotation (D-11): same reuse detection as donors, within the 8-hour family. An inactive or locked staff
/// member loses the whole family. Roles and the `pwd_change` claim are re-read, so a change applies at the next refresh.
public sealed record RefreshStaffSessionCommand(string? RefreshToken, string? UserAgent) : ICommand<StaffSession>;

internal sealed partial class RefreshStaffSessionCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IStaffUserRepository staff,
    IRefreshTokenCodec codec,
    StaffSessions sessions,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock,
    ILogger<RefreshStaffSessionCommandHandler> logger) : ICommandHandler<RefreshStaffSessionCommand, StaffSession>
{
    public async Task<Result<StaffSession>> HandleAsync(RefreshStaffSessionCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return AuthErrors.RefreshInvalid;
        }

        var hash = codec.Hash(command.RefreshToken);
        return await uow.ExecuteInTransactionAsync(token => RotateAsync(hash, command.UserAgent, token), commitOnFailure: true, ct);
    }

    private async Task<Result<StaffSession>> RotateAsync(string hash, string? userAgent, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var current = await refreshTokens.FindByHashForUpdateAsync(hash, ct);
        if (current is null || current.SubjectType != TokenSubjectType.Staff)
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
            audit.Record(AuditActions.TokenReuseDetected, "refresh_token", current.FamilyId.ToString(), $"staff:{current.SubjectId}");
            LogReuse(logger, current.FamilyId);
            return AuthErrors.RefreshReused;
        }

        if (current.IsExpired(now))
        {
            return AuthErrors.RefreshExpired;
        }

        var user = await staff.GetByIdAsync(current.SubjectId, ct);
        if (user is null || !user.IsActive || user.IsLocked(now))
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, ct);
            return AuthErrors.RefreshInvalid;
        }

        return sessions.Rotate(user, current, userAgent, now);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Staff refresh token reuse detected; family {FamilyId} revoked")]
    private static partial void LogReuse(ILogger logger, Guid familyId);
}
