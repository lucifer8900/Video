using Lingmai.RedMist.MediaPipeline;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class FfprobeResultParserTests
{
    [Fact]
    public void ParsesNormalizedAudioVideoMetadata()
    {
        var parser = new FfprobeResultParser();

        MediaProbeResult result = parser.Parse(Fixture("probe-av.json"));

        Assert.Equal("mov,mp4,m4a,3gp,3g2,mj2", result.ContainerFormat);
        Assert.Equal("h264", result.VideoCodec);
        Assert.Equal("High", result.VideoProfile);
        Assert.Equal("yuv420p", result.PixelFormat);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(24d, result.FramesPerSecond, precision: 6);
        Assert.True(result.IsProgressive);
        Assert.Equal(TimeSpan.FromSeconds(8.042), result.Duration);
        Assert.True(result.HasAudio);
        Assert.Equal("aac", result.AudioCodec);
        Assert.Equal(48_000, result.AudioSampleRateHz);
        Assert.Equal(2, result.AudioChannels);
    }

    [Fact]
    public void ParsesVideoOnlyAndRationalFrameRate()
    {
        MediaProbeResult result = new FfprobeResultParser().Parse(
            Fixture("probe-video-only.json"));

        Assert.False(result.HasAudio);
        Assert.Null(result.AudioCodec);
        Assert.Null(result.AudioSampleRateHz);
        Assert.Null(result.AudioChannels);
        Assert.Equal(30000d / 1001d, result.FramesPerSecond, precision: 6);
        Assert.Equal(TimeSpan.FromSeconds(10.005), result.Duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"streams\":[],\"format\":{\"duration\":\"1.0\"}}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"r_frame_rate\":\"0/0\"}]," +
                "\"format\":{\"duration\":\"1.0\"}}")]
    public void RejectsMalformedMissingVideoAndInvalidFrameRate(string json)
    {
        var parser = new FfprobeResultParser();

        FfprobeResultException error = Assert.Throws<FfprobeResultException>(() => parser.Parse(json));

        Assert.Equal("media.invalid_probe_result", error.Code);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MediaFixtures", name));
}
