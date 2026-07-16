namespace Lingmai.RedMist.Api.Intent;

public sealed class NpcConsequenceGate
{
    private static readonly HashSet<string> SevereOperations = new(StringComparer.Ordinal)
    {
        "attack",
        "permanent_relationship_break",
        "quest_failure",
    };

    public NpcConsequenceDecision Evaluate(
        NpcResponsePolicy response,
        double confidence,
        IReadOnlyDictionary<string, NpcResponsePolicy> allowedResponses)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(allowedResponses);

        string? confirmationResponseId = null;
        foreach (NpcEffectPolicy effect in response.Effects ?? Array.Empty<NpcEffectPolicy>())
        {
            if (effect is null || !SevereOperations.Contains(effect.Operation)) continue;
            NpcConfirmationPolicy? confirmation = effect.Confirmation;
            if (confirmation is null) return Blocked();

            if (confirmation.Mode == NpcConfirmationMode.HighConfidence)
            {
                if (!IsUnit(confidence) ||
                    !IsUnit(confirmation.MinimumConfidence) ||
                    confidence < confirmation.MinimumConfidence)
                {
                    return Blocked();
                }
                continue;
            }

            if (confirmation.Mode != NpcConfirmationMode.NpcSecondConfirmation ||
                string.IsNullOrWhiteSpace(confirmation.NpcResponseRef))
            {
                return Blocked();
            }

            if (confirmationResponseId is not null &&
                !string.Equals(
                    confirmationResponseId,
                    confirmation.NpcResponseRef,
                    StringComparison.Ordinal))
            {
                return Blocked();
            }
            confirmationResponseId = confirmation.NpcResponseRef;
        }

        if (confirmationResponseId is null)
            return new NpcConsequenceDecision(NpcConsequenceOutcome.Allowed, response.Id);
        if (string.Equals(confirmationResponseId, response.Id, StringComparison.Ordinal) ||
            !allowedResponses.TryGetValue(confirmationResponseId, out NpcResponsePolicy? prompt) ||
            prompt.Effects.Count != 0)
        {
            return Blocked();
        }

        return new NpcConsequenceDecision(
            NpcConsequenceOutcome.ConfirmationRequired,
            prompt.Id);
    }

    private static bool IsUnit(double value) =>
        double.IsFinite(value) && value is >= 0d and <= 1d;

    private static NpcConsequenceDecision Blocked() =>
        new(NpcConsequenceOutcome.Blocked, null);
}
