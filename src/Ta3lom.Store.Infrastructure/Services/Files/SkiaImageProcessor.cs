using SkiaSharp;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Files;

/// Decodes an upload and re-encodes it as WebP (quality 85), scaling the longest edge down to the limit. Only
/// pixels are written, so EXIF and other metadata (GPS, device) are dropped (11 §9.6, audit G12).
internal sealed class SkiaImageProcessor : IImageProcessor
{
    public ProcessedImage? ToWebp(ReadOnlySpan<byte> input, int maxEdge)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        var bitmap = decoded;
        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(decoded.Width, decoded.Height));
        SKBitmap? resized = null;
        if (scale < 1.0)
        {
            resized = decoded.Resize(new SKImageInfo((int)(decoded.Width * scale), (int)(decoded.Height * scale)), new SKSamplingOptions(SKCubicResampler.Mitchell));
            bitmap = resized ?? decoded;
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
            return new ProcessedImage(encoded.ToArray(), bitmap.Width, bitmap.Height);
        }
        finally
        {
            resized?.Dispose();
        }
    }
}
