using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Video;

namespace Lingmai.RedMist.Cx504
{
    internal static class AddressablesPatchExperimentRunner
    {
        private const string GeneratedRoot = "Assets/Cx504Generated/Experiment";
        private const string SettingsFolder = GeneratedRoot + "/Settings";
        private const string ContentFolder = GeneratedRoot + "/Content";
        private const string TargetVideoAsset = ContentFolder + "/target.mp4";
        private const string ReplacementProbeAsset = ContentFolder + "/replacement-probe.mp4";
        private const string ExpectedVideoGroup = "chapter-red-mist-video-remote";
        private const string UpdateCatalogJsonRelativePath =
            "remote/StandaloneWindows64/catalog_cx504.addressables.v1.json";
        private const string UpdateCatalogHashRelativePath =
            "remote/StandaloneWindows64/catalog_cx504.addressables.v1.hash";

        public static PatchExperimentReport Execute(PatchExperimentOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException(
                    "CX-504 requires -buildTarget StandaloneWindows64; active target was " +
                    EditorUserBuildSettings.activeBuildTarget + ".");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string planPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                "..",
                "..",
                "content",
                "packaging",
                "cx504-addressables-pack-plan.json"));
            AddressablesContentPackPlan plan = AddressablesContentPackPlan.Load(planPath);
            string[] planErrors = plan.Validate();
            if (planErrors.Length > 0)
                throw new InvalidDataException("CX-504 plan failed validation: " + string.Join(",", planErrors));

            Directory.CreateDirectory(options.OutputRoot);
            string publishRoot = Path.Combine(options.OutputRoot, "publish");
            string evidenceRoot = Path.Combine(options.OutputRoot, "evidence");
            string immutableStatePath = Path.Combine(evidenceRoot, "baseline-content-state.bin");
            string startedAt = DateTimeOffset.UtcNow.ToString("O");
            string defaultConfigFolder = AddressableAssetSettingsDefaultObject.kDefaultConfigFolder;
            string defaultConfigFolderFullPath = AssetPathToFullPath(projectRoot, defaultConfigFolder);
            string defaultObjectAssetPath = defaultConfigFolder + "/DefaultObject.asset";
            bool defaultConfigFolderExistedBefore = Directory.Exists(defaultConfigFolderFullPath);
            bool defaultObjectFileExistedBefore = File.Exists(
                AssetPathToFullPath(projectRoot, defaultObjectAssetPath));
            AddressableAssetSettingsDefaultObject previousDefaultObject;
            bool configObjectExistedBefore = EditorBuildSettings.TryGetConfigObject(
                AddressableAssetSettingsDefaultObject.kDefaultConfigObjectName,
                out previousDefaultObject);
            AddressableAssetSettings previousSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (!Cx504DefaultSettingsCleanupPolicy.CanStartExperiment(
                    configObjectExistedBefore,
                    defaultObjectFileExistedBefore,
                    previousSettings != null))
                throw new InvalidOperationException(
                    "CX-504 refuses to replace a pre-existing Addressables DefaultObject whose settings cannot be loaded.");
            AddressableAssetSettings experimentSettings = null;

            try
            {
                DeleteGeneratedAssets();
                StagingResult staging = PrepareStaging(
                    projectRoot,
                    options.BaselineVideoPath,
                    options.ReplacementVideoPath);
                PatchVideoInput baselineInput = DescribeInput(
                    options.BaselineVideoPath,
                    staging.BaselineDurationMilliseconds,
                    AddressablesContentPackBuilder.StableAddress("chapter_red_mist_videos", TargetVideoAsset));
                PatchVideoInput replacementInput = DescribeInput(
                    options.ReplacementVideoPath,
                    staging.ReplacementDurationMilliseconds,
                    baselineInput.logicalAddress);

                experimentSettings = AddressablesContentPackBuilder.CreateSettings(
                    plan,
                    SettingsFolder,
                    publishRoot,
                    staging.Assignments);
                experimentSettings.OverridePlayerVersion = "cx504.addressables.v1";
                EditorUtility.SetDirty(experimentSettings);
                AssetDatabase.SaveAssets();
                if (!(experimentSettings.ActivePlayerDataBuilder is BuildScriptPackedMode))
                    throw new InvalidOperationException("CX-504 requires the Addressables Packed Mode data builder.");

                Directory.CreateDirectory(AssetPathToFullPath(
                    projectRoot,
                    defaultConfigFolder));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AddressableAssetSettingsDefaultObject.Settings = experimentSettings;
                var baselineTimer = Stopwatch.StartNew();
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult baselineResult);
                baselineTimer.Stop();
                RequireBuildResult(baselineResult, expectUpdate: false, "baseline");
                string baselineStatePath = RequireExistingPath(
                    baselineResult.ContentStateFilePath,
                    "CX-504 baseline content state was not generated.");
                Directory.CreateDirectory(evidenceRoot);
                File.Copy(baselineStatePath, immutableStatePath, false);
                if (ContentUpdateScript.LoadContentState(immutableStatePath) == null)
                    throw new InvalidDataException("CX-504 immutable baseline content state is invalid.");

