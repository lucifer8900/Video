using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Generation;

namespace LipSyncReviewer;

public sealed partial class LipSyncReviewReportWriter
{
    public const string FileName = "lip-sync-review.report.json";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly TimeProvider _timeProvider;

    public LipSyncReviewReportWriter(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public WrittenLipSyncReviewReport Write(
        LipSyncReviewSession session,
        string outputPath,
        string? reviewerSubject)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        LipSyncReviewExportSnapshot capture = session.CaptureForReport();
        IReadOnlyList<LipSyncReviewItemState> states = capture.States;
        LoadedLipSyncReviewManifest manifest = capture.Manifest;
        int total = checked(manifest.ReviewableItems.Count + manifest.BlockedItems.Count);
        if (total == 0)
            throw Gate("review.empty_scope", "The lip-sync review scope is empty.");
        if (states.Any(state => state.Decision is null))
            throw Gate("review.incomplete", "Every reviewable response must have a decision before export.");
        if (manifest.ReviewableItems.Count > 0 &&
            !Sha256Pattern().IsMatch(reviewerSubject ?? string.Empty))
        {
            throw Gate(
                "review.reviewer_subject",
                "A hashed local reviewer subject is required for a completed review.");
        }
        if (manifest.ReviewableItems.Count == 0 && reviewerSubject is not null)
            throw Gate("review.reviewer_subject", "A blocked inventory report cannot claim a reviewer.");

        IReadOnlyList<LipSyncReviewDecisionEvent> events = capture.Events;
        Dictionary<string, LipSyncReviewDecisionEvent> finalEvents = events
            .GroupBy(review => review.ResponseId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        Dictionary<string, LipSyncReviewItemState> statesById = states
            .ToDictionary(state => state.ResponseId, StringComparer.Ordinal);
        LipSyncReviewItemContract[] items = manifest.ReviewableItems
            .Select(candidate => MapItem(candidate, statesById[candidate.ResponseId], finalEvents[candidate.ResponseId]))
            .ToArray();
        LipSyncBlockedResponseContract[] blocked = manifest.BlockedItems
            .Select(item => new LipSyncBlockedResponseContract(
                item.ResponseId,
                item.SourceReadinessStatus,
                item.IssueCodes.ToArray()))
            .ToArray();
        LipSyncReviewEventContract[] eventContracts = events.Select(MapEvent).ToArray();
        int passedCount = items.Count(item => string.Equals(item.Decision, "pass", StringComparison.Ordinal));
        int redoCount = items.Length - passedCount;
        string overallStatus = blocked.Length > 0
            ? "blocked"
            : redoCount > 0
                ? "redo_required"
                : "lip_sync_passed";
        DateTimeOffset completedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        string reportId = BuildReportId(manifest.ManifestContentHash, eventContracts, completedAtUtc);
        var reportWithoutHash = new LipSyncReviewReportContract(
            SchemaVersion: "1.0.0",
            ReportId: reportId,
            ReviewPolicyVersion: LipSyncReviewManifestLoader.ReviewPolicyVersion,
            ReviewScope: "pronunciation_subtitle_lip_sync",
            ProductionReadiness: "not_evaluated",
            Authority: "human_review_export_not_promotion_authority",
            OverallStatus: overallStatus,
            SourceManifestId: manifest.ManifestId,
            SourceManifestContentHash: manifest.ManifestContentHash,
            SourceBundleId: manifest.SourceBundleId,
            SourceBundleContentHash: manifest.SourceBundleContentHash,
            Language: manifest.Language,
            ReviewerSubject: reviewerSubject,
            StartedAtUtc: capture.StartedAtUtc,
            CompletedAtUtc: completedAtUtc,
            TotalResponseCount: total,
            ReviewableCount: items.Length,
            PassedCount: passedCount,
            RedoCount: redoCount,
            BlockedCount: blocked.Length,
            Items: items,
            BlockedItems: blocked,
            Events: eventContracts,
            ContentHash: string.Empty);
        string contentHash = LipSyncReviewReportVerifier.ComputeContentHash(reportWithoutHash);
        LipSyncReviewReportContract report = reportWithoutHash with { ContentHash = contentHash };
        LipSyncReviewReportVerifier.Verify(report);
        string json = NormalizeNewlines(JsonSerializer.Serialize(report, OutputOptions)) + "\n";
        string fullPath = WriteAtomic(outputPath, json);
        return new WrittenLipSyncReviewReport(report, json, fullPath);
    }

    private static LipSyncReviewItemContract MapItem(
        LipSyncReviewCandidate candidate,
        LipSyncReviewItemState state,
        LipSyncReviewDecisionEvent finalEvent) =>
        new(
            candidate.ResponseId,
            candidate.SourceNodeIds.ToArray(),
            new LipSyncReviewLocalizedValueContract(
                candidate.Dialogue.LocalizationKey,
                candidate.Dialogue.Text,
                candidate.DialogueContentHash),
            candidate.Emotion.LocalizationKey,
            candidate.Emotion.Text,
            MapPin(candidate.LipSyncMedia),
            MapPin(candidate.AudioMedia),
            MapPin(candidate.FallbackMedia),
            candidate.PresentationHash,
            candidate.SourceReadinessStatus,
            candidate.SourceIssueCodes.ToArray(),
            state.VideoPlaybackCompleted,
            state.VideoPlayedMilliseconds,
            state.AudioPlaybackCompleted,
            state.AudioPlayedMilliseconds,
            Decision(state.Decision!.Value),
            MapChecks(state.Checks!),
            state.ReasonCodes.ToArray(),
            finalEvent.RecordedAtUtc,
            finalEvent.EventHash);

    private static LipSyncReviewEventContract MapEvent(LipSyncReviewDecisionEvent review) =>
        new(
            review.Sequence,
            review.ResponseId,
            review.ManifestContentHash,
            review.DialogueContentHash,
            review.PresentationHash,
            review.Revision,
            review.VideoPlaybackCompleted,
            review.VideoPlayedMilliseconds,
            review.AudioPlaybackCompleted,
            review.AudioPlayedMilliseconds,
            Decision(review.Decision),
            MapChecks(review.Checks),
            review.ReasonCodes.ToArray(),
            review.RecordedAtUtc,
            review.PreviousEventHash,
            review.EventHash);

    private static LipSyncReviewChecksContract MapChecks(LipSyncReviewChecks checks) =>
        new(
            checks.RawAudioUnmasked,
            checks.PronunciationPassed,
            checks.SubtitlePassed,
            checks.LipSyncPassed);

    private static LipSyncReviewMediaPinContract MapPin(LipSyncReviewMediaPin pin) =>
        new(pin.MediaRef, pin.AssetVersion, pin.ContentHash, pin.MediaType);

    private static string Decision(LipSyncReviewDecision decision) => decision switch
    {
        LipSyncReviewDecision.Pass => "pass",
        LipSyncReviewDecision.Redo => "redo",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown lip-sync decision."),
    };

    private static string BuildReportId(
        string manifestHash,
        IReadOnlyList<LipSyncReviewEventContract> events,
        DateTimeOffset completedAtUtc)
    {
        string suffix = events.LastOrDefault()?.EventHash["sha256:".Length..][..16]
            ?? completedAtUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"lip-sync-review.{manifestHash["sha256:".Length..][..16]}.{suffix}";
    }

    private static string ContentHash(LipSyncReviewReportContract report)
    {
        JsonObject root = JsonSerializer.SerializeToNode(report, CanonicalOptions)!.AsObject();
        root.Remove("contentHash");
        string canonical = Canonicalize(root);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

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

    private static string WriteAtomic(string outputPath, string json)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
            throw new ArgumentException("The report output path has no parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        if (File.Exists(fullPath)) throw Gate("report.exists", "The review report already exists.");
        string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, fullPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(fullPath))
            {
                throw Gate("report.exists", "The review report already exists.");
            }
            return fullPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static LipSyncReviewGateException Gate(string code, string message) => new(code, message);

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

public sealed record WrittenLipSyncReviewReport(
    LipSyncReviewReportContract Report,
    string Json,
    string Path);
