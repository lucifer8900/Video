using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderDeterministicMockTests
{
    [Fact]
    public async Task VideoMockIsIdenticalAcrossFreshInstances()
    {
        var first = new MockVideoGenerationProvider();
        var second = new MockVideoGenerationProvider();
        VideoGenerationRequest request = ProviderTestRequests.Video();

        ProviderOperation firstStart = await first.StartAsync(request, CancellationToken.None);
        ProviderOperation secondStart = await second.StartAsync(request, CancellationToken.None);
        ProviderOperation firstRunning = await first.GetOperationAsync(
            firstStart.OperationId,
            CancellationToken.None);
        ProviderOperation secondRunning = await second.GetOperationAsync(
            secondStart.OperationId,
            CancellationToken.None);
        ProviderOperation firstDone = await first.GetOperationAsync(
            firstStart.OperationId,
            CancellationToken.None);
        ProviderOperation secondDone = await second.GetOperationAsync(
            secondStart.OperationId,
            CancellationToken.None);

        Assert.Equal(firstStart, secondStart);
        Assert.Equal(firstRunning, secondRunning);
        Assert.Equal(firstDone, secondDone);
        Assert.Equal(ProviderOperationStatus.Succeeded, firstDone.Status);
        Assert.NotNull(firstDone.Artifact);
        await using var firstBytes = new MemoryStream();
        await using var secondBytes = new MemoryStream();
        await first.DownloadAsync(firstDone.Artifact!, firstBytes, CancellationToken.None);
        await second.DownloadAsync(secondDone.Artifact!, secondBytes, CancellationToken.None);
        Assert.Equal(firstBytes.ToArray(), secondBytes.ToArray());
    }

    [Fact]
    public async Task ImageMockIsDeterministicAndSeedSensitive()
    {
        var first = new MockImageGenerationProvider();
        var second = new MockImageGenerationProvider();

        ProviderArtifact firstResult = await first.GenerateAsync(
            ProviderTestRequests.Image(seed: 17),
            CancellationToken.None);
        ProviderArtifact repeated = await second.GenerateAsync(
            ProviderTestRequests.Image(seed: 17),
            CancellationToken.None);
        ProviderArtifact changedSeed = await second.GenerateAsync(
            ProviderTestRequests.Image(seed: 18),
            CancellationToken.None);

        Assert.Equal(firstResult, repeated);
        Assert.NotEqual(firstResult.ArtifactId, changedSeed.ArtifactId);
        Assert.NotEqual(firstResult.Sha256, changedSeed.Sha256);
    }

    [Fact]
    public async Task TextMockProducesByteEquivalentStructuredJson()
    {
        var first = new MockTextGenerationProvider();
        var second = new MockTextGenerationProvider();

        StructuredTextResult firstResult = await first.GenerateAsync(
            ProviderTestRequests.Text(),
            CancellationToken.None);
        StructuredTextResult secondResult = await second.GenerateAsync(
            ProviderTestRequests.Text(),
            CancellationToken.None);

        Assert.Equal(firstResult.Output.GetRawText(), secondResult.Output.GetRawText());
        Assert.Equal(firstResult.OutputHash, secondResult.OutputHash);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, firstResult.Output.ValueKind);
    }
}
