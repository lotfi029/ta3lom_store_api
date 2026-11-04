using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Payments;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Every 5 minutes (`0 0/5 * * * ?`): re-reads open payment attempts from their gateway (RUL-38).
[DisallowConcurrentExecution]
internal sealed class ReconcilePaymentsJob(ICommandHandler<ReconcilePaymentsCommand, int> handler, JobRuns runs) : IJob
{
    public const string Cron = "0 0/5 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await runs.RunAsync(context.JobDetail.Key.Name, context.ScheduledFireTimeUtc, ct => handler.HandleAsync(new ReconcilePaymentsCommand(), ct), cancellationToken);
}

/// Every 5 minutes, offset by 2 (`0 2/5 * * * ?`): expires unpaid orders past their due time.
[DisallowConcurrentExecution]
internal sealed class ExpireUnpaidOrdersJob(ICommandHandler<ExpireUnpaidOrdersCommand, int> handler, JobRuns runs) : IJob
{
    public const string Cron = "0 2/5 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await runs.RunAsync(context.JobDetail.Key.Name, context.ScheduledFireTimeUtc, ct => handler.HandleAsync(new ExpireUnpaidOrdersCommand(), ct), cancellationToken);
}

/// Every 5 minutes, offset by 4 (`0 4/5 * * * ?`): confirms or fails refunds still waiting for the gateway (review 04 P-3).
[DisallowConcurrentExecution]
internal sealed class ReconcileRefundsJob(ICommandHandler<ReconcileRefundsCommand, int> handler, JobRuns runs) : IJob
{
    public const string Cron = "0 4/5 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await runs.RunAsync(context.JobDetail.Key.Name, context.ScheduledFireTimeUtc, ct => handler.HandleAsync(new ReconcileRefundsCommand(), ct), cancellationToken);
}
