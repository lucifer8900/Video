using System.Collections.ObjectModel;
using Lingmai.RedMist.Contracts.Associations;

namespace Lingmai.RedMist.Generation.Associations;

internal interface IAssociationDecisionIdFactory
{
    string CreateThreadId();

    string CreateAuditId();
}

internal sealed class SequenceAssociationDecisionIdFactory(
    string threadPrefix,
    string auditPrefix) : IAssociationDecisionIdFactory
{
    private int _threadSequence;
    private int _auditSequence;

    public string CreateThreadId() =>
        threadPrefix + "." + Interlocked.Increment(ref _threadSequence);

    public string CreateAuditId() =>
        auditPrefix + "." + Interlocked.Increment(ref _auditSequence);
}

public sealed class AssociationDecisionConfigurationException : InvalidOperationException
{
    internal AssociationDecisionConfigurationException()
        : base("The association decision service is not configured with approved content.")
    {
    }
}

public sealed class AssociationDecisionValidationException : InvalidOperationException
{
    internal AssociationDecisionValidationException()
        : base("The association decision request is invalid.")
    {
    }
}

public sealed class AssociationDecisionService
{
    internal static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(8);

    private readonly AssociationRuleFilter _ruleFilter;
    private readonly IAssociationRanker _ranker;
    private readonly ApprovedTextVariantRegistry _variants;
    private readonly ApprovedFallbackStoryThreadCatalog _fallbacks;
    private readonly IAssociationDecisionIdFactory _idFactory;
    private readonly TimeProvider _timeProvider;

