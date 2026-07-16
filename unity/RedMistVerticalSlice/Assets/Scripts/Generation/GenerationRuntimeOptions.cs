using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lingmai.RedMist
{
    public sealed class GenerationRuntimeConfigurationException : Exception
    {
        public GenerationRuntimeConfigurationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Client-only connection policy. It never contains a provider credential or model ID.
    /// The checked-in configuration is disabled, so constructing this value cannot enable any
    /// network request or paid generation operation.
    /// </summary>
    public sealed class GenerationRuntimeOptions
    {
        internal GenerationRuntimeOptions(
            bool enabled,
            Uri apiBaseUri,
            string[] allowedMediaHosts,
            int requestTimeoutSeconds,
            int maxResponseBytes,
            long maxDownloadBytes)
        {
            Enabled = enabled;
            ApiBaseUri = apiBaseUri;
            AllowedMediaHosts = Array.AsReadOnly(
                (string[])allowedMediaHosts.Clone());
            RequestTimeoutSeconds = requestTimeoutSeconds;
            MaxResponseBytes = maxResponseBytes;
            MaxDownloadBytes = maxDownloadBytes;
        }

        public bool Enabled { get; }
        public Uri ApiBaseUri { get; }
        public IReadOnlyList<string> AllowedMediaHosts { get; }
        public int RequestTimeoutSeconds { get; }
        public int MaxResponseBytes { get; }
        public long MaxDownloadBytes { get; }

        public override string ToString()
        {
            return "GenerationRuntimeOptions(Enabled=" + Enabled + ")";
        }
    }

    public static class GenerationRuntimeOptionsLoader
    {
        public const string RelativePath = "Config/generation.runtime.json";

        private const int MaxConfigurationBytes = 65536;
        private const string SchemaVersion = "1.0.0";

        private static readonly string[] AllowedProperties =
        {
            "schemaVersion",
            "enabled",
            "apiBaseUrl",
            "allowedMediaHosts",
            "requestTimeoutSeconds",
            "maxResponseBytes",
            "maxDownloadBytes"
        };

        public static GenerationRuntimeOptions LoadFromStreamingAssets(
            string streamingAssetsRoot)
        {
            if (string.IsNullOrWhiteSpace(streamingAssetsRoot))
                throw new GenerationRuntimeConfigurationException(
                    "The StreamingAssets root is required.");

            string path;
            try
            {
                path = Path.Combine(Path.GetFullPath(streamingAssetsRoot), RelativePath);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxConfigurationBytes)
                    throw new GenerationRuntimeConfigurationException(
                        "The generation runtime configuration is missing or has an invalid size.");
                return Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
            }
            catch (GenerationRuntimeConfigurationException)
            {
                throw;
            }
            catch (Exception error) when (
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is ArgumentException ||
                error is DecoderFallbackException)
            {
                throw new GenerationRuntimeConfigurationException(
                    "The generation runtime configuration could not be read.");
            }
        }

        public static GenerationRuntimeOptions Parse(string json)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) > MaxConfigurationBytes)
                throw new GenerationRuntimeConfigurationException(
                    "The generation runtime configuration is too large.");

            StoryJsonValue root;
            try
            {
                root = StoryJson.Parse(json);
            }
            catch (Exception error) when (
                error is StoryJsonException ||
                error is ArgumentException)
            {
                throw new GenerationRuntimeConfigurationException(
                    "The generation runtime configuration is not valid JSON.");
            }

            RequireKind(root, StoryJsonKind.Object, "/");
            EnsureExactProperties(root, AllowedProperties, "/");
            if (!string.Equals(
                    RequiredString(root, "schemaVersion", "/schemaVersion"),
                    SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw Invalid("/schemaVersion");
            }

            bool enabled = RequiredBoolean(root, "enabled", "/enabled");
            string apiBaseUrl = RequiredString(root, "apiBaseUrl", "/apiBaseUrl");
            string[] allowedHosts = RequiredHosts(root, "allowedMediaHosts");
            int timeoutSeconds = RequiredInteger(
                root,
                "requestTimeoutSeconds",
                1,
                60,
                "/requestTimeoutSeconds");
            int maxResponseBytes = RequiredInteger(
                root,
                "maxResponseBytes",
                1024,
                4 * 1024 * 1024,
                "/maxResponseBytes");
            long maxDownloadBytes = RequiredLong(
                root,
                "maxDownloadBytes",
                1024 * 1024,
                2L * 1024 * 1024 * 1024,
                "/maxDownloadBytes");

            if (!enabled)
            {
                if (apiBaseUrl.Length != 0 || allowedHosts.Length != 0)
                    throw Invalid("/");
                return new GenerationRuntimeOptions(
                    false,
                    null,
                    Array.Empty<string>(),
                    timeoutSeconds,
                    maxResponseBytes,
                    maxDownloadBytes);
            }

            Uri apiBaseUri = ParseApiBaseUri(apiBaseUrl);
            if (allowedHosts.Length == 0) throw Invalid("/allowedMediaHosts");
            return new GenerationRuntimeOptions(
                true,
                apiBaseUri,
                allowedHosts,
                timeoutSeconds,
                maxResponseBytes,
                maxDownloadBytes);
        }

        private static Uri ParseApiBaseUri(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                !value.EndsWith("/", StringComparison.Ordinal) ||
                !Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
            {
                throw Invalid("/apiBaseUrl");
            }

            bool https = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
            bool explicitLoopbackHttp = string.Equals(
                                            uri.Scheme,
                                            Uri.UriSchemeHttp,
                                            StringComparison.OrdinalIgnoreCase) &&
                                        uri.IsLoopback;
            if (!https && !explicitLoopbackHttp) throw Invalid("/apiBaseUrl");
            return uri;
        }

        private static string[] RequiredHosts(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property, "/" + property);
            RequireKind(value, StoryJsonKind.Array, "/" + property);
            if (value.ArrayValue.Count > 32) throw Invalid("/" + property);

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hosts = new List<string>();
            for (int index = 0; index < value.ArrayValue.Count; index++)
            {
                StoryJsonValue item = value.ArrayValue[index];
                RequireKind(item, StoryJsonKind.String, "/" + property + "/" + index);
                string host = item.StringValue;
                if (string.IsNullOrWhiteSpace(host) ||
                    host.Length > 253 ||
                    host.IndexOf('*') >= 0 ||
                    host.IndexOf('/') >= 0 ||
                    host.IndexOf(':') >= 0 ||
                    host.IndexOf('@') >= 0 ||
                    Uri.CheckHostName(host) != UriHostNameType.Dns)
                {
                    throw Invalid("/" + property + "/" + index);
                }

                string canonical;
                try
                {
                    canonical = new IdnMapping().GetAscii(host).ToLowerInvariant();
                }
                catch (ArgumentException)
                {
                    throw Invalid("/" + property + "/" + index);
                }
                if (!unique.Add(canonical)) throw Invalid("/" + property + "/" + index);
                hosts.Add(canonical);
            }
            return hosts.ToArray();
        }

        private static int RequiredInteger(
            StoryJsonValue root,
            string property,
            int minimum,
            int maximum,
            string path)
        {
            long value = RequiredLong(root, property, minimum, maximum, path);
            return checked((int)value);
        }

        private static long RequiredLong(
            StoryJsonValue root,
            string property,
            long minimum,
            long maximum,
            string path)
        {
            StoryJsonValue value = RequiredProperty(root, property, path);
            RequireKind(value, StoryJsonKind.Number, path);
            if (!long.TryParse(
                    value.NumberToken,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long number) ||
                number < minimum ||
                number > maximum)
            {
                throw Invalid(path);
            }
            return number;
        }

        private static string RequiredString(
            StoryJsonValue root,
            string property,
            string path)
        {
            StoryJsonValue value = RequiredProperty(root, property, path);
            RequireKind(value, StoryJsonKind.String, path);
            return value.StringValue;
        }

        private static bool RequiredBoolean(
            StoryJsonValue root,
            string property,
            string path)
        {
            StoryJsonValue value = RequiredProperty(root, property, path);
            RequireKind(value, StoryJsonKind.Boolean, path);
            return value.BooleanValue;
        }

        private static StoryJsonValue RequiredProperty(
            StoryJsonValue owner,
            string property,
            string path)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value)) throw Invalid(path);
            return value;
        }

        private static void EnsureExactProperties(
            StoryJsonValue owner,
            IEnumerable<string> allowed,
            string path)
        {
            var names = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (string property in owner.ObjectValue.Keys)
            {
                if (!names.Contains(property)) throw Invalid(path + property);
            }
            foreach (string property in names)
            {
                if (!owner.ObjectValue.ContainsKey(property)) throw Invalid(path + property);
            }
        }

        private static void RequireKind(
            StoryJsonValue value,
            StoryJsonKind expected,
            string path)
        {
            if (value == null || value.Kind != expected) throw Invalid(path);
        }

        private static GenerationRuntimeConfigurationException Invalid(string path)
        {
            return new GenerationRuntimeConfigurationException(
                "The generation runtime configuration is invalid at " + path + ".");
        }
    }
}
