using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Runtime story access point. The legacy catalog remains available for one
    /// compatibility release, but this facade never silently falls back to it.
    /// </summary>
    public static class StoryCatalog
    {
        private static readonly IReadOnlyDictionary<string, StoryNode> EmptyNodes =
            new Dictionary<string, StoryNode>(StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<string, StoryNpcResponse> EmptyNpcResponses =
            new Dictionary<string, StoryNpcResponse>(StringComparer.Ordinal);

        private static StoryBundle _bundle;
        private static StoryBundleLoadResult _lastLoadResult;

        public static string DefaultBundlePath => Path.Combine(
            Application.streamingAssetsPath,
            "Story",
            "red-mist",
            StoryBundleLoader.BundleFileName);

        public static StoryBundleIdentity Identity
        {
            get
            {
                EnsureInitialized();
                return _bundle?.Identity;
            }
        }

        public static string EntryNodeId
        {
            get
            {
                EnsureInitialized();
                return _bundle?.EntryNodeId;
            }
        }

        public static IReadOnlyDictionary<string, StoryNode> Nodes
        {
            get
            {
                EnsureInitialized();
                return _bundle?.Nodes ?? EmptyNodes;
            }
        }

        public static IReadOnlyDictionary<string, StoryNpcResponse> NpcResponses
        {
            get
            {
                EnsureInitialized();
                return _bundle?.NpcResponses ?? EmptyNpcResponses;
            }
        }

        public static StoryBundleLoadResult LastLoadResult
        {
            get
            {
                EnsureInitialized();
                return _lastLoadResult;
            }
        }

        public static StoryBundleLoadResult InitializeFromStreamingAssets()
        {
            StoryBundleLoadResult result = StoryBundleLoader.LoadFromFile(DefaultBundlePath);
            _lastLoadResult = result;
            _bundle = result.Success ? result.Bundle : null;
            return result;
        }

        public static void Install(StoryBundle bundle)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            _lastLoadResult = StoryBundleLoadResult.Succeeded(bundle);
        }

        public static StoryNode Get(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && Nodes.TryGetValue(id, out StoryNode node)
                ? node
                : null;
        }

        public static bool TryGetTransition(string nodeId, string triggerId, out string nextNodeId)
        {
            nextNodeId = null;
            StoryNode node = Get(nodeId);
            if (node == null || string.IsNullOrWhiteSpace(triggerId)) return false;
            foreach (StoryTransitionDefinition transition in node.transitions)
            {
                if (!string.Equals(transition.triggerId, triggerId, StringComparison.Ordinal)) continue;
                nextNodeId = transition.nextNodeId;
                return true;
            }

            return false;
        }

        public static bool TryGetNpcResponse(
            string nodeId,
            string responseId,
            out StoryNpcResponse response)
        {
            response = null;
            StoryNode node = Get(nodeId);
            if (node == null || string.IsNullOrWhiteSpace(responseId) ||
                !node.npcResponseRefs.Contains(responseId))
            {
                return false;
            }
            return NpcResponses.TryGetValue(responseId, out response);
        }

        private static void EnsureInitialized()
        {
            if (_bundle != null || _lastLoadResult != null) return;
            InitializeFromStreamingAssets();
        }
    }
}
