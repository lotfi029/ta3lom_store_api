using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Infrastructure.Persistence.Interceptors;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// Staff changes are written to audit_log in the same SaveChanges; personal data is masked (09 §3.4).
public sealed class AuditInterceptorTests
{
    [Fact]
    public async Task Staff_changes_are_audited_with_masked_personal_data()
    {
        var cs = await StoreSchema.CreateMigratedAsync("audit");
        var staff = new Actor(ActorKind.Staff, "manager@ta3lom.sa", "ab12");
        await using (var db = StoreSchema.CreateContext(cs, new AuditSaveChangesInterceptor(staff, new FakeClock())))
        {
            db.Add(new Category("new-cat", "تصنيف جديد"));
            db.Add(new Customer("+966551234567", DateTimeOffset.UnixEpoch));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var rows = (await SqlRunner.QueryAllAsync(cs, "SELECT actor || '|' || action || '|' || entity || '|' || coalesce(ip_hash,'') || '|' || diff::text FROM store.audit_log ORDER BY action")).Single();

        rows.Count.ShouldBe(2);
        rows[0].ShouldStartWith("manager@ta3lom.sa|category.added|category|ab12");
        var customerDiff = JsonDocument.Parse(rows[1].Split('|', 5)[4]).RootElement;
        rows[1].ShouldStartWith("manager@ta3lom.sa|customer.added|customer|");
        customerDiff.GetProperty("mobile").GetString().ShouldBe(AuditSaveChangesInterceptor.Mask);
        customerDiff.GetProperty("status").GetString().ShouldBe("active");   // stored value, not the C# name
        rows[1].ShouldNotContain("551234567");
    }

    [Fact]
    public async Task Customer_and_anonymous_changes_are_not_audited()
    {
        var cs = await StoreSchema.CreateMigratedAsync("audit_anon");
        await using (var db = StoreSchema.CreateContext(cs, new AuditSaveChangesInterceptor(new Actor(ActorKind.Anonymous, null, null), new FakeClock())))
        {
            db.Add(new Category("anon-cat", "تصنيف"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.audit_log")).ShouldBe(0);
    }

    private sealed record Actor(ActorKind Kind, string? Name, string? IpHash) : ICurrentActor
    {
        public Guid? Id => null;
    }
}
