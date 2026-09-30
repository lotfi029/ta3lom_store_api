using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Downloads.Commands.DownloadFile;

/// `GET /v1/downloads/{token}` (#46, RUL-41): checks the signed link, revocation and the limit (conditional increment),
/// logs the download (hashed IP) and returns the watermarked PDF — stamped once per grant and cached privately
/// (`downloads/{grantId}.pdf`, P0-4).
public sealed record DownloadFileCommand(string Token, string? IpHash) : ICommand<DownloadedFile>;

public sealed record DownloadedFile(byte[] Content, string FileName);

internal sealed class DownloadFileCommandHandler(
    IDownloadLinks links,
    IDownloadGrantRepository grants,
    IGenericRepository<DownloadLogEntry> log,
    IApplicationDbContext db,
    IFileStorage storage,
    IPdfWatermarker watermarker,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<DownloadFileCommand, DownloadedFile>
{
    public async Task<Result<DownloadedFile>> HandleAsync(DownloadFileCommand command, CancellationToken ct = default)
    {
        var token = links.Read(command.Token, clock.UtcNow);
        if (token is null)
        {
            return DownloadErrors.LinkExpired;
        }

        var grant = await db.DownloadGrants.AsNoTracking().FirstOrDefaultAsync(g => g.Id == token.GrantId, ct);
        if (grant is null || grant.CustomerId != token.CustomerId)
        {
            return DownloadErrors.LinkExpired;                                             // not this customer's link
        }

        if (grant.IsRevoked)
        {
            return DownloadErrors.Revoked;
        }

        var info = await (from l in db.OrderLines.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                          join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                          join f in db.BookFormats.AsNoTracking() on grant.BookFormatId equals f.Id
                          where l.Id == grant.OrderLineId
                          select new { o.Reference, o.ContactName, o.ContactMobile, p.Slug, f.FileAssetId }).FirstAsync(ct);

        var content = await CachedOrStampedAsync(grant.Id, info.FileAssetId, new PdfWatermark(info.ContactName, SaudiMobile.Mask(info.ContactMobile),
            info.Reference), ct);

        if (!await grants.TryConsumeAsync(grant.Id, ct))
        {
            return (await db.DownloadGrants.AsNoTracking().AnyAsync(g => g.Id == grant.Id && g.RevokedAt != null, ct))
                ? DownloadErrors.Revoked
                : DownloadErrors.LimitReached;
        }

        log.Add(DownloadLogEntry.For(grant.Id, command.IpHash, clock.UtcNow));
        await uow.SaveChangesAsync(ct);
        return new DownloadedFile(content, info.Slug + ".pdf");
    }

    private async Task<byte[]> CachedOrStampedAsync(Guid grantId, Guid? fileAssetId, PdfWatermark watermark, CancellationToken ct)
    {
        var cacheKey = FormattableString.Invariant($"downloads/{grantId:N}.pdf");
        await using (var cached = await storage.OpenReadAsync(cacheKey, isPublic: false, ct))
        {
            if (cached is not null)
            {
                using var buffer = new MemoryStream();
                await cached.CopyToAsync(buffer, ct);
                return buffer.ToArray();
            }
        }

        var sourceKey = await db.MediaAssets.AsNoTracking().Where(m => m.Id == fileAssetId).Select(m => m.StorageKey).FirstOrDefaultAsync(ct)
                        ?? throw new InvalidOperationException($"Book file asset {fileAssetId} is missing.");
        await using var source = await storage.OpenReadAsync(sourceKey, isPublic: false, ct)
                                 ?? throw new InvalidOperationException($"Book file {sourceKey} is missing from storage.");
        var stamped = await watermarker.WatermarkAsync(source, watermark, ct);
        using (var stream = new MemoryStream(stamped, writable: false))
        {
            await storage.SaveAsync(cacheKey, isPublic: false, stream, ct);
        }

        return stamped;
    }
}
