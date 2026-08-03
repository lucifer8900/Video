using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class MediaPipelineSecurityTests
{
    [Fact]
    public async Task OversizedInputIsRejectedBeforeUploadOrSigningAndIsCleaned()
    {
        using DistributionTempFile temporary = DistributionTempFile.Create(new byte[65]);
        string temporaryPath = temporary.Path;
        var storage = new RecordingObjectStorage();
        var signer = new RecordingMediaUrlSigner();
        var pipeline = CreatePipeline(storage, signer, maxInputBytes: 64);

        MediaPipelineException error = await Assert.ThrowsAsync<MediaPipelineException>(
            () => pipeline.DistributeAsync(
                new MediaDistributionRequest(temporaryPath, "video/mp4", ".mp4"),
                CancellationToken.None));

        Assert.Equal("media.input_too_large", error.Code);
        Assert.Equal(0, storage.UploadCount);
        Assert.Equal(0, signer.CallCount);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task SigningFailureIsSanitizedAndTemporaryFileIsCleaned()
    {
        const string signature = "private-signature-query-value";
        using DistributionTempFile temporary = DistributionTempFile.Create([1, 2, 3, 4]);
        string temporaryPath = temporary.Path;
        var storage = new RecordingObjectStorage();
        var signer = new RecordingMediaUrlSigner(
            new InvalidOperationException(
                $"signer failed https://storage.googleapis.com/x?X-Goog-Signature={signature}"));
        var pipeline = CreatePipeline(storage, signer, maxInputBytes: 64);

        MediaPipelineException error = await Assert.ThrowsAsync<MediaPipelineException>(
            () => pipeline.DistributeAsync(
                new MediaDistributionRequest(temporaryPath, "video/mp4", ".mp4"),
                CancellationToken.None));
        string rendered = error.ToString();

        Assert.Equal("media.distribution_failed", error.Code);
        Assert.Equal(1, storage.UploadCount);
        Assert.Equal(1, signer.CallCount);
        Assert.False(File.Exists(temporaryPath));
        Assert.DoesNotContain(signature, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Goog-", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temporaryPath, rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DistributionResultCannotCarryVideoBytesOrStreams()
    {
        Type[] forbidden = [typeof(byte[]), typeof(Stream), typeof(MemoryStream)];

        Assert.All(
            typeof(MediaDistributionResult).GetProperties(),
            property => Assert.DoesNotContain(property.PropertyType, forbidden));
        Assert.All(
            typeof(MediaDistributionResult).GetFields(),
            field => Assert.DoesNotContain(field.FieldType, forbidden));
    }

    [Fact]
    public async Task ResultToStringDoesNotLeakSignedUrlQuery()
    {
        const string signature = "unit-test-secret";
        using DistributionTempFile temporary = DistributionTempFile.Create([9, 8, 7]);
        var pipeline = CreatePipeline(
            new RecordingObjectStorage(),
            new RecordingMediaUrlSigner(),
            maxInputBytes: 64);

        MediaDistributionResult result = await pipeline.DistributeAsync(
            new MediaDistributionRequest(temporary.Path, "video/mp4", ".mp4"),
            CancellationToken.None);
        string rendered = result.ToString();

        Assert.DoesNotContain(signature, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Goog-", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", rendered, StringComparison.Ordinal);
    }

    private static MediaDistributionPipeline CreatePipeline(
        IObjectStorage storage,
        IMediaUrlSigner signer,
        long maxInputBytes) =>
        new(
            storage,
            signer,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 16, 9, 30, 0, TimeSpan.Zero)),
            new MediaDistributionOptions
            {
                MaxInputBytes = maxInputBytes,
                SignedUrlTtl = TimeSpan.FromMinutes(5),
            });
}
