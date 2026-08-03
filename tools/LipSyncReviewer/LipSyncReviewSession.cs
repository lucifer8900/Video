using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace LipSyncReviewer;

public enum LipSyncPlaybackKind
{
    OriginalVideo,
    ReferenceAudio,
}

public enum LipSyncReviewDecision
{
    Pass,
    Redo,
}

public sealed record LipSyncReviewChecks(
    bool RawAudioUnmasked,
    bool PronunciationPassed,
    bool SubtitlePassed,
    bool LipSyncPassed);

public sealed record LipSyncReviewDecisionRequest(
    string ResponseId,
    long ExpectedRevision,
    LipSyncReviewDecision RequestedDecision,
    LipSyncReviewChecks Checks);

public sealed record LipSyncReviewItemState(
    string ResponseId,
    long Revision,
    bool VideoPlaybackCompleted,
    long VideoPlayedMilliseconds,
    bool AudioPlaybackCompleted,
    long AudioPlayedMilliseconds,
    LipSyncReviewDecision? Decision,
    LipSyncReviewChecks? Checks,
    IReadOnlyList<string> ReasonCodes);

public sealed record LipSyncReviewDecisionEvent(
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
    LipSyncReviewDecision Decision,
    LipSyncReviewChecks Checks,
    IReadOnlyList<string> ReasonCodes,
    DateTimeOffset RecordedAtUtc,
    string? PreviousEventHash,
    string EventHash);

public sealed record LipSyncReviewExportSnapshot(
    LoadedLipSyncReviewManifest Manifest,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<LipSyncReviewItemState> States,
    IReadOnlyList<LipSyncReviewDecisionEvent> Events);

public sealed class LipSyncReviewSession
{
    private readonly object _gate = new();
    private readonly LoadedLipSyncReviewManifest _manifest;
    private readonly IReadOnlyDictionary<string, LipSyncReviewCandidate> _candidateByResponseId;
    private readonly IReadOnlyDictionary<string, LipSyncReviewMediaItem> _mediaByResponseId;
    private readonly Dictionary<string, MutableItemState> _states;
    private readonly List<LipSyncReviewDecisionEvent> _events = [];
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _sessionTokenHash;
    private LipSyncReviewExportSnapshot? _exportSnapshot;

