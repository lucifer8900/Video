using System;
using System.Collections.Generic;
using System.IO;
using Lingmai.RedMist;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lingmai.Editor
{
    public static class VerticalSliceBuilder
    {
        private const string ScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Vertical Slice/Generate Project Assets")]
        public static void GenerateProjectAssets()
        {
            RuntimeCharacterAssetBuilder.Prepare();
            ValidateContent();
            Directory.CreateDirectory("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject runtime = new GameObject("RedMistVerticalSlice");
            runtime.AddComponent<VerticalSliceGame>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Lingmai Yujin Studio";
            PlayerSettings.productName = "灵脉余烬：赤雾秘苑";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Low);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("RED_MIST_GENERATION_OK scene=" + ScenePath + " nodes=" + StoryCatalog.Nodes.Count);
        }

        [MenuItem("Vertical Slice/Validate Content")]
        public static void ValidateContent()
        {
            StoryBundleLoadResult bundleResult = StoryCatalog.InitializeFromStreamingAssets();
            if (!bundleResult.Success)
            {
                throw new InvalidDataException(
                    bundleResult.UserMessage + " " + bundleResult.TechnicalMessage);
            }

            IReadOnlyDictionary<string, StoryNode> nodes = StoryCatalog.Nodes;
            string[] required =
            {
                "prologue", "camp", "alliance", "flight", "herb_route", "corpse_signs",
                "rescue", "shijun", "formation", "underground", "combat_one", "combat_two",
                "aftermath", "ending"
            };
            foreach (string id in required)
            {
                if (!nodes.TryGetValue(id, out StoryNode node)) throw new InvalidOperationException("Missing story node: " + id);
                if (string.IsNullOrWhiteSpace(node.maleText) || string.IsNullOrWhiteSpace(node.femaleText))
                    throw new InvalidOperationException("Both routes require authored text: " + id);
                if (node.estimatedMinutes <= 0) throw new InvalidOperationException("Missing duration estimate: " + id);
            }
            if (nodes.Count != required.Length) throw new InvalidOperationException("Unexpected story node count: " + nodes.Count);
            if (nodes["ending"].kind != NodeKind.Ending) throw new InvalidOperationException("Ending node kind is invalid.");

            string[] requiredArt =
            {
                "red_mist_establishing", "red_mist_weather", "red_mist_modules",
                "flight_sky", "flight_sky-v2", "dragon_cavern", "dragon_cavern-v2",
                "dragon_attack-v1", "shi_jun-v1", "shen_yan", "chu_mingqi"
            };
            foreach (string art in requiredArt)
            {
                if (Resources.Load<Texture2D>("Art/" + art) == null) throw new InvalidOperationException("Missing required art: " + art);
            }

            string[] requiredGeneratedArt =
            {
                GeneratedArtCatalog.SanctuaryEntrance,
                GeneratedArtCatalog.HerbCourtyard,
                GeneratedArtCatalog.SwordSealVault,
                GeneratedArtCatalog.CelestialStormRoute,
                GeneratedArtCatalog.CelestialFormationHall,
                GeneratedArtCatalog.AllianceShenGuFirstFrame,
                GeneratedArtCatalog.AllianceChuPetitionersFirstFrame,
                GeneratedArtCatalog.ShenYanPortrait,
                GeneratedArtCatalog.ChuMingqiPortrait,
                GeneratedArtCatalog.BronzeWardenPortrait,
                GeneratedArtCatalog.CeladonScoutIdentity,
                GeneratedArtCatalog.ExplorerShenYanFront,
                GeneratedArtCatalog.ExplorerShenYanBack,
                GeneratedArtCatalog.ExplorerChuMingqiFront,
                GeneratedArtCatalog.ExplorerChuMingqiBack,
                GeneratedArtCatalog.FlyingSword,
                GeneratedArtCatalog.SealBoulder,
                GeneratedArtCatalog.ThornArch,
                GeneratedArtCatalog.SpiritCrystal
            };
            foreach (string path in requiredGeneratedArt)
            {
                if (Resources.Load<Texture2D>(path) == null)
                    throw new InvalidOperationException("Missing GPT Image production art: " + path);
            }

            string[] requiredModels =
            {
                "Characters/PeopleSansPeople/PSP_Person",
                "Characters/UnityStandard/DefaultFemale",
                "Environment/Models/pine_sapling_small/pine_sapling_small_1k",
                "Environment/Models/rock_moss_set_02/rock_moss_set_02_1k",
                "Environment/Models/fern_02/fern_02_1k",
                "Environment/Models/shrub_02/shrub_02_1k",
                "Environment/Models/island_tree_01/island_tree_01_1k"
            };
            foreach (string path in requiredModels)
            {
                if (Resources.Load<GameObject>(path) == null) throw new InvalidOperationException("Missing required 3D model: " + path);
            }

            string[] requiredShaders =
            {
                "Shaders/NaturalPBR", "Shaders/FoliageCutout", "Shaders/SanctuaryWater", "Shaders/SanctuaryParticle",
                "Shaders/CinematicCharacterBillboard", "Shaders/CinematicVistaBlend"
            };
            foreach (string path in requiredShaders)
            {
                if (Resources.Load<Shader>(path) == null) throw new InvalidOperationException("Missing required shader: " + path);
            }

            string[] requiredFmvCues =
            {
                CinematicCatalog.GateArrival,
                CinematicCatalog.CelestialFlight,
                CinematicCatalog.HerbCourtyard,
                CinematicCatalog.SwordVault
            };
            const long minimumFmvBytes = 1024L * 1024L;
            foreach (string cueId in requiredFmvCues)
            {
                if (!CinematicCatalog.TryGetCue(cueId, out CinematicCueDefinition cue))
                    throw new InvalidOperationException("Missing required local FMV catalog cue: " + cueId);

                string fileName = cue.FileName;
                string fullPath = Path.Combine(Application.streamingAssetsPath, "Media", fileName);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("Missing required local FMV: " + fileName);

                long length = new FileInfo(fullPath).Length;
                if (length < minimumFmvBytes)
                    throw new InvalidOperationException("Required local FMV is unexpectedly small: " + fileName + " bytes=" + length);
            }

            Debug.Log("RED_MIST_CONTENT_VALID nodes=" + nodes.Count +
                      " bundleVersion=" + StoryCatalog.Identity.Version +
                      " bundleHash=" + StoryCatalog.Identity.ContentHash + " art=" + requiredArt.Length +
                      " generatedArt=" + requiredGeneratedArt.Length +
                      " models=" + requiredModels.Length + " shaders=" + requiredShaders.Length +
                      " fmv=" + requiredFmvCues.Length);
        }

        [MenuItem("Vertical Slice/Build Windows IL2CPP")]
        public static void BuildWindows()
        {
            GenerateProjectAssets();
            Directory.CreateDirectory("Builds/Windows");
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/LingmaiYujin-RedMist.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
            Debug.Log("RED_MIST_BUILD_OK size=" + report.summary.totalSize + " path=" + options.locationPathName);
        }

        [MenuItem("Vertical Slice/Build Windows to E Fanren")]
        public static void BuildWindowsToFanren()
        {
            GenerateProjectAssets();
            string outputDirectory = @"E:\fanren\Builds\RedMistSanctuaryPreview";
            Directory.CreateDirectory(outputDirectory);
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outputDirectory, "LingmaiYujin-RedMist.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build to E failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
            Debug.Log("RED_MIST_E_BUILD_OK size=" + report.summary.totalSize + " path=" + options.locationPathName);
        }
    }
}
