namespace Lingmai.RedMist.Generation;

public interface IGenerationJobRepository
{
    Task<GenerationJob> CreateOrGetAsync(
        GenerationJobSubmission submission,
        Guid jobId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<GenerationJob?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    Task<GenerationJob> TransitionAsync(
        Guid jobId,
        long expectedVersion,
        GenerationJobStatus targetStatus,
        CancellationToken cancellationToken);

    Task<GenerationJobLease?> TryAcquireNextAsync(
        string workerId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

}
