using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class NpcConsequenceGateTests
{
    private readonly NpcConsequenceGate _gate = new();

    [Theory]
    [InlineData("attack")]
    [InlineData("permanent_relationship_break")]
    [InlineData("quest_failure")]
    public void SevereConsequenceRequiresFiniteConfidenceAtOrAboveThreshold(string operation)
    {
        NpcResponsePolicy response = Response(
            "response.severe",
            new NpcEffectPolicy(operation, NpcConfirmationPolicy.HighConfidence(0.9d)));
        var allowed = Responses(response);

        Assert.Equal(NpcConsequenceOutcome.Blocked, _gate.Evaluate(response, 0.899d, allowed).Outcome);
        Assert.Equal(NpcConsequenceOutcome.Blocked, _gate.Evaluate(response, double.NaN, allowed).Outcome);
        Assert.Equal(NpcConsequenceOutcome.Blocked, _gate.Evaluate(response, double.PositiveInfinity, allowed).Outcome);
        NpcConsequenceDecision accepted = _gate.Evaluate(response, 0.9d, allowed);
        Assert.Equal(NpcConsequenceOutcome.Allowed, accepted.Outcome);
        Assert.Equal("response.severe", accepted.ResponseId);
    }

    [Fact]
    public void SecondConfirmationReturnsOnlyTheSafePromptAndNeverTheSevereResponse()
    {
        NpcResponsePolicy severe = Response(
            "response.severe",
            new NpcEffectPolicy(
                "attack",
                NpcConfirmationPolicy.SecondConfirmation("response.confirm")));
        NpcResponsePolicy confirmation = Response("response.confirm");

        NpcConsequenceDecision decision = _gate.Evaluate(
            severe,
            1d,
            Responses(severe, confirmation));

        Assert.Equal(NpcConsequenceOutcome.ConfirmationRequired, decision.Outcome);
        Assert.Equal("response.confirm", decision.ResponseId);
        Assert.NotEqual(severe.Id, decision.ResponseId);
    }

    [Fact]
    public void UnsafeOrMissingSecondConfirmationPromptFailsClosed()
    {
        NpcResponsePolicy severe = Response(
            "response.severe",
            new NpcEffectPolicy(
                "quest_failure",
                NpcConfirmationPolicy.SecondConfirmation("response.confirm")));
        NpcResponsePolicy unsafePrompt = Response(
            "response.confirm",
            new NpcEffectPolicy(
                "attack",
                NpcConfirmationPolicy.HighConfidence(0d)));

        Assert.Equal(
            NpcConsequenceOutcome.Blocked,
            _gate.Evaluate(severe, 1d, Responses(severe)).Outcome);
        Assert.Equal(
            NpcConsequenceOutcome.Blocked,
            _gate.Evaluate(severe, 1d, Responses(severe, unsafePrompt)).Outcome);
    }

    private static NpcResponsePolicy Response(string id, params NpcEffectPolicy[] effects) =>
        new(id, effects);

    private static IReadOnlyDictionary<string, NpcResponsePolicy> Responses(
        params NpcResponsePolicy[] responses) =>
        responses.ToDictionary(response => response.Id, StringComparer.Ordinal);
}
