using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Entities.Settings;
using Ta3lom.Store.Domain.ReadModels;

namespace Ta3lom.Store.Application.Abstractions.Data;

/// Read access for query handlers (DMS `IApplicationDbContext`): `AsNoTracking` projections to DTOs only.
/// No SaveChanges, no raw SQL, no row locks here: commands go through repositories + IUnitOfWork.
public interface IApplicationDbContext
{
    DbSet<AuditLogEntry> AuditLog { get; }

    DbSet<Category> Categories { get; }

    DbSet<Project> Projects { get; }

    DbSet<ProjectOption> ProjectOptions { get; }

    DbSet<ProjectSlugHistory> ProjectSlugHistory { get; }

    DbSet<Cart> Carts { get; }

    DbSet<CartLine> CartLines { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderLine> OrderLines { get; }

    /// The view `v_catalog_stats` (staff dashboard).
    DbSet<CatalogStat> CatalogStats { get; }

    DbSet<Customer> Customers { get; }

    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<StaffUser> StaffUsers { get; }

    DbSet<MediaAsset> MediaAssets { get; }

    DbSet<MessageTemplate> MessageTemplates { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<Setting> Settings { get; }
}
