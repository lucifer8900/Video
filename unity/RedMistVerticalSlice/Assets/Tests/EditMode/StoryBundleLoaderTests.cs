using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Lingmai.RedMist.Tests
{
    public sealed class StoryBundleLoaderTests
    {
        private static string BundlePath => Path.Combine(
            Application.streamingAssetsPath,
            "Story",
            "red-mist",
            StoryBundleLoader.BundleFileName);

        [Test]
        public void ApprovedBundleLoadsWithValidatedIdentityAndRuntimeContent()
        {
            StoryBundleLoadResult result = StoryBundleLoader.LoadFromFile(BundlePath);

            Assert.IsTrue(result.Success, result.TechnicalMessage);
            Assert.AreEqual(StoryBundleLoader.SupportedSchemaVersion, result.Bundle.Identity.Version);
            StringAssert.StartsWith("sha256:", result.Bundle.Identity.ContentHash);
            Assert.AreEqual(71, result.Bundle.Identity.ContentHash.Length);
            Assert.AreEqual("prologue", result.Bundle.EntryNodeId);
            Assert.AreEqual(14, result.Bundle.Nodes.Count);
            Assert.AreEqual(11, result.Bundle.MediaAssets.Count);
            CollectionAssert.AreEqual(
                new[] { "node_intro" },
                result.Bundle.Nodes["prologue"].injectionPoints);
            CollectionAssert.AreEqual(
                new[] { "travel_event" },
                result.Bundle.Nodes["flight"].injectionPoints);
            CollectionAssert.AreEqual(
                new[] { "npc_mention" },
                result.Bundle.Nodes["shijun"].injectionPoints);
            foreach (StoryNode node in result.Bundle.Nodes.Values)
            {
                if (node.id == "prologue" || node.id == "flight" || node.id == "shijun")
                    continue;
                CollectionAssert.IsEmpty(node.injectionPoints, node.id);
            }
        }

        [Test]
        public void VoiceIntentDefinitionsAndCurrentNodeWhitelistAreRetained()
        {
            string json = BuildBundleWithSyntheticVoiceIntent(
                "[\"fixture.intent.prologue.inspect\"]");

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsTrue(result.Success, result.TechnicalMessage);
            Assert.AreEqual(6, result.Bundle.VoiceIntents.Count);
            Assert.IsTrue(result.Bundle.VoiceIntents.ContainsKey("fixture.intent.prologue.inspect"));
            CollectionAssert.AreEqual(
                new[] { "fixture.intent.prologue.inspect" },
                result.Bundle.Nodes["prologue"].voiceIntentRefs);
            Assert.AreEqual(31, result.Bundle.NpcResponses.Count);
            Assert.IsTrue(result.Bundle.NpcResponses.ContainsKey("fixture.response.prologue.inspect"));
            Assert.AreEqual(
                "npc.invalid.calm.silence",
                result.Bundle.Nodes["prologue"].invalidInputRules[StoryInvalidInputKind.Silence]);
            Assert.AreEqual(
                "请再说明一次。",
                result.Bundle.NpcResponses["fixture.response.prologue.inspect"].Text);
        }

        [Test]
        public void DuplicateCurrentNodeVoiceIntentReferenceReportsExactJsonPointer()
        {
            string json = BuildBundleWithSyntheticVoiceIntent(
                "[\"fixture.intent.prologue.inspect\", \"fixture.intent.prologue.inspect\"]");

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.SchemaInvalid, result.ErrorCode);
            StringAssert.StartsWith(
                "/sceneNodes/0/voiceIntentRefs/1 ",
                result.TechnicalMessage);
        }

        [Test]
        public void DanglingCurrentNodeVoiceIntentReferenceReportsExactJsonPointer()
        {
            string json = BuildBundleWithSyntheticVoiceIntent(
                "[\"fixture.intent.prologue.missing\"]");

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.SchemaInvalid, result.ErrorCode);
            StringAssert.StartsWith(
                "/sceneNodes/0/voiceIntentRefs/0 ",
                result.TechnicalMessage);
        }

        [Test]
        public void ProductionBundleLoadsFiveMajorIntentsAndFiveInvalidInputRulesPerNode()
        {
            StoryBundleLoadResult result = StoryBundleLoader.LoadFromFile(BundlePath);

            Assert.IsTrue(result.Success, result.TechnicalMessage);
            Assert.AreEqual(5, result.Bundle.VoiceIntents.Count);
            Assert.AreEqual(30, result.Bundle.NpcResponses.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "intent.prologue.inspect_mist",
                    "intent.alliance.cautious_cooperation",
                    "intent.rescue.secure_survivor",
                    "intent.shijun.verify_bargain",
                    "intent.underground.coordinate_retreat"
                },
                result.Bundle.VoiceIntents.Keys);
            foreach (StoryNode node in result.Bundle.Nodes.Values)
            {
                Assert.AreEqual(5, node.invalidInputRules.Count, node.id);
                Assert.AreEqual(
                    5 + node.voiceIntentRefs.Count,
                    node.npcResponseRefs.Count,
                    node.id);
            }
        }

        [Test]
        public void MalformedJsonReturnsFriendlyStructuredFailure()
        {
            StoryBundleLoadResult result = StoryBundleLoader.Parse("{ definitely-not-json");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.JsonMalformed, result.ErrorCode);
            StringAssert.Contains("剧情包", result.UserMessage);
            Assert.IsNotEmpty(result.TechnicalMessage);

            StoryBundleErrorPageModel page = StoryBundleErrorPageModel.From(result);
            Assert.AreEqual("剧情包无法加载", page.Title);
            StringAssert.Contains(result.UserMessage, page.Message);
            Assert.IsFalse(page.CanContinue);
        }

        [Test]
        public void UnsupportedVersionIsRejectedBeforeHashValidation()
        {
            string json = File.ReadAllText(BundlePath)
                .Replace("\"schemaVersion\": \"1.0.0\"", "\"schemaVersion\": \"9.9.9\"");

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.UnsupportedVersion, result.ErrorCode);
            StringAssert.Contains("9.9.9", result.UserMessage);
        }

        [Test]
        public void MissingRequiredSchemaFieldIsRejectedBeforeHashValidation()
        {
            string json = File.ReadAllText(BundlePath)
                .Replace("  \"entryNodeId\": \"prologue\",\r\n", string.Empty)
                .Replace("  \"entryNodeId\": \"prologue\",\n", string.Empty);

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.SchemaInvalid, result.ErrorCode);
            StringAssert.Contains("entryNodeId", result.TechnicalMessage);
        }

        [Test]
        public void TamperedPayloadIsRejectedByCanonicalContentHash()
        {
            string json = File.ReadAllText(BundlePath).Replace(
                "story.red_mist.node.prologue.title\": \"\\u96FE\\u95E8\\u5C06\\u542F",
                "story.red_mist.node.prologue.title\": \"tampered-title");

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.HashMismatch, result.ErrorCode);
            StringAssert.Contains("哈希", result.UserMessage);
        }

        [Test]
        public void CanonicalHashIsInsensitiveToObjectPropertyOrder()
        {
            string original = File.ReadAllText(BundlePath);
            string reordered = original.Replace(
                "  \"schemaVersion\": \"1.0.0\",\r\n  \"id\": \"chapter.red_mist\",",
                "  \"id\": \"chapter.red_mist\",\r\n  \"schemaVersion\": \"1.0.0\",")
                .Replace(
                    "  \"schemaVersion\": \"1.0.0\",\n  \"id\": \"chapter.red_mist\",",
                    "  \"id\": \"chapter.red_mist\",\n  \"schemaVersion\": \"1.0.0\",");

            Assert.AreNotEqual(original, reordered, "The fixture must really reorder root properties.");
            Assert.AreEqual(
                StoryBundleLoader.ComputeContentHash(original),
                StoryBundleLoader.ComputeContentHash(reordered));
            Assert.IsTrue(StoryBundleLoader.Parse(reordered).Success);
        }

        [Test]
        public void SchemaValidOptionalNodeFallbackMediaIsAccepted()
        {
            string json = File.ReadAllText(BundlePath).Replace(
                "      \"backgroundMediaRef\": \"red_mist_weather\",",
                "      \"backgroundMediaRef\": \"red_mist_weather\",\n      \"fallbackMediaRef\": \"red_mist_weather\",");
            json = Rehash(json);

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsTrue(result.Success, result.TechnicalMessage);
        }

        [Test]
        public void InvalidNestedStateEffectIsRejectedEvenWithRecomputedHash()
        {
            string json = File.ReadAllText(BundlePath).Replace("\"effects\": []", "\"effects\": [{}]");
            json = Rehash(json);

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.SchemaInvalid, result.ErrorCode);
        }

        [Test]
        public void UnsupportedAdvancedRootRecordsAreRejectedInsteadOfPartiallyAccepted()
        {
            string json = File.ReadAllText(BundlePath).Replace("\"storyFacts\": []", "\"storyFacts\": [{}]");
            json = Rehash(json);

            StoryBundleLoadResult result = StoryBundleLoader.Parse(json);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(StoryBundleErrorCode.SchemaInvalid, result.ErrorCode);
            StringAssert.Contains("storyFacts", result.TechnicalMessage);
        }

        private static string BuildBundleWithSyntheticVoiceIntent(string voiceIntentRefsJson)
        {
            string json = File.ReadAllText(BundlePath).Replace("\r\n", "\n");
            json = ReplaceFirst(
                json,
                "\"zh-CN\": {",
                "\"zh-CN\": {\n" +
                "      \"fixture.display.intent.prologue.inspect\": \"观察四周\",\n" +
                "      \"fixture.topic.inspect\": \"看看周围\",\n" +
                "      \"fixture.tone.neutral\": \"平静\",\n" +
                "      \"fixture.response.prologue.inspect.text\": \"请再说明一次。\",\n" +
                "      \"fixture.emotion.attentive\": \"留意\",");
            json = ReplaceFirst(
                json,
                "\"voiceIntentRefs\": [\n" +
                "        \"intent.prologue.inspect_mist\"\n" +
                "      ]",
                "\"voiceIntentRefs\": " + voiceIntentRefsJson);
            json = ReplaceFirst(
                json,
                "\"npcResponseRefs\": [",
                "\"npcResponseRefs\": [\n" +
                "        \"fixture.response.prologue.inspect\",");
            json = ReplaceFirst(
                json,
                "\"voiceIntents\": [",
                "\"voiceIntents\": [\n" +
                "    {\n" +
                "      \"schemaVersion\": \"1.0.0\",\n" +
                "      \"id\": \"fixture.intent.prologue.inspect\",\n" +
                "      \"displayKey\": \"fixture.display.intent.prologue.inspect\",\n" +
                "      \"topicKeys\": [\"fixture.topic.inspect\"],\n" +
                "      \"toneKey\": \"fixture.tone.neutral\",\n" +
                "      \"minimumConfidence\": 0.8,\n" +
                "      \"npcResponseRef\": \"fixture.response.prologue.inspect\",\n" +
                "      \"effects\": [],\n" +
                "      \"approvalStatus\": \"approved\"\n" +
                "    },");
            json = ReplaceFirst(
                json,
                "\"npcResponses\": [",
                "\"npcResponses\": [\n" +
                "    {\n" +
                "      \"schemaVersion\": \"1.0.0\",\n" +
                "      \"id\": \"fixture.response.prologue.inspect\",\n" +
                "      \"textKey\": \"fixture.response.prologue.inspect.text\",\n" +
                "      \"emotionKey\": \"fixture.emotion.attentive\",\n" +
                "      \"lipSyncMediaRef\": \"red_mist_weather\",\n" +
                "      \"effects\": [],\n" +
                "      \"approvalStatus\": \"approved\"\n" +
                "    },");
            return Rehash(json);
        }

        private static string ReplaceFirst(string source, string oldValue, string newValue)
        {
            int index = source.IndexOf(oldValue, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(index, 0, "Synthetic fixture anchor was not found: " + oldValue);
            return source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
        }

        private static string Rehash(string json)
        {
            StoryBundleLoadResult original = StoryBundleLoader.LoadFromFile(BundlePath);
            Assert.IsTrue(original.Success, original.TechnicalMessage);
            string oldHash = original.Bundle.Identity.ContentHash;
            string newHash = StoryBundleLoader.ComputeContentHash(json);
            return json.Replace(oldHash, newHash);
        }
    }
}
