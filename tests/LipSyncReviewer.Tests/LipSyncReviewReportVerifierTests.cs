using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Contracts.Generation;

namespace LipSyncReviewer.Tests;

public sealed class LipSyncReviewReportVerifierTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidReportPassesEverySemanticCheck()
    {
        LipSyncReviewReportContract report = ValidReport();

        LipSyncReviewReportVerifier.Verify(report);
    }

    [Theory]
    [InlineData("lip-sync-review-report.pass.json")]
    [InlineData("lip-sync-review-report.blocked.json")]
    public void ValidSchemaFixturesAlsoPassSemanticVerification(string fileName)
    {
        DirectoryInfo root = TestRepository.FindRoot();
        string json = File.ReadAllText(Path.Combine(
            root.FullName,
            "tests",
            "StoryFixtures",
            "valid",
            fileName));
        LipSyncReviewReportContract report = JsonSerializer.Deserialize<LipSyncReviewReportContract>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        LipSyncReviewReportVerifier.Verify(report);
    }

    [Fact]
    public void SelfContentHashMustMatchCanonicalReportContent()
    {
        LipSyncReviewReportContract report = ValidReport() with { PassedCount = 42 };

        AssertCode("report.content_hash", report);
    }

    [Fact]
    public void EventSequencePreviousLinkAndHashAreVerified()
    {
        LipSyncReviewReportContract valid = ValidReport();
        LipSyncReviewEventContract second = valid.Events[1];

        AssertCode(
            "report.event_sequence",
            Rehash(valid with
            {
                Events = [valid.Events[0], second with { Sequence = 3 }],
            }));
        AssertCode(
            "report.event_previous_hash",
            Rehash(valid with
            {
                Events = [valid.Events[0], second with { PreviousEventHash = Hash("wrong-previous") }],
            }));
        AssertCode(
            "report.event_hash",
            Rehash(valid with
            {
                Events = [valid.Events[0], second with { EventHash = Hash("wrong-event") }],
            }));
    }

    [Fact]
    public void EveryEventMustUseTheTopLevelManifestHash()
    {
        LipSyncReviewReportContract valid = ValidReport();
        LipSyncReviewEventContract changed = valid.Events[0] with
        {
            ManifestContentHash = Hash("other-manifest"),
        };
        changed = changed with
        {
            EventHash = LipSyncReviewReportVerifier.ComputeEventHash(changed),
        };
        LipSyncReviewEventContract final = valid.Events[1] with
        {
            PreviousEventHash = changed.EventHash,
        };
        final = final with
        {
            EventHash = LipSyncReviewReportVerifier.ComputeEventHash(final),
        };

        AssertCode(
            "report.event_source_hash",
            Rehash(valid with { Events = [changed, final] }));
    }

    [Fact]
    public void ItemMustMatchItsTerminalDecisionEvent()
    {
        LipSyncReviewReportContract valid = ValidReport();
        LipSyncReviewItemContract item = valid.Items[0] with
        {
            FinalEventHash = Hash("unrelated-final-event"),
        };

        AssertCode(
            "report.item_terminal_event",
            Rehash(valid with { Items = [item] }));
    }

    [Fact]
    public void PlaybackEvidenceMustBeCompleteAndPositive()
    {
        LipSyncReviewReportContract valid = ValidReport();

        AssertCode(
            "report.playback",
            Rehash(valid with
            {
                Items = [valid.Items[0] with { VideoPlaybackCompleted = false }],
            }));
        AssertCode(
            "report.playback",
            Rehash(valid with
            {
                Items = [valid.Items[0] with { AudioPlayedMilliseconds = 0 }],
            }));
    }

    [Fact]
    public void EveryDecisionEventMustContainCompletePositivePlaybackEvidence()
    {
        LipSyncReviewReportContract valid = ValidReport();
        LipSyncReviewEventContract changed = valid.Events[0] with
        {
            VideoPlaybackCompleted = false,
            VideoPlayedMilliseconds = 0,
        };
        changed = changed with
        {
            EventHash = LipSyncReviewReportVerifier.ComputeEventHash(changed),
        };
        LipSyncReviewEventContract final = valid.Events[1] with
        {
            PreviousEventHash = changed.EventHash,
        };
        final = final with
        {
            EventHash = LipSyncReviewReportVerifier.ComputeEventHash(final),
        };
        LipSyncReviewItemContract item = valid.Items[0] with
        {
            FinalEventHash = final.EventHash,
        };

        AssertCode(
            "report.event_playback",
            Rehash(valid with { Items = [item], Events = [changed, final] }));
    }

    [Fact]
    public void CountsMustEqualTheActualInventoryAndDecisions()
    {
        LipSyncReviewReportContract valid = ValidReport();

        AssertCode("report.counts", Rehash(valid with { TotalResponseCount = 99 }));
        AssertCode("report.counts", Rehash(valid with { ReviewableCount = 2 }));
        AssertCode("report.counts", Rehash(valid with { PassedCount = 0, RedoCount = 1 }));
        AssertCode("report.counts", Rehash(valid with { BlockedCount = 1 }));
    }

    [Fact]
    public void OverallStatusMustBeDerivedAndBlockedCannotVacuouslyPass()
    {
        LipSyncReviewReportContract valid = ValidReport();
        LipSyncReviewReportContract blocked = ValidBlockedReport();

        AssertCode(
            "report.status",
            Rehash(valid with { OverallStatus = "redo_required" }));
        AssertCode(
            "report.status",
            Rehash(blocked with { OverallStatus = "lip_sync_passed" }));
        LipSyncReviewReportVerifier.Verify(blocked);
    }

    [Fact]
    public void ReportCannotClaimProductionOrPromotionAuthority()
    {
        LipSyncReviewReportContract valid = ValidReport();

        AssertCode(
            "report.metadata",
            Rehash(valid with { ProductionReadiness = "ready" }));
        AssertCode(
            "report.metadata",
            Rehash(valid with { Authority = "production_promotion_authority" }));
    }

    private static void AssertCode(string expectedCode, LipSyncReviewReportContract report)
    {
        LipSyncReviewGateException exception = Assert.Throws<LipSyncReviewGateException>(
            () => LipSyncReviewReportVerifier.Verify(report));
        Assert.Equal(expectedCode, exception.Code);
    }

    private static LipSyncReviewReportContract ValidReport()
    {
        string manifestHash = Hash("manifest");
        string dialogueHash = Hash("dialogue");
        string presentationHash = Hash("presentation");
        LipSyncReviewChecksContract failedChecks = new(true, true, true, false);
        LipSyncReviewChecksContract passedChecks = new(true, true, true, true);
        LipSyncReviewEventContract first = Event(
            sequence: 1,
            revision: 3,
            decision: "redo",
            checks: failedChecks,
            reasons: ["lip_sync.mismatch"],
            previousHash: null,
            recordedAt: StartedAt.AddMinutes(1),
            manifestHash,
            dialogueHash,
            presentationHash);
        LipSyncReviewEventContract second = Event(
            sequence: 2,
            revision: 4,
            decision: "pass",
            checks: passedChecks,
            reasons: [],
            previousHash: first.EventHash,
            recordedAt: StartedAt.AddMinutes(2),
            manifestHash,
            dialogueHash,
            presentationHash);
        var item = new LipSyncReviewItemContract(
            "response.fixture",
            ["node.fixture"],
            new LipSyncReviewLocalizedValueContract(
                "story.fixture.response",
                "锁定台词",
                dialogueHash),
            "story.fixture.emotion",
            "平静",
            new LipSyncReviewMediaPinContract("video.fixture", "v1", Hash("video"), "video"),
            new LipSyncReviewMediaPinContract("audio.fixture", "v1", Hash("audio"), "audio"),
            new LipSyncReviewMediaPinContract("fallback.fixture", "v1", Hash("fallback"), "image"),
            presentationHash,
            "needs_review",
            ["human_review.required"],
            VideoPlaybackCompleted: true,
            VideoPlayedMilliseconds: 8_000,
            AudioPlaybackCompleted: true,
            AudioPlayedMilliseconds: 7_500,
            Decision: "pass",
            Checks: passedChecks,
            ReasonCodes: [],
            ReviewedAtUtc: second.RecordedAtUtc,
            FinalEventHash: second.EventHash);
        var report = new LipSyncReviewReportContract(
            "1.0.0",
            "lip-sync-review.fixture",
            "cx503.1",
            "pronunciation_subtitle_lip_sync",
            "not_evaluated",
            "human_review_export_not_promotion_authority",
            "lip_sync_passed",
            "manifest.fixture",
            manifestHash,
            "bundle.fixture",
            Hash("bundle"),
            "zh-CN",
            Hash("reviewer"),
            StartedAt,
            StartedAt.AddMinutes(3),
            TotalResponseCount: 1,
            ReviewableCount: 1,
            PassedCount: 1,
            RedoCount: 0,
            BlockedCount: 0,
            Items: [item],
            BlockedItems: [],
            Events: [first, second],
            ContentHash: string.Empty);
        return Rehash(report);
    }

    private static LipSyncReviewReportContract ValidBlockedReport()
    {
        var report = new LipSyncReviewReportContract(
            "1.0.0",
            "lip-sync-review.blocked",
            "cx503.1",
            "pronunciation_subtitle_lip_sync",
            "not_evaluated",
            "human_review_export_not_promotion_authority",
            "blocked",
            "manifest.fixture",
            Hash("manifest"),
            "bundle.fixture",
            Hash("bundle"),
            "zh-CN",
            null,
            StartedAt,
            StartedAt,
            TotalResponseCount: 1,
            ReviewableCount: 0,
            PassedCount: 0,
            RedoCount: 0,
            BlockedCount: 1,
            Items: [],
            BlockedItems:
            [
                new LipSyncBlockedResponseContract(
                    "response.blocked",
                    "blocked",
                    ["audio.missing"]),
            ],
            Events: [],
            ContentHash: string.Empty);
        return Rehash(report);
    }

    private static LipSyncReviewEventContract Event(
        long sequence,
        long revision,
        string decision,
        LipSyncReviewChecksContract checks,
        IReadOnlyList<string> reasons,
        string? previousHash,
        DateTimeOffset recordedAt,
        string manifestHash,
        string dialogueHash,
        string presentationHash)
    {
        var value = new LipSyncReviewEventContract(
            sequence,
            "response.fixture",
            manifestHash,
            dialogueHash,
            presentationHash,
            revision,
            VideoPlaybackCompleted: true,
            VideoPlayedMilliseconds: 8_000,
            AudioPlaybackCompleted: true,
            AudioPlayedMilliseconds: 7_500,
            decision,
            checks,
            reasons,
            recordedAt,
            previousHash,
            string.Empty);
        return value with
        {
            EventHash = LipSyncReviewReportVerifier.ComputeEventHash(value),
        };
    }

    private static LipSyncReviewReportContract Rehash(LipSyncReviewReportContract report) =>
        report with
        {
            ContentHash = LipSyncReviewReportVerifier.ComputeContentHash(report),
        };

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
