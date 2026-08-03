using Lingmai.RedMist.Generation;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationJobIdempotencyTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SameKeyAndInputReturnTheOriginalJob()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = new GenerationJobService(repository, new FixedTimeProvider(FixedNow));
        GenerationJobSubmission submission = Submission("idem-same-request", 'a');

        GenerationJob first = await service.SubmitAsync(submission, CancellationToken.None);
        GenerationJob replay = await service.SubmitAsync(submission, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(GenerationJobStatus.Created, replay.Status);
        Assert.Equal(0, replay.Version);
        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SameKeyWithDifferentInputIsAnExplicitConflict()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = new GenerationJobService(repository, new FixedTimeProvider(FixedNow));
        await service.SubmitAsync(
            Submission("idem-conflicting-request", 'b'),
            CancellationToken.None);

        IdempotencyConflictException exception = await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => service.SubmitAsync(
                Submission("idem-conflicting-request", 'c'),
                CancellationToken.None));

        Assert.Equal("idem-conflicting-request", exception.IdempotencyKey);
        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentReplaysCreateExactlyOneJob()
    {
        var repository = new InMemoryGenerationJobRepository();
        var service = new GenerationJobService(repository, new FixedTimeProvider(FixedNow));
        GenerationJobSubmission submission = Submission("idem-64-way-race", 'd');

        Task<GenerationJob>[] requests = Enumerable.Range(0, 64)
            .Select(_ => service.SubmitAsync(submission, CancellationToken.None))
            .ToArray();
        GenerationJob[] results = await Task.WhenAll(requests);

        Assert.Single(results.Select(result => result.Id).Distinct());
        Assert.All(results, result => Assert.Equal(GenerationJobStatus.Created, result.Status));
        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    private static GenerationJobSubmission Submission(string idempotencyKey, char hashDigit) =>
        new(
            idempotencyKey,
            $"sha256:{new string(hashDigit, 64)}");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
