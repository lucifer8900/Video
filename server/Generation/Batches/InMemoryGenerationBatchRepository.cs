namespace Lingmai.RedMist.Generation.Batches;

public sealed class InMemoryGenerationBatchRepository : IGenerationBatchRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GenerationBatch> _batchesById = [];
    private readonly Dictionary<string, Guid> _batchIdsByIdempotencyKey =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid BatchId, string Fingerprint)> _reviewsByIdempotencyKey =
        new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (string Owner, DateTimeOffset ExpiresAtUtc, long FencingToken)> _runLeases = [];
    private readonly Dictionary<Guid, long> _lastRunLeaseFencingTokens = [];

    public Task<GenerationBatchCreateResult> CreateOrGetAsync(
        GenerationBatchSubmission submission,
        Guid batchId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();
        GenerationBatchPersistenceValidator.EnsureValidItems(submission.Items);

        lock (_gate)
        {
            if (_batchIdsByIdempotencyKey.TryGetValue(submission.IdempotencyKey, out Guid existingId))
            {
                GenerationBatch existing = _batchesById[existingId];
                if (!string.Equals(
                        existing.RequestFingerprint,
                        submission.RequestFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new GenerationBatchIdempotencyConflictException(submission.IdempotencyKey);
                }

                return Task.FromResult(new GenerationBatchCreateResult(existing, Created: false));
            }

            bool previousReviewPending = _batchesById.Values.Any(existing =>
                string.Equals(
                    existing.ManifestId,
                    submission.ManifestId,
                    StringComparison.Ordinal) &&
                existing.Status is GenerationBatchStatus.Running or GenerationBatchStatus.AwaitingReview);
            if (previousReviewPending)
            {
                throw new GenerationBatchGateException(
                    "batch.previous_review_pending",
                    "The previous batch must be fully reviewed before another batch can start.");
            }

            if (submission.TargetTier == GenerationBatchTier.FinalQuality &&
                submission.SourcePreviewBatchId is Guid sourcePreviewBatchId)
            {
                HashSet<string> requestedShotIds = submission.Items
                    .Select(item => item.ShotId)
                    .ToHashSet(StringComparer.Ordinal);
                bool duplicatePromotion = _batchesById.Values
                    .Where(existing =>
                        existing.TargetTier == GenerationBatchTier.FinalQuality &&
                        existing.SourcePreviewBatchId == sourcePreviewBatchId)
                    .SelectMany(existing => existing.Items)
                    .Any(item => requestedShotIds.Contains(item.ShotId));
                if (duplicatePromotion)
                {
                    throw new GenerationBatchGateException(
                        "batch.promotion_already_exists",
                        "An adopted preview shot can have only one final-quality promotion batch.");
                }
            }

            GenerationBatch batch = new()
            {
                Id = batchId,
                IdempotencyKey = submission.IdempotencyKey,
                RequestFingerprint = submission.RequestFingerprint,
                ManifestId = submission.ManifestId,
                ManifestContentHash = submission.ManifestContentHash,
                TargetTier = submission.TargetTier,
                SourcePreviewBatchId = submission.SourcePreviewBatchId,
                Currency = submission.Currency,
                Status = GenerationBatchStatus.Running,
                Items = FreezeItems(submission.Items),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                Version = 0,
            };
            _batchesById.Add(batch.Id, batch);
            _batchIdsByIdempotencyKey.Add(batch.IdempotencyKey, batch.Id);
            return Task.FromResult(new GenerationBatchCreateResult(batch, Created: true));
        }
    }

    public Task<GenerationBatch?> GetAsync(Guid batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _batchesById.TryGetValue(batchId, out GenerationBatch? batch);
            return Task.FromResult(batch);
        }
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_batchesById.Count);
    }

    public Task<GenerationBatchRunLease?> TryAcquireRunAsync(
        Guid batchId,
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A batch run owner is required.", nameof(owner));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        lock (_gate)
        {
            GenerationBatch batch = GetRequired(batchId);
            if (batch.Status != GenerationBatchStatus.Running)
                return Task.FromResult<GenerationBatchRunLease?>(null);
            if (_runLeases.TryGetValue(batchId, out var currentLease) &&
                currentLease.ExpiresAtUtc > nowUtc &&
                !string.Equals(currentLease.Owner, owner, StringComparison.Ordinal))
            {
                return Task.FromResult<GenerationBatchRunLease?>(null);
            }

            DateTimeOffset expiresAtUtc = nowUtc + leaseDuration;
            long fencingToken = checked(
                _lastRunLeaseFencingTokens.GetValueOrDefault(batchId) + 1);
            _lastRunLeaseFencingTokens[batchId] = fencingToken;
            _runLeases[batchId] = (owner, expiresAtUtc, fencingToken);
            return Task.FromResult<GenerationBatchRunLease?>(
                new GenerationBatchRunLease(batch, owner, expiresAtUtc, fencingToken));
        }
    }

    public Task ReleaseRunAsync(
        Guid batchId,
        string owner,
        long fencingToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_runLeases.TryGetValue(batchId, out var lease) &&
                string.Equals(lease.Owner, owner, StringComparison.Ordinal) &&
                lease.FencingToken == fencingToken)
            {
                _runLeases.Remove(batchId);
            }

            return Task.CompletedTask;
        }
    }

    public Task<GenerationBatch> AppendAttemptAsync(
        Guid batchId,
        long expectedVersion,
        string shotId,
        GenerationBatchAttempt attempt,
        string runLeaseOwner,
        long runLeaseFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        cancellationToken.ThrowIfCancellationRequested();
        GenerationBatchPersistenceValidator.EnsureValidAttempt(attempt);

        lock (_gate)
        {
            if (!_runLeases.TryGetValue(batchId, out var lease) ||
                !string.Equals(lease.Owner, runLeaseOwner, StringComparison.Ordinal) ||
                lease.FencingToken != runLeaseFencingToken ||
                lease.ExpiresAtUtc <= nowUtc)
            {
                throw new GenerationBatchGateException(
                    "batch.run_lease_lost",
                    "The generation batch run lease is missing, expired, or owned by another runner.");
            }

            GenerationBatch current = GetRequired(batchId);
            if (current.Status != GenerationBatchStatus.Running)
            {
                throw new GenerationBatchGateException(
                    "batch.not_running",
                    "Only a running generation batch can accept attempts.");
            }
            if (current.Version != expectedVersion)
            {
                throw new GenerationBatchConcurrencyException(
                    batchId,
                    expectedVersion,
                    current.Version);
            }

            int itemIndex = current.Items
                .Select((item, index) => (item, index))
                .Where(candidate => string.Equals(candidate.item.ShotId, shotId, StringComparison.Ordinal))
                .Select(candidate => candidate.index)
                .DefaultIfEmpty(-1)
                .Single();
            if (itemIndex < 0)
                throw new GenerationBatchValidationException("batch.unknown_shot", "The shot is not in this batch.");

            GenerationBatchItem currentItem = current.Items[itemIndex];
            if (currentItem.Status != GenerationBatchItemStatus.Pending)
                return Task.FromResult(current);

            GenerationBatchItem updatedItem = currentItem with
            {
                Status = GenerationBatchItemStatus.AwaitingReview,
                Attempts = [.. currentItem.Attempts, attempt],
            };
            GenerationBatchItem[] updatedItems = current.Items.ToArray();
            updatedItems[itemIndex] = updatedItem;
            GenerationBatchStatus status = StatusFor(updatedItems);
            GenerationBatch updated = current with
            {
                Status = status,
                Items = FreezeItems(updatedItems),
                UpdatedAtUtc = attempt.CompletedAtUtc,
                Version = checked(current.Version + 1),
            };
            _batchesById[batchId] = updated;
            return Task.FromResult(updated);
        }
    }


    public Task<GenerationBatchReviewApplyResult> ApplyReviewsAsync(
        Guid batchId,
        long expectedVersion,
        string idempotencyKey,
        string reviewFingerprint,
        IReadOnlyList<GenerationBatchItemReview> reviews,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_reviewsByIdempotencyKey.TryGetValue(
                    idempotencyKey,
                    out (Guid BatchId, string Fingerprint) replay))
            {
                if (replay.BatchId != batchId ||
                    !string.Equals(replay.Fingerprint, reviewFingerprint, StringComparison.Ordinal))
                {
                    throw new GenerationBatchIdempotencyConflictException(idempotencyKey);
                }

                return Task.FromResult(
                    new GenerationBatchReviewApplyResult(GetRequired(batchId), Applied: false));
            }

            GenerationBatch current = GetRequired(batchId);
            if (current.Version != expectedVersion)
            {
                throw new GenerationBatchConcurrencyException(
                    batchId,
                    expectedVersion,
                    current.Version);
            }

            if (current.Status != GenerationBatchStatus.AwaitingReview)
            {
                throw new GenerationBatchGateException(
                    "batch.not_awaiting_review",
                    "The batch is not awaiting human review.");
            }

            GenerationBatchItem[] updatedItems = current.Items.ToArray();
            foreach (GenerationBatchItemReview review in reviews)
            {
                int itemIndex = Array.FindIndex(
                    updatedItems,
                    item => string.Equals(item.ShotId, review.ShotId, StringComparison.Ordinal));
                if (itemIndex < 0)
                    throw new GenerationBatchValidationException("batch.unknown_shot", "The shot is not in this batch.");
                if (updatedItems[itemIndex].Status != GenerationBatchItemStatus.AwaitingReview)
                {
                    throw new GenerationBatchGateException(
                        "batch.item_not_awaiting_review",
                        "The selected shot is not awaiting review.");
                }

                GenerationBatchAttempt? reviewedAttempt = updatedItems[itemIndex].Attempts.LastOrDefault();
                if (reviewedAttempt is null ||
                    reviewedAttempt.AttemptNumber != review.ExpectedAttemptNumber ||
                    !string.Equals(
                        reviewedAttempt.Artifact.ContentHash,
                        review.ExpectedArtifactHash,
                        StringComparison.Ordinal))
                {
                    throw new GenerationBatchGateException(
                        "batch.review_stale_attempt",
                        "The review does not match the latest generated artifact.");
                }

                updatedItems[itemIndex] = updatedItems[itemIndex] with
                {
                    Status = review.Decision switch
                    {
                        GenerationBatchReviewDecision.Adopt => GenerationBatchItemStatus.Adopted,
                        GenerationBatchReviewDecision.Reject => GenerationBatchItemStatus.Rejected,
                        GenerationBatchReviewDecision.Retry => GenerationBatchItemStatus.Pending,
                        _ => throw new GenerationBatchValidationException(
                            "batch.review_decision",
                            "The review decision is invalid."),
                    },
                };
            }

            GenerationBatch updated = current with
            {
                Status = StatusFor(updatedItems),
                Items = FreezeItems(updatedItems),
                UpdatedAtUtc = nowUtc,
                Version = checked(current.Version + 1),
            };
            _batchesById[batchId] = updated;
            _reviewsByIdempotencyKey.Add(idempotencyKey, (batchId, reviewFingerprint));
            return Task.FromResult(new GenerationBatchReviewApplyResult(updated, Applied: true));
        }
    }

    public Task<GenerationBatch> FailItemAsync(
        Guid batchId,
        long expectedVersion,
        string shotId,
        string failureCode,
        string runLeaseOwner,
        long runLeaseFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        lock (_gate)
        {
            if (!_runLeases.TryGetValue(batchId, out var lease) ||
                !string.Equals(lease.Owner, runLeaseOwner, StringComparison.Ordinal) ||
                lease.FencingToken != runLeaseFencingToken ||
                lease.ExpiresAtUtc <= nowUtc)
            {
                throw new GenerationBatchGateException(
                    "batch.run_lease_lost",
                    "The generation batch run lease is missing, expired, or owned by another runner.");
            }

            GenerationBatch current = GetRequired(batchId);
            if (current.Status != GenerationBatchStatus.Running)
            {
                throw new GenerationBatchGateException(
                    "batch.not_running",
                    "Only a running generation batch can enter failed state.");
            }
            if (current.Version != expectedVersion)
            {
                throw new GenerationBatchConcurrencyException(
                    batchId,
                    expectedVersion,
                    current.Version);
            }

            int itemIndex = current.Items
                .Select((item, index) => (item, index))
                .Where(candidate => string.Equals(candidate.item.ShotId, shotId, StringComparison.Ordinal))
                .Select(candidate => candidate.index)
                .DefaultIfEmpty(-1)
                .Single();
            if (itemIndex < 0)
                throw new GenerationBatchValidationException("batch.unknown_shot", "The shot is not in this batch.");

            GenerationBatchItem[] items = current.Items
                .Select((item, index) => item with
                {
                    Status = GenerationBatchItemStatus.Failed,
                    FailureCode = index == itemIndex
                        ? failureCode
                        : "batch.skipped_after_failure",
                })
                .ToArray();
            GenerationBatch updated = current with
            {
                Status = GenerationBatchStatus.Failed,
                Items = FreezeItems(items),
                UpdatedAtUtc = nowUtc,
                Version = checked(current.Version + 1),
            };
            _batchesById[batchId] = updated;
            return Task.FromResult(updated);
        }
    }

    private GenerationBatch GetRequired(Guid batchId) =>
        _batchesById.TryGetValue(batchId, out GenerationBatch? batch)
            ? batch
            : throw new InvalidOperationException($"Generation batch '{batchId}' was not found.");

    private static GenerationBatchStatus StatusFor(IReadOnlyList<GenerationBatchItem> items)
    {
        if (items.Any(item => item.Status == GenerationBatchItemStatus.Pending))
            return GenerationBatchStatus.Running;
        if (items.Any(item => item.Status == GenerationBatchItemStatus.AwaitingReview))
            return GenerationBatchStatus.AwaitingReview;
        if (items.All(item => item.Status is GenerationBatchItemStatus.Adopted or GenerationBatchItemStatus.Rejected))
            return GenerationBatchStatus.Reviewed;
        return GenerationBatchStatus.Failed;
    }

    private static IReadOnlyList<GenerationBatchItem> FreezeItems(
        IEnumerable<GenerationBatchItem> items) =>
        Array.AsReadOnly(
            items.Select(item => item with
            {
                Attempts = Array.AsReadOnly(item.Attempts.ToArray()),
            }).ToArray());
}
