using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Associations;

public sealed class AssociationConsistencyGuard
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly AssociationEntityRegistry _entities;
    private readonly ApprovedTextVariantRegistry _variants;
    private readonly IAssociationWorldStateProvider _worldStateProvider;
    private readonly IAssociationConsistencyReviewer _reviewer;
    private readonly AssociationConsistencyReviewOptions _reviewOptions;
    private readonly AssociationRejectionMetrics _metrics;
    private readonly IAssociationTriggerGate _triggerGate;

    public AssociationConsistencyGuard(
        AssociationEntityRegistry entities,
        ApprovedTextVariantRegistry variants,
        IAssociationWorldStateProvider worldStateProvider,
        IAssociationConsistencyReviewer reviewer,
        AssociationConsistencyReviewOptions reviewOptions,
        AssociationRejectionMetrics metrics,
        IAssociationTriggerGate triggerGate)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _variants = variants ?? throw new ArgumentNullException(nameof(variants));
        _worldStateProvider = worldStateProvider ??
            throw new ArgumentNullException(nameof(worldStateProvider));
        _reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
        _reviewOptions = reviewOptions ?? throw new ArgumentNullException(nameof(reviewOptions));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _triggerGate = triggerGate ?? throw new ArgumentNullException(nameof(triggerGate));
    }

    public bool TryCaptureWorldSnapshot(
        AssociationDecisionRequest request,
        out AssociationWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        snapshot = null!;
        try
        {
            var query = new AssociationWorldStateQuery(
                request.PlayerId,
                request.Chapter,
                request.WorldClock,
                request.Route.UpcomingNodeId,
                request.Route.UpcomingLocationId);
            if (!_worldStateProvider.TryCapture(
                    query,
                    out AssociationWorldSnapshot? captured) ||
                captured is null)
            {
                RecordRejection(AssociationConsistencyRejectionReason.StateUnavailable);
                return false;
            }

            snapshot = captured.DeepCopy();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordRejection(AssociationConsistencyRejectionReason.StateUnavailable);
            return false;
        }
    }

    public AssociationConsistencyResult Evaluate(AssociationConsistencyInput input)
    {
        if (input is null ||
            input.Request is null ||
            input.Candidate is null ||
            input.Draft is null ||
            input.World is null ||
            input.Request.Route is null ||
            input.Request.ActiveFactIds is null ||
            input.Request.LedgerEntries is null ||
            input.Request.TriggerHistory is null)
        {
            return Reject(AssociationConsistencyRejectionReason.StateUnavailable);
        }

        if (!AllReferencedEntitiesAreRegistered(input))
            return Reject(AssociationConsistencyRejectionReason.UnknownEntity);

        if (HasForbiddenFact(input))
            return Reject(AssociationConsistencyRejectionReason.ForbiddenFact);

        foreach (string targetNpcId in GetLedgerActorTargets(input.Candidate))
        {
            if (!input.World.TryGetNpc(targetNpcId, out AssociationNpcWorldState targetState) ||
                targetState.LifeState == AssociationNpcLifeState.Unknown)
            {
                return Reject(AssociationConsistencyRejectionReason.StateUnavailable);
            }

            if (targetState.LifeState == AssociationNpcLifeState.Dead)
                return Reject(AssociationConsistencyRejectionReason.TargetDead);

            if (targetState.LifeState != AssociationNpcLifeState.Alive)
                return Reject(AssociationConsistencyRejectionReason.StateUnavailable);

            if (!input.World.IsReachable(targetState.LocationId))
                return Reject(AssociationConsistencyRejectionReason.TargetUnreachable);
        }

        AssociationConsistencyRejectionReason? effectFailure = ValidateEffects(input);
        if (effectFailure is AssociationConsistencyRejectionReason effectReason)
            return Reject(effectReason);

        AssociationConsistencyRejectionReason? triggerFailure = ValidateTriggerHistory(input);
        if (triggerFailure is AssociationConsistencyRejectionReason triggerReason)
            return Reject(triggerReason);

        if (!IsExactApprovedVariant(input))
            return Reject(AssociationConsistencyRejectionReason.UnapprovedTextVariant);

        return AssociationConsistencyResult.Success;
    }

    public async Task<AssociationConsistencyResult> ReviewAsync(
        AssociationConsistencyInput input,
        long startedTimestamp,
        TimeSpan hardTimeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_reviewOptions.EnableModelReview) return AssociationConsistencyResult.Success;

        TimeSpan remaining;
        try
        {
            remaining = hardTimeout - timeProvider.GetElapsedTime(startedTimestamp);
        }
        catch (ArgumentException)
        {
            return Reject(AssociationConsistencyRejectionReason.ModelTimeout);
        }

        if (hardTimeout <= TimeSpan.Zero || remaining <= TimeSpan.Zero)
            return Reject(AssociationConsistencyRejectionReason.ModelTimeout);

        AssociationConsistencyReviewRequest request = CreateReviewRequest(input);
        using var providerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<string?> providerTask;
        try
        {
            providerTask = _reviewer.ReviewAsync(request, providerCancellation.Token);
            if (providerTask is null)
                return Reject(AssociationConsistencyRejectionReason.ModelUnavailable);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The association consistency review was canceled by its caller.",
                exception,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Reject(AssociationConsistencyRejectionReason.ModelUnavailable);
        }

        using var timeoutCancellation = new CancellationTokenSource();
        Task timeoutTask = Task.Delay(
            remaining,
            timeProvider,
            timeoutCancellation.Token);
        Task callerTask = cancellationToken.CanBeCanceled
            ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            : Task.Delay(Timeout.InfiniteTimeSpan);
        Task completed = await Task.WhenAny(
            providerTask,
            timeoutTask,
            callerTask).ConfigureAwait(false);
        if (completed == callerTask)
        {
            SafeCancel(providerCancellation);
            ObserveLate(providerTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(cancellationToken);
        }

        if (completed == timeoutTask)
        {
            SafeCancel(providerCancellation);
            ObserveLate(providerTask);
            return Reject(AssociationConsistencyRejectionReason.ModelTimeout);
        }

        timeoutCancellation.Cancel();
        string? rawResponse;
        try
        {
            rawResponse = await providerTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The association consistency review was canceled by its caller.",
                exception,
                cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException)
        {
            return Reject(AssociationConsistencyRejectionReason.ModelTimeout);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Reject(AssociationConsistencyRejectionReason.ModelUnavailable);
        }

        try
        {
            if (timeProvider.GetElapsedTime(startedTimestamp) >= hardTimeout)
            {
                return Reject(AssociationConsistencyRejectionReason.ModelTimeout);
            }
        }
        catch (ArgumentException)
        {
            return Reject(AssociationConsistencyRejectionReason.ModelTimeout);
        }

        if (!AssociationConsistencyReviewWireProtocol.TryParse(
                rawResponse,
                out AssociationConsistencyReviewResponse? response))
        {
            return Reject(AssociationConsistencyRejectionReason.ModelInvalidResponse);
        }

        return response!.ReasonCode switch
        {
            AssociationConsistencyReviewReasonCodes.None =>
                AssociationConsistencyResult.Success,
            AssociationConsistencyReviewReasonCodes.FactConflict => Reject(
                AssociationConsistencyRejectionReason.ModelFactConflict),
            AssociationConsistencyReviewReasonCodes.CharacterConflict => Reject(
                AssociationConsistencyRejectionReason.ModelCharacterConflict),
            AssociationConsistencyReviewReasonCodes.ChapterToneConflict => Reject(
                AssociationConsistencyRejectionReason.ModelChapterToneConflict),
            _ => Reject(AssociationConsistencyRejectionReason.ModelInvalidResponse),
        };
    }

    public void RecordRejection(AssociationConsistencyRejectionReason reason)
    {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        _metrics.Record(reason);
    }

    public AssociationConsistencyResult ValidateReviewContext(
        AssociationConsistencyInput reviewedInput,
        AssociationConsistencyInput signingInput)
    {
        ArgumentNullException.ThrowIfNull(reviewedInput);
        ArgumentNullException.ThrowIfNull(signingInput);
        if (!_reviewOptions.EnableModelReview)
            return AssociationConsistencyResult.Success;

        var reviewedFacts = new HashSet<string>(
            reviewedInput.Request.ActiveFactIds,
            StringComparer.Ordinal);
        reviewedFacts.UnionWith(reviewedInput.World.ActiveFactIds);
        var signingFacts = new HashSet<string>(
            signingInput.Request.ActiveFactIds,
            StringComparer.Ordinal);
        signingFacts.UnionWith(signingInput.World.ActiveFactIds);
        return reviewedFacts.SetEquals(signingFacts)
            ? AssociationConsistencyResult.Success
            : Reject(AssociationConsistencyRejectionReason.ModelReviewContextChanged);
    }

    public AssociationConsistencyResult TryReserveTrigger(
        AssociationConsistencyInput input,
        out AssociationTriggerReservation? reservation)
    {
        ArgumentNullException.ThrowIfNull(input);
        reservation = null;
        AssociationTemplateContract template = input.Candidate.Template.Template;
        long triggerCount = 0;
        long? lastTriggeredWorldClock = null;
        try
        {
            foreach (AssociationTriggerHistory history in input.Request.TriggerHistory.Where(
                         history => history is not null && string.Equals(
                             history.TemplateId,
                             template.TemplateId,
                             StringComparison.Ordinal)))
            {
                triggerCount = checked(triggerCount + history.TriggerCount);
                if (lastTriggeredWorldClock is null ||
                    history.LastTriggeredWorldClock > lastTriggeredWorldClock.Value)
                {
                    lastTriggeredWorldClock = history.LastTriggeredWorldClock;
                }
            }

            AssociationTriggerGateResult result = _triggerGate.TryReserve(
                new AssociationTriggerGateRequest(
                    input.Request.PlayerId,
                    template.TemplateId,
                    input.Request.WorldClock,
                    template.CooldownWorldClock,
                    template.MaxTriggersPerPlayer,
                    triggerCount,
                    lastTriggeredWorldClock));
            if (!result.Reserved || result.Reservation is null)
            {
                return Reject(result.Reason ??
                    AssociationConsistencyRejectionReason.StateUnavailable);
            }

            reservation = result.Reservation;
            return AssociationConsistencyResult.Success;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Reject(AssociationConsistencyRejectionReason.StateUnavailable);
        }
    }

    public bool IsApprovedFallbackSafe(
        AssociationDecisionRequest request,
        ApprovedFallbackStoryThread fallback)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fallback);
        if (request.Route is null ||
            !_entities.ContainsEntity(request.Route.UpcomingNodeId) ||
            !_entities.ContainsEntity(request.Route.UpcomingLocationId) ||
            !_entities.ContainsEntity(fallback.ThreadId) ||
            fallback.MediaRefs.Any(mediaId => !_entities.ContainsEntity(mediaId)))
        {
            return false;
        }

        foreach (StoryThreadEffectContract effect in fallback.Effects)
        {
            bool registered = effect.Op switch
            {
                StoryThreadEffectOperations.Relationship => false,
                StoryThreadEffectOperations.Clue =>
                    effect.Value is not null &&
                    _entities.ContainsClue(effect.Value),
                StoryThreadEffectOperations.BranchUnlock =>
                    effect.Value is not null &&
                    _entities.ContainsBranch(effect.Value),
                _ => false,
            };
            if (!registered) return false;
        }

        return true;
    }

    private AssociationConsistencyResult Reject(AssociationConsistencyRejectionReason reason)
    {
        RecordRejection(reason);
        return new AssociationConsistencyResult(false, reason);
    }

    private bool AllReferencedEntitiesAreRegistered(AssociationConsistencyInput input)
    {
        AssociationDecisionRequest request = input.Request;
        AssociationRuleCandidate candidate = input.Candidate;
        AssociationTemplateContract template = candidate.Template.Template;
        AssociationThreadDraft draft = input.Draft;

        if (!_entities.ContainsChapter(request.Chapter) ||
            !_entities.ContainsEntity(request.Route.UpcomingNodeId) ||
            !_entities.ContainsEntity(request.Route.UpcomingLocationId) ||
            !_entities.ContainsEntity(template.TemplateId) ||
            !string.Equals(template.TemplateId, draft.TemplateId, StringComparison.Ordinal) ||
            request.ActiveFactIds.Any(factId => !_entities.ContainsFact(factId)) ||
            template.Preconditions.ChapterWindow.Any(
                chapterId => !_entities.ContainsChapter(chapterId)) ||
            !TryResolveForbiddenFacts(input, out IReadOnlyList<string>? forbiddenFacts) ||
            forbiddenFacts.Any(factId => !_entities.ContainsFact(factId)) ||
            candidate.ResolvedParameters.Values.Any(value => !_entities.ContainsEntity(value)) ||
            draft.ResolvedParameters.Values.Any(value => !_entities.ContainsEntity(value)) ||
            !_entities.ContainsEntity(draft.Injection.NodeId) ||
            draft.MediaRefs.Any(mediaId => !_entities.ContainsEntity(mediaId)) ||
            request.TriggerHistory.Any(history =>
                history is null || !_entities.ContainsEntity(history.TemplateId)) ||
            input.World.NpcStates.Any(state =>
                !_entities.ContainsEntity(state.NpcId) ||
                !_entities.ContainsEntity(state.LocationId)) ||
            input.World.ReachableLocationIds.Any(locationId =>
                !_entities.ContainsEntity(locationId)) ||
            input.World.ActiveFactIds.Any(factId =>
                !_entities.ContainsFact(factId)))
        {
            return false;
        }

        foreach (AssociationAllowedEffectContract allowedEffect in template.AllowedEffects)
        {
            string? resolvedValue = allowedEffect.Value is null
                ? null
                : ResolveTemplateReference(
                    allowedEffect.Value,
                    candidate.ResolvedParameters);
            if (allowedEffect.Op == AssociationEffectOperations.Clue &&
                (resolvedValue is null || !_entities.ContainsClue(resolvedValue)))
            {
                return false;
            }

            if (allowedEffect.Op == AssociationEffectOperations.BranchUnlock &&
                (resolvedValue is null || !_entities.ContainsBranch(resolvedValue)))
            {
                return false;
            }
        }

        foreach (StoryThreadEffectContract effect in draft.Injection.Effects)
        {
            if (effect is null) return false;
            if (effect.Op == StoryThreadEffectOperations.Relationship &&
                (effect.TargetRef is null || !_entities.ContainsEntity(effect.TargetRef)))
            {
                return false;
            }

            if (effect.Op == StoryThreadEffectOperations.Clue &&
                (effect.Value is null || !_entities.ContainsClue(effect.Value)))
            {
                return false;
            }

            if (effect.Op == StoryThreadEffectOperations.BranchUnlock &&
                (effect.Value is null || !_entities.ContainsBranch(effect.Value)))
            {
                return false;
            }
        }

        var ledgerIds = request.LedgerEntries
            .Where(entry => entry?.Event is not null)
            .Select(entry => entry.Event.EntryId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (NarrativeLedgerEntry entry in request.LedgerEntries)
        {
            if (entry is null ||
                entry.Event is null ||
                !_entities.ContainsChapter(entry.Event.Chapter) ||
                entry.Event.Actors is null ||
                entry.Event.FactRefs is null ||
                entry.Event.Actors.Any(actorId => !_entities.ContainsEntity(actorId)) ||
                !_entities.ContainsEntity(entry.Event.SourceNodeId) ||
                entry.Event.FactRefs.Any(factId => !_entities.ContainsFact(factId)) ||
                !PayloadReferencesAreRegistered(entry.Event.Payload, ledgerIds))
            {
                return false;
            }
        }

        foreach (string targetNpcId in GetLedgerActorTargets(candidate))
        {
            if (!_entities.ContainsEntity(targetNpcId))
            {
                return false;
            }

            if (input.World.TryGetNpc(targetNpcId, out AssociationNpcWorldState state) &&
                (!_entities.ContainsEntity(state.NpcId) ||
                 !_entities.ContainsEntity(state.LocationId)))
            {
                return false;
            }
        }

        return true;
    }

    private bool PayloadReferencesAreRegistered(
        JsonElement payload,
        IReadOnlySet<string> ledgerIds)
    {
        if (payload.ValueKind != JsonValueKind.Object) return false;
        foreach (JsonProperty property in payload.EnumerateObject())
        {
            if (!property.Name.EndsWith("Ref", StringComparison.Ordinal)) continue;
            if (property.Value.ValueKind != JsonValueKind.String ||
                property.Value.GetString() is not string entityId)
            {
                return false;
            }

            bool registered = string.Equals(property.Name, "debtRef", StringComparison.Ordinal)
                ? ledgerIds.Contains(entityId)
                : _entities.ContainsEntity(entityId);
            if (!registered) return false;
        }

        return true;
    }

    private static bool HasForbiddenFact(AssociationConsistencyInput input)
    {
        if (!TryResolveForbiddenFacts(input, out IReadOnlyList<string>? forbiddenFacts))
            return true;
        var activeFacts = new HashSet<string>(input.Request.ActiveFactIds, StringComparer.Ordinal);
        activeFacts.UnionWith(input.World.ActiveFactIds);
        return forbiddenFacts.Any(activeFacts.Contains);
    }

    private static bool TryResolveForbiddenFacts(
        AssociationConsistencyInput input,
        out IReadOnlyList<string> forbiddenFacts)
    {
        var resolvedFacts = new List<string>();
        foreach (string pattern in input.Candidate.Template.Template.Preconditions.ForbidsFacts)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                forbiddenFacts = Array.Empty<string>();
                return false;
            }

            string resolved = pattern;
            foreach ((string name, string value) in input.Candidate.ResolvedParameters
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                resolved = resolved.Replace("{" + name + "}", value, StringComparison.Ordinal);
            }

            if (resolved.Contains('{', StringComparison.Ordinal) ||
                resolved.Contains('}', StringComparison.Ordinal))
            {
                forbiddenFacts = Array.Empty<string>();
                return false;
            }

            resolvedFacts.Add(resolved);
        }

        forbiddenFacts = Array.AsReadOnly(resolvedFacts.ToArray());
        return true;
    }

    private static bool TryGetTarget(
        AssociationRuleCandidate candidate,
        [NotNullWhen(true)] out string? targetNpcId)
    {
        if (candidate.ResolvedParameters.TryGetValue("target", out string? value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            targetNpcId = value;
            return true;
        }

        targetNpcId = null;
        return false;
    }

    private static IReadOnlyList<string> GetLedgerActorTargets(
        AssociationRuleCandidate candidate) =>
        Array.AsReadOnly(candidate.Template.Template.ParameterSlots
            .Where(slot =>
                slot.Value?.Source is string source &&
                source.StartsWith("ledger.actors[", StringComparison.Ordinal))
            .OrderBy(slot => slot.Key, StringComparer.Ordinal)
            .Select(slot => candidate.ResolvedParameters.TryGetValue(
                slot.Key,
                out string? value)
                ? value
                : null)
            .Where(value =>
                value is not null &&
                value.StartsWith("npc.", StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray());

    private static AssociationConsistencyRejectionReason? ValidateEffects(
        AssociationConsistencyInput input)
    {
        IReadOnlyList<AssociationAllowedEffectContract> allowedEffects =
            input.Candidate.Template.Template.AllowedEffects;
        var consumed = new bool[allowedEffects.Count];
        foreach (StoryThreadEffectContract effect in input.Draft.Injection.Effects)
        {
            bool authorized = false;
            bool structuralMatch = false;
            bool rangeFailure = false;
            for (int index = 0; index < allowedEffects.Count; index++)
            {
                if (consumed[index]) continue;
                AssociationAllowedEffectContract allowed = allowedEffects[index];
                if (!string.Equals(effect.Op, allowed.Op, StringComparison.Ordinal)) continue;

                if (effect.Op == StoryThreadEffectOperations.Relationship)
                {
                    if (!TryGetTarget(input.Candidate, out string? targetNpcId) ||
                        !string.Equals(effect.TargetRef, targetNpcId, StringComparison.Ordinal) ||
                        !string.Equals(effect.Field, allowed.Field, StringComparison.Ordinal) ||
                        effect.Value is not null ||
                        effect.Delta is not double delta ||
                        !double.IsFinite(delta))
                    {
                        continue;
                    }

                    structuralMatch = true;
                    if (allowed.Range is not { Count: 2 } ||
                        !double.IsFinite(allowed.Range[0]) ||
                        !double.IsFinite(allowed.Range[1]) ||
                        allowed.Range[0] > allowed.Range[1] ||
                        delta < allowed.Range[0] ||
                        delta > allowed.Range[1])
                    {
                        rangeFailure = true;
                        continue;
                    }
                }
                else if (effect.Op is StoryThreadEffectOperations.Clue or
                         StoryThreadEffectOperations.BranchUnlock)
                {
                    string? resolvedValue = allowed.Value is null
                        ? null
                        : ResolveTemplateReference(
                            allowed.Value,
                            input.Candidate.ResolvedParameters);
                    if (effect.TargetRef is not null ||
                        effect.Field is not null ||
                        effect.Delta is not null ||
                        !string.Equals(effect.Value, resolvedValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    structuralMatch = true;
                }
                else
                {
                    continue;
                }

                consumed[index] = true;
                rangeFailure = false;
                structuralMatch = true;
                authorized = true;
                break;
            }

            if (rangeFailure)
                return AssociationConsistencyRejectionReason.EffectOutOfRange;
            if (!structuralMatch || !authorized)
            {
                return AssociationConsistencyRejectionReason.EffectNotAllowed;
            }
        }

        return null;
    }

    private static AssociationConsistencyRejectionReason? ValidateTriggerHistory(
        AssociationConsistencyInput input)
    {
        AssociationTemplateContract template = input.Candidate.Template.Template;
        if (template.CooldownWorldClock < 0 || input.Request.WorldClock < 0)
            return AssociationConsistencyRejectionReason.CooldownActive;

        AssociationTriggerHistory[] histories = input.Request.TriggerHistory
            .Where(history => history is not null && string.Equals(
                history.TemplateId,
                template.TemplateId,
                StringComparison.Ordinal))
            .ToArray();
        foreach (AssociationTriggerHistory history in histories)
        {
            if (history.LastTriggeredWorldClock < 0 ||
                history.LastTriggeredWorldClock > input.Request.WorldClock ||
                input.Request.WorldClock - history.LastTriggeredWorldClock <
                    template.CooldownWorldClock)
            {
                return AssociationConsistencyRejectionReason.CooldownActive;
            }
        }

        if (template.MaxTriggersPerPlayer <= 0)
            return AssociationConsistencyRejectionReason.TriggerLimitReached;

        long triggerCount = 0;
        foreach (AssociationTriggerHistory history in histories)
        {
            if (history.TriggerCount < 0)
                return AssociationConsistencyRejectionReason.TriggerLimitReached;
            try
            {
                triggerCount = checked(triggerCount + history.TriggerCount);
            }
            catch (OverflowException)
            {
                return AssociationConsistencyRejectionReason.TriggerLimitReached;
            }
        }

        return triggerCount >= template.MaxTriggersPerPlayer
            ? AssociationConsistencyRejectionReason.TriggerLimitReached
            : null;
    }

    private bool IsExactApprovedVariant(AssociationConsistencyInput input)
    {
        AssociationThreadDraft draft = input.Draft;
        if (!_variants.TryGet(draft.TemplateId, draft.VariantToken, out ApprovedTextVariant? variant) ||
            !string.Equals(
                draft.TemplateId,
                input.Candidate.Template.Template.TemplateId,
                StringComparison.Ordinal) ||
            !string.Equals(draft.Injection.NodeId, input.Request.Route.UpcomingNodeId, StringComparison.Ordinal) ||
            !string.Equals(draft.Injection.Point, input.Candidate.InjectionPoint, StringComparison.Ordinal) ||
            !string.Equals(draft.Injection.Point, variant.InjectionPoint, StringComparison.Ordinal) ||
            !ExactUtf8(draft.Injection.Text, variant.Text) ||
            !SequenceEqualOrdinal(draft.MediaRefs, variant.MediaRefs) ||
            !DictionaryEqualOrdinal(draft.ResolvedParameters, input.Candidate.ResolvedParameters))
        {
            return false;
        }

        return true;
    }

    private static bool ExactUtf8(string left, string right)
    {
        if (left is null || right is null) return false;
        try
        {
            return StrictUtf8.GetBytes(left).AsSpan()
                .SequenceEqual(StrictUtf8.GetBytes(right));
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool SequenceEqualOrdinal(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(pair => string.Equals(
            pair.First,
            pair.Second,
            StringComparison.Ordinal));

    private static bool DictionaryEqualOrdinal(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(item => right.TryGetValue(item.Key, out string? value) &&
                         string.Equals(item.Value, value, StringComparison.Ordinal));

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

    private static AssociationConsistencyReviewRequest CreateReviewRequest(
        AssociationConsistencyInput input)
    {
        TryGetTarget(input.Candidate, out string? targetNpcId);
        if (targetNpcId is not null &&
            !targetNpcId.StartsWith("npc.", StringComparison.Ordinal))
        {
            targetNpcId = null;
        }

        targetNpcId ??= GetLedgerActorTargets(input.Candidate).FirstOrDefault();
        return new AssociationConsistencyReviewRequest(
            input.Request.Chapter,
            input.Candidate.Template.Template.TemplateId,
            targetNpcId,
            input.Draft.Injection.Text,
            Array.AsReadOnly(input.Request.ActiveFactIds
                .Concat(input.World.ActiveFactIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()));
    }

    private static void ObserveLate(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static void SafeCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (Exception)
        {
            // Provider callbacks cannot replace deterministic timeout or cancellation semantics.
        }
    }
}