                Dictionary<string, PublishFileSnapshot> baselineSnapshot = CapturePublishSnapshot(publishRoot);
                if (baselineSnapshot.Count == 0)
                    throw new InvalidDataException("CX-504 baseline produced no player-deliverable files.");
                long baselineTemporaryDiskBytes = DirectorySize(options.OutputRoot);
                var baselineFiles = baselineSnapshot.Values
                    .Select(value => value.ToRecord())
                    .Concat(new[]
                    {
                        PublishFileSnapshot.FromFile(
                            immutableStatePath,
                            "evidence/baseline-content-state.bin",
                            "content_state").ToRecord()
                    })
                    .OrderBy(file => file.relativePath, StringComparer.Ordinal)
                    .ToArray();

                CopyFile(options.ReplacementVideoPath, projectRoot, TargetVideoAsset);
                AssetDatabase.ImportAsset(
                    TargetVideoAsset,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                long importedReplacementDuration = ReadVideoDurationMilliseconds(TargetVideoAsset);
                if (importedReplacementDuration != replacementInput.durationMilliseconds)
                    throw new InvalidDataException("CX-504 replacement duration changed after same-GUID import.");

                var updateTimer = Stopwatch.StartNew();
                AddressablesPlayerBuildResult updateResult =
                    ContentUpdateScript.BuildContentUpdate(experimentSettings, immutableStatePath);
                updateTimer.Stop();
                RequireBuildResult(updateResult, expectUpdate: true, "content update");

                Dictionary<string, PublishFileSnapshot> updatedSnapshot = CapturePublishSnapshot(publishRoot);
                string[] deletedBaselineFiles = baselineSnapshot.Values
                    .Where(file => !updatedSnapshot.ContainsKey(file.FullPath))
                    .Select(file => file.RelativePath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                if (deletedBaselineFiles.Length > 0)
                    throw new InvalidDataException(
                        "CX-504 content update deleted baseline deliverables: " +
                        string.Join(",", deletedBaselineFiles));
                PublishFileSnapshot[] patchFiles = updatedSnapshot.Values
                    .Where(file => !baselineSnapshot.TryGetValue(file.FullPath, out PublishFileSnapshot previous) ||
                                   previous.Length != file.Length ||
                                   !string.Equals(previous.ContentHash, file.ContentHash, StringComparison.Ordinal))
                    .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                    .ToArray();
                if (patchFiles.Length == 0)
                    throw new InvalidDataException("CX-504 content update produced no changed player-deliverable files.");
                RequireSinglePatchFile(
                    patchFiles,
                    UpdateCatalogJsonRelativePath,
                    "catalog",
                    "CX-504 content update catalog JSON must be part of the patch payload.");
                RequireSinglePatchFile(
                    patchFiles,
                    UpdateCatalogHashRelativePath,
                    "catalog_hash",
                    "CX-504 content update catalog hash must be part of the patch payload.");

                ChangedBundleEvidence changedBundles = ResolveChangedBundles(updateResult, patchFiles);
                bool unchangedBundleHashesPreserved = baselineSnapshot.Values
                    .Where(file => file.Kind == "bundle")
                    .All(file => updatedSnapshot.TryGetValue(file.FullPath, out PublishFileSnapshot current) &&
                                 current.Length == file.Length &&
                                 string.Equals(current.ContentHash, file.ContentHash, StringComparison.Ordinal));
                long patchPayloadBytes = patchFiles.Sum(file => file.Length);
                long maximumObservedBundleBytes = baselineSnapshot.Values
                    .Concat(updatedSnapshot.Values)
                    .Where(file => file.Kind == "bundle")
                    .Select(file => file.Length)
                    .DefaultIfEmpty(0L)
                    .Max();
                long changedAssetBytes = Math.Max(baselineInput.length, replacementInput.length);
                PatchEvaluation evaluation = AddressablesPatchEvaluator.Evaluate(
                    changedAssetBytes,
                    patchPayloadBytes,
                    plan.PatchThreshold.ChangedAssetMultiplier,
                    plan.PatchThreshold.FixedOverheadBytes,
                    changedBundles.Groups,
                    ExpectedVideoGroup,
                    changedBundles.Addresses,
                    baselineInput.logicalAddress,
                    unchangedBundleHashesPreserved,
                    maximumObservedBundleBytes,
                    plan.MaximumBundleBytes);

                PatchFileRecord[] updateFiles = patchFiles
                    .Select(file => file.ToRecord())
                    .OrderBy(file => file.relativePath, StringComparer.Ordinal)
                    .ToArray();
                return new PatchExperimentReport
                {
                    schemaVersion = "1.0.0",
                    reportId = "cx504.patch." + replacementInput.contentHash.Substring("sha256:".Length, 16),
                    planId = plan.PlanId,
                    mode = "experiment_only",
                    runtimeMigration = false,
                    steamPipeVerified = false,
                    productionReadiness = "not_evaluated",
                    authority = "addressables_payload_measurement_not_steam_download_authority",
                    buildTarget = BuildTarget.StandaloneWindows64.ToString(),
                    unityVersion = Application.unityVersion,
                    addressablesVersion = "1.22.3",
                    startedAtUtc = startedAt,
                    completedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    baselineInput = baselineInput,
                    replacementInput = replacementInput,
                    baselineBuild = BuildInventory(
                        baselineTimer.ElapsedMilliseconds,
                        baselineTemporaryDiskBytes,
                        baselineFiles),
                    updateBuild = BuildInventory(
                        updateTimer.ElapsedMilliseconds,
                        DirectorySize(options.OutputRoot),
                        updateFiles),
                    patchPayloadBytes = evaluation.PatchPayloadBytes,
                    amplificationRatio = evaluation.AmplificationRatio,
                    changedAssetBytes = evaluation.ChangedAssetBytes,
                    maximumAllowedPatchBytes = evaluation.MaximumAllowedPatchBytes,
                    maximumBundleBytes = plan.MaximumBundleBytes,
                    maximumObservedBundleBytes = evaluation.MaximumObservedBundleBytes,
                    expectedChangedBundleGroup = ExpectedVideoGroup,
                    changedBundleGroups = evaluation.ChangedBundleGroups,
                    expectedChangedBundleAddress = baselineInput.logicalAddress,
                    changedBundleAddresses = evaluation.ChangedBundleAddresses,
                    unexpectedChangedBundles = evaluation.UnexpectedChangedBundles,
                    unchangedBundleHashesPreserved = evaluation.UnchangedBundleHashesPreserved,
                    verdict = evaluation.Passed ? "passed" : "failed",
                    limitations = new[]
                    {
                        "not_runtime_migration",
                        "not_steam_download_measurement",
                        "language_and_asr_are_fixture_manifests"
                    },
                    contentHash = string.Empty
                };
            }
            finally
            {
                try
                {
                    if (experimentSettings != null &&
                        ReferenceEquals(AddressableAssetSettingsDefaultObject.Settings, experimentSettings))
                        AddressableAssetSettingsDefaultObject.Settings = previousSettings;
                    AssetDatabase.SaveAssets();
                }
                finally
                {
                    DeleteGeneratedAssets();
                    if (previousSettings == null &&
                        Cx504DefaultSettingsCleanupPolicy.ShouldDeleteDefaultObject(
                            configObjectExistedBefore,
                            defaultObjectFileExistedBefore))
                    {
                        EditorBuildSettings.RemoveConfigObject(
                            AddressableAssetSettingsDefaultObject.kDefaultConfigObjectName);
                        AssetDatabase.DeleteAsset(defaultObjectAssetPath);
                    }

                    bool defaultFolderIsEmpty = Directory.Exists(defaultConfigFolderFullPath) &&
                                                !Directory.EnumerateFileSystemEntries(defaultConfigFolderFullPath).Any();
                    if (previousSettings == null &&
                        Cx504DefaultSettingsCleanupPolicy.CanDeleteDefaultFolder(
                            defaultConfigFolderExistedBefore,
                            configObjectExistedBefore,
                            defaultObjectFileExistedBefore,
                            defaultFolderIsEmpty))
                    {
                        AssetDatabase.DeleteAsset(defaultConfigFolder);
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
        }

        private static StagingResult PrepareStaging(
            string projectRoot,
            string baselineVideo,
            string replacementVideo)
        {
            Directory.CreateDirectory(AssetPathToFullPath(projectRoot, ContentFolder));
            CopyFile(baselineVideo, projectRoot, TargetVideoAsset);
            CopyFile(baselineVideo, projectRoot, ContentFolder + "/control-01.mp4");
            CopyFile(baselineVideo, projectRoot, ContentFolder + "/control-02.mp4");
            CopyFile(replacementVideo, projectRoot, ReplacementProbeAsset);
            WriteManifest(projectRoot, ContentFolder + "/base-client.json", "base_client", "experiment_fixture");
            WriteManifest(
                projectRoot,
                ContentFolder + "/language-zh-cn.json",
                "language_zh_cn_audio_subtitles",
                "fixture_only_no_production_audio_split");
            WriteManifest(
                projectRoot,
                ContentFolder + "/optional-asr.json",
                "optional_offline_asr",
                "fixture_only_model_missing");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            StabilizeAssetGuids(
                projectRoot,
                new[]
                {
                    TargetVideoAsset,
                    ContentFolder + "/control-01.mp4",
                    ContentFolder + "/control-02.mp4",
                    ReplacementProbeAsset,
                    ContentFolder + "/base-client.json",
                    ContentFolder + "/language-zh-cn.json",
                    ContentFolder + "/optional-asr.json"
                });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            long baselineDuration = ReadVideoDurationMilliseconds(TargetVideoAsset);
            long replacementDuration = ReadVideoDurationMilliseconds(ReplacementProbeAsset);
            AssetDatabase.DeleteAsset(ReplacementProbeAsset);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            return new StagingResult
            {
                BaselineDurationMilliseconds = baselineDuration,
                ReplacementDurationMilliseconds = replacementDuration,
                Assignments = new[]
                {
                    new PackAssetAssignment("base_client", ContentFolder + "/base-client.json"),
                    new PackAssetAssignment("chapter_red_mist_videos", TargetVideoAsset),
                    new PackAssetAssignment("chapter_red_mist_videos", ContentFolder + "/control-01.mp4"),
                    new PackAssetAssignment("chapter_red_mist_videos", ContentFolder + "/control-02.mp4"),
                    new PackAssetAssignment(
                        "language_zh_cn_audio_subtitles",
                        ContentFolder + "/language-zh-cn.json"),
                    new PackAssetAssignment("optional_offline_asr", ContentFolder + "/optional-asr.json")
                }
            };
        }

        private static PatchVideoInput DescribeInput(string path, long durationMilliseconds, string address)
        {
            var file = new FileInfo(path);
            return new PatchVideoInput
            {
                logicalAddress = address,
                contentHash = PatchReportSerializer.HashFile(path),
                length = file.Length,
                durationMilliseconds = durationMilliseconds
            };
        }

        private static PatchBuildInventory BuildInventory(
            long durationMilliseconds,
            long temporaryDiskBytes,
            PatchFileRecord[] files)
        {
            if (files == null || files.Length == 0)
                throw new InvalidDataException("CX-504 build inventory is empty.");
            return new PatchBuildInventory
            {
                durationMilliseconds = durationMilliseconds,
                temporaryDiskBytes = temporaryDiskBytes,
                totalBytes = files.Sum(file => file.length),
                files = files
            };
        }

        private static void RequireBuildResult(
            AddressablesPlayerBuildResult result,
            bool expectUpdate,
            string label)
        {
            if (result == null) throw new InvalidOperationException("CX-504 " + label + " returned null.");
            if (!string.IsNullOrWhiteSpace(result.Error))
                throw new InvalidOperationException("CX-504 " + label + " failed: " + result.Error);
            if (result.IsUpdateContentBuild != expectUpdate)
                throw new InvalidOperationException("CX-504 " + label + " returned an invalid update flag.");
        }

        private static ChangedBundleEvidence ResolveChangedBundles(
            AddressablesPlayerBuildResult result,
            IEnumerable<PublishFileSnapshot> patchFiles)
        {
            var groups = new List<string>();
            var addresses = new List<string>();
            foreach (PublishFileSnapshot file in patchFiles.Where(file => file.Kind == "bundle"))
            {
                AddressablesPlayerBuildResult.BundleBuildResult[] matches = result.AssetBundleBuildResults
                    .Where(bundle => string.Equals(
                        NormalizeFullPath(bundle.FilePath),
                        file.FullPath,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length != 1 || matches[0].SourceAssetGroup == null)
                    throw new InvalidDataException(
                        "CX-504 could not map a changed bundle to exactly one Addressables group: " +
                        file.RelativePath);
                AddressableAssetGroup sourceGroup = matches[0].SourceAssetGroup;
                string fileName = Path.GetFileName(file.RelativePath);
                AddressableAssetEntry[] addressMatches = sourceGroup.entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.address) &&
                                    fileName.IndexOf(
                                        "_" + entry.address + "_",
                                        StringComparison.Ordinal) >= 0)
                    .ToArray();
                if (addressMatches.Length != 1)
                    throw new InvalidDataException(
                        "CX-504 could not map a changed PackSeparately bundle to exactly one address: " +
                        file.RelativePath);
                groups.Add(sourceGroup.Name);
                addresses.Add(addressMatches[0].address);
            }

            return new ChangedBundleEvidence
            {
                Groups = groups.ToArray(),
                Addresses = addresses.ToArray()
            };
        }

        private static PublishFileSnapshot RequireSinglePatchFile(
            IEnumerable<PublishFileSnapshot> patchFiles,
            string relativePath,
            string kind,
            string message)
        {
            PublishFileSnapshot[] matches = patchFiles
                .Where(file =>
                    string.Equals(file.RelativePath, relativePath, StringComparison.Ordinal) &&
                    string.Equals(file.Kind, kind, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1) throw new InvalidDataException(message);
            return matches[0];
        }

        private static Dictionary<string, PublishFileSnapshot> CapturePublishSnapshot(string publishRoot)
        {
            if (!Directory.Exists(publishRoot))
                throw new DirectoryNotFoundException("CX-504 publish root was not created: " + publishRoot);
            var result = new Dictionary<string, PublishFileSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(publishRoot, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                string relative = NormalizeRelative(Path.GetRelativePath(publishRoot, info.FullName));
                if (!relative.StartsWith("local/", StringComparison.Ordinal) &&
                    !relative.StartsWith("remote/", StringComparison.Ordinal))
                    continue;
                if (info.Length <= 0)
                    throw new InvalidDataException(
                        "CX-504 player-deliverable file is empty: " + relative);
                var snapshot = PublishFileSnapshot.FromFile(info.FullName, relative, KindFromPath(relative));
                result.Add(snapshot.FullPath, snapshot);
            }

            return result;
        }

        private static string KindFromPath(string path)
        {
            string fileName = Path.GetFileName(path);
            if (path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) return "bundle";
            if (fileName.IndexOf("catalog", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase)) return "catalog_hash";
            if (fileName.IndexOf("catalog", StringComparison.OrdinalIgnoreCase) >= 0) return "catalog";
            if (string.Equals(fileName, "addressables_content_state.bin", StringComparison.OrdinalIgnoreCase))
                return "content_state";
            return "metadata";
        }

        private static long ReadVideoDurationMilliseconds(string assetPath)
        {
            VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(assetPath);
            if (clip == null) throw new InvalidDataException("CX-504 could not import video: " + assetPath);
            long duration = checked((long)Math.Round(clip.length * 1000d, MidpointRounding.AwayFromZero));
            if (duration < 7900 || duration > 8100)
                throw new InvalidDataException(
                    "CX-504 video must be 8 seconds (7900-8100 ms): " + assetPath + " was " + duration + " ms.");
            return duration;
        }

        private static void WriteManifest(string projectRoot, string assetPath, string packId, string status)
        {
            string json = "{\"packId\":\"" + packId + "\",\"contentStatus\":\"" + status + "\"}";
            File.WriteAllText(AssetPathToFullPath(projectRoot, assetPath), json, new UTF8Encoding(false));
        }

        private static void StabilizeAssetGuids(string projectRoot, IEnumerable<string> assetPaths)
        {
            foreach (string assetPath in assetPaths)
            {
                string metaPath = AssetPathToFullPath(projectRoot, assetPath) + ".meta";
                if (!File.Exists(metaPath))
                    throw new FileNotFoundException("CX-504 imported asset meta is missing.", metaPath);
                string deterministicGuid = AddressablesContentPackBuilder.DeterministicGuid(
                    "cx504.asset." + assetPath);
                string existingAsset = AssetDatabase.GUIDToAssetPath(deterministicGuid);
                if (!string.IsNullOrEmpty(existingAsset) &&
                    !string.Equals(existingAsset, assetPath, StringComparison.Ordinal))
                    throw new InvalidDataException("CX-504 deterministic asset GUID collision: " + assetPath);

                string[] lines = File.ReadAllLines(metaPath);
                int guidLine = Array.FindIndex(lines, line => line.StartsWith("guid: ", StringComparison.Ordinal));
                if (guidLine < 0) throw new InvalidDataException("CX-504 imported meta has no GUID: " + assetPath);
                lines[guidLine] = "guid: " + deterministicGuid;
                File.WriteAllLines(metaPath, lines, new UTF8Encoding(false));
            }
        }

        private static void CopyFile(string sourcePath, string projectRoot, string assetPath)
        {
            File.Copy(sourcePath, AssetPathToFullPath(projectRoot, assetPath), true);
        }

        private static string AssetPathToFullPath(string projectRoot, string assetPath)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string RequireExistingPath(string path, string message)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new FileNotFoundException(message);
            string fullPath = NormalizeFullPath(path);
            if (!File.Exists(fullPath)) throw new FileNotFoundException(message, fullPath);
            return fullPath;
        }

        private static long DirectorySize(string root)
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path).Length)
                    .Sum()
                : 0L;
        }

        private static string NormalizeFullPath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeRelative(string path) => path.Replace('\\', '/');

        private static void DeleteGeneratedAssets()
        {
            if (AssetDatabase.IsValidFolder(GeneratedRoot)) AssetDatabase.DeleteAsset(GeneratedRoot);
        }

        private sealed class StagingResult
        {
            public long BaselineDurationMilliseconds;
            public long ReplacementDurationMilliseconds;
            public PackAssetAssignment[] Assignments;
        }

        private sealed class ChangedBundleEvidence
        {
            public string[] Groups;
            public string[] Addresses;
        }

        private sealed class PublishFileSnapshot
        {
            private PublishFileSnapshot(
                string fullPath,
                string relativePath,
                string contentHash,
                long length,
                string kind)
            {
                FullPath = fullPath;
                RelativePath = relativePath;
                ContentHash = contentHash;
                Length = length;
                Kind = kind;
            }

            public string FullPath { get; }
            public string RelativePath { get; }
            public string ContentHash { get; }
            public long Length { get; }
            public string Kind { get; }

            public static PublishFileSnapshot FromFile(
                string path,
                string relativePath,
                string kind)
            {
                string fullPath = NormalizeFullPath(path);
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length <= 0)
                    throw new InvalidDataException("CX-504 inventory file is missing or empty: " + relativePath);
                return new PublishFileSnapshot(
                    fullPath,
                    NormalizeRelative(relativePath),
                    PatchReportSerializer.HashFile(fullPath),
                    info.Length,
                    kind);
            }

            public PatchFileRecord ToRecord()
            {
                return new PatchFileRecord
                {
                    relativePath = RelativePath,
                    contentHash = ContentHash,
                    length = Length,
                    kind = Kind
                };
            }
        }
    }
}
