namespace Lingmai.RedMist.Api.Asr;

public sealed class AsrTranscriptionService
{
    private readonly WavAudioInspector _inspector;
    private readonly TemporaryAudioStore _temporaryAudio;
    private readonly IAsrProvider _provider;

    public AsrTranscriptionService(
        WavAudioInspector inspector,
        TemporaryAudioStore temporaryAudio,
        IAsrProvider provider)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _temporaryAudio = temporaryAudio ?? throw new ArgumentNullException(nameof(temporaryAudio));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<AsrProviderResult> TranscribeAsync(
        Stream audio,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (!string.Equals(Path.GetExtension(fileName), ".wav", StringComparison.OrdinalIgnoreCase))
            throw new AsrValidationException(AsrErrorCodes.InvalidWav, "Only WAV uploads are accepted.");
        if (!string.Equals(contentType, "audio/wav", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
            throw new AsrValidationException(AsrErrorCodes.InvalidWav, "Only WAV uploads are accepted.");

        return _temporaryAudio.UseAsync(
            audio,
            ".wav",
            TranscribeStagedAsync,
            cancellationToken);
    }

    private async Task<AsrProviderResult> TranscribeStagedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        WavAudioMetadata metadata = _inspector.Inspect(stream);
        stream.Position = 0;
        var input = new AsrAudioInput(
            stream,
            "audio.wav",
            "audio/wav",
            stream.Length,
            metadata.SampleRateHz,
            metadata.Channels,
            metadata.DurationSeconds);
        AsrProviderResult result = await _provider.TranscribeAsync(input, cancellationToken).ConfigureAwait(false);
        return result with { DurationSeconds = metadata.DurationSeconds };
    }
}
