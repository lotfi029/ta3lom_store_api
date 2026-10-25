namespace Ta3lom.Store.Application.Features.Media;

public enum FileSignature
{
    Unknown,
    Jpeg,
    Png,
    Webp,
    Pdf,
}

/// Magic-byte sniffing: the first bytes decide the type, never the file name or the declared content type.
public static class FileSignatures
{
    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static FileSignature Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return FileSignature.Jpeg;
        }

        if (head.StartsWith(PngMagic))
        {
            return FileSignature.Png;
        }

        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return FileSignature.Webp;
        }

        return head.StartsWith("%PDF-"u8) ? FileSignature.Pdf : FileSignature.Unknown;
    }
}
