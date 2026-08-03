namespace Lingmai.RedMist.Api.Asr;

public sealed record AsrAudioInput(
    Stream Content,
    string FileName,
    string ContentType,
    long Length,
    int SampleRateHz,
    int Channels,
    double DurationSeconds);

public sealed record AsrProviderResult(
    string Text,
    string? Language,
    double DurationSeconds);

public interface IAsrProvider
{
    Task<AsrProviderResult> TranscribeAsync(
        AsrAudioInput input,
        CancellationToken cancellationToken);
}

public sealed class AsrProviderException : Exception
{
    public AsrProviderException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AsrProviderException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
