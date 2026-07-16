using Lingmai.RedMist.MediaPipeline;

namespace Lingmai.RedMist.MediaPipeline.Tests;

public sealed class FfmpegCommandBuilderTests
{
    private const string FfmpegPath = @"E:\Tools With Spaces\ffmpeg.exe";
    private const string FfprobePath = @"E:\Tools With Spaces\ffprobe.exe";

    [Fact]
    public void ProbeUsesExecutableAndIndividualArgumentsWithoutAShell()
    {
        const string input = @"E:\job input\clip & do-not-execute.mp4";
        var builder = Builder();

        ProcessSpec command = builder.BuildProbe(input);

        Assert.Equal(FfprobePath, command.FileName);
        Assert.Equal(
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", input],
            command.ArgumentList);
        Assert.Single(command.ArgumentList, argument => argument == input);
        Assert.DoesNotContain(command.ArgumentList, argument => argument.Contains('"'));
        Assert.DoesNotContain("cmd", command.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", command.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranscodeBuildsTheNormalizedUnityCompatibleProfileWithoutStretching()
    {
        const string input = @"E:\job input\source.mp4";
        const string output = @"E:\job output\normalized.part.mp4";
        var builder = Builder();

        ProcessSpec command = builder.BuildTranscode(input, output, Profile());

        Assert.Equal(FfmpegPath, command.FileName);
        Assert.Equal(input, ValueAfter(command, "-i"));
        Assert.Equal("0:v:0", ValueAfter(command, "-map"));
        Assert.Contains("0:a:0", command.ArgumentList);
        Assert.Equal("libx264", ValueAfter(command, "-c:v"));
        Assert.Equal("high", ValueAfter(command, "-profile:v"));
        Assert.Equal("yuv420p", ValueAfter(command, "-pix_fmt"));
        Assert.Equal("+faststart", ValueAfter(command, "-movflags"));
        Assert.Equal("aac", ValueAfter(command, "-c:a"));
        Assert.Equal("48000", ValueAfter(command, "-ar"));
        Assert.Equal("2", ValueAfter(command, "-ac"));
        Assert.Equal("128k", ValueAfter(command, "-b:a"));
        Assert.Equal("8.25", ValueAfter(command, "-t"));
        Assert.Equal(output, command.ArgumentList[^1]);

        string videoFilter = ValueAfter(command, "-vf");
        Assert.Contains("scale=1920:1080:force_original_aspect_ratio=decrease", videoFilter);
        Assert.Contains("pad=1920:1080:(ow-iw)/2:(oh-ih)/2", videoFilter);
        Assert.Contains("setsar=1", videoFilter);
        Assert.Contains("fps=24", videoFilter);
        Assert.DoesNotContain("scale=1920:1080,", videoFilter, StringComparison.Ordinal);

        string audioFilter = ValueAfter(command, "-af");
        Assert.Contains("loudnorm=I=-16", audioFilter);
        Assert.Contains("TP=-1.5", audioFilter);
        Assert.Contains("LRA=11", audioFilter);
    }

    [Theory]
    [InlineData(MediaAudioPolicy.Require, "0:a:0")]
    [InlineData(MediaAudioPolicy.Optional, "0:a:0?")]
    public void AudioMappingFollowsTheConfiguredPolicy(
        MediaAudioPolicy audioPolicy,
        string expectedMap)
    {
        MediaTranscodeProfile profile = Profile() with { AudioPolicy = audioPolicy };

        ProcessSpec command = Builder().BuildTranscode("input.mp4", "output.mp4", profile);

        Assert.Contains(expectedMap, command.ArgumentList);
    }

    [Fact]
    public void DropAudioPolicyEmitsNoAudioAndNoLoudnessFilter()
    {
        MediaTranscodeProfile profile = Profile() with { AudioPolicy = MediaAudioPolicy.Drop };

        ProcessSpec command = Builder().BuildTranscode("input.mp4", "output.mp4", profile);

        Assert.Contains("-an", command.ArgumentList);
        Assert.DoesNotContain("-af", command.ArgumentList);
        Assert.DoesNotContain("-c:a", command.ArgumentList);
    }

    private static FfmpegCommandBuilder Builder() => new(new FfmpegToolOptions
    {
        FfmpegPath = FfmpegPath,
        FfprobePath = FfprobePath,
        ProcessTimeout = TimeSpan.FromMinutes(2),
    });

    internal static MediaTranscodeProfile Profile() => new()
    {
        Width = 1920,
        Height = 1080,
        FramesPerSecond = 24,
        VideoCodec = "libx264",
        VideoProfile = "high",
        PixelFormat = "yuv420p",
        AudioPolicy = MediaAudioPolicy.Require,
        AudioCodec = "aac",
        AudioSampleRateHz = 48_000,
        AudioChannels = 2,
        AudioBitrateKbps = 128,
        IntegratedLoudnessLufs = -16,
        TruePeakDbtp = -1.5,
        LoudnessRangeLu = 11,
        MaximumDuration = TimeSpan.FromSeconds(8.25),
    };

    private static string ValueAfter(ProcessSpec command, string option)
    {
        int index = command.ArgumentList.ToList().IndexOf(option);
        Assert.InRange(index, 0, command.ArgumentList.Count - 2);
        return command.ArgumentList[index + 1];
    }
}
