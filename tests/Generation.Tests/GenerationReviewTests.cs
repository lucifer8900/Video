using Lingmai.RedMist.Generation.Review;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationReviewTests
{
    private static readonly GenerationReviewChecklist FullyApproved = new(
        PromptPolicyPassed: true,
        PlayerInputPolicyPassed: true,
        OutputPolicyPassed: true,
        CharacterConsistencyPassed: true,
        DialogueAccuracyPassed: true,
        SourceProvenancePassed: true);

    public static TheoryData<GenerationReviewChecklist, string> SingleFailedGateCases() =>
        new()
        {
            { FullyApproved with { PromptPolicyPassed = false }, "review.prompt_policy" },
            { FullyApproved with { PlayerInputPolicyPassed = false }, "review.player_input_policy" },
            { FullyApproved with { OutputPolicyPassed = false }, "review.output_policy" },
            { FullyApproved with { CharacterConsistencyPassed = false }, "review.character_consistency" },
            { FullyApproved with { DialogueAccuracyPassed = false }, "review.dialogue_accuracy" },
            { FullyApproved with { SourceProvenancePassed = false }, "review.source_provenance" },
        };

    [Fact]
    public void AllSixReviewGatesMustPassBeforeReady()
    {
        GenerationReviewResult result = GenerationReviewGate.Evaluate(FullyApproved);

        Assert.True(result.CanEnterReady);
        Assert.Empty(result.FailureCodes);
    }

    [Theory]
    [MemberData(nameof(SingleFailedGateCases))]
    public void AnySingleFailedReviewGateRejectsReady(
        GenerationReviewChecklist checklist,
        string expectedFailureCode)
    {
        GenerationReviewResult result = GenerationReviewGate.Evaluate(checklist);

        Assert.False(result.CanEnterReady);
        Assert.Contains(expectedFailureCode, result.FailureCodes);
    }

    [Fact]
    public void MultipleFailuresAreReportedInStableGateOrder()
    {
        GenerationReviewChecklist checklist = FullyApproved with
        {
            PromptPolicyPassed = false,
            OutputPolicyPassed = false,
            SourceProvenancePassed = false,
        };

        GenerationReviewResult result = GenerationReviewGate.Evaluate(checklist);

        Assert.False(result.CanEnterReady);
        Assert.Equal(
            [
                "review.prompt_policy",
                "review.output_policy",
                "review.source_provenance",
            ],
            result.FailureCodes);
    }
}
