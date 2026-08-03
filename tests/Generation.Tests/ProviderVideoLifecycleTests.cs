using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderVideoLifecycleTests
{
    [Fact]
    public async Task MockCompletesStartPollPollAndDownloadWorkflow()
    {
        var provider = new MockVideoGenerationProvider();

        ProviderOperation started = await provider.StartAsync(
            ProviderTestRequests.Video(),
            CancellationToken.None);
        ProviderOperation running = await provider.GetOperationAsync(
            started.OperationId,
            CancellationToken.None);
        ProviderOperation completed = await provider.GetOperationAsync(
            started.OperationId,
            CancellationToken.None);

        Assert.Equal(ProviderOperationStatus.Pending, started.Status);
        Assert.Equal(ProviderOperationStatus.Running, running.Status);
        Assert.Equal(ProviderOperationStatus.Succeeded, completed.Status);
        ProviderArtifact artifact = Assert.IsType<ProviderArtifact>(completed.Artifact);
        await using var destination = new MemoryStream();
        await provider.DownloadAsync(artifact, destination, CancellationToken.None);
        Assert.Equal(artifact.Length, destination.Length);
        Assert.NotEmpty(destination.ToArray());
    }

    [Fact]
    public async Task MockCancellationIsIdempotentAndPreventsArtifactDownload()
    {
        var provider = new MockVideoGenerationProvider();
        ProviderOperation started = await provider.StartAsync(
            ProviderTestRequests.Video(),
            CancellationToken.None);

        await provider.CancelAsync(started.OperationId, CancellationToken.None);
        await provider.CancelAsync(started.OperationId, CancellationToken.None);
        ProviderOperation cancelled = await provider.GetOperationAsync(
            started.OperationId,
            CancellationToken.None);

        Assert.Equal(ProviderOperationStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Artifact);
    }

    [Fact]
    public async Task UnknownOperationIsMappedToAStableDomainError()
    {
        var provider = new MockVideoGenerationProvider();

        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(
            () => provider.GetOperationAsync("unknown-operation", CancellationToken.None));

        Assert.Equal(GenerationProviderErrorCodes.OperationNotFound, exception.Code);
        Assert.False(exception.IsTransient);
    }
}
