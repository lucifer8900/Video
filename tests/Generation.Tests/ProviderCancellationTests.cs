using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderCancellationTests
{
    [Fact]
    public async Task AllMocksPreserveCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MockVideoGenerationProvider().StartAsync(
                ProviderTestRequests.Video(),
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MockImageGenerationProvider().GenerateAsync(
                ProviderTestRequests.Image(),
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MockTextGenerationProvider().GenerateAsync(
                ProviderTestRequests.Text(),
                cancellation.Token));
    }

    [Fact]
    public async Task HttpProviderDoesNotTranslateCallerCancellationIntoProviderFailure()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation-aware fake unexpectedly resumed.");
        });
        var provider = new VeoVideoGenerationProvider(
            new HttpClient(handler),
            new VeoProviderOptions
            {
                Endpoint = new Uri("https://veo.test/v1/videos:generate"),
                ApiKey = "test-only-key",
                ModelId = "server-video-model",
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.StartAsync(ProviderTestRequests.Video(), cancellation.Token));

        Assert.Single(handler.Requests);
    }
}
