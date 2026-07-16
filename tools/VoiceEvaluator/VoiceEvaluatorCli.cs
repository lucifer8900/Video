namespace VoiceEvaluator;

public static class VoiceEvaluatorCli
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length != 2)
        {
            stderr.WriteLine("Usage: VoiceEvaluator <voice-evaluation.dataset.json> <output-directory>");
            return 1;
        }

        try
        {
            LoadedVoiceEvaluationDataset dataset = new VoiceEvaluationDatasetLoader().Load(args[0]);
            VoiceEvaluationReport report = new VoiceEvaluationRunner()
                .EvaluateAsync(dataset, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            WrittenVoiceEvaluationReport written = new VoiceEvaluationReportWriter()
                .Write(report, args[1]);
            stdout.Write(written.Json);
            return report.Passed ? 0 : 2;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stderr.WriteLine("voice-evaluator: " + exception.Message);
            return 1;
        }
    }
}
