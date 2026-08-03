using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Lingmai.RedMist.Cx504.Tests
{
    public sealed class AddressablesPatchExperimentTests
    {
        [Test]
        public void EvaluationPassesOnlyOneExpectedVideoBundleWithinGuardrail()
        {
            PatchEvaluation result = AddressablesPatchEvaluator.Evaluate(
                changedAssetBytes: 8_817_938,
                patchPayloadBytes: 9_000_000,
                multiplier: 1.5,
                fixedOverheadBytes: 1_048_576,
                changedBundleGroups: new[] { "chapter-red-mist-video-remote" },
                expectedVideoGroup: "chapter-red-mist-video-remote",
                changedBundleAddresses: new[] { "cx504.chapter_red_mist_videos.target" },
                expectedVideoAddress: "cx504.chapter_red_mist_videos.target",
                unchangedBundleHashesPreserved: true,
                maximumObservedBundleBytes: 9_000_000,
                maximumBundleBytes: 2_147_483_648);

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.UnexpectedChangedBundles.Length);
            Assert.AreEqual(8_817_938, result.ChangedAssetBytes);
            Assert.AreEqual(9_000_000d / 8_817_938d, result.AmplificationRatio, 0.000000001d);
            CollectionAssert.AreEqual(
                new[] { "chapter-red-mist-video-remote" },
                result.ChangedBundleGroups);
            Assert.Greater(result.MaximumAllowedPatchBytes, result.PatchPayloadBytes);
        }

        [Test]
        public void EvaluationFailsUnexpectedBundleAmplificationOrChangedUnchangedHashes()
        {
            PatchEvaluation result = AddressablesPatchEvaluator.Evaluate(
                changedAssetBytes: 1_000,
                patchPayloadBytes: 4_000_000,
                multiplier: 1.5,
                fixedOverheadBytes: 1_048_576,
                changedBundleGroups: new[]
                {
                    "chapter-red-mist-video-remote",
                    "base-client-local"
                },
                expectedVideoGroup: "chapter-red-mist-video-remote",
                changedBundleAddresses: new[] { "cx504.chapter_red_mist_videos.target" },
                expectedVideoAddress: "cx504.chapter_red_mist_videos.target",
                unchangedBundleHashesPreserved: false,
                maximumObservedBundleBytes: 3_000_000_000,
                maximumBundleBytes: 2_147_483_648);

            Assert.IsFalse(result.Passed);
            CollectionAssert.Contains(result.UnexpectedChangedBundles, "base-client-local");
            Assert.IsFalse(result.UnchangedBundleHashesPreserved);
            Assert.IsFalse(result.PatchWithinThreshold);
            Assert.IsFalse(result.BundleSizeWithinLimit);
        }

        [Test]
        public void EvaluationFailsWhenAControlBundleChangesInsideTheExpectedVideoGroup()
        {
            PatchEvaluation result = AddressablesPatchEvaluator.Evaluate(
                changedAssetBytes: 8_817_938,
                patchPayloadBytes: 1_066_107,
                multiplier: 1.5,
                fixedOverheadBytes: 1_048_576,
                changedBundleGroups: new[] { "chapter-red-mist-video-remote" },
                expectedVideoGroup: "chapter-red-mist-video-remote",
                changedBundleAddresses: new[] { "cx504.chapter_red_mist_videos.control-01" },
                expectedVideoAddress: "cx504.chapter_red_mist_videos.target",
                unchangedBundleHashesPreserved: true,
                maximumObservedBundleBytes: 8_822_784,
                maximumBundleBytes: 2_147_483_648);

            Assert.IsFalse(result.Passed);
            CollectionAssert.AreEqual(
                new[] { "cx504.chapter_red_mist_videos.control-01" },
                result.UnexpectedChangedBundleAddresses);
        }

        [Test]
        public void ArgumentsRequireDistinctMp4InputsAndKeepGeneratedPayloadOutsideTheUnityProject()
        {
            string root = Path.Combine(Path.GetTempPath(), "cx504-options-" + Guid.NewGuid().ToString("N"));
            string projectRoot = Path.Combine(root, "project");
            string baseline = Path.Combine(root, "baseline.mp4");
            string replacement = Path.Combine(root, "replacement.mp4");
            string output = Path.Combine(root, "payload");
            string report = Path.Combine(root, "report.json");
            Directory.CreateDirectory(projectRoot);
            File.WriteAllBytes(baseline, new byte[] { 1 });
            File.WriteAllBytes(replacement, new byte[] { 2 });

            try
            {
                PatchExperimentOptions options = PatchExperimentOptions.Parse(
                    new[]
                    {
                        "-cx504BaselineVideo", baseline,
                        "-cx504ReplacementVideo", replacement,
                        "-cx504Output", output,
                        "-cx504Report", report
                    },
                    projectRoot);

                Assert.AreEqual(Path.GetFullPath(baseline), options.BaselineVideoPath);
                Assert.AreEqual(Path.GetFullPath(replacement), options.ReplacementVideoPath);
                Assert.AreEqual(Path.GetFullPath(output), options.OutputRoot);
                Assert.AreEqual(Path.GetFullPath(report), options.ReportPath);

                Assert.Throws<InvalidDataException>(() => PatchExperimentOptions.Parse(
                    new[]
                    {
                        "-cx504BaselineVideo", baseline,
                        "-cx504ReplacementVideo", baseline,
                        "-cx504Output", output,
                        "-cx504Report", report
                    },
                    projectRoot));

                Assert.Throws<InvalidDataException>(() => PatchExperimentOptions.Parse(
                    new[]
                    {
                        "-cx504BaselineVideo", baseline,
                        "-cx504ReplacementVideo", replacement,
                        "-cx504Output", Path.Combine(projectRoot, "generated"),
                        "-cx504Report", report
                    },
                    projectRoot));

                Assert.Throws<InvalidDataException>(() => PatchExperimentOptions.Parse(
                    new[]
                    {
                        "-cx504BaselineVideo", Path.GetRelativePath(Directory.GetCurrentDirectory(), baseline),
                        "-cx504ReplacementVideo", replacement,
                        "-cx504Output", output,
                        "-cx504Report", report
                    },
                    projectRoot));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ReportIsSelfHashedFailClosedPathSafeAndWrittenWithoutOverwrite()
        {
            PatchExperimentReport report = CreateReport();
            string json = PatchReportSerializer.SerializeAndSeal(report);

            Assert.IsTrue(PatchReportSerializer.Verify(json));
            StringAssert.Contains("\"mode\": \"experiment_only\"", json);
            StringAssert.Contains("\"runtimeMigration\": false", json);
            StringAssert.Contains("\"steamPipeVerified\": false", json);
            StringAssert.Contains(
                "\"authority\": \"addressables_payload_measurement_not_steam_download_authority\"",
                json);
            Assert.IsFalse(PatchReportSerializer.Verify(json.Replace("8300", "8301")));
            string unknownFieldJson = json.Insert(
                json.LastIndexOf('}'),
                ",\n  \"hostPath\": \"C:\\\\secret\\\\bundle\"\n");
            Assert.IsFalse(PatchReportSerializer.Verify(unknownFieldJson));

            PatchExperimentReport missingCatalog = CreateReport();
            missingCatalog.updateBuild.files = missingCatalog.updateBuild.files
                .Where(file => file.kind == "bundle")
                .ToArray();
            missingCatalog.updateBuild.totalBytes = 8200;
            missingCatalog.patchPayloadBytes = 8200;
            missingCatalog.amplificationRatio = 8200d / 8100d;
            missingCatalog.maximumObservedBundleBytes = 8200;
            Assert.Throws<InvalidDataException>(() =>
                PatchReportSerializer.SerializeAndSeal(missingCatalog));

            report.steamPipeVerified = true;
            Assert.Throws<InvalidDataException>(() => PatchReportSerializer.SerializeAndSeal(report));
            report.steamPipeVerified = false;
            report.baselineBuild.files[0].relativePath = @"C:\secret\bundle";
            Assert.Throws<InvalidDataException>(() => PatchReportSerializer.SerializeAndSeal(report));
            report.baselineBuild.files[0].relativePath = "baseline.bundle";
            report.limitations[0] = @"C:\secret\limitation";
            Assert.Throws<InvalidDataException>(() => PatchReportSerializer.SerializeAndSeal(report));
            report.limitations[0] = "not_runtime_migration";
            report.maximumAllowedPatchBytes++;
            Assert.Throws<InvalidDataException>(() => PatchReportSerializer.SerializeAndSeal(report));

            string root = Path.Combine(Path.GetTempPath(), "cx504-report-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "report.json");
            try
            {
                PatchReportSerializer.WriteNew(path, json);
                Assert.AreEqual(json, File.ReadAllText(path));
                Assert.Throws<IOException>(() => PatchReportSerializer.WriteNew(path, json));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static PatchExperimentReport CreateReport()
        {
            return new PatchExperimentReport
            {
                schemaVersion = "1.0.0",
                reportId = "cx504.patch.test",
                planId = "cx504.red-mist.addressables.v1",
                mode = "experiment_only",
                runtimeMigration = false,
                steamPipeVerified = false,
                productionReadiness = "not_evaluated",
                authority = "addressables_payload_measurement_not_steam_download_authority",
                buildTarget = "StandaloneWindows64",
                unityVersion = "2022.3.62f3c1",
                addressablesVersion = "1.22.3",
                startedAtUtc = "2026-07-16T16:00:00Z",
                completedAtUtc = "2026-07-16T16:01:00Z",
                baselineInput = new PatchVideoInput
                {
                    logicalAddress = "cx504.chapter_red_mist_videos.target",
                    contentHash = "sha256:" + new string('1', 64),
                    length = 8000,
                    durationMilliseconds = 8000
                },
                replacementInput = new PatchVideoInput
                {
                    logicalAddress = "cx504.chapter_red_mist_videos.target",
                    contentHash = "sha256:" + new string('2', 64),
                    length = 8100,
                    durationMilliseconds = 8000
                },
                baselineBuild = BuildInventory("baseline.bundle", '3', 8200),
                updateBuild = BuildUpdateInventory(),
                patchPayloadBytes = 8300,
                amplificationRatio = 1.024691358,
                changedAssetBytes = 8100,
                maximumAllowedPatchBytes = 1060726,
                maximumBundleBytes = 2147483648,
                maximumObservedBundleBytes = 8200,
                expectedChangedBundleGroup = "chapter-red-mist-video-remote",
                changedBundleGroups = new[] { "chapter-red-mist-video-remote" },
                expectedChangedBundleAddress = "cx504.chapter_red_mist_videos.target",
                changedBundleAddresses = new[] { "cx504.chapter_red_mist_videos.target" },
                unexpectedChangedBundles = Array.Empty<string>(),
                unchangedBundleHashesPreserved = true,
                verdict = "passed",
                limitations = new[]
                {
                    "not_runtime_migration",
                    "not_steam_download_measurement",
                    "language_and_asr_are_fixture_manifests"
                },
                contentHash = string.Empty
            };
        }

        private static PatchBuildInventory BuildInventory(string relativePath, char hash, long length)
        {
            return new PatchBuildInventory
            {
                durationMilliseconds = 100,
                temporaryDiskBytes = length,
                totalBytes = length,
                files = new[]
                {
                    new PatchFileRecord
                    {
                        relativePath = relativePath,
                        contentHash = "sha256:" + new string(hash, 64),
                        length = length,
                        kind = "bundle"
                    }
                }
            };
        }

        private static PatchBuildInventory BuildUpdateInventory()
        {
            return new PatchBuildInventory
            {
                durationMilliseconds = 100,
                temporaryDiskBytes = 8300,
                totalBytes = 8300,
                files = new[]
                {
                    new PatchFileRecord
                    {
                        relativePath = "remote/StandaloneWindows64/catalog_cx504.addressables.v1.hash",
                        contentHash = "sha256:" + new string('4', 64),
                        length = 32,
                        kind = "catalog_hash"
                    },
                    new PatchFileRecord
                    {
                        relativePath = "remote/StandaloneWindows64/catalog_cx504.addressables.v1.json",
                        contentHash = "sha256:" + new string('5', 64),
                        length = 68,
                        kind = "catalog"
                    },
                    new PatchFileRecord
                    {
                        relativePath = "remote/StandaloneWindows64/chapter-red-mist-video-remote_assets_cx504.chapter_red_mist_videos.target_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bundle",
                        contentHash = "sha256:" + new string('6', 64),
                        length = 8200,
                        kind = "bundle"
                    }
                }
            };
        }
    }
}
