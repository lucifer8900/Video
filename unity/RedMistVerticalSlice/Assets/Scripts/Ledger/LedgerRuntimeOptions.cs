using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lingmai.RedMist
{
    public sealed class LedgerRuntimeConfigurationException : Exception
    {
        public LedgerRuntimeConfigurationException(string message)
            : base(message)
        {
        }
    }

    public sealed class LedgerRuntimeOptions
    {
        internal LedgerRuntimeOptions(
            bool enabled,
            Uri apiBaseUri,
            int requestTimeoutSeconds,
            int maxResponseBytes,
            int batchSize,
            int flushIntervalSeconds,
            int maxQueuedEvents)
        {
            Enabled = enabled;
            ApiBaseUri = apiBaseUri;
            RequestTimeoutSeconds = requestTimeoutSeconds;
            MaxResponseBytes = maxResponseBytes;
            BatchSize = batchSize;
            FlushIntervalSeconds = flushIntervalSeconds;
            MaxQueuedEvents = maxQueuedEvents;
        }

        public bool Enabled { get; }
        public Uri ApiBaseUri { get; }
        public int RequestTimeoutSeconds { get; }
        public int MaxResponseBytes { get; }
        public int BatchSize { get; }
        public int FlushIntervalSeconds { get; }
        public int MaxQueuedEvents { get; }

        public override string ToString()
        {
            return "LedgerRuntimeOptions(Enabled=" + Enabled +
                   ", BatchSize=" + BatchSize.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    public static class LedgerRuntimeOptionsLoader
    {
        public const string LedgerRelativePath = "Config/ledger.runtime.json";
        public const string ClientRelativePath = "Config/client.runtime.json";

        private const int MaximumConfigurationBytes = 65536;
        private const string SchemaVersion = "1.0.0";
        private static readonly string[] LedgerProperties =
        {
            "schemaVersion", "enabled", "requestTimeoutSeconds", "maxResponseBytes",
            "batchSize", "flushIntervalSeconds", "maxQueuedEvents"
        };
        private static readonly string[] ClientProperties =
        {
            "schemaVersion", "asrEnabled", "apiBaseUrl"
        };

        public static LedgerRuntimeOptions LoadFromStreamingAssets(string streamingAssetsRoot)
        {
            if (string.IsNullOrWhiteSpace(streamingAssetsRoot)) throw Failure();
            try
            {
                string root = Path.GetFullPath(streamingAssetsRoot);
                string ledgerJson = ReadBounded(Path.Combine(root, LedgerRelativePath));
                string clientJson = ReadBounded(Path.Combine(root, ClientRelativePath));
                return Parse(ledgerJson, clientJson);
            }
            catch (LedgerRuntimeConfigurationException)
            {
                throw;
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException ||
                error is DecoderFallbackException)
            {
                throw Failure();
            }
        }

        public static LedgerRuntimeOptions Parse(string ledgerJson, string clientJson)
        {
            if (ledgerJson == null || clientJson == null ||
                Encoding.UTF8.GetByteCount(ledgerJson) > MaximumConfigurationBytes ||
                Encoding.UTF8.GetByteCount(clientJson) > MaximumConfigurationBytes)
            {
                throw Failure();
            }

            try
            {
                StoryJsonValue ledger = StoryJson.Parse(ledgerJson);
                StoryJsonValue client = StoryJson.Parse(clientJson);
                RequireObjectWithExactProperties(ledger, LedgerProperties);
                RequireObjectWithExactProperties(client, ClientProperties);
                RequireSchemaVersion(ledger);
                RequireSchemaVersion(client);
                bool enabled = RequiredBoolean(ledger, "enabled");
                int timeout = RequiredInteger(ledger, "requestTimeoutSeconds", 1, 60);
                int maxResponse = RequiredInteger(ledger, "maxResponseBytes", 1024, 1024 * 1024);
                int batchSize = RequiredInteger(ledger, "batchSize", 1, 50);
                int flushInterval = RequiredInteger(ledger, "flushIntervalSeconds", 1, 3600);
                int maxQueued = RequiredInteger(ledger, "maxQueuedEvents", 1, 100000);
                RequiredBoolean(client, "asrEnabled");
                string apiBase = RequiredString(client, "apiBaseUrl");
                Uri apiBaseUri = enabled ? ParseApiBaseUri(apiBase) : null;
                return new LedgerRuntimeOptions(
                    enabled,
                    apiBaseUri,
                    timeout,
                    maxResponse,
                    batchSize,
                    flushInterval,
                    maxQueued);
            }
            catch (LedgerRuntimeConfigurationException)
            {
                throw;
            }
            catch (Exception error) when (
                error is StoryJsonException || error is ArgumentException ||
                error is OverflowException || error is FormatException)
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
            bool https = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool loopbackHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                uri.IsLoopback;
            if (!https && !loopbackHttp) throw Failure();
            return uri;
        }

        private static void RequireSchemaVersion(StoryJsonValue root)
        {
            if (!string.Equals(RequiredString(root, "schemaVersion"), SchemaVersion, StringComparison.Ordinal))
                throw Failure();
        }

        private static int RequiredInteger(
            StoryJsonValue root,
            string property,
            int minimum,
            int maximum)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.Number ||
                !long.TryParse(value.NumberToken, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ||
                number < minimum || number > maximum)
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

        private static StoryJsonValue RequiredProperty(StoryJsonValue root, string property)
        {
            if (!root.TryGetProperty(property, out StoryJsonValue value)) throw Failure();
            return value;
        }

        private static void RequireObjectWithExactProperties(
            StoryJsonValue root,
            string[] allowed)
        {
            if (root.Kind != StoryJsonKind.Object || root.ObjectValue.Count != allowed.Length)
                throw Failure();
            for (int index = 0; index < allowed.Length; index++)
            {
                if (!root.ObjectValue.ContainsKey(allowed[index])) throw Failure();
            }
        }

        private static LedgerRuntimeConfigurationException Failure()
        {
            return new LedgerRuntimeConfigurationException(
                "The ledger runtime configuration is invalid or unavailable.");
        }
    }
}
