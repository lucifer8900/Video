using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation;

public sealed class GenerationJobService
{
    private static readonly Regex IdempotencyKeyPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly IGenerationJobRepository _repository;
    private readonly TimeProvider _timeProvider;

    public GenerationJobService(
        IGenerationJobRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GenerationJob> SubmitAsync(
        GenerationJobSubmission submission,
        CancellationToken cancellationToken)
    {
        ValidateSubmission(submission);
        GenerationJob stored = await _repository.CreateOrGetAsync(
            submission,
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            cancellationToken);
        if (!string.Equals(stored.InputHash, submission.InputHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException(submission.IdempotencyKey);

        return stored;
    }

    public async Task<GenerationJob> SubmitAndQueueAsync(
        GenerationJobSubmission submission,
        CancellationToken cancellationToken)
    {
        GenerationJob job = await SubmitAsync(submission, cancellationToken).ConfigureAwait(false);
        while (job.Status == GenerationJobStatus.Created)
        {
            try
            {
                return await _repository.TransitionAsync(
                    job.Id,
                    job.Version,
                    GenerationJobStatus.Queued,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (GenerationJobConcurrencyException)
            {
                job = await _repository.GetAsync(job.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new GenerationJobNotFoundException(job.Id);
            }
        }

        return job;
    }

    public Task<GenerationJob?> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _repository.GetAsync(jobId, cancellationToken);

    private static void ValidateSubmission(GenerationJobSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (!IdempotencyKeyPattern.IsMatch(submission.IdempotencyKey ?? string.Empty))
        {
            throw new ArgumentException(
                "Idempotency key must match the public generation request contract.",
                nameof(submission));
        }

        if (!Sha256Pattern.IsMatch(submission.InputHash ?? string.Empty))
            throw new ArgumentException(
                "Input hash must use the sha256: prefix followed by 64 lowercase hexadecimal characters.",
                nameof(submission));
    }
}

public sealed class IdempotencyConflictException : InvalidOperationException
{
    public IdempotencyConflictException(string idempotencyKey)
        : base("The idempotency key was already used for another input.") =>
        IdempotencyKey = idempotencyKey;

    public string IdempotencyKey { get; }
}
