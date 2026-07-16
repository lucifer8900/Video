using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Lingmai.RedMist.Tests
{
    public sealed class StoryCatalogBundleEquivalenceTests
    {
        private StoryBundle _bundle;

        [SetUp]
        public void LoadBundle()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath,
                "Story",
                "red-mist",
                StoryBundleLoader.BundleFileName);
            StoryBundleLoadResult result = StoryBundleLoader.LoadFromFile(path);
            Assert.IsTrue(result.Success, result.TechnicalMessage);
            _bundle = result.Bundle;
            StoryCatalog.Install(_bundle);
        }

        [Test]
        public void BundleCatalogMatchesEveryLegacyNodeChoiceAndTransition()
        {
            Assert.AreSame(_bundle.Nodes, StoryCatalog.Nodes,
                "Runtime StoryCatalog must expose the installed bundle, not a legacy fallback copy.");
            Assert.AreEqual(14, LegacyStoryCatalog.Nodes.Count);
            Assert.AreEqual(LegacyStoryCatalog.Nodes.Count, StoryCatalog.Nodes.Count);

            int choiceEdges = 0;
            int transitionEdges = 0;
            foreach (KeyValuePair<string, StoryNode> pair in LegacyStoryCatalog.Nodes)
            {
                StoryNode expected = pair.Value;
                StoryNode actual = StoryCatalog.Get(pair.Key);
                Assert.NotNull(actual, pair.Key);
                Assert.AreEqual(expected.title, actual.title, pair.Key + " title");
                Assert.AreEqual(expected.location, actual.location, pair.Key + " location");
                Assert.AreEqual(expected.speaker, actual.speaker, pair.Key + " speaker");
                Assert.AreEqual(expected.maleText, actual.maleText, pair.Key + " male text");
                Assert.AreEqual(expected.femaleText, actual.femaleText, pair.Key + " female text");
                Assert.AreEqual(expected.background, actual.background, pair.Key + " background");
                Assert.AreEqual(expected.portrait, actual.portrait, pair.Key + " portrait");
                Assert.AreEqual(expected.introMediaRef, actual.introMediaRef, pair.Key + " intro media");
                Assert.AreEqual(expected.kind, actual.kind, pair.Key + " kind");
                Assert.AreEqual(expected.estimatedMinutes, actual.estimatedMinutes, pair.Key + " duration");

                Assert.AreEqual(expected.choices.Count, actual.choices.Count, pair.Key + " choice count");
                for (int index = 0; index < expected.choices.Count; index++)
                {
                    ChoiceDefinition left = expected.choices[index];
                    ChoiceDefinition right = actual.choices[index];
                    Assert.AreEqual(left.label, right.label, pair.Key + " choice label " + index);
                    Assert.AreEqual(left.hint, right.hint, pair.Key + " choice hint " + index);
                    Assert.AreEqual(left.action, right.action, pair.Key + " choice action " + index);
                    Assert.AreEqual(left.nextNodeId, right.nextNodeId, pair.Key + " choice target " + index);
                    choiceEdges++;
                }

                CollectionAssert.AreEquivalent(
                    expected.transitions.Select(Edge),
                    actual.transitions.Select(Edge),
                    pair.Key + " runtime transitions");
                transitionEdges += actual.transitions.Count;
            }

            Assert.AreEqual(21, choiceEdges);
            Assert.AreEqual(25, transitionEdges);
        }

        [Test]
        public void EveryFixedRouteEdgeIsTraversedAndReachableEndingSetMatchesLegacy()
        {
            GraphResult legacy = Traverse(LegacyStoryCatalog.Nodes, "prologue");
            GraphResult bundled = Traverse(StoryCatalog.Nodes, _bundle.EntryNodeId);

            CollectionAssert.AreEquivalent(LegacyStoryCatalog.Nodes.Keys, legacy.ReachableNodes);
            CollectionAssert.AreEquivalent(StoryCatalog.Nodes.Keys, bundled.ReachableNodes);
            CollectionAssert.AreEquivalent(legacy.ChoiceEdges, bundled.ChoiceEdges);
            CollectionAssert.AreEquivalent(legacy.TransitionEdges, bundled.TransitionEdges);
            CollectionAssert.AreEquivalent(new[] { "ending" }, legacy.ReachableEndings);
            CollectionAssert.AreEquivalent(legacy.ReachableEndings, bundled.ReachableEndings);
        }

        private static string Edge(StoryTransitionDefinition transition) =>
            transition.triggerId + "->" + transition.nextNodeId;

        private static GraphResult Traverse(IReadOnlyDictionary<string, StoryNode> nodes, string entryNodeId)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal) { entryNodeId };
            var choices = new HashSet<string>(StringComparer.Ordinal);
            var transitions = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            pending.Enqueue(entryNodeId);
            while (pending.Count > 0)
            {
                string nodeId = pending.Dequeue();
                StoryNode node = nodes[nodeId];
                foreach (ChoiceDefinition choice in node.choices)
                {
                    choices.Add(nodeId + "|" + choice.action + "|" + choice.nextNodeId);
                    if (reachable.Add(choice.nextNodeId)) pending.Enqueue(choice.nextNodeId);
                }

                foreach (StoryTransitionDefinition transition in node.transitions)
                {
                    transitions.Add(nodeId + "|" + transition.triggerId + "|" + transition.nextNodeId);
                    if (reachable.Add(transition.nextNodeId)) pending.Enqueue(transition.nextNodeId);
                }
            }

            string[] endings = reachable
                .Where(id => nodes[id].kind == NodeKind.Ending)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            return new GraphResult(reachable, choices, transitions, endings);
        }

        private sealed class GraphResult
        {
            public GraphResult(
                HashSet<string> reachableNodes,
                HashSet<string> choiceEdges,
                HashSet<string> transitionEdges,
                string[] reachableEndings)
            {
                ReachableNodes = reachableNodes;
                ChoiceEdges = choiceEdges;
                TransitionEdges = transitionEdges;
                ReachableEndings = reachableEndings;
            }

            public HashSet<string> ReachableNodes { get; }
            public HashSet<string> ChoiceEdges { get; }
            public HashSet<string> TransitionEdges { get; }
            public string[] ReachableEndings { get; }
        }
    }
}
