using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Associations;

public interface IAssociationRanker
{
    Task<string?> RankAsync(
        AssociationRankerRequest request,
        CancellationToken cancellationToken);
}

public sealed class MockAssociationRanker : IAssociationRanker
{
    private readonly object _sync = new();
    private readonly Queue<string?> _responses;
    private readonly List<AssociationRankerRequest> _requests = new();

    public MockAssociationRanker(params string?[] responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        _responses = new Queue<string?>(responses);
    }

    public int CallCount
    {
        get
        {
            lock (_sync) return _requests.Count;
        }
    }

    public IReadOnlyList<AssociationRankerRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(_requests.ToArray());
            }
        }
    }

    public Task<string?> RankAsync(
        AssociationRankerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _requests.Add(request);
            return Task.FromResult(_responses.Count == 0 ? null : _responses.Dequeue());
        }
    }
}

public static class AssociationRankerWireProtocol
{
    public const int MaximumResponseBytes = 64 * 1024;
    public const int MaximumProposalCount = 32;
    public const int MaximumEffectSelectionCount = 16;
    public const int MaximumJsonDepth = 16;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth,
    };
    private static readonly Regex CandidateTokenPattern = new(
        "^cand\\.[0-9]{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VariantTokenPattern = new(
        "^var\\.[A-Za-z0-9][A-Za-z0-9._:-]{0,59}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool TryParse(
        string? rawJson,
        out IReadOnlyList<AssociationRankerProposal> proposals)
    {
        proposals = Array.Empty<AssociationRankerProposal>();
        if (string.IsNullOrWhiteSpace(rawJson) ||
            Encoding.UTF8.GetByteCount(rawJson) > MaximumResponseBytes)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson, DocumentOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactProperties(root, "schemaVersion", "proposals") ||
                !root.TryGetProperty("schemaVersion", out JsonElement schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.String ||
                schemaVersion.GetString() != "1.0.0" ||
                !root.TryGetProperty("proposals", out JsonElement proposalArray) ||
                proposalArray.ValueKind != JsonValueKind.Array ||
                proposalArray.GetArrayLength() > MaximumProposalCount)
            {
                return false;
            }

            var rawTokenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in proposalArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                JsonProperty[] tokenProperties = item.EnumerateObject()
                    .Where(property => string.Equals(
                        property.Name,
                        "candidateToken",
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (tokenProperties.Length != 1 ||
                    tokenProperties[0].Value.ValueKind != JsonValueKind.String ||
                    tokenProperties[0].Value.GetString() is not string rawToken)
                {
                    continue;
                }

                rawTokenCounts.TryGetValue(rawToken, out int count);
                rawTokenCounts[rawToken] = count + 1;
            }

            var parsed = new List<AssociationRankerProposal>();
            int index = 0;
            foreach (JsonElement item in proposalArray.EnumerateArray())
            {
                if (TryParseProposal(item, index, out AssociationRankerProposal? proposal))
                {
                    parsed.Add(proposal!);
                }

                index++;
            }

            var duplicateTokens = rawTokenCounts
                .Where(item => item.Value > 1)
                .Select(item => item.Key)
                .Concat(parsed
                .GroupBy(item => item.CandidateToken, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            proposals = Array.AsReadOnly(parsed
                .Where(item => !duplicateTokens.Contains(item.CandidateToken))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.OriginalIndex)
                .ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseProposal(
        JsonElement item,
        int originalIndex,
        out AssociationRankerProposal? proposal)
    {
        proposal = null;
        if (item.ValueKind != JsonValueKind.Object ||
            !HasExactProperties(
                item,
                "candidateToken",
                "score",
                "variantToken",
                "effectSelections") ||
            !item.TryGetProperty("candidateToken", out JsonElement candidateTokenElement) ||
            candidateTokenElement.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("score", out JsonElement scoreElement) ||
            scoreElement.ValueKind != JsonValueKind.Number ||
            !item.TryGetProperty("variantToken", out JsonElement variantTokenElement) ||
            variantTokenElement.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("effectSelections", out JsonElement selectionsElement) ||
            selectionsElement.ValueKind != JsonValueKind.Array ||
            selectionsElement.GetArrayLength() > MaximumEffectSelectionCount)
        {
            return false;
        }

        string? candidateToken = candidateTokenElement.GetString();
        string? variantToken = variantTokenElement.GetString();
        if (candidateToken is null ||
            variantToken is null ||
            !CandidateTokenPattern.IsMatch(candidateToken) ||
            !VariantTokenPattern.IsMatch(variantToken) ||
            !scoreElement.TryGetDouble(out double score) ||
            !double.IsFinite(score) ||
            score is < 0 or > 1)
        {
            return false;
        }

        var selections = new List<AssociationRankerEffectSelection>();
        var indexes = new HashSet<int>();
        foreach (JsonElement selection in selectionsElement.EnumerateArray())
        {
            if (!TryParseSelection(selection, out AssociationRankerEffectSelection? parsed) ||
                !indexes.Add(parsed!.EffectIndex))
            {
                return false;
            }

            selections.Add(parsed);
        }

        proposal = new AssociationRankerProposal(
            candidateToken,
            score,
            variantToken,
            Array.AsReadOnly(selections.ToArray()),
            originalIndex);
        return true;
    }

    private static bool TryParseSelection(
        JsonElement item,
        out AssociationRankerEffectSelection? selection)
    {
        selection = null;
        if (item.ValueKind != JsonValueKind.Object ||
            !HasOnlyProperties(item, "effectIndex", "relationshipDelta") ||
            !item.TryGetProperty("effectIndex", out JsonElement indexElement) ||
            indexElement.ValueKind != JsonValueKind.Number ||
            !indexElement.TryGetInt32(out int index) ||
            index is < 0 or >= MaximumEffectSelectionCount)
        {
            return false;
        }

        double? delta = null;
        if (item.TryGetProperty("relationshipDelta", out JsonElement deltaElement))
        {
            if (deltaElement.ValueKind != JsonValueKind.Number ||
                !deltaElement.TryGetDouble(out double value) ||
                !double.IsFinite(value) ||
                value is < -10 or > 10)
            {
                return false;
            }

            delta = value;
        }

        selection = new AssociationRankerEffectSelection(index, delta);
        return true;
    }

    private static bool HasExactProperties(JsonElement element, params string[] names)
    {
        if (!HasOnlyProperties(element, names)) return false;
        var present = element.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        return names.All(present.Contains);
    }

    private static bool HasOnlyProperties(JsonElement element, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) ||
                !exact.Add(property.Name) ||
                !folded.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }
}
