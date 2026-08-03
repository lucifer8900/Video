using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class SaveSystemBundleCompatibilityTests
    {
        private static readonly StoryBundleIdentity CurrentIdentity = new StoryBundleIdentity(
            "red-mist",
            "1.0.0",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        [Test]
        public void SaveEnvelopeRecordsBundleVersionAndHashAndMatchingIdentityLoads()
        {
            var state = new GameState { currentNodeId = "camp", mana = 73 };

            string json = SaveCodec.Encode(state, CurrentIdentity, "2026-07-16T00:00:00.0000000Z");
            SaveLoadResult result = SaveCodec.Decode(json, CurrentIdentity);

            StringAssert.Contains("\"version\": 3", json);
            StringAssert.Contains("\"storyBundleVersion\": \"1.0.0\"", json);
            StringAssert.Contains("\"storyBundleContentHash\": \"sha256:aaaaaaaa", json);
            Assert.AreEqual(SaveLoadStatus.Success, result.Status, result.UserMessage);
            Assert.AreEqual("camp", result.State.currentNodeId);
            Assert.AreEqual(73, result.State.mana);
        }

        [Test]
        public void DifferentBundleVersionIsExplicitlyRejected()
        {
            string json = SaveCodec.Encode(new GameState(), CurrentIdentity);
            var other = new StoryBundleIdentity("red-mist", "2.0.0", CurrentIdentity.ContentHash);

            SaveLoadResult result = SaveCodec.Decode(json, other);

            Assert.AreEqual(SaveLoadStatus.BundleMismatch, result.Status);
            StringAssert.Contains("版本", result.UserMessage);
        }

        [Test]
        public void DifferentBundleHashIsExplicitlyRejected()
        {
            string json = SaveCodec.Encode(new GameState(), CurrentIdentity);
            var other = new StoryBundleIdentity(
                "red-mist",
                "1.0.0",
                "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            SaveLoadResult result = SaveCodec.Decode(json, other);

            Assert.AreEqual(SaveLoadStatus.BundleMismatch, result.Status);
            StringAssert.Contains("哈希", result.UserMessage);
        }

        [Test]
        public void VersionTwoSaveWithoutBundleIdentityIsRejectedButNotClassifiedAsCorrupt()
        {
            const string legacy = "{\"version\":2,\"savedAtUtc\":\"2026-07-15T00:00:00Z\",\"state\":{\"currentNodeId\":\"camp\"}}";

            SaveLoadResult result = SaveCodec.Decode(legacy, CurrentIdentity);

            Assert.AreEqual(SaveLoadStatus.BundleMismatch, result.Status);
            StringAssert.Contains("旧版存档", result.UserMessage);
        }

        [Test]
        public void MalformedSaveIsClassifiedAsCorrupt()
        {
            SaveLoadResult result = SaveCodec.Decode("{ broken", CurrentIdentity);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
            StringAssert.Contains("损坏", result.UserMessage);
        }

        [Test]
        public void MissingExplicitSaveVersionIsCorruptInsteadOfInheritingCurrentDefault()
        {
            const string json = "{\"storyBundleVersion\":\"1.0.0\",\"storyBundleContentHash\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"state\":{\"currentNodeId\":\"camp\"}}";

            SaveLoadResult result = SaveCodec.Decode(json, CurrentIdentity);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
            StringAssert.Contains("版本字段", result.UserMessage);
        }

        [Test]
        public void SavePointingAtUnknownCurrentBundleNodeIsCorrupt()
        {
            string json = SaveCodec.Encode(new GameState { currentNodeId = "removed-node" }, CurrentIdentity);

            SaveLoadResult result = SaveCodec.Decode(
                json,
                CurrentIdentity,
                nodeId => nodeId == "prologue");

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
            StringAssert.Contains("剧情节点", result.UserMessage);
        }

        [Test]
        public void VersionThreeSaveWithoutStoryThreadFieldsLoadsWithSafeEmptyDefaults()
        {
            const string json =
                "{\"version\":3,\"savedAtUtc\":\"2026-07-16T00:00:00Z\"," +
                "\"storyBundleVersion\":\"1.0.0\"," +
                "\"storyBundleContentHash\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
                "\"state\":{\"currentNodeId\":\"camp\"}}";

            SaveLoadResult result = SaveCodec.Decode(json, CurrentIdentity);

            Assert.AreEqual(SaveLoadStatus.Success, result.Status, result.UserMessage);
            Assert.NotNull(result.State.storyRelationships);
            Assert.NotNull(result.State.storyClueIds);
            Assert.NotNull(result.State.storyBranchIds);
            Assert.NotNull(result.State.appliedStoryThreadReceipts);
            Assert.IsEmpty(result.State.storyRelationships);
            Assert.IsEmpty(result.State.storyClueIds);
            Assert.IsEmpty(result.State.storyBranchIds);
            Assert.IsEmpty(result.State.appliedStoryThreadReceipts);
        }

        [Test]
        public void StoryThreadEffectsAndReceiptsRoundTripButGeneratedTextDoesNotEnterSave()
        {
            var state = new GameState();
            state.storyRelationships.Add(new StoryRelationshipState("npc.known", "trust", 2d));
            state.storyClueIds.Add("clue.known");
            state.storyBranchIds.Add("branch.known");
            state.appliedStoryThreadReceipts.Add("thr.test/prologue/node_intro/0");

            string json = SaveCodec.Encode(
                state,
                CurrentIdentity,
                "2026-07-16T00:00:00.0000000Z");
            SaveLoadResult result = SaveCodec.Decode(json, CurrentIdentity);

            Assert.AreEqual(SaveLoadStatus.Success, result.Status, result.UserMessage);
            Assert.AreEqual(2d, result.State.storyRelationships[0].value);
            CollectionAssert.AreEqual(new[] { "clue.known" }, result.State.storyClueIds);
            CollectionAssert.AreEqual(new[] { "branch.known" }, result.State.storyBranchIds);
            CollectionAssert.AreEqual(
                new[] { "thr.test/prologue/node_intro/0" },
                result.State.appliedStoryThreadReceipts);
            StringAssert.DoesNotContain("technical text", json);
            StringAssert.DoesNotContain("resolvedParams", json);
        }

        [Test]
        public void UndefinedRouteEnumIsRejectedAsCorrupt()
        {
            var identity = new StoryBundleIdentity(
                "chapter.red_mist",
                "1.0.0",
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            string encoded = SaveCodec.Encode(
                new GameState { route = PlayerRoute.ShenYan },
                identity,
                "2026-07-16T00:00:00.0000000Z");
            string tampered = encoded.Replace("\"route\": 1", "\"route\": 999");
            Assert.AreNotEqual(encoded, tampered, "The route fixture replacement must apply.");

            SaveLoadResult result = SaveCodec.Decode(tampered, identity);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
            Assert.IsNull(result.State);
        }

        [Test]
        public void InvalidPersistedL2IdentifiersAreRejectedAsCorrupt()
        {
            string json = SaveCodec.Encode(
                new GameState(),
                CurrentIdentity,
                "2026-07-16T00:00:00.0000000Z").Replace(
                "\"storyClueIds\": [],",
                "\"storyClueIds\": [\"branch.wrong\"],");

            SaveLoadResult result = SaveCodec.Decode(json, CurrentIdentity);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
            StringAssert.Contains("关联剧情状态", result.UserMessage);
        }
    }
}
