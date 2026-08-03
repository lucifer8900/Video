using System.Collections.Generic;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class StoryThreadWeaverTests
    {
        private static readonly StoryBundleIdentity Identity = new StoryBundleIdentity(
            "chapter.red_mist",
            "1.0.0",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        [Test]
        public void NoThreadIsPurePassThroughForNarrativeChoicesAndCurrentSaveState()
        {
            StoryNode node = NodeWithSlots();
            node.choices.Add(new ChoiceDefinition("choice", "act", "hint", "next"));
            node.transitions.Add(new StoryTransitionDefinition("trigger", "next"));
            var state = new GameState { route = PlayerRoute.ShenYan, currentNodeId = "prologue" };
            string before = SaveCodec.Encode(state, Identity, "2026-07-16T00:00:00.0000000Z");
            var weaver = new StoryThreadWeaver(new StateEffectAtomicChannel());

            StoryThreadPresentation presentation = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                null);
            string after = SaveCodec.Encode(state, Identity, "2026-07-16T00:00:00.0000000Z");

            Assert.AreEqual("BASE", presentation.Narrative);
            Assert.AreEqual(before, after);
            Assert.AreEqual("act", node.choices[0].action);
            Assert.AreEqual("next", node.choices[0].nextNodeId);
            Assert.AreEqual("trigger", node.transitions[0].triggerId);
            Assert.IsFalse(presentation.Injected);
        }

        [Test]
        public void ThreeSlotsRenderInCanonicalOrderAndAreConsumedOnlyOnce()
        {
            StoryNode node = NodeWithSlots();
            var state = new GameState();
            var weaver = new StoryThreadWeaver(new StateEffectAtomicChannel());
            StoryThread thread = Thread(
                Injection("npc_mention", "NPC"),
                Injection("travel_event", "TRAVEL"),
                Injection("node_intro", "INTRO"));

            StoryThreadPresentation first = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                thread);
            StoryThreadPresentation second = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                thread);

            Assert.AreEqual("INTRO\n\nBASE\n\nTRAVEL\n\nNPC", first.Narrative);
            Assert.IsTrue(first.Injected);
            Assert.AreEqual(3, state.appliedStoryThreadReceipts.Count);
            Assert.AreEqual("BASE", second.Narrative);
            Assert.IsFalse(second.Injected);
        }

        [Test]
        public void InjectionForUndeclaredSlotNeverRendersOrAppliesEffects()
        {
            StoryNode node = NodeWithSlots();
            node.injectionPoints.Remove("npc_mention");
            var state = new GameState();
            var weaver = new StoryThreadWeaver(new StateEffectAtomicChannel());
            StoryThread thread = Thread(new StoryThreadInjection(
                "prologue",
                "npc_mention",
                "SHOULD_NOT_RENDER",
                new[] { new StoryThreadEffect("clue", null, null, null, "clue.no") }));

            StoryThreadPresentation result = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                thread);

            Assert.AreEqual("BASE", result.Narrative);
            Assert.IsEmpty(state.storyClueIds);
            Assert.IsEmpty(state.appliedStoryThreadReceipts);
        }

        [Test]
        public void WovenEffectsUseAtomicChannelAndBecomeQueryableGameStateOnce()
        {
            StoryNode node = NodeWithSlots();
            var state = new GameState();
            var weaver = new StoryThreadWeaver(new StateEffectAtomicChannel());
            StoryThread thread = Thread(new StoryThreadInjection(
                "prologue",
                "node_intro",
                "INTRO",
                new[]
                {
                    new StoryThreadEffect("relationship", "npc.known", "trust", 1d, null),
                    new StoryThreadEffect("clue", null, null, null, "clue.known"),
                    new StoryThreadEffect("branch_unlock", null, null, null, "branch.known")
                }));

            StoryThreadPresentation first = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                thread);
            StoryThreadPresentation second = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                thread);

            Assert.AreEqual("INTRO\n\nBASE", first.Narrative);
            Assert.AreEqual(1d, state.GetStoryRelationship("npc.known", "trust"));
            Assert.IsTrue(state.HasStoryClue("clue.known"));
            Assert.IsTrue(state.IsStoryBranchUnlocked("branch.known"));
            Assert.AreEqual("BASE", second.Narrative);
            Assert.AreEqual(1d, state.GetStoryRelationship("npc.known", "trust"));
        }

        [Test]
        public void InvalidLaterInjectionPreventsAllTextEffectsAndReceipts()
        {
            StoryNode node = NodeWithSlots();
            var state = new GameState();
            var weaver = new StoryThreadWeaver(new StateEffectAtomicChannel());
            StoryThread thread = Thread(
                new StoryThreadInjection(
                    "prologue",
                    "node_intro",
                    "INTRO",
                    new[] { new StoryThreadEffect("clue", null, null, null, "clue.valid") }),
                new StoryThreadInjection(
                    "prologue",
                    "npc_mention",
                    "NPC",
                    new[] { new StoryThreadEffect("clue", null, null, null, "branch.invalid") }));

            StoryThreadPresentation result = weaver.Present(
                node,
                PlayerRoute.ShenYan,
                state,
                thread);

            Assert.AreEqual("BASE", result.Narrative);
            Assert.IsFalse(result.Injected);
            Assert.IsEmpty(state.storyClueIds);
            Assert.IsEmpty(state.appliedStoryThreadReceipts);
        }

        private static StoryNode NodeWithSlots()
        {
            var node = new StoryNode
            {
                id = "prologue",
                maleText = "BASE",
                femaleText = "BASE_F",
                kind = NodeKind.Narrative
            };
            node.injectionPoints.AddRange(new[] { "node_intro", "travel_event", "npc_mention" });
            return node;
        }

        private static StoryThreadInjection Injection(string point, string text)
        {
            return new StoryThreadInjection(
                "prologue",
                point,
                text,
                new StoryThreadEffect[0]);
        }

        private static StoryThread Thread(params StoryThreadInjection[] injections)
        {
            return new StoryThread(
                "1.0.0",
                "thr.test.v1",
                "assoc.test.v1",
                "p.test",
                new Dictionary<string, string>(),
                injections,
                new string[0],
                true,
                "genjob.test.v1",
                false);
        }
    }
}
