using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Lingmai.RedMist.Tests
{
    public sealed class Cx505FullPathAuditTests
    {
        private static readonly PlayerRoute[] Routes =
        {
            PlayerRoute.ShenYan,
            PlayerRoute.ChuMingqi
        };

        private StoryBundle _bundle;
        private string _storyRoot;
        private string _promptPath;

        [SetUp]
        public void LoadProductionContent()
        {
            _storyRoot = Path.Combine(Application.streamingAssetsPath, "Story", "red-mist");
            StoryBundleLoadResult loaded = StoryBundleLoader.LoadFromFile(
                Path.Combine(_storyRoot, StoryBundleLoader.BundleFileName));
            Assert.IsTrue(loaded.Success, loaded.TechnicalMessage);
            _bundle = loaded.Bundle;
            _promptPath = Path.Combine(
                RepositoryRoot(),
                "content",
                "story",
                "red-mist",
                "video-generation-prompts.cx505.md");
        }

        [Test]
        public void EveryWeightedPathReachesAllEndingsWithAndWithoutAssociation()
        {
            var coveredEdges = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerRoute route in Routes)
            {
                AuditResult baseline = new AuditRunner(
                    _bundle,
                    _storyRoot,
                    route,
                    false).Run();
                AuditResult associated = new AuditRunner(
                    _bundle,
                    _storyRoot,
                    route,
                    true).Run();

                Assert.Greater(baseline.TotalPathCount, 0L, route.ToString());
                Assert.AreEqual(baseline.TotalPathCount, associated.TotalPathCount, route.ToString());
                AssertDictionariesEqual(
                    baseline.OutcomePathCounts,
                    associated.OutcomePathCounts,
                    route + " ending multiplicities");
                AssertDictionariesEqual(
                    baseline.TerminalStateCounts,
                    associated.TerminalStateCounts,
                    route + " terminal gameplay states");
                CollectionAssert.AreEquivalent(
                    Enum.GetValues(typeof(RedMistEndingKind)),
                    baseline.OutcomePathCounts.Keys,
                    route + " ending kinds");
                CollectionAssert.AreEquivalent(
                    new[] { "prologue", "flight", "shijun" },
                    associated.InjectedNodeIds,
                    route + " approved association points");
                Assert.IsEmpty(baseline.InjectedNodeIds, route + " no-thread baseline");

                coveredEdges.UnionWith(baseline.CoveredEdges);
                string summary =
                    "CX505_FULL_PATH route=" + route +
                    " paths=" + baseline.TotalPathCount +
                    " distinctStates=" + baseline.DistinctStateCount +
                    " retreat=" + baseline.OutcomePathCounts[RedMistEndingKind.Retreat] +
                    " costly=" + baseline.OutcomePathCounts[RedMistEndingKind.CostlyVictory] +
                    " cautious=" + baseline.OutcomePathCounts[RedMistEndingKind.CautiousAlliance];
                TestContext.Progress.WriteLine(summary);
                Debug.Log(summary);
            }

            CollectionAssert.AreEquivalent(ExpectedAuthoredEdges(), coveredEdges);
        }

        [Test]
        public void MajorVoiceIntentsKeepFixedChoicesAndEveryResponseHasManualVideoPrompt()
        {
            string[] expectedIntentIds =
            {
                "intent.prologue.inspect_mist",
                "intent.alliance.cautious_cooperation",
                "intent.rescue.secure_survivor",
                "intent.shijun.verify_bargain",
                "intent.underground.coordinate_retreat"
            };
            Assert.AreEqual(5, _bundle.VoiceIntents.Count);
            CollectionAssert.AreEquivalent(expectedIntentIds, _bundle.VoiceIntents.Keys);
            Assert.IsTrue(File.Exists(_promptPath), "Manual video prompt file is missing: " + _promptPath);
            string prompts = File.ReadAllText(_promptPath);

            int intentReferences = 0;
            foreach (StoryNode node in _bundle.Nodes.Values)
            {
                Assert.AreEqual(5, node.invalidInputRules.Count, node.id);
                foreach (string responseId in node.invalidInputRules.Values)
                    Assert.IsTrue(node.npcResponseRefs.Contains(responseId), node.id + " invalid response allowlist");

                foreach (string intentId in node.voiceIntentRefs)
                {
                    intentReferences++;
                    StoryVoiceIntent intent = _bundle.VoiceIntents[intentId];
                    Assert.IsNotEmpty(intent.KeywordPhrases, intentId);
                    Assert.GreaterOrEqual(intent.MinimumConfidence, 0.8d, intentId);
                    Assert.IsTrue(node.npcResponseRefs.Contains(intent.NpcResponseRef), intentId);
                    Assert.IsTrue(node.npcResponseRefs.Contains(intent.FallbackNpcResponseRef), intentId);

                    int choiceCount = node.choices.Count;
                    var coordinator = new VoiceInteractionCoordinator();
                    int generation = coordinator.Begin(node.id, node.npcResponseRefs);
                    VoicePresentationDecision decision = coordinator.Complete(
                        generation,
                        node.id,
                        VoiceInteractionClientResult.Succeeded(
                            generation,
                            "npc_response",
                            "valid",
                            intent.Id,
                            0.95d,
                            "cx505_audit",
                            intent.NpcResponseRef,
                            "req.cx505",
                            200));
                    Assert.AreEqual(VoicePresentationOutcome.PresentNpcResponse, decision.Outcome, intentId);
                    Assert.AreEqual(intent.NpcResponseRef, decision.NpcResponseId, intentId);
                    Assert.AreEqual(choiceCount, node.choices.Count, intentId + " must retain fixed choices");
                }
            }
            Assert.AreEqual(5, intentReferences);

            foreach (StoryNpcResponse response in _bundle.NpcResponses.Values)
            {
                StringAssert.Contains(
                    "<!-- response:" + response.Id + " -->",
                    prompts,
                    response.Id + " manual video prompt");
            }
        }

        [Test]
        public void EveryMediaReferenceResolvesAndEveryMissingNodeVideoHasManualPrompt()
        {
            string prompts = File.ReadAllText(_promptPath);
            foreach (StoryMediaAsset media in _bundle.MediaAssets.Values)
            {
                string path = ResolveMediaPath(media.Uri);
                Assert.IsTrue(File.Exists(path), media.Id + " -> " + path);
                string actual = "sha256:" +
                    BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(path)))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                Assert.AreEqual(media.ContentHash, actual, media.Id);
            }

            StoryNode[] missingNodeVideos = _bundle.Nodes.Values
                .Where(node => string.IsNullOrWhiteSpace(node.introMediaRef))
                .OrderBy(node => node.id, StringComparer.Ordinal)
                .ToArray();
            Assert.AreEqual(10, missingNodeVideos.Length);
            foreach (StoryNode node in missingNodeVideos)
            {
                Assert.IsTrue(_bundle.MediaAssets.ContainsKey(node.background), node.id + " fallback image");
                StringAssert.Contains(
                    "<!-- node:" + node.id + " -->",
                    prompts,
                    node.id + " manual video prompt");
            }

            foreach (StoryNode node in _bundle.Nodes.Values)
            {
                Assert.IsTrue(_bundle.MediaAssets.ContainsKey(node.background), node.id + " background");
                if (!string.IsNullOrWhiteSpace(node.introMediaRef))
                {
                    Assert.AreEqual(
                        "video",
                        _bundle.MediaAssets[node.introMediaRef].MediaType,
                        node.id + " intro");
                }
            }
        }

        [Test]
        public void EveryManualVideoPromptUsesOneExistingLandscapeFirstFrame()
        {
            string prompts = File.ReadAllText(_promptPath);
            MatchCollection promptMarkers = Regex.Matches(
                prompts,
                @"<!-- (?:node|response):[^>]+ -->");
            MatchCollection firstFrameLines = Regex.Matches(
                prompts,
                @"^上传首帧（仅上传这一张）：`(?<path>[^`\r\n]+\.png)`$",
                RegexOptions.Multiline);

            Assert.AreEqual(40, promptMarkers.Count, "CX-505 video prompt count");
            Assert.AreEqual(
                promptMarkers.Count,
                firstFrameLines.Count,
                "Every video prompt must name exactly one uploadable first-frame PNG.");
            Assert.AreEqual(
                0,
                Regex.Matches(prompts, @"(?:参考图|人物参考|辅助参考|优先首帧)：").Count,
                "The manual pack must not imply that Veo Image-to-video accepts extra reference images.");

            foreach (Match match in firstFrameLines)
            {
                string relativePath = match.Groups["path"].Value;
                string absolutePath = Path.Combine(
                    RepositoryRoot(),
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(absolutePath), "Missing first frame: " + relativePath);

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    Assert.IsTrue(
                        texture.LoadImage(File.ReadAllBytes(absolutePath)),
                        "Unreadable first frame: " + relativePath);
                    Assert.That(
                        (double)texture.width / texture.height,
                        Is.EqualTo(16d / 9d).Within(0.01d),
                        "First frame must be 16:9 without stretching: " + relativePath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private HashSet<string> ExpectedAuthoredEdges()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (StoryNode node in _bundle.Nodes.Values)
            {
                foreach (ChoiceDefinition choice in node.choices)
                    result.Add(Edge(node.id, choice.action, choice.nextNodeId));
                foreach (StoryTransitionDefinition transition in node.transitions)
                    result.Add(Edge(node.id, transition.triggerId, transition.nextNodeId));
            }
            return result;
        }

        private static string ResolveMediaPath(string uri)
        {
            const string resourcePrefix = "unity-resource:///";
            const string streamingPrefix = "streaming-assets:///";
            if (uri.StartsWith(resourcePrefix, StringComparison.Ordinal))
            {
                return Path.Combine(
                    Application.dataPath,
                    "Resources",
                    uri.Substring(resourcePrefix.Length).Replace('/', Path.DirectorySeparatorChar));
            }
            if (uri.StartsWith(streamingPrefix, StringComparison.Ordinal))
            {
                return Path.Combine(
                    Application.streamingAssetsPath,
                    uri.Substring(streamingPrefix.Length).Replace('/', Path.DirectorySeparatorChar));
            }
            Assert.Fail("Unsupported media URI: " + uri);
            return string.Empty;
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        }

        private static string Edge(string nodeId, string triggerId, string nextNodeId)
        {
            return nodeId + "|" + triggerId + "|" + nextNodeId;
        }

        private static void AssertDictionariesEqual<TKey>(
            IReadOnlyDictionary<TKey, long> expected,
            IReadOnlyDictionary<TKey, long> actual,
            string message)
        {
            CollectionAssert.AreEquivalent(expected.Keys, actual.Keys, message);
            foreach (KeyValuePair<TKey, long> pair in expected)
                Assert.AreEqual(pair.Value, actual[pair.Key], message + " " + pair.Key);
        }

        private sealed class AuditRunner
        {
            private static readonly MinigameResult[] Results =
            {
                new MinigameResult(false, false, 0, "failure"),
                new MinigameResult(true, false, 1, "success"),
                new MinigameResult(true, true, 2, "perfect")
            };

            private readonly StoryBundle _bundle;
            private readonly PlayerRoute _route;
            private readonly bool _withThread;
            private readonly StoryThreadWeaver _weaver;
            private readonly StoryThread _thread;
            private readonly Dictionary<string, Dictionary<string, WeightedState>> _states;
            private readonly AuditResult _result;

            public AuditRunner(
                StoryBundle bundle,
                string storyRoot,
                PlayerRoute route,
                bool withThread)
            {
                _bundle = bundle;
                _route = route;
                _withThread = withThread;
                _weaver = new StoryThreadWeaver(new StateEffectAtomicChannel());
                _states = bundle.Nodes.Keys.ToDictionary(
                    id => id,
                    id => new Dictionary<string, WeightedState>(StringComparer.Ordinal),
                    StringComparer.Ordinal);
                _result = new AuditResult();
                if (withThread)
                {
                    var context = new StoryThreadContext(
                        "p.cx505",
                        bundle.Identity,
                        route,
                        bundle.EntryNodeId,
                        bundle.Nodes,
                        bundle.MediaAssets);
                    _thread = StoryThreadDefaults.LoadFromFile(
                        Path.Combine(storyRoot, "association.defaults.json"),
                        context);
                    Assert.NotNull(_thread, route + " default association thread");
                    foreach (StoryThreadInjection injection in _thread.Injections)
                        Assert.IsEmpty(injection.Effects, injection.NodeId + "/" + injection.Point);
                }
            }

            public AuditResult Run()
            {
                var initial = new GameState
                {
                    route = _route,
                    currentNodeId = _bundle.EntryNodeId,
                    sectDuty = _route == PlayerRoute.ChuMingqi ? 2 : 0
                };
                AddState(_bundle.EntryNodeId, initial, 1L);

                foreach (string nodeId in TopologicalNodeIds(_bundle))
                {
                    StoryNode node = _bundle.Nodes[nodeId];
                    WeightedState[] activeStates = _states[nodeId].Values.ToArray();
                    _states[nodeId].Clear();
                    foreach (WeightedState weighted in activeStates)
                    {
                        _result.DistinctStateCount++;
                        int successorCount = Expand(node, weighted);
                        if (node.kind == NodeKind.Ending)
                            Assert.AreEqual(0, successorCount, node.id);
                        else
                            Assert.Greater(successorCount, 0, node.id + " dead end");
                    }
                }
                Assert.AreEqual(
                    _result.TotalPathCount,
                    _result.OutcomePathCounts.Values.Sum(),
                    _route + " path accounting");
                return _result;
            }

            private int Expand(StoryNode node, WeightedState weighted)
            {
                if (node.kind == NodeKind.Ending)
                {
                    RedMistEndingKind ending = StoryProgressionRules.ClassifyEnding(weighted.State);
                    AddCount(_result.OutcomePathCounts, ending, weighted.Count);
                    AddCount(
                        _result.TerminalStateCounts,
                        StateSignature(weighted.State, false),
                        weighted.Count);
                    _result.TotalPathCount += weighted.Count;
                    return 0;
                }

                int successors = 0;
                if (node.kind == NodeKind.Narrative)
                {
                    foreach (ChoiceDefinition choice in node.choices)
                    {
                        GameState state = Clone(weighted.State);
                        StoryProgressionRules.ApplyNarrativeChoice(state, choice.action);
                        AddTransition(node, choice.action, choice.nextNodeId, state, weighted.Count);
                        successors++;
                    }
                    return successors;
                }

                if (node.kind == NodeKind.Flight ||
                    node.kind == NodeKind.HerbGathering ||
                    node.kind == NodeKind.DivineSense ||
                    node.kind == NodeKind.Formation)
                {
                    foreach (MinigameResult result in Results)
                    {
                        GameState state = Clone(weighted.State);
                        string trigger;
                        if (node.kind == NodeKind.Flight)
                            trigger = StoryProgressionRules.ApplyFlight(state, result);
                        else if (node.kind == NodeKind.HerbGathering)
                            trigger = StoryProgressionRules.ApplyHerb(state, result);
                        else if (node.kind == NodeKind.DivineSense)
                            trigger = StoryProgressionRules.ApplyScan(state, result);
                        else
                            trigger = StoryProgressionRules.ApplyFormation(state, result);
                        AddTransition(node, trigger, TransitionTarget(node, trigger), state, weighted.Count);
                        successors++;
                    }
                    if (node.kind == NodeKind.Formation && weighted.State.formationOpened)
                    {
                        GameState bypass = Clone(weighted.State);
                        string trigger = StoryProgressionRules.ApplyFormationSpike(bypass);
                        AddTransition(node, trigger, TransitionTarget(node, trigger), bypass, weighted.Count);
                        successors++;
                    }
                    return successors;
                }

                if (node.kind == NodeKind.CombatOne || node.kind == NodeKind.CombatTwo)
                {
                    int round = node.kind == NodeKind.CombatOne ? 1 : 2;
                    foreach (string action in StoryProgressionRules.GetAvailableCombatActions(weighted.State, round))
                    {
                        GameState state = Clone(weighted.State);
                        CombatProgressionResult resolution = StoryProgressionRules.ApplyCombat(state, action, round);
                        AddTransition(
                            node,
                            resolution.TriggerId,
                            TransitionTarget(node, resolution.TriggerId),
                            state,
                            weighted.Count);
                        successors++;
                    }
                    return successors;
                }

                Assert.Fail("Unsupported node kind in CX-505 audit: " + node.kind);
                return 0;
            }

            private void AddTransition(
                StoryNode node,
                string trigger,
                string target,
                GameState state,
                long count)
            {
                Assert.IsTrue(_bundle.Nodes.ContainsKey(target), Edge(node.id, trigger, target));
                _result.CoveredEdges.Add(Edge(node.id, trigger, target));
                AddState(target, state, count);
            }

            private void AddState(string nodeId, GameState state, long count)
            {
                state.currentNodeId = nodeId;
                StoryNode node = _bundle.Nodes[nodeId];
                StoryThreadPresentation presentation = _weaver.Present(
                    node,
                    _route,
                    state,
                    _withThread ? _thread : null);
                if (presentation.Injected)
                    _result.InjectedNodeIds.Add(nodeId);
                if (!_withThread)
                {
                    Assert.IsFalse(presentation.Injected, nodeId);
                    Assert.AreEqual(node.TextFor(_route), presentation.Narrative, nodeId);
                }

                string signature = StateSignature(state, true);
                Dictionary<string, WeightedState> atNode = _states[nodeId];
                if (atNode.TryGetValue(signature, out WeightedState existing))
                    existing.Count += count;
                else
                    atNode.Add(signature, new WeightedState(state, count));
            }

            private static string TransitionTarget(StoryNode node, string trigger)
            {
                StoryTransitionDefinition transition = node.transitions.FirstOrDefault(
                    item => string.Equals(item.triggerId, trigger, StringComparison.Ordinal));
                Assert.NotNull(transition, node.id + " missing transition " + trigger);
                return transition.nextNodeId;
            }

            private static IReadOnlyList<string> TopologicalNodeIds(StoryBundle bundle)
            {
                var outgoing = bundle.Nodes.Keys.ToDictionary(
                    id => id,
                    id => new HashSet<string>(StringComparer.Ordinal),
                    StringComparer.Ordinal);
                var indegree = bundle.Nodes.Keys.ToDictionary(id => id, id => 0, StringComparer.Ordinal);
                foreach (StoryNode node in bundle.Nodes.Values)
                {
                    foreach (ChoiceDefinition choice in node.choices)
                        outgoing[node.id].Add(choice.nextNodeId);
                    foreach (StoryTransitionDefinition transition in node.transitions)
                        outgoing[node.id].Add(transition.nextNodeId);
                }
                foreach (HashSet<string> targets in outgoing.Values)
                    foreach (string target in targets) indegree[target]++;

                var ready = new SortedSet<string>(
                    indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key),
                    StringComparer.Ordinal);
                var result = new List<string>(bundle.Nodes.Count);
                while (ready.Count > 0)
                {
                    string nodeId = ready.Min;
                    ready.Remove(nodeId);
                    result.Add(nodeId);
                    foreach (string target in outgoing[nodeId])
                    {
                        indegree[target]--;
                        if (indegree[target] == 0) ready.Add(target);
                    }
                }
                Assert.AreEqual(bundle.Nodes.Count, result.Count, "Story graph must be acyclic for exhaustive path accounting.");
                return result;
            }

            private static GameState Clone(GameState source)
            {
                return JsonUtility.FromJson<GameState>(JsonUtility.ToJson(source));
            }

            private static string StateSignature(GameState source, bool includeReceipts)
            {
                // CX-505 aggregates histories that are behaviorally equivalent from the
                // current node onward. Omitted bookkeeping fields never participate in an
                // entry condition, action availability or ending classification in this
                // approved v1 bundle; path multiplicity is retained on the representative.
                if (source.currentNodeId == "aftermath" || source.currentNodeId == "ending")
                {
                    return ((int)source.route) + "|ending|" +
                           (int)StoryProgressionRules.ClassifyEnding(source);
                }

                bool secondRound = source.currentNodeId == "combat_two";
                int mana = secondRound ? (source.mana >= 25 ? 1 : 0) : source.mana;
                int wards = secondRound
                    ? (source.wards > 0 ? 1 : 0)
                    : Mathf.Min(source.wards, 2);
                int trust;
                if (source.currentNodeId == "prologue" ||
                    source.currentNodeId == "camp" ||
                    source.currentNodeId == "alliance" ||
                    source.currentNodeId == "flight" ||
                    source.currentNodeId == "herb_route" ||
                    source.currentNodeId == "corpse_signs" ||
                    source.currentNodeId == "rescue")
                {
                    trust = Mathf.Min(source.trust, 3);
                }
                else
                {
                    trust = Mathf.Min(source.trust, 1);
                }
                int receiptCount = includeReceipts && source.appliedStoryThreadReceipts != null
                    ? source.appliedStoryThreadReceipts.Count
                    : 0;
                return ((int)source.route) + "|" +
                       mana + "|" +
                       source.health + "|" +
                       wards + "|" +
                       trust + "|" +
                       Mathf.Min(source.exposure, 4) + "|" +
                       source.dragonHealth + "|" +
                       Flag(source.formationOpened) +
                       Flag(source.keyTrumpCardUsed) +
                       Flag(source.retreated) +
                       Flag(source.dragonDefeated) + "|" +
                       receiptCount;
            }

            private static string Flag(bool value)
            {
                return value ? "1" : "0";
            }

            private static void AddCount<TKey>(Dictionary<TKey, long> counts, TKey key, long value)
            {
                counts[key] = counts.TryGetValue(key, out long current) ? current + value : value;
            }
        }

        private sealed class WeightedState
        {
            public WeightedState(GameState state, long count)
            {
                State = state;
                Count = count;
            }

            public GameState State { get; }
            public long Count { get; set; }
        }

        private sealed class AuditResult
        {
            public long TotalPathCount;
            public int DistinctStateCount;
            public readonly Dictionary<RedMistEndingKind, long> OutcomePathCounts =
                new Dictionary<RedMistEndingKind, long>();
            public readonly Dictionary<string, long> TerminalStateCounts =
                new Dictionary<string, long>(StringComparer.Ordinal);
            public readonly HashSet<string> CoveredEdges = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> InjectedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
