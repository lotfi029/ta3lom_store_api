using FluentValidation;
using Ta3lom.Store.Application.DTOs.Media;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.Media.Commands.UploadMedia;

/// `POST /v1/admin/media`: the content is sniffed by its magic bytes (the declared type is not trusted). Images are JPEG,
/// PNG or WebP, ≤ 10 MB and ≤ 40 MP, re-encoded to WebP with the longest edge ≤ 2400 px (metadata dropped); SVG and
/// everything else is refused. PDFs start with `%PDF`, ≤ 20 MB. Storage keys are generated here. If the row cannot be
/// saved the stored file is deleted again (no orphan files).
public sealed record UploadMediaCommand(string Kind, long Length, Stream Content, string? AltAr, string? AltEn) : ICommand<MediaAssetResponse>;

internal sealed class UploadMediaCommandHandler(
    IGenericRepository<MediaAsset> media,
    IFileStorage storage,
    IImageProcessor images,
    IFileUrls urls,
    ICurrentActor actor,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UploadMediaCommand, MediaAssetResponse>
{
    public const long MaxImageBytes = 10L * 1024 * 1024;
    public const long MaxPdfBytes = 20L * 1024 * 1024;
    public const long MaxImagePixels = 40_000_000;
    public const int MaxImageEdge = 2400;

    public async Task<Result<MediaAssetResponse>> HandleAsync(UploadMediaCommand command, CancellationToken ct = default)
    {
        var kind = EnumWire.Parse<MediaKind>(command.Kind);
        var limit = kind == MediaKind.Image ? MaxImageBytes : MaxPdfBytes;
        if (command.Length > limit)
        {
            return MediaErrors.TooLarge(limit);
        }

        if (kind == MediaKind.Image && string.IsNullOrWhiteSpace(command.AltAr))
        {
            return MediaErrors.AltRequired;
        }

        var bytes = await ReadAtMostAsync(command.Content, limit, ct);
        if (bytes is null)
        {
            return MediaErrors.TooLarge(limit);
        }

        var now = clock.UtcNow;
        var id = Guid.CreateVersion7();
        string key, mime;
        byte[] stored;
        int? width = null, height = null;
        if (kind == MediaKind.Image)
        {
            if (FileSignatures.Sniff(bytes) is not (FileSignature.Jpeg or FileSignature.Png or FileSignature.Webp))
            {
                return MediaErrors.TypeNotAllowed;
            }

            var processed = images.ToWebp(bytes, MaxImageEdge, MaxImagePixels);
            if (processed.Status == ImageProcessStatus.TooManyPixels)
            {
                return MediaErrors.TooLarge(MaxImagePixels);
            }

            if (processed.Image is not { } image)
            {
                return MediaErrors.TypeNotAllowed;
            }

            (key, mime, stored, width, height) = ($"images/{now:yyyy}/{id:N}.webp", "image/webp", image.Bytes, image.Width, image.Height);
        }
        else
        {
            if (FileSignatures.Sniff(bytes) != FileSignature.Pdf)
            {
                return MediaErrors.TypeNotAllowed;
            }

            (key, mime, stored) = ($"pdf/{now:yyyy}/{id:N}.pdf", "application/pdf", bytes);
        }

        await using (var content = new MemoryStream(stored, writable: false))
        {
            await storage.SaveAsync(key, isPublic: true, content, ct);
        }

        var asset = new MediaAsset(kind, key, mime, stored.LongLength, width, height, command.AltAr, command.AltEn, actor.Name, now);
        try
        {
            media.Add(asset);
            await uow.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(key, isPublic: true, CancellationToken.None);
            throw;
        }

        return MediaMapping.ToResponse(asset, urls);
    }

    /// The whole upload, or null when it is longer than <paramref name="limit"/> (a lying Content-Length).
    private static async Task<byte[]?> ReadAtMostAsync(Stream content, long limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

internal sealed class UploadMediaCommandValidator : AbstractValidator<UploadMediaCommand>
{
    public UploadMediaCommandValidator()
    {
        RuleFor(c => c.Kind).Must(k => k is "image" or "pdf").WithMessage("kind must be image or pdf.");
        RuleFor(c => c.Length).GreaterThan(0).WithMessage("file is required.");
        RuleFor(c => c.AltAr).MaximumLength(300);
        RuleFor(c => c.AltEn).MaximumLength(300);
    }
}
