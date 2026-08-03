using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Lingmai.RedMist.Cx504
{
    public sealed class PatchExperimentOptions
    {
        private PatchExperimentOptions(
            string baselineVideoPath,
            string replacementVideoPath,
            string outputRoot,
            string reportPath)
        {
            BaselineVideoPath = baselineVideoPath;
            ReplacementVideoPath = replacementVideoPath;
            OutputRoot = outputRoot;
            ReportPath = reportPath;
        }

        public string BaselineVideoPath { get; }
        public string ReplacementVideoPath { get; }
        public string OutputRoot { get; }
        public string ReportPath { get; }

        public static PatchExperimentOptions Parse(IEnumerable<string> arguments, string unityProjectRoot)
        {
            string[] values = (arguments ?? throw new ArgumentNullException(nameof(arguments))).ToArray();
            string projectRoot = RequireFullPath(unityProjectRoot, nameof(unityProjectRoot));
            if (!Directory.Exists(projectRoot))
                throw new DirectoryNotFoundException("Unity project root was not found: " + projectRoot);

            string baseline = RequireExistingMp4(values, "-cx504BaselineVideo");
            string replacement = RequireExistingMp4(values, "-cx504ReplacementVideo");
            if (string.Equals(baseline, replacement, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CX-504 baseline and replacement videos must be distinct files.");

            string output = RequireFullPath(RequireSingleValue(values, "-cx504Output"), "-cx504Output");
            if (IsSameOrDescendant(output, projectRoot))
                throw new InvalidDataException("CX-504 generated payload must remain outside the Unity project.");
            if (File.Exists(output))
                throw new InvalidDataException("CX-504 output path is an existing file.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new InvalidDataException("CX-504 output directory must be absent or empty.");

            string report = RequireFullPath(RequireSingleValue(values, "-cx504Report"), "-cx504Report");
            if (!string.Equals(Path.GetExtension(report), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CX-504 report must be a JSON file.");
            if (File.Exists(report) || Directory.Exists(report))
                throw new InvalidDataException("CX-504 report path must not already exist.");
            if (IsSameOrDescendant(report, output))
                throw new InvalidDataException("CX-504 report must not be stored inside generated payload.");

            return new PatchExperimentOptions(baseline, replacement, output, report);
        }

        private static string RequireExistingMp4(IReadOnlyList<string> arguments, string key)
        {
            string path = RequireFullPath(RequireSingleValue(arguments, key), key);
            if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(key + " must reference an MP4 file.");
            if (!File.Exists(path)) throw new FileNotFoundException(key + " was not found.", path);
            if (new FileInfo(path).Length <= 0) throw new InvalidDataException(key + " must not be empty.");
            return path;
        }

        private static string RequireSingleValue(IReadOnlyList<string> arguments, string key)
        {
            var matches = new List<string>();
            for (int index = 0; index < arguments.Count; index++)
            {
                if (!string.Equals(arguments[index], key, StringComparison.Ordinal)) continue;
                if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]) ||
                    arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new InvalidDataException("CX-504 argument has no value: " + key);
                matches.Add(arguments[index + 1]);
            }

            if (matches.Count != 1)
                throw new InvalidDataException("CX-504 requires exactly one argument: " + key);
            return matches[0];
        }

        private static string RequireFullPath(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Path is required.", name);
            if (!Path.IsPathFullyQualified(value))
                throw new InvalidDataException("CX-504 path must be fully qualified: " + name);
            try
            {
                return Path.GetFullPath(value);
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                throw new InvalidDataException("CX-504 path is invalid: " + name, exception);
            }
        }

        private static bool IsSameOrDescendant(string candidate, string root)
        {
            string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
            return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public sealed class PatchVideoInput
    {
        public string logicalAddress;
        public string contentHash;
        public long length;
        public long durationMilliseconds;
    }

    [Serializable]
    public sealed class PatchFileRecord
    {
        public string relativePath;
        public string contentHash;
        public long length;
        public string kind;
    }

    [Serializable]
    public sealed class PatchBuildInventory
    {
        public long durationMilliseconds;
        public long temporaryDiskBytes;
        public long totalBytes;
        public PatchFileRecord[] files;
    }

    [Serializable]
    public sealed class PatchExperimentReport
    {
        public string schemaVersion;
        public string reportId;
        public string planId;
        public string mode;
        public bool runtimeMigration;
        public bool steamPipeVerified;
        public string productionReadiness;
        public string authority;
        public string buildTarget;
        public string unityVersion;
        public string addressablesVersion;
        public string startedAtUtc;
        public string completedAtUtc;
        public PatchVideoInput baselineInput;
        public PatchVideoInput replacementInput;
        public PatchBuildInventory baselineBuild;
        public PatchBuildInventory updateBuild;
        public long patchPayloadBytes;
        public double amplificationRatio;
        public long changedAssetBytes;
        public long maximumAllowedPatchBytes;
        public long maximumBundleBytes;
        public long maximumObservedBundleBytes;
        public string expectedChangedBundleGroup;
        public string[] changedBundleGroups;
        public string expectedChangedBundleAddress;
        public string[] changedBundleAddresses;
        public string[] unexpectedChangedBundles;
        public bool unchangedBundleHashesPreserved;
        public string verdict;
        public string[] limitations;
        public string contentHash;
    }

    public static class PatchReportSerializer
    {
        private static readonly HashSet<string> FileKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "bundle",
            "catalog",
            "catalog_hash",
            "content_state",
            "metadata"
        };

        private const string HashPrefix = "sha256:";

        public static string SerializeAndSeal(PatchExperimentReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            report.contentHash = string.Empty;
            Validate(report, requireHash: false);
            report.contentHash = ComputeHash(JsonUtility.ToJson(report, false));
            Validate(report, requireHash: true);
            return JsonUtility.ToJson(report, true);
        }

        public static bool Verify(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                PatchExperimentReport report = JsonUtility.FromJson<PatchExperimentReport>(json);
                if (report == null || !IsHash(report.contentHash)) return false;
                string expected = report.contentHash;
                Validate(report, requireHash: true);
                if (!string.Equals(
                        NormalizeJsonText(json),
                        NormalizeJsonText(JsonUtility.ToJson(report, true)),
                        StringComparison.Ordinal))
                    return false;
                report.contentHash = string.Empty;
                Validate(report, requireHash: false);
                return string.Equals(expected, ComputeHash(JsonUtility.ToJson(report, false)), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static void WriteNew(string path, string json)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Report path is required.", nameof(path));
            if (string.IsNullOrWhiteSpace(json) || !Verify(json))
                throw new InvalidDataException("CX-504 report is not a valid sealed report.");
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) throw new InvalidDataException("CX-504 report parent is missing.");
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
            }
        }

        public static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return HashPrefix + ToLowerHex(sha.ComputeHash(stream));
        }

        private static void Validate(PatchExperimentReport report, bool requireHash)
        {
            Require(report.schemaVersion == "1.0.0", "report.schema_version");
            Require(report.mode == "experiment_only", "report.mode");
            Require(!report.runtimeMigration, "report.runtime_migration_claim");
            Require(!report.steamPipeVerified, "report.steam_claim");
            Require(report.productionReadiness == "not_evaluated", "report.production_claim");
            Require(
                report.authority == "addressables_payload_measurement_not_steam_download_authority",
                "report.authority");
            Require(report.unityVersion == "2022.3.62f3c1", "report.unity_version");
            Require(report.addressablesVersion == "1.22.3", "report.addressables_version");
            RequireId(report.reportId, "report.id");
            RequireId(report.planId, "report.plan_id");
            Require(!string.IsNullOrWhiteSpace(report.buildTarget), "report.build_target");
            Require(DateTimeOffset.TryParse(report.startedAtUtc, out _), "report.started_at");
            Require(DateTimeOffset.TryParse(report.completedAtUtc, out _), "report.completed_at");
            ValidateInput(report.baselineInput, "report.baseline_input");
            ValidateInput(report.replacementInput, "report.replacement_input");
            Require(
                report.baselineInput.logicalAddress == report.replacementInput.logicalAddress,
                "report.logical_address_changed");
            Require(
                report.baselineInput.contentHash != report.replacementInput.contentHash,
                "report.video_hash_unchanged");
            ValidateInventory(report.baselineBuild, "report.baseline_build");
            ValidateInventory(report.updateBuild, "report.update_build");
            Require(report.patchPayloadBytes > 0, "report.patch_bytes");
            Require(double.IsFinite(report.amplificationRatio) && report.amplificationRatio >= 0d, "report.ratio");
            Require(report.maximumAllowedPatchBytes > 0, "report.threshold");
            Require(report.maximumBundleBytes > 0 && report.maximumBundleBytes <= 2_147_483_648L, "report.bundle_limit");
            Require(report.maximumObservedBundleBytes > 0, "report.maximum_observed_bundle");
            Require(
                report.expectedChangedBundleGroup == "chapter-red-mist-video-remote",
                "report.expected_changed_group");
            Require(report.changedBundleGroups != null, "report.changed_groups");
            Require(
                report.expectedChangedBundleAddress == "cx504.chapter_red_mist_videos.target",
                "report.expected_changed_address");
            Require(
                report.expectedChangedBundleAddress == report.baselineInput.logicalAddress &&
                report.expectedChangedBundleAddress == report.replacementInput.logicalAddress,
                "report.expected_address_input_mismatch");
            Require(report.changedBundleAddresses != null, "report.changed_addresses");
            Require(report.unexpectedChangedBundles != null, "report.unexpected_bundles");
            Require(report.verdict == "passed" || report.verdict == "failed", "report.verdict");
            Require(report.limitations != null && report.limitations.Length > 0, "report.limitations");
            ValidateTokens(report.changedBundleGroups, "report.changed_groups");
            ValidateTokens(report.changedBundleAddresses, "report.changed_addresses");
            ValidateTokens(report.unexpectedChangedBundles, "report.unexpected_bundles");
            ValidateTokens(report.limitations, "report.limitations");
            Require(report.limitations.Contains("not_runtime_migration"), "report.runtime_limitation");
            Require(report.limitations.Contains("not_steam_download_measurement"), "report.steam_limitation");

            long changedAssetBytes = Math.Max(report.baselineInput.length, report.replacementInput.length);
            Require(report.changedAssetBytes == changedAssetBytes, "report.changed_asset_bytes_mismatch");
            long expectedMaximum = checked((long)Math.Ceiling(changedAssetBytes * 1.5d) + 1_048_576L);
            Require(report.maximumAllowedPatchBytes == expectedMaximum, "report.threshold_mismatch");
            Require(report.patchPayloadBytes == report.updateBuild.totalBytes, "report.patch_total_mismatch");
            double expectedRatio = report.patchPayloadBytes / (double)changedAssetBytes;
            Require(Math.Abs(report.amplificationRatio - expectedRatio) <= 0.000000001d, "report.ratio_mismatch");
            long maximumObservedBundle = report.baselineBuild.files
                .Concat(report.updateBuild.files)
                .Where(file => file.kind == "bundle")
                .Select(file => file.length)
                .DefaultIfEmpty(0L)
                .Max();
            Require(maximumObservedBundle > 0, "report.bundle_missing");
            Require(
                report.maximumObservedBundleBytes == maximumObservedBundle,
                "report.maximum_observed_bundle_mismatch");
            if (report.verdict == "passed")
            {
                Require(
                    report.changedBundleGroups.Length == 1 &&
                    report.changedBundleGroups[0] == report.expectedChangedBundleGroup,
                    "report.passed_changed_groups");
                Require(
                    report.changedBundleAddresses.Length == 1 &&
                    report.changedBundleAddresses[0] == report.expectedChangedBundleAddress,
                    "report.passed_changed_addresses");
                Require(report.updateBuild.files.Length == 3, "report.passed_patch_file_count");
                Require(
                    report.updateBuild.files.Count(file =>
                        file.kind == "catalog" &&
                        file.relativePath ==
                        "remote/StandaloneWindows64/catalog_cx504.addressables.v1.json") == 1,
                    "report.passed_catalog");
                Require(
                    report.updateBuild.files.Count(file =>
                        file.kind == "catalog_hash" &&
                        file.relativePath ==
                        "remote/StandaloneWindows64/catalog_cx504.addressables.v1.hash") == 1,
                    "report.passed_catalog_hash");
                PatchFileRecord[] changedBundleFiles = report.updateBuild.files
                    .Where(file => file.kind == "bundle")
                    .ToArray();
                Require(changedBundleFiles.Length == 1, "report.passed_changed_bundle_count");
                string expectedBundlePrefix = report.expectedChangedBundleGroup +
                                              "_assets_" + report.expectedChangedBundleAddress + "_";
                string changedBundleName = Path.GetFileName(changedBundleFiles[0].relativePath);
                const string bundleSuffix = ".bundle";
                bool bundleShapeMatches = changedBundleName.StartsWith(
                                              expectedBundlePrefix,
                                              StringComparison.Ordinal) &&
                                          changedBundleName.EndsWith(
                                              bundleSuffix,
                                              StringComparison.Ordinal);
                string bundleHash = bundleShapeMatches
                    ? changedBundleName.Substring(
                        expectedBundlePrefix.Length,
                        changedBundleName.Length - expectedBundlePrefix.Length - bundleSuffix.Length)
                    : string.Empty;
                Require(
                    bundleShapeMatches && bundleHash.Length == 32 &&
                    bundleHash.All(character =>
                        (character >= '0' && character <= '9') ||
                        (character >= 'a' && character <= 'f')),
                    "report.passed_changed_bundle_identity");
                Require(report.unexpectedChangedBundles.Length == 0, "report.passed_unexpected_bundles");
                Require(report.unchangedBundleHashesPreserved, "report.passed_unchanged_hashes");
                Require(report.patchPayloadBytes <= report.maximumAllowedPatchBytes, "report.passed_threshold");
                Require(maximumObservedBundle <= report.maximumBundleBytes, "report.passed_bundle_limit");
            }

            if (requireHash) Require(IsHash(report.contentHash), "report.content_hash");
            else Require(string.IsNullOrEmpty(report.contentHash), "report.unsealed_hash");
        }

        private static void ValidateInput(PatchVideoInput input, string code)
        {
            Require(input != null, code);
            RequireId(input.logicalAddress, code + ".address");
            Require(IsHash(input.contentHash), code + ".hash");
            Require(input.length > 0, code + ".length");
            Require(input.durationMilliseconds >= 7900 && input.durationMilliseconds <= 8100, code + ".duration");
        }

        private static void ValidateInventory(PatchBuildInventory inventory, string code)
        {
            Require(inventory != null, code);
            Require(inventory.durationMilliseconds >= 0, code + ".duration");
            Require(inventory.temporaryDiskBytes >= 0, code + ".temporary_disk");
            Require(inventory.totalBytes > 0, code + ".total");
            Require(inventory.files != null && inventory.files.Length > 0, code + ".files");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            foreach (PatchFileRecord file in inventory.files)
            {
                Require(file != null, code + ".file");
                Require(IsSafeRelativePath(file.relativePath), code + ".relative_path");
                Require(paths.Add(file.relativePath), code + ".duplicate_path");
                Require(IsHash(file.contentHash), code + ".hash");
                Require(file.length > 0, code + ".length");
                Require(FileKinds.Contains(file.kind), code + ".kind");
                total = checked(total + file.length);
            }

            Require(total == inventory.totalBytes, code + ".total_mismatch");
        }

        private static bool IsSafeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
                value.Contains(":") || value.Contains("\\")) return false;
            string[] segments = value.Split('/');
            return segments.All(segment => !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != "..");
        }

        private static void ValidateTokens(IEnumerable<string> values, string code)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 160, code);
                Require(value.All(character =>
                    char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-'), code);
                Require(seen.Add(value), code + ".duplicate");
            }
        }

        private static void RequireId(string value, string code)
        {
            Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 160, code);
            Require(char.IsLetterOrDigit(value[0]), code);
            Require(value.All(character => char.IsLetterOrDigit(character) || ".:_-".Contains(character)), code);
        }

        private static bool IsHash(string value)
        {
            return value != null && value.Length == HashPrefix.Length + 64 &&
                   value.StartsWith(HashPrefix, StringComparison.Ordinal) &&
                   value.Skip(HashPrefix.Length).All(character =>
                       (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
        }

        private static string ComputeHash(string canonicalJson)
        {
            using (SHA256 sha = SHA256.Create())
                return HashPrefix + ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson)));
        }

        private static string NormalizeJsonText(string value)
        {
            return value.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) throw new InvalidDataException("CX-504 report validation failed: " + code);
        }
    }
}
