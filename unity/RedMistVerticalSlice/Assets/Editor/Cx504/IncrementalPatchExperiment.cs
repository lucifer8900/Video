using System;
using System.Collections.Generic;
using System.Linq;

namespace Lingmai.RedMist.Cx504
{
    public sealed class PatchEvaluation
    {
        public bool Passed { get; internal set; }
        public long ChangedAssetBytes { get; internal set; }
        public long PatchPayloadBytes { get; internal set; }
        public double AmplificationRatio { get; internal set; }
        public long MaximumAllowedPatchBytes { get; internal set; }
        public long MaximumObservedBundleBytes { get; internal set; }
        public bool PatchWithinThreshold { get; internal set; }
        public bool BundleSizeWithinLimit { get; internal set; }
        public bool UnchangedBundleHashesPreserved { get; internal set; }
        public string[] UnexpectedChangedBundles { get; internal set; }
        public string[] ChangedBundleGroups { get; internal set; }
        public string[] UnexpectedChangedBundleAddresses { get; internal set; }
        public string[] ChangedBundleAddresses { get; internal set; }
    }

    public static class AddressablesPatchEvaluator
    {
        public static PatchEvaluation Evaluate(
            long changedAssetBytes,
            long patchPayloadBytes,
            double multiplier,
            long fixedOverheadBytes,
            IEnumerable<string> changedBundleGroups,
            string expectedVideoGroup,
            IEnumerable<string> changedBundleAddresses,
            string expectedVideoAddress,
            bool unchangedBundleHashesPreserved,
            long maximumObservedBundleBytes,
            long maximumBundleBytes)
        {
            if (changedAssetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(changedAssetBytes));
            if (patchPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(patchPayloadBytes));
            if (!double.IsFinite(multiplier) || multiplier < 1d || multiplier > 2d)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            if (fixedOverheadBytes < 0) throw new ArgumentOutOfRangeException(nameof(fixedOverheadBytes));
            if (string.IsNullOrWhiteSpace(expectedVideoGroup))
                throw new ArgumentException("Expected video group is required.", nameof(expectedVideoGroup));
            if (string.IsNullOrWhiteSpace(expectedVideoAddress))
                throw new ArgumentException("Expected video address is required.", nameof(expectedVideoAddress));
            string[] groups = (changedBundleGroups ?? throw new ArgumentNullException(nameof(changedBundleGroups)))
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .ToArray();
            string[] addresses = (changedBundleAddresses ??
                                  throw new ArgumentNullException(nameof(changedBundleAddresses)))
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToArray();
            string[] unexpected = groups
                .Where(group => !string.Equals(group, expectedVideoGroup, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(group => group, StringComparer.Ordinal)
                .ToArray();
            string[] unexpectedAddresses = addresses
                .Where(address => !string.Equals(address, expectedVideoAddress, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();
            long maximumAllowed = checked((long)Math.Ceiling(changedAssetBytes * multiplier) + fixedOverheadBytes);
            bool expectedExactlyOnce = groups.Count(group =>
                string.Equals(group, expectedVideoGroup, StringComparison.Ordinal)) == 1;
            bool expectedAddressExactlyOnce = addresses.Count(address =>
                string.Equals(address, expectedVideoAddress, StringComparison.Ordinal)) == 1;
            bool patchWithin = patchPayloadBytes <= maximumAllowed;
            bool bundleWithin = maximumObservedBundleBytes > 0 &&
                                maximumObservedBundleBytes <= maximumBundleBytes;
            return new PatchEvaluation
            {
                ChangedAssetBytes = changedAssetBytes,
                PatchPayloadBytes = patchPayloadBytes,
                AmplificationRatio = patchPayloadBytes / (double)changedAssetBytes,
                MaximumAllowedPatchBytes = maximumAllowed,
                MaximumObservedBundleBytes = maximumObservedBundleBytes,
                PatchWithinThreshold = patchWithin,
                BundleSizeWithinLimit = bundleWithin,
                UnchangedBundleHashesPreserved = unchangedBundleHashesPreserved,
                UnexpectedChangedBundles = unexpected,
                ChangedBundleGroups = groups,
                UnexpectedChangedBundleAddresses = unexpectedAddresses,
                ChangedBundleAddresses = addresses,
                Passed = expectedExactlyOnce && expectedAddressExactlyOnce &&
                         unexpected.Length == 0 && unexpectedAddresses.Length == 0 && patchWithin &&
                         bundleWithin && unchangedBundleHashesPreserved
            };
        }
    }

    public static class IncrementalPatchExperiment
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                string projectRoot = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
                PatchExperimentOptions options = PatchExperimentOptions.Parse(
                    Environment.GetCommandLineArgs(),
                    projectRoot);
                PatchExperimentReport report = AddressablesPatchExperimentRunner.Execute(options);
                string json = PatchReportSerializer.SerializeAndSeal(report);
                PatchReportSerializer.WriteNew(options.ReportPath, json);
                if (!PatchReportSerializer.Verify(json))
                    throw new System.IO.InvalidDataException("CX-504 sealed report failed self-verification.");
                if (!string.Equals(report.verdict, "passed", StringComparison.Ordinal))
                    throw new System.IO.InvalidDataException("CX-504 A/B experiment failed its engineering guardrail.");

                UnityEngine.Debug.Log(
                    "CX504_RESULT:PASS patchBytes=" + report.patchPayloadBytes +
                    " changedAssetBytes=" + report.changedAssetBytes +
                    " ratio=" + report.amplificationRatio.ToString("0.000000"));
                exitCode = 0;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                UnityEngine.Debug.LogError("CX504_RESULT:FAIL");
            }
            finally
            {
                UnityEditor.EditorApplication.Exit(exitCode);
            }
        }
    }
}
