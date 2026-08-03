using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Batches;

internal static class GenerationBatchPersistenceValidator
{
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void EnsureValidItems(IReadOnlyList<GenerationBatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > GenerationBatchService.MaximumBatchSize)
        {
            throw new GenerationBatchValidationException(
                "batch.shot_count",
                "A batch must contain between 1 and 20 shots.");
        }
        if (items.Any(item => !HasValidPinnedSpec(item)) ||
            items.SelectMany(item => item.Attempts).Any(attempt => !HasValidAttempt(attempt)))
        {
            throw InvalidPersistedSpec();
        }
    }

    public static void EnsureValidAttempt(GenerationBatchAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (!HasValidAttempt(attempt)) throw InvalidPersistedSpec();
    }

    public static bool HasValidPinnedSpec(GenerationBatchItem item) =>
        item is not null &&
        string.Equals(
            item.DialogueBinding,
            "bound_to_primary_media",
            StringComparison.Ordinal) &&
        IsValidFrame(item.FirstFrame) &&
        IsValidFrame(item.LastFrame) &&
        IsValidMedia(item.PrimaryMedia, ["video", "animation"]) &&
        IsValidMedia(item.FallbackMedia, ["video", "animation", "image"]);

    public static bool HasValidAttempt(GenerationBatchAttempt attempt) =>
        attempt is not null &&
        attempt.AttemptNumber > 0 &&
        attempt.GenerationJobId != Guid.Empty &&
        attempt.Artifact is not null &&
        !string.IsNullOrWhiteSpace(attempt.Artifact.ArtifactId) &&
        string.Equals(attempt.Artifact.MediaType, "video", StringComparison.Ordinal) &&
        Sha256Pattern.IsMatch(attempt.Artifact.ContentHash ?? string.Empty) &&
        attempt.ActualDurationMilliseconds > 0 &&
        attempt.ActualCostMicros >= 0;

    private static bool IsValidFrame(GenerationBatchFramePin? frame) =>
        frame is not null &&
        !string.IsNullOrWhiteSpace(frame.MediaRef) &&
        !string.IsNullOrWhiteSpace(frame.AssetVersion) &&
        Sha256Pattern.IsMatch(frame.ContentHash ?? string.Empty) &&
        string.Equals(frame.MediaType, "image", StringComparison.Ordinal) &&
        string.Equals(frame.Origin, "approved_frame", StringComparison.Ordinal);

    private static bool IsValidMedia(
        GenerationBatchMediaPin? media,
        IReadOnlyCollection<string> allowedTypes) =>
        media is not null &&
        !string.IsNullOrWhiteSpace(media.MediaRef) &&
        !string.IsNullOrWhiteSpace(media.AssetVersion) &&
        Sha256Pattern.IsMatch(media.ContentHash ?? string.Empty) &&
        allowedTypes.Contains(media.MediaType, StringComparer.Ordinal);

    private static GenerationBatchValidationException InvalidPersistedSpec() =>
        new(
            "batch.persisted_spec_invalid",
            "A generation batch item contains an invalid pinned spec or attempt artifact.");
}
