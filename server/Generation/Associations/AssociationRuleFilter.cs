using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Associations;

/// <summary>
/// Applies only deterministic, server-owned association rules. No provider is
/// consulted and no value is inferred from free text.
/// </summary>
public sealed class AssociationRuleFilter
{
    private const string RouteLocationSource = "route.upcomingNode.location";
    private const string LedgerActorSourcePrefix = "ledger.actors[";

    public IReadOnlyList<AssociationRuleCandidate> Filter(
        AssociationDecisionRequest request,
        IReadOnlyList<ApprovedAssociationTemplate> templates) =>
        Filter(request, templates, rejectionObserver: null);

    internal IReadOnlyList<AssociationRuleCandidate> Filter(
        AssociationDecisionRequest request,
        IReadOnlyList<ApprovedAssociationTemplate> templates,
        Action<AssociationConsistencyRejectionReason>? rejectionObserver)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(templates);

        if (!IsUsableRequest(request))
        {
            return Array.Empty<AssociationRuleCandidate>();
        }

        var candidates = new List<AssociationRuleCandidate>();
        foreach (ApprovedAssociationTemplate approved in templates
                     .Where(item => item is not null && item.Template is not null)
                     .OrderBy(item => item.Template.TemplateId, StringComparer.Ordinal)
                     .ThenBy(item => item.SourcePath, StringComparer.Ordinal))
        {
            if (TryCreateCandidate(
                    request,
                    approved,
                    out AssociationRuleCandidate? candidate,
                    out AssociationConsistencyRejectionReason? rejectionReason))
            {
                candidates.Add(candidate);
            }
            else if (rejectionReason.HasValue)
            {
                rejectionObserver?.Invoke(rejectionReason.Value);
            }
        }