    internal AssociationDecisionService(
        AssociationRuleFilter ruleFilter,
        IAssociationRanker ranker,
        ApprovedTextVariantRegistry variants,
        ApprovedFallbackStoryThreadCatalog fallbacks,
        IAssociationDecisionIdFactory idFactory,
        TimeProvider timeProvider)
    {
        _ruleFilter = ruleFilter ?? throw new ArgumentNullException(nameof(ruleFilter));
        _ranker = ranker ?? throw new ArgumentNullException(nameof(ranker));
        _variants = variants ?? throw new ArgumentNullException(nameof(variants));
        _fallbacks = fallbacks ?? throw new ArgumentNullException(nameof(fallbacks));
        _idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<StoryThreadContract> DecideAsync(
        AssociationDecisionRequest request,
        IReadOnlyList<ApprovedAssociationTemplate> templates,
        CancellationToken cancellationToken)
    {
        long startedTimestamp = _timeProvider.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        ValidateTemplates(templates);
        try
        {
            AssociationDecisionSnapshot snapshot = AssociationDecisionSnapshotter.Create(request, templates);
            request = snapshot.Request;
            templates = snapshot.Templates;
        }
        catch (Exception exception) when (exception is
            NullReferenceException or
            ArgumentException or
            InvalidOperationException)
        {
            throw new AssociationDecisionValidationException();
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AssociationRuleCandidate> filtered = _ruleFilter.Filter(request, templates);
        CandidateBinding[] bindings = filtered
            .OrderBy(item => item.Template.Template.TemplateId, StringComparer.Ordinal)
            .Select(CreateBinding)
            .Where(item => item is not null)
            .Take(AssociationRankerWireProtocol.MaximumProposalCount)
            .Cast<CandidateBinding>()
            .Select((item, index) => item with { CandidateToken = $"cand.{index:D4}" })
            .ToArray();
        if (bindings.Length == 0)
        {
            return CreateFallback(request, cancellationToken);
        }

        var rankerRequest = new AssociationRankerRequest(Array.AsReadOnly(bindings
            .Select(binding => ToRankerCandidate(binding, request.WorldClock))
            .ToArray()));
        string? rawResponse = await InvokeRankerAsync(
            rankerRequest,
            startedTimestamp,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AssociationRankerWireProtocol.TryParse(rawResponse, out IReadOnlyList<AssociationRankerProposal> proposals))
        {
            return CreateFallback(request, cancellationToken);
        }

        var byToken = bindings.ToDictionary(item => item.CandidateToken, StringComparer.Ordinal);
        string? candidateFallbackThreadId = null;
        foreach (AssociationRankerProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byToken.TryGetValue(proposal.CandidateToken, out CandidateBinding? binding))
            {
                continue;
            }

            candidateFallbackThreadId ??= binding.Candidate.Template.Template.FallbackThreadId;
            if (!TryMaterialize(
                    request,
                    binding,
                    proposal,
                    cancellationToken,
                    out StoryThreadContract? thread))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return thread!;
        }

        return CreateFallback(request, cancellationToken, candidateFallbackThreadId);
    }

    private CandidateBinding? CreateBinding(AssociationRuleCandidate candidate)
    {
        string templateId = candidate.Template.Template.TemplateId;
        IReadOnlyList<ApprovedTextVariant> variants = _variants.FindForCandidate(
            templateId,
            new[] { candidate.InjectionPoint });
        return variants.Count == 0
            ? null
            : new CandidateBinding(string.Empty, candidate, variants);
    }

    private static AssociationRankerCandidate ToRankerCandidate(
        CandidateBinding binding,
        long currentWorldClock)
    {
        AssociationTemplateContract template = binding.Candidate.Template.Template;
        var effectOptions = new AssociationRankerEffectOption[template.AllowedEffects.Count];
        for (int index = 0; index < template.AllowedEffects.Count; index++)
        {
            AssociationAllowedEffectContract effect = template.AllowedEffects[index];
            effectOptions[index] = effect.Op == AssociationEffectOperations.Relationship &&
                                   effect.Range is { Count: 2 }
                ? new AssociationRankerEffectOption(index, effect.Range[0], effect.Range[1])
                : new AssociationRankerEffectOption(index, null, null);
        }

        long evidenceClock = binding.Candidate.PrimaryEvidence.Event.WorldClock;
        long age = evidenceClock >= 0 && evidenceClock <= currentWorldClock
            ? currentWorldClock - evidenceClock
            : 0;
        return new AssociationRankerCandidate(
            binding.CandidateToken,
            template.Kind,
            binding.Candidate.PrimaryEvidence.Event.Severity,
            age,
            binding.Variants.Select(item => item.VariantToken).ToArray(),
            effectOptions);
    }

    private async Task<string?> InvokeRankerAsync(
        AssociationRankerRequest request,
        long startedTimestamp,
        CancellationToken callerCancellation)
    {
        callerCancellation.ThrowIfCancellationRequested();
        TimeSpan elapsedBeforeCall = SafeElapsed(startedTimestamp);
        if (elapsedBeforeCall >= HardTimeout) return null;

        using var providerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        Task<string?> providerTask;
        try
        {
            providerTask = _ranker.RankAsync(request, providerCancellation.Token);
            if (providerTask is null) return null;
        }
        catch (Exception exception) when (callerCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The association decision was canceled by its caller.",
                exception,
                callerCancellation);
        }
        catch (Exception)
        {
            return null;
        }

        using var timeoutCancellation = new CancellationTokenSource();
        Task timeoutTask = Task.Delay(
            HardTimeout - elapsedBeforeCall,
            _timeProvider,
            timeoutCancellation.Token);
        Task callerTask = callerCancellation.CanBeCanceled
            ? Task.Delay(Timeout.InfiniteTimeSpan, callerCancellation)
            : Task.Delay(Timeout.InfiniteTimeSpan);
        Task completed = await Task.WhenAny(providerTask, timeoutTask, callerTask).ConfigureAwait(false);
        if (completed == callerTask)
        {
            SafeCancel(providerCancellation);
            ObserveLate(providerTask);
            callerCancellation.ThrowIfCancellationRequested();
            throw new OperationCanceledException(callerCancellation);
        }

        if (completed == timeoutTask)
        {
            SafeCancel(providerCancellation);
            ObserveLate(providerTask);
            return null;
        }

        timeoutCancellation.Cancel();
        try
        {
            string? response = await providerTask.ConfigureAwait(false);
            callerCancellation.ThrowIfCancellationRequested();
            if (SafeElapsed(startedTimestamp) >= HardTimeout)
            {
                SafeCancel(providerCancellation);
                return null;
            }

            return response;
        }
        catch (Exception exception) when (callerCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The association decision was canceled by its caller.",
                exception,
                callerCancellation);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool TryMaterialize(
        AssociationDecisionRequest request,
        CandidateBinding binding,
        AssociationRankerProposal proposal,
        CancellationToken cancellationToken,
        out StoryThreadContract? thread)
    {
        thread = null;
        ApprovedTextVariant? variant = binding.Variants.SingleOrDefault(item =>
            string.Equals(item.VariantToken, proposal.VariantToken, StringComparison.Ordinal));
        if (variant is null ||
            !string.Equals(variant.InjectionPoint, binding.Candidate.InjectionPoint, StringComparison.Ordinal) ||
            !TryMaterializeEffects(
                binding.Candidate,
                proposal.EffectSelections,
                out IReadOnlyList<StoryThreadEffectContract> effects))
        {
            return false;
        }

        var injection = new StoryThreadInjectionContract(
            request.Route.UpcomingNodeId,
            variant.InjectionPoint,
            variant.Text,
            effects);
        StoryThreadInjectionContract[] injections = { injection };
        if (!ApprovedAssociationContentValidation.IsValidThreadContent(
                binding.Candidate.Template.Template.TemplateId,
                request.PlayerId,
                binding.Candidate.ResolvedParameters,
                injections,
                variant.MediaRefs))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        thread = CreateThread(
            binding.Candidate.Template.Template.TemplateId,
            request.PlayerId,
            binding.Candidate.ResolvedParameters,
            injections,
            variant.MediaRefs,
            fallbackUsed: false,
            cancellationToken);
        return true;
    }

    private static bool TryMaterializeEffects(
        AssociationRuleCandidate candidate,
        IReadOnlyList<AssociationRankerEffectSelection> selections,
        out IReadOnlyList<StoryThreadEffectContract> effects)
    {
        effects = Array.Empty<StoryThreadEffectContract>();
        IReadOnlyList<AssociationAllowedEffectContract> allowed =
            candidate.Template.Template.AllowedEffects;
        var result = new List<StoryThreadEffectContract>(selections.Count);
        foreach (AssociationRankerEffectSelection selection in selections)
        {
            if (selection.EffectIndex < 0 || selection.EffectIndex >= allowed.Count) return false;
            AssociationAllowedEffectContract source = allowed[selection.EffectIndex];
            if (source.Op == AssociationEffectOperations.Relationship)
            {
                if (selection.RelationshipDelta is not double delta ||
                    !double.IsFinite(delta) ||
                    source.Range is not { Count: 2 } ||
                    delta < source.Range[0] ||
                    delta > source.Range[1] ||
                    source.Field is null ||
                    !candidate.ResolvedParameters.TryGetValue("target", out string? target))
                {
                    return false;
                }

                result.Add(new StoryThreadEffectContract(
                    StoryThreadEffectOperations.Relationship,
                    target,
                    source.Field,
                    delta,
                    null));
                continue;
            }

            if (selection.RelationshipDelta is not null || source.Value is null) return false;
            string? value = ResolveTemplateReference(source.Value, candidate.ResolvedParameters);
            if (!ApprovedAssociationContentValidation.IsId(value)) return false;
            string operation = source.Op == AssociationEffectOperations.Clue
                ? StoryThreadEffectOperations.Clue
                : source.Op == AssociationEffectOperations.BranchUnlock
                    ? StoryThreadEffectOperations.BranchUnlock
                    : string.Empty;
            if (operation.Length == 0) return false;
            result.Add(new StoryThreadEffectContract(operation, null, null, null, value));
        }

        effects = Array.AsReadOnly(result.ToArray());
        return true;
    }

    private StoryThreadContract CreateFallback(
        AssociationDecisionRequest request,
        CancellationToken cancellationToken,
        string? candidateFallbackThreadId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fallbackThreadId = candidateFallbackThreadId ?? request.DefaultFallbackThreadId;
        if (!_fallbacks.TryGet(fallbackThreadId, out ApprovedFallbackStoryThread? fallback) ||
            !request.Route.AllowedInjectionPoints.Contains(fallback.InjectionPoint, StringComparer.Ordinal))
        {
            throw new AssociationDecisionConfigurationException();
        }

        var injection = new StoryThreadInjectionContract(
            request.Route.UpcomingNodeId,
            fallback.InjectionPoint,
            fallback.Text,
            Array.AsReadOnly(fallback.Effects
                .Select(ApprovedAssociationContentValidation.CloneEffect)
                .ToArray()));
        StoryThreadInjectionContract[] injections = { injection };
        var emptyParameters = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));
        if (!ApprovedAssociationContentValidation.IsValidThreadContent(
                fallback.ThreadId,
                request.PlayerId,
                emptyParameters,
                injections,
                fallback.MediaRefs))
        {
            throw new AssociationDecisionConfigurationException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return CreateThread(
            fallback.ThreadId,
            request.PlayerId,
            emptyParameters,
            injections,
            fallback.MediaRefs,
            fallbackUsed: true,
            cancellationToken);
    }

