namespace Lingmai.RedMist.Api.Asr;

public sealed class AsrOptions
{
    public string Provider { get; set; } = "Mock";
    public string MockTranscript { get; set; } = string.Empty;
    public int MaxAudioBytes { get; set; } = 262_144;
    public double MaxDurationSeconds { get; set; } = 8d;
    public int[] AllowedSampleRatesHz { get; set; } = [16_000];
    public int RequiredChannels { get; set; } = 1;
    public int RequiredBitsPerSample { get; set; } = 16;
    public bool DeleteAfterProcessing { get; set; } = true;
    public string StagingDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "lingmai-redmist",
        "asr");
    public OpenAiAsrOptions OpenAi { get; set; } = new();
}

public sealed class OpenAiAsrOptions
{
    public Uri Endpoint { get; set; } = new("https://api.openai.com/v1/audio/transcriptions");
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}

public static class AsrErrorCodes
{
    public const string AudioTooLarge = "asr.audio_too_large";
    public const string DurationExceeded = "asr.duration_exceeded";
    public const string SampleRateUnsupported = "asr.sample_rate_unsupported";
    public const string ChannelsUnsupported = "asr.channels_unsupported";
    public const string EncodingUnsupported = "asr.encoding_unsupported";
    public const string InvalidWav = "asr.wav_invalid";
    public const string ProviderUnavailable = "asr.provider_unavailable";
}

public sealed class AsrValidationException : Exception
{
    public AsrValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
