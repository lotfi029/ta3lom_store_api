using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Allocation.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 0 2 * * ?` (09 §6) — 02:00 daily: allocated course seats never started after lapse_days → lapsed → awaiting_allocation, enrolment revoked, SMS (RUL-50).
[DisallowConcurrentExecution]
internal sealed class LapseUnitsJob(ICommandHandler<LapseUnitsCommand, int> handler) : IJob
{
    public const string Cron = "0 0 2 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new LapseUnitsCommand(), cancellationToken);
}
