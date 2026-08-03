namespace LipSyncReviewer.Tests;

public sealed class ReviewMediaProbeTests
{
    [Fact]
    public void ProbeContractConsumesTheLockedStreamInsteadOfAReopenedPath()
    {
        Type firstParameter = typeof(IReviewMediaProbe)
            .GetMethod(nameof(IReviewMediaProbe.ProbeAsync))!
            .GetParameters()[0]
            .ParameterType;

        Assert.Equal(typeof(Stream), firstParameter);
    }

    [Fact]
    public void VideoRequiresVideoAudioAndPositiveDuration()
    {
        ReviewMediaProbeResult result = FfprobeReviewMediaProbe.ParseProbeResult(
            """
            {
              "streams": [
                { "codec_type": "video", "duration": "8.0" },
                { "codec_type": "audio", "duration": "8.0" }
              ],
              "format": { "duration": "8.0" }
            }
            """,
            "video");

        Assert.Equal(8_000, result.DurationMilliseconds);

        LipSyncReviewInputException exception = Assert.Throws<LipSyncReviewInputException>(
            () => FfprobeReviewMediaProbe.ParseProbeResult(
                """
                {
                  "streams": [{ "codec_type": "video", "duration": "8.0" }],
                  "format": { "duration": "8.0" }
                }
                """,
                "video"));
        Assert.Equal("media.probe_invalid", exception.Code);
    }

    [Fact]
    public void AudioRequiresAudioAndPositiveDuration()
    {
        ReviewMediaProbeResult result = FfprobeReviewMediaProbe.ParseProbeResult(
            """
            {
              "streams": [{ "codec_type": "audio", "duration": "7.5" }],
              "format": { "duration": "7.5" }
            }
            """,
            "audio");

        Assert.Equal(7_500, result.DurationMilliseconds);
    }
}
