namespace Lingmai.RedMist.Generation.Review;

public sealed record GenerationReviewChecklist(
    bool PromptPolicyPassed,
    bool PlayerInputPolicyPassed,
    bool OutputPolicyPassed,
    bool CharacterConsistencyPassed,
    bool DialogueAccuracyPassed,
    bool SourceProvenancePassed);

public sealed record GenerationReviewResult(
    bool CanEnterReady,
    IReadOnlyList<string> FailureCodes);

public static class GenerationReviewGate
{
    public static GenerationReviewResult Evaluate(GenerationReviewChecklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);

        var failures = new List<string>(capacity: 6);
        AddFailureUnless(checklist.PromptPolicyPassed, "review.prompt_policy", failures);
        AddFailureUnless(checklist.PlayerInputPolicyPassed, "review.player_input_policy", failures);
        AddFailureUnless(checklist.OutputPolicyPassed, "review.output_policy", failures);
        AddFailureUnless(checklist.CharacterConsistencyPassed, "review.character_consistency", failures);
        AddFailureUnless(checklist.DialogueAccuracyPassed, "review.dialogue_accuracy", failures);
        AddFailureUnless(checklist.SourceProvenancePassed, "review.source_provenance", failures);

        return new GenerationReviewResult(failures.Count == 0, failures.ToArray());
    }

    private static void AddFailureUnless(bool passed, string failureCode, ICollection<string> failures)
    {
        if (!passed)
        {
            failures.Add(failureCode);
        }
    }
}
