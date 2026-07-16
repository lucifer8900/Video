namespace Lingmai.RedMist.Generation;

public interface IGenerationStageProcessor
{
    Task<GenerationJobStatus> ProcessAsync(
        GenerationJob job,
        CancellationToken cancellationToken);
}

public sealed class GenerationWorkerOptions
{
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class GenerationJobWorker
{
    private readonly IGenerationJobRepository _repository;
    private readonly IGenerationStageProcessor _processor;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;

    public GenerationJobWorker(
        IGenerationJobRepository repository,
        IGenerationStageProcessor processor,
        TimeProvider timeProvider,
        GenerationWorkerOptions options)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        if (options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Lease duration must be positive.");
        _leaseDuration = options.LeaseDuration;
    }

    public async Task<bool> TryProcessOneAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        GenerationJobLease? lease = await _repository.TryAcquireNextAsync(
            workerId,
            _timeProvider.GetUtcNow(),
            _leaseDuration,
            cancellationToken);
        if (lease is null) return false;

        GenerationJobStatus targetStatus = await _processor.ProcessAsync(
            lease.Job,
            cancellationToken);
        DateTimeOffset completedAtUtc = _timeProvider.GetUtcNow();
        if (!string.Equals(lease.Job.LeaseOwner, workerId, StringComparison.Ordinal) ||
            lease.Job.LeaseExpiresAtUtc is null ||
            lease.Job.LeaseExpiresAtUtc <= completedAtUtc)
        {
            throw new GenerationJobLeaseConflictException(lease.Job.Id);
        }

        await _repository.TransitionAsync(
            lease.Job.Id,
            lease.Job.Version,
            targetStatus,
            cancellationToken);
        return true;
    }
}
