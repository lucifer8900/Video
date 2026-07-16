#nullable disable

using System;
using System.Collections.Generic;

namespace Lingmai.RedMist
{
    public enum StateEffectApplyResult
    {
        Applied,
        AlreadyApplied,
        Rejected
    }

    public sealed class StoryThreadEffectApplication
    {
        public StoryThreadEffectApplication(
            string receiptId,
            IReadOnlyList<StoryThreadEffect> effects)
        {
            ReceiptId = receiptId;
            Effects = effects;
        }

        public string ReceiptId { get; }
        public IReadOnlyList<StoryThreadEffect> Effects { get; }
    }

    /// <summary>
    /// Applies the complete set of effects selected for one node as one transaction.
    /// Work is performed against detached lists and becomes visible only after every
    /// application and effect has passed the bounded L2 checks.
    /// </summary>
    public sealed class StateEffectAtomicChannel
    {
        private const int MaximumApplications = 16;
        private const int MaximumPersistedEntries = 4096;
        private const double MaximumRelationshipMagnitude = 1000000d;
        private static readonly HashSet<string> RelationshipFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "trust",
                "respect",
                "fear",
                "debt",
                "suspicion",
                "affection"
            };

        public StateEffectApplyResult TryApply(
            GameState state,
            IReadOnlyList<StoryThreadEffectApplication> applications)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (applications == null || applications.Count > MaximumApplications ||
                !IsPersistedStateValid(state)) return StateEffectApplyResult.Rejected;

            var existingReceipts = new HashSet<string>(
                state.appliedStoryThreadReceipts ?? new List<string>(),
                StringComparer.Ordinal);
            var batchReceipts = new HashSet<string>(StringComparer.Ordinal);
            var pending = new List<StoryThreadEffectApplication>();

            for (int index = 0; index < applications.Count; index++)
            {
                StoryThreadEffectApplication application = applications[index];
                if (application == null ||
                    !IsSafeReceipt(application.ReceiptId) ||
                    application.Effects == null ||
                    application.Effects.Count > StoryThreadContractRules.MaximumCollectionCount ||
                    !batchReceipts.Add(application.ReceiptId))
                {
                    return StateEffectApplyResult.Rejected;
                }

                if (!existingReceipts.Contains(application.ReceiptId))
                    pending.Add(application);
            }

            if (pending.Count == 0) return StateEffectApplyResult.AlreadyApplied;

            List<StoryRelationshipState> relationships = CloneRelationships(state.storyRelationships);
            var clueIds = new List<string>(state.storyClueIds ?? new List<string>());
            var branchIds = new List<string>(state.storyBranchIds ?? new List<string>());
            var receipts = new List<string>(state.appliedStoryThreadReceipts ?? new List<string>());
            var clueSet = new HashSet<string>(clueIds, StringComparer.Ordinal);
            var branchSet = new HashSet<string>(branchIds, StringComparer.Ordinal);
            var receiptSet = new HashSet<string>(receipts, StringComparer.Ordinal);

            for (int applicationIndex = 0; applicationIndex < pending.Count; applicationIndex++)
            {
                StoryThreadEffectApplication application = pending[applicationIndex];
                for (int effectIndex = 0; effectIndex < application.Effects.Count; effectIndex++)
                {
                    StoryThreadEffect effect = application.Effects[effectIndex];
                    if (!TryApplyToDraft(effect, relationships, clueIds, clueSet, branchIds, branchSet))
                        return StateEffectApplyResult.Rejected;
                }

                if (receipts.Count >= MaximumPersistedEntries ||
                    !receiptSet.Add(application.ReceiptId))
                    return StateEffectApplyResult.Rejected;
                receipts.Add(application.ReceiptId);
            }

