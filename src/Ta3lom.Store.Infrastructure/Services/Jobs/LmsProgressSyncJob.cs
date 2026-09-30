using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Enrolments.Commands.LmsProgressSync;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Hourly at :15 (`0 15 * * * ?`, 09 §6): LMS progress and completion → enrolments; sponsored completions → units (RUL-42, RUL-50).
[DisallowConcurrentExecution]
internal sealed class LmsProgressSyncJob(ICommandHandler<LmsProgressSyncCommand, int> handler) : IJob
{
    public const string Cron = "0 15 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new LmsProgressSyncCommand(), cancellationToken);
}
