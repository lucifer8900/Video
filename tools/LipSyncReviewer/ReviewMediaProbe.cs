using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace LipSyncReviewer;

public sealed record ReviewMediaProbeResult(long DurationMilliseconds);

public interface IReviewMediaProbe
{
    Task<ReviewMediaProbeResult> ProbeAsync(
        Stream media,
        string mediaType,
        CancellationToken cancellationToken);
}

public sealed class FfprobeReviewMediaProbe : IReviewMediaProbe
{
    private readonly string _ffprobePath;
    private readonly TimeSpan _timeout;

    public FfprobeReviewMediaProbe()
        : this(
            Environment.GetEnvironmentVariable("FFPROBE_PATH") ?? "ffprobe",
            TimeSpan.FromSeconds(30))
    {
    }

    internal FfprobeReviewMediaProbe(string ffprobePath, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _ffprobePath = ffprobePath;
        _timeout = timeout;
    }

    public async Task<ReviewMediaProbeResult> ProbeAsync(
        Stream media,
        string mediaType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (!media.CanRead || !media.CanSeek)
            throw Invalid("media.probe_stream", "Locked review media must be readable and seekable.");
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = BuildStartInfo(),
            EnableRaisingEvents = true,
        };
        try
        {
            if (!process.Start()) throw ProbeUnavailable();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw Invalid("media.probe_unavailable", "FFprobe could not inspect locked review media.", exception);
        }

        media.Position = 0;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            Task pump = PumpAsync(media, process.StandardInput.BaseStream, linked.Token);
            Task wait = process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(pump, wait).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await WaitAfterKillAsync(process).ConfigureAwait(false);
            await DrainQuietlyAsync(stdout, stderr).ConfigureAwait(false);
            media.Position = 0;
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            throw Invalid("media.probe_timeout", "FFprobe exceeded the review media probe timeout.");
        }
        catch (IOException exception)
        {
            Kill(process);
            await WaitAfterKillAsync(process).ConfigureAwait(false);
            await DrainQuietlyAsync(stdout, stderr).ConfigureAwait(false);
            media.Position = 0;
            throw Invalid("media.probe_failed", "FFprobe rejected locked review media.", exception);
        }
        finally
        {
            media.Position = 0;
        }

        string output = await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw Invalid("media.probe_failed", "FFprobe rejected locked review media.");

        return ParseProbeResult(output, mediaType);
    }

    private ProcessStartInfo BuildStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
                 {
                     "-v", "error",
                     "-print_format", "json",
                     "-show_format",
                     "-show_streams",
                     "-i", "pipe:0",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static async Task PumpAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, 81_920, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await destination.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DrainQuietlyAsync(params Task<string>[] readers)
    {
        try
        {
            await Task.WhenAll(readers).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private static LipSyncReviewInputException ProbeUnavailable() =>
        Invalid("media.probe_unavailable", "FFprobe could not inspect locked review media.");

    internal static ReviewMediaProbeResult ParseProbeResult(string json, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("streams", out JsonElement streams) ||
                streams.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("media.probe_invalid", "FFprobe returned incomplete review media metadata.");
            }
            bool hasVideo = false;
            bool hasAudio = false;
            double durationSeconds = 0;
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? codecType = StringValue(stream, "codec_type");
                hasVideo |= string.Equals(codecType, "video", StringComparison.Ordinal);
                hasAudio |= string.Equals(codecType, "audio", StringComparison.Ordinal);
                durationSeconds = Math.Max(durationSeconds, PositiveDouble(stream, "duration"));
            }
            if (root.TryGetProperty("format", out JsonElement format) &&
                format.ValueKind == JsonValueKind.Object)
            {
                durationSeconds = Math.Max(durationSeconds, PositiveDouble(format, "duration"));
            }

            bool validStreams = mediaType is "video" or "animation"
                ? hasVideo && hasAudio
                : mediaType == "audio" && hasAudio;
            if (!validStreams || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
                throw Invalid("media.probe_invalid", "Locked review media lacks required streams or duration.");
            double milliseconds = Math.Ceiling(durationSeconds * 1000d);
            if (milliseconds > long.MaxValue)
                throw Invalid("media.probe_invalid", "Locked review media duration is out of range.");
            return new ReviewMediaProbeResult(checked((long)milliseconds));
        }
        catch (LipSyncReviewInputException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw Invalid("media.probe_invalid", "FFprobe returned invalid review media metadata.", exception);
        }
    }

    private static string? StringValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static double PositiveDouble(JsonElement element, string propertyName)
    {
        string? value = StringValue(element, propertyName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
               double.IsFinite(parsed) && parsed > 0
            ? parsed
            : 0;
    }

    private static LipSyncReviewInputException Invalid(
        string code,
        string message,
        Exception? inner = null) =>
        inner is null
            ? new LipSyncReviewInputException(code, message)
            : new LipSyncReviewInputException(code, message, inner);
}
