using System.Buffers;
using System.Security.Cryptography;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class MediaContentHasher
{
    private readonly int _bufferSizeBytes;

    public MediaContentHasher(int bufferSizeBytes = 81_920)
    {
        if (bufferSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSizeBytes));
        _bufferSizeBytes = bufferSizeBytes;
    }

    public async Task<string> ComputeSha256Async(
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
            throw new ArgumentException("The media stream must be readable.", nameof(content));
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSizeBytes);
        try
        {
            using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                int read = await content.ReadAsync(
                    buffer.AsMemory(0, _bufferSizeBytes),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                hasher.AppendData(buffer, 0, read);
            }

            return "sha256:" + Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, _bufferSizeBytes));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task EnsureMatchesSha256Async(
        Stream content,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeSha256(expectedHash, out byte[]? expectedBytes))
            throw new ArgumentException("Expected hash must be a lowercase prefixed SHA-256 value.", nameof(expectedHash));

        string actualHash = await ComputeSha256Async(content, cancellationToken).ConfigureAwait(false);
        _ = TryDecodeSha256(actualHash, out byte[]? actualBytes);
        bool matches = CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes!);
        CryptographicOperations.ZeroMemory(expectedBytes);
        CryptographicOperations.ZeroMemory(actualBytes!);
        if (!matches)
            throw new MediaIntegrityException(
                "media.hash_mismatch",
                "The media content did not match its expected integrity hash.");
    }

    private static bool TryDecodeSha256(string? value, out byte[]? bytes)
    {
        bytes = null;
        if (value is null || value.Length != 71 ||
            !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        string hex = value[7..];
        if (hex.Any(character => character is >= 'A' and <= 'F')) return false;
        try
        {
            bytes = Convert.FromHexString(hex);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class MediaIntegrityException : Exception
{
    public MediaIntegrityException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
