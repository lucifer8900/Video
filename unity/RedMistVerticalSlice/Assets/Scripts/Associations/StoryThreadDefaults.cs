using System;
using System.IO;
using System.Text;

namespace Lingmai.RedMist
{
    public static class StoryThreadDefaults
    {
        public const string OfflinePlayerId = "p.offline";
        public const string BundleIdParameter = "bundleId";
        public const string BundleVersionParameter = "bundleVersion";
        public const string BundleContentHashParameter = "bundleContentHash";
        public const string ApprovedRedMistDefaultsHash =
            "sha256:f073bcc07d932f4b7a1b1ea8958e4d547cd6f4e2df89db2a75af12781d6f96ed";

        public static StoryThread LoadFromFile(string path, StoryThreadContext context)
        {
            return new FileStoryThreadDefaultCatalog(
                path,
                ApprovedRedMistDefaultsHash).GetDefault(context);
        }

        public static bool TryBind(
            StoryThread source,
            StoryThreadContext context,
            out StoryThread rebound)
        {
            rebound = null;
            if (source == null ||
                !StoryThreadContractRules.IsValid(context) ||
                !source.FallbackUsed ||
                !string.Equals(source.PlayerId, OfflinePlayerId, StringComparison.Ordinal) ||
                !HasIdentityBindings(source, context.BundleIdentity))
            {
                return false;
            }

            StoryThread candidate;
            try
            {
                candidate = source.RebindPlayer(context.PlayerId);
            }
            catch (Exception error) when (
                error is ArgumentException ||
                error is InvalidOperationException)
            {
                return false;
            }

            if (!StoryThreadValidator.TryValidate(candidate, context)) return false;
            rebound = candidate;
            return true;
        }

        private static bool HasIdentityBindings(
            StoryThread source,
            StoryBundleIdentity identity)
        {
            return identity != null &&
                   source.ResolvedParams != null &&
                   source.ResolvedParams.TryGetValue(
                       BundleIdParameter,
                       out string bundleId) &&
                   string.Equals(bundleId, identity.BundleId, StringComparison.Ordinal) &&
                   source.ResolvedParams.TryGetValue(
                       BundleVersionParameter,
                       out string bundleVersion) &&
                   string.Equals(bundleVersion, identity.Version, StringComparison.Ordinal) &&
                   source.ResolvedParams.TryGetValue(
                       BundleContentHashParameter,
                       out string bundleContentHash) &&
                   string.Equals(
                       bundleContentHash,
                       identity.ContentHash,
                       StringComparison.Ordinal);
        }
    }

    public sealed class FileStoryThreadDefaultCatalog : IStoryThreadDefaultCatalog
    {
        private readonly string _path;
        private readonly string _expectedContentHash;

        public FileStoryThreadDefaultCatalog(string path, string expectedContentHash)
        {
            _path = path;
            if (!StoryThreadContractRules.IsSha256(expectedContentHash))
                throw new ArgumentException(
                    "A trusted default-thread SHA-256 is required.",
                    nameof(expectedContentHash));
            _expectedContentHash = expectedContentHash;
        }

        public StoryThread GetDefault(StoryThreadContext context)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_path) ||
                    !StoryThreadContractRules.IsValid(context))
                {
                    return null;
                }

                string canonicalPath = Path.GetFullPath(_path);
                var info = new FileInfo(canonicalPath);
                if (!info.Exists ||
                    info.Length <= 0 ||
                    info.Length > StoryThreadContractRules.MaximumDocumentBytes)
                {
                    return null;
                }

                string json = new UTF8Encoding(false, true).GetString(
                    File.ReadAllBytes(canonicalPath));
                if (!HasExpectedContentHash(json)) return null;
                StoryThread source = StoryThreadCodec.Decode(json);
                return StoryThreadDefaults.TryBind(source, context, out StoryThread rebound)
                    ? rebound
                    : null;
            }
            catch (Exception error) when (
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is ArgumentException ||
                error is NotSupportedException ||
                error is DecoderFallbackException ||
                error is StoryThreadContractException)
            {
                return null;
            }
        }

        private bool HasExpectedContentHash(string json)
        {
            if (json == null) return false;
            try
            {
                return string.Equals(
                    StoryBundleLoader.ComputeContentHash(json),
                    _expectedContentHash,
                    StringComparison.Ordinal);
            }
            catch (Exception error) when (
                error is StoryJsonException ||
                error is InvalidDataException ||
                error is ArgumentException ||
                error is InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Concise production name retained for callers that do not need to know the storage type.
    /// Invalid or unavailable content always degrades to no thread.
    /// </summary>
    public sealed class StoryThreadDefaultCatalog : IStoryThreadDefaultCatalog
    {
        private readonly FileStoryThreadDefaultCatalog _files;

        public StoryThreadDefaultCatalog(string path)
        {
            _files = new FileStoryThreadDefaultCatalog(
                path,
                StoryThreadDefaults.ApprovedRedMistDefaultsHash);
        }

        public StoryThread GetDefault(StoryThreadContext context)
        {
            return _files.GetDefault(context);
        }
    }
}
