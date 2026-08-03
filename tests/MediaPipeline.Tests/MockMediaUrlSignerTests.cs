using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class MockMediaUrlSignerTests
{
    [Fact]
    public async Task MockSignerIsDeterministicOfflineAndHonorsTtl()
    {
        var now = new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
        var signer = new MockMediaUrlSigner(
            new Uri("https://offline-media.example.test/"),
            new FixedTimeProvider(now),
            TimeSpan.FromMinutes(15));

        SignedMediaUrl first = await signer.CreateReadUrlAsync(
            "media/sha256/ab/video.mp4",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        SignedMediaUrl second = await signer.CreateReadUrlAsync(
            "media/sha256/ab/video.mp4",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal("offline-media.example.test", first.Url.Host);
        Assert.Equal("/media/sha256/ab/video.mp4", first.Url.AbsolutePath);
        Assert.Equal(string.Empty, first.Url.Query);
        Assert.Equal(now.AddMinutes(5), first.ExpiresAtUtc);
    }
}
