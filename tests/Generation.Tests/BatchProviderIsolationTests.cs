using Lingmai.RedMist.Generation.Batches;
using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BatchProviderIsolationTests
{
    [Fact]
    public async Task MockExecutorUsesExistingGenerationJobLifecycleAtZeroCost()
    {
        var jobs = new InMemoryGenerationJobRepository();
        var executor = new MockShotAttemptExecutor(
            jobs,
            new MockVideoGenerationProvider(),
            TimeProvider.System);
        GenerationBatchAttemptRequest request = AttemptRequest();

        GenerationBatchAttemptReceipt receipt = await executor.ExecuteAsync(
            request,
            CancellationToken.None);
        GenerationJob? job = await jobs.GetAsync(receipt.GenerationJobId, CancellationToken.None);

        Assert.Equal(GenerationBatchProviderMode.Mock, executor.Mode);
        Assert.NotNull(job);
        Assert.Equal(GenerationJobStatus.Ready, job.Status);
        Assert.Equal(0, receipt.ActualCostMicros);
        Assert.True(receipt.CostSettled);
        Assert.Equal("video", receipt.Artifact.MediaType);
        Assert.StartsWith("sha256:", receipt.Artifact.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockExecutorReplayReusesGenerationJobAndArtifact()
    {
        var jobs = new InMemoryGenerationJobRepository();
        var provider = new MockVideoGenerationProvider();
        var executor = new MockShotAttemptExecutor(jobs, provider, TimeProvider.System);
        GenerationBatchAttemptRequest request = AttemptRequest();

        GenerationBatchAttemptReceipt first = await executor.ExecuteAsync(request, CancellationToken.None);
        GenerationBatchAttemptReceipt replay = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(1, await jobs.CountAsync(CancellationToken.None));
    }

    [Fact]
    public void PublicBatchRequestCannotSelectProviderOrClaimG2Approval()
    {
        string[] propertyNames = typeof(StartGenerationBatchRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        string[] forbiddenFragments =
        [
            "Provider",
            "Model",
            "Endpoint",
            "ApiKey",
            "G2",
            "Budget",
            "MaximumCost",
        ];

        Assert.All(
            forbiddenFragments,
            fragment => Assert.DoesNotContain(
                propertyNames,
                name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void PublishedServiceCompositionOnlyAcceptsConcreteOfflineMockExecutor()
    {
        System.Reflection.ConstructorInfo[] publicConstructors = typeof(GenerationBatchService)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        System.Reflection.ConstructorInfo constructor = Assert.Single(publicConstructors);
        Assert.Equal(
            typeof(MockShotAttemptExecutor),
            constructor.GetParameters()[1].ParameterType);
        Assert.DoesNotContain(
            publicConstructors,
            candidate => candidate.GetParameters().Any(
                parameter => parameter.ParameterType == typeof(IGenerationBatchAttemptExecutor)));
    }

    [Fact]
    public async Task RetryCannotBypassMockOnlyGateThroughAServiceSharingTheRepository()
    {
        var repository = new InMemoryGenerationBatchRepository();
        var mockService = new GenerationBatchService(
            repository,
            new ZeroCostRecordingExecutor(GenerationBatchProviderMode.Mock),
            TimeProvider.System);
        GenerationBatch preview = await mockService.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "batch.isolation.preview",
                BatchFixture.Dispatchable(1),
                ["shot.001"]),
            CancellationToken.None);
        var externalExecutor = new ZeroCostRecordingExecutor(GenerationBatchProviderMode.External);
        var externalService = new GenerationBatchService(
            repository,
            externalExecutor,
            TimeProvider.System);

        GenerationBatchGateException exception = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => externalService.ReviewAsync(
                new ReviewGenerationBatchRequest(
                    "review.isolation.retry",
                    preview.Id,
                    [BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Retry)]),
                CancellationToken.None));
        GenerationBatch? unchanged = await repository.GetAsync(preview.Id, CancellationToken.None);

        Assert.Equal("batch.external_provider_g2_blocked", exception.Code);
        Assert.Equal(0, externalExecutor.CallCount);
        Assert.NotNull(unchanged);
        Assert.Equal(GenerationBatchStatus.AwaitingReview, unchanged.Status);
        Assert.Single(unchanged.Items[0].Attempts);
    }

    private static GenerationBatchAttemptRequest AttemptRequest()
    {
        GenerationBatchShot shot = BatchFixture.Dispatchable(1).Shots[0];
        return new GenerationBatchAttemptRequest(
            Guid.Parse("7604718b-2d39-4536-a77e-b3d26d9b2202"),
            "shot.mock.001",
            BatchFixture.Hash("mock.executor.input"),
            GenerationBatchTier.PreviewFast,
            AttemptNumber: 1,
            TargetDurationMilliseconds: 8_000,
            AspectRatio: "16:9",
            shot.DialogueBinding,
            shot.FirstFrame,
            shot.LastFrame,
            shot.PrimaryMedia,
            shot.FallbackMedia);
    }

    private sealed class ZeroCostRecordingExecutor(GenerationBatchProviderMode mode)
        : IGenerationBatchAttemptExecutor
    {
        public GenerationBatchProviderMode Mode { get; } = mode;

        public int CallCount { get; private set; }

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new GenerationBatchAttemptReceipt(
                BatchFixture.DeterministicGuid(request.ShotId + request.AttemptNumber),
                new GenerationBatchArtifact(
                    "mock-artifact-" + request.AttemptNumber,
                    "video",
                    BatchFixture.Hash("mock-artifact:" + request.AttemptNumber)),
                request.TargetDurationMilliseconds,
                ActualCostMicros: 0,
                CostSettled: true));
        }
    }
}
