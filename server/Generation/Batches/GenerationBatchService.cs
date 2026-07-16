using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace Lingmai.RedMist.Generation.Batches;

public sealed class GenerationBatchService
{
    public const int MaximumBatchSize = 20;
    private static readonly TimeSpan RunLeaseDuration = TimeSpan.FromMinutes(5);

    private static readonly Regex IdempotencyKeyPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly IGenerationBatchRepository _repository;
    private readonly IGenerationBatchAttemptExecutor _executor;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<GenerationBatchRunKey, Lazy<Task<GenerationBatch>>> _activeRuns = [];

    public GenerationBatchService(
        IGenerationBatchRepository repository,
        MockShotAttemptExecutor executor,
        TimeProvider timeProvider)
        : this(repository, (IGenerationBatchAttemptExecutor)executor, timeProvider)
    {
    }

    internal GenerationBatchService(
        IGenerationBatchRepository repository,
        IGenerationBatchAttemptExecutor executor,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GenerationBatch> StartPreviewAsync(
        StartGenerationBatchRequest request,
        CancellationToken cancellationToken)
    {
        ValidatedStart validated = ValidateStart(request);
        EnsureMockOnly();

        GenerationBatchSubmission submission = new(
            request.IdempotencyKey,
            validated.RequestFingerprint,
            request.Manifest.ManifestId,
            request.Manifest.ContentHash,
            GenerationBatchTier.PreviewFast,
            SourcePreviewBatchId: null,
            request.Manifest.Currency,
            validated.Shots.Select(ToPendingPreviewItem).ToArray());
        GenerationBatchCreateResult stored = await _repository.CreateOrGetAsync(
            submission,
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        GenerationBatch batch = stored.Batch;
        if (batch.Status != GenerationBatchStatus.Running)
            return batch;

        return await RunSingleFlightAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationBatch> ReviewAsync(
        ReviewGenerationBatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(request.IdempotencyKey);
        if (request.BatchId == Guid.Empty)
            throw Validation("batch.id", "A batch ID is required.");
        ValidateReviewItems(request.Reviews);
        if (request.Reviews.Any(review => review.Decision == GenerationBatchReviewDecision.Retry))
            EnsureMockOnly();

        GenerationBatch current = await _repository.GetAsync(request.BatchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Validation("batch.not_found", "The generation batch was not found.");
        string[] reviewParts =
        [
            "cx502.review.v1",
            current.Id.ToString("D"),
            .. request.Reviews
                .OrderBy(review => review.ShotId, StringComparer.Ordinal)
                .Select(review => string.Join(
                    ":",
                    review.ShotId,
                    review.Decision,
                    review.ExpectedAttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    review.ExpectedArtifactHash)),
        ];
        string fingerprint = Hash(string.Join("\n", reviewParts));
        GenerationBatchReviewApplyResult applied = await _repository.ApplyReviewsAsync(
            current.Id,
            current.Version,
            request.IdempotencyKey,
            fingerprint,
            request.Reviews,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        if (applied.Batch.Status != GenerationBatchStatus.Running)
            return applied.Batch;

        return await RunSingleFlightAsync(applied.Batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationBatch> PromoteAdoptedAsync(
        PromoteGenerationBatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(request.IdempotencyKey);
        ValidateShotIds(request.ShotIds);
        EnsureMockOnly();

        GenerationBatch source = await _repository.GetAsync(
            request.SourcePreviewBatchId,
            cancellationToken).ConfigureAwait(false)
            ?? throw Validation("batch.not_found", "The source preview batch was not found.");
        if (source.TargetTier != GenerationBatchTier.PreviewFast ||
            source.Status != GenerationBatchStatus.Reviewed)
        {
            throw new GenerationBatchGateException(
                "batch.promotion_review_pending",
                "A preview batch must be fully reviewed before promotion.");
        }

        Dictionary<string, GenerationBatchItem> sourceItems = source.Items.ToDictionary(
            item => item.ShotId,
            StringComparer.Ordinal);
        var selected = new List<GenerationBatchItem>(request.ShotIds.Count);
        foreach (string shotId in request.ShotIds.Order(StringComparer.Ordinal))
        {
            if (!sourceItems.TryGetValue(shotId, out GenerationBatchItem? item))
                throw Validation("batch.unknown_shot", "A selected shot is not in the preview batch.");
            if (item.Status != GenerationBatchItemStatus.Adopted)
            {
                throw new GenerationBatchGateException(
                    "batch.promotion_not_adopted",
                    "Only an adopted preview can be promoted.");
            }

            if (item.RequestedTier != GenerationBatchTier.FinalQuality)
            {
                throw new GenerationBatchGateException(
                    "batch.promotion_tier",
                    "The shot manifest does not request a final-quality upgrade.");
            }

            selected.Add(item);
        }

        string[] fingerprintParts =
        [
            "cx502.final.v2",
            source.ManifestId,
            source.ManifestContentHash,
            source.Id.ToString("D"),
            source.Currency,
            GenerationBatchTier.FinalQuality.ToString(),
            .. selected.Select(CanonicalPromotionItem),
        ];
        string fingerprint = Hash(string.Join("\n", fingerprintParts));
        GenerationBatchItem[] items = selected.Select(item => new GenerationBatchItem
        {
            ShotId = item.ShotId,
            InputHash = Hash(string.Join(
                "\n",
                "cx502.final.input.v1",
                item.InputHash,
                item.Attempts[^1].Artifact.ContentHash)),
            RequestedTier = item.RequestedTier,
            TargetTier = GenerationBatchTier.FinalQuality,
            TargetDurationMilliseconds = item.TargetDurationMilliseconds,
            AspectRatio = item.AspectRatio,
            MaximumCostMicros = item.MaximumCostMicros,
            DialogueBinding = item.DialogueBinding,
            FirstFrame = item.FirstFrame,
            LastFrame = item.LastFrame,
            PrimaryMedia = item.PrimaryMedia,
            FallbackMedia = item.FallbackMedia,
            Status = GenerationBatchItemStatus.Pending,
            Attempts = [],
        }).ToArray();
        var submission = new GenerationBatchSubmission(
            request.IdempotencyKey,
            fingerprint,
            source.ManifestId,
            source.ManifestContentHash,
            GenerationBatchTier.FinalQuality,
            source.Id,
            source.Currency,
            items);
        GenerationBatchCreateResult stored = await _repository.CreateOrGetAsync(
            submission,
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        if (stored.Batch.Status != GenerationBatchStatus.Running)
            return stored.Batch;
        return await RunSingleFlightAsync(stored.Batch, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GenerationBatch> RunSingleFlightAsync(
        GenerationBatch batch,
        CancellationToken cancellationToken)
    {
        var runKey = new GenerationBatchRunKey(batch.Id, batch.Version);
        Lazy<Task<GenerationBatch>> candidate = null!;
        candidate = new Lazy<Task<GenerationBatch>>(
            () => RunAndRemoveAsync(runKey, candidate, batch),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<GenerationBatch>> shared = _activeRuns.GetOrAdd(runKey, candidate);
        return await shared.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<GenerationBatch> RunAndRemoveAsync(
        GenerationBatchRunKey runKey,
        Lazy<Task<GenerationBatch>> owner,
        GenerationBatch batch)
    {
        try
        {
            return await RunPendingAttemptsAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _activeRuns.TryRemove(
                new KeyValuePair<GenerationBatchRunKey, Lazy<Task<GenerationBatch>>>(runKey, owner));
        }
    }

    private async Task<GenerationBatch> RunPendingAttemptsAsync(
        GenerationBatch batch,
        CancellationToken cancellationToken)
    {
        EnsureMockOnly();
        string owner = "cx502-run-" + Guid.NewGuid().ToString("N");
        while (true)
        {
            GenerationBatchRunLease? lease = await _repository.TryAcquireRunAsync(
                batch.Id,
                owner,
                _timeProvider.GetUtcNow(),
                RunLeaseDuration,
                cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                GenerationBatch current = await _repository.GetAsync(batch.Id, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw Validation("batch.not_found", "The generation batch was not found.");
                if (current.Status != GenerationBatchStatus.Running)
                    return current;
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
                batch = current;
                continue;
            }

            batch = lease.Batch;
            long fencingToken = lease.FencingToken;
            try
            {
                foreach (GenerationBatchItem item in batch.Items)
                {
                    if (item.Status != GenerationBatchItemStatus.Pending) continue;

                    EnsureMockOnly();
                    GenerationBatchRunLease? renewed = await _repository.TryAcquireRunAsync(
                        batch.Id,
                        owner,
                        _timeProvider.GetUtcNow(),
                        RunLeaseDuration,
                        cancellationToken).ConfigureAwait(false);
                    if (renewed is null)
                    {
                        throw new GenerationBatchGateException(
                            "batch.run_lease_lost",
                            "The generation batch run lease could not be renewed.");
                    }

                    batch = renewed.Batch;
                    fencingToken = renewed.FencingToken;
                    GenerationBatchItem currentItem = batch.Items.Single(candidate =>
                        string.Equals(candidate.ShotId, item.ShotId, StringComparison.Ordinal));
                    if (currentItem.Status != GenerationBatchItemStatus.Pending) continue;
                    var attemptRequest = new GenerationBatchAttemptRequest(
                        batch.Id,
                        currentItem.ShotId,
                        currentItem.InputHash,
                        batch.TargetTier,
                        AttemptNumber: currentItem.Attempts.Count + 1,
                        currentItem.TargetDurationMilliseconds,
                        currentItem.AspectRatio,
                        currentItem.DialogueBinding,
                        currentItem.FirstFrame,
                        currentItem.LastFrame,
                        currentItem.PrimaryMedia,
                        currentItem.FallbackMedia);
                    GenerationBatchAttemptReceipt receipt;
                    try
                    {
                        receipt = await _executor.ExecuteAsync(
                            attemptRequest,
                            cancellationToken).ConfigureAwait(false);
                        ValidateMockReceipt(receipt);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return await _repository.FailItemAsync(
                            batch.Id,
                            batch.Version,
                            currentItem.ShotId,
                            "batch.mock_execution_failed",
                            owner,
                            fencingToken,
                            _timeProvider.GetUtcNow(),
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    var attempt = new GenerationBatchAttempt(
                        attemptRequest.AttemptNumber,
                        receipt.GenerationJobId,
                        receipt.Artifact,
                        receipt.ActualDurationMilliseconds,
                        receipt.ActualCostMicros,
                        receipt.CostSettled,
                        _timeProvider.GetUtcNow());
                    batch = await _repository.AppendAttemptAsync(
                        batch.Id,
                        batch.Version,
                        currentItem.ShotId,
                        attempt,
                        owner,
                        fencingToken,
                        _timeProvider.GetUtcNow(),
                        cancellationToken).ConfigureAwait(false);
                }

                return batch;
            }
            finally
            {
                await _repository.ReleaseRunAsync(
                    batch.Id,
                    owner,
                    fencingToken,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void EnsureMockOnly()
    {
        if (_executor.Mode != GenerationBatchProviderMode.Mock)
        {
            throw new GenerationBatchGateException(
                "batch.external_provider_g2_blocked",
                "CX-502 permits only the offline deterministic mock executor until G2 is approved.");
        }
    }

    private static ValidatedStart ValidateStart(StartGenerationBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.ShotIds);

        ValidateIdempotencyKey(request.IdempotencyKey);
        if (request.Manifest.ApprovalStatus != GenerationBatchManifestApproval.Approved ||
            !request.Manifest.ShotDispatchAllowed)
        {
            throw Validation(
                "manifest.not_dispatchable",
                "The shot manifest is not approved for dispatch.");
        }

        if (request.ShotIds.Count is < 1 or > MaximumBatchSize)
            throw Validation("batch.shot_count", "A batch must contain between 1 and 20 shots.");
        if (request.ShotIds.Any(string.IsNullOrWhiteSpace) ||
            request.ShotIds.Distinct(StringComparer.Ordinal).Count() != request.ShotIds.Count)
        {
            throw Validation("batch.shot_ids", "Shot IDs must be non-empty and unique.");
        }

        if (string.IsNullOrWhiteSpace(request.Manifest.ManifestId) ||
            !Sha256Pattern.IsMatch(request.Manifest.ContentHash ?? string.Empty) ||
            string.IsNullOrWhiteSpace(request.Manifest.Currency))
        {
            throw Validation("manifest.invalid", "The shot manifest identity is invalid.");
        }

        Dictionary<string, GenerationBatchShot> available;
        try
        {
            available = request.Manifest.Shots.ToDictionary(shot => shot.ShotId, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            throw Validation("manifest.duplicate_shot", "The shot manifest contains duplicate shot IDs.");
        }

        var selected = new List<GenerationBatchShot>(request.ShotIds.Count);
        foreach (string shotId in request.ShotIds.Order(StringComparer.Ordinal))
        {
            if (!available.TryGetValue(shotId, out GenerationBatchShot? shot))
                throw Validation("batch.unknown_shot", "A selected shot is not in the manifest.");
            if (!shot.DispatchAllowed || shot.ReadinessStatus != GenerationBatchShotReadiness.Ready)
                throw Validation("shot.not_dispatchable", "A selected shot is not ready for dispatch.");
            if (!Sha256Pattern.IsMatch(shot.InputHash ?? string.Empty) ||
                shot.TargetDurationMilliseconds <= 0 ||
                string.IsNullOrWhiteSpace(shot.AspectRatio) ||
                shot.MaximumCostMicros <= 0 ||
                !string.Equals(
                    shot.DialogueBinding,
                    "bound_to_primary_media",
                    StringComparison.Ordinal) ||
                shot.FirstFrame is null ||
                shot.LastFrame is null ||
                shot.PrimaryMedia is null ||
                shot.FallbackMedia is null ||
                !IsValidFrame(shot.FirstFrame) ||
                !IsValidFrame(shot.LastFrame) ||
                !IsValidMedia(shot.PrimaryMedia, ["video", "animation"]) ||
                !IsValidMedia(shot.FallbackMedia, ["video", "animation", "image"]))
            {
                throw Validation("shot.invalid", "A selected shot has an invalid pinned generation spec.");
            }

            selected.Add(shot);
        }

        string manifestContentHash = request.Manifest.ContentHash!;
        string[] fingerprintParts =
        [
            "cx502.preview.v2",
            request.Manifest.ManifestId,
            manifestContentHash,
            request.Manifest.ApprovalStatus.ToString(),
            request.Manifest.ShotDispatchAllowed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Manifest.Currency,
            GenerationBatchTier.PreviewFast.ToString(),
            .. selected.Select(CanonicalPreviewShot),
        ];
        string fingerprint = Hash(string.Join("\n", fingerprintParts));
        return new ValidatedStart(selected, fingerprint);
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (!IdempotencyKeyPattern.IsMatch(idempotencyKey ?? string.Empty))
            throw Validation("batch.idempotency_key", "The idempotency key is invalid.");
    }

    private static void ValidateShotIds(IReadOnlyList<string> shotIds)
    {
        ArgumentNullException.ThrowIfNull(shotIds);
        if (shotIds.Count is < 1 or > MaximumBatchSize)
            throw Validation("batch.shot_count", "A batch must contain between 1 and 20 shots.");
        if (shotIds.Any(string.IsNullOrWhiteSpace) ||
            shotIds.Distinct(StringComparer.Ordinal).Count() != shotIds.Count)
        {
            throw Validation("batch.shot_ids", "Shot IDs must be non-empty and unique.");
        }
    }

    private static void ValidateReviewItems(IReadOnlyList<GenerationBatchItemReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count is < 1 or > MaximumBatchSize)
            throw Validation("batch.review_count", "A review must contain between 1 and 20 items.");
        if (reviews.Any(review => string.IsNullOrWhiteSpace(review.ShotId)) ||
            reviews.Select(review => review.ShotId).Distinct(StringComparer.Ordinal).Count() != reviews.Count ||
            reviews.Any(review =>
                !Enum.IsDefined(review.Decision) ||
                review.ExpectedAttemptNumber < 1 ||
                !Sha256Pattern.IsMatch(review.ExpectedArtifactHash ?? string.Empty)))
        {
            throw Validation("batch.review_items", "Review items must be unique and valid.");
        }
    }

    private static GenerationBatchItem ToPendingPreviewItem(GenerationBatchShot shot) =>
        new()
        {
            ShotId = shot.ShotId,
            InputHash = shot.InputHash,
            RequestedTier = shot.RequestedTier,
            TargetTier = GenerationBatchTier.PreviewFast,
            TargetDurationMilliseconds = shot.TargetDurationMilliseconds,
            AspectRatio = shot.AspectRatio,
            MaximumCostMicros = shot.MaximumCostMicros,
            DialogueBinding = shot.DialogueBinding,
            FirstFrame = shot.FirstFrame,
            LastFrame = shot.LastFrame,
            PrimaryMedia = shot.PrimaryMedia,
            FallbackMedia = shot.FallbackMedia,
            Status = GenerationBatchItemStatus.Pending,
            Attempts = [],
        };

    private static string CanonicalPreviewShot(GenerationBatchShot shot) =>
        string.Join(
            ":",
            shot.ShotId,
            shot.InputHash,
            shot.DispatchAllowed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            shot.ReadinessStatus.ToString(),
            shot.RequestedTier.ToString(),
            shot.TargetDurationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            shot.AspectRatio,
            shot.MaximumCostMicros.ToString(System.Globalization.CultureInfo.InvariantCulture),
            shot.DialogueBinding,
            CanonicalFrame(shot.FirstFrame),
            CanonicalFrame(shot.LastFrame),
            CanonicalMedia(shot.PrimaryMedia),
            CanonicalMedia(shot.FallbackMedia));

    private static string CanonicalPromotionItem(GenerationBatchItem item) =>
        string.Join(
            ":",
            item.ShotId,
            item.InputHash,
            item.RequestedTier.ToString(),
            GenerationBatchTier.FinalQuality.ToString(),
            item.TargetDurationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.AspectRatio,
            item.MaximumCostMicros.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.DialogueBinding,
            CanonicalFrame(item.FirstFrame),
            CanonicalFrame(item.LastFrame),
            CanonicalMedia(item.PrimaryMedia),
            CanonicalMedia(item.FallbackMedia),
            item.Attempts[^1].Artifact.ContentHash);

    private static string CanonicalFrame(GenerationBatchFramePin? frame) => frame is null
        ? "null"
        : string.Join(
            ":",
            frame.MediaRef,
            frame.AssetVersion,
            frame.ContentHash,
            frame.MediaType,
            frame.Origin);

    private static string CanonicalMedia(GenerationBatchMediaPin? media) => media is null
        ? "null"
        : string.Join(
            ":",
            media.MediaRef,
            media.AssetVersion,
            media.ContentHash,
            media.MediaType);

    private static bool IsValidFrame(GenerationBatchFramePin frame) =>
        !string.IsNullOrWhiteSpace(frame.MediaRef) &&
        !string.IsNullOrWhiteSpace(frame.AssetVersion) &&
        Sha256Pattern.IsMatch(frame.ContentHash ?? string.Empty) &&
        string.Equals(frame.MediaType, "image", StringComparison.Ordinal) &&
        string.Equals(frame.Origin, "approved_frame", StringComparison.Ordinal);

    private static bool IsValidMedia(
        GenerationBatchMediaPin media,
        IReadOnlyCollection<string> allowedTypes) =>
        !string.IsNullOrWhiteSpace(media.MediaRef) &&
        !string.IsNullOrWhiteSpace(media.AssetVersion) &&
        Sha256Pattern.IsMatch(media.ContentHash ?? string.Empty) &&
        allowedTypes.Contains(media.MediaType, StringComparer.Ordinal);

    private static void ValidateMockReceipt(GenerationBatchAttemptReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Artifact);
        if (receipt.GenerationJobId == Guid.Empty ||
            string.IsNullOrWhiteSpace(receipt.Artifact.ArtifactId) ||
            !string.Equals(receipt.Artifact.MediaType, "video", StringComparison.Ordinal) ||
            !Sha256Pattern.IsMatch(receipt.Artifact.ContentHash ?? string.Empty) ||
            receipt.ActualDurationMilliseconds <= 0)
        {
            throw new GenerationBatchGateException(
                "batch.mock_receipt_invalid",
                "The mock executor returned an invalid receipt.");
        }

        if (!receipt.CostSettled || receipt.ActualCostMicros != 0)
        {
            throw new GenerationBatchGateException(
                "batch.mock_cost_nonzero",
                "The CX-502 mock executor must settle at zero cost.");
        }
    }

    private static GenerationBatchValidationException Validation(string code, string message) =>
        new(code, message);

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record ValidatedStart(
        IReadOnlyList<GenerationBatchShot> Shots,
        string RequestFingerprint);

    private readonly record struct GenerationBatchRunKey(Guid BatchId, long Version);
}
