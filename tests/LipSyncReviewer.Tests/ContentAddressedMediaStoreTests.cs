using System.Security.Cryptography;

namespace LipSyncReviewer.Tests;

public sealed class ContentAddressedMediaStoreTests
{
    [Fact]
    public async Task ExactContentAddressedMediaOpensAndRemainsGuardedUntilDisposed()
    {
        await using var fixture = new MediaStoreFixture();
        StoredMedia video = fixture.Write(".mp4", [0, 0, 0, 24, .. "ftypisom"u8.ToArray()]);
        StoredMedia audio = fixture.Write(".wav", [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WAVE"u8.ToArray()]);
        StoredMedia fallback = fixture.Write(".png", [137, 80, 78, 71, 13, 10, 26, 10]);
        LoadedLipSyncReviewManifest manifest = FixtureManifest(video, audio, fallback);

        await using LipSyncReviewMediaLease lease = await new ContentAddressedMediaStore(new StubProbe())
            .OpenAsync(manifest, fixture.Root, CancellationToken.None);

        LipSyncReviewMediaItem item = Assert.Single(lease.Items);
        Assert.Equal(Path.GetFullPath(video.Path), item.LipSyncPath);
        Assert.Equal(Path.GetFullPath(audio.Path), item.AudioPath);
        Assert.Equal(Path.GetFullPath(fallback.Path), item.FallbackPath);
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<IOException>(
                () => File.Open(video.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        }
    }

    [Fact]
    public async Task HashMismatchIsRejected()
    {
        await using var fixture = new MediaStoreFixture();
        StoredMedia video = fixture.Write(".mp4", [0, 0, 0, 24, .. "ftypisom"u8.ToArray()]);
        StoredMedia audio = fixture.Write(".wav", [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WAVE"u8.ToArray()]);
        StoredMedia fallback = fixture.Write(".png", [137, 80, 78, 71, 13, 10, 26, 10]);
        await File.WriteAllBytesAsync(video.Path, "tampered"u8.ToArray());

        LipSyncReviewInputException exception =
            await Assert.ThrowsAsync<LipSyncReviewInputException>(
                () => new ContentAddressedMediaStore(new StubProbe()).OpenAsync(
                    FixtureManifest(video, audio, fallback),
                    fixture.Root,
                    CancellationToken.None));

        Assert.Equal("media.hash_mismatch", exception.Code);
    }

    [Fact]
    public async Task MultipleAllowedFilesForOneHashAreRejectedAsAmbiguous()
    {
        await using var fixture = new MediaStoreFixture();
        StoredMedia video = fixture.Write(".mp4", [0, 0, 0, 24, .. "ftypisom"u8.ToArray()]);
        File.Copy(video.Path, Path.ChangeExtension(video.Path, ".webm"));
        StoredMedia audio = fixture.Write(".wav", [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WAVE"u8.ToArray()]);
        StoredMedia fallback = fixture.Write(".png", [137, 80, 78, 71, 13, 10, 26, 10]);

        LipSyncReviewInputException exception =
            await Assert.ThrowsAsync<LipSyncReviewInputException>(
                () => new ContentAddressedMediaStore(new StubProbe()).OpenAsync(
                    FixtureManifest(video, audio, fallback),
                    fixture.Root,
                    CancellationToken.None));

        Assert.Equal("media.ambiguous", exception.Code);
    }

    [Fact]
    public async Task MediaExtensionCannotDisguiseMalformedContainer()
    {
        await using var fixture = new MediaStoreFixture();
        StoredMedia video = fixture.Write(".mp4", "not-an-mp4"u8.ToArray());
        StoredMedia audio = fixture.Write(".wav", "not-a-wav"u8.ToArray());
        StoredMedia fallback = fixture.Write(".png", [137, 80, 78, 71, 13, 10, 26, 10]);

        LipSyncReviewInputException exception =
            await Assert.ThrowsAsync<LipSyncReviewInputException>(
                () => new ContentAddressedMediaStore(new StubProbe()).OpenAsync(
                    FixtureManifest(video, audio, fallback),
                    fixture.Root,
                    CancellationToken.None));

        Assert.Equal("media.container_invalid", exception.Code);
    }

    private static LoadedLipSyncReviewManifest FixtureManifest(
        StoredMedia video,
        StoredMedia audio,
        StoredMedia fallback)
    {
        var candidate = new LipSyncReviewCandidate(
            "response.fixture",
            ["node.fixture"],
            new LipSyncReviewLocalizedValue("story.fixture.response", "锁定台词"),
            new LipSyncReviewLocalizedValue("story.fixture.emotion", "平静"),
            Hash("dialogue"u8.ToArray()),
            new LipSyncReviewMediaPin("video.fixture", "v1", video.Hash, "video"),
            new LipSyncReviewMediaPin("audio.fixture", "v1", audio.Hash, "audio"),
            new LipSyncReviewMediaPin("fallback.fixture", "v1", fallback.Hash, "image"),
            "needs_review",
            ["human_review.required"],
            Hash("presentation"u8.ToArray()));
        return new LoadedLipSyncReviewManifest(
            "manifest.fixture",
            Hash("manifest"u8.ToArray()),
            "bundle.fixture",
            Hash("bundle"u8.ToArray()),
            "zh-CN",
            [candidate],
            []);
    }

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record StoredMedia(string Hash, string Path);

    private sealed class StubProbe : IReviewMediaProbe
    {
        public Task<ReviewMediaProbeResult> ProbeAsync(
            Stream media,
            string mediaType,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReviewMediaProbeResult(
                mediaType == "audio" ? 7_500 : 8_000));
    }

    private sealed class MediaStoreFixture : IAsyncDisposable
    {
        public MediaStoreFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "cx503-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public StoredMedia Write(string extension, byte[] bytes)
        {
            string hash = Hash(bytes);
            string hex = hash["sha256:".Length..];
            string directory = Path.Combine(Root, "media", "sha256", hex[..2]);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, hex + extension);
            File.WriteAllBytes(path, bytes);
            return new StoredMedia(hash, path);
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
