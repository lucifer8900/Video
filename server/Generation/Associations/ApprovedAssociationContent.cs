using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Associations;

namespace Lingmai.RedMist.Generation.Associations;

internal sealed record ApprovedTextVariantFixture(
    string TemplateId,
    string VariantToken,
    string InjectionPoint,
    string Text,
    IReadOnlyList<string> MediaRefs);

public sealed class ApprovedTextVariant
{
    internal ApprovedTextVariant(
        string templateId,
        string variantToken,
        string injectionPoint,
        string text,
        IReadOnlyList<string> mediaRefs)
    {
        TemplateId = templateId;
        VariantToken = variantToken;
        InjectionPoint = injectionPoint;
        Text = text;
        MediaRefs = Array.AsReadOnly(mediaRefs.ToArray());
    }

    public string TemplateId { get; }

    public string VariantToken { get; }

    public string InjectionPoint { get; }

    public string Text { get; }

    public IReadOnlyList<string> MediaRefs { get; }
}

public sealed class ApprovedTextVariantRegistry
{
    private readonly IReadOnlyDictionary<string, ApprovedTextVariant> _byTemplateAndToken;

    private ApprovedTextVariantRegistry(IReadOnlyList<ApprovedTextVariant> entries)
    {
        Entries = Array.AsReadOnly(entries.ToArray());
        _byTemplateAndToken = new ReadOnlyDictionary<string, ApprovedTextVariant>(
            entries.ToDictionary(Key, StringComparer.Ordinal));
    }

    public static ApprovedTextVariantRegistry Empty { get; } = new(Array.Empty<ApprovedTextVariant>());

    public IReadOnlyList<ApprovedTextVariant> Entries { get; }

    internal static ApprovedTextVariantRegistry CreateForTests(
        IEnumerable<ApprovedTextVariantFixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        var entries = new List<ApprovedTextVariant>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApprovedTextVariantFixture fixture in fixtures)
        {
            if (fixture is null ||
                !ApprovedAssociationContentValidation.IsAssociationId(fixture.TemplateId) ||
                !ApprovedAssociationContentValidation.IsVariantToken(fixture.VariantToken) ||
                !StoryThreadInjectionPoints.All.Contains(fixture.InjectionPoint) ||
                !ApprovedAssociationContentValidation.IsApprovedText(fixture.Text) ||
                !ApprovedAssociationContentValidation.TryFreezeIds(
                    fixture.MediaRefs,
                    16,
                    out IReadOnlyList<string> mediaRefs) ||
                !keys.Add(Key(fixture.TemplateId, fixture.VariantToken)) ||
                !tokens.Add(fixture.VariantToken))
            {
                throw new ArgumentException(
                    "Approved text variants must be unique, bounded, and schema-valid.",
                    nameof(fixtures));
            }

            entries.Add(new ApprovedTextVariant(
                fixture.TemplateId,
                fixture.VariantToken,
                fixture.InjectionPoint,
                fixture.Text,
                mediaRefs));
        }

        return new ApprovedTextVariantRegistry(entries);
    }

    internal IReadOnlyList<ApprovedTextVariant> FindForCandidate(
        string templateId,
        IReadOnlyCollection<string> allowedInjectionPoints) =>
        Array.AsReadOnly(Entries
            .Where(item =>
                string.Equals(item.TemplateId, templateId, StringComparison.Ordinal) &&
                allowedInjectionPoints.Contains(item.InjectionPoint, StringComparer.Ordinal))
            .OrderBy(item => item.VariantToken, StringComparer.Ordinal)
            .ToArray());

    internal bool TryGet(
        string templateId,
        string variantToken,
        out ApprovedTextVariant variant) =>
        _byTemplateAndToken.TryGetValue(Key(templateId, variantToken), out variant!);

    private static string Key(ApprovedTextVariant entry) => Key(entry.TemplateId, entry.VariantToken);

    private static string Key(string templateId, string variantToken) => templateId + "\n" + variantToken;
}

internal sealed record ApprovedFallbackStoryThreadFixture(
    string ThreadId,
    string InjectionPoint,
    string Text,
    IReadOnlyList<StoryThreadEffectContract> Effects,
    IReadOnlyList<string> MediaRefs);

