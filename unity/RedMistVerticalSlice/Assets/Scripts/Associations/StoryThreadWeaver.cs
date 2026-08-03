#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lingmai.RedMist
{
    public sealed class StoryThreadPresentation
    {
        public StoryThreadPresentation(string narrative, bool injected)
        {
            Narrative = narrative;
            Injected = injected;
        }

        public string Narrative { get; }
        public bool Injected { get; }
    }

    /// <summary>
    /// Produces the text shown for one node and consumes every accepted injection through
    /// the atomic StateEffect channel. With no eligible thread this is a pure pass-through
    /// to the M1 node text.
    /// </summary>
    public sealed class StoryThreadWeaver
    {
        private static readonly string[] CanonicalPoints =
        {
            "node_intro",
            "travel_event",
            "npc_mention"
        };

        private readonly StateEffectAtomicChannel _effects;

        public StoryThreadWeaver(StateEffectAtomicChannel effects)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        public StoryThreadPresentation Present(
            StoryNode node,
            PlayerRoute route,
            GameState state,
            StoryThread thread)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (state == null) throw new ArgumentNullException(nameof(state));

            string baseline = node.TextFor(route);
            if (thread == null || thread.Injections == null || thread.Injections.Count == 0)
                return new StoryThreadPresentation(baseline, false);

            var declaredPoints = new HashSet<string>(
                node.injectionPoints ?? new List<string>(),
                StringComparer.Ordinal);
            if (declaredPoints.Count == 0)
                return new StoryThreadPresentation(baseline, false);

            var consumed = new HashSet<string>(
                state.appliedStoryThreadReceipts ?? new List<string>(),
                StringComparer.Ordinal);
            var ordered = new List<PendingInjection>();

            for (int pointIndex = 0; pointIndex < CanonicalPoints.Length; pointIndex++)
            {
                string point = CanonicalPoints[pointIndex];
                if (!declaredPoints.Contains(point)) continue;

                for (int injectionIndex = 0; injectionIndex < thread.Injections.Count; injectionIndex++)
                {
                    StoryThreadInjection injection = thread.Injections[injectionIndex];
                    if (injection == null ||
                        !string.Equals(injection.NodeId, node.id, StringComparison.Ordinal) ||
                        !string.Equals(injection.Point, point, StringComparison.Ordinal) ||
                        !IsRenderableText(injection.Text))
                    {
                        continue;
                    }

                    string receipt = BuildReceipt(
                        thread.ThreadId,
                        node.id,
                        point,
                        injectionIndex);
                    if (!consumed.Contains(receipt))
                        ordered.Add(new PendingInjection(injection, receipt));
                }
            }

            if (ordered.Count == 0)
                return new StoryThreadPresentation(baseline, false);

            var applications = new List<StoryThreadEffectApplication>(ordered.Count);
            for (int index = 0; index < ordered.Count; index++)
            {
                PendingInjection pending = ordered[index];
                applications.Add(new StoryThreadEffectApplication(
                    pending.Receipt,
                    pending.Injection.Effects));
            }

            if (_effects.TryApply(state, applications) != StateEffectApplyResult.Applied)
                return new StoryThreadPresentation(baseline, false);

            var narrativeParts = new List<string>(ordered.Count + 1);
            AppendPointText(narrativeParts, ordered, "node_intro");
            narrativeParts.Add(baseline ?? string.Empty);
            AppendPointText(narrativeParts, ordered, "travel_event");
            AppendPointText(narrativeParts, ordered, "npc_mention");
            return new StoryThreadPresentation(
                string.Join("\n\n", narrativeParts),
                true);
        }

        private static void AppendPointText(
            List<string> output,
            IReadOnlyList<PendingInjection> injections,
            string point)
        {
            for (int index = 0; index < injections.Count; index++)
            {
                PendingInjection pending = injections[index];
                if (string.Equals(pending.Injection.Point, point, StringComparison.Ordinal))
                    output.Add(pending.Injection.Text);
            }
        }

        private static string BuildReceipt(
            string threadId,
            string nodeId,
            string point,
            int injectionIndex)
        {
            return (threadId ?? string.Empty) + "/" +
                   (nodeId ?? string.Empty) + "/" +
                   point + "/" +
                   injectionIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsRenderableText(string value)
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

        private sealed class PendingInjection
        {
            public PendingInjection(StoryThreadInjection injection, string receipt)
            {
                Injection = injection;
                Receipt = receipt;
            }

            public StoryThreadInjection Injection { get; }
            public string Receipt { get; }
        }
    }
}
