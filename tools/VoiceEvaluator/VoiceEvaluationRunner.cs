using Lingmai.RedMist.Api.Intent;

namespace VoiceEvaluator;

public sealed class VoiceEvaluationRunner
{
    public const double MinimumValidIntentAccuracy = 0.9d;
    public const double MaximumSevereFalsePositiveRateExclusive = 0.01d;

    private static readonly string[] OutcomeOrder =
    [
        "matched",
        "irrelevant",
        "abuse",
        "too_long",
        "silence",
        "low_confidence",
    ];

    private readonly InvalidInputDetector _detector = new();
    private readonly IntentClassificationService _classifier = new(
        new LocalKeywordIntentClassifier(),
        new LocalKeywordIntentClassifier());

    public async Task<VoiceEvaluationReport> EvaluateAsync(
        LoadedVoiceEvaluationDataset loaded,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        VoiceEvaluationDataset dataset = loaded.Dataset;
        var candidatesByNode = dataset.Nodes.ToDictionary(
            node => node.NodeId,
            node => (IReadOnlyList<IntentCandidate>)node.Intents
                .Select(intent => new IntentCandidate(
                    intent.Id,
                    intent.KeywordPhrases,
                    intent.MinimumConfidence))
                .ToArray(),
            StringComparer.Ordinal);
        var severeIds = new HashSet<string>(dataset.SevereIntentIds, StringComparer.Ordinal);
        var results = new List<VoiceEvaluationSampleReport>(dataset.Samples.Count);

        foreach (VoiceEvaluationSample sample in dataset.Samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActualClassification actual = await ClassifyAsync(
                sample,
                candidatesByNode[sample.NodeId],
                dataset.StoryCharacterLimit,
                cancellationToken).ConfigureAwait(false);
            bool correct = string.Equals(
                               sample.ExpectedOutcome,
                               actual.Outcome,
                               StringComparison.Ordinal) &&
                           (sample.ExpectedOutcome != "matched" ||
                            string.Equals(
                                sample.ExpectedIntentId,
                                actual.IntentId,
                                StringComparison.Ordinal));
            bool expectedSevere = sample.ExpectedOutcome == "matched" &&
                                  sample.ExpectedIntentId is not null &&
                                  severeIds.Contains(sample.ExpectedIntentId);
            bool severeFalsePositive = !expectedSevere &&
                                       actual.Outcome == "matched" &&
                                       actual.IntentId is not null &&
                                       severeIds.Contains(actual.IntentId);

            results.Add(new VoiceEvaluationSampleReport
            {
                Id = sample.Id,
                NodeId = sample.NodeId,
                ExpectedOutcome = sample.ExpectedOutcome,
                ExpectedIntentId = sample.ExpectedIntentId,
                ActualOutcome = actual.Outcome,
                ActualIntentId = actual.IntentId,
                Confidence = actual.Confidence,
                Source = actual.Source,
                Correct = correct,
                SevereFalsePositive = severeFalsePositive,
            });
        }

        VoiceEvaluationMetrics metrics = CalculateMetrics(results, severeIds);
        bool passed = metrics.ValidIntentTotal > 0 &&
                      metrics.ValidIntentAccuracy >= MinimumValidIntentAccuracy &&
                      metrics.NonSevereSampleTotal > 0 &&
                      metrics.SevereFalsePositiveRate < MaximumSevereFalsePositiveRateExclusive;

        return new VoiceEvaluationReport
        {
            EvaluationScope = dataset.Scope,
            DatasetId = dataset.DatasetId,
            DatasetSha256 = loaded.DatasetSha256,
            SampleCount = results.Count,
            AudioSampleCount = 0,
            Thresholds = new VoiceEvaluationThresholds
            {
                MinimumValidIntentAccuracy = MinimumValidIntentAccuracy,
                MaximumSevereFalsePositiveRateExclusive = MaximumSevereFalsePositiveRateExclusive,
            },
            Metrics = metrics,
            Passed = passed,
            Classes = BuildClassReports(results),
            Samples = results,
        };
    }

