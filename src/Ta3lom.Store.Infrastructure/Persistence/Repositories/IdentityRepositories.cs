using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
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
    public async Task<IReadOnlyList<DateTimeOffset>> IssuedForMobileSinceAsync(string mobileE164, DateTimeOffset since, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(o => o.Mobile == mobileE164 && o.CreatedAt >= since)
            .OrderBy(o => o.CreatedAt).Select(o => o.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<DateTimeOffset>> IssuedForIpSinceAsync(string ipHash, DateTimeOffset since, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(o => o.IpHash == ipHash && o.CreatedAt >= since)
            .OrderBy(o => o.CreatedAt).Select(o => o.CreatedAt).ToListAsync(ct);

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

    public Task<int> DeleteCreatedBeforeAsync(DateTimeOffset before, CancellationToken ct = default) =>
        Set.Where(o => o.CreatedAt < before).ExecuteDeleteAsync(ct);
}

internal sealed class RefreshTokenRepository(StoreDbContext db) : GenericRepository<RefreshToken>(db), IRefreshTokenRepository
{
    public async Task<RefreshToken?> FindByHashForUpdateAsync(string tokenHash, CancellationToken ct = default)
    {
        var rows = await Set.FromSql($"SELECT * FROM store.refresh_token WHERE token_hash = {tokenHash} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct = default) =>
        Set.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now), ct);

    public Task<int> RevokeAllForSubjectAsync(TokenSubjectType subjectType, Guid subjectId, DateTimeOffset now, CancellationToken ct = default) =>
        Set.Where(t => t.SubjectType == subjectType && t.SubjectId == subjectId && t.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now), ct);
}

internal sealed class NotificationRepository(StoreDbContext db) : GenericRepository<Notification>(db), INotificationRepository
{
    public async Task<Notification?> TryClaimAsync(Guid outboxMessageId, MessageChannel channel, string recipient, string templateKey, string locale,
        Guid? relatedOrderId, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = new NpgsqlParameter("related_order_id", NpgsqlDbType.Uuid) { Value = (object?)relatedOrderId ?? DBNull.Value };
        var ids = await Db.Database.SqlQuery<Guid>($"""
            INSERT INTO store.notification (id, channel, recipient, template_key, locale, status, related_order_id, outbox_message_id, created_at)
            VALUES ({Guid.CreateVersion7()}, {channel.ToWire()}, {recipient}, {templateKey}, {locale}, 'queued', {order}, {outboxMessageId}, {now})
            ON CONFLICT (outbox_message_id, channel, recipient) WHERE outbox_message_id IS NOT NULL
            DO UPDATE SET status = 'queued', error = NULL WHERE notification.status = 'failed'
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        return ids.Count == 0 ? null : await Set.SingleAsync(n => n.Id == ids[0], ct);
    }
}

internal sealed class StaffUserRepository(StoreDbContext db) : GenericRepository<StaffUser>(db), IStaffUserRepository
{
    /// `staff_user.email` is citext: the comparison in SQL is case-insensitive.
    public Task<StaffUser?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(s => s.Email == email, ct);
}
