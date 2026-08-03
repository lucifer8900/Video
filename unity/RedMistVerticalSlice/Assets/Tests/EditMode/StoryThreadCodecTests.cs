using System;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class StoryThreadCodecTests
    {
        [Test]
        public void StrictContractParsesAllThreeL2Effects()
        {
            StoryThread thread = StoryThreadCodec.Decode(ValidJson(
                "[{\"op\":\"relationship\",\"targetRef\":\"npc.known\",\"field\":\"trust\",\"delta\":1}," +
                "{\"op\":\"clue\",\"value\":\"clue.known\"}," +
                "{\"op\":\"branch_unlock\",\"value\":\"branch.known\"}]"));

            Assert.AreEqual("thr.test.v1", thread.ThreadId);
            Assert.AreEqual("p.test", thread.PlayerId);
            Assert.IsTrue(thread.ExpiresAtChapterEnd);
            Assert.AreEqual(1, thread.Injections.Count);
            Assert.AreEqual(3, thread.Injections[0].Effects.Count);
            Assert.AreEqual(1d, thread.Injections[0].Effects[0].Delta);
        }

        [TestCase("\"unexpected\":true,")]
        [TestCase("\"expiresAtChapterEnd\":false,")]
        public void AdditionalOrConflictingRootDataIsRejected(string prefix)
        {
            string json = ValidJson("[]").Replace(
                "{\"schemaVersion\"",
                "{" + prefix + "\"schemaVersion\"");

            Assert.Throws<StoryThreadContractException>(() => StoryThreadCodec.Decode(json));
        }

        [Test]
        public void InvalidEffectShapeRejectsTheWholeThread()
        {
            string json = ValidJson(
                "[{\"op\":\"relationship\",\"targetRef\":\"npc.known\",\"field\":\"trust\",\"delta\":1,\"value\":\"clue.illegal\"}]");

            Assert.Throws<StoryThreadContractException>(() => StoryThreadCodec.Decode(json));
        }

        [Test]
        public void UnsafeControlCharacterAndDuplicatePropertyAreRejected()
        {
            string control = ValidJson("[]").Replace("technical text", "technical\\u202Etext");
            string duplicate = ValidJson("[]").Replace(
                "\"threadId\":\"thr.test.v1\"",
                "\"threadId\":\"thr.test.v1\",\"threadId\":\"thr.other.v1\"");

            Assert.Throws<StoryThreadContractException>(() => StoryThreadCodec.Decode(control));
            Assert.Throws<StoryThreadContractException>(() => StoryThreadCodec.Decode(duplicate));
        }

        [Test]
        public void PlayerNodeSlotAndMediaAreBoundToTheCurrentBundleContext()
        {
            StoryThreadContext context = StoryThreadPrefetchCoordinatorTests.Context();
            StoryThread wrongPlayer = StoryThreadCodec.Decode(
                ValidJson("[]").Replace("\"p.test\"", "\"p.other\""));
            StoryThread wrongNode = StoryThreadCodec.Decode(
                ValidJson("[]").Replace("\"prologue\"", "\"unknown_node\""));
            StoryThread wrongSlot = StoryThreadCodec.Decode(
                ValidJson("[]").Replace("\"node_intro\"", "\"travel_event\""));
            StoryThread wrongMedia = StoryThreadCodec.Decode(
                ValidJson("[]").Replace("\"mediaRefs\":[]", "\"mediaRefs\":[\"cue.missing\"]"));

            Assert.IsFalse(StoryThreadValidator.TryValidate(wrongPlayer, context));
            Assert.IsFalse(StoryThreadValidator.TryValidate(wrongNode, context));
            Assert.IsFalse(StoryThreadValidator.TryValidate(wrongSlot, context));
            Assert.IsFalse(StoryThreadValidator.TryValidate(wrongMedia, context));
        }

        [Test]
        public void OversizedDocumentIsRejectedBeforeParsing()
        {
            string oversized = ValidJson("[]") + new string(' ', 65536);

            Assert.Throws<StoryThreadContractException>(() =>
                StoryThreadCodec.Decode(oversized));
        }

        internal static string ValidJson(string effects)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"threadId\":\"thr.test.v1\"," +
                   "\"templateId\":\"assoc.test.v1\"," +
                   "\"playerId\":\"p.test\"," +
                   "\"resolvedParams\":{}," +
                   "\"injections\":[{" +
                   "\"nodeId\":\"prologue\"," +
                   "\"point\":\"node_intro\"," +
                   "\"text\":\"technical text\"," +
                   "\"effects\":" + effects + "}]," +
                   "\"mediaRefs\":[]," +
                   "\"expiresAtChapterEnd\":true," +
                   "\"auditRef\":\"genjob.test.v1\"," +
                   "\"fallbackUsed\":false" +
                   "}";
        }
    }
}
