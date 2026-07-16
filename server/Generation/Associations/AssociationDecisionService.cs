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
    internal static readonly TimeSpan AuditPersistenceTimeout = TimeSpan.FromSeconds(1);

    private readonly AssociationRuleFilter _ruleFilter;
    private readonly IAssociationRanker _ranker;
    private readonly ApprovedTextVariantRegistry _variants;
    private readonly ApprovedFallbackStoryThreadCatalog _fallbacks;
    private readonly AssociationConsistencyGuard _consistencyGuard;
    private readonly IAssociationDecisionIdFactory _idFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IAssociationDecisionAuditRepository _auditRepository;
    private readonly AssociationDecisionMetrics _metrics;
    private readonly AssociationProviderAuditProfile _rankerAuditProfile;
    private readonly AssociationProviderAuditProfile? _reviewerAuditProfile;
    private readonly bool _reviewerEnabled;

    internal AssociationDecisionService(
        AssociationRuleFilter ruleFilter,
        IAssociationRanker ranker,
        ApprovedTextVariantRegistry variants,
        ApprovedFallbackStoryThreadCatalog fallbacks,
        AssociationConsistencyGuard consistencyGuard,
        IAssociationDecisionIdFactory idFactory,
        TimeProvider timeProvider,
        IAssociationDecisionAuditRepository auditRepository,
        AssociationDecisionMetrics metrics)
    {
        _ruleFilter = ruleFilter ?? throw new ArgumentNullException(nameof(ruleFilter));
        _ranker = ranker ?? throw new ArgumentNullException(nameof(ranker));
        _variants = variants ?? throw new ArgumentNullException(nameof(variants));
        _fallbacks = fallbacks ?? throw new ArgumentNullException(nameof(fallbacks));
        _consistencyGuard = consistencyGuard ?? throw new ArgumentNullException(nameof(consistencyGuard));
        _idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _rankerAuditProfile = _ranker.AuditProfile ??
            throw new ArgumentException("The ranker audit profile is required.", nameof(ranker));
        if (!string.Equals(_rankerAuditProfile.Role, "ranker", StringComparison.Ordinal))
            throw new ArgumentException("The ranker audit profile role is invalid.", nameof(ranker));
        _reviewerEnabled = _consistencyGuard.ModelReviewEnabled;
        _reviewerAuditProfile = _reviewerEnabled
            ? _consistencyGuard.ReviewerAuditProfile
            : null;
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
        string inputHash = AssociationAuditHash.ComputeInputHash(request, templates);
        AssociationProviderAuditProfile rankerAuditProfile = _rankerAuditProfile;
        bool reviewerEnabled = _reviewerEnabled;
        AssociationProviderAuditProfile? reviewerAuditProfile = _reviewerAuditProfile;
        var trace = new AssociationDecisionTrace();

        async Task<StoryThreadContract> PersistFallbackAsync(
            string outcomeReason,
            string? fallbackThreadId = null)
        {
            trace.SetFallback(outcomeReason);
            StoryThreadContract fallback = CreateFallback(
                request,
                cancellationToken,
                fallbackThreadId);
            return await PersistDecisionAsync(
                fallback,
                request,
                inputHash,
                trace,
                rankerAuditProfile,
                reviewerAuditProfile,
                reviewerEnabled,
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<AssociationRuleCandidate> filtered = _ruleFilter.Filter(
            request,
            templates,
            reason =>
            {
                _consistencyGuard.RecordRejection(reason);
                trace.RecordRejection("prefilter", reason);
            });
        var eligibleBindings = new List<CandidateBinding>(filtered.Count);
        foreach (AssociationRuleCandidate candidate in filtered.OrderBy(
                     item => item.Template.Template.TemplateId,
                     StringComparer.Ordinal))
        {
            CandidateBinding? binding = CreateBinding(candidate);
            if (binding is null)
            {
                _consistencyGuard.RecordRejection(
                    AssociationConsistencyRejectionReason.UnapprovedTextVariant);
                trace.RecordRejection(
                    "selection",
                    AssociationConsistencyRejectionReason.UnapprovedTextVariant);
                continue;
            }

            eligibleBindings.Add(binding);
        }

        CandidateBinding[] bindings = eligibleBindings
            .Take(AssociationRankerWireProtocol.MaximumProposalCount)
            .Select((item, index) => item with { CandidateToken = $"cand.{index:D4}" })
            .ToArray();
        trace.CandidateCount = bindings.Length;
        if (bindings.Length == 0)
        {
            trace.SetFallback("no_candidates");
            trace.Record("selection", "rejected", "no_candidates");
            StoryThreadContract fallback = CreateFallback(request, cancellationToken);
            return await PersistDecisionAsync(
                fallback,
                request,
                inputHash,
                trace,
                rankerAuditProfile,
                reviewerAuditProfile,
                reviewerEnabled,
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_consistencyGuard.TryCaptureWorldSnapshot(
                request,
                out AssociationWorldSnapshot worldSnapshot))
        {
            trace.SetFallback("state_unavailable");
            trace.Record("deterministic", "rejected", "state_unavailable");
            StoryThreadContract fallback = CreateFallback(request, cancellationToken);
            return await PersistDecisionAsync(
                fallback,
                request,
                inputHash,
                trace,
                rankerAuditProfile,
                reviewerAuditProfile,
                reviewerEnabled,
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
        }

        var rankerRequest = new AssociationRankerRequest(Array.AsReadOnly(bindings
            .Select(binding => ToRankerCandidate(binding, request.WorldClock))
            .ToArray()));
        AssociationRankerInvocation rankerInvocation = await InvokeRankerAsync(
            rankerRequest,
            startedTimestamp,
            () => trace.RankerInvoked = true,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (rankerInvocation.FailureReason is string rankerFailure)
        {
            trace.SetFallback(rankerFailure);
            trace.Record("selection", "rejected", rankerFailure);
            return await PersistFallbackAsync(rankerFailure).ConfigureAwait(false);
        }

        string? rawResponse = rankerInvocation.Response;
        if (!AssociationRankerWireProtocol.TryParse(rawResponse, out IReadOnlyList<AssociationRankerProposal> proposals))
        {
            trace.SetFallback("ranker_invalid_response");
            trace.Record("selection", "rejected", "ranker_invalid_response");
            StoryThreadContract fallback = CreateFallback(request, cancellationToken);
            return await PersistDecisionAsync(
                fallback,
                request,
                inputHash,
                trace,
                rankerAuditProfile,
                reviewerAuditProfile,
                reviewerEnabled,
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
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
            if (!TryMaterializeDraft(
                    request,
                    binding,
                    proposal,
                    cancellationToken,
                    out AssociationThreadDraft? draft,
                    out AssociationConsistencyRejectionReason? materializationRejection))
            {
                if (materializationRejection.HasValue)
                {
                    _consistencyGuard.RecordRejection(materializationRejection.Value);
                    trace.RecordRejection("selection", materializationRejection.Value);
                }

                continue;
            }

            var consistencyInput = new AssociationConsistencyInput(
                request,
                binding.Candidate,
                draft!,
                worldSnapshot);
            AssociationConsistencyResult deterministic =
                _consistencyGuard.Evaluate(consistencyInput);
            if (!deterministic.Passed)
            {
                trace.RecordRejection(
                    "deterministic",
                    deterministic.Reason ?? AssociationConsistencyRejectionReason.StateUnavailable,
                    worldSnapshot.SnapshotId);
                continue;
            }
            trace.Record("deterministic", "passed", "none", worldSnapshot.SnapshotId);

            AssociationConsistencyResult review = await _consistencyGuard.ReviewAsync(
                consistencyInput,
                startedTimestamp,
                HardTimeout,
                _timeProvider,
                cancellationToken,
                () => trace.ReviewerInvoked = true).ConfigureAwait(false);
            if (!review.Passed)
            {
                trace.RecordRejection(
                    "review",
                    review.Reason ?? AssociationConsistencyRejectionReason.ModelUnavailable,
                    worldSnapshot.SnapshotId);
                return await PersistFallbackAsync(
                    "reviewer_rejected",
                    candidateFallbackThreadId).ConfigureAwait(false);
            }
            trace.Record(
                "review",
                reviewerEnabled ? "passed" : "not_run",
                reviewerEnabled ? "none" : "disabled",
                worldSnapshot.SnapshotId);

            if (!_consistencyGuard.TryCaptureWorldSnapshot(
                    request,
                    out AssociationWorldSnapshot signingSnapshot))
            {
                trace.RecordRejection(
                    "signing",
                    AssociationConsistencyRejectionReason.StateUnavailable);
                return await PersistFallbackAsync(
                    "guard_rejected",
                    candidateFallbackThreadId).ConfigureAwait(false);
            }

            AssociationConsistencyInput signingInput =
                consistencyInput with { World = signingSnapshot };
            AssociationConsistencyResult signingCheck =
                _consistencyGuard.Evaluate(signingInput);
            if (!signingCheck.Passed)
            {
                trace.RecordRejection(
                    "signing",
                    signingCheck.Reason ?? AssociationConsistencyRejectionReason.StateUnavailable,
                    signingSnapshot.SnapshotId);
                return await PersistFallbackAsync(
                    "guard_rejected",
                    candidateFallbackThreadId).ConfigureAwait(false);
            }
            trace.Record("signing", "passed", "none", signingSnapshot.SnapshotId);

            AssociationConsistencyResult reviewContext =
                _consistencyGuard.ValidateReviewContext(
                    consistencyInput,
                    signingInput);
            if (!reviewContext.Passed)
            {
                trace.RecordRejection(
                    "review",
                    reviewContext.Reason ?? AssociationConsistencyRejectionReason.ModelReviewContextChanged,
                    signingSnapshot.SnapshotId);
                return await PersistFallbackAsync(
                    "reviewer_rejected",
                    candidateFallbackThreadId).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (SafeElapsed(startedTimestamp) >= HardTimeout)
            {
                _consistencyGuard.RecordRejection(
                    AssociationConsistencyRejectionReason.ModelTimeout);
                trace.Record("selection", "rejected", "deadline_exceeded");
                return await PersistFallbackAsync(
                    "deadline_exceeded",
                    candidateFallbackThreadId).ConfigureAwait(false);
            }

            AssociationConsistencyResult triggerReservation =
                _consistencyGuard.TryReserveTrigger(
                    signingInput,
                    out AssociationTriggerReservation? reservation);
            if (!triggerReservation.Passed)
            {
                trace.RecordRejection(
                    "trigger_reservation",
                    triggerReservation.Reason ?? AssociationConsistencyRejectionReason.StateUnavailable,
                    signingSnapshot.SnapshotId);
                return await PersistFallbackAsync(
                    "guard_rejected",
                    candidateFallbackThreadId).ConfigureAwait(false);
            }
            trace.Record(
                "trigger_reservation",
                "passed",
                "none",
                signingSnapshot.SnapshotId);

            AssociationTriggerReservation activeReservation = reservation!;
            using (activeReservation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SafeElapsed(startedTimestamp) >= HardTimeout)
                {
                    _consistencyGuard.RecordRejection(
                        AssociationConsistencyRejectionReason.ModelTimeout);
                    trace.Record("selection", "rejected", "deadline_exceeded");
                    return await PersistFallbackAsync(
                        "deadline_exceeded",
                        candidateFallbackThreadId).ConfigureAwait(false);
                }

                StoryThreadContract thread = CreateThread(
                    draft!.TemplateId,
                    request.PlayerId,
                    draft.ResolvedParameters,
                    new[] { draft.Injection },
                    draft.MediaRefs,
                    fallbackUsed: false,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (SafeElapsed(startedTimestamp) >= HardTimeout)
                {
                    _consistencyGuard.RecordRejection(
                        AssociationConsistencyRejectionReason.ModelTimeout);
                    trace.Record("selection", "rejected", "deadline_exceeded");
                    return await PersistFallbackAsync(
                        "deadline_exceeded",
                        candidateFallbackThreadId).ConfigureAwait(false);
                }

                activeReservation.Commit();
                StoryThreadContract auditedThread = await PersistDecisionAsync(
                    thread,
                    request,
                    inputHash,
                    trace,
                    rankerAuditProfile,
                    reviewerAuditProfile,
                    reviewerEnabled,
                    startedTimestamp,
                    cancellationToken).ConfigureAwait(false);
                return auditedThread;
            }
        }

        trace.Record("selection", "rejected", "no_valid_proposal");
        return await PersistFallbackAsync(
            "no_valid_proposal",
            candidateFallbackThreadId).ConfigureAwait(false);
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

    private async Task<AssociationRankerInvocation> InvokeRankerAsync(
        AssociationRankerRequest request,
        long startedTimestamp,
        Action providerInvoked,
        CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(providerInvoked);
        callerCancellation.ThrowIfCancellationRequested();
        TimeSpan elapsedBeforeCall = SafeElapsed(startedTimestamp);
        if (elapsedBeforeCall >= HardTimeout)
            return new AssociationRankerInvocation(null, "ranker_timeout");

        using var providerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        Task<string?> providerTask;
        try
        {
            providerInvoked();
            providerTask = _ranker.RankAsync(request, providerCancellation.Token);
            if (providerTask is null)
                return new AssociationRankerInvocation(null, "ranker_unavailable");
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
            return new AssociationRankerInvocation(null, "ranker_unavailable");
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
            return new AssociationRankerInvocation(null, "ranker_timeout");
        }

        timeoutCancellation.Cancel();
        try
        {
            string? response = await providerTask.ConfigureAwait(false);
            callerCancellation.ThrowIfCancellationRequested();
            if (SafeElapsed(startedTimestamp) >= HardTimeout)
            {
                SafeCancel(providerCancellation);
                return new AssociationRankerInvocation(null, "ranker_timeout");
            }

            return response is null
                ? new AssociationRankerInvocation(null, "ranker_unavailable")
                : new AssociationRankerInvocation(response, null);
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
            return new AssociationRankerInvocation(null, "ranker_unavailable");
        }
    }

    private bool TryMaterializeDraft(
        AssociationDecisionRequest request,
        CandidateBinding binding,
        AssociationRankerProposal proposal,
        CancellationToken cancellationToken,
        out AssociationThreadDraft? draft,
        out AssociationConsistencyRejectionReason? rejectionReason)
    {
        draft = null;
        rejectionReason = null;
        ApprovedTextVariant? variant = binding.Variants.SingleOrDefault(item =>
            string.Equals(item.VariantToken, proposal.VariantToken, StringComparison.Ordinal));
        if (variant is null ||
            !string.Equals(
                variant.InjectionPoint,
                binding.Candidate.InjectionPoint,
                StringComparison.Ordinal))
        {
            rejectionReason = AssociationConsistencyRejectionReason.UnapprovedTextVariant;
            return false;
        }

        if (!TryMaterializeEffects(
                binding.Candidate,
                proposal.EffectSelections,
                out IReadOnlyList<StoryThreadEffectContract> effects,
                out rejectionReason))
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
            rejectionReason = AssociationConsistencyRejectionReason.UnapprovedTextVariant;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        draft = new AssociationThreadDraft(
            binding.Candidate.Template.Template.TemplateId,
            variant.VariantToken,
            binding.Candidate.ResolvedParameters,
            injection,
            variant.MediaRefs);
        return true;
    }

    private static bool TryMaterializeEffects(
        AssociationRuleCandidate candidate,
        IReadOnlyList<AssociationRankerEffectSelection> selections,
        out IReadOnlyList<StoryThreadEffectContract> effects,
        out AssociationConsistencyRejectionReason? rejectionReason)
    {
        effects = Array.Empty<StoryThreadEffectContract>();
        rejectionReason = null;
        IReadOnlyList<AssociationAllowedEffectContract> allowed =
            candidate.Template.Template.AllowedEffects;
        var result = new List<StoryThreadEffectContract>(selections.Count);
        foreach (AssociationRankerEffectSelection selection in selections)
        {
            if (selection.EffectIndex < 0 || selection.EffectIndex >= allowed.Count)
            {
                rejectionReason = AssociationConsistencyRejectionReason.EffectNotAllowed;
                return false;
            }

            AssociationAllowedEffectContract source = allowed[selection.EffectIndex];
            if (source.Op == AssociationEffectOperations.Relationship)
            {
                if (selection.RelationshipDelta is not double delta ||
                    !double.IsFinite(delta) ||
                    source.Range is not { Count: 2 } ||
                    source.Field is null ||
                    !candidate.ResolvedParameters.TryGetValue("target", out string? target))
                {
                    rejectionReason = AssociationConsistencyRejectionReason.EffectNotAllowed;
                    return false;
                }

                if (delta < source.Range[0] || delta > source.Range[1])
                {
                    rejectionReason = AssociationConsistencyRejectionReason.EffectOutOfRange;
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

            if (selection.RelationshipDelta is not null || source.Value is null)
            {
                rejectionReason = AssociationConsistencyRejectionReason.EffectNotAllowed;
                return false;
            }

            string? value = ResolveTemplateReference(source.Value, candidate.ResolvedParameters);
            if (!ApprovedAssociationContentValidation.IsId(value))
            {
                rejectionReason = AssociationConsistencyRejectionReason.EffectNotAllowed;
                return false;
            }

            string operation = source.Op == AssociationEffectOperations.Clue
                ? StoryThreadEffectOperations.Clue
                : source.Op == AssociationEffectOperations.BranchUnlock
                    ? StoryThreadEffectOperations.BranchUnlock
                    : string.Empty;
            if (operation.Length == 0)
            {
                rejectionReason = AssociationConsistencyRejectionReason.EffectNotAllowed;
                return false;
            }

            result.Add(new StoryThreadEffectContract(operation, null, null, null, value));
        }

        effects = Array.AsReadOnly(result.ToArray());
        return true;
    }

    private async Task<StoryThreadContract> PersistDecisionAsync(
        StoryThreadContract thread,
        AssociationDecisionRequest request,
        string inputHash,
        AssociationDecisionTrace trace,
        AssociationProviderAuditProfile rankerAuditProfile,
        AssociationProviderAuditProfile? reviewerAuditProfile,
        bool reviewerEnabled,
        long startedTimestamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TimeSpan elapsed = SafeElapsed(startedTimestamp);
        AssociationDecisionAuditContract audit = AssociationDecisionAuditFactory.Create(
            thread,
            request,
            inputHash,
            trace,
            rankerAuditProfile,
            reviewerAuditProfile,
            reviewerEnabled,
            elapsed,
            _timeProvider.GetUtcNow());
        // Ranking and signing own the eight-second gameplay deadline. A fallback selected at
        // that exact boundary must still be traceable, so persistence has its own short,
        // fail-closed budget instead of being allowed to block without limit.
        using var persistenceDeadline = new CancellationTokenSource(
            AuditPersistenceTimeout,
            _timeProvider);
        try
        {
            await _auditRepository.StoreAsync(audit, persistenceDeadline.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new AssociationDecisionAuditException(exception);
        }

        TimeSpan completedElapsed = SafeElapsed(startedTimestamp);
        _metrics.RecordCompleted(
            trace.CandidateCount,
            thread.FallbackUsed,
            completedElapsed,
            trace.Rejections);
        return thread;
    }

    private StoryThreadContract CreateFallback(
        AssociationDecisionRequest request,
        CancellationToken cancellationToken,
        string? candidateFallbackThreadId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fallbackThreadId = candidateFallbackThreadId ?? request.DefaultFallbackThreadId;
        if (!_fallbacks.TryGet(fallbackThreadId, out ApprovedFallbackStoryThread? fallback) ||
            !request.Route.AllowedInjectionPoints.Contains(fallback.InjectionPoint, StringComparer.Ordinal) ||
            !_consistencyGuard.IsApprovedFallbackSafe(request, fallback))
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

    private sealed record AssociationRankerInvocation(
        string? Response,
        string? FailureReason);
}
