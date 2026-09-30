using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.PruneOtp;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Hourly (`0 0 * * * ?`): deletes OTP challenges older than 24 h (PDPL, 09 §6).
[DisallowConcurrentExecution]
internal sealed class PruneOtpJob(ICommandHandler<PruneOtpCommand, int> handler) : IJob
{
    public const string Cron = "0 0 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) => await handler.HandleAsync(new PruneOtpCommand(), cancellationToken);
}
