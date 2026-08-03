namespace Lingmai.RedMist.Api.Intent;

public sealed class MockIntentClassifier : IIntentClassifier
{
    private readonly IntentDecision _result;
    private int _callCount;

    public MockIntentClassifier(IntentDecision result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<IntentDecision> ClassifyAsync(
        IntentClassificationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(_result);
    }
}
