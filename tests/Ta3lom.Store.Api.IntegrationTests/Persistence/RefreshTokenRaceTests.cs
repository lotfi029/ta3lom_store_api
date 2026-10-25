using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.IRepositories;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// A-6: "sign out everywhere" (and every other revoke-all) racing a refresh: the rotation's successor is revoked too.
public sealed class RefreshTokenRaceTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static string NewHash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Revoking_all_tokens_during_a_rotation_also_revokes_the_successor()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.CreateVersion7();
        var now = factory.Clock.UtcNow;
        var hash = NewHash();
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            seed.ServiceProvider.GetRequiredService<IRefreshTokenRepository>()
                .Add(RefreshToken.StartFamily(TokenSubjectType.Customer, subject, hash, TimeSpan.FromDays(60), TimeSpan.FromDays(1), null, now));
            await seed.ServiceProvider.GetRequiredService<StoreDbContext>().SaveChangesAsync(ct);
        }

        // A refresh in flight: the current token is locked, rotated and its successor written; revoke-all starts before the
        // rotation commits.
        Task? revokeAll = null;
        await using (var rotation = factory.Services.CreateAsyncScope())
        {
            var repo = rotation.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
            var db = rotation.ServiceProvider.GetRequiredService<StoreDbContext>();
            await rotation.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
            {
                var current = await repo.FindByHashForUpdateAsync(hash, token);
                repo.Add(current!.Rotate(NewHash(), TimeSpan.FromDays(1), null, now));
                await db.SaveChangesAsync(token);
                revokeAll = Task.Run(async () =>
                {
                    await using var scope = factory.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>().RevokeAllForSubjectAsync(TokenSubjectType.Customer, subject, now, ct);
                }, ct);
                await Task.Delay(500, token);
            }, ct);
        }

        await revokeAll!;

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refresh_token WHERE subject_id = '{subject}' AND revoked_at IS NULL")).ShouldBe(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refresh_token WHERE subject_id = '{subject}'")).ShouldBe(2);
    }
}
