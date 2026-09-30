namespace Ta3lom.Store.Application.Interfaces;

/// File storage (09 §4.3): `LocalFileStorage` under `Store:Files:Root` with `public/` (served at
/// `/files/public`) and `private/` (never served directly: book files, PDFs). Keys are relative paths.
public interface IFileStorage
{
    Task SaveAsync(string storageKey, bool isPublic, Stream content, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string storageKey, bool isPublic, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, bool isPublic, CancellationToken ct = default);
}

/// Image upload processing (11 §9.6): decode, strip metadata (EXIF) by re-encoding to WebP, max edge 2400 px.
public interface IImageProcessor
{
    /// Null when the bytes are not a decodable image.
    ProcessedImage? ToWebp(ReadOnlySpan<byte> input, int maxEdge);
}

public sealed record ProcessedImage(byte[] Bytes, int Width, int Height);
