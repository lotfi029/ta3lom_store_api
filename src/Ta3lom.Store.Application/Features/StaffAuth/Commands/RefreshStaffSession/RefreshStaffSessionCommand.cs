using MapsterMapper;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.RefreshStaffSession;

/// Staff refresh rotation (09 §5.8): same reuse detection as customers; an inactive or locked staff user loses
/// the whole family. Roles are re-read, so a role change applies at the next refresh (≤ 10 min).
public sealed record RefreshStaffSessionCommand(string? RefreshToken, string? UserAgent) : ICommand<StaffSession>;

internal sealed partial class RefreshStaffSessionCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IStaffUserRepository staff,
    IRefreshTokenCodec codec,
    IAccessTokenIssuer issuer,
    IMapper mapper,
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
        return await uow.ExecuteInTransactionAsync(
            async token =>
            {
                var now = clock.UtcNow;
                var current = await refreshTokens.FindByHashForUpdateAsync(hash, token);
                if (current is null || current.SubjectType != TokenSubjectType.Staff)
                {
                    return Result.Failure<StaffSession>(AuthErrors.RefreshInvalid);
                }

                if (current.RevokedAt is { } revokedAt)
                {
                    if (current.ReplacedBy is not null && now - revokedAt < SessionPolicy.RefreshRaceGrace)
                    {
                        return Result.Failure<StaffSession>(AuthErrors.RefreshRace);
                    }

                    await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, token);
                    LogReuse(logger, current.FamilyId);
                    return Result.Failure<StaffSession>(AuthErrors.RefreshReused);
                }

                if (current.ExpiresAt <= now)
                {
                    return Result.Failure<StaffSession>(AuthErrors.RefreshExpired);
                }

                var user = await staff.GetByIdAsync(current.SubjectId, token);
                if (user is null || !user.IsActive || user.IsLocked(now))
                {
                    await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, token);
                    return Result.Failure<StaffSession>(AuthErrors.RefreshInvalid);
                }

                var (raw, nextHash) = codec.Create();
                var expiresAt = now + SessionPolicy.StaffFamilyLifetime;
                var next = current.Rotate(nextHash, SessionPolicy.StaffFamilyLifetime, command.UserAgent, now);
                refreshTokens.Add(next);

                var access = issuer.IssueStaff(user.Id, user.Email, user.Roles);
                return Result.Success(new StaffSession(new StaffSessionResponse(access.Token, access.ExpiresInSeconds, mapper.Map<StaffMeResponse>(user)), raw, expiresAt));
            },
            commitOnFailure: true,
            ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Staff refresh token reuse detected; family {FamilyId} revoked")]
    private static partial void LogReuse(ILogger logger, Guid familyId);
}
