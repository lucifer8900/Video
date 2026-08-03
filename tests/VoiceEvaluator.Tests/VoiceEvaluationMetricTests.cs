using VoiceEvaluator;

namespace VoiceEvaluator.Tests;

public sealed class VoiceEvaluationMetricTests
{
    private static readonly IReadOnlySet<string> SevereIntentIds =
        new HashSet<string>(["fixture.intent.attack"], StringComparer.Ordinal);

    [Fact]
    public void NinetyPercentValidIntentAccuracyPassesButAnythingBelowFails()
    {
        VoiceEvaluationSampleReport[] exactBoundary = Enumerable.Range(0, 100)
            .Select(index => Matched(index, correct: index < 90))
            .ToArray();
        VoiceEvaluationSampleReport[] belowBoundary = Enumerable.Range(0, 100)
            .Select(index => Matched(index, correct: index < 89))
            .ToArray();

        VoiceEvaluationMetrics exact = VoiceEvaluationRunner.CalculateMetrics(
            exactBoundary,
            SevereIntentIds);
        VoiceEvaluationMetrics below = VoiceEvaluationRunner.CalculateMetrics(
            belowBoundary,
            SevereIntentIds);

        Assert.Equal(0.9d, exact.ValidIntentAccuracy, precision: 12);
        Assert.True(exact.ValidIntentAccuracy >= VoiceEvaluationRunner.MinimumValidIntentAccuracy);
        Assert.Equal(0.89d, below.ValidIntentAccuracy, precision: 12);
        Assert.False(below.ValidIntentAccuracy >= VoiceEvaluationRunner.MinimumValidIntentAccuracy);
    }

    [Fact]
    public void OnePercentSevereFalsePositiveRateFailsTheExclusiveThreshold()
    {
        VoiceEvaluationSampleReport[] exactBoundary = Enumerable.Range(0, 100)
            .Select(index => NonSevere(index, severeFalsePositive: index == 0))
            .ToArray();
        VoiceEvaluationSampleReport[] belowBoundary = Enumerable.Range(0, 200)
            .Select(index => NonSevere(index, severeFalsePositive: index == 0))
            .ToArray();

        VoiceEvaluationMetrics exact = VoiceEvaluationRunner.CalculateMetrics(
            exactBoundary,
            SevereIntentIds);
        VoiceEvaluationMetrics below = VoiceEvaluationRunner.CalculateMetrics(
            belowBoundary,
            SevereIntentIds);

        Assert.Equal(0.01d, exact.SevereFalsePositiveRate, precision: 12);
        Assert.False(
            exact.SevereFalsePositiveRate <
            VoiceEvaluationRunner.MaximumSevereFalsePositiveRateExclusive);
        Assert.Equal(0.005d, below.SevereFalsePositiveRate, precision: 12);
        Assert.True(
            below.SevereFalsePositiveRate <
            VoiceEvaluationRunner.MaximumSevereFalsePositiveRateExclusive);
    }

    private static VoiceEvaluationSampleReport Matched(int index, bool correct) => new()
    {
        Id = $"matched.{index:D3}",
        NodeId = "fixture.node.primary",
        ExpectedOutcome = "matched",
        ExpectedIntentId = "fixture.intent.observe",
        ActualOutcome = "matched",
        ActualIntentId = correct ? "fixture.intent.observe" : "fixture.intent.help",
        Confidence = 0.85d,
        Source = "test",
        Correct = correct,
        SevereFalsePositive = false,
    };

    private static VoiceEvaluationSampleReport NonSevere(
        int index,
        bool severeFalsePositive) => new()
    {
        Id = $"ordinary.{index:D3}",
        NodeId = "fixture.node.primary",
        ExpectedOutcome = "irrelevant",
        ExpectedIntentId = null,
        ActualOutcome = severeFalsePositive ? "matched" : "irrelevant",
        ActualIntentId = severeFalsePositive ? "fixture.intent.attack" : null,
        Confidence = severeFalsePositive ? 0.95d : 0d,
        Source = "test",
        Correct = !severeFalsePositive,
        SevereFalsePositive = severeFalsePositive,
    };
}
