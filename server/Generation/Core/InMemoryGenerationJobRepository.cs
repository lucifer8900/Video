namespace Lingmai.RedMist.Generation;

public sealed class InMemoryGenerationJobRepository : IGenerationJobRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GenerationJob> _jobsById = [];
    private readonly Dictionary<string, Guid> _jobIdsByIdempotencyKey =
        new(StringComparer.Ordinal);

    public Task<GenerationJob> CreateOrGetAsync(
        GenerationJobSubmission submission,
        Guid jobId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_jobIdsByIdempotencyKey.TryGetValue(submission.IdempotencyKey, out Guid existingId))
                return Task.FromResult(_jobsById[existingId]);

            if (_jobsById.ContainsKey(jobId))
                throw new InvalidOperationException($"Generation job ID '{jobId}' already exists.");

            GenerationJob candidate = GenerationJob.Create(jobId, submission, nowUtc);
            _jobsById.Add(jobId, candidate);
            _jobIdsByIdempotencyKey.Add(candidate.IdempotencyKey, candidate.Id);
            return Task.FromResult(candidate);
        }
    }

    public Task<GenerationJob?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _jobsById.TryGetValue(jobId, out GenerationJob? job);
            return Task.FromResult(job);
        }
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_jobsById.Count);
    }

    public Task<GenerationJob> TransitionAsync(
        Guid jobId,
        long expectedVersion,
        GenerationJobStatus targetStatus,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            GenerationJob current = GetRequired(jobId);
            EnsureVersion(current, expectedVersion);
            GenerationJobStateMachine.EnsureCanTransition(current.Status, targetStatus);
            GenerationJob updated = current with
            {
                Status = targetStatus,
                FailureCode = FailureCodeFor(current.Status, targetStatus),
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Version = checked(current.Version + 1),
            };
            _jobsById[jobId] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<GenerationJobLease?> TryAcquireNextAsync(
        string workerId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateLeaseArguments(workerId, leaseDuration);

        lock (_gate)
        {
            GenerationJob? candidate = _jobsById.Values
                .Where(IsRecoverable)
                .Where(job => job.LeaseOwner is null || job.LeaseExpiresAtUtc <= nowUtc)
                .OrderBy(job => job.CreatedAtUtc)
                .ThenBy(job => job.Id)
                .FirstOrDefault();
            if (candidate is null) return Task.FromResult<GenerationJobLease?>(null);

            DateTimeOffset expiresAtUtc = nowUtc + leaseDuration;
            GenerationJob leased = candidate with
            {
                LeaseOwner = workerId,
                LeaseExpiresAtUtc = expiresAtUtc,
                AttemptCount = checked(candidate.AttemptCount + 1),
                UpdatedAtUtc = nowUtc,
                Version = checked(candidate.Version + 1),
            };
            _jobsById[candidate.Id] = leased;
            return Task.FromResult<GenerationJobLease?>(
                new GenerationJobLease(leased));
        }
    }

    private GenerationJob GetRequired(Guid jobId) =>
        _jobsById.TryGetValue(jobId, out GenerationJob? job)
            ? job
            : throw new GenerationJobNotFoundException(jobId);

    private static void EnsureVersion(GenerationJob job, long expectedVersion)
    {
        if (job.Version != expectedVersion)
            throw new GenerationJobConcurrencyException(job.Id, expectedVersion, job.Version);
    }

    private static bool IsRecoverable(GenerationJob job) =>
        job.Status is
            GenerationJobStatus.Queued or
            GenerationJobStatus.Generating or
            GenerationJobStatus.Moderating or
            GenerationJobStatus.Transcoding;

    private static string? FailureCodeFor(
        GenerationJobStatus currentStatus,
        GenerationJobStatus targetStatus) => targetStatus == GenerationJobStatus.Failed
        ? currentStatus == GenerationJobStatus.Moderating
            ? "moderation.rejected"
            : "generation.failed"
        : null;

    private static void ValidateLeaseArguments(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID is required.", nameof(workerId));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }
}
