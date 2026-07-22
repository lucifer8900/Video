namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ReferenceImageInspector
{
    public static DownloadedImage Inspect(byte[] bytes, string? declaredMediaType)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new(bytes, "image/png", ".png");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return new(bytes, "image/jpeg", ".jpg");
        }

        if (bytes.Length >= 12 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return new(bytes, "image/webp", ".webp");
        }

        throw new InvalidDataException($"Unsupported or invalid image payload ({declaredMediaType ?? "unknown"}).");
    }

    public static bool MatchesMediaType(Stream stream, string mediaType)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);
        return mediaType switch
        {
            "image/png" => read >= 8 && header[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/webp" => read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };
    }
}
