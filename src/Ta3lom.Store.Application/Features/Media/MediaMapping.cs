using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.Media;

internal static class MediaMapping
{
    /// Private files (book files) have no URL (11 §9.6).
    public static MediaAssetResponse ToResponse(MediaAsset m, IFileUrls urls) =>
        new(m.Id, m.Kind.ToWire(), m.IsPublic, m.IsPublic ? urls.Public(m.StorageKey) : null, m.Mime, m.SizeBytes, m.Width, m.Height, m.AltAr, m.AltEn, m.CreatedAt);
}
