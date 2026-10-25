using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository(StoreDbContext db) : GenericRepository<Customer>(db), ICustomerRepository
{
    public Task<Customer?> FindByMobileAsync(string mobileE164, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(c => c.Mobile == mobileE164, ct);
}

internal sealed class OtpChallengeRepository(StoreDbContext db) : GenericRepository<OtpChallenge>(db), IOtpChallengeRepository
{
    public Task LockMobileAsync(string mobileE164, CancellationToken ct = default) =>
        Db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"otp:" + mobileE164}, 0))", ct);

    public Task LockIpAsync(string ipHash, CancellationToken ct = default) =>
        Db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"otp-ip:" + ipHash}, 0))", ct);

    public async Task<IReadOnlyList<DateTimeOffset>> IssuedForMobileSinceAsync(string mobileE164, DateTimeOffset since, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(o => o.Mobile == mobileE164 && o.CreatedAt >= since)
            .OrderBy(o => o.CreatedAt).Select(o => o.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<DateTimeOffset>> IssuedForIpSinceAsync(string ipHash, DateTimeOffset since, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(o => o.IpHash == ipHash && o.CreatedAt >= since)
            .OrderBy(o => o.CreatedAt).Select(o => o.CreatedAt).ToListAsync(ct);

    public Task<int> CountIssuedSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
        Set.AsNoTracking().CountAsync(o => o.CreatedAt >= since, ct);

    public async Task<OtpChallenge?> LatestOpenForUpdateAsync(string mobileE164, OtpPurpose purpose, CancellationToken ct = default)
    {
        var wire = purpose.ToWire();
        var rows = await Set.FromSql($"""
            SELECT * FROM store.otp_challenge
             WHERE mobile = {mobileE164} AND purpose = {wire} AND consumed_at IS NULL
             ORDER BY created_at DESC LIMIT 1
             FOR UPDATE
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }
}

/// A-6: a rotation and a revoke-all of the same subject serialise on one transaction-scoped advisory lock, so revoke-all
/// waits for a rotation in flight and its UPDATE then sees the successor (READ COMMITTED gives each statement a new snapshot).
internal sealed class RefreshTokenRepository(StoreDbContext db) : GenericRepository<RefreshToken>(db), IRefreshTokenRepository
{
    public async Task<RefreshToken?> FindByHashForUpdateAsync(string tokenHash, CancellationToken ct = default)
    {
        var subject = await Set.AsNoTracking().Where(t => t.TokenHash == tokenHash).Select(t => new { t.SubjectType, t.SubjectId }).FirstOrDefaultAsync(ct);
        if (subject is null)
        {
            return null;
        }

        await LockSubjectAsync(subject.SubjectType, subject.SubjectId, ct);
        var rows = await Set.FromSql($"SELECT * FROM store.refresh_token WHERE token_hash = {tokenHash} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct = default) =>
        Set.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now), ct);

    public async Task<int> RevokeAllForSubjectAsync(TokenSubjectType subjectType, Guid subjectId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (Db.Database.CurrentTransaction is not null)
        {
            return await RevokeLockedAsync();
        }

        return await Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await Db.Database.BeginTransactionAsync(ct);
            var revoked = await RevokeLockedAsync();
            await tx.CommitAsync(ct);
            return revoked;
        });

        async Task<int> RevokeLockedAsync()
        {
            await LockSubjectAsync(subjectType, subjectId, ct);
            return await Set.Where(t => t.SubjectType == subjectType && t.SubjectId == subjectId && t.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now), ct);
        }
    }

    private Task<int> LockSubjectAsync(TokenSubjectType subjectType, Guid subjectId, CancellationToken ct) =>
        Db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"tokens:" + subjectType.ToWire() + ":" + subjectId.ToString("N")}, 0))", ct);
}

internal sealed class NotificationRepository(StoreDbContext db) : GenericRepository<Notification>(db), INotificationRepository
{
    public async Task<NotificationClaim> TryClaimAsync(string dedupeKey, MessageChannel channel, string recipientHash, string templateKey,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var ids = await Db.Database.SqlQuery<Guid>($"""
            INSERT INTO store.notification (id, channel, recipient_hash, template_key, dedupe_key, status, created_at)
            VALUES ({Guid.CreateVersion7()}, {channel.ToWire()}, {recipientHash}, {templateKey}, {dedupeKey}, 'sending', {now})
            ON CONFLICT (dedupe_key) DO UPDATE SET status = 'sending', created_at = EXCLUDED.created_at WHERE notification.status = 'failed'
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        return ids.Count == 0
            ? new NotificationClaim(await Set.SingleAsync(n => n.DedupeKey == dedupeKey, ct), Claimed: false)
            : new NotificationClaim(await Set.SingleAsync(n => n.Id == ids[0], ct), Claimed: true);
    }
}

internal sealed class StaffUserRepository(StoreDbContext db) : GenericRepository<StaffUser>(db), IStaffUserRepository
{
    /// `staff_user.email` is citext: the comparison in SQL is case-insensitive.
    public Task<StaffUser?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(s => s.Email == email, ct);

    public async Task LockForUpdateAsync(StaffUser user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await Db.Database.SqlQuery<Guid>($"""SELECT id AS "Value" FROM store.staff_user WHERE id = {user.Id} FOR UPDATE""").ToListAsync(ct);
        await Db.Entry(user).ReloadAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> LockActiveAdminIdsAsync(CancellationToken ct = default) =>
        await Db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM store.staff_user WHERE is_active AND 'admin' = ANY (roles) ORDER BY id FOR UPDATE
            """).ToListAsync(ct);
}
