using Ta3lom.Store.Application.DTOs.Media;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.Media;

internal static class MediaMapping
{
    /// Private files have no URL.
    public static MediaAssetResponse ToResponse(MediaAsset m, IFileUrls urls) =>
        new(m.Id, m.Kind.ToWire(), m.IsPublic, m.IsPublic ? urls.Public(m.StorageKey) : null, m.Mime, m.SizeBytes, m.Width, m.Height, m.AltAr, m.AltEn, m.CreatedAt);
}
