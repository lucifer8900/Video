namespace Lingmai.RedMist.Api.Intent;

public sealed class VoiceInteractionService
{
    private readonly InvalidInputDetector _detector;
    private readonly IntentClassificationService _classifier;
    private readonly NpcConsequenceGate _gate;

    public VoiceInteractionService(
        InvalidInputDetector detector,
        IntentClassificationService classifier,
        NpcConsequenceGate gate)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public async Task<VoiceInteractionDecision> ResolveAsync(
        string transcript,
        NodeVoicePolicy policy,
        int storyCharacterLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        InvalidInputSignal? detected = _detector.Detect(transcript, storyCharacterLimit);
        if (detected is not null)
            return ResolveInvalid(detected, policy);

        IntentDecision intent = await _classifier.ClassifyAsync(
            new IntentClassificationContext(transcript.Trim(), policy.AllowedIntents),
            cancellationToken).ConfigureAwait(false);
        if (intent.Outcome == IntentOutcome.Matched && intent.IntentId is not null)
        {
            if (!policy.IntentResponses.TryGetValue(intent.IntentId, out string? responseId))
                return Fallback(null, intent.IntentId, intent.Confidence, intent.Source);
            return ResolveResponse(
                VoiceInteractionResolution.IntentMatched,
                null,
                intent.IntentId,
                intent.Confidence,
                intent.Source,
                responseId,
                policy);
        }

        InvalidInputKind kind = intent.Outcome == IntentOutcome.Irrelevant
            ? InvalidInputKind.Irrelevant
            : InvalidInputKind.LowConfidence;
        return ResolveInvalid(
            new InvalidInputSignal(kind, intent.Confidence, intent.Source),
            policy);
    }

    private VoiceInteractionDecision ResolveInvalid(
        InvalidInputSignal signal,
        NodeVoicePolicy policy)
    {
        if (!policy.InvalidInputResponses.TryGetValue(signal.Kind, out string? responseId))
            return Fallback(signal.Kind, null, signal.Confidence, signal.Source);
        return ResolveResponse(
            VoiceInteractionResolution.NpcReaction,
            signal.Kind,
            null,
            signal.Confidence,
            signal.Source,
            responseId,
            policy);
    }

    private VoiceInteractionDecision ResolveResponse(
        VoiceInteractionResolution allowedResolution,
        InvalidInputKind? inputKind,
        string? intentId,
        double confidence,
        string source,
        string responseId,
        NodeVoicePolicy policy)
    {
        if (!policy.Responses.TryGetValue(responseId, out NpcResponsePolicy? response))
            return Fallback(inputKind, intentId, confidence, source);

        NpcConsequenceDecision consequence = _gate.Evaluate(
            response,
            confidence,
            policy.Responses);
        return consequence.Outcome switch
        {
            NpcConsequenceOutcome.Allowed => new VoiceInteractionDecision(
                allowedResolution,
                inputKind,
                intentId,
                confidence,
                source,
                consequence.ResponseId),
            NpcConsequenceOutcome.ConfirmationRequired => new VoiceInteractionDecision(
                VoiceInteractionResolution.ConfirmationRequired,
                inputKind,
                intentId,
                confidence,
                source,
                consequence.ResponseId),
            _ => Fallback(inputKind, intentId, confidence, source),
        };
    }

    private static VoiceInteractionDecision Fallback(
        InvalidInputKind? inputKind,
        string? intentId,
        double confidence,
        string source) =>
        new(
            VoiceInteractionResolution.FixedChoiceFallback,
            inputKind,
            intentId,
            double.IsFinite(confidence) && confidence is >= 0d and <= 1d ? confidence : 0d,
            string.IsNullOrWhiteSpace(source) ? "guard" : source,
            null);
}