            // Unity gameplay state is owned by the main thread. Assigning the fully validated
            // detached collections here is the single commit boundary for this effect batch.
            state.storyRelationships = relationships;
            state.storyClueIds = clueIds;
            state.storyBranchIds = branchIds;
            state.appliedStoryThreadReceipts = receipts;
            return StateEffectApplyResult.Applied;
        }

        private static bool TryApplyToDraft(
            StoryThreadEffect effect,
            List<StoryRelationshipState> relationships,
            List<string> clueIds,
            HashSet<string> clueSet,
            List<string> branchIds,
            HashSet<string> branchSet)
        {
            if (effect == null || string.IsNullOrEmpty(effect.Op)) return false;

            switch (effect.Op)
            {
                case "relationship":
                    return TryApplyRelationship(effect, relationships);
                case "clue":
                    return TryApplyIdentifierEffect(
                        effect,
                        "clue.",
                        clueIds,
                        clueSet);
                case "branch_unlock":
                    return TryApplyIdentifierEffect(
                        effect,
                        "branch.",
                        branchIds,
                        branchSet);
                default:
                    return false;
            }
        }

        private static bool TryApplyRelationship(
            StoryThreadEffect effect,
            List<StoryRelationshipState> relationships)
        {
            if (!IsIdentifier(effect.TargetRef) ||
                !RelationshipFields.Contains(effect.Field ?? string.Empty) ||
                !effect.Delta.HasValue ||
                double.IsNaN(effect.Delta.Value) ||
                double.IsInfinity(effect.Delta.Value) ||
                effect.Delta.Value < -10d ||
                effect.Delta.Value > 10d ||
                effect.Value != null)
            {
                return false;
            }

            for (int index = 0; index < relationships.Count; index++)
            {
                StoryRelationshipState current = relationships[index];
                if (current == null ||
                    !string.Equals(current.targetRef, effect.TargetRef, StringComparison.Ordinal) ||
                    !string.Equals(current.field, effect.Field, StringComparison.Ordinal))
                {
                    continue;
                }

                double next = current.value + effect.Delta.Value;
                if (double.IsNaN(next) || double.IsInfinity(next) ||
                    Math.Abs(next) > MaximumRelationshipMagnitude) return false;
                current.value = next;
                return true;
            }

            if (relationships.Count >= MaximumPersistedEntries) return false;
            relationships.Add(new StoryRelationshipState(
                effect.TargetRef,
                effect.Field,
                effect.Delta.Value));
            return true;
        }

        public static bool IsPersistedStateValid(GameState state)
        {
            if (state == null ||
                state.storyRelationships == null ||
                state.storyClueIds == null ||
                state.storyBranchIds == null ||
                state.appliedStoryThreadReceipts == null ||
                state.storyRelationships.Count > MaximumPersistedEntries ||
                state.storyClueIds.Count > MaximumPersistedEntries ||
                state.storyBranchIds.Count > MaximumPersistedEntries ||
                state.appliedStoryThreadReceipts.Count > MaximumPersistedEntries)
            {
                return false;
            }

            var relationships = new HashSet<string>(StringComparer.Ordinal);
            foreach (StoryRelationshipState relationship in state.storyRelationships)
            {
                if (relationship == null ||
                    !IsIdentifier(relationship.targetRef) ||
                    !RelationshipFields.Contains(relationship.field ?? string.Empty) ||
                    double.IsNaN(relationship.value) ||
                    double.IsInfinity(relationship.value) ||
                    Math.Abs(relationship.value) > MaximumRelationshipMagnitude ||
                    !relationships.Add(relationship.targetRef + "\u001f" + relationship.field))
                {
                    return false;
                }
            }

            if (!IsUniqueIdentifierList(state.storyClueIds, "clue.") ||
                !IsUniqueIdentifierList(state.storyBranchIds, "branch."))
            {
                return false;
            }

            var receipts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string receipt in state.appliedStoryThreadReceipts)
            {
                if (!IsSafeReceipt(receipt) || !receipts.Add(receipt)) return false;
            }
            return true;
        }

        private static bool IsUniqueIdentifierList(
            IEnumerable<string> values,
            string requiredPrefix)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (!IsIdentifier(value) ||
                    !value.StartsWith(requiredPrefix, StringComparison.Ordinal) ||
                    !unique.Add(value)) return false;
            }
            return true;
        }

        private static bool TryApplyIdentifierEffect(
            StoryThreadEffect effect,
            string requiredPrefix,
            List<string> values,
            HashSet<string> valueSet)
        {
            if (effect.TargetRef != null ||
                effect.Field != null ||
                effect.Delta.HasValue ||
                !IsIdentifier(effect.Value) ||
                !effect.Value.StartsWith(requiredPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (valueSet.Contains(effect.Value)) return true;
            if (values.Count >= MaximumPersistedEntries) return false;
            valueSet.Add(effect.Value);
            values.Add(effect.Value);
            return true;
        }

        private static List<StoryRelationshipState> CloneRelationships(
            IReadOnlyList<StoryRelationshipState> source)
        {
            var result = new List<StoryRelationshipState>();
            if (source == null) return result;

            for (int index = 0; index < source.Count; index++)
            {
                StoryRelationshipState item = source[index];
                result.Add(item == null
                    ? null
                    : new StoryRelationshipState(item.targetRef, item.field, item.value));
            }
            return result;
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 160 || !IsAsciiAlphaNumeric(value[0]))
                return false;

            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAsciiAlphaNumeric(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != ':' &&
                    character != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                   (value >= 'a' && value <= 'z') ||
                   (value >= '0' && value <= '9');
        }

        private static bool IsSafeReceipt(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsControl(character) ||
                    character == '\u00ad' ||
                    character == '\u061c' ||
                    character == '\u200e' ||
                    character == '\u200f' ||
                    (character >= '\u2028' && character <= '\u202e') ||
                    (character >= '\u2066' && character <= '\u2069') ||
                    character == '\ufeff')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
