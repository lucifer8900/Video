namespace Lingmai.RedMist.Api.Intent;

public enum InvalidInputKind
{
    Abuse,
    Irrelevant,
    TooLong,
    Silence,
    LowConfidence,
}

public sealed record InvalidInputSignal(
    InvalidInputKind Kind,
    double Confidence,
    string Source);

public enum NpcConfirmationMode
{
    HighConfidence,
    NpcSecondConfirmation,
}

public sealed record NpcConfirmationPolicy(
    NpcConfirmationMode Mode,
    double MinimumConfidence,
    string? NpcResponseRef)
{
    public static NpcConfirmationPolicy HighConfidence(double minimumConfidence) =>
        new(NpcConfirmationMode.HighConfidence, minimumConfidence, null);

    public static NpcConfirmationPolicy SecondConfirmation(string npcResponseRef) =>
        new(NpcConfirmationMode.NpcSecondConfirmation, 0d, npcResponseRef);
}

public sealed record NpcEffectPolicy(
    string Operation,
    NpcConfirmationPolicy? Confirmation);

public sealed record NpcResponsePolicy(
    string Id,
    IReadOnlyList<NpcEffectPolicy> Effects);

public sealed class NodeVoicePolicy
{
    public NodeVoicePolicy(
        IReadOnlyList<IntentCandidate> allowedIntents,
        IReadOnlyDictionary<string, string> intentResponses,
        IReadOnlyDictionary<InvalidInputKind, string> invalidInputResponses,
        IReadOnlyDictionary<string, NpcResponsePolicy> responses)
    {
        AllowedIntents = allowedIntents?.ToArray() ?? Array.Empty<IntentCandidate>();
        IntentResponses = new Dictionary<string, string>(
            intentResponses ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
        InvalidInputResponses = new Dictionary<InvalidInputKind, string>(
            invalidInputResponses ?? new Dictionary<InvalidInputKind, string>());
        Responses = new Dictionary<string, NpcResponsePolicy>(
            responses ?? new Dictionary<string, NpcResponsePolicy>(),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<IntentCandidate> AllowedIntents { get; }
    public Dictionary<string, string> IntentResponses { get; }
    public Dictionary<InvalidInputKind, string> InvalidInputResponses { get; }
    public Dictionary<string, NpcResponsePolicy> Responses { get; }
}

public interface IVoiceInteractionCatalog
{
    bool TryGetNodePolicy(string nodeId, out NodeVoicePolicy policy);
}

public enum NpcConsequenceOutcome
{
    Allowed,
    ConfirmationRequired,
    Blocked,
}

public sealed record NpcConsequenceDecision(
    NpcConsequenceOutcome Outcome,
    string? ResponseId);

public enum VoiceInteractionResolution
{
    IntentMatched,
    NpcReaction,
    ConfirmationRequired,
    FixedChoiceFallback,
}

public sealed record VoiceInteractionDecision(
    VoiceInteractionResolution Resolution,
    InvalidInputKind? InputKind,
    string? IntentId,
    double Confidence,
    string Source,
    string? NpcResponseId);
