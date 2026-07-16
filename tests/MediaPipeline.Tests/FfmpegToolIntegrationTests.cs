using Lingmai.RedMist.MediaPipeline;
using Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class FfmpegToolIntegrationTests
{
    [FfmpegIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task LocalToolsCreateNormalizeProbeAndHashATinySample()
    {
        string ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        string ffprobe = Environment.GetEnvironmentVariable("FFPROBE_PATH") ?? "ffprobe";
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "lingmai-media-pipeline-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string source = Path.Combine(tempRoot, "source.mp4");
        string normalized = Path.Combine(tempRoot, "normalized.mp4");

        try
        {
            var runner = new FfmpegProcessRunner();
            ProcessResult generated = await runner.RunAsync(
                new ProcessSpec(
                    ffmpeg,
                    [
                        "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                        "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24:d=0.5",
                        "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.5",
                        "-shortest", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                        "-c:a", "aac", source,
                    ],
                    TimeSpan.FromSeconds(30)),
                CancellationToken.None);
            Assert.Equal(0, generated.ExitCode);

            var builder = new FfmpegCommandBuilder(new FfmpegToolOptions
            {
                FfmpegPath = ffmpeg,
                FfprobePath = ffprobe,
                ProcessTimeout = TimeSpan.FromSeconds(30),
            });
            MediaTranscodeProfile profile = FfmpegCommandBuilderTests.Profile() with
            {
                Width = 320,
                Height = 180,
                MaximumDuration = TimeSpan.FromSeconds(0.5),
            };

            ProcessResult transcoded = await runner.RunAsync(
                builder.BuildTranscode(source, normalized, profile),
                CancellationToken.None);
            Assert.Equal(0, transcoded.ExitCode);
            Assert.True(File.Exists(normalized));

            ProcessResult probed = await runner.RunAsync(
                builder.BuildProbe(normalized),
                CancellationToken.None);
            Assert.Equal(0, probed.ExitCode);
            MediaProbeResult metadata = new FfprobeResultParser().Parse(probed.StandardOutput);
            Assert.Equal("h264", metadata.VideoCodec);
            Assert.Equal("yuv420p", metadata.PixelFormat);
            Assert.Equal(320, metadata.Width);
            Assert.Equal(180, metadata.Height);
            Assert.Equal(24d, metadata.FramesPerSecond, precision: 3);
            Assert.Equal("aac", metadata.AudioCodec);
            Assert.Equal(48_000, metadata.AudioSampleRateHz);

            await using FileStream output = File.OpenRead(normalized);
            string hash = await new MediaContentHasher().ComputeSha256Async(
                output,
                CancellationToken.None);
            Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
            Assert.Equal(71, hash.Length);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }
}
