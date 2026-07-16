using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lingmai.RedMist.Contracts.Generation;

namespace LipSyncReviewer;

public static class LipSyncReviewReportVerifier
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static void Verify(LipSyncReviewReportContract report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(report.ContentHash, ComputeContentHash(report), StringComparison.Ordinal))
            throw Invalid("report.content_hash", "The report content hash does not match its canonical content.");

        VerifyMetadata(report);
        VerifyCountsAndStatus(report);

        Dictionary<string, LipSyncReviewItemContract> items = UniqueItems(report.Items);
        VerifyBlockedScope(report.BlockedItems, items.Keys);
        Dictionary<string, LipSyncReviewEventContract> finalEvents = VerifyEvents(report, items);
        VerifyItems(report.Items, finalEvents);
    }

    public static string ComputeContentHash(LipSyncReviewReportContract report)
    {
        ArgumentNullException.ThrowIfNull(report);
        JsonObject root = JsonSerializer.SerializeToNode(report, CanonicalOptions)!.AsObject();
        root.Remove("contentHash");
        return Hash(Canonicalize(root));
    }

    public static string ComputeEventHash(LipSyncReviewEventContract review)
    {
        ArgumentNullException.ThrowIfNull(review);
        string decision = review.Decision switch
        {
            "pass" => "Pass",
            "redo" => "Redo",
            _ => throw Invalid("report.event_decision", "A review event has an unknown decision."),
        };
        return Hash(string.Join(
            "\n",
            review.Sequence.ToString(CultureInfo.InvariantCulture),
            review.ResponseId,
            review.ManifestContentHash,
            review.DialogueContentHash,
            review.PresentationHash,
            review.Revision.ToString(CultureInfo.InvariantCulture),
            review.VideoPlaybackCompleted.ToString(),
            review.VideoPlayedMilliseconds.ToString(CultureInfo.InvariantCulture),
            review.AudioPlaybackCompleted.ToString(),
            review.AudioPlayedMilliseconds.ToString(CultureInfo.InvariantCulture),
            decision,
            review.Checks.RawAudioUnmasked.ToString(),
            review.Checks.PronunciationPassed.ToString(),
            review.Checks.SubtitlePassed.ToString(),
            review.Checks.LipSyncPassed.ToString(),
            string.Join(",", review.ReasonCodes),
            review.RecordedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            review.PreviousEventHash ?? "none"));
    }

    private static void VerifyMetadata(LipSyncReviewReportContract report)
    {
        if (report.SchemaVersion != "1.0.0" ||
            report.ReviewPolicyVersion != LipSyncReviewManifestLoader.ReviewPolicyVersion ||
            report.ReviewScope != "pronunciation_subtitle_lip_sync" ||
            report.ProductionReadiness != "not_evaluated" ||
            report.Authority != "human_review_export_not_promotion_authority" ||
            !IsSha256(report.SourceManifestContentHash) ||
            !IsSha256(report.SourceBundleContentHash) ||
            report.StartedAtUtc > report.CompletedAtUtc)
        {
            throw Invalid("report.metadata", "The report metadata or authority declaration is invalid.");
        }

        bool hasReviewer = IsSha256(report.ReviewerSubject);
        if ((report.ReviewableCount > 0 && !hasReviewer) ||
            (report.ReviewableCount == 0 && report.ReviewerSubject is not null))
        {
            throw Invalid("report.metadata", "The reviewer subject does not match the review scope.");
        }
    }

    private static void VerifyCountsAndStatus(LipSyncReviewReportContract report)
    {
        int reviewable = report.Items.Count;
        int blocked = report.BlockedItems.Count;
        int total = checked(reviewable + blocked);
        int passed = report.Items.Count(item => item.Decision == "pass");
        int redo = report.Items.Count(item => item.Decision == "redo");
        if (total == 0 ||
            report.TotalResponseCount != total ||
            report.ReviewableCount != reviewable ||
            report.PassedCount != passed ||
            report.RedoCount != redo ||
            report.BlockedCount != blocked ||
            passed + redo != reviewable)
        {
            throw Invalid("report.counts", "The report counts do not match its inventory and decisions.");
        }

        string expectedStatus = blocked > 0
            ? "blocked"
            : redo > 0
                ? "redo_required"
                : "lip_sync_passed";
        if (!string.Equals(report.OverallStatus, expectedStatus, StringComparison.Ordinal))
            throw Invalid("report.status", "The overall status does not match the terminal decisions.");
    }

    private static Dictionary<string, LipSyncReviewItemContract> UniqueItems(
        IReadOnlyList<LipSyncReviewItemContract> source)
    {
        var result = new Dictionary<string, LipSyncReviewItemContract>(StringComparer.Ordinal);
        foreach (LipSyncReviewItemContract item in source)
        {
            if (string.IsNullOrWhiteSpace(item.ResponseId) || !result.TryAdd(item.ResponseId, item))
                throw Invalid("report.response_scope", "Review response IDs must be non-empty and unique.");
        }
        return result;
    }

    private static void VerifyBlockedScope(
        IReadOnlyList<LipSyncBlockedResponseContract> blocked,
        IEnumerable<string> reviewableIds)
    {
        var ids = new HashSet<string>(reviewableIds, StringComparer.Ordinal);
        foreach (LipSyncBlockedResponseContract item in blocked)
        {
            if (string.IsNullOrWhiteSpace(item.ResponseId) ||
                !ids.Add(item.ResponseId) ||
                item.IssueCodes.Count == 0)
            {
                throw Invalid("report.response_scope", "Blocked responses must be unique and explain why they are blocked.");
            }
        }
    }

    private static Dictionary<string, LipSyncReviewEventContract> VerifyEvents(
        LipSyncReviewReportContract report,
        IReadOnlyDictionary<string, LipSyncReviewItemContract> items)
    {
        var finalEvents = new Dictionary<string, LipSyncReviewEventContract>(StringComparer.Ordinal);
        var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
        string? previousHash = null;
        for (int index = 0; index < report.Events.Count; index++)
        {
            LipSyncReviewEventContract review = report.Events[index];
            if (review.Sequence != index + 1L)
                throw Invalid("report.event_sequence", "Review event sequences must be contiguous and ordered.");
            if (!string.Equals(review.PreviousEventHash, previousHash, StringComparison.Ordinal))
                throw Invalid("report.event_previous_hash", "A review event does not link to the preceding event.");
            if (!string.Equals(
                    review.ManifestContentHash,
                    report.SourceManifestContentHash,
                    StringComparison.Ordinal))
            {
                throw Invalid("report.event_source_hash", "A review event references another source manifest.");
            }
            if (!items.TryGetValue(review.ResponseId, out LipSyncReviewItemContract? item) ||
                !string.Equals(review.DialogueContentHash, item.Dialogue.ContentHash, StringComparison.Ordinal) ||
                !string.Equals(review.PresentationHash, item.PresentationHash, StringComparison.Ordinal))
            {
                throw Invalid("report.event_item_pin", "A review event is not pinned to its exported item.");
            }
            if (revisions.TryGetValue(review.ResponseId, out long priorRevision) &&
                review.Revision <= priorRevision)
            {
                throw Invalid("report.event_revision", "Review revisions must increase for each response.");
            }
            if (!CompletePlayback(
                    review.VideoPlaybackCompleted,
                    review.VideoPlayedMilliseconds,
                    review.AudioPlaybackCompleted,
                    review.AudioPlayedMilliseconds))
            {
                throw Invalid(
                    "report.event_playback",
                    "Every decision event must contain positive completed playback evidence.");
            }

            VerifyDecision(review.Decision, review.Checks, review.ReasonCodes, "report.event_decision");
            if (!string.Equals(review.EventHash, ComputeEventHash(review), StringComparison.Ordinal))
                throw Invalid("report.event_hash", "A review event hash does not match its canonical event.");
            if (review.RecordedAtUtc < report.StartedAtUtc || review.RecordedAtUtc > report.CompletedAtUtc)
                throw Invalid("report.event_time", "A review event falls outside the report time range.");

            revisions[review.ResponseId] = review.Revision;
            finalEvents[review.ResponseId] = review;
            previousHash = review.EventHash;
        }
        return finalEvents;
    }

    private static void VerifyItems(
        IReadOnlyList<LipSyncReviewItemContract> items,
        IReadOnlyDictionary<string, LipSyncReviewEventContract> finalEvents)
    {
        foreach (LipSyncReviewItemContract item in items)
        {
            if (!CompletePlayback(
                    item.VideoPlaybackCompleted,
                    item.VideoPlayedMilliseconds,
                    item.AudioPlaybackCompleted,
                    item.AudioPlayedMilliseconds))
            {
                throw Invalid("report.playback", "Every review item must contain positive completed playback evidence.");
            }

            VerifyDecision(item.Decision, item.Checks, item.ReasonCodes, "report.item_decision");
            if (!finalEvents.TryGetValue(item.ResponseId, out LipSyncReviewEventContract? finalEvent) ||
                !string.Equals(item.FinalEventHash, finalEvent.EventHash, StringComparison.Ordinal) ||
                !string.Equals(item.Decision, finalEvent.Decision, StringComparison.Ordinal) ||
                item.VideoPlaybackCompleted != finalEvent.VideoPlaybackCompleted ||
                item.VideoPlayedMilliseconds != finalEvent.VideoPlayedMilliseconds ||
                item.AudioPlaybackCompleted != finalEvent.AudioPlaybackCompleted ||
                item.AudioPlayedMilliseconds != finalEvent.AudioPlayedMilliseconds ||
                item.Checks != finalEvent.Checks ||
                !item.ReasonCodes.SequenceEqual(finalEvent.ReasonCodes, StringComparer.Ordinal) ||
                item.ReviewedAtUtc != finalEvent.RecordedAtUtc)
            {
                throw Invalid("report.item_terminal_event", "A review item does not match its terminal event.");
            }
        }
    }

    private static bool CompletePlayback(
        bool videoCompleted,
        long videoPlayedMilliseconds,
        bool audioCompleted,
        long audioPlayedMilliseconds) =>
        videoCompleted &&
        videoPlayedMilliseconds > 0 &&
        audioCompleted &&
        audioPlayedMilliseconds > 0;

    private static void VerifyDecision(
        string decision,
        LipSyncReviewChecksContract checks,
        IReadOnlyList<string> reasons,
        string code)
    {
        string[] expectedReasons = ReasonsFor(checks);
        bool valid = decision switch
        {
            "pass" => expectedReasons.Length == 0 && reasons.Count == 0,
            "redo" => expectedReasons.Length > 0 && reasons.SequenceEqual(expectedReasons, StringComparer.Ordinal),
            _ => false,
        };
        if (!valid) throw Invalid(code, "A review decision does not match its checks and reason codes.");
    }

    private static string[] ReasonsFor(LipSyncReviewChecksContract checks)
    {
        var reasons = new List<string>(capacity: 4);
        if (!checks.RawAudioUnmasked) reasons.Add("lip_sync.raw_audio_masked");
        if (!checks.PronunciationPassed) reasons.Add("lip_sync.pronunciation");
        if (!checks.SubtitlePassed) reasons.Add("lip_sync.subtitle");
        if (!checks.LipSyncPassed) reasons.Add("lip_sync.mismatch");
        return reasons.ToArray();
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static string Canonicalize(JsonNode? node) => node switch
    {
        JsonObject value => "{" + string.Join(",", value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property =>
                JsonSerializer.Serialize(property.Key) + ":" + Canonicalize(property.Value))) + "}",
        JsonArray value => "[" + string.Join(",", value.Select(Canonicalize)) + "]",
        null => "null",
        _ => node.ToJsonString(CanonicalOptions),
    };

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static LipSyncReviewGateException Invalid(string code, string message) => new(code, message);
}
