using Lingmai.RedMist.Generation;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationWorkerRecoveryTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 7, 16, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatedJobsMustBeQueuedBeforeAWorkerCanClaimThem()
    {
        var clock = new ManualTimeProvider(Epoch);
        var repository = new InMemoryGenerationJobRepository();
        var service = new GenerationJobService(repository, clock);
        await service.SubmitAsync(
            new GenerationJobSubmission("created-not-worker-ready", $"sha256:{new string('0', 64)}"),
            CancellationToken.None);

        GenerationJobLease? lease = await repository.TryAcquireNextAsync(
            "worker-a",
            clock.GetUtcNow(),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.Null(lease);
    }

    [Fact]
    public async Task UnexpiredLeaseCannotBeStolen()
    {
        var clock = new ManualTimeProvider(Epoch);
        var repository = new InMemoryGenerationJobRepository();
        GenerationJob job = await CreateAtStatusAsync(
            repository,
            clock,
            "lease-not-expired",
            GenerationJobStatus.Generating);
        GenerationJobLease? firstLease = await repository.TryAcquireNextAsync(
            "worker-a",
            clock.GetUtcNow(),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        var processor = new RecordingStageProcessor(GenerationJobStatus.Moderating);
        var restartedWorker = new GenerationJobWorker(
            repository,
            processor,
            clock,
            new GenerationWorkerOptions { LeaseDuration = TimeSpan.FromMinutes(5) });

        bool processed = await restartedWorker.TryProcessOneAsync("worker-b", CancellationToken.None);

        Assert.NotNull(firstLease);
        Assert.Equal(job.Id, firstLease.Job.Id);
        Assert.False(processed);
        Assert.Empty(processor.ObservedJobs);
    }

    [Fact]
    public async Task ExpiredLeaseIsRecoveredAtItsPersistedStage()
    {
        var clock = new ManualTimeProvider(Epoch);
        var repository = new InMemoryGenerationJobRepository();
        GenerationJob job = await CreateAtStatusAsync(
            repository,
            clock,
            "lease-recovered",
            GenerationJobStatus.Generating);
        GenerationJobLease? abandonedLease = await repository.TryAcquireNextAsync(
            "worker-a",
            clock.GetUtcNow(),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        var processor = new RecordingStageProcessor(GenerationJobStatus.Moderating);
        var restartedWorker = new GenerationJobWorker(
            repository,
            processor,
            clock,
            new GenerationWorkerOptions { LeaseDuration = TimeSpan.FromMinutes(5) });

        bool processed = await restartedWorker.TryProcessOneAsync("worker-b", CancellationToken.None);
        GenerationJob? recoveredOrNull = await repository.GetAsync(job.Id, CancellationToken.None);
        Assert.NotNull(recoveredOrNull);
        GenerationJob recovered = recoveredOrNull!;

        Assert.NotNull(abandonedLease);
        Assert.True(processed);
        GenerationJob observed = Assert.Single(processor.ObservedJobs);
        Assert.Equal(job.Id, observed.Id);
        Assert.Equal(GenerationJobStatus.Generating, observed.Status);
        Assert.Equal(GenerationJobStatus.Moderating, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task RestartDoesNotRepeatDurablyCompletedStages()
    {
        var clock = new ManualTimeProvider(Epoch);
        var repository = new InMemoryGenerationJobRepository();
        GenerationJob job = await CreateAtStatusAsync(
            repository,
            clock,
            "resume-transcoding-only",
            GenerationJobStatus.Transcoding);
        var processor = new RecordingStageProcessor(GenerationJobStatus.Ready);
        var restartedWorker = new GenerationJobWorker(
            repository,
            processor,
            clock,
            new GenerationWorkerOptions { LeaseDuration = TimeSpan.FromMinutes(5) });

        Assert.True(await restartedWorker.TryProcessOneAsync("worker-restarted", CancellationToken.None));
        GenerationJob? completedOrNull = await repository.GetAsync(job.Id, CancellationToken.None);
        Assert.NotNull(completedOrNull);
        GenerationJob completed = completedOrNull!;

        GenerationJob observed = Assert.Single(processor.ObservedJobs);
        Assert.Equal(GenerationJobStatus.Transcoding, observed.Status);
        Assert.Equal(GenerationJobStatus.Ready, completed.Status);
    }

    [Theory]
    [InlineData(GenerationJobStatus.Ready)]
    [InlineData(GenerationJobStatus.Failed)]
    [InlineData(GenerationJobStatus.Expired)]
    public async Task TerminalJobsAreNeverRecovered(GenerationJobStatus terminal)
    {
        var clock = new ManualTimeProvider(Epoch);
        var repository = new InMemoryGenerationJobRepository();
        await CreateAtStatusAsync(repository, clock, $"terminal-{terminal}", terminal);
        var processor = new RecordingStageProcessor(GenerationJobStatus.Ready);
        var worker = new GenerationJobWorker(
            repository,
            processor,
            clock,
            new GenerationWorkerOptions { LeaseDuration = TimeSpan.FromMinutes(5) });

        Assert.False(await worker.TryProcessOneAsync("worker-a", CancellationToken.None));
        Assert.Empty(processor.ObservedJobs);
    }

    private static async Task<GenerationJob> CreateAtStatusAsync(
        InMemoryGenerationJobRepository repository,
        TimeProvider clock,
        string idempotencyKey,
        GenerationJobStatus target)
    {
        var service = new GenerationJobService(repository, clock);
        GenerationJob job = await service.SubmitAsync(
            new GenerationJobSubmission(idempotencyKey, $"sha256:{new string('e', 64)}"),
            CancellationToken.None);

        GenerationJobStatus[] path =
        [
            GenerationJobStatus.Queued,
            GenerationJobStatus.Generating,
            GenerationJobStatus.Moderating,
            GenerationJobStatus.Transcoding,
            GenerationJobStatus.Ready,
        ];
        if (target is GenerationJobStatus.Failed or GenerationJobStatus.Expired)
            return await repository.TransitionAsync(job.Id, job.Version, target, CancellationToken.None);

        foreach (GenerationJobStatus status in path)
        {
            job = await repository.TransitionAsync(job.Id, job.Version, status, CancellationToken.None);
            if (status == target) return job;
        }

        return job;
    }

    private sealed class RecordingStageProcessor(GenerationJobStatus resultStatus)
        : IGenerationStageProcessor
    {
        public List<GenerationJob> ObservedJobs { get; } = [];

        public Task<GenerationJobStatus> ProcessAsync(
            GenerationJob job,
            CancellationToken cancellationToken)
        {
            ObservedJobs.Add(job);
            return Task.FromResult(resultStatus);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
