using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.Media.Commands.UploadMedia;

/// `POST /v1/admin/media` (#75, FR-S72, RUL-03): allow-list per kind; images are re-encoded to WebP (max 2400 px),
/// which strips EXIF; PDFs and book files must start with `%PDF`; `book_file` is stored private.
/// Limits: images 10 MB, PDFs and book files 200 MB (11 §9.6, OP-6).
public sealed record UploadMediaCommand(string Kind, string ContentType, long Length, Stream Content, string? AltAr, string? AltEn) : ICommand<MediaAssetResponse>;

internal sealed class UploadMediaCommandHandler(
    IGenericRepository<MediaAsset> media,
    IFileStorage storage,
    IImageProcessor images,
    IFileUrls urls,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UploadMediaCommand, MediaAssetResponse>
{
    public const long MaxImageBytes = 10L * 1024 * 1024;
    public const long MaxPdfBytes = 200L * 1024 * 1024;
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public async Task<Result<MediaAssetResponse>> HandleAsync(UploadMediaCommand command, CancellationToken ct = default)
    {
        var kind = EnumWire.Parse<MediaKind>(command.Kind);
        var now = clock.UtcNow;
        var id = Guid.CreateVersion7();

        if (kind == MediaKind.Image)
        {
            if (string.IsNullOrWhiteSpace(command.AltAr))
            {
                return AdminCatalogErrors.MediaAltRequired;
            }

            if (!ImageTypes.Contains(command.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                return AdminCatalogErrors.MediaTypeNotAllowed;
            }

            if (command.Length > MaxImageBytes)
            {
                return AdminCatalogErrors.MediaTooLarge(MaxImageBytes);
            }

            using var buffer = new MemoryStream();
            await command.Content.CopyToAsync(buffer, ct);
            var processed = images.ToWebp(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), 2400);
            if (processed is null)
            {
                return AdminCatalogErrors.MediaTypeNotAllowed;
            }

            var key = $"images/{now:yyyy}/{id:N}.webp";
            await storage.SaveAsync(key, isPublic: true, new MemoryStream(processed.Bytes), ct);
            return await SaveAsync(MediaAsset.Create(kind, key, "image/webp", processed.Bytes.LongLength, processed.Width, processed.Height,
                command.AltAr, command.AltEn, now), ct);
        }

        if (!string.Equals(command.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return AdminCatalogErrors.MediaTypeNotAllowed;
        }

        if (command.Length > MaxPdfBytes)
        {
            return AdminCatalogErrors.MediaTooLarge(MaxPdfBytes);
        }

        var header = new byte[5];
        var read = await command.Content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < 5 || header[0] != '%' || header[1] != 'P' || header[2] != 'D' || header[3] != 'F')
        {
            return AdminCatalogErrors.MediaTypeNotAllowed;
        }

        var isPublic = kind != MediaKind.BookFile;
        var fileKey = kind == MediaKind.BookFile ? $"books/{id:N}.pdf" : $"pdf/{now:yyyy}/{id:N}.pdf";
        using (var content = new ConcatenatedStream(header.AsMemory(0, read), command.Content))
        {
            await storage.SaveAsync(fileKey, isPublic, content, ct);
        }

        return await SaveAsync(MediaAsset.Create(kind, fileKey, "application/pdf", command.Length, null, null, command.AltAr, command.AltEn, now), ct);
    }

    private async Task<Result<MediaAssetResponse>> SaveAsync(MediaAsset asset, CancellationToken ct)
    {
        media.Add(asset);
        await uow.SaveChangesAsync(ct);
        return MediaMapping.ToResponse(asset, urls);
    }

    /// The bytes already read for the magic-number check, followed by the rest of the upload.
    private sealed class ConcatenatedStream(ReadOnlyMemory<byte> head, Stream tail) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position < head.Length)
            {
                var n = Math.Min(buffer.Length, head.Length - _position);
                head.Span.Slice(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            return tail.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < head.Length)
            {
                var n = Math.Min(buffer.Length, head.Length - _position);
                head.Slice(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            return await tail.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class UploadMediaCommandValidator : AbstractValidator<UploadMediaCommand>
{
    public UploadMediaCommandValidator()
    {
        RuleFor(c => c.Kind).Must(k => k is "image" or "pdf" or "book_file").WithMessage("kind must be image, pdf or book_file.");
        RuleFor(c => c.Length).GreaterThan(0).WithMessage("file is required.");
    }
}
