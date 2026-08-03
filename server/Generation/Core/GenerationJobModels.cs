namespace Lingmai.RedMist.Generation;

public sealed record GenerationJobSubmission(string IdempotencyKey, string InputHash);

public sealed record GenerationJob
{
    public required Guid Id { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string InputHash { get; init; }

    public required GenerationJobStatus Status { get; init; }

    public string? FailureCode { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public long Version { get; init; }

    public int AttemptCount { get; init; }

    public string? LeaseOwner { get; init; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    public static GenerationJob Create(
        Guid id,
        GenerationJobSubmission submission,
        DateTimeOffset nowUtc) =>
        new()
        {
            Id = id,
            IdempotencyKey = submission.IdempotencyKey,
            InputHash = submission.InputHash,
            Status = GenerationJobStatus.Created,
            FailureCode = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Version = 0,
            AttemptCount = 0,
        };
}

public sealed record GenerationJobLease(GenerationJob Job);

public sealed class GenerationJobNotFoundException : InvalidOperationException
{
    public GenerationJobNotFoundException(Guid jobId)
        : base($"Generation job '{jobId}' was not found.") => JobId = jobId;

    public Guid JobId { get; }
}

public sealed class GenerationJobConcurrencyException : InvalidOperationException
{
    public GenerationJobConcurrencyException(Guid jobId, long expectedVersion, long actualVersion)
        : base(
            $"Generation job '{jobId}' has version {actualVersion}; expected {expectedVersion}.")
    {
        JobId = jobId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public Guid JobId { get; }

    public long ExpectedVersion { get; }

    public long ActualVersion { get; }
}

public sealed class GenerationJobLeaseConflictException : InvalidOperationException
{
    public GenerationJobLeaseConflictException(Guid jobId)
        : base($"Generation job '{jobId}' is owned by another or expired lease.") => JobId = jobId;

    public Guid JobId { get; }
}
