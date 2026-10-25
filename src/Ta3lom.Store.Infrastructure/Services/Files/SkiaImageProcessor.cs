using SkiaSharp;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Files;

/// Reads the header first and refuses images above the pixel limit before any pixel is decoded (decompression bombs),
/// applies the EXIF orientation (A-5: phone photos), then re-encodes as WebP (quality 85) with the longest edge scaled
/// down to the limit. Only pixels are written, so EXIF and other metadata (GPS, device) are dropped.
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

        using var raw = SKBitmap.Decode(codec);
        if (raw is null)
        {
            return new(ImageProcessStatus.NotAnImage, null);
        }

        using var decoded = Orient(raw, codec.EncodedOrigin);

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

    /// A copy of <paramref name="source"/> drawn upright for its EXIF origin (the 8 rotate/flip cases; width and height
    /// swap for the 90° ones).
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        SKMatrix? matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };
        if (matrix is not { } m)
        {
            return source.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var oriented = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height,
            source.ColorType, source.AlphaType, source.ColorSpace));
        using var canvas = new SKCanvas(oriented);
        canvas.SetMatrix(m);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return oriented;
    }
}
