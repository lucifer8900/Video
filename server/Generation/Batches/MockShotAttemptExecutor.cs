using System.Security.Cryptography;
using System.Text;
using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Batches;

public sealed class MockShotAttemptExecutor : IGenerationBatchAttemptExecutor
{
    private readonly IGenerationJobRepository _jobRepository;
    private readonly GenerationJobService _jobService;
    private readonly MockVideoGenerationProvider _provider;

    public MockShotAttemptExecutor(
        IGenerationJobRepository jobRepository,
        MockVideoGenerationProvider provider,
        TimeProvider timeProvider)
    {
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _jobService = new GenerationJobService(
            jobRepository,
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)));
    }

    public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

    public async Task<GenerationBatchAttemptReceipt> ExecuteAsync(
        GenerationBatchAttemptRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalRequest(request)));
        string digestHex = Convert.ToHexString(digest).ToLowerInvariant();
        string inputHash = "sha256:" + digestHex;
        var submission = new GenerationJobSubmission(
            "cx502.attempt." + digestHex,
            inputHash);
        GenerationJob job = await _jobService.SubmitAndQueueAsync(
            submission,
            cancellationToken).ConfigureAwait(false);
        if (job.Status == GenerationJobStatus.Queued)
        {
            job = await _jobRepository.TransitionAsync(
                job.Id,
                job.Version,
                GenerationJobStatus.Generating,
                cancellationToken).ConfigureAwait(false);
        }

        var providerRequest = new VideoGenerationRequest
        {
            JobId = job.Id,
            Prompt = "TEST_ONLY_CX502:" + inputHash,
            InputHash = inputHash,
            Seed = BitConverter.ToInt64(digest, 0),
            AspectRatio = request.AspectRatio,
            DurationSeconds = checked((request.TargetDurationMilliseconds + 999) / 1_000),
        };
        ProviderOperation operation = await CompleteMockOperationAsync(
            providerRequest,
            cancellationToken).ConfigureAwait(false);
        ProviderArtifact artifact = operation.Artifact
            ?? throw new GenerationBatchGateException(
                "batch.mock_artifact_missing",
                "The deterministic mock operation completed without an artifact.");

        job = await AdvanceToReadyAsync(job, cancellationToken).ConfigureAwait(false);
        return new GenerationBatchAttemptReceipt(
            job.Id,
            new GenerationBatchArtifact(
                artifact.ArtifactId,
                SemanticMediaType(artifact.ContentType),
                artifact.Sha256),
            request.TargetDurationMilliseconds,
            ActualCostMicros: 0,
            CostSettled: true);
    }

    private async Task<ProviderOperation> CompleteMockOperationAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ProviderOperation operation = await _provider.StartAsync(request, cancellationToken)
            .ConfigureAwait(false);
        for (int poll = 0; poll < 4 &&
             operation.Status is ProviderOperationStatus.Pending or ProviderOperationStatus.Running;
             poll++)
        {
            operation = await _provider.GetOperationAsync(
                operation.OperationId,
                cancellationToken).ConfigureAwait(false);
        }

        if (operation.Status != ProviderOperationStatus.Succeeded)
        {
            throw new GenerationBatchGateException(
                "batch.mock_operation_failed",
                "The deterministic mock operation did not succeed.");
        }

        return operation;
    }

    private async Task<GenerationJob> AdvanceToReadyAsync(
        GenerationJob job,
        CancellationToken cancellationToken)
    {
        while (job.Status != GenerationJobStatus.Ready)
        {
            GenerationJobStatus next = job.Status switch
            {
                GenerationJobStatus.Generating => GenerationJobStatus.Moderating,
                GenerationJobStatus.Moderating => GenerationJobStatus.Transcoding,
                GenerationJobStatus.Transcoding => GenerationJobStatus.Ready,
                _ => throw new GenerationBatchGateException(
                    "batch.generation_job_state",
                    "The generation job cannot advance through the mock pipeline."),
            };
            job = await _jobRepository.TransitionAsync(
                job.Id,
                job.Version,
                next,
                cancellationToken).ConfigureAwait(false);
        }

        return job;
    }

    private static void Validate(GenerationBatchAttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BatchId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ShotId) ||
            string.IsNullOrWhiteSpace(request.InputHash) ||
            request.AttemptNumber < 1 ||
            request.TargetDurationMilliseconds < 1 ||
            string.IsNullOrWhiteSpace(request.AspectRatio) ||
            !string.Equals(
                request.DialogueBinding,
                "bound_to_primary_media",
                StringComparison.Ordinal) ||
            request.FirstFrame is null ||
            request.LastFrame is null ||
            request.PrimaryMedia is null ||
            request.FallbackMedia is null)
        {
            throw new GenerationBatchValidationException(
                "batch.attempt_request",
                "The batch attempt request is invalid.");
        }
    }

    private static string CanonicalRequest(GenerationBatchAttemptRequest request) =>
        string.Join(
            "\n",
            "cx502.mock-attempt.v1",
            request.BatchId.ToString("D"),
            request.ShotId,
            request.InputHash,
            request.Tier.ToString(),
            request.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.TargetDurationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.AspectRatio,
            request.DialogueBinding,
            CanonicalFrame(request.FirstFrame!),
            CanonicalFrame(request.LastFrame!),
            CanonicalMedia(request.PrimaryMedia!),
            CanonicalMedia(request.FallbackMedia));

    private static string CanonicalFrame(GenerationBatchFramePin frame) =>
        string.Join(
            ":",
            frame.MediaRef,
            frame.AssetVersion,
            frame.ContentHash,
            frame.MediaType,
            frame.Origin);

    private static string CanonicalMedia(GenerationBatchMediaPin media) =>
        string.Join(
            ":",
            media.MediaRef,
            media.AssetVersion,
            media.ContentHash,
            media.MediaType);

    private static string SemanticMediaType(string contentType) =>
        contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            ? "video"
            : throw new GenerationBatchGateException(
                "batch.mock_artifact_media_type",
                "The deterministic mock provider returned a non-video artifact.");
}
