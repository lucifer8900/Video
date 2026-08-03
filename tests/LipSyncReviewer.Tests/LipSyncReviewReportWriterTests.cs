using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LipSyncReviewer.Tests;

public sealed class LipSyncReviewReportWriterTests
{
    private const string Token = "test-report-token-1234567890";
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IncompleteSessionCannotExport()
    {
        using var output = new TemporaryDirectory();
        LipSyncReviewSession session = ReviewableSession();

        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => Writer().Write(
                session,
                Path.Combine(output.Path, "lip-sync-review.report.json"),
                Hash("reviewer")));

        Assert.Equal("review.incomplete", exception.Code);
        Assert.Empty(Directory.EnumerateFiles(output.Path));
    }

    [Fact]
    public void RedoReportIsPinnedHasEventChainAndContainsNoLocalPaths()
    {
        using var output = new TemporaryDirectory();
        LipSyncReviewSession session = ReviewableSession();
        CompletePlayback(session);
        LipSyncReviewItemState state = session.Snapshot()[0];
        session.RecordDecision(
            new LipSyncReviewDecisionRequest(
                state.ResponseId,
                state.Revision,
                LipSyncReviewDecision.Redo,
                new LipSyncReviewChecks(
                    RawAudioUnmasked: true,
                    PronunciationPassed: true,
                    SubtitlePassed: true,
                    LipSyncPassed: false)),
            Token);
        string reportPath = Path.Combine(output.Path, "lip-sync-review.report.json");

        WrittenLipSyncReviewReport written = Writer().Write(
            session,
            reportPath,
            Hash("reviewer"));

        Assert.Equal("redo_required", written.Report.OverallStatus);
        Assert.Equal("not_evaluated", written.Report.ProductionReadiness);
        Assert.Equal("human_review_export_not_promotion_authority", written.Report.Authority);
        Assert.Equal("lip_sync.mismatch", Assert.Single(written.Report.Items).ReasonCodes.Single());
        Assert.Single(written.Report.Events);
        Assert.DoesNotContain("C:\\", written.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("video.mp4", written.Json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(written.Json, File.ReadAllText(reportPath));
        Assert.StartsWith("sha256:", written.Report.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingReportIsNeverOverwritten()
    {
        using var output = new TemporaryDirectory();
        LipSyncReviewSession session = ReviewableSession();
        CompletePlayback(session);
        LipSyncReviewItemState state = session.Snapshot()[0];
        session.RecordDecision(
            new LipSyncReviewDecisionRequest(
                state.ResponseId,
                state.Revision,
                LipSyncReviewDecision.Pass,
                new LipSyncReviewChecks(true, true, true, true)),
            Token);
        string path = Path.Combine(output.Path, "lip-sync-review.report.json");
        LipSyncReviewReportWriter writer = Writer();
        writer.Write(session, path, Hash("reviewer"));
        byte[] original = File.ReadAllBytes(path);

        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => writer.Write(session, path, Hash("reviewer")));

        Assert.Equal("report.exists", exception.Code);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.DoesNotContain(Directory.EnumerateFiles(output.Path), file => file.EndsWith(".tmp"));
    }

    [Fact]
    public async Task ActualBlockedManifestExportsBlockedInsteadOfVacuousPass()
    {
        using var output = new TemporaryDirectory();
        DirectoryInfo root = TestRepository.FindRoot();
        LoadedLipSyncReviewManifest manifest = await new LipSyncReviewManifestLoader(
                Path.Combine(root.FullName, "server", "Contracts", "schemas"))
            .LoadAsync(
                Path.Combine(root.FullName, "content", "story", "red-mist", "shot-manifest.json"),
                CancellationToken.None);
        var session = new LipSyncReviewSession(
            manifest,
            [],
            new FixedTimeProvider(FixedNow),
            Token);

        WrittenLipSyncReviewReport written = Writer().Write(
            session,
            Path.Combine(output.Path, "lip-sync-review.report.json"),
            reviewerSubject: null);

        Assert.Equal("blocked", written.Report.OverallStatus);
        Assert.Equal(30, written.Report.TotalResponseCount);
        Assert.Equal(30, written.Report.BlockedCount);
        Assert.Equal(0, written.Report.PassedCount);
        Assert.Empty(written.Report.Items);
        Assert.Null(written.Report.ReviewerSubject);
    }

    private static LipSyncReviewReportWriter Writer() =>
        new(new FixedTimeProvider(FixedNow));

    private static LipSyncReviewSession ReviewableSession()
    {
        LipSyncReviewCandidate candidate = Candidate();
        var manifest = new LoadedLipSyncReviewManifest(
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
                "C:\\secret\\video.mp4",
                "C:\\secret\\audio.wav",
                "C:\\secret\\fallback.png",
                LipSyncLength: 100,
                AudioLength: 100,
                LipSyncDurationMilliseconds: 8_000,
                AudioDurationMilliseconds: 7_500)],
            new FixedTimeProvider(FixedNow),
            Token);
    }

    private static void CompletePlayback(LipSyncReviewSession session)
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
        LipSyncReviewItemState video = session.CompletePlayback(
            "response.fixture",
            0,
            LipSyncPlaybackKind.OriginalVideo,
            8_000,
            Token);
        session.CompletePlayback(
            "response.fixture",
            video.Revision,
            LipSyncPlaybackKind.ReferenceAudio,
            7_500,
            Token);
    }

    private static LipSyncReviewCandidate Candidate() =>
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
            ["human_review.required", "source.original_missing"],
            Hash("presentation"));

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cx503-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