    public static VoiceEvaluationMetrics CalculateMetrics(
        IReadOnlyList<VoiceEvaluationSampleReport> samples,
        IReadOnlySet<string> severeIntentIds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(severeIntentIds);
        int total = samples.Count;
        int overallCorrect = samples.Count(sample => sample.Correct);
        VoiceEvaluationSampleReport[] valid = samples
            .Where(sample => sample.ExpectedOutcome == "matched")
            .ToArray();
        int validCorrect = valid.Count(sample => sample.Correct);
        VoiceEvaluationSampleReport[] nonSevere = samples
            .Where(sample => sample.ExpectedOutcome != "matched" ||
                             sample.ExpectedIntentId is null ||
                             !severeIntentIds.Contains(sample.ExpectedIntentId))
            .ToArray();
        int severeFalsePositives = nonSevere.Count(sample => sample.SevereFalsePositive);

        return new VoiceEvaluationMetrics
        {
            OverallCorrect = overallCorrect,
            TotalSamples = total,
            OverallAccuracy = Ratio(overallCorrect, total),
            ValidIntentCorrect = validCorrect,
            ValidIntentTotal = valid.Length,
            ValidIntentAccuracy = Ratio(validCorrect, valid.Length),
            SevereFalsePositiveCount = severeFalsePositives,
            NonSevereSampleTotal = nonSevere.Length,
            SevereFalsePositiveRate = Ratio(severeFalsePositives, nonSevere.Length),
        };
    }

    private async Task<ActualClassification> ClassifyAsync(
        VoiceEvaluationSample sample,
        IReadOnlyList<IntentCandidate> candidates,
        int characterLimit,
        CancellationToken cancellationToken)
    {
        InvalidInputSignal? invalid = _detector.Detect(sample.Transcript, characterLimit);
        if (invalid is not null)
        {
            return new ActualClassification(
                InvalidOutcome(invalid.Kind),
                null,
                invalid.Confidence,
                invalid.Source);
        }

        IntentDecision decision = await _classifier.ClassifyAsync(
            new IntentClassificationContext(sample.Transcript, candidates),
            cancellationToken).ConfigureAwait(false);
        return decision.Outcome switch
        {
            IntentOutcome.Matched => new ActualClassification(
                "matched",
                decision.IntentId,
                decision.Confidence,
                decision.Source),
            IntentOutcome.LowConfidence => new ActualClassification(
                "low_confidence",
                null,
                decision.Confidence,
                decision.Source),
            _ => new ActualClassification(
                "irrelevant",
                null,
                decision.Confidence,
                decision.Source),
        };
    }

    private static string InvalidOutcome(InvalidInputKind kind) => kind switch
    {
        InvalidInputKind.Abuse => "abuse",
        InvalidInputKind.TooLong => "too_long",
        InvalidInputKind.Silence => "silence",
        InvalidInputKind.LowConfidence => "low_confidence",
        _ => "irrelevant",
    };

    private static IReadOnlyList<VoiceEvaluationClassReport> BuildClassReports(
        IReadOnlyList<VoiceEvaluationSampleReport> samples)
    {
        var reports = new List<VoiceEvaluationClassReport>();
        foreach (string outcome in OutcomeOrder)
        {
            VoiceEvaluationSampleReport[] group = samples
                .Where(sample => sample.ExpectedOutcome == outcome)
                .ToArray();
            if (group.Length == 0) continue;
            int correct = group.Count(sample => sample.Correct);
            reports.Add(new VoiceEvaluationClassReport
            {
                ExpectedOutcome = outcome,
                Total = group.Length,
                Correct = correct,
                Accuracy = Ratio(correct, group.Length),
            });
        }

        return reports;
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0d : (double)numerator / denominator;

    private sealed record ActualClassification(
        string Outcome,
        string? IntentId,
        double Confidence,
        string Source);
}
