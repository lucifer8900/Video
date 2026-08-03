namespace Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

public sealed class FfmpegIntegrationFactAttribute : FactAttribute
{
    public FfmpegIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_FFMPEG_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set RUN_FFMPEG_INTEGRATION=1 to run local FFmpeg tool integration tests.";
        }
    }
}
