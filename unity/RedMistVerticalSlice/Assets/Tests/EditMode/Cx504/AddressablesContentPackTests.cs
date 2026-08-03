using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Lingmai.RedMist.Cx504.Tests
{
    public sealed class AddressablesContentPackTests
    {
        [Test]
        public void VersionedPlanDeclaresFourFailClosedExperimentPacks()
        {
            AddressablesContentPackPlan plan = AddressablesContentPackPlan.Load(PlanPath());

            CollectionAssert.IsEmpty(plan.Validate());
            Assert.AreEqual("experiment_only", plan.Mode);
            Assert.IsFalse(plan.RuntimeMigration);
            Assert.IsFalse(plan.SteamPipeVerified);
            Assert.AreEqual("not_evaluated", plan.ProductionReadiness);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "base_client",
                    "chapter_red_mist_videos",
                    "language_zh_cn_audio_subtitles",
                    "optional_offline_asr"
                },
                plan.Packs.Select(pack => pack.PackId));

            ContentPackDefinition video = plan.RequirePack("chapter_red_mist_videos");
            Assert.AreEqual("pack_separately", video.BundleMode);
            Assert.AreEqual("uncompressed", video.Compression);
            Assert.IsFalse(video.StaticContent);
            Assert.AreEqual("needs_review", video.ContentStatus);

            ContentPackDefinition asr = plan.RequirePack("optional_offline_asr");
            Assert.IsTrue(asr.Optional);
            Assert.IsFalse(asr.DefaultInstall);
            Assert.That(asr.ContentStatus, Does.Contain("missing"));
        }

        [Test]
        public void BuilderCreatesPinnedPathsSchemasAndUniquePackEntries()
        {
            string root = "Assets/Cx504Generated/Tests/" + Guid.NewGuid().ToString("N");
            string settingsFolder = root + "/Settings";
            string contentFolder = root + "/Content";
            Directory.CreateDirectory(contentFolder);
            var assignments = new List<PackAssetAssignment>();
            try
            {
                AddressablesContentPackPlan plan = AddressablesContentPackPlan.Load(PlanPath());
                foreach (ContentPackDefinition pack in plan.Packs)
                {
                    string asset = contentFolder + "/" + pack.PackId + ".json";
                    File.WriteAllText(asset, "{\"packId\":\"" + pack.PackId + "\"}");
                    assignments.Add(new PackAssetAssignment(pack.PackId, asset));
                }
                string secondVideo = contentFolder + "/chapter_control.bytes";
                File.WriteAllBytes(secondVideo, new byte[] { 1, 2, 3, 4 });
                assignments.Add(new PackAssetAssignment("chapter_red_mist_videos", secondVideo));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                AddressableAssetSettings settings = AddressablesContentPackBuilder.CreateSettings(
                    plan,
                    settingsFolder,
                    Path.Combine(Path.GetTempPath(), "cx504-build-" + Guid.NewGuid().ToString("N")),
                    assignments);

                Assert.IsTrue(settings.BuildRemoteCatalog);
                string remoteLoadPath = settings.profileSettings.GetValueByName(
                    settings.activeProfileId,
                    AddressableAssetSettings.kRemoteLoadPath);
                Assert.AreEqual(
                    "https://cx504.invalid/content/[BuildTarget]",
                    remoteLoadPath,
                    "A/B builds need one stable logical load path; the local output folder is not runtime authority.");
                AddressablesContentPackBuilder.SetBuildRoot(
                    settings,
                    Path.Combine(Path.GetTempPath(), "cx504-update-" + Guid.NewGuid().ToString("N")));
                Assert.AreEqual(
                    remoteLoadPath,
                    settings.profileSettings.GetValueByName(
                        settings.activeProfileId,
                        AddressableAssetSettings.kRemoteLoadPath));
                foreach (ContentPackDefinition pack in plan.Packs)
                {
                    AddressableAssetGroup group = settings.FindGroup(pack.GroupName);
                    Assert.IsNotNull(group, pack.GroupName);
                    BundledAssetGroupSchema bundle = group.GetSchema<BundledAssetGroupSchema>();
                    ContentUpdateGroupSchema update = group.GetSchema<ContentUpdateGroupSchema>();
                    Assert.IsNotNull(bundle);
                    Assert.IsNotNull(update);
                    Assert.AreEqual(pack.StaticContent, update.StaticContent);
                    Assert.That(bundle.BuildPath.GetValue(settings), Does.Contain(
                        pack.Delivery == "local" ? "/local/" : "/remote/"));
                    Assert.Greater(group.entries.Count, 0, pack.PackId);
                    Assert.AreEqual(group.entries.Count, group.entries.Select(entry => entry.address).Distinct().Count());
                }

                AddressableAssetGroup videoGroup = settings.FindGroup(
                    plan.RequirePack("chapter_red_mist_videos").GroupName);
                BundledAssetGroupSchema videoSchema = videoGroup.GetSchema<BundledAssetGroupSchema>();
                Assert.AreEqual(BundledAssetGroupSchema.BundlePackingMode.PackSeparately, videoSchema.BundleMode);
                Assert.AreEqual(BundledAssetGroupSchema.BundleCompressionMode.Uncompressed, videoSchema.Compression);

                AddressableAssetSettings repeatedSettings = AddressablesContentPackBuilder.CreateSettings(
                    plan,
                    settingsFolder + "-stable",
                    Path.Combine(Path.GetTempPath(), "cx504-stable-" + Guid.NewGuid().ToString("N")),
                    assignments);
                foreach (ContentPackDefinition pack in plan.Packs)
                {
                    Assert.AreEqual(
                        settings.FindGroup(pack.GroupName).Guid,
                        repeatedSettings.FindGroup(pack.GroupName).Guid,
                        "Group GUIDs are build evidence and must be deterministic: " + pack.PackId);
                }

                Assert.Throws<InvalidDataException>(() => AddressablesContentPackBuilder.CreateSettings(
                    plan,
                    settingsFolder + "-duplicate",
                    Path.Combine(Path.GetTempPath(), "cx504-duplicate-" + Guid.NewGuid().ToString("N")),
                    new[]
                    {
                        assignments[0],
                        new PackAssetAssignment("chapter_red_mist_videos", assignments[0].AssetPath)
                    }));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
                AssetDatabase.DeleteAsset(settingsFolder + "-stable");
                AssetDatabase.DeleteAsset(settingsFolder + "-duplicate");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void BuilderRejectsSettingsOutsideItsOwnedGeneratedSubtree()
        {
            AddressablesContentPackPlan plan = AddressablesContentPackPlan.Load(PlanPath());
            Assert.Throws<ArgumentException>(() => AddressablesContentPackBuilder.CreateSettings(
                plan,
                "Assets/ProductionAddressables",
                Path.Combine(Path.GetTempPath(), "cx504-unsafe-" + Guid.NewGuid().ToString("N")),
                Array.Empty<PackAssetAssignment>()));
        }

        [Test]
        public void CleanupPolicyNeverDeletesPreExistingAddressablesAssets()
        {
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.CanStartExperiment(
                configObjectExistedBefore: true,
                defaultObjectFileExistedBefore: false,
                previousSettingsAvailable: false));
            Assert.IsTrue(Cx504DefaultSettingsCleanupPolicy.CanStartExperiment(
                configObjectExistedBefore: true,
                defaultObjectFileExistedBefore: false,
                previousSettingsAvailable: true));
            Assert.IsTrue(Cx504DefaultSettingsCleanupPolicy.CanStartExperiment(
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: false,
                previousSettingsAvailable: false));
            Assert.IsTrue(Cx504DefaultSettingsCleanupPolicy.ShouldDeleteDefaultObject(
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: false));
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.ShouldDeleteDefaultObject(
                configObjectExistedBefore: true,
                defaultObjectFileExistedBefore: false));
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.ShouldDeleteDefaultObject(
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: true));
            Assert.IsTrue(Cx504DefaultSettingsCleanupPolicy.CanDeleteDefaultFolder(
                folderExistedBefore: false,
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: false,
                folderIsEmpty: true));
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.CanDeleteDefaultFolder(
                folderExistedBefore: true,
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: false,
                folderIsEmpty: true));
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.CanDeleteDefaultFolder(
                folderExistedBefore: false,
                configObjectExistedBefore: true,
                defaultObjectFileExistedBefore: false,
                folderIsEmpty: true));
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.CanDeleteDefaultFolder(
                folderExistedBefore: false,
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: true,
                folderIsEmpty: true));
            Assert.IsFalse(Cx504DefaultSettingsCleanupPolicy.CanDeleteDefaultFolder(
                folderExistedBefore: false,
                configObjectExistedBefore: false,
                defaultObjectFileExistedBefore: false,
                folderIsEmpty: false));
        }

        private static string PlanPath()
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "..",
                "content",
                "packaging",
                "cx504-addressables-pack-plan.json"));
        }
    }
}
