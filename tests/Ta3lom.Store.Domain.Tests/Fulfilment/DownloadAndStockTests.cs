using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Fulfilment;

/// Digital download grants (RUL-41) and the stock ledger (RUL-30).
public sealed class DownloadAndStockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Rule("RUL-41")]
    public void A_grant_allows_five_downloads_by_default()
    {
        var customer = Guid.CreateVersion7();
        var line = Guid.CreateVersion7();
        var format = Guid.CreateVersion7();

        var g = new DownloadGrant(customer, line, format, T0);

        g.CustomerId.ShouldBe(customer);
        g.OrderLineId.ShouldBe(line);
        g.BookFormatId.ShouldBe(format);
        g.MaxDownloads.ShouldBe(5);
        g.DownloadCount.ShouldBe(0);
        g.Remaining.ShouldBe(5);
        g.CreatedAt.ShouldBe(T0);
        g.IsRevoked.ShouldBeFalse();
        Should.Throw<DomainException>(() => new DownloadGrant(customer, line, Guid.Empty, T0)).Code.ShouldBe("guard.empty");
    }

    [Theory]
    [Rule("RUL-41")]
    [InlineData(10, 10)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public void The_limit_comes_from_the_setting_and_is_at_least_one(int setting, int expected) =>
        DownloadGrant.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), setting, T0).MaxDownloads.ShouldBe(expected);

    [Fact]
    [Rule("RUL-41")]
    public void Staff_can_reset_a_live_grant_but_not_a_revoked_one()
    {
        var g = DownloadGrant.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 5, T0);

        g.Reset().ShouldBeTrue();
        g.DownloadCount.ShouldBe(0);

        g.Revoke(T0.AddDays(1));
        g.Revoke(T0.AddDays(2));
        g.IsRevoked.ShouldBeTrue();
        g.RevokedAt.ShouldBe(T0.AddDays(1));
        g.Reset().ShouldBeFalse();
    }

    [Fact]
    public void A_download_log_entry_keeps_the_grant_and_ip_hash()
    {
        var grant = Guid.CreateVersion7();

        var entry = DownloadLogEntry.For(grant, "ip-hash", T0);

        entry.GrantId.ShouldBe(grant);
        entry.IpHash.ShouldBe("ip-hash");
        entry.At.ShouldBe(T0);
        new DownloadLogEntry(grant, T0).IpHash.ShouldBeNull();
        Should.Throw<DomainException>(() => new DownloadLogEntry(Guid.Empty, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-30")]
    public void Every_stock_change_is_a_non_zero_ledger_row()
    {
        var format = Guid.CreateVersion7();

        var m = StockMovement.Record(format, -2, StockMovementReason.Sale, "order", "T3-26-000001", "paid", "settlement", T0);

        m.BookFormatId.ShouldBe(format);
        m.Delta.ShouldBe(-2);
        m.Reason.ShouldBe(StockMovementReason.Sale);
        m.RefType.ShouldBe("order");
        m.RefId.ShouldBe("T3-26-000001");
        m.Note.ShouldBe("paid");
        m.Actor.ShouldBe("settlement");
        m.At.ShouldBe(T0);
        Should.Throw<DomainException>(() => StockMovement.Record(format, 0, StockMovementReason.Adjustment, "x", null, null, "a", T0))
            .Code.ShouldBe("stock.delta_zero");
        Should.Throw<DomainException>(() => new StockMovement(format, 1, StockMovementReason.Restock, null!, T0)).Code.ShouldBe("guard.null");
    }
}
