using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LipSyncReviewer.Tests;

public sealed class LipSyncReviewWebHostTests
{
    private const string Token = "web-host-test-token-1234567890";
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoopbackHostSupportsRangeAndCompletesReviewWithoutLeakingPaths()
    {
        await using var fixture = await WebHostFixture.CreateAsync();
        await using RunningLipSyncReviewHost host = await LipSyncReviewWebHost.StartAsync(
            fixture.Session,
            fixture.MediaLease,
            new LipSyncReviewReportWriter(new FixedTimeProvider(FixedNow)),
            fixture.ReportPath,
            Hash("reviewer"),
            Token,
            port: 0,
            CancellationToken.None);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(host.BaseAddress.Host)));
        HttpResponseMessage page = await client.GetAsync("/");
        string html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("default-src 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("逐条口型审核", html, StringComparison.Ordinal);

        using var hostileHostRequest = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        hostileHostRequest.Headers.Host = "attacker.invalid";
        HttpResponseMessage hostileHost = await client.SendAsync(hostileHostRequest);
        Assert.Equal(HttpStatusCode.BadRequest, hostileHost.StatusCode);

        string script = await client.GetStringAsync("/review.js");
        Assert.Contains("textContent", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);

        string sessionJson = await client.GetStringAsync("/api/session");
        Assert.DoesNotContain(fixture.Root, sessionJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script>", sessionJson, StringComparison.OrdinalIgnoreCase);
        using JsonDocument sessionDocument = JsonDocument.Parse(sessionJson);
        JsonElement item = sessionDocument.RootElement.GetProperty("items")[0];
        Assert.Equal("response.fixture", item.GetProperty("responseId").GetString());
        Assert.Equal("/media/response.fixture/video", item.GetProperty("videoUrl").GetString());

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "/media/response.fixture/video");
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
        HttpResponseMessage range = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal(4, (await range.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(
            "same-origin",
            range.Headers.GetValues("Cross-Origin-Resource-Policy").Single());

        HttpResponseMessage forbidden = await client.PostAsJsonAsync(
            "/api/playback",
            new { responseId = "response.fixture", expectedRevision = 0, kind = "original_video", playedMilliseconds = 8000 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        HttpResponseMessage incompleteDelivery = await PostAsync(
            client,
            "/api/playback",
            new { responseId = "response.fixture", expectedRevision = 0, kind = "original_video", playedMilliseconds = 8000 });
        Assert.Equal(HttpStatusCode.BadRequest, incompleteDelivery.StatusCode);

        _ = await client.GetByteArrayAsync("/media/response.fixture/video");
        _ = await client.GetByteArrayAsync("/media/response.fixture/audio");

        HttpResponseMessage forgedDuration = await PostAsync(
            client,
            "/api/playback",
            new { responseId = "response.fixture", expectedRevision = 0, kind = "original_video", playedMilliseconds = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, forgedDuration.StatusCode);

        HttpResponseMessage videoPlayback = await PostAsync(
            client,
            "/api/playback",
            new { responseId = "response.fixture", expectedRevision = 0, kind = "original_video", playedMilliseconds = 8000 });
        Assert.Equal(HttpStatusCode.OK, videoPlayback.StatusCode);
        HttpResponseMessage audioPlayback = await PostAsync(
            client,
            "/api/playback",
            new { responseId = "response.fixture", expectedRevision = 1, kind = "reference_audio", playedMilliseconds = 7500 });
        Assert.Equal(HttpStatusCode.OK, audioPlayback.StatusCode);
        HttpResponseMessage decision = await PostAsync(
            client,
            "/api/decision",
            new
            {
                responseId = "response.fixture",
                expectedRevision = 2,
                requestedDecision = "pass",
                checks = new
                {
                    rawAudioUnmasked = true,
                    pronunciationPassed = true,
                    subtitlePassed = true,
                    lipSyncPassed = true,
                },
            });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        HttpResponseMessage export = await PostAsync(client, "/api/export", new { });
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.True(File.Exists(fixture.ReportPath));
        Assert.DoesNotContain(fixture.Root, File.ReadAllText(fixture.ReportPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NullChecksReturnStableBadRequestWithoutPathDisclosure()
    {
        await using var fixture = await WebHostFixture.CreateAsync();
        await using RunningLipSyncReviewHost host = await LipSyncReviewWebHost.StartAsync(
            fixture.Session,
            fixture.MediaLease,
            new LipSyncReviewReportWriter(new FixedTimeProvider(FixedNow)),
            fixture.ReportPath,
            Hash("reviewer"),
            Token,
            port: 0,
            CancellationToken.None);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        HttpResponseMessage response = await PostAsync(
            client,
            "/api/decision",
            new
            {
                responseId = "response.fixture",
                expectedRevision = 0,
                requestedDecision = "pass",
                checks = (object?)null,
            });
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("review.request_invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, body, StringComparison.OrdinalIgnoreCase);

        HttpResponseMessage missingCheck = await PostAsync(
            client,
            "/api/decision",
            new
            {
                responseId = "response.fixture",
                expectedRevision = 0,
                requestedDecision = "redo",
                checks = new
                {
                    rawAudioUnmasked = true,
                    pronunciationPassed = true,
                    subtitlePassed = true,
                },
            });
        string missingBody = await missingCheck.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, missingCheck.StatusCode);
        Assert.Contains("review.request_invalid", missingBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostRejectsSessionAndLeaseFromDifferentMediaBindings()
    {
        await using var fixture = await WebHostFixture.CreateAsync();
        LipSyncReviewCandidate candidate = fixture.Session.Manifest.ReviewableItems[0] with
        {
            PresentationHash = Hash("different-presentation"),
        };
        var manifest = new LoadedLipSyncReviewManifest(
            "manifest.other",
            Hash("manifest.other"),
            "bundle.fixture",
            Hash("bundle"),
            "zh-CN",
            [candidate],
            []);
        LipSyncReviewMediaItem source = fixture.MediaLease.Items[0];
        var mismatchedSession = new LipSyncReviewSession(
            manifest,
            [source with { Candidate = candidate }],
            new FixedTimeProvider(FixedNow),
            Token);

        LipSyncReviewInputException exception;
        try
        {
            await using RunningLipSyncReviewHost host = await LipSyncReviewWebHost.StartAsync(
                mismatchedSession,
                fixture.MediaLease,
                new LipSyncReviewReportWriter(new FixedTimeProvider(FixedNow)),
                fixture.ReportPath,
                Hash("reviewer"),
                Token,
                port: 0,
                CancellationToken.None);
            Assert.Fail("A mismatched session/media lease must not start a reviewer host.");
            return;
        }
        catch (LipSyncReviewInputException caught)
        {
            exception = caught;
        }

        Assert.Equal("review.media_session_mismatch", exception.Code);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Lip-Sync-Review-Token", Token);
        return await client.SendAsync(request);
    }

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class WebHostFixture : IAsyncDisposable
    {
        private WebHostFixture(
            string root,
            LipSyncReviewMediaLease mediaLease,
            LipSyncReviewSession session)
        {
            Root = root;
            MediaLease = mediaLease;
            Session = session;
            ReportPath = Path.Combine(root, "lip-sync-review.report.json");
        }

        public string Root { get; }

        public string ReportPath { get; }

        public LipSyncReviewMediaLease MediaLease { get; }

        public LipSyncReviewSession Session { get; }

        public static async Task<WebHostFixture> CreateAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "cx503-web-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            StoredMedia video = Write(root, ".mp4", [0, 0, 0, 24, .. "ftypisom"u8.ToArray()]);
            StoredMedia audio = Write(root, ".wav", [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WAVE"u8.ToArray()]);
            StoredMedia fallback = Write(root, ".png", [137, 80, 78, 71, 13, 10, 26, 10]);
            var candidate = new LipSyncReviewCandidate(
                "response.fixture",
                ["node.fixture"],
                new LipSyncReviewLocalizedValue("story.fixture.response", "</script><img src=x onerror=alert(1)>"),
                new LipSyncReviewLocalizedValue("story.fixture.emotion", "平静"),
                Hash("dialogue"),
                new LipSyncReviewMediaPin("video.fixture", "v1", video.Hash, "video"),
                new LipSyncReviewMediaPin("audio.fixture", "v1", audio.Hash, "audio"),
                new LipSyncReviewMediaPin("fallback.fixture", "v1", fallback.Hash, "image"),
                "needs_review",
                ["human_review.required"],
                Hash("presentation"));
            var manifest = new LoadedLipSyncReviewManifest(
                "manifest.fixture",
                Hash("manifest"),
                "bundle.fixture",
                Hash("bundle"),
                "zh-CN",
                [candidate],
                []);
            LipSyncReviewMediaLease lease = await new ContentAddressedMediaStore(new StubProbe())
                .OpenAsync(manifest, root, CancellationToken.None);
            var session = new LipSyncReviewSession(
                manifest,
                lease.Items,
                new FixedTimeProvider(FixedNow),
                Token);
            return new WebHostFixture(root, lease, session);
        }

        public async ValueTask DisposeAsync()
        {
            await MediaLease.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }

        private static StoredMedia Write(string root, string extension, byte[] bytes)
        {
            string hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string hex = hash["sha256:".Length..];
            string directory = Path.Combine(root, "media", "sha256", hex[..2]);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, hex + extension);
            File.WriteAllBytes(path, bytes);
            return new StoredMedia(hash);
        }

        private sealed record StoredMedia(string Hash);
    }

    private sealed class StubProbe : IReviewMediaProbe
    {
        public Task<ReviewMediaProbeResult> ProbeAsync(
            Stream media,
            string mediaType,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReviewMediaProbeResult(
                mediaType == "audio" ? 7_500 : 8_000));
    }
}