    public LipSyncReviewSession(
        LoadedLipSyncReviewManifest manifest,
        IReadOnlyList<LipSyncReviewMediaItem> mediaItems,
        TimeProvider timeProvider,
        string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(mediaItems);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length < 16)
            throw new ArgumentException("A strong local review session token is required.", nameof(sessionToken));
        _sessionTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken));

        Dictionary<string, LipSyncReviewMediaItem> media;
        try
        {
            media = mediaItems.ToDictionary(item => item.Candidate.ResponseId, StringComparer.Ordinal);
        }
        catch (ArgumentException exception)
        {
            throw new LipSyncReviewInputException(
                "review.duplicate_media",
                "Review media contains a duplicate response ID.",
                exception);
        }

        string[] expectedIds = manifest.ReviewableItems
            .Select(item => item.ResponseId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualIds = media.Keys.Order(StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            throw new LipSyncReviewInputException(
                "review.media_scope",
                "Review media must match every reviewable response exactly once.");
        }
        foreach (LipSyncReviewCandidate expected in manifest.ReviewableItems)
        {
            if (!Equals(expected, media[expected.ResponseId].Candidate))
            {
                throw new LipSyncReviewInputException(
                    "review.media_pin_mismatch",
                    "Review media does not match the locked manifest candidate.");
            }
        }

        _manifest = FreezeManifest(manifest);
        _candidateByResponseId = new ReadOnlyDictionary<string, LipSyncReviewCandidate>(
            _manifest.ReviewableItems.ToDictionary(
                candidate => candidate.ResponseId,
                StringComparer.Ordinal));
        _mediaByResponseId = new ReadOnlyDictionary<string, LipSyncReviewMediaItem>(media);
        MediaBindingHash = LipSyncReviewMediaBinding.Compute(_mediaByResponseId.Values);
        _states = expectedIds.ToDictionary(
            responseId => responseId,
            responseId => new MutableItemState(responseId),
            StringComparer.Ordinal);
        StartedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
    }

    public DateTimeOffset StartedAtUtc { get; }

    public LoadedLipSyncReviewManifest Manifest => _manifest;

    public IReadOnlyDictionary<string, LipSyncReviewMediaItem> MediaByResponseId =>
        _mediaByResponseId;

    internal string MediaBindingHash { get; }

    public LipSyncReviewItemState CompletePlayback(
        string responseId,
        long expectedRevision,
        LipSyncPlaybackKind kind,
        long playedMilliseconds,
        string sessionToken)
    {
        EnsureToken(sessionToken);
        if (!Enum.IsDefined(kind))
            throw Gate("review.playback_kind", "The playback kind is invalid.");
        if (playedMilliseconds <= 0)
            throw Gate("review.playback_duration", "Playback duration must be positive.");

        lock (_gate)
        {
            EnsureNotSealed();
            MutableItemState state = GetState(responseId);
            EnsureRevision(state, expectedRevision);
            if (state.Decision is not null)
            {
                throw Gate(
                    "review.decision_already_recorded",
                    "Playback evidence is frozen after a review decision is recorded.");
            }
            DeliveryCoverage delivery = kind == LipSyncPlaybackKind.OriginalVideo
                ? state.VideoDelivery
                : state.AudioDelivery;
            if (!delivery.IsComplete)
            {
                throw Gate(
                    "review.media_not_fully_delivered",
                    "The locked media bytes must be fully delivered before playback can be completed.");
            }
            long expectedDuration = kind == LipSyncPlaybackKind.OriginalVideo
                ? _mediaByResponseId[state.ResponseId].LipSyncDurationMilliseconds
                : _mediaByResponseId[state.ResponseId].AudioDurationMilliseconds;
            long requiredDuration = Math.Max(1, expectedDuration - (expectedDuration / 10));
            if (playedMilliseconds < requiredDuration)
            {
                throw Gate(
                    "review.playback_duration_mismatch",
                    "Reported playback is shorter than the probed locked media duration.");
            }
            if (kind == LipSyncPlaybackKind.OriginalVideo)
            {
                state.VideoPlaybackCompleted = true;
                state.VideoPlayedMilliseconds = playedMilliseconds;
            }
            else
            {
                state.AudioPlaybackCompleted = true;
                state.AudioPlayedMilliseconds = playedMilliseconds;
            }
            state.Revision = checked(state.Revision + 1);
            return Freeze(state);
        }
    }

    public LipSyncReviewItemState RecordDecision(
        LipSyncReviewDecisionRequest request,
        string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Checks);
        EnsureToken(sessionToken);
        if (!Enum.IsDefined(request.RequestedDecision))
            throw Gate("review.decision", "The review decision is invalid.");

        lock (_gate)
        {
            EnsureNotSealed();
            MutableItemState state = GetState(request.ResponseId);
            EnsureRevision(state, request.ExpectedRevision);
            if (!state.VideoPlaybackCompleted || !state.AudioPlaybackCompleted)
            {
                throw Gate(
                    "review.playback_incomplete",
                    "The original video and locked reference audio must both play to completion.");
            }

            string[] reasons = ReasonsFor(request.Checks);
            bool allPassed = reasons.Length == 0;
            if (request.RequestedDecision == LipSyncReviewDecision.Pass && !allPassed)
                throw Gate("review.pass_checks", "Pass requires every technical check to pass.");
            if (request.RequestedDecision == LipSyncReviewDecision.Redo && allPassed)
                throw Gate("review.redo_reason_missing", "Redo requires at least one failed check.");

            state.Revision = checked(state.Revision + 1);
            state.Decision = request.RequestedDecision;
            state.Checks = request.Checks;
            state.ReasonCodes = reasons;
            AppendEvent(state);
            return Freeze(state);
        }
    }

    public IReadOnlyList<LipSyncReviewItemState> Snapshot()
    {
        lock (_gate)
            return _states.Values.OrderBy(state => state.ResponseId, StringComparer.Ordinal).Select(Freeze).ToArray();
    }

    public IReadOnlyList<LipSyncReviewDecisionEvent> Events()
    {
        lock (_gate) return _events.ToArray();
    }

    public void Authorize(string sessionToken) => EnsureToken(sessionToken);

    internal void RecordMediaDelivery(
        string responseId,
        LipSyncPlaybackKind kind,
        long startInclusive,
        long endInclusive,
        long totalLength)
    {
        if (!Enum.IsDefined(kind))
            throw Gate("review.playback_kind", "The playback kind is invalid.");
        lock (_gate)
        {
            if (_exportSnapshot is not null) return;
            MutableItemState state = GetState(responseId);
            if (state.Decision is not null) return;
            DeliveryCoverage coverage = kind == LipSyncPlaybackKind.OriginalVideo
                ? state.VideoDelivery
                : state.AudioDelivery;
            coverage.Add(startInclusive, endInclusive, totalLength);
        }
    }

    public LipSyncReviewExportSnapshot CaptureForReport()
    {
        lock (_gate)
        {
            if (_exportSnapshot is not null) return _exportSnapshot;
            if (_states.Values.Any(state => state.Decision is null))
            {
                throw Gate(
                    "review.incomplete",
                    "Every reviewable response must have a decision before export.");
            }
            _exportSnapshot = new LipSyncReviewExportSnapshot(
                _manifest,
                StartedAtUtc,
                _states.Values
                    .OrderBy(state => state.ResponseId, StringComparer.Ordinal)
                    .Select(Freeze)
                    .ToArray(),
                _events.ToArray());
            return _exportSnapshot;
        }
    }

    private void AppendEvent(MutableItemState state)
    {
        LipSyncReviewCandidate candidate = _candidateByResponseId[state.ResponseId];
        long sequence = checked(_events.Count + 1L);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        string? previousHash = _events.LastOrDefault()?.EventHash;
        string[] reasons = state.ReasonCodes.ToArray();
        string eventHash = Hash(
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.ResponseId,
            _manifest.ManifestContentHash,
            candidate.DialogueContentHash,
            candidate.PresentationHash,
            state.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.VideoPlaybackCompleted.ToString(),
            state.VideoPlayedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.AudioPlaybackCompleted.ToString(),
            state.AudioPlayedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.Decision!.Value.ToString(),
            state.Checks!.RawAudioUnmasked.ToString(),
            state.Checks.PronunciationPassed.ToString(),
            state.Checks.SubtitlePassed.ToString(),
            state.Checks.LipSyncPassed.ToString(),
            string.Join(",", reasons),
            now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            previousHash ?? "none");
        _events.Add(new LipSyncReviewDecisionEvent(
            sequence,
            state.ResponseId,
            _manifest.ManifestContentHash,
            candidate.DialogueContentHash,
            candidate.PresentationHash,
            state.Revision,
            state.VideoPlaybackCompleted,
            state.VideoPlayedMilliseconds,
            state.AudioPlaybackCompleted,
            state.AudioPlayedMilliseconds,
            state.Decision.Value,
            state.Checks,
            reasons,
            now,
            previousHash,
            eventHash));
    }

    private MutableItemState GetState(string responseId)
    {
        if (string.IsNullOrWhiteSpace(responseId) ||
            !_states.TryGetValue(responseId, out MutableItemState? state))
        {
            throw Gate("review.unknown_response", "The response is not part of this review session.");
        }
        return state;
    }

    private static void EnsureRevision(MutableItemState state, long expectedRevision)
    {
        if (state.Revision != expectedRevision)
            throw Gate("review.revision_stale", "The review item revision is stale.");
    }

    private void EnsureNotSealed()
    {
        if (_exportSnapshot is not null)
            throw Gate("review.session_sealed", "The review session is sealed for report export.");
    }

    private void EnsureToken(string sessionToken)
    {
        byte[] candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken ?? string.Empty));
        if (!CryptographicOperations.FixedTimeEquals(_sessionTokenHash, candidateHash))
            throw Gate("review.token_invalid", "The local review session token is invalid.");
    }

    private static string[] ReasonsFor(LipSyncReviewChecks checks)
    {
        var reasons = new List<string>(capacity: 4);
        if (!checks.RawAudioUnmasked) reasons.Add("lip_sync.raw_audio_masked");
        if (!checks.PronunciationPassed) reasons.Add("lip_sync.pronunciation");
        if (!checks.SubtitlePassed) reasons.Add("lip_sync.subtitle");
        if (!checks.LipSyncPassed) reasons.Add("lip_sync.mismatch");
        return reasons.ToArray();
    }

    private static LipSyncReviewItemState Freeze(MutableItemState state) =>
        new(
            state.ResponseId,
            state.Revision,
            state.VideoPlaybackCompleted,
            state.VideoPlayedMilliseconds,
            state.AudioPlaybackCompleted,
            state.AudioPlayedMilliseconds,
            state.Decision,
            state.Checks,
            state.ReasonCodes.ToArray());

    private static LoadedLipSyncReviewManifest FreezeManifest(LoadedLipSyncReviewManifest manifest) =>
        new(
            manifest.ManifestId,
            manifest.ManifestContentHash,
            manifest.SourceBundleId,
            manifest.SourceBundleContentHash,
            manifest.Language,
            new ReadOnlyCollection<LipSyncReviewCandidate>(manifest.ReviewableItems
                .Select(FreezeCandidate)
                .ToList()),
            new ReadOnlyCollection<LipSyncBlockedResponse>(manifest.BlockedItems
                .Select(item => new LipSyncBlockedResponse(
                    item.ResponseId,
                    item.SourceReadinessStatus,
                    new ReadOnlyCollection<string>(item.IssueCodes.ToList())))
                .ToList()));

    private static LipSyncReviewCandidate FreezeCandidate(LipSyncReviewCandidate candidate) =>
        candidate with
        {
            SourceNodeIds = new ReadOnlyCollection<string>(candidate.SourceNodeIds.ToList()),
            SourceIssueCodes = new ReadOnlyCollection<string>(candidate.SourceIssueCodes.ToList()),
        };

    private static string Hash(params string[] parts) =>
        "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))))
            .ToLowerInvariant();

    private static LipSyncReviewGateException Gate(string code, string message) => new(code, message);

    private sealed class MutableItemState(string responseId)
    {
        public string ResponseId { get; } = responseId;

        public long Revision { get; set; }

        public bool VideoPlaybackCompleted { get; set; }

        public long VideoPlayedMilliseconds { get; set; }

        public bool AudioPlaybackCompleted { get; set; }

        public long AudioPlayedMilliseconds { get; set; }

        public LipSyncReviewDecision? Decision { get; set; }

        public LipSyncReviewChecks? Checks { get; set; }

        public IReadOnlyList<string> ReasonCodes { get; set; } = [];

        public DeliveryCoverage VideoDelivery { get; } = new();

        public DeliveryCoverage AudioDelivery { get; } = new();
    }

    private sealed class DeliveryCoverage
    {
        private readonly List<(long Start, long End)> _ranges = [];
        private long? _totalLength;

        public bool IsComplete =>
            _totalLength is > 0 &&
            _ranges.Count == 1 &&
            _ranges[0].Start == 0 &&
            _ranges[0].End == _totalLength.Value - 1;

        public void Add(long startInclusive, long endInclusive, long totalLength)
        {
            if (totalLength <= 0 || startInclusive < 0 || endInclusive < startInclusive ||
                endInclusive >= totalLength)
            {
                throw Gate("review.media_delivery_invalid", "The delivered media byte range is invalid.");
            }
            if (_totalLength is { } existing && existing != totalLength)
            {
                throw Gate("review.media_delivery_changed", "The locked media length changed during review.");
            }
            _totalLength = totalLength;
            _ranges.Add((startInclusive, endInclusive));
            _ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            for (int index = 0; index + 1 < _ranges.Count;)
            {
                (long Start, long End) current = _ranges[index];
                (long Start, long End) next = _ranges[index + 1];
                if (next.Start > current.End + 1)
                {
                    index++;
                    continue;
                }
                _ranges[index] = (current.Start, Math.Max(current.End, next.End));
                _ranges.RemoveAt(index + 1);
            }
        }
    }
}

public sealed class LipSyncReviewGateException : InvalidOperationException
{
    public LipSyncReviewGateException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
