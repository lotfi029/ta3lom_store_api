using SkiaSharp;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Files;

/// Reads the header first and refuses images above the pixel limit before any pixel is decoded (decompression bombs),
/// then re-encodes as WebP (quality 85) with the longest edge scaled down to the limit. Only pixels are written, so EXIF
/// and other metadata (GPS, device) are dropped.
internal sealed class SkiaImageProcessor : IImageProcessor
{
    public ImageProcessResult ToWebp(ReadOnlySpan<byte> input, int maxEdge, long maxPixels)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return new(ImageProcessStatus.NotAnImage, null);
        }

        if ((long)codec.Info.Width * codec.Info.Height > maxPixels)
        {
            return new(ImageProcessStatus.TooManyPixels, null);
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return new(ImageProcessStatus.NotAnImage, null);
        }

        var bitmap = decoded;
        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(decoded.Width, decoded.Height));
        SKBitmap? resized = null;
        if (scale < 1.0)
        {
            var info = new SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale)));
            resized = decoded.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
            bitmap = resized ?? decoded;
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
            return new(ImageProcessStatus.Ok, new ProcessedImage(encoded.ToArray(), bitmap.Width, bitmap.Height));
        }
        finally
        {
            resized?.Dispose();
        }
    }
}
