using System.Security.Cryptography;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class MockObjectStorage : IObjectStorage
{
    private readonly object _gate = new();
    private readonly long _maxObjectBytes;
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    public MockObjectStorage(long maxObjectBytes)
    {
        if (maxObjectBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxObjectBytes));
        _maxObjectBytes = maxObjectBytes;
    }

    public int ObjectCount
    {
        get
        {
            lock (_gate) return _objects.Count;
        }
    }

    public async Task<ObjectStorageWriteResult> UploadAsync(
        ObjectStorageUpload upload,
        CancellationToken cancellationToken)
    {
        MediaDistributionValidation.EnsureUpload(upload, _maxObjectBytes);
        byte[] bytes = await ReadAndVerifyAsync(upload, _maxObjectBytes, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            if (_objects.TryGetValue(upload.ObjectKey, out StoredObject? existing))
            {
                if (!string.Equals(existing.ContentHash, upload.ContentHash, StringComparison.Ordinal))
                    throw new MediaObjectConflictException();

                return new ObjectStorageWriteResult(
                    upload.ObjectKey,
                    upload.ContentHash,
                    Reused: true);
            }

            _objects.Add(upload.ObjectKey, new StoredObject(upload.ContentHash, bytes));
            return new ObjectStorageWriteResult(
                upload.ObjectKey,
                upload.ContentHash,
                Reused: false);
        }
    }

    private static async Task<byte[]> ReadAndVerifyAsync(
        ObjectStorageUpload upload,
        long maxObjectBytes,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream(
            upload.ContentLength <= int.MaxValue ? (int)upload.ContentLength : 0);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await upload.Content
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maxObjectBytes)
            {
                throw new MediaPipelineException(
                    "media.input_too_large",
                    "The media input exceeds the size limit.");
            }

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        if (total != upload.ContentLength)
        {
            throw new MediaPipelineException(
                "media.content_length_mismatch",
                "The media content length did not match its declaration.");
        }

        string actualHash =
            "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actualHash, upload.ContentHash, StringComparison.Ordinal))
        {
            throw new MediaPipelineException(
                "media.content_hash_mismatch",
                "The media content hash did not match its declaration.");
        }

        return destination.ToArray();
    }

    private sealed record StoredObject(string ContentHash, byte[] Bytes);
}
