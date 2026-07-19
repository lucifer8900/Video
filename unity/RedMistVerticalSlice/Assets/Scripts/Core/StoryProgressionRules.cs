using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lingmai.RedMist
{
    public enum RedMistEndingKind
    {
        Retreat,
        CostlyVictory,
        CautiousAlliance
    }

    public enum CombatCueTone
    {
        Danger,
        Jade,
        Gold,
        Retreat
    }

    public readonly struct CombatProgressionResult
    {
        public CombatProgressionResult(string triggerId, string cueId, CombatCueTone cueTone)
        {
            TriggerId = triggerId ?? string.Empty;
            CueId = cueId ?? string.Empty;
            CueTone = cueTone;
        }

        public string TriggerId { get; }
        public string CueId { get; }
        public CombatCueTone CueTone { get; }
    }

    /// <summary>
    /// Deterministic Red Mist state mutations shared by the playable runtime and CX-505.
    /// Presentation, audio, saving and node lookup remain the caller's responsibility.
    /// </summary>
    public static class StoryProgressionRules
    {
        private static readonly string[] ShenYanRoundOne =
        {
            "probe_blades", "ward_tail", "protect_ally", "retreat"
        };

        private static readonly string[] ChuMingqiRoundOne =
        {
            "moon_control", "protect_ally", "phoenix_flash", "retreat"
        };

        private static readonly string[] ShenYanRoundTwo =
        {
            "trump_blades", "formation_burst", "joint_strike", "retreat"
        };

        private static readonly string[] ChuMingqiRoundTwo =
        {
            "phoenix_ring", "joint_strike", "hold_line", "retreat"
        };

        public static void ApplyNarrativeChoice(GameState state, string action)
        {
            RequireState(state);
            switch (action)
            {
                case "to_camp":
                    break;
                case "gate_scout":
                    state.divineSense -= 8;
                    state.worldMinutes += 6;
                    state.ambushKnown = true;
                    state.discoveries.Add("水面逆流暴露的侧门伏击痕迹");
                    break;
                case "gate_companion":
                    state.trust += 1;
                    state.sectDuty += 1;
                    break;
                case "camp_retreat":
                    state.mana += 10;
                    state.wards = 1;
                    state.discoveries.Add("保留撤退法力");
                    break;
                case "camp_balanced":
                    state.wards += 2;
                    state.mana -= 5;
                    state.discoveries.Add("完整战前载荷");
                    break;
                case "camp_protect":
                    state.wards = Mathf.Max(0, state.wards - 1);
                    state.trust += 2;
                    state.sectDuty += 1;
                    break;
                case "ally_cautious":
                    state.trust += 1;
                    state.respect += 1;
                    break;
                case "ally_open":
                    state.trust += 2;
                    state.exposure += 1;
                    state.secretPreserved = false;
                    break;
                case "ally_refuse":
                    state.suspicion += 1;
                    break;
                case "rescue_careful":
                    state.wards = Mathf.Max(0, state.wards - 1);
                    state.worldMinutes += 18;
                    state.discipleRescued = true;
                    state.trust += 2;
                    state.sectDuty += 1;
                    break;
                case "rescue_rush":
                    state.health -= 18;
                    state.worldMinutes += 8;
                    state.discipleRescued = true;
                    state.allyWounded = true;
                    state.trust += 1;
                    break;
                case "rescue_skip":
                    state.worldMinutes += 4;
                    state.trust -= 2;
                    state.sectDuty += 2;
                    break;
                case "shijun_probe":
                    state.shijunTracked = state.ambushKnown;
                    state.respect += 1;
                    state.worldMinutes += 5;
                    break;
                case "shijun_threat":
                    state.exposure += 2;
                    state.suspicion += 1;
                    state.worldMinutes += 1;
                    break;
                case "shijun_trade":
                    state.herbs = Mathf.Max(0, state.herbs - 1);
                    state.formationOpened = true;
                    state.worldMinutes += 2;
                    break;
                case "meet_cautious":
                    state.trust += 1;
                    state.respect += 1;
                    break;
                case "meet_warn":
                    state.trust += 2;
                    state.respect += 1;
                    state.mana -= 4;
                    break;
                case "meet_wait":
                    state.suspicion += 2;
                    break;
                case "loot_rescue":
                    state.discipleRescued = true;
                    state.trust += 2;
                    state.sectDuty = Mathf.Max(0, state.sectDuty - 1);
                    state.herbs = Mathf.Max(0, state.herbs - 1);
                    break;
                case "loot_share":
                    state.trust += 2;
                    state.respect += 2;
                    state.secretPreserved = true;
                    break;
                case "loot_escape":
                    state.herbs += 2;
                    state.trust -= 1;
                    state.suspicion += 1;
                    break;
                default:
                    throw new ArgumentException("Unknown narrative action: " + action, nameof(action));
            }

            state.worldMinutes += 4;
            state.Clamp();
        }

        public static string ApplyFlight(GameState state, MinigameResult result)
        {
            RequireResult(result);
            RequireState(state);
            state.flightPerfect = result.perfect;
            state.mana += result.perfect ? 8 : result.success ? -5 : -18;
            state.exposure += result.success ? 0 : 2;
            state.worldMinutes += result.perfect ? 5 : result.success ? 8 : 12;
            if (!result.success)
            {
                state.allyWounded = true;
                state.discoveries.Add("狭天隘紧急坠落点");
            }
            state.Clamp();
            return "flight_" + ResultBranch(result);
        }

        public static string ApplyHerb(GameState state, MinigameResult result)
        {
            RequireResult(result);
            RequireState(state);
            state.herbPerfect = result.perfect;
            state.herbs += result.perfect ? 3 : result.success ? 2 : 1;
            state.worldMinutes += result.perfect ? 7 : 12;
            if (result.perfect) state.discoveries.Add("完整玉髓芝与引兽粉痕迹");
            if (!result.success) state.exposure += 2;
            state.Clamp();
            return "herb_" + ResultBranch(result);
        }

        public static string ApplyScan(GameState state, MinigameResult result)
        {
            RequireResult(result);
            RequireState(state);
            state.scanPerfect = result.perfect;
            state.ambushKnown = result.success;
            state.divineSense -= result.perfect ? 8 : result.success ? 16 : 25;
            state.exposure += result.perfect ? 0 : result.success ? 1 : 2;
            if (result.success) state.discoveries.Add("伏击者的火性蛛丝与撤离方向");
            state.Clamp();
            return "scan_" + ResultBranch(result);
        }

        public static string ApplyFormation(GameState state, MinigameResult result)
        {
            RequireResult(result);
            RequireState(state);
            state.formationOpened = result.success || state.formationOpened;
            state.mana -= result.perfect ? 3 : result.success ? 10 : 24;
            state.exposure += result.perfect ? 0 : result.success ? 1 : 2;
            state.worldMinutes += result.perfect ? 4 : 10;
            if (result.perfect) state.discoveries.Add("保留的青石撤退阵眼");
            if (!result.success)
            {
                state.health -= 12;
                state.suspicion += 2;
                state.allyWounded = true;
                state.discoveries.Add("强行破禁留下的反噬裂痕");
            }
            state.Clamp();
            return "formation_" + ResultBranch(result);
        }

        public static string ApplyFormationSpike(GameState state)
        {
            RequireState(state);
            if (!state.formationOpened)
                throw new InvalidOperationException("The formation spike bypass is not available.");
            state.worldMinutes += 2;
            state.respect += 1;
            state.discoveries.Add("石峻阵钉开启的无损通路");
            state.Clamp();
            return "formation_spike_bypass";
        }

        public static IReadOnlyList<string> GetAvailableCombatActions(GameState state, int round)
        {
            RequireState(state);
            string[] candidates = CombatCandidates(state.route, round);
            var result = new List<string>(candidates.Length);
            for (int index = 0; index < candidates.Length; index++)
            {
                if (IsCombatActionAvailable(state, candidates[index], round))
                    result.Add(candidates[index]);
            }
            return result.AsReadOnly();
        }

        public static bool IsCombatActionAvailable(GameState state, string action, int round)
        {
            RequireState(state);
            if (string.IsNullOrWhiteSpace(action)) return false;
            string[] candidates = CombatCandidates(state.route, round);
            bool belongsToRoute = false;
            for (int index = 0; index < candidates.Length; index++)
            {
                if (string.Equals(candidates[index], action, StringComparison.Ordinal))
                {
                    belongsToRoute = true;
                    break;
                }
            }
            if (!belongsToRoute) return false;

            switch (action)
            {
                case "ward_tail":
                    return state.wards > 0;
                case "trump_blades":
                case "phoenix_ring":
                    return state.mana >= 25;
                case "formation_burst":
                    return state.formationOpened && state.wards > 0;
                case "joint_strike":
                    return state.trust >= 1 &&
                           (state.route != PlayerRoute.ChuMingqi || state.formationOpened);
                default:
                    return true;
            }
        }

        public static CombatProgressionResult ApplyCombat(
            GameState state,
            string action,
            int round)
        {
            RequireState(state);
            if (!IsCombatActionAvailable(state, action, round))
                throw new InvalidOperationException("Combat action is unavailable: " + action);

            if (string.Equals(action, "retreat", StringComparison.Ordinal))
            {
                state.retreated = true;
                state.mana -= 12;
                state.worldMinutes += 15;
                state.Clamp();
                return new CombatProgressionResult(action, "撤退窗口开启", CombatCueTone.Retreat);
            }

            bool damageDragon = false;
            string cue;
            CombatCueTone tone = CombatCueTone.Danger;
            switch (action)
            {
                case "probe_blades":
                    state.mana -= 12;
                    state.divineSense -= 14;
                    damageDragon = true;
                    cue = "飞刃试鳞";
                    break;
                case "ward_tail":
                    state.wards--;
                    state.mana -= 8;
                    damageDragon = true;
                    cue = "符锁蛟尾";
                    break;
                case "protect_ally":
                    state.trust += 2;
                    state.mana -= 10;
                    state.allyWounded = false;
                    cue = "护送撤位";
                    tone = CombatCueTone.Jade;
                    break;
                case "moon_control":
                    state.mana -= 17;
                    state.divineSense -= 10;
                    damageDragon = true;
                    cue = "月轮镇泥";
                    break;
                case "phoenix_flash":
                    state.mana -= 22;
                    state.exposure += 2;
                    state.keyTrumpCardUsed = true;
                    damageDragon = true;
                    cue = "赤鸾初燃";
                    break;
                case "trump_blades":
                    state.mana -= 30;
                    state.exposure += 2;
                    state.keyTrumpCardUsed = true;
                    damageDragon = true;
                    cue = "金蜉连环";
                    break;
                case "formation_burst":
                    state.mana -= 16;
                    state.wards--;
                    damageDragon = true;
                    cue = "符阵断潮";
                    break;
                case "joint_strike":
                    state.mana -= 18;
                    state.trust += 1;
                    state.respect += 2;
                    damageDragon = true;
                    cue = "雾火合击";
                    tone = CombatCueTone.Gold;
                    break;
                case "phoenix_ring":
                    state.mana -= 34;
                    state.exposure += 4;
                    state.keyTrumpCardUsed = true;
                    state.secretPreserved = false;
                    damageDragon = true;
                    cue = "赤鸾焰环";
                    break;
                case "hold_line":
                    state.health -= 20;
                    state.trust += 2;
                    state.sectDuty = Mathf.Max(0, state.sectDuty - 1);
                    damageDragon = true;
                    cue = "孤身截蛟";
                    break;
                default:
                    throw new ArgumentException("Unknown combat action: " + action, nameof(action));
            }

            if (damageDragon) state.dragonHealth--;
            if (round == 1 && !damageDragon)
            {
                state.health -= 10;
                state.allyWounded = true;
            }
            if (round == 2 && state.dragonHealth > 0)
            {
                state.health -= 18;
                state.mana -= 10;
            }
            state.dragonDefeated = state.dragonHealth <= 0;
            state.worldMinutes += round == 1 ? 6 : 9;
            state.Clamp();
            return new CombatProgressionResult(action, cue, tone);
        }

        public static string ResultBranch(MinigameResult result)
        {
            RequireResult(result);
            return result.perfect ? "perfect" : result.success ? "success" : "failure";
        }

        public static RedMistEndingKind ClassifyEnding(GameState state)
        {
            RequireState(state);
            if (state.retreated) return RedMistEndingKind.Retreat;
            if (state.keyTrumpCardUsed || state.exposure >= 4 || state.health <= 35)
                return RedMistEndingKind.CostlyVictory;
            return RedMistEndingKind.CautiousAlliance;
        }

        private static string[] CombatCandidates(PlayerRoute route, int round)
        {
            if (route == PlayerRoute.None)
                throw new InvalidOperationException("A player route is required for combat.");
            if (round != 1 && round != 2)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (round == 1)
                return route == PlayerRoute.ShenYan ? ShenYanRoundOne : ChuMingqiRoundOne;
            return route == PlayerRoute.ShenYan ? ShenYanRoundTwo : ChuMingqiRoundTwo;
        }

        private static void RequireState(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Clamp();
        }

        private static void RequireResult(MinigameResult result)
        {
            if (result.perfect && !result.success)
                throw new ArgumentException("A perfect minigame result must also be successful.", nameof(result));
        }
    }
}
