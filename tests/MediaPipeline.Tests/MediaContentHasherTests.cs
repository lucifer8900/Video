using System.Text;
using Lingmai.RedMist.MediaPipeline;
using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class MediaContentHasherTests
{
    private const string AbcSha256 =
        "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public async Task ComputesLowercasePrefixedSha256FromAStream()
    {
        var hasher = new MediaContentHasher(bufferSizeBytes: 16);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("abc"));

        string hash = await hasher.ComputeSha256Async(content, CancellationToken.None);

        Assert.Equal(AbcSha256, hash);
    }

    [Fact]
    public async Task HashingIsStreamingAndDoesNotRequireLengthOrSeek()
    {
        byte[] bytes = Enumerable.Range(0, 10_000).Select(value => (byte)(value % 251)).ToArray();
        var hasher = new MediaContentHasher(bufferSizeBytes: 64);
        await using var content = new ChunkedReadStream(bytes, maximumChunkBytes: 7);

        string hash = await hasher.ComputeSha256Async(content, CancellationToken.None);

        Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
        Assert.Equal(71, hash.Length);
        Assert.InRange(content.LargestRequestedRead, 1, 64);
    }

    [Fact]
    public async Task IntegrityCheckRejectsTamperedContent()
    {
        var hasher = new MediaContentHasher(bufferSizeBytes: 16);
        await using var original = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        await hasher.EnsureMatchesSha256Async(original, AbcSha256, CancellationToken.None);
        await using var tampered = new MemoryStream(Encoding.UTF8.GetBytes("abd"));

        MediaIntegrityException error = await Assert.ThrowsAsync<MediaIntegrityException>(
            () => hasher.EnsureMatchesSha256Async(
                tampered,
                AbcSha256,
                CancellationToken.None));

        Assert.Equal("media.hash_mismatch", error.Code);
        Assert.DoesNotContain(AbcSha256, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        var hasher = new MediaContentHasher(bufferSizeBytes: 16);
        await using var content = new MemoryStream(new byte[128]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hasher.ComputeSha256Async(content, cancellation.Token));
    }
}
