using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Central registry for the GPT Image authored production plates used by the vertical slice.
    /// Resource names intentionally live here so missing artwork fails loudly instead of silently
    /// falling back to procedural pixels.
    /// </summary>
    public static class GeneratedArtCatalog
    {
        public const string SanctuaryEntrance = "Generated/Scenes/scene_immortal_sanctuary_gate_v3";
        public const string HerbCourtyard = "Generated/Scenes/scene_immortal_herb_garden_v3";
        public const string SwordSealVault = "Generated/Scenes/scene_sword_seal_vault_v2";
        public const string CelestialStormRoute = "Generated/Scenes/scene_celestial_storm_route_v2";
        public const string CelestialFormationHall = "Generated/Scenes/scene_celestial_formation_hall_v3";
        public const string AllianceShenGuFirstFrame = "Generated/Scenes/firstframe_alliance_shen_gu_v1";
        public const string AllianceChuPetitionersFirstFrame = "Generated/Scenes/firstframe_alliance_chu_petitioners_v1";

        public const string ShenYanPortrait = "Generated/Characters/portrait_shen_yan_v2";
        public const string ChuMingqiPortrait = "Generated/Characters/portrait_chu_mingqi_v2";
        public const string BronzeWardenPortrait = "Generated/Characters/portrait_bronze_warden_v2";
        public const string CeladonScoutIdentity = "Generated/Characters/identity_celadon_scout_v3";
        public const string ExplorerShenYanFront = "Generated/Characters/explorer_shen_yan_front_v3";
        public const string ExplorerShenYanBack = "Generated/Characters/explorer_shen_yan_back_v3";
        public const string ExplorerChuMingqiFront = "Generated/Characters/explorer_chu_mingqi_front_v3";
        public const string ExplorerChuMingqiBack = "Generated/Characters/explorer_chu_mingqi_back_v3";

        public const string FlyingSword = "Generated/Minigame/flying_sword_v2";
        public const string SealBoulder = "Generated/Minigame/seal_boulder_v2";
        public const string ThornArch = "Generated/Minigame/thorn_arch_v2";
        public const string SpiritCrystal = "Generated/Minigame/spirit_crystal_v2";

        public const string LegacyEstablishing = "Art/red_mist_establishing";
        public const string LegacyWeatherStudy = "Art/red_mist_weather";
        public const string LegacyEnvironmentModules = "Art/red_mist_modules";
        public const string LegacyFlightSky = "Art/flight_sky";
        public const string LegacyFlightSkyV2 = "Art/flight_sky-v2";
        public const string LegacyCavernModules = "Art/dragon_cavern";
        public const string LegacyCavern = "Art/dragon_cavern-v2";
        public const string LegacyDragonAttack = "Art/dragon_attack-v1";
        public const string LegacyShenYan = "Art/shen_yan";
        public const string LegacyChuMingqi = "Art/chu_mingqi";
        public const string LegacyShiJun = "Art/shi_jun-v1";

        public static string SceneForNode(string nodeId)
        {
            return SceneForNode(nodeId, PlayerRoute.None);
        }

        public static string SceneForNode(string nodeId, PlayerRoute route)
        {
            switch (nodeId)
            {
                case "prologue":
                    return SanctuaryEntrance;
                case "camp":
                    return SanctuaryEntrance;
                case "alliance":
                    if (route == PlayerRoute.ShenYan) return AllianceShenGuFirstFrame;
                    if (route == PlayerRoute.ChuMingqi) return AllianceChuPetitionersFirstFrame;
                    return CelestialStormRoute;
                case "flight":
                    return CelestialStormRoute;
                case "herb_route":
                case "corpse_signs":
                    return HerbCourtyard;
                case "rescue":
                    return HerbCourtyard;
                case "shijun":
                    return SanctuaryEntrance;
                case "formation":
                    return CelestialFormationHall;
                case "underground":
                    return SwordSealVault;
                case "combat_one":
                    return SwordSealVault;
                case "combat_two":
                case "aftermath":
                    return SwordSealVault;
                default:
                    return SanctuaryEntrance;
            }
        }

        public static Rect SceneRegionForNode(string nodeId)
        {
            // Story nodes only use complete cinematic plates. Production boards are displayed
            // uncropped in the in-game sanctuary archive instead of being stretched as scenery.
            return new Rect(0f, 0f, 1f, 1f);
        }

        public const int ReferenceBoardCount = 3;

        public static string ReferenceBoardAt(int index)
        {
            switch ((index % ReferenceBoardCount + ReferenceBoardCount) % ReferenceBoardCount)
            {
                case 0: return LegacyWeatherStudy;
                case 1: return LegacyEnvironmentModules;
                default: return LegacyCavernModules;
            }
        }

        public static string ReferenceBoardCaptionAt(int index)
        {
            switch ((index % ReferenceBoardCount + ReferenceBoardCount) % ReferenceBoardCount)
            {
                case 0: return "赤雾天候与光色记录";
                case 1: return "秘苑建筑与环境构件图";
                default: return "地窟结构与封印遗迹图";
            }
        }

        public static string PortraitForNode(string nodeId, PlayerRoute route)
        {
            if (nodeId == "shijun") return LegacyShiJun;
            if (nodeId == "formation") return BronzeWardenPortrait;
            if (nodeId == "prologue" || nodeId == "camp")
                return route == PlayerRoute.ChuMingqi ? LegacyChuMingqi : LegacyShenYan;
            return route == PlayerRoute.ChuMingqi ? ChuMingqiPortrait : ShenYanPortrait;
        }

        public static Rect PortraitRegionForNode(string nodeId)
        {
            if (nodeId == "prologue" || nodeId == "camp" || nodeId == "shijun")
                return new Rect(0f, 0.20f, 0.34f, 0.78f);
            return new Rect(0.06f, 0.30f, 0.88f, 0.66f);
        }

        public static Texture2D LoadRequired(string resourcePath)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) Debug.LogError("RED_MIST_GENERATED_ART_MISSING path=" + resourcePath);
            return texture;
        }
    }
}
