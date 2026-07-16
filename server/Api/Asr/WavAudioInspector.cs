using System.Text;

namespace Lingmai.RedMist.Api.Asr;

public sealed record WavAudioMetadata(
    int SampleRateHz,
    int Channels,
    int BitsPerSample,
    int DataBytes,
    double DurationSeconds);

public sealed class WavAudioInspector
{
    private const ushort PcmFormat = 1;
    private readonly int _maxAudioBytes;
    private readonly double _maxDurationSeconds;
    private readonly HashSet<int> _allowedSampleRates;
    private readonly int _requiredChannels;
    private readonly int _requiredBitsPerSample;

    public WavAudioInspector(AsrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxAudioBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxAudioBytes must be positive.");
        if (options.MaxDurationSeconds <= 0d || double.IsNaN(options.MaxDurationSeconds))
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDurationSeconds must be positive.");
        if (options.AllowedSampleRatesHz is null || options.AllowedSampleRatesHz.Length == 0 ||
            options.AllowedSampleRatesHz.Any(rate => rate <= 0))
            throw new ArgumentOutOfRangeException(nameof(options), "At least one positive sample rate is required.");
        if (options.RequiredChannels <= 0 || options.RequiredBitsPerSample <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The required WAV format must be positive.");

        _maxAudioBytes = options.MaxAudioBytes;
        _maxDurationSeconds = options.MaxDurationSeconds;
        _allowedSampleRates = new HashSet<int>(options.AllowedSampleRatesHz);
        _requiredChannels = options.RequiredChannels;
        _requiredBitsPerSample = options.RequiredBitsPerSample;
    }

    public WavAudioMetadata Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw Invalid("The WAV stream must be readable and seekable.");

        long originalPosition = stream.Position;
        try
        {
            long length = stream.Length;
            if (length > _maxAudioBytes)
                throw new AsrValidationException(AsrErrorCodes.AudioTooLarge, "The audio exceeds the configured byte limit.");
            if (length < 44)
                throw Invalid("The WAV file is truncated.");

            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (ReadFourCc(reader) != "RIFF") throw Invalid("The RIFF header is missing.");
            uint declaredRiffBytes = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE") throw Invalid("The WAVE signature is missing.");
            if ((long)declaredRiffBytes + 8 != length)
                throw Invalid("The RIFF length does not match the uploaded file.");

            FormatChunk? format = null;
            int? dataBytes = null;
            while (stream.Position < length)
            {
                if (length - stream.Position < 8) throw Invalid("A WAV chunk header is truncated.");
                string chunkId = ReadFourCc(reader);
                uint chunkSize = reader.ReadUInt32();
                long chunkStart = stream.Position;
                long chunkEnd = checked(chunkStart + chunkSize);
                long paddedEnd = checked(chunkEnd + (chunkSize & 1u));
                if (chunkEnd > length || paddedEnd > length)
                    throw Invalid("A WAV chunk extends beyond the uploaded file.");

                if (chunkId == "fmt ")
                {
                    if (format is not null || chunkSize < 16) throw Invalid("The WAV format chunk is invalid.");
                    format = new FormatChunk(
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        checked((int)reader.ReadUInt32()),
                        checked((int)reader.ReadUInt32()),
                        reader.ReadUInt16(),
                        reader.ReadUInt16());
                }
                else if (chunkId == "data")
                {
                    if (dataBytes is not null || chunkSize > int.MaxValue) throw Invalid("The WAV data chunk is invalid.");
                    dataBytes = (int)chunkSize;
                }

                stream.Position = paddedEnd;
            }

            if (format is null || dataBytes is null) throw Invalid("The WAV format or data chunk is missing.");
            ValidateFormat(format, dataBytes.Value);
            double durationSeconds = (double)dataBytes.Value / format.ByteRate;
            if (durationSeconds > _maxDurationSeconds)
                throw new AsrValidationException(AsrErrorCodes.DurationExceeded, "The audio exceeds the configured duration limit.");

            return new WavAudioMetadata(
                format.SampleRateHz,
                format.Channels,
                format.BitsPerSample,
                dataBytes.Value,
                durationSeconds);
        }
        catch (AsrValidationException)
        {
            throw;
        }
        catch (Exception error) when (error is EndOfStreamException or IOException or OverflowException)
        {
            throw Invalid("The WAV file is malformed.");
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private void ValidateFormat(FormatChunk format, int dataBytes)
    {
        if (!_allowedSampleRates.Contains(format.SampleRateHz))
            throw new AsrValidationException(AsrErrorCodes.SampleRateUnsupported, "The WAV sample rate is unsupported.");
        if (format.Channels != _requiredChannels)
            throw new AsrValidationException(AsrErrorCodes.ChannelsUnsupported, "The WAV channel count is unsupported.");
        if (format.AudioFormat != PcmFormat || format.BitsPerSample != _requiredBitsPerSample)
            throw new AsrValidationException(AsrErrorCodes.EncodingUnsupported, "Only the configured PCM encoding is accepted.");

        int expectedBlockAlign = checked(format.Channels * format.BitsPerSample / 8);
        int expectedByteRate = checked(format.SampleRateHz * expectedBlockAlign);
        if (format.BitsPerSample % 8 != 0 || format.BlockAlign != expectedBlockAlign ||
            format.ByteRate != expectedByteRate || format.ByteRate <= 0 || dataBytes % format.BlockAlign != 0)
            throw Invalid("The WAV rate or alignment fields are inconsistent.");
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        byte[] value = reader.ReadBytes(4);
        if (value.Length != 4) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(value);
    }

    private static AsrValidationException Invalid(string message) =>
        new(AsrErrorCodes.InvalidWav, message);

    private sealed record FormatChunk(
        ushort AudioFormat,
        int Channels,
        int SampleRateHz,
        int ByteRate,
        int BlockAlign,
        int BitsPerSample);
}
