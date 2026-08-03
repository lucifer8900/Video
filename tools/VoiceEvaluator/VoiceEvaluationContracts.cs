namespace VoiceEvaluator;

public sealed class VoiceEvaluationDataset
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string DatasetId { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public bool TestOnly { get; set; }
    public bool DoNotShip { get; set; }
    public int StoryCharacterLimit { get; set; }
    public List<string> SevereIntentIds { get; set; } = [];
    public List<VoiceEvaluationNode> Nodes { get; set; } = [];
    public List<VoiceEvaluationSample> Samples { get; set; } = [];
}

public sealed class VoiceEvaluationNode
{
    public string NodeId { get; set; } = string.Empty;
    public List<VoiceEvaluationIntent> Intents { get; set; } = [];
}

public sealed class VoiceEvaluationIntent
{
    public string Id { get; set; } = string.Empty;
    public List<string> KeywordPhrases { get; set; } = [];
    public double MinimumConfidence { get; set; }
}

public sealed class VoiceEvaluationSample
{
    public string Id { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public string ExpectedOutcome { get; set; } = string.Empty;
    public string? ExpectedIntentId { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed record LoadedVoiceEvaluationDataset(
    VoiceEvaluationDataset Dataset,
    string DatasetSha256);

public sealed class VoiceEvaluationThresholds
{
    public double MinimumValidIntentAccuracy { get; init; }
    public double MaximumSevereFalsePositiveRateExclusive { get; init; }
}

public sealed class VoiceEvaluationMetrics
{
    public int OverallCorrect { get; init; }
    public int TotalSamples { get; init; }
    public double OverallAccuracy { get; init; }
    public int ValidIntentCorrect { get; init; }
    public int ValidIntentTotal { get; init; }
    public double ValidIntentAccuracy { get; init; }
    public int SevereFalsePositiveCount { get; init; }
    public int NonSevereSampleTotal { get; init; }
    public double SevereFalsePositiveRate { get; init; }
}

public sealed class VoiceEvaluationClassReport
{
    public string ExpectedOutcome { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Correct { get; init; }
    public double Accuracy { get; init; }
}

public sealed class VoiceEvaluationSampleReport
{
    public string Id { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string ExpectedOutcome { get; init; } = string.Empty;
    public string? ExpectedIntentId { get; init; }
    public string ActualOutcome { get; init; } = string.Empty;
    public string? ActualIntentId { get; init; }
    public double Confidence { get; init; }
    public string Source { get; init; } = string.Empty;
    public bool Correct { get; init; }
    public bool SevereFalsePositive { get; init; }
}

public sealed class VoiceEvaluationReport
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string EvaluationScope { get; init; } = string.Empty;
    public string ProductionReadiness { get; init; } = "not_evaluable_no_production_voice_intents";
    public string DatasetId { get; init; } = string.Empty;
    public string DatasetSha256 { get; init; } = string.Empty;
    public string ClassifierId { get; init; } = "local_keyword";
    public int SampleCount { get; init; }
    public int AudioSampleCount { get; init; }
    public VoiceEvaluationThresholds Thresholds { get; init; } = new();
    public VoiceEvaluationMetrics Metrics { get; init; } = new();
    public bool Passed { get; init; }
    public IReadOnlyList<VoiceEvaluationClassReport> Classes { get; init; } = [];
    public IReadOnlyList<VoiceEvaluationSampleReport> Samples { get; init; } = [];
}
