using System.Net;
using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class MediaObjectStorageTests
{
    [Fact]
    public async Task MockStorageReusesSameImmutableKeyAndHash()
    {
        byte[] bytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        string hash = DistributionTestHash.Sha256(bytes);
        var storage = new MockObjectStorage(maxObjectBytes: 8192);
        const string objectKey = "media/sha256/ab/immutable-video.mp4";

        ObjectStorageWriteResult first = await storage.UploadAsync(
            Upload(objectKey, hash, bytes),
            CancellationToken.None);
        ObjectStorageWriteResult replay = await storage.UploadAsync(
            Upload(objectKey, hash, bytes),
            CancellationToken.None);

        Assert.False(first.Reused);
        Assert.True(replay.Reused);
        Assert.Equal(objectKey, replay.ObjectKey);
        Assert.Equal(hash, replay.ContentHash);
        Assert.Equal(1, storage.ObjectCount);
    }

    [Fact]
    public async Task SameImmutableKeyWithDifferentHashIsRejected()
    {
        var storage = new MockObjectStorage(maxObjectBytes: 8192);
        const string objectKey = "media/sha256/ab/immutable-video.mp4";
        byte[] firstBytes = [1, 2, 3];
        byte[] secondBytes = [4, 5, 6];
        await storage.UploadAsync(
            Upload(objectKey, DistributionTestHash.Sha256(firstBytes), firstBytes),
            CancellationToken.None);

        MediaObjectConflictException error = await Assert.ThrowsAsync<MediaObjectConflictException>(
            () => storage.UploadAsync(
                Upload(objectKey, DistributionTestHash.Sha256(secondBytes), secondBytes),
                CancellationToken.None));

        Assert.Equal("media.object_hash_conflict", error.Code);
        Assert.Equal(1, storage.ObjectCount);
    }

    [Theory]
    [InlineData("../escape.mp4")]
    [InlineData("/absolute/video.mp4")]
    [InlineData("media\\windows-path.mp4")]
    [InlineData("media/video.mp4?X-Goog-Signature=secret")]
    [InlineData("media//empty-segment.mp4")]
    public async Task UnsafeObjectKeysAreRejectedBeforeReading(string objectKey)
    {
        var storage = new MockObjectStorage(maxObjectBytes: 8192);
        byte[] bytes = [1, 2, 3];
        using var content = new GuardedReadStream(bytes, maximumReadRequest: 4096);
        var upload = new ObjectStorageUpload(
            objectKey,
            DistributionTestHash.Sha256(bytes),
            "video/mp4",
            bytes.Length,
            content);

        MediaPipelineException error = await Assert.ThrowsAsync<MediaPipelineException>(
            () => storage.UploadAsync(upload, CancellationToken.None));

        Assert.Equal("media.object_key_invalid", error.Code);
        Assert.Equal(0, storage.ObjectCount);
    }

    [Fact]
    public async Task GcsUploadUsesFakeHandlerAndStreamsCallerContent()
    {
        byte[] bytes = Enumerable.Range(0, 128 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var handler = new RecordingGcsHandler();
        using var client = new HttpClient(handler);
        var storage = new GcsObjectStorage(
            client,
            new GcsObjectStorageOptions
            {
                BucketName = "redmist-test",
                MaxObjectBytes = 256 * 1024,
            },
            new StaticGcsAccessTokenSource("unit-test-access-token"));
        const string objectKey = "media/sha256/ab/immutable-video.mp4";
        await using var content = new GuardedReadStream(bytes, maximumReadRequest: 128 * 1024);

        ObjectStorageWriteResult result = await storage.UploadAsync(
            new ObjectStorageUpload(
                objectKey,
                DistributionTestHash.Sha256(bytes),
                "video/mp4",
                bytes.Length,
                content),
            CancellationToken.None);

        Assert.False(result.Reused);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("storage.googleapis.com", handler.RequestUri!.Host);
        Assert.Equal("https", handler.RequestUri.Scheme);
        Assert.Contains("uploadType=media", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("ifGenerationMatch=0", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("name=media%2Fsha256%2Fab%2Fimmutable-video.mp4", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal("unit-test-access-token", handler.Authorization.Parameter);
        Assert.Equal("video/mp4", handler.ContentType);
        Assert.Equal(bytes, handler.UploadedBytes);
    }

    [Fact]
    public async Task GcsFailureIsSanitizedWithoutVendorBodyOrRequestQuery()
    {
        const string secret = "signed-or-token-secret";
        var handler = new RecordingGcsHandler(
            HttpStatusCode.InternalServerError,
            $"vendor echoed token={secret}");
        using var client = new HttpClient(handler);
        var storage = new GcsObjectStorage(
            client,
            new GcsObjectStorageOptions
            {
                BucketName = "redmist-test",
                MaxObjectBytes = 1024,
            },
            new StaticGcsAccessTokenSource(secret));
        byte[] bytes = [1, 2, 3];

        MediaPipelineException error = await Assert.ThrowsAsync<MediaPipelineException>(
            () => storage.UploadAsync(
                Upload("media/sha256/ab/video.mp4", DistributionTestHash.Sha256(bytes), bytes),
                CancellationToken.None));

        string rendered = error.ToString();
        Assert.Equal("media.storage_unavailable", error.Code);
        Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("uploadType", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vendor echoed", rendered, StringComparison.OrdinalIgnoreCase);
    }

    private static ObjectStorageUpload Upload(
        string objectKey,
        string contentHash,
        byte[] bytes) =>
        new(
            objectKey,
            contentHash,
            "video/mp4",
            bytes.Length,
            new MemoryStream(bytes, writable: false));
}
