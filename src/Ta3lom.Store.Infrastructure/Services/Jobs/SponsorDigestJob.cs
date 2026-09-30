using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Giving.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// 1st of the month 09:00 Riyadh (`0 0 9 1 * ?`, 09 §6): monthly sponsor digest, once per sponsor per month (RUL-63).
[DisallowConcurrentExecution]
internal sealed class SponsorDigestJob(ICommandHandler<SponsorDigestCommand, int> handler) : IJob
{
    public const string Cron = "0 0 9 1 * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new SponsorDigestCommand(), cancellationToken);
}
