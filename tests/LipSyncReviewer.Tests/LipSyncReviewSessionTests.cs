using System.Security.Cryptography;

namespace LipSyncReviewer.Tests;

public sealed class LipSyncReviewSessionTests
{
    private const string Token = "test-session-token-1234567890";

    [Fact]
    public void DecisionRequiresOriginalVideoAndReferenceAudioPlaybackCompletion()
    {
        LipSyncReviewSession session = CreateSession();

        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => session.RecordDecision(
                new LipSyncReviewDecisionRequest(
                    "response.fixture",
                    ExpectedRevision: 0,
                    LipSyncReviewDecision.Pass,
                    AllPassed()),
                Token));

        Assert.Equal("review.playback_incomplete", exception.Code);
    }

    [Fact]
    public void PassRequiresAllChecksAndRedoReasonsAreServerDerived()
    {
        LipSyncReviewSession session = CreateSession();
        DeliverAll(session);
        LipSyncReviewItemState video = session.CompletePlayback(
            "response.fixture",
            expectedRevision: 0,
            LipSyncPlaybackKind.OriginalVideo,
            playedMilliseconds: 8_000,
            Token);
        LipSyncReviewItemState audio = session.CompletePlayback(
            "response.fixture",
            video.Revision,
            LipSyncPlaybackKind.ReferenceAudio,
            playedMilliseconds: 7_500,
            Token);
        var failedChecks = AllPassed() with
        {
            PronunciationPassed = false,
            LipSyncPassed = false,
        };

        LipSyncReviewGateException barePass = Assert.Throws<LipSyncReviewGateException>(
            () => session.RecordDecision(
                new LipSyncReviewDecisionRequest(
                    "response.fixture",
                    audio.Revision,
                    LipSyncReviewDecision.Pass,
                    failedChecks),
                Token));
        LipSyncReviewItemState redo = session.RecordDecision(
            new LipSyncReviewDecisionRequest(
                "response.fixture",
                audio.Revision,
                LipSyncReviewDecision.Redo,
                failedChecks),
            Token);

        Assert.Equal("review.pass_checks", barePass.Code);
        Assert.Equal(LipSyncReviewDecision.Redo, redo.Decision);
        Assert.Equal(
            ["lip_sync.pronunciation", "lip_sync.mismatch"],
            redo.ReasonCodes);
    }

    [Fact]
    public void InvalidTokenStaleRevisionAndZeroPlaybackAreRejected()
    {
        LipSyncReviewSession session = CreateSession();
        DeliverAll(session);

        LipSyncReviewGateException invalidToken = Assert.Throws<LipSyncReviewGateException>(
            () => session.CompletePlayback(
                "response.fixture",
                0,
                LipSyncPlaybackKind.OriginalVideo,
                8_000,
                "wrong-token"));
        LipSyncReviewGateException zeroPlayback = Assert.Throws<LipSyncReviewGateException>(
            () => session.CompletePlayback(
                "response.fixture",
                0,
                LipSyncPlaybackKind.OriginalVideo,
                0,
                Token));
        LipSyncReviewItemState first = session.CompletePlayback(
            "response.fixture",
            0,
            LipSyncPlaybackKind.OriginalVideo,
            8_000,
            Token);
        LipSyncReviewGateException stale = Assert.Throws<LipSyncReviewGateException>(
            () => session.CompletePlayback(
                "response.fixture",
                0,
                LipSyncPlaybackKind.ReferenceAudio,
                7_500,
                Token));

        Assert.Equal("review.token_invalid", invalidToken.Code);
        Assert.Equal("review.playback_duration", zeroPlayback.Code);
        Assert.Equal("review.revision_stale", stale.Code);
        Assert.Equal(1, first.Revision);
    }

    [Fact]
    public void DecisionEventHashBindsLockedDialogueAndPresentation()
    {
        LipSyncReviewSession first = CreateSession("presentation.one");
        LipSyncReviewSession second = CreateSession("presentation.two");
        CompleteAndPass(first);
        CompleteAndPass(second);

        Assert.NotEqual(
            Assert.Single(first.Events()).EventHash,
            Assert.Single(second.Events()).EventHash);
    }

    [Fact]
    public void PlaybackCompletionRequiresFullServerMediaDelivery()
    {
        LipSyncReviewSession session = CreateSession();

        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => session.CompletePlayback(
                "response.fixture",
                expectedRevision: 0,
                LipSyncPlaybackKind.OriginalVideo,
                playedMilliseconds: 8_000,
                Token));

        Assert.Equal("review.media_not_fully_delivered", exception.Code);
    }

    [Fact]
    public void DecisionFreezesPlaybackState()
    {
        LipSyncReviewSession session = CreateSession();
        CompleteAndPass(session);
        LipSyncReviewItemState decided = Assert.Single(session.Snapshot());

        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => session.CompletePlayback(
                decided.ResponseId,
                decided.Revision,
                LipSyncPlaybackKind.OriginalVideo,
                playedMilliseconds: 8_000,
                Token));

        Assert.Equal("review.decision_already_recorded", exception.Code);
    }

    [Fact]
    public void DecisionEventHashBindsPlaybackEvidence()
    {
        LipSyncReviewSession first = CreateSession();
        LipSyncReviewSession second = CreateSession();
        CompleteAndPass(first, videoMilliseconds: 8_000, audioMilliseconds: 7_500);
        CompleteAndPass(second, videoMilliseconds: 8_250, audioMilliseconds: 7_750);

        Assert.NotEqual(
            Assert.Single(first.Events()).EventHash,
            Assert.Single(second.Events()).EventHash);
    }

    [Fact]
    public void MediaLookupCannotBeDowncastAndMutated()
    {
        LipSyncReviewSession session = CreateSession();
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, LipSyncReviewMediaItem>>(
            session.MediaByResponseId);

        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Single(session.MediaByResponseId);
    }

    [Fact]
    public void ExportCaptureSealsDecisionsAndRetriesTheSameSnapshot()
    {
        LipSyncReviewSession session = CreateSession();
        CompleteAndPass(session);
        LipSyncReviewExportSnapshot first = session.CaptureForReport();
        LipSyncReviewItemState decided = Assert.Single(first.States);

        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => session.RecordDecision(
                new LipSyncReviewDecisionRequest(
                    decided.ResponseId,
                    decided.Revision,
                    LipSyncReviewDecision.Redo,
                    AllPassed() with { LipSyncPassed = false }),
                Token));

        Assert.Equal("review.session_sealed", exception.Code);
        Assert.Same(first, session.CaptureForReport());
    }

    private static LipSyncReviewSession CreateSession(string presentationSeed = "presentation")
    {
        LipSyncReviewCandidate candidate = Candidate(presentationSeed);
        LoadedLipSyncReviewManifest manifest = new(
            "manifest.fixture",
            Hash("manifest"),
            "bundle.fixture",
            Hash("bundle"),
            "zh-CN",
            [candidate],
            []);
        return new LipSyncReviewSession(
            manifest,
            [new LipSyncReviewMediaItem(
                candidate,
                "video.mp4",
                "audio.wav",
                "fallback.png",
                LipSyncLength: 100,
                AudioLength: 100,
                LipSyncDurationMilliseconds: 8_000,
                AudioDurationMilliseconds: 7_500)],
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)),
            Token);
    }

    private static void CompleteAndPass(
        LipSyncReviewSession session,
        long videoMilliseconds = 8_000,
        long audioMilliseconds = 7_500)
    {
        DeliverAll(session);
        LipSyncReviewItemState video = session.CompletePlayback(
            "response.fixture",
            0,
            LipSyncPlaybackKind.OriginalVideo,
            videoMilliseconds,
            Token);
        LipSyncReviewItemState audio = session.CompletePlayback(
            "response.fixture",
            video.Revision,
            LipSyncPlaybackKind.ReferenceAudio,
            audioMilliseconds,
            Token);
        session.RecordDecision(
            new LipSyncReviewDecisionRequest(
                "response.fixture",
                audio.Revision,
                LipSyncReviewDecision.Pass,
                AllPassed()),
            Token);
    }

    private static void DeliverAll(LipSyncReviewSession session)
    {
        session.RecordMediaDelivery(
            "response.fixture",
            LipSyncPlaybackKind.OriginalVideo,
            startInclusive: 0,
            endInclusive: 99,
            totalLength: 100);
        session.RecordMediaDelivery(
            "response.fixture",
            LipSyncPlaybackKind.ReferenceAudio,
            startInclusive: 0,
            endInclusive: 99,
            totalLength: 100);
    }

    private static LipSyncReviewCandidate Candidate(string presentationSeed) =>
        new(
            "response.fixture",
            ["node.fixture"],
            new LipSyncReviewLocalizedValue("story.fixture.response", "锁定台词"),
            new LipSyncReviewLocalizedValue("story.fixture.emotion", "平静"),
            Hash("dialogue"),
            new LipSyncReviewMediaPin("video.fixture", "v1", Hash("video"), "video"),
            new LipSyncReviewMediaPin("audio.fixture", "v1", Hash("audio"), "audio"),
            new LipSyncReviewMediaPin("fallback.fixture", "v1", Hash("fallback"), "image"),
            "needs_review",
            ["human_review.required"],
            Hash(presentationSeed));

    private static LipSyncReviewChecks AllPassed() => new(true, true, true, true);

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
