using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Lingmai.RedMist.Cx504
{
    public sealed class PackAssetAssignment
    {
        public PackAssetAssignment(string packId, string assetPath)
        {
            PackId = packId;
            AssetPath = assetPath;
        }

        public string PackId { get; }
        public string AssetPath { get; }
    }

    public static class AddressablesContentPackBuilder
    {
        private const string SettingsName = "Cx504AddressableAssetSettings";
        private const string OwnedSettingsRoot = "Assets/Cx504Generated/";
        private const string StableLocalLoadPath = "file:///cx504.invalid/local/[BuildTarget]";
        private const string StableRemoteLoadPath = "https://cx504.invalid/content/[BuildTarget]";

        public static AddressableAssetSettings CreateSettings(
            AddressablesContentPackPlan plan,
            string settingsFolder,
            string buildRoot,
            IEnumerable<PackAssetAssignment> assignments)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            string[] planErrors = plan.Validate();
            if (planErrors.Length > 0)
                throw new InvalidDataException("CX-504 pack plan failed validation: " + string.Join(",", planErrors));
            string normalizedSettingsFolder = Normalize(settingsFolder ?? string.Empty).TrimEnd('/');
            if (!normalizedSettingsFolder.StartsWith(OwnedSettingsRoot, StringComparison.Ordinal) ||
                normalizedSettingsFolder.Split('/').Any(segment => segment == "." || segment == ".."))
                throw new ArgumentException(
                    "Settings folder must be owned by CX-504 under " + OwnedSettingsRoot,
                    nameof(settingsFolder));
            settingsFolder = normalizedSettingsFolder;
            if (string.IsNullOrWhiteSpace(buildRoot))
                throw new ArgumentException("Build root is required.", nameof(buildRoot));

            PackAssetAssignment[] assets = (assignments ?? throw new ArgumentNullException(nameof(assignments)))
                .ToArray();
            ValidateAssignments(plan, assets);

            if (AssetDatabase.IsValidFolder(settingsFolder)) AssetDatabase.DeleteAsset(settingsFolder);
            AddressableAssetSettings settings = AddressableAssetSettings.Create(
                settingsFolder,
                SettingsName,
                true,
                true);
            ConfigureProfile(settings, Path.GetFullPath(buildRoot));
            ConfigureGroups(settings, plan);
            AddEntries(settings, plan, assets);
            settings.BuildRemoteCatalog = true;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            settings.ContentStateBuildPath = Normalize(Path.Combine(Path.GetFullPath(buildRoot), "state"));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static void SetBuildRoot(AddressableAssetSettings settings, string buildRoot)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ConfigureProfile(settings, Path.GetFullPath(buildRoot));
            settings.ContentStateBuildPath = Normalize(Path.Combine(Path.GetFullPath(buildRoot), "state"));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        public static string StableAddress(string packId, string assetPath)
        {
            string stem = Path.GetFileNameWithoutExtension(assetPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(stem))
                throw new ArgumentException("Pack ID and asset path are required for an address.");
            string safeStem = new string(stem.Select(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? char.ToLowerInvariant(character)
                    : '-').ToArray());
            return "cx504." + packId + "." + safeStem;
        }

        public static string DeterministicGuid(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                throw new ArgumentException("Deterministic GUID identity is required.", nameof(identity));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                var builder = new StringBuilder(32);
                for (int index = 0; index < 16; index++) builder.Append(hash[index].ToString("x2"));
                return builder.ToString();
            }
        }

        private static void ValidateAssignments(
            AddressablesContentPackPlan plan,
            IReadOnlyList<PackAssetAssignment> assignments)
        {
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (PackAssetAssignment assignment in assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.PackId) ||
                    string.IsNullOrWhiteSpace(assignment.AssetPath))
                    throw new InvalidDataException("CX-504 asset assignment is incomplete.");
                plan.RequirePack(assignment.PackId);
                string path = Normalize(assignment.AssetPath);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    throw new InvalidDataException("CX-504 asset must be imported under Assets: " + path);
                if (!seenPaths.Add(path))
                    throw new InvalidDataException("CX-504 asset is assigned more than once: " + path);
            }

            foreach (ContentPackDefinition pack in plan.Packs.Where(pack => !pack.Optional))
            {
                if (!assignments.Any(assignment => assignment.PackId == pack.PackId))
                    throw new InvalidDataException("CX-504 required pack is empty: " + pack.PackId);
            }
        }

        private static void ConfigureProfile(AddressableAssetSettings settings, string buildRoot)
        {
            string localBuild = Normalize(Path.Combine(buildRoot, "local", "[BuildTarget]"));
            string remoteBuild = Normalize(Path.Combine(buildRoot, "remote", "[BuildTarget]"));
            settings.profileSettings.SetValue(
                settings.activeProfileId,
                AddressableAssetSettings.kLocalBuildPath,
                localBuild);
            settings.profileSettings.SetValue(
                settings.activeProfileId,
                AddressableAssetSettings.kLocalLoadPath,
                StableLocalLoadPath);
            settings.profileSettings.SetValue(
                settings.activeProfileId,
                AddressableAssetSettings.kRemoteBuildPath,
                remoteBuild);
            settings.profileSettings.SetValue(
                settings.activeProfileId,
                AddressableAssetSettings.kRemoteLoadPath,
                StableRemoteLoadPath);
        }

        private static void ConfigureGroups(
            AddressableAssetSettings settings,
            AddressablesContentPackPlan plan)
        {
            AddressableAssetGroup defaultGroup = settings.DefaultGroup;
            ContentPackDefinition basePack = plan.RequirePack("base_client");
            defaultGroup.Name = basePack.GroupName;
            SetDeterministicGroupGuid(defaultGroup, basePack.PackId);
            ConfigureGroup(settings, defaultGroup, basePack);

            foreach (ContentPackDefinition pack in plan.Packs.Where(pack => pack.PackId != "base_client"))
            {
                AddressableAssetGroup group = settings.CreateGroup(
                    pack.GroupName,
                    false,
                    false,
                    true,
                    null,
                    typeof(ContentUpdateGroupSchema),
                    typeof(BundledAssetGroupSchema));
                SetDeterministicGroupGuid(group, pack.PackId);
                ConfigureGroup(settings, group, pack);
            }
        }

        private static void SetDeterministicGroupGuid(AddressableAssetGroup group, string packId)
        {
            var serialized = new SerializedObject(group);
            SerializedProperty guid = serialized.FindProperty("m_GUID");
            if (guid == null) throw new InvalidDataException("Addressables group GUID field was not found.");
            guid.stringValue = DeterministicGuid("cx504.group." + packId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(group);
        }

        private static void ConfigureGroup(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            ContentPackDefinition pack)
        {
            BundledAssetGroupSchema bundle = group.GetSchema<BundledAssetGroupSchema>();
            ContentUpdateGroupSchema update = group.GetSchema<ContentUpdateGroupSchema>();
            string buildVariable = pack.Delivery == "local"
                ? AddressableAssetSettings.kLocalBuildPath
                : AddressableAssetSettings.kRemoteBuildPath;
            string loadVariable = pack.Delivery == "local"
                ? AddressableAssetSettings.kLocalLoadPath
                : AddressableAssetSettings.kRemoteLoadPath;
            bundle.BuildPath.SetVariableByName(settings, buildVariable);
            bundle.LoadPath.SetVariableByName(settings, loadVariable);
            bundle.BundleMode = pack.BundleMode == "pack_separately"
                ? BundledAssetGroupSchema.BundlePackingMode.PackSeparately
                : BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundle.Compression = pack.Compression == "uncompressed"
                ? BundledAssetGroupSchema.BundleCompressionMode.Uncompressed
                : BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundle.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
            update.StaticContent = pack.StaticContent;
            EditorUtility.SetDirty(bundle);
            EditorUtility.SetDirty(update);
            EditorUtility.SetDirty(group);
        }

        private static void AddEntries(
            AddressableAssetSettings settings,
            AddressablesContentPackPlan plan,
            IEnumerable<PackAssetAssignment> assignments)
        {
            foreach (PackAssetAssignment assignment in assignments)
            {
                string assetPath = Normalize(assignment.AssetPath);
                AddressableAssetGroup group = settings.FindGroup(plan.RequirePack(assignment.PackId).GroupName);
                AddressableAssetEntry entry = settings.CreateOrMoveEntry(
                    AssetDatabase.AssetPathToGUID(assetPath),
                    group,
                    false,
                    false);
                entry.address = StableAddress(assignment.PackId, assetPath);
                EditorUtility.SetDirty(group);
            }
        }

        private static string Normalize(string value) => value.Replace('\\', '/');
    }

    public static class Cx504DefaultSettingsCleanupPolicy
    {
        public static bool CanStartExperiment(
            bool configObjectExistedBefore,
            bool defaultObjectFileExistedBefore,
            bool previousSettingsAvailable)
        {
            return (!configObjectExistedBefore && !defaultObjectFileExistedBefore) ||
                   previousSettingsAvailable;
        }

        public static bool ShouldDeleteDefaultObject(
            bool configObjectExistedBefore,
            bool defaultObjectFileExistedBefore)
        {
            return !configObjectExistedBefore && !defaultObjectFileExistedBefore;
        }

        public static bool CanDeleteDefaultFolder(
            bool folderExistedBefore,
            bool configObjectExistedBefore,
            bool defaultObjectFileExistedBefore,
            bool folderIsEmpty)
        {
            return !folderExistedBefore && !configObjectExistedBefore &&
                   !defaultObjectFileExistedBefore && folderIsEmpty;
        }
    }
}
