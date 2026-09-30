namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.Logout;

/// Revokes the current refresh token (FR-S28). Always succeeds so a stale cookie can still be cleared.
public sealed record LogoutCommand(string? RefreshToken) : ICommand;

internal sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IRefreshTokenCodec codec,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            var current = await refreshTokens.FindByHashAsync(codec.Hash(command.RefreshToken), ct);
            if (current is { IsRevoked: false })
            {
                current.Revoke(clock.UtcNow);
                await uow.SaveChangesAsync(ct);
            }
        }

        return Result.Success();
    }
}
