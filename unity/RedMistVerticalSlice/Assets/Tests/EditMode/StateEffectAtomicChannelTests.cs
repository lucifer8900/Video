using System.Collections.Generic;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class StateEffectAtomicChannelTests
    {
        [Test]
        public void ValidRelationshipClueAndBranchCommitOnceAsOneTransaction()
        {
            var state = new GameState();
            var channel = new StateEffectAtomicChannel();
            var effects = new[]
            {
                new StoryThreadEffect("relationship", "npc.known", "trust", 2d, null),
                new StoryThreadEffect("clue", null, null, null, "clue.known"),
                new StoryThreadEffect("branch_unlock", null, null, null, "branch.known")
            };
            var applications = new[]
            {
                new StoryThreadEffectApplication("thr.test/prologue/node_intro/0", effects)
            };

            StateEffectApplyResult first = channel.TryApply(state, applications);
            StateEffectApplyResult second = channel.TryApply(state, applications);

            Assert.AreEqual(StateEffectApplyResult.Applied, first);
            Assert.AreEqual(StateEffectApplyResult.AlreadyApplied, second);
            Assert.AreEqual(1, state.storyRelationships.Count);
            Assert.AreEqual("npc.known", state.storyRelationships[0].targetRef);
            Assert.AreEqual("trust", state.storyRelationships[0].field);
            Assert.AreEqual(2d, state.storyRelationships[0].value);
            Assert.AreEqual(0, state.trust, "Target-specific L2 effects must not mutate legacy global trust.");
            CollectionAssert.AreEqual(new[] { "clue.known" }, state.storyClueIds);
            CollectionAssert.AreEqual(new[] { "branch.known" }, state.storyBranchIds);
            Assert.IsTrue(state.HasStoryClue("clue.known"));
            Assert.IsTrue(state.IsStoryBranchUnlocked("branch.known"));
            Assert.AreEqual(2d, state.GetStoryRelationship("npc.known", "trust"));
            CollectionAssert.AreEqual(
                new[] { "thr.test/prologue/node_intro/0" },
                state.appliedStoryThreadReceipts);
        }

        [Test]
        public void InvalidSecondEffectRollsBackEveryEffectAndReceipt()
        {
            var state = new GameState();
            var channel = new StateEffectAtomicChannel();
            var applications = new[]
            {
                new StoryThreadEffectApplication(
                    "thr.test/prologue/node_intro/0",
                    new[]
                    {
                        new StoryThreadEffect("clue", null, null, null, "clue.valid"),
                        new StoryThreadEffect("kill_named_npc", "npc.known", null, null, null)
                    })
            };

            StateEffectApplyResult result = channel.TryApply(state, applications);

            Assert.AreEqual(StateEffectApplyResult.Rejected, result);
            Assert.AreEqual(0, state.storyRelationships.Count);
            Assert.AreEqual(0, state.storyClueIds.Count);
            Assert.AreEqual(0, state.storyBranchIds.Count);
            Assert.AreEqual(0, state.appliedStoryThreadReceipts.Count);
        }

        [Test]
        public void InvalidLaterInjectionRollsBackEarlierInjectionInSameNodeBatch()
        {
            var state = new GameState();
            var channel = new StateEffectAtomicChannel();
            var applications = new List<StoryThreadEffectApplication>
            {
                new StoryThreadEffectApplication(
                    "thr.test/prologue/node_intro/0",
                    new[] { new StoryThreadEffect("clue", null, null, null, "clue.valid") }),
                new StoryThreadEffectApplication(
                    "thr.test/prologue/npc_mention/1",
                    new[] { new StoryThreadEffect("clue", null, null, null, "branch.wrong_prefix") })
            };

            Assert.AreEqual(StateEffectApplyResult.Rejected, channel.TryApply(state, applications));
            Assert.IsEmpty(state.storyClueIds);
            Assert.IsEmpty(state.appliedStoryThreadReceipts);
        }
    }
}
