using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class SceneIntentClassificationServiceTests
{
    [Fact]
    public async Task EmptyAllowListReturnsIrrelevantWithoutCallingEitherClassifier()
    {
        var primary = Stub.Returning(Matched("intent.untrusted", 1d));
        var fallback = Stub.Returning(Matched("intent.untrusted", 1d));
        var service = new IntentClassificationService(primary, fallback);

        IntentDecision result = await service.ClassifyAsync(
            new IntentClassificationContext("任意表达", []),
            CancellationToken.None);

        Assert.Equal(IntentOutcome.Irrelevant, result.Outcome);
        Assert.Null(result.IntentId);
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task AllowedHighConfidenceIntentPassesTheGuard()
    {
        var primary = Stub.Returning(Matched("intent.allowed", 0.9d));
        var service = new IntentClassificationService(primary, Stub.Returning(Irrelevant()));

        IntentDecision result = await service.ClassifyAsync(Context(), CancellationToken.None);

        Assert.Equal(IntentOutcome.Matched, result.Outcome);
        Assert.Equal("intent.allowed", result.IntentId);
        Assert.Equal(0.9d, result.Confidence);
    }

    [Fact]
    public async Task UnknownIntentCanNeverEscapeTheCurrentNodeAllowList()
    {
        var primary = Stub.Returning(Matched("intent.invented", 0.99d));
        var fallback = Stub.Returning(Matched("intent.invented", 0.99d));
        var service = new IntentClassificationService(primary, fallback);

        IntentDecision result = await service.ClassifyAsync(Context(), CancellationToken.None);

        Assert.Equal(IntentOutcome.LowConfidence, result.Outcome);
        Assert.Null(result.IntentId);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task CandidateThresholdDowngradesAndClearsIntentId()
    {
        var primary = Stub.Returning(Matched("intent.allowed", 0.69d));
        var service = new IntentClassificationService(primary, Stub.Returning(Irrelevant()));

        IntentDecision result = await service.ClassifyAsync(Context(), CancellationToken.None);

        Assert.Equal(IntentOutcome.LowConfidence, result.Outcome);
        Assert.Null(result.IntentId);
    }

    [Theory]
    [InlineData(IntentOutcome.Irrelevant)]
    [InlineData(IntentOutcome.LowConfidence)]
    public async Task NonMatchedOutcomesNeverCarryAnUntrustedIntentId(IntentOutcome outcome)
    {
        var primary = Stub.Returning(new IntentDecision(outcome, "intent.allowed", 0.8d, "untrusted"));
        var service = new IntentClassificationService(primary, Stub.Returning(Irrelevant()));

        IntentDecision result = await service.ClassifyAsync(Context(), CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.IntentId);
    }

    [Fact]
    public async Task PrimaryFailureUsesTheLocalFallbackButStillAppliesTheAllowList()
    {
        var primary = Stub.Throwing(new IntentProviderException(IntentErrorCodes.ProviderUnavailable, "unavailable"));
        var fallback = Stub.Returning(Matched("intent.allowed", 0.8d, "local"));
        var service = new IntentClassificationService(primary, fallback);

        IntentDecision result = await service.ClassifyAsync(Context(), CancellationToken.None);

        Assert.Equal(IntentOutcome.Matched, result.Outcome);
        Assert.Equal("intent.allowed", result.IntentId);
        Assert.Equal(1, fallback.CallCount);
    }

    private static IntentClassificationContext Context() => new(
        "我愿意协助",
        [new IntentCandidate("intent.allowed", ["协助", "帮忙"], 0.7d)]);

    private static IntentDecision Matched(string id, double confidence, string source = "primary") =>
        new(IntentOutcome.Matched, id, confidence, source);

    private static IntentDecision Irrelevant() =>
        new(IntentOutcome.Irrelevant, null, 0d, "fallback");

    private sealed class Stub : IIntentClassifier
    {
        private readonly Func<IntentClassificationContext, IntentDecision> _classify;

        private Stub(Func<IntentClassificationContext, IntentDecision> classify)
        {
            _classify = classify;
        }

        public int CallCount { get; private set; }

        public static Stub Returning(IntentDecision result) => new(_ => result);
        public static Stub Throwing(Exception error) => new(_ => throw error);

        public Task<IntentDecision> ClassifyAsync(
            IntentClassificationContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_classify(context));
        }
    }
}
