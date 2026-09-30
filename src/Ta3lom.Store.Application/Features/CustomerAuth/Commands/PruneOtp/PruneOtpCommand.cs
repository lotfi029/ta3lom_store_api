using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.PruneOtp;

/// PruneOtp job (09 §6, hourly): PDPL — OTP challenges older than 24 h are deleted.
public sealed record PruneOtpCommand : ICommand<int>;

internal sealed class PruneOtpCommandHandler(IOtpChallengeRepository otps, IClock clock) : ICommandHandler<PruneOtpCommand, int>
{
    public async Task<Result<int>> HandleAsync(PruneOtpCommand command, CancellationToken ct = default) =>
        await otps.DeleteCreatedBeforeAsync(clock.UtcNow - SessionPolicy.OtpRetention, ct);
}
