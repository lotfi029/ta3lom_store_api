namespace Ta3lom.Store.Domain.Entities.Media;

/// Table `media_asset` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class MediaAsset : AggregateRoot
{
    public MediaAsset(MediaKind kind, string storageKey, string mime, long sizeBytes, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Kind = kind;
        StorageKey = Guard.NotNull(storageKey);
        IsPublic = true;
        Mime = Guard.NotNull(mime);
        SizeBytes = sizeBytes;
        CreatedAt = now;
    }

    private MediaAsset()
    {
    }

    public MediaKind Kind { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public bool IsPublic { get; private set; }

    public string Mime { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public string? AltAr { get; private set; }

    public string? AltEn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// Book files are always private (`ck_media_private_book`); images need Arabic alt text (RUL-03).
    public static MediaAsset Create(MediaKind kind, string storageKey, string mime, long sizeBytes, int? width, int? height,
        string? altAr, string? altEn, DateTimeOffset now)
    {
        Guard.Against(kind == MediaKind.Image && string.IsNullOrWhiteSpace(altAr), "media.alt_required");
        var asset = new MediaAsset(kind, storageKey, mime, sizeBytes, now)
        {
            IsPublic = kind != MediaKind.BookFile,
            Width = width,
            Height = height,
            AltAr = altAr,
            AltEn = altEn,
        };
        return asset;
    }

    public void SetAlt(string? altAr, string? altEn)
    {
        Guard.Against(Kind == MediaKind.Image && string.IsNullOrWhiteSpace(altAr), "media.alt_required");
        AltAr = altAr;
        AltEn = altEn;
    }
}
