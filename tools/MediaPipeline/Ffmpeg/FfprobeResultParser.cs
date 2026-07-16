using System.Globalization;
using System.Text.Json;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class FfprobeResultParser
{
    public MediaProbeResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw InvalidResult();

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("streams", out JsonElement streams) ||
                streams.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("format", out JsonElement format) ||
                format.ValueKind != JsonValueKind.Object)
            {
                throw InvalidResult();
            }

            JsonElement? video = FindStream(streams, "video");
            if (video is null) throw InvalidResult();
            JsonElement? audio = FindStream(streams, "audio");

            string containerFormat = RequiredString(format, "format_name");
            double durationSeconds = RequiredPositiveDouble(format, "duration");
            string videoCodec = RequiredString(video.Value, "codec_name");
            string? videoProfile = OptionalString(video.Value, "profile");
            string pixelFormat = RequiredString(video.Value, "pix_fmt");
            int width = RequiredPositiveInt(video.Value, "width");
            int height = RequiredPositiveInt(video.Value, "height");
            string frameRateValue = OptionalString(video.Value, "avg_frame_rate")
                                    ?? RequiredString(video.Value, "r_frame_rate");
            double framesPerSecond = ParseFrameRate(frameRateValue);
            bool progressive = string.Equals(
                OptionalString(video.Value, "field_order"),
                "progressive",
                StringComparison.OrdinalIgnoreCase);

            string? audioCodec = null;
            int? audioSampleRate = null;
            int? audioChannels = null;
            if (audio is not null)
            {
                audioCodec = RequiredString(audio.Value, "codec_name");
                audioSampleRate = RequiredPositiveInt(audio.Value, "sample_rate");
                audioChannels = RequiredPositiveInt(audio.Value, "channels");
            }

            return new MediaProbeResult(
                containerFormat,
                videoCodec,
                videoProfile,
                pixelFormat,
                width,
                height,
                framesPerSecond,
                progressive,
                TimeSpan.FromSeconds(durationSeconds),
                audioCodec,
                audioSampleRate,
                audioChannels);
        }
        catch (FfprobeResultException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or OverflowException or InvalidOperationException)
        {
            throw InvalidResult();
        }
    }

    private static JsonElement? FindStream(JsonElement streams, string codecType)
    {
        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (stream.ValueKind == JsonValueKind.Object &&
                string.Equals(
                    OptionalString(stream, "codec_type"),
                    codecType,
                    StringComparison.Ordinal))
            {
                return stream;
            }
        }

        return null;
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName) is { Length: > 0 } value
            ? value
            : throw InvalidResult();

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static int RequiredPositiveInt(JsonElement element, string propertyName)
    {
        string value = RequiredString(element, propertyName);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ||
            result <= 0)
        {
            throw InvalidResult();
        }

        return result;
    }

    private static double RequiredPositiveDouble(JsonElement element, string propertyName)
    {
        string value = RequiredString(element, propertyName);
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result) ||
            !double.IsFinite(result) || result <= 0)
        {
            throw InvalidResult();
        }

        return result;
    }

    private static double ParseFrameRate(string value)
    {
        string[] parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            if (double.TryParse(
                    parts[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double direct) &&
                double.IsFinite(direct) && direct > 0)
            {
                return direct;
            }

            throw InvalidResult();
        }

        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) ||
            !double.IsFinite(numerator) || !double.IsFinite(denominator) ||
            numerator <= 0 || denominator <= 0)
        {
            throw InvalidResult();
        }

        double result = numerator / denominator;
        return double.IsFinite(result) && result > 0 ? result : throw InvalidResult();
    }

    private static FfprobeResultException InvalidResult() =>
        new(
            "media.invalid_probe_result",
            "The media probe returned an invalid or incomplete result.");
}
