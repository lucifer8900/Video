namespace Lingmai.RedMist.Api.Intent;

public sealed record IntentCandidate(
    string Id,
    IReadOnlyList<string> KeywordPhrases,
    double MinimumConfidence);

public sealed record IntentClassificationContext(
    string Transcript,
    IReadOnlyList<IntentCandidate> AllowedIntents);

public enum IntentOutcome
{
    Matched,
    Irrelevant,
    LowConfidence,
}

public sealed record IntentDecision(
    IntentOutcome Outcome,
    string? IntentId,
    double Confidence,
    string Source);

public interface IIntentClassifier
{
    Task<IntentDecision> ClassifyAsync(
        IntentClassificationContext context,
        CancellationToken cancellationToken);
}

public static class IntentErrorCodes
{
    public const string InvalidRequest = "intent.invalid_request";
    public const string UnknownNode = "intent.unknown_node";
    public const string InvalidProviderResponse = "intent.invalid_provider_response";
    public const string ProviderUnavailable = "intent.provider_unavailable";
}

public sealed class IntentProviderException : Exception
{
    public IntentProviderException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public IntentProviderException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
