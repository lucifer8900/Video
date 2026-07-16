#nullable disable
// Unity 2022 serialization model linked into nullable-enabled .NET equivalence tests.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lingmai.RedMist
{
    [Serializable]
    public sealed class StoryRelationshipState
    {
        public string targetRef = "";
        public string field = "";
        public double value;

        public StoryRelationshipState()
        {
        }

        public StoryRelationshipState(string targetRef, string field, double value)
        {
            this.targetRef = targetRef ?? "";
            this.field = field ?? "";
            this.value = value;
        }
    }

    public enum PlayerRoute
    {
        None,
        ShenYan,
        ChuMingqi
    }

    public enum NodeKind
    {
        Narrative,
        Flight,
        DivineSense,
        HerbGathering,
        Formation,
        CombatOne,
        CombatTwo,
        Ending
    }

    [Serializable]
    public sealed class GameState
    {
        public PlayerRoute route;
        public string currentNodeId = "route_select";
        public int worldMinutes;
        public int mana = 100;
        public int divineSense = 100;
        public int health = 100;
        public int herbs;
        public int wards = 2;
        public int trust;
        public int respect;
        public int suspicion;
        public int exposure;
        public int sectDuty;
        public int dragonHealth = 3;
        public bool ambushKnown;
        public bool discipleRescued;
        public bool shijunTracked;
        public bool allyWounded;
        public bool formationOpened;
        public bool keyTrumpCardUsed;
        public bool retreated;
        public bool dragonDefeated;
        public bool secretPreserved = true;
        public bool flightPerfect;
        public bool scanPerfect;
        public bool herbPerfect;
        public bool reducedMotion;
        public float textSpeed = 1f;
        public float masterVolume = 0.75f;
        public string pendingCinematicId = "";
        public float cinematicCheckpointSeconds;
        public bool subtitlesEnabled = true;
        public float cinematicVolume = 0.75f;
        public List<string> history = new List<string>();
        public List<string> discoveries = new List<string>();
        public List<string> watchedCinematics = new List<string>();
        public List<string> choiceHistory = new List<string>();
        public List<StoryRelationshipState> storyRelationships = new List<StoryRelationshipState>();
        public List<string> storyClueIds = new List<string>();
        public List<string> storyBranchIds = new List<string>();
        public List<string> appliedStoryThreadReceipts = new List<string>();

        public string RouteName => route == PlayerRoute.ShenYan ? "沈砚线" : route == PlayerRoute.ChuMingqi ? "楚明绮线" : "未选择";

        public bool HasStoryClue(string clueId)
        {
            return storyClueIds != null && storyClueIds.Contains(clueId);
        }

        public bool IsStoryBranchUnlocked(string branchId)
        {
            return storyBranchIds != null && storyBranchIds.Contains(branchId);
        }

        public double GetStoryRelationship(string targetRef, string field)
        {
            if (storyRelationships == null) return 0d;
            for (int index = 0; index < storyRelationships.Count; index++)
            {
                StoryRelationshipState relationship = storyRelationships[index];
                if (relationship != null &&
                    string.Equals(relationship.targetRef, targetRef, StringComparison.Ordinal) &&
                    string.Equals(relationship.field, field, StringComparison.Ordinal))
                {
                    return relationship.value;
                }
            }
            return 0d;
        }

        public void Clamp()
        {
            mana = Mathf.Clamp(mana, 0, 100);
            divineSense = Mathf.Clamp(divineSense, 0, 100);
            health = Mathf.Clamp(health, 0, 100);
            trust = Mathf.Clamp(trust, -5, 10);
            respect = Mathf.Clamp(respect, -5, 10);
            suspicion = Mathf.Clamp(suspicion, 0, 10);
            exposure = Mathf.Clamp(exposure, 0, 10);
            sectDuty = Mathf.Clamp(sectDuty, 0, 10);
            dragonHealth = Mathf.Clamp(dragonHealth, 0, 3);
            cinematicCheckpointSeconds = Mathf.Max(0f, cinematicCheckpointSeconds);
            cinematicVolume = Mathf.Clamp01(cinematicVolume);
            currentNodeId ??= "route_select";
            pendingCinematicId ??= "";
            history ??= new List<string>();
            discoveries ??= new List<string>();
            watchedCinematics ??= new List<string>();
            choiceHistory ??= new List<string>();
            storyRelationships ??= new List<StoryRelationshipState>();
            storyClueIds ??= new List<string>();
            storyBranchIds ??= new List<string>();
            appliedStoryThreadReceipts ??= new List<string>();
        }
    }

    public sealed class ChoiceDefinition
    {
        public readonly string label;
        public readonly string action;
        public readonly string hint;
        public readonly string nextNodeId;

        public ChoiceDefinition(string label, string action, string hint = "", string nextNodeId = "")
        {
            this.label = label;
            this.action = action;
            this.hint = hint;
            this.nextNodeId = nextNodeId;
        }
    }

    public sealed class StoryTransitionDefinition
    {
        public readonly string triggerId;
        public readonly string nextNodeId;

        public StoryTransitionDefinition(string triggerId, string nextNodeId)
        {
            this.triggerId = triggerId;
            this.nextNodeId = nextNodeId;
        }
    }

    public sealed class StoryVoiceIntent
    {
        public StoryVoiceIntent(
            string id,
            string displayText,
            IReadOnlyList<string> keywordPhrases,
            string toneText,
            double minimumConfidence,
            string npcResponseRef,
            string fallbackNpcResponseRef)
        {
            Id = id ?? string.Empty;
            DisplayText = displayText ?? string.Empty;
            KeywordPhrases = new List<string>(keywordPhrases ?? Array.Empty<string>()).AsReadOnly();
            ToneText = toneText ?? string.Empty;
            MinimumConfidence = minimumConfidence;
            NpcResponseRef = npcResponseRef ?? string.Empty;
            FallbackNpcResponseRef = fallbackNpcResponseRef ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayText { get; }
        public IReadOnlyList<string> KeywordPhrases { get; }
        public string ToneText { get; }
        public double MinimumConfidence { get; }
        public string NpcResponseRef { get; }
        public string FallbackNpcResponseRef { get; }
    }

    public enum StoryInvalidInputKind
    {
        Abuse,
        Irrelevant,
        TooLong,
        Silence,
        LowConfidence
    }

    public sealed class StoryNpcResponse
    {
        public StoryNpcResponse(
            string id,
            string text,
            string emotion,
            string lipSyncMediaRef,
            string audioMediaRef,
            string fallbackMediaRef)
        {
            Id = id ?? string.Empty;
            Text = text ?? string.Empty;
            Emotion = emotion ?? string.Empty;
            LipSyncMediaRef = lipSyncMediaRef ?? string.Empty;
            AudioMediaRef = audioMediaRef ?? string.Empty;
            FallbackMediaRef = fallbackMediaRef ?? string.Empty;
        }

        public string Id { get; }
        public string Text { get; }
        public string Emotion { get; }
        public string LipSyncMediaRef { get; }
        public string AudioMediaRef { get; }
        public string FallbackMediaRef { get; }
    }

    public sealed class StoryNode
    {
        public string id;
        public string title;
        public string location;
        public string speaker;
        public string maleText;
        public string femaleText;
        public string background;
        public string portrait;
        public string introMediaRef = "";
        public string fallbackMediaRef = "";
        public string offlineFallbackNodeId = "";
        public NodeKind kind;
        public int estimatedMinutes;
        public readonly List<ChoiceDefinition> choices = new List<ChoiceDefinition>();
        public readonly List<StoryTransitionDefinition> transitions = new List<StoryTransitionDefinition>();
        public readonly List<string> voiceIntentRefs = new List<string>();
        public readonly List<string> npcResponseRefs = new List<string>();
        public readonly List<string> injectionPoints = new List<string>();
        public readonly Dictionary<StoryInvalidInputKind, string> invalidInputRules =
            new Dictionary<StoryInvalidInputKind, string>();

        public string TextFor(PlayerRoute route)
        {
            return route == PlayerRoute.ChuMingqi ? femaleText : maleText;
        }
    }

    [Serializable]
    public sealed class SaveEnvelope
    {
        public const int CurrentVersion = 3;

        // Zero is intentional: JsonUtility preserves field initializers for missing JSON.
        // Decode must therefore require an explicit version instead of silently upgrading it.
        public int version;
        public string savedAtUtc;
        public string storyBundleVersion = "";
        public string storyBundleContentHash = "";
        public GameState state;
    }

    public readonly struct MinigameResult
    {
        public readonly bool success;
        public readonly bool perfect;
        public readonly int score;
        public readonly string summary;

        public MinigameResult(bool success, bool perfect, int score, string summary)
        {
            this.success = success;
            this.perfect = perfect;
            this.score = score;
            this.summary = summary;
        }
    }
}