public sealed class ApprovedFallbackStoryThread
{
    internal ApprovedFallbackStoryThread(
        string threadId,
        string injectionPoint,
        string text,
        IReadOnlyList<StoryThreadEffectContract> effects,
        IReadOnlyList<string> mediaRefs)
    {
        ThreadId = threadId;
        InjectionPoint = injectionPoint;
        Text = text;
        Effects = Array.AsReadOnly(effects.Select(ApprovedAssociationContentValidation.CloneEffect).ToArray());
        MediaRefs = Array.AsReadOnly(mediaRefs.ToArray());
    }

    public string ThreadId { get; }

    public string InjectionPoint { get; }

    public string Text { get; }

    public IReadOnlyList<StoryThreadEffectContract> Effects { get; }

    public IReadOnlyList<string> MediaRefs { get; }
}

public sealed class ApprovedFallbackStoryThreadCatalog
{
    private readonly IReadOnlyDictionary<string, ApprovedFallbackStoryThread> _byThreadId;

    private ApprovedFallbackStoryThreadCatalog(IReadOnlyList<ApprovedFallbackStoryThread> entries)
    {
        Entries = Array.AsReadOnly(entries.ToArray());
        _byThreadId = new ReadOnlyDictionary<string, ApprovedFallbackStoryThread>(
            entries.ToDictionary(entry => entry.ThreadId, StringComparer.Ordinal));
    }

    public static ApprovedFallbackStoryThreadCatalog Empty { get; } =
        new(Array.Empty<ApprovedFallbackStoryThread>());

    public IReadOnlyList<ApprovedFallbackStoryThread> Entries { get; }

    internal static ApprovedFallbackStoryThreadCatalog CreateForTests(
        IEnumerable<ApprovedFallbackStoryThreadFixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        var entries = new List<ApprovedFallbackStoryThread>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApprovedFallbackStoryThreadFixture fixture in fixtures)
        {
            if (fixture is null ||
                !ApprovedAssociationContentValidation.IsAssociationId(fixture.ThreadId) ||
                !StoryThreadInjectionPoints.All.Contains(fixture.InjectionPoint) ||
                !ApprovedAssociationContentValidation.IsApprovedText(fixture.Text) ||
                !ApprovedAssociationContentValidation.TryFreezeEffects(
                    fixture.Effects,
                    out IReadOnlyList<StoryThreadEffectContract> effects) ||
                !ApprovedAssociationContentValidation.TryFreezeIds(
                    fixture.MediaRefs,
                    16,
                    out IReadOnlyList<string> mediaRefs) ||
                !ids.Add(fixture.ThreadId))
            {
                throw new ArgumentException(
                    "Approved fallback threads must be unique, bounded, and schema-valid.",
                    nameof(fixtures));
            }

            entries.Add(new ApprovedFallbackStoryThread(
                fixture.ThreadId,
                fixture.InjectionPoint,
                fixture.Text,
                effects,
                mediaRefs));
        }

        return new ApprovedFallbackStoryThreadCatalog(entries);
    }

    internal bool TryGet(string threadId, out ApprovedFallbackStoryThread fallback) =>
        _byThreadId.TryGetValue(threadId, out fallback!);
}

internal static class ApprovedAssociationContentValidation
{
    private static readonly Regex StableIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VariantTokenPattern = new(
        "^var\\.[A-Za-z0-9][A-Za-z0-9._:-]{0,59}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ParameterNamePattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IReadOnlySet<string> RelationshipFields =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "trust",
            "respect",
            "fear",
            "debt",
            "suspicion",
            "affection",
        };

    internal static bool IsAssociationId(string? value) =>
        IsId(value) && value!.StartsWith("assoc.", StringComparison.Ordinal);

    internal static bool IsVariantToken(string? value) =>
        value is not null && VariantTokenPattern.IsMatch(value);

    internal static bool IsApprovedText(string? value) =>
        value is { Length: >= 1 and <= 512 } &&
        !string.IsNullOrWhiteSpace(value) &&
        value.All(character =>
        {
            UnicodeCategory category = char.GetUnicodeCategory(character);
            return !char.IsControl(character) &&
                   !char.IsSurrogate(character) &&
                   category is not UnicodeCategory.Format and
                       not UnicodeCategory.LineSeparator and
                       not UnicodeCategory.ParagraphSeparator;
        });

