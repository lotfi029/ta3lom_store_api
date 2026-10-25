using SkiaSharp;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Services.Files;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// A-5: phone photos carry the camera orientation in EXIF; the stored WebP has it applied (and no EXIF at all).
public sealed class ImageOrientationTests
{
    /// A 40×20 JPEG, left half red and right half blue, with an EXIF APP1 segment saying Orientation = <paramref name="orientation"/>.
    private static byte[] JpegWithOrientation(ushort orientation)
    {
        using var bitmap = new SKBitmap(40, 20);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 20, 20, red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();

        // "Exif\0\0" + big-endian TIFF header + one IFD entry: 0x0112 Orientation, SHORT, count 1, value.
        byte[] tiff = [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0, 0, 0, 0, 0];
        byte[] payload = [.. "Exif\0\0"u8.ToArray(), .. tiff];
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [jpeg[0], jpeg[1], .. app1, .. jpeg[2..]];
    }

    [Fact]
    public void The_input_really_carries_the_orientation()
    {
        using var codec = SKCodec.Create(new MemoryStream(JpegWithOrientation(6)));

        codec.EncodedOrigin.ShouldBe(SKEncodedOrigin.RightTop);
    }

    [Theory]
    [InlineData((ushort)6, 20, 40, true)]    // rotate 90° clockwise: the left (red) half ends on top
    [InlineData((ushort)8, 20, 40, false)]   // rotate 90° counter-clockwise: the left (red) half ends at the bottom
    [InlineData((ushort)3, 40, 20, false)]   // rotate 180°: red ends on the right
    [InlineData((ushort)1, 40, 20, true)]    // as stored
    public void The_orientation_is_applied_before_the_image_is_stored(ushort orientation, int width, int height, bool redFirst)
    {
        var result = new SkiaImageProcessor().ToWebp(JpegWithOrientation(orientation), 2400, 40_000_000);

        result.Status.ShouldBe(ImageProcessStatus.Ok);
        result.Image!.Width.ShouldBe(width);
        result.Image.Height.ShouldBe(height);
        using var output = SKBitmap.Decode(result.Image.Bytes);
        (output.Width, output.Height).ShouldBe((width, height));
        var first = output.GetPixel(2, 2);
        var last = output.GetPixel(width - 3, height - 3);
        (first.Red > first.Blue).ShouldBe(redFirst);
        (last.Red > last.Blue).ShouldBe(!redFirst);
        using var codec = SKCodec.Create(new MemoryStream(result.Image.Bytes));
        codec.EncodedOrigin.ShouldBe(SKEncodedOrigin.TopLeft);
    }
}
