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
    }
}
