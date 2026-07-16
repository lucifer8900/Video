using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lingmai.RedMist
{
    public sealed class AssociationRuntimeConfigurationException : Exception
    {
        public AssociationRuntimeConfigurationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Connection policy for the optional StoryThread enhancement. It contains no provider
    /// credential, model identifier, ledger payload, transcript, or device information.
    /// </summary>
    public sealed class AssociationRuntimeOptions
    {
        internal AssociationRuntimeOptions(
            bool enabled,
            Uri apiBaseUri,
            int hardTimeoutSeconds,
            int maxResponseBytes)
        {
            Enabled = enabled;
            ApiBaseUri = apiBaseUri;
            HardTimeoutSeconds = hardTimeoutSeconds;
            MaxResponseBytes = maxResponseBytes;
        }

        public bool Enabled { get; }
        public Uri ApiBaseUri { get; }
        public int HardTimeoutSeconds { get; }
        public int MaxResponseBytes { get; }

        public override string ToString()
        {
            return "AssociationRuntimeOptions(Enabled=" + Enabled + ")";
        }
    }

    public static class AssociationRuntimeOptionsLoader
    {
        public const string AssociationRelativePath = "Config/association.runtime.json";
        public const string ClientRelativePath = "Config/client.runtime.json";
        public const int RequiredHardTimeoutSeconds = 8;

        private const int MaximumConfigurationBytes = 65536;
        private const int MinimumResponseBytes =
            StoryThreadContractRules.MaximumDocumentBytes;
        private const int MaximumResponseBytes =
            StoryThreadContractRules.MaximumDocumentBytes;
        private const string SchemaVersion = "1.0.0";

        private static readonly string[] AssociationProperties =
        {
            "schemaVersion",
            "enabled",
            "hardTimeoutSeconds",
            "maxResponseBytes"
        };

        private static readonly string[] ClientProperties =
        {
            "schemaVersion",
            "asrEnabled",
            "apiBaseUrl"
        };

        public static AssociationRuntimeOptions LoadFromStreamingAssets(
            string streamingAssetsRoot)
        {
            if (string.IsNullOrWhiteSpace(streamingAssetsRoot)) throw Failure();
            try
            {
                string root = Path.GetFullPath(streamingAssetsRoot);
                string associationJson = ReadBounded(
                    Path.Combine(root, AssociationRelativePath));
                string clientJson = ReadBounded(Path.Combine(root, ClientRelativePath));
                return Parse(associationJson, clientJson);
            }
            catch (AssociationRuntimeConfigurationException)
            {
                throw;
            }
            catch (Exception error) when (
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is ArgumentException ||
                error is NotSupportedException ||
                error is DecoderFallbackException)
            {
                throw Failure();
            }
        }

        public static AssociationRuntimeOptions Parse(
            string associationJson,
            string clientJson)
        {
            if (associationJson == null || clientJson == null ||
                Encoding.UTF8.GetByteCount(associationJson) > MaximumConfigurationBytes ||
                Encoding.UTF8.GetByteCount(clientJson) > MaximumConfigurationBytes)
            {
                throw Failure();
            }

            try
            {
                StoryJsonValue association = StoryJson.Parse(associationJson);
                StoryJsonValue client = StoryJson.Parse(clientJson);
                RequireObjectWithExactProperties(association, AssociationProperties);
                RequireObjectWithExactProperties(client, ClientProperties);
                RequireSchemaVersion(association);
                RequireSchemaVersion(client);

                bool enabled = RequiredBoolean(association, "enabled");
                int hardTimeout = RequiredInteger(
                    association,
                    "hardTimeoutSeconds",
                    RequiredHardTimeoutSeconds,
                    RequiredHardTimeoutSeconds);
                int maxResponseBytes = RequiredInteger(
                    association,
                    "maxResponseBytes",
                    MinimumResponseBytes,
                    MaximumResponseBytes);

                // This field is validated because client.runtime.json is a strict shared
                // contract. Its value cannot enable StoryThread networking.
                RequiredBoolean(client, "asrEnabled");
                string apiBaseUrl = RequiredString(client, "apiBaseUrl");
                Uri apiBaseUri = enabled ? ParseApiBaseUri(apiBaseUrl) : null;
                return new AssociationRuntimeOptions(
                    enabled,
                    apiBaseUri,
                    hardTimeout,
                    maxResponseBytes);
            }
            catch (AssociationRuntimeConfigurationException)
            {
                throw;
            }
            catch (Exception error) when (
                error is StoryJsonException ||
                error is ArgumentException ||
                error is OverflowException ||
                error is FormatException)
            {
                throw Failure();
            }
        }

        private static string ReadBounded(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumConfigurationBytes)
                throw Failure();
            return File.ReadAllText(path, new UTF8Encoding(false, true));
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
                throw Failure();
            }

            bool https = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
            bool loopbackHttp = string.Equals(
                                    uri.Scheme,
                                    Uri.UriSchemeHttp,
                                    StringComparison.OrdinalIgnoreCase) &&
                                uri.IsLoopback;
            if (!https && !loopbackHttp) throw Failure();
            return uri;
        }

        private static void RequireSchemaVersion(StoryJsonValue root)
        {
            if (!string.Equals(
                    RequiredString(root, "schemaVersion"),
                    SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw Failure();
            }
        }

        private static int RequiredInteger(
            StoryJsonValue root,
            string property,
            int minimum,
            int maximum)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.Number ||
                !long.TryParse(
                    value.NumberToken,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long number) ||
                number < minimum ||
                number > maximum)
            {
                throw Failure();
            }
            return checked((int)number);
        }

        private static bool RequiredBoolean(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.Boolean) throw Failure();
            return value.BooleanValue;
        }

        private static string RequiredString(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.String) throw Failure();
            return value.StringValue;
        }

        private static StoryJsonValue RequiredProperty(
            StoryJsonValue root,
            string property)
        {
            if (!root.TryGetProperty(property, out StoryJsonValue value)) throw Failure();
            return value;
        }

        private static void RequireObjectWithExactProperties(
            StoryJsonValue root,
            IEnumerable<string> properties)
        {
            if (root == null || root.Kind != StoryJsonKind.Object) throw Failure();
            var expected = new HashSet<string>(properties, StringComparer.Ordinal);
            if (root.ObjectValue.Count != expected.Count) throw Failure();
            foreach (string property in expected)
            {
                if (!root.ObjectValue.ContainsKey(property)) throw Failure();
            }
        }

        private static AssociationRuntimeConfigurationException Failure()
        {
            return new AssociationRuntimeConfigurationException(
                "The association runtime configuration is invalid or unavailable.");
        }
    }
}
