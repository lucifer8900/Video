namespace Lingmai.RedMist.Api.Intent;

public sealed class IntentClassificationService
{
    private const string GuardSource = "guard";
    private const string EmptyAllowListSource = "allow_list";

    private readonly IIntentClassifier _primary;
    private readonly IIntentClassifier _fallback;

    public IntentClassificationService(
        IIntentClassifier primary,
        IIntentClassifier fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public async Task<IntentDecision> ClassifyAsync(
        IntentClassificationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IntentCandidate> allowed = context.AllowedIntents ?? Array.Empty<IntentCandidate>();
        if (allowed.Count == 0)
            return new IntentDecision(IntentOutcome.Irrelevant, null, 0d, EmptyAllowListSource);

        IntentDecision decision;
        try
        {
            decision = await _primary.ClassifyAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (IntentProviderException)
        {
            decision = await _fallback.ClassifyAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return Guard(decision, allowed);
    }

    private static IntentDecision Guard(
        IntentDecision? decision,
        IReadOnlyList<IntentCandidate> allowed)
    {
        if (decision is null || !IsValidConfidence(decision.Confidence))
            return LowConfidence(0d, decision?.Source);

        string source = string.IsNullOrWhiteSpace(decision.Source) ? GuardSource : decision.Source;
        if (decision.Outcome == IntentOutcome.Irrelevant)
            return new IntentDecision(IntentOutcome.Irrelevant, null, decision.Confidence, source);
        if (decision.Outcome == IntentOutcome.LowConfidence)
            return new IntentDecision(IntentOutcome.LowConfidence, null, decision.Confidence, source);
        if (decision.Outcome != IntentOutcome.Matched || string.IsNullOrWhiteSpace(decision.IntentId))
            return LowConfidence(decision.Confidence, source);

        IntentCandidate? candidate = FindCandidate(allowed, decision.IntentId);
        if (candidate is null)
            return LowConfidence(decision.Confidence, source);

        double threshold = IsValidConfidence(candidate.MinimumConfidence)
            ? candidate.MinimumConfidence
            : 1d;
        if (decision.Confidence < threshold)
            return LowConfidence(decision.Confidence, source);

        return new IntentDecision(
            IntentOutcome.Matched,
            candidate.Id,
            decision.Confidence,
            source);
    }

    private static IntentCandidate? FindCandidate(
        IReadOnlyList<IntentCandidate> allowed,
        string intentId)
    {
        IntentCandidate? match = null;
        for (int index = 0; index < allowed.Count; index++)
        {
            IntentCandidate? candidate = allowed[index];
            if (candidate is null ||
                !string.Equals(candidate.Id, intentId, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null) return null;
            match = candidate;
        }

        return match;
    }

    private static bool IsValidConfidence(double confidence) =>
        !double.IsNaN(confidence) &&
        !double.IsInfinity(confidence) &&
        confidence >= 0d &&
        confidence <= 1d;

    private static IntentDecision LowConfidence(double confidence, string? source) =>
        new(
            IntentOutcome.LowConfidence,
            null,
            IsValidConfidence(confidence) ? confidence : 0d,
            string.IsNullOrWhiteSpace(source) ? GuardSource : source);
}
