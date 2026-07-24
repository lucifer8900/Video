using System.Buffers.Binary;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ReferenceImageInspector
{
    public static DownloadedImage Inspect(byte[] bytes, string? declaredMediaType)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            if (bytes.Length < 24)
            {
                throw new InvalidDataException("PNG payload is missing its IHDR dimensions.");
            }

            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            return Validated(bytes, "image/png", ".png", width, height);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            var (width, height) = ReadJpegDimensions(bytes);
            return Validated(bytes, "image/jpeg", ".jpg", width, height);
        }

        if (bytes.Length >= 12 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            var (width, height) = ReadWebpDimensions(bytes);
            return Validated(bytes, "image/webp", ".webp", width, height);
        }

        throw new InvalidDataException($"Unsupported or invalid image payload ({declaredMediaType ?? "unknown"}).");
    }

    private static DownloadedImage Validated(
        byte[] bytes,
        string mediaType,
        string extension,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"{mediaType} payload has invalid pixel dimensions {width}x{height}.");
        }

        return new(bytes, mediaType, extension, width, height);
    }

    private static (int Width, int Height) ReadJpegDimensions(byte[] bytes)
    {
        var offset = 2;
        while (offset + 3 < bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] != 0xFF) offset++;
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) break;

            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 1 >= bytes.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) break;
            if (IsStartOfFrame(marker) && length >= 7)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2));
                return (width, height);
            }

            offset += length;
        }

        throw new InvalidDataException("JPEG payload has no supported start-of-frame dimensions.");
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static (int Width, int Height) ReadWebpDimensions(byte[] bytes)
    {
        if (bytes.Length >= 30 && bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8))
        {
            var width = 1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16);
            var height = 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16);
            return (width, height);
        }

        if (bytes.Length >= 25 && bytes.AsSpan(12, 4).SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
        {
            var width = 1 + bytes[21] + ((bytes[22] & 0x3F) << 8);
            var height = 1 + (bytes[22] >> 6) + (bytes[23] << 2) + ((bytes[24] & 0x0F) << 10);
            return (width, height);
        }

        if (bytes.Length >= 30 && bytes.AsSpan(12, 4).SequenceEqual("VP8 "u8) &&
            bytes.AsSpan(23, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26, 2)) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2)) & 0x3FFF;
            return (width, height);
        }

        throw new InvalidDataException("WEBP payload has no supported dimension header.");
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