    internal static bool TryFreezeIds(
        IReadOnlyList<string>? values,
        int maximumCount,
        out IReadOnlyList<string> frozen)
    {
        frozen = Array.Empty<string>();
        if (values is null || values.Count > maximumCount) return false;
        string[] copy = values.ToArray();
        if (copy.Any(value => !IsId(value)) ||
            copy.Distinct(StringComparer.Ordinal).Count() != copy.Length ||
            copy.Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
        {
            return false;
        }

        frozen = Array.AsReadOnly(copy);
        return true;
    }

    internal static bool TryFreezeEffects(
        IReadOnlyList<StoryThreadEffectContract>? effects,
        out IReadOnlyList<StoryThreadEffectContract> frozen)
    {
        frozen = Array.Empty<StoryThreadEffectContract>();
        if (effects is null || effects.Count > 16) return false;
        var copy = new List<StoryThreadEffectContract>(effects.Count);
        foreach (StoryThreadEffectContract effect in effects)
        {
            if (!IsValidEffect(effect)) return false;
            copy.Add(CloneEffect(effect));
        }

        frozen = Array.AsReadOnly(copy.ToArray());
        return true;
    }

    internal static StoryThreadEffectContract CloneEffect(StoryThreadEffectContract effect) =>
        new(effect.Op, effect.TargetRef, effect.Field, effect.Delta, effect.Value);

    internal static bool IsId(string? value) =>
        value is { Length: >= 1 and <= 160 } && StableIdPattern.IsMatch(value);

    internal static bool IsValidThreadContent(
        string templateId,
        string playerId,
        IReadOnlyDictionary<string, string> resolvedParameters,
        IReadOnlyList<StoryThreadInjectionContract> injections,
        IReadOnlyList<string> mediaRefs)
    {
        if (!IsAssociationId(templateId) ||
            !AssociationDecisionLedgerValidation.IsPlayerId(playerId) ||
            resolvedParameters is null ||
            resolvedParameters.Count > 16 ||
            resolvedParameters.Any(item =>
                !ParameterNamePattern.IsMatch(item.Key) ||
                !IsId(item.Value)) ||
            resolvedParameters.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                resolvedParameters.Count ||
            injections is null ||
            injections.Count is < 1 or > 16 ||
            mediaRefs is null ||
            mediaRefs.Count > 16 ||
            mediaRefs.Any(item => !IsId(item)) ||
            mediaRefs.Distinct(StringComparer.Ordinal).Count() != mediaRefs.Count)
        {
            return false;
        }

        foreach (StoryThreadInjectionContract injection in injections)
        {
            if (injection is null ||
                !IsId(injection.NodeId) ||
                !StoryThreadInjectionPoints.All.Contains(injection.Point) ||
                !IsApprovedText(injection.Text) ||
                injection.Effects is null ||
                injection.Effects.Count > 16 ||
                injection.Effects.Any(effect => !IsValidEffect(effect)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidEffect(StoryThreadEffectContract? effect)
    {
        if (effect is null || !StoryThreadEffectOperations.All.Contains(effect.Op)) return false;
        if (effect.Op == StoryThreadEffectOperations.Relationship)
        {
            return IsId(effect.TargetRef) &&
                   effect.Field is not null &&
                   RelationshipFields.Contains(effect.Field) &&
                   effect.Delta is double delta &&
                   double.IsFinite(delta) &&
                   delta is >= -10 and <= 10 &&
                   effect.Value is null;
        }

        string requiredPrefix = effect.Op == StoryThreadEffectOperations.Clue
            ? "clue."
            : effect.Op == StoryThreadEffectOperations.BranchUnlock
                ? "branch."
                : string.Empty;
        return requiredPrefix.Length > 0 &&
               effect.TargetRef is null &&
               effect.Field is null &&
               effect.Delta is null &&
               IsId(effect.Value) &&
               effect.Value!.StartsWith(requiredPrefix, StringComparison.Ordinal);
    }
}
