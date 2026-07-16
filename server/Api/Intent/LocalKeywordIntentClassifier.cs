using System.Text;

namespace Lingmai.RedMist.Api.Intent;

public sealed class LocalKeywordIntentClassifier : IIntentClassifier
{
    private const string Source = "local_keyword";

    private static readonly string[] NegationMarkers =
    [
        "并不是", "并不想", "不愿意", "不可以", "不可能", "没打算",
        "不要", "不想", "不愿", "不能", "不会", "不肯", "不必",
        "并非", "并不", "绝不", "休想", "无意", "没有", "拒绝",
        "别", "莫", "勿", "未", "没", "不",
    ];

    private static readonly string[] PositiveScopeMarkers =
    [
        "愿意", "可以", "打算", "决定", "肯定", "会", "要", "想", "肯",
    ];

    private static readonly string[] NegativeSuffixes =
    [
        "不了", "不成", "不行", "不要", "算了",
    ];

    public Task<IntentDecision> ClassifyAsync(
        IntentClassificationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string transcript = Normalize(context.Transcript);
        IReadOnlyList<IntentCandidate> candidates =
            context.AllowedIntents ?? Array.Empty<IntentCandidate>();
        if (transcript.Length == 0 || candidates.Count == 0)
            return Task.FromResult(Irrelevant());

        var matches = new List<CandidateMatch>();
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IntentCandidate? candidate = candidates[candidateIndex];
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.Id) ||
                IsReserved(candidate.Id))
            {
                continue;
            }

            var phrases = new HashSet<string>(StringComparer.Ordinal);
            int longest = 0;
            int totalLength = 0;
            int exactMatches = 0;
            IReadOnlyList<string> keywordPhrases =
                candidate.KeywordPhrases ?? Array.Empty<string>();
            for (var phraseIndex = 0; phraseIndex < keywordPhrases.Count; phraseIndex++)
            {
                string phrase = Normalize(keywordPhrases[phraseIndex]);
                if (phrase.Length == 0 || !phrases.Add(phrase)) continue;
                if (!HasAffirmativeOccurrence(transcript, phrase)) continue;

                longest = Math.Max(longest, phrase.Length);
                totalLength += phrase.Length;
                if (string.Equals(transcript, phrase, StringComparison.Ordinal)) exactMatches++;
            }

            if (longest > 0)
            {
                matches.Add(new CandidateMatch(
                    candidate,
                    longest,
                    phrases.Count(phrase => HasAffirmativeOccurrence(transcript, phrase)),
                    totalLength,
                    exactMatches));
            }
        }

        if (matches.Count == 0) return Task.FromResult(Irrelevant());

        matches.Sort(static (left, right) => CompareStrength(right, left));
        CandidateMatch strongest = matches[0];
        if (matches.Count > 1 && CompareStrength(strongest, matches[1]) == 0)
        {
            return Task.FromResult(new IntentDecision(
                IntentOutcome.LowConfidence,
                null,
                0.5d,
                Source));
        }

        double confidence = strongest.ExactMatches > 0
            ? 0.95d
            : strongest.MatchCount > 1
                ? 0.9d
                : 0.85d;
        return Task.FromResult(new IntentDecision(
            IntentOutcome.Matched,
            strongest.Candidate.Id,
            confidence,
            Source));
    }

    private static bool HasAffirmativeOccurrence(string transcript, string phrase)
    {
        var start = 0;
        while (start <= transcript.Length - phrase.Length)
        {
            int index = transcript.IndexOf(phrase, start, StringComparison.Ordinal);
            if (index < 0) return false;
            if (!IsNegated(transcript, index, phrase.Length)) return true;
            start = index + Math.Max(phrase.Length, 1);
        }

        return false;
    }

    private static bool IsNegated(string transcript, int phraseStart, int phraseLength)
    {
        int prefixStart = Math.Max(0, phraseStart - 7);
        string prefix = transcript[prefixStart..phraseStart];
        int negationStart = -1;
        int negationEnd = -1;
        foreach (string marker in NegationMarkers)
        {
            int markerStart = prefix.LastIndexOf(marker, StringComparison.Ordinal);
            int markerEnd = markerStart < 0 ? -1 : markerStart + marker.Length;
            if (markerStart > negationStart ||
                (markerStart == negationStart && markerEnd > negationEnd))
            {
                negationStart = markerStart;
                negationEnd = markerEnd;
            }
        }

        if (negationStart >= 0)
        {
            foreach (string marker in PositiveScopeMarkers)
            {
                int positiveStart = prefix.LastIndexOf(marker, StringComparison.Ordinal);
                if (positiveStart >= negationEnd) return false;
            }

            return true;
        }

        int suffixStart = phraseStart + phraseLength;
        int suffixLength = Math.Min(4, transcript.Length - suffixStart);
        if (suffixLength <= 0) return false;
        string suffix = transcript.Substring(suffixStart, suffixLength);
        return NegativeSuffixes.Any(marker => suffix.StartsWith(marker, StringComparison.Ordinal));
    }

    private static int CompareStrength(CandidateMatch left, CandidateMatch right)
    {
        int comparison = left.LongestPhrase.CompareTo(right.LongestPhrase);
        if (comparison != 0) return comparison;
        comparison = left.MatchCount.CompareTo(right.MatchCount);
        if (comparison != 0) return comparison;
        comparison = left.TotalMatchedLength.CompareTo(right.TotalMatchedLength);
        if (comparison != 0) return comparison;
        return left.ExactMatches.CompareTo(right.ExactMatches);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character)) builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsReserved(string id) =>
        string.Equals(id, "irrelevant", StringComparison.Ordinal) ||
        string.Equals(id, "low_confidence", StringComparison.Ordinal);

    private static IntentDecision Irrelevant() =>
        new(IntentOutcome.Irrelevant, null, 0d, Source);

    private sealed record CandidateMatch(
        IntentCandidate Candidate,
        int LongestPhrase,
        int MatchCount,
        int TotalMatchedLength,
        int ExactMatches);
}
