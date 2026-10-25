using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Operations;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.IntegrationTests.Operations;

/// The operations jobs are registered with Quartz and each resolves the Application command it runs.
public sealed class OperationsJobsTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    [Fact]
    public void Every_maintenance_job_is_scheduled() =>
        factory.Services.GetRequiredService<IJobRunner>().Jobs.Keys.ShouldBe(
            ["PruneOtpJob", "PruneIdempotencyKeysJob", "ExpireGuestCartsJob", "RetentionJob", "IntegrityChecksJob"], ignoreOrder: true);

    [Fact]
    public void Every_job_command_has_a_handler()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<ICommandHandler<PruneOtpCommand, int>>().ShouldNotBeNull();
        sp.GetRequiredService<ICommandHandler<PruneIdempotencyKeysCommand, int>>().ShouldNotBeNull();
        sp.GetRequiredService<ICommandHandler<ExpireGuestCartsCommand, int>>().ShouldNotBeNull();
        sp.GetRequiredService<ICommandHandler<RetentionCommand, int>>().ShouldNotBeNull();
        sp.GetRequiredService<ICommandHandler<IntegrityChecksCommand, int>>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Jobs_resolve_through_the_Quartz_job_factory()
    {
        var scheduler = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);

        foreach (var name in factory.Services.GetRequiredService<IJobRunner>().Jobs.Keys)
        {
            (await scheduler.GetJobDetail(new JobKey(name, "store"), TestContext.Current.CancellationToken)).ShouldNotBeNull(name);
        }
    }
}
