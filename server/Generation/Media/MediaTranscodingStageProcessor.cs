namespace Lingmai.RedMist.Generation.Media;

public sealed record GenerationMediaResult(
    string ObjectReference,
    string ContentHash,
    string ContentType,
    long Length);

public interface IGenerationMediaPipeline
{
    Task<GenerationMediaResult> ProcessAsync(
        GenerationJob job,
        CancellationToken cancellationToken);
}

public interface IGenerationMediaResultStore
{
    Task PersistAsync(
        Guid jobId,
        GenerationMediaResult result,
        CancellationToken cancellationToken);
}

public sealed class MediaTranscodingStageProcessor : IGenerationStageProcessor
{
    private readonly IGenerationMediaPipeline _pipeline;
    private readonly IGenerationMediaResultStore _resultStore;

    public MediaTranscodingStageProcessor(
        IGenerationMediaPipeline pipeline,
        IGenerationMediaResultStore resultStore)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));
    }

    public async Task<GenerationJobStatus> ProcessAsync(
        GenerationJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Status != GenerationJobStatus.Transcoding)
        {
            throw new InvalidOperationException(
                "The media transcoding stage only accepts transcoding jobs.");
        }

        try
        {
            GenerationMediaResult result = await _pipeline
                .ProcessAsync(job, cancellationToken)
                .ConfigureAwait(false);
            await _resultStore
                .PersistAsync(job.Id, result, cancellationToken)
                .ConfigureAwait(false);
            return GenerationJobStatus.Ready;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return GenerationJobStatus.Failed;
        }
    }
}
