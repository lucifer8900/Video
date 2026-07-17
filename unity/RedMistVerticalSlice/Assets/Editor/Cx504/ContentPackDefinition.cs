using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Lingmai.RedMist.Cx504
{
    [Serializable]
    public sealed class ContentPackDefinition
    {
        [SerializeField] private string packId;
        [SerializeField] private string groupName;
        [SerializeField] private string delivery;
        [SerializeField] private bool optional;
        [SerializeField] private bool defaultInstall;
        [SerializeField] private bool staticContent;
        [SerializeField] private string bundleMode;
        [SerializeField] private string compression;
        [SerializeField] private string sourceKind;
        [SerializeField] private string contentStatus;

        public string PackId => packId;
        public string GroupName => groupName;
        public string Delivery => delivery;
        public bool Optional => optional;
        public bool DefaultInstall => defaultInstall;
        public bool StaticContent => staticContent;
        public string BundleMode => bundleMode;
        public string Compression => compression;
        public string SourceKind => sourceKind;
        public string ContentStatus => contentStatus;
    }

    [Serializable]
    public sealed class PatchThresholdDefinition
    {
        [SerializeField] private double changedAssetMultiplier;
        [SerializeField] private long fixedOverheadBytes;
        [SerializeField] private string authority;

        public double ChangedAssetMultiplier => changedAssetMultiplier;
        public long FixedOverheadBytes => fixedOverheadBytes;
        public string Authority => authority;
    }

    [Serializable]
    public sealed class AddressablesContentPackPlan
    {
        private static readonly string[] RequiredPackIds =
        {
            "base_client",
            "chapter_red_mist_videos",
            "language_zh_cn_audio_subtitles",
            "optional_offline_asr"
        };

        [SerializeField] private string schemaVersion;
        [SerializeField] private string planId;
        [SerializeField] private string mode;
        [SerializeField] private bool runtimeMigration;
        [SerializeField] private bool steamPipeVerified;
        [SerializeField] private string productionReadiness;
        [SerializeField] private string claimAuthority;
        [SerializeField] private string unityVersion;
        [SerializeField] private string addressablesVersion;
        [SerializeField] private long maximumBundleBytes;
        [SerializeField] private PatchThresholdDefinition patchThreshold;
        [SerializeField] private ContentPackDefinition[] packs;
        [SerializeField] private string[] prohibitedSourcePatterns;

        public string SchemaVersion => schemaVersion;
        public string PlanId => planId;
        public string Mode => mode;
        public bool RuntimeMigration => runtimeMigration;
        public bool SteamPipeVerified => steamPipeVerified;
        public string ProductionReadiness => productionReadiness;
        public string ClaimAuthority => claimAuthority;
        public string UnityVersion => unityVersion;
        public string AddressablesVersion => addressablesVersion;
        public long MaximumBundleBytes => maximumBundleBytes;
        public PatchThresholdDefinition PatchThreshold => patchThreshold;
        public IReadOnlyList<ContentPackDefinition> Packs => packs ?? Array.Empty<ContentPackDefinition>();
        public IReadOnlyList<string> ProhibitedSourcePatterns =>
            prohibitedSourcePatterns ?? Array.Empty<string>();

        public static AddressablesContentPackPlan Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Pack plan path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("CX-504 pack plan was not found.", fullPath);
            AddressablesContentPackPlan plan = JsonUtility.FromJson<AddressablesContentPackPlan>(
                File.ReadAllText(fullPath));
            if (plan == null) throw new InvalidDataException("CX-504 pack plan is invalid JSON.");
            return plan;
        }

        public ContentPackDefinition RequirePack(string packId)
        {
            ContentPackDefinition pack = Packs.SingleOrDefault(candidate =>
                string.Equals(candidate.PackId, packId, StringComparison.Ordinal));
            return pack ?? throw new InvalidDataException("CX-504 pack is missing: " + packId);
        }

        public string[] Validate()
        {
            var errors = new List<string>();
            Check(errors, schemaVersion == "1.0.0", "plan.schema_version");
            Check(errors, planId == "cx504.red-mist.addressables.v1", "plan.id");
            Check(errors, mode == "experiment_only", "plan.mode");
            Check(errors, !runtimeMigration, "plan.runtime_migration_claim");
            Check(errors, !steamPipeVerified, "plan.steam_claim");
            Check(errors, productionReadiness == "not_evaluated", "plan.production_claim");
            Check(
                errors,
                claimAuthority == "addressables_payload_experiment_not_runtime_or_steam_authority",
                "plan.authority");
            Check(errors, unityVersion == "2022.3.62f3c1", "plan.unity_version");
            Check(errors, addressablesVersion == "1.22.3", "plan.addressables_version");
            Check(errors, maximumBundleBytes == 2_147_483_648L, "plan.bundle_limit");
            Check(errors, patchThreshold != null, "plan.patch_threshold");
            if (patchThreshold != null)
            {
                Check(
                    errors,
                    patchThreshold.ChangedAssetMultiplier == 1.5d,
                    "plan.patch_multiplier");
                Check(errors, patchThreshold.FixedOverheadBytes == 1_048_576L, "plan.patch_overhead");
                Check(
                    errors,
                    patchThreshold.Authority == "engineering_guardrail_not_steam_download_guarantee",
                    "plan.patch_authority");
            }

            string[] actualIds = Packs.Select(pack => pack?.PackId).ToArray();
            Check(errors, actualIds.Length == RequiredPackIds.Length, "plan.pack_count");
            Check(
                errors,
                actualIds.Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal).Count() ==
                actualIds.Length,
                "plan.pack_duplicate");
            foreach (string required in RequiredPackIds)
                Check(errors, actualIds.Contains(required, StringComparer.Ordinal), "plan.pack_missing:" + required);
            foreach (ContentPackDefinition pack in Packs.Where(value => value != null))
            {
                Check(errors, IsIdentifier(pack.GroupName), "plan.group_name:" + pack.PackId);
                Check(errors, pack.Delivery == "local" || pack.Delivery == "remote", "plan.delivery:" + pack.PackId);
                Check(
                    errors,
                    pack.BundleMode == "pack_together" || pack.BundleMode == "pack_separately",
                    "plan.bundle_mode:" + pack.PackId);
                Check(
                    errors,
                    pack.Compression == "lz4" || pack.Compression == "uncompressed",
                    "plan.compression:" + pack.PackId);
                Check(errors, !string.IsNullOrWhiteSpace(pack.ContentStatus), "plan.content_status:" + pack.PackId);
            }

            ValidateExactPack(
                errors,
                actualIds,
                "base_client",
                "base-client-local",
                "local",
                optional: false,
                defaultInstall: true,
                staticContent: true,
                "pack_together",
                "lz4",
                "generated_manifest",
                "experiment_fixture");
            ValidateExactPack(
                errors,
                actualIds,
                "chapter_red_mist_videos",
                "chapter-red-mist-video-remote",
                "remote",
                optional: false,
                defaultInstall: true,
                staticContent: false,
                "pack_separately",
                "uncompressed",
                "local_ignored_video_copy",
                "needs_review");
            ValidateExactPack(
                errors,
                actualIds,
                "language_zh_cn_audio_subtitles",
                "zh-cn-voice-subtitles-remote",
                "remote",
                optional: false,
                defaultInstall: true,
                staticContent: true,
                "pack_together",
                "lz4",
                "generated_manifest",
                "fixture_only_no_production_audio_split");
            ValidateExactPack(
                errors,
                actualIds,
                "optional_offline_asr",
                "optional-asr-model-remote",
                "remote",
                optional: true,
                defaultInstall: false,
                staticContent: true,
                "pack_together",
                "lz4",
                "generated_manifest",
                "fixture_only_model_missing");

            Check(
                errors,
                ProhibitedSourcePatterns.Contains(
                    "content/originalization/source-to-game-name-map.json",
                    StringComparer.Ordinal),
                "plan.prohibited_mapping");
            Check(
                errors,
                ProhibitedSourcePatterns.Contains("GEMINI_API_KEY", StringComparer.Ordinal),
                "plan.prohibited_key");
            return errors.ToArray();
        }

        private static void Check(ICollection<string> errors, bool condition, string code)
        {
            if (!condition) errors.Add(code);
        }

        private void ValidateExactPack(
            ICollection<string> errors,
            IReadOnlyCollection<string> actualIds,
            string packId,
            string groupName,
            string delivery,
            bool optional,
            bool defaultInstall,
            bool staticContent,
            string bundleMode,
            string compression,
            string sourceKind,
            string contentStatus)
        {
            if (!actualIds.Contains(packId, StringComparer.Ordinal)) return;
            ContentPackDefinition pack = RequirePack(packId);
            Check(errors, pack.GroupName == groupName, "plan.pack_group:" + packId);
            Check(errors, pack.Delivery == delivery, "plan.pack_delivery:" + packId);
            Check(errors, pack.Optional == optional, "plan.pack_optional:" + packId);
            Check(errors, pack.DefaultInstall == defaultInstall, "plan.pack_default_install:" + packId);
            Check(errors, pack.StaticContent == staticContent, "plan.pack_static_content:" + packId);
            Check(errors, pack.BundleMode == bundleMode, "plan.pack_bundle_mode:" + packId);
            Check(errors, pack.Compression == compression, "plan.pack_compression:" + packId);
            Check(errors, pack.SourceKind == sourceKind, "plan.pack_source_kind:" + packId);
            Check(errors, pack.ContentStatus == contentStatus, "plan.pack_content_status:" + packId);
        }

        private static bool IsIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.All(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.');
        }
    }
}