        return Array.AsReadOnly(candidates.ToArray());
    }

    private static bool IsUsableRequest(AssociationDecisionRequest request) =>
        !string.IsNullOrWhiteSpace(request.PlayerId) &&
        !string.IsNullOrWhiteSpace(request.Chapter) &&
        request.WorldClock >= 0 &&
        request.Route is not null &&
        !string.IsNullOrWhiteSpace(request.Route.UpcomingNodeId) &&
        request.Route.AllowedInjectionPoints is not null &&
        request.ActiveFactIds is not null &&
        request.ActiveFactIds.All(item => !string.IsNullOrWhiteSpace(item)) &&
        request.LedgerEntries is not null &&
        request.TriggerHistory is not null;

    private static bool TryCreateCandidate(
        AssociationDecisionRequest request,
        ApprovedAssociationTemplate approved,
        [NotNullWhen(true)] out AssociationRuleCandidate? candidate,
        out AssociationConsistencyRejectionReason? rejectionReason)
    {
        candidate = null;
        rejectionReason = null;
        AssociationTemplateContract template = approved.Template;
        if (!string.Equals(template.ApprovalStatus, "approved", StringComparison.Ordinal) ||
            template.Preconditions is null ||
            template.Preconditions.RequiresLedger is null ||
            template.Preconditions.RequiresLedger.Count == 0 ||
            template.Preconditions.ForbidsFacts is null ||
            template.Preconditions.ChapterWindow is null ||
            template.ParameterSlots is null ||
            template.InjectionPoints is null ||
            !template.Preconditions.ChapterWindow.Contains(request.Chapter, StringComparer.Ordinal) ||
            !AllowsMediaPolicy(template.MediaPolicy, request.EffectiveRuntimePolicy))
        {
            return false;
        }

        if (!TryResolveInjectionPoint(
                template.InjectionPoints,
                request.Route.AllowedInjectionPoints,
                out string? injectionPoint) ||
            !TryMatchAllLedgerRequirements(
                template.Preconditions.RequiresLedger,
                request,
                out NarrativeLedgerEntry? primaryEvidence) ||
            !TryResolveParameters(
                template.ParameterSlots,
                request.Route,
                primaryEvidence,
                out IReadOnlyDictionary<string, string>? parameters))
        {
            return false;
        }

        if (HasForbiddenFact(
                template.Preconditions.ForbidsFacts,
                request.ActiveFactIds,
                parameters,
                out bool matchedActiveFact))
        {
            if (matchedActiveFact)
            {
                rejectionReason = AssociationConsistencyRejectionReason.ForbiddenFact;
            }

            return false;
        }

        if (!AllowsTriggerHistory(template, request, out rejectionReason))
        {
            return false;
        }

        candidate = new AssociationRuleCandidate(
            approved,
            parameters,
            primaryEvidence,
            injectionPoint);
        return true;
    }

    private static bool AllowsMediaPolicy(
        string mediaPolicy,
        AssociationRuntimePolicy runtimePolicy)
    {
        if (string.Equals(mediaPolicy, AssociationMediaPolicies.ReuseOnly, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
                   mediaPolicy,
                   AssociationMediaPolicies.AllowRuntimeGeneration,
                   StringComparison.Ordinal) &&
               runtimePolicy.AllowRuntimeGeneration;
    }

    private static bool AllowsTriggerHistory(
        AssociationTemplateContract template,
        AssociationDecisionRequest request,
        out AssociationConsistencyRejectionReason? rejectionReason)
    {
        rejectionReason = null;
        if (template.CooldownWorldClock < 0 || template.MaxTriggersPerPlayer <= 0)
        {
            return false;
        }

        long totalTriggers = 0;
        foreach (AssociationTriggerHistory history in request.TriggerHistory)
        {
            if (history is null ||
                !string.Equals(history.TemplateId, template.TemplateId, StringComparison.Ordinal))
            {
                continue;
            }

            if (history.TriggerCount < 0 ||
                history.LastTriggeredWorldClock < 0 ||
                history.LastTriggeredWorldClock > request.WorldClock)
            {
                return false;
            }

            try
            {
                totalTriggers = checked(totalTriggers + history.TriggerCount);
            }
            catch (OverflowException)
            {
                return false;
            }

            long elapsed = request.WorldClock - history.LastTriggeredWorldClock;
            if (elapsed < template.CooldownWorldClock)
            {
                rejectionReason = AssociationConsistencyRejectionReason.CooldownActive;
                return false;
            }
        }

        if (totalTriggers >= template.MaxTriggersPerPlayer)
        {
            rejectionReason = AssociationConsistencyRejectionReason.TriggerLimitReached;
            return false;
        }

        return true;
    }

    private static bool TryResolveInjectionPoint(
        IReadOnlyList<string> templatePoints,
        IReadOnlyList<string> routePoints,
        [NotNullWhen(true)] out string? injectionPoint)
    {
        var allowed = new HashSet<string>(routePoints, StringComparer.Ordinal);
        injectionPoint = templatePoints
            .Where(item => item is not null && allowed.Contains(item))
            .OrderBy(item => item, StringComparer.Ordinal)
            .FirstOrDefault();
        return !string.IsNullOrWhiteSpace(injectionPoint);
    }

    private static bool TryMatchAllLedgerRequirements(
        IReadOnlyList<AssociationLedgerRequirementContract> requirements,
        AssociationDecisionRequest request,
        [NotNullWhen(true)] out NarrativeLedgerEntry? primaryEvidence)
    {
        primaryEvidence = null;
        for (int requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
        {
            AssociationLedgerRequirementContract requirement = requirements[requirementIndex];
            if (requirement is null || requirement.MaxAgeWorldClock < 0)
            {
                return false;
            }

            NarrativeLedgerEntry? evidence = request.LedgerEntries
                .Where(entry => MatchesLedgerRequirement(entry, requirement, request))
                .OrderByDescending(entry => entry.Event.WorldClock)
                .ThenByDescending(entry => entry.IngestSequence)
                .ThenBy(entry => entry.Event.EntryId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (evidence is null)
            {
                return false;
            }

            if (requirementIndex == 0)
            {
                primaryEvidence = evidence;
            }
        }

        return primaryEvidence is not null;
    }

    private static bool MatchesLedgerRequirement(
        NarrativeLedgerEntry? entry,
        AssociationLedgerRequirementContract requirement,
        AssociationDecisionRequest request)
    {
        if (entry?.Event is null ||
            string.IsNullOrWhiteSpace(entry.Event.EntryId) ||
            !string.Equals(entry.Event.PlayerId, request.PlayerId, StringComparison.Ordinal) ||
            !string.Equals(entry.Event.Chapter, request.Chapter, StringComparison.Ordinal) ||
            !string.Equals(entry.Event.Type, requirement.Type, StringComparison.Ordinal) ||
            entry.Event.Severity < requirement.MinSeverity ||
            entry.Event.WorldClock < 0 ||
            entry.Event.WorldClock > request.WorldClock)
        {
            return false;
        }

        long age = request.WorldClock - entry.Event.WorldClock;
        return age <= requirement.MaxAgeWorldClock;
    }

    private static bool TryResolveParameters(
        IReadOnlyDictionary<string, AssociationParameterSlotContract> slots,
        AssociationRouteContext route,
        NarrativeLedgerEntry primaryEvidence,
        [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? resolved)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, AssociationParameterSlotContract slot) in
                 slots.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name) ||
                slot is null ||
                !TryResolveParameterValue(slot.Source, route, primaryEvidence, out string? value))
            {
                resolved = null;
                return false;
            }

            values.Add(name, value);
        }

        resolved = values;
        return true;
    }

    private static bool TryResolveParameterValue(
        string source,
        AssociationRouteContext route,
        NarrativeLedgerEntry evidence,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (string.Equals(source, RouteLocationSource, StringComparison.Ordinal))
        {
            value = route.UpcomingLocationId;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (source is null ||
            !source.StartsWith(LedgerActorSourcePrefix, StringComparison.Ordinal) ||
            !source.EndsWith(']'))
        {
            return false;
        }

        ReadOnlySpan<char> indexText = source.AsSpan(
            LedgerActorSourcePrefix.Length,
            source.Length - LedgerActorSourcePrefix.Length - 1);
        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int actorIndex) ||
            evidence.Event.Actors is null ||
            actorIndex < 0 ||
            actorIndex >= evidence.Event.Actors.Count)
        {
            return false;
        }

        value = evidence.Event.Actors[actorIndex];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool HasForbiddenFact(
        IReadOnlyList<string> forbiddenPatterns,
        IReadOnlyList<string> activeFacts,
        IReadOnlyDictionary<string, string> parameters,
        out bool matchedActiveFact)
    {
        matchedActiveFact = false;
        var facts = new HashSet<string>(activeFacts, StringComparer.Ordinal);
        foreach (string pattern in forbiddenPatterns)
        {
            if (!TryResolvePattern(pattern, parameters, out string? forbiddenFact))
            {
                return true;
            }

            if (facts.Contains(forbiddenFact))
            {
                matchedActiveFact = true;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolvePattern(
        string pattern,
        IReadOnlyDictionary<string, string> parameters,
        [NotNullWhen(true)] out string? resolved)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            resolved = null;
            return false;
        }

        resolved = pattern;
        foreach ((string name, string value) in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            resolved = resolved.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return !resolved.Contains('{', StringComparison.Ordinal) &&
               !resolved.Contains('}', StringComparison.Ordinal);
    }
}
