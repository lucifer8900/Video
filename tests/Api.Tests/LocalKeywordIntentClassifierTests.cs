using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class LocalKeywordIntentClassifierTests
{
    [Fact]
    public async Task UniqueChineseKeywordMatchReturnsOnlyTheAllowedIntent()
    {
        var classifier = new LocalKeywordIntentClassifier();

        IntentDecision result = await classifier.ClassifyAsync(
            Context("我愿意帮忙救人"),
            CancellationToken.None);

        Assert.Equal(IntentOutcome.Matched, result.Outcome);
        Assert.Equal("intent.help", result.IntentId);
        Assert.InRange(result.Confidence, 0.6d, 1d);
    }

    [Fact]
    public async Task EquallyStrongMatchesAreAmbiguousAndClearTheIntentId()
    {
        var classifier = new LocalKeywordIntentClassifier();

        IntentDecision result = await classifier.ClassifyAsync(
            Context("我可以帮忙，但也可能拒绝"),
            CancellationToken.None);

        Assert.Equal(IntentOutcome.LowConfidence, result.Outcome);
        Assert.Null(result.IntentId);
    }

    [Fact]
    public async Task UnknownExpressionReturnsIrrelevantWithoutInventingAnIntent()
    {
        var classifier = new LocalKeywordIntentClassifier();

        IntentDecision result = await classifier.ClassifyAsync(
            Context("今天天色如何"),
            CancellationToken.None);

        Assert.Equal(IntentOutcome.Irrelevant, result.Outcome);
        Assert.Null(result.IntentId);
    }

    [Fact]
    public async Task NegatedPositiveKeywordNeverTriggersThePositiveIntent()
    {
        var classifier = new LocalKeywordIntentClassifier();

        IntentDecision result = await classifier.ClassifyAsync(
            Context("我不想帮忙，也不要救人"),
            CancellationToken.None);

        Assert.NotEqual("intent.help", result.IntentId);
        Assert.Contains(result.Outcome, new[] { IntentOutcome.LowConfidence, IntentOutcome.Irrelevant });
    }

    private static IntentClassificationContext Context(string transcript) => new(
        transcript,
        [
            new IntentCandidate("intent.help", ["帮忙", "救人"], 0.6d),
            new IntentCandidate("intent.refuse", ["拒绝", "离开"], 0.6d),
        ]);
}
