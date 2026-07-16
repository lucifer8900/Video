namespace LipSyncReviewer;

public sealed record LipSyncReviewLocalizedValue(
    string LocalizationKey,
    string Text);

public sealed record LipSyncReviewMediaPin(
    string MediaRef,
    string AssetVersion,
    string ContentHash,
    string MediaType);

public sealed record LipSyncReviewCandidate(
    string ResponseId,
    IReadOnlyList<string> SourceNodeIds,
    LipSyncReviewLocalizedValue Dialogue,
    LipSyncReviewLocalizedValue Emotion,
    string DialogueContentHash,
    LipSyncReviewMediaPin LipSyncMedia,
    LipSyncReviewMediaPin AudioMedia,
    LipSyncReviewMediaPin FallbackMedia,
    string SourceReadinessStatus,
    IReadOnlyList<string> SourceIssueCodes,
    string PresentationHash);

public sealed record LipSyncBlockedResponse(
    string ResponseId,
    string SourceReadinessStatus,
    IReadOnlyList<string> IssueCodes);

public sealed record LoadedLipSyncReviewManifest(
    string ManifestId,
    string ManifestContentHash,
    string SourceBundleId,
    string SourceBundleContentHash,
    string Language,
    IReadOnlyList<LipSyncReviewCandidate> ReviewableItems,
    IReadOnlyList<LipSyncBlockedResponse> BlockedItems);