    private StoryThreadContract CreateThread(
        string templateId,
        string playerId,
        IReadOnlyDictionary<string, string> resolvedParameters,
        IReadOnlyList<StoryThreadInjectionContract> injections,
        IReadOnlyList<string> mediaRefs,
        bool fallbackUsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ApprovedAssociationContentValidation.IsValidThreadContent(
                templateId,
                playerId,
                resolvedParameters,
                injections,
                mediaRefs))
        {
            throw new AssociationDecisionConfigurationException();
        }

        string threadId = _idFactory.CreateThreadId();
        string auditId = _idFactory.CreateAuditId();
        if (!ApprovedAssociationContentValidation.IsId(threadId) ||
            !threadId.StartsWith("thr.", StringComparison.Ordinal) ||
            !ApprovedAssociationContentValidation.IsId(auditId) ||
            !auditId.StartsWith("genjob.", StringComparison.Ordinal))
        {
            throw new AssociationDecisionConfigurationException();
        }

        var parameters = new ReadOnlyDictionary<string, string>(
            resolvedParameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        var result = new StoryThreadContract(
            "1.0.0",
            threadId,
            templateId,
            playerId,
            parameters,
            Array.AsReadOnly(injections.ToArray()),
            Array.AsReadOnly(mediaRefs.ToArray()),
            true,
            auditId,
            fallbackUsed);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string? ResolveTemplateReference(
        string value,
        IReadOnlyDictionary<string, string> parameters)
    {
        string resolved = value;
        foreach ((string name, string parameterValue) in parameters)
        {
            resolved = resolved.Replace(
                "{" + name + "}",
                parameterValue.StartsWith("npc.", StringComparison.Ordinal)
                    ? parameterValue[4..]
                    : parameterValue,
                StringComparison.Ordinal);
        }

        return resolved.Contains('{', StringComparison.Ordinal) ||
               resolved.Contains('}', StringComparison.Ordinal)
            ? null
            : resolved;
    }

    private static void ValidateRequest(AssociationDecisionRequest? request)
    {
        if (request is null ||
            !AssociationDecisionLedgerValidation.IsPlayerId(request.PlayerId) ||
            !AssociationDecisionLedgerValidation.IsChapterId(request.Chapter) ||
            request.WorldClock < 0 ||
            request.Route is null ||
            !ApprovedAssociationContentValidation.IsId(request.Route.UpcomingNodeId) ||
            !ApprovedAssociationContentValidation.IsId(request.Route.UpcomingLocationId) ||
            request.Route.AllowedInjectionPoints is null ||
            request.Route.AllowedInjectionPoints.Count is < 1 or > 3 ||
            request.Route.AllowedInjectionPoints.Any(point => !StoryThreadInjectionPoints.All.Contains(point)) ||
            request.Route.AllowedInjectionPoints.Distinct(StringComparer.Ordinal).Count() !=
                request.Route.AllowedInjectionPoints.Count ||
            request.ActiveFactIds is null ||
            request.ActiveFactIds.Count > AssociationDecisionLimits.MaximumActiveFactCount ||
            request.ActiveFactIds.Any(item => !ApprovedAssociationContentValidation.IsId(item)) ||
            request.ActiveFactIds.Distinct(StringComparer.Ordinal).Count() != request.ActiveFactIds.Count ||
            request.LedgerEntries is null ||
            request.LedgerEntries.Count > AssociationDecisionLimits.MaximumLedgerEntryCount ||
            request.LedgerEntries.Any(entry => !AssociationDecisionLedgerValidation.IsValidEntry(
                entry,
                request.PlayerId,
                request.Chapter)) ||
            request.TriggerHistory is null ||
            request.TriggerHistory.Count > AssociationDecisionLimits.MaximumTriggerHistoryCount ||
            request.TriggerHistory.Any(item =>
                item is null ||
                !ApprovedAssociationContentValidation.IsAssociationId(item.TemplateId) ||
                item.LastTriggeredWorldClock < 0 ||
                item.LastTriggeredWorldClock > request.WorldClock ||
                item.TriggerCount < 0) ||
            !ApprovedAssociationContentValidation.IsAssociationId(request.DefaultFallbackThreadId))
        {
            throw new AssociationDecisionValidationException();
        }
    }

    private static void ValidateTemplates(IReadOnlyList<ApprovedAssociationTemplate>? templates)
    {
        if (templates is null ||
            templates.Count > AssociationDecisionLimits.MaximumTemplateCount ||
            templates.Any(item => item?.Template is null) ||
            templates.Select(item => item.Template.TemplateId)
                .Distinct(StringComparer.Ordinal).Count() != templates.Count ||
            templates.Select(item => item.Template.TemplateId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != templates.Count)
        {
            throw new AssociationDecisionValidationException();
        }
    }

    private TimeSpan SafeElapsed(long startedTimestamp)
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(startedTimestamp);
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static void ObserveLate(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static void SafeCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (Exception)
        {
            // Provider-owned callbacks must not replace caller-cancellation or fallback semantics.
        }
    }

    private sealed record CandidateBinding(
        string CandidateToken,
        AssociationRuleCandidate Candidate,
        IReadOnlyList<ApprovedTextVariant> Variants);
}
