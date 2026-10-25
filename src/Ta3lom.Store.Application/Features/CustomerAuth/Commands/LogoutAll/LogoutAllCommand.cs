namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.LogoutAll;

/// "Sign out everywhere": every refresh-token family of the donor is revoked.
public sealed record LogoutAllCommand(Guid CustomerId) : ICommand;

internal sealed class LogoutAllCommandHandler(IRefreshTokenRepository refreshTokens, IClock clock) : ICommandHandler<LogoutAllCommand>
{
    public async Task<Result> HandleAsync(LogoutAllCommand command, CancellationToken ct = default)
    {
        await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Customer, command.CustomerId, clock.UtcNow, ct);
        return Result.Success();
    }
}
