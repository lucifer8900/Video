namespace Lingmai.RedMist.Contracts.Generation;

public sealed record LipSyncReviewLocalizedValueContract(
    string LocalizationKey,
    string Text,
    string ContentHash);

public sealed record LipSyncReviewMediaPinContract(
    string MediaRef,
    string AssetVersion,
    string ContentHash,
    string MediaType);

public sealed record LipSyncReviewChecksContract(
    bool RawAudioUnmasked,
    bool PronunciationPassed,
    bool SubtitlePassed,
    bool LipSyncPassed);

public sealed record LipSyncReviewItemContract(
    string ResponseId,
    IReadOnlyList<string> SourceNodeIds,
    LipSyncReviewLocalizedValueContract Dialogue,
    string EmotionLocalizationKey,
    string EmotionText,
    LipSyncReviewMediaPinContract LipSyncMedia,
    LipSyncReviewMediaPinContract AudioMedia,
    LipSyncReviewMediaPinContract FallbackMedia,
    string PresentationHash,
    string SourceReadinessStatus,
    IReadOnlyList<string> SourceIssueCodes,
    bool VideoPlaybackCompleted,
    long VideoPlayedMilliseconds,
    bool AudioPlaybackCompleted,
    long AudioPlayedMilliseconds,
    string Decision,
    LipSyncReviewChecksContract Checks,
    IReadOnlyList<string> ReasonCodes,
    DateTimeOffset ReviewedAtUtc,
    string FinalEventHash);

public sealed record LipSyncBlockedResponseContract(
    string ResponseId,
    string SourceReadinessStatus,
    IReadOnlyList<string> IssueCodes);

public sealed record LipSyncReviewEventContract(
    long Sequence,
    string ResponseId,
    string ManifestContentHash,
    string DialogueContentHash,
    string PresentationHash,
    long Revision,
    bool VideoPlaybackCompleted,
    long VideoPlayedMilliseconds,
    bool AudioPlaybackCompleted,
    long AudioPlayedMilliseconds,
    string Decision,
    LipSyncReviewChecksContract Checks,
    IReadOnlyList<string> ReasonCodes,
    DateTimeOffset RecordedAtUtc,
    string? PreviousEventHash,
    string EventHash);

public sealed record LipSyncReviewReportContract(
    string SchemaVersion,
    string ReportId,
    string ReviewPolicyVersion,
    string ReviewScope,
    string ProductionReadiness,
    string Authority,
    string OverallStatus,
    string SourceManifestId,
    string SourceManifestContentHash,
    string SourceBundleId,
    string SourceBundleContentHash,
    string Language,
    string? ReviewerSubject,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int TotalResponseCount,
    int ReviewableCount,
    int PassedCount,
    int RedoCount,
    int BlockedCount,
    IReadOnlyList<LipSyncReviewItemContract> Items,
    IReadOnlyList<LipSyncBlockedResponseContract> BlockedItems,
    IReadOnlyList<LipSyncReviewEventContract> Events,
    string ContentHash);
