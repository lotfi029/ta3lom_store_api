namespace Ta3lom.Store.Application.DTOs.Media;

/// A media library entry; `url` is set for public files only.
public sealed record MediaAssetResponse(Guid Id, string Kind, bool IsPublic, string? Url, string Mime, long SizeBytes, int? Width, int? Height,
    string? AltAr, string? AltEn, DateTimeOffset CreatedAt);

/// `PATCH /v1/admin/media/{id}` body.
public sealed record MediaAltInput(string? AltAr, string? AltEn);
