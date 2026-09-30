using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Documents;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Entities.Settings;
using Ta3lom.Store.Domain.ReadModels;

namespace Ta3lom.Store.Application.Abstractions.Data;

/// Read access for query handlers (DMS `IApplicationDbContext`, K-c): `AsNoTracking` projections to DTOs only.
/// No SaveChanges, no raw SQL, no row locks here: commands go through repositories + IUnitOfWork.
public interface IApplicationDbContext
{
    // Auditing
    DbSet<AuditLogEntry> AuditLog { get; }


    // Carts
    DbSet<Cart> Carts { get; }

    DbSet<CartLine> CartLines { get; }


    // Catalog
    DbSet<BookDetail> BookDetails { get; }

    DbSet<BookFormat> BookFormats { get; }

    DbSet<Category> Categories { get; }

    DbSet<CauseDetail> CauseDetails { get; }

    DbSet<CourseDetail> CourseDetails { get; }

    DbSet<CourseModule> CourseModules { get; }

    DbSet<GoalAdjustment> GoalAdjustments { get; }

    DbSet<GoalProgress> GoalProgress { get; }

    DbSet<Product> Products { get; }

    DbSet<SponsorshipOffer> SponsorshipOffers { get; }


    // Documents
    DbSet<DocumentCounter> DocumentCounters { get; }

    DbSet<DonationReceipt> DonationReceipts { get; }

    DbSet<Invoice> Invoices { get; }


    // Fulfilment
    DbSet<DownloadGrant> DownloadGrants { get; }

    DbSet<DownloadLogEntry> DownloadLog { get; }

    DbSet<Enrolment> Enrolments { get; }

    DbSet<Shipment> Shipments { get; }

    DbSet<StockMovement> StockMovements { get; }


    // Giving
    DbSet<AllocationBatch> AllocationBatches { get; }

    DbSet<Beneficiary> Beneficiaries { get; }

    DbSet<CauseUpdate> CauseUpdates { get; }

    DbSet<Distribution> Distributions { get; }

    DbSet<PoolContribution> PoolContributions { get; }

    DbSet<SponsoredUnit> SponsoredUnits { get; }

    DbSet<SponsoredUnitEvent> SponsoredUnitEvents { get; }

    DbSet<SponsorshipPool> SponsorshipPools { get; }

    DbSet<UnitFunding> UnitFundings { get; }

    DbSet<ZakatLedgerEntry> ZakatLedger { get; }


    // Identity
    DbSet<Customer> Customers { get; }

    DbSet<CustomerAddress> CustomerAddresses { get; }

    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<StaffUser> StaffUsers { get; }


    // Media
    DbSet<MediaAsset> MediaAssets { get; }


    // Notifications
    DbSet<MessageTemplate> MessageTemplates { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }


    // Ordering
    DbSet<Dedication> Dedications { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderLine> OrderLines { get; }


    // Payments
    DbSet<Payment> Payments { get; }

    DbSet<PaymentEvent> PaymentEvents { get; }

    DbSet<Refund> Refunds { get; }

    DbSet<RefundLine> RefundLines { get; }


    // Settings
    DbSet<Setting> Settings { get; }

    // Views (read models)
    DbSet<GoalView> Goals { get; }

    DbSet<SalesDailyView> SalesDaily { get; }

    DbSet<AllocationBacklogView> AllocationBacklog { get; }

    DbSet<ZakatBalanceView> ZakatBalances { get; }
}
