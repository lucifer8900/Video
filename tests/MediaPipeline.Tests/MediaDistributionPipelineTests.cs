using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class MediaDistributionPipelineTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GeneratesImmutableContentAddressedKeyAndStreamsUpload()
    {
        byte[] bytes = Enumerable.Range(0, 64 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        string expectedHash = DistributionTestHash.Sha256(bytes);
        using DistributionTempFile temporary = DistributionTempFile.Create(bytes);
        string temporaryPath = temporary.Path;
        var storage = new RecordingObjectStorage();
        var signer = new RecordingMediaUrlSigner();
        var pipeline = CreatePipeline(storage, signer, maxInputBytes: 128 * 1024);

        MediaDistributionResult result = await pipeline.DistributeAsync(
            new MediaDistributionRequest(temporaryPath, "video/mp4", ".mp4"),
            CancellationToken.None);

        string hashHex = expectedHash["sha256:".Length..];
        string expectedKey = $"media/sha256/{hashHex[..2]}/{hashHex}.mp4";
        Assert.Equal(expectedHash, result.ContentHash);
        Assert.Equal(expectedKey, result.ObjectKey);
        Assert.Equal(expectedKey, storage.LastUpload!.ObjectKey);
        Assert.Equal(bytes.Length, storage.LastUpload.ContentLength);
        Assert.Equal(bytes, storage.UploadedBytes);
        Assert.True(storage.LargestReadRequest <= 4096);
        Assert.Equal(expectedKey, signer.LastObjectKey);
        Assert.Equal(TimeSpan.FromMinutes(5), signer.LastTtl);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task SameContentReusesSingleStoredObject()
    {
        byte[] bytes = [8, 6, 7, 5, 3, 0, 9];
        using DistributionTempFile firstFile = DistributionTempFile.Create(bytes);
        using DistributionTempFile replayFile = DistributionTempFile.Create(bytes);
        var storage = new MockObjectStorage(maxObjectBytes: 1024);
        var signer = new RecordingMediaUrlSigner();
        var pipeline = CreatePipeline(storage, signer, maxInputBytes: 1024);

        MediaDistributionResult first = await pipeline.DistributeAsync(
            new MediaDistributionRequest(firstFile.Path, "video/mp4", ".mp4"),
            CancellationToken.None);
        MediaDistributionResult replay = await pipeline.DistributeAsync(
            new MediaDistributionRequest(replayFile.Path, "video/mp4", ".mp4"),
            CancellationToken.None);

        Assert.Equal(first.ObjectKey, replay.ObjectKey);
        Assert.Equal(first.ContentHash, replay.ContentHash);
        Assert.False(first.Reused);
        Assert.True(replay.Reused);
        Assert.Equal(1, storage.ObjectCount);
        Assert.Equal(2, signer.CallCount);
    }

    [Fact]
    public async Task UploadFailureNeverRequestsSignedUrlAndCleansTemporaryFile()
    {
        const string secret = "X-Goog-Signature=must-not-escape";
        using DistributionTempFile temporary = DistributionTempFile.Create([1, 2, 3]);
        string temporaryPath = temporary.Path;
        var storage = new RecordingObjectStorage(
            new InvalidOperationException($"vendor failure {secret} path={temporaryPath}"));
        var signer = new RecordingMediaUrlSigner();
        var pipeline = CreatePipeline(storage, signer, maxInputBytes: 1024);

        MediaPipelineException error = await Assert.ThrowsAsync<MediaPipelineException>(
            () => pipeline.DistributeAsync(
                new MediaDistributionRequest(temporaryPath, "video/mp4", ".mp4"),
                CancellationToken.None));

        Assert.Equal("media.distribution_failed", error.Code);
        Assert.Equal(0, signer.CallCount);
        Assert.False(File.Exists(temporaryPath));
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(temporaryPath, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static MediaDistributionPipeline CreatePipeline(
        IObjectStorage storage,
        IMediaUrlSigner signer,
        long maxInputBytes) =>
        new(
            storage,
            signer,
            new FixedTimeProvider(FixedNow),
            new MediaDistributionOptions
            {
                MaxInputBytes = maxInputBytes,
                SignedUrlTtl = TimeSpan.FromMinutes(5),
            });
}
