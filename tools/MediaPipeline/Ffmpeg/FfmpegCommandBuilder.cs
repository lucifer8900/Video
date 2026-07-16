using System.Globalization;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class FfmpegCommandBuilder
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly TimeSpan _processTimeout;

    public FfmpegCommandBuilder(FfmpegToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.FfmpegPath))
            throw new ArgumentException("An FFmpeg path is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.FfprobePath))
            throw new ArgumentException("An FFprobe path is required.", nameof(options));
        if (options.ProcessTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));

        _ffmpegPath = options.FfmpegPath;
        _ffprobePath = options.FfprobePath;
        _processTimeout = options.ProcessTimeout;
    }

    public ProcessSpec BuildProbe(string inputPath)
    {
        ValidatePath(inputPath, nameof(inputPath));
        return new ProcessSpec(
            _ffprobePath,
            [
                "-v", "error",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                inputPath,
            ],
            _processTimeout);
    }

    public ProcessSpec BuildTranscode(
        string inputPath,
        string outputPath,
        MediaTranscodeProfile profile)
    {
        ValidatePath(inputPath, nameof(inputPath));
        ValidatePath(outputPath, nameof(outputPath));
        ValidateProfile(profile);

        var arguments = new List<string>
        {
            "-nostdin",
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", inputPath,
            "-map", "0:v:0",
        };

        if (profile.AudioPolicy == MediaAudioPolicy.Require)
            arguments.AddRange(["-map", "0:a:0"]);
        else if (profile.AudioPolicy == MediaAudioPolicy.Optional)
            arguments.AddRange(["-map", "0:a:0?"]);

        string frameRate = FormatNumber(profile.FramesPerSecond);
        arguments.AddRange(
        [
            "-vf",
            $"scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease," +
            $"pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2," +
            $"setsar=1,fps={frameRate}",
            "-c:v", profile.VideoCodec,
            "-profile:v", profile.VideoProfile,
            "-pix_fmt", profile.PixelFormat,
            "-movflags", "+faststart",
        ]);

        if (profile.AudioPolicy == MediaAudioPolicy.Drop)
        {
            arguments.Add("-an");
        }
        else
        {
            arguments.AddRange(
            [
                "-c:a", profile.AudioCodec,
                "-ar", profile.AudioSampleRateHz.ToString(CultureInfo.InvariantCulture),
                "-ac", profile.AudioChannels.ToString(CultureInfo.InvariantCulture),
                "-b:a", profile.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k",
                "-af",
                $"loudnorm=I={FormatNumber(profile.IntegratedLoudnessLufs)}:" +
                $"TP={FormatNumber(profile.TruePeakDbtp)}:" +
                $"LRA={FormatNumber(profile.LoudnessRangeLu)}",
            ]);
        }

        if (profile.MaximumDuration is { } maximumDuration)
            arguments.AddRange(["-t", FormatNumber(maximumDuration.TotalSeconds)]);
        arguments.Add(outputPath);

        return new ProcessSpec(_ffmpegPath, arguments, _processTimeout);
    }

    private static void ValidateProfile(MediaTranscodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Width <= 0 || profile.Height <= 0 ||
            !double.IsFinite(profile.FramesPerSecond) || profile.FramesPerSecond <= 0 ||
            string.IsNullOrWhiteSpace(profile.VideoCodec) ||
            string.IsNullOrWhiteSpace(profile.VideoProfile) ||
            string.IsNullOrWhiteSpace(profile.PixelFormat))
        {
            throw new ArgumentException("The video transcode profile is invalid.", nameof(profile));
        }

        if (profile.MaximumDuration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentException("Maximum duration must be positive.", nameof(profile));

        if (profile.AudioPolicy != MediaAudioPolicy.Drop &&
            (string.IsNullOrWhiteSpace(profile.AudioCodec) ||
             profile.AudioSampleRateHz <= 0 ||
             profile.AudioChannels <= 0 ||
             profile.AudioBitrateKbps <= 0 ||
             !double.IsFinite(profile.IntegratedLoudnessLufs) ||
             !double.IsFinite(profile.TruePeakDbtp) ||
             !double.IsFinite(profile.LoudnessRangeLu) ||
             profile.LoudnessRangeLu <= 0))
        {
            throw new ArgumentException("The audio transcode profile is invalid.", nameof(profile));
        }
    }

    private static void ValidatePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A media path is required.", parameterName);
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
