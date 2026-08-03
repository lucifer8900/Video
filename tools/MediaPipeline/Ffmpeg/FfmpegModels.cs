namespace Lingmai.RedMist.MediaPipeline;

public sealed class FfmpegToolOptions
{
    public string FfmpegPath { get; set; } = string.Empty;

    public string FfprobePath { get; set; } = string.Empty;

    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromMinutes(2);
}

public enum MediaAudioPolicy
{
    Require,
    Optional,
    Drop,
}

public sealed record MediaTranscodeProfile
{
    public int Width { get; init; }

    public int Height { get; init; }

    public double FramesPerSecond { get; init; }

    public string VideoCodec { get; init; } = string.Empty;

    public string VideoProfile { get; init; } = string.Empty;

    public string PixelFormat { get; init; } = string.Empty;

    public MediaAudioPolicy AudioPolicy { get; init; }

    public string AudioCodec { get; init; } = string.Empty;

    public int AudioSampleRateHz { get; init; }

    public int AudioChannels { get; init; }

    public int AudioBitrateKbps { get; init; }

    public double IntegratedLoudnessLufs { get; init; }

    public double TruePeakDbtp { get; init; }

    public double LoudnessRangeLu { get; init; }

    public TimeSpan? MaximumDuration { get; init; }
}

public sealed record MediaProbeResult(
    string ContainerFormat,
    string VideoCodec,
    string? VideoProfile,
    string PixelFormat,
    int Width,
    int Height,
    double FramesPerSecond,
    bool IsProgressive,
    TimeSpan Duration,
    string? AudioCodec,
    int? AudioSampleRateHz,
    int? AudioChannels)
{
    public bool HasAudio => AudioCodec is not null;
}

public sealed class FfprobeResultException : Exception
{
    public FfprobeResultException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
