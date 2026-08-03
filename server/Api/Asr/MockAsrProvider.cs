namespace Lingmai.RedMist.Api.Asr;

public sealed class MockAsrProvider : IAsrProvider
{
    private readonly AsrProviderResult _result;
    private int _callCount;

    public MockAsrProvider(AsrProviderResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<AsrProviderResult> TranscribeAsync(
        AsrAudioInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(_result);
    }
}
