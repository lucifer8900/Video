using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    public sealed class GenerationHttpRequest
    {
        public GenerationHttpRequest(
            string method,
            Uri url,
            string jsonBody,
            int timeoutSeconds,
            int maxResponseBytes)
        {
            if (!string.Equals(method, "GET", StringComparison.Ordinal) &&
                !string.Equals(method, "POST", StringComparison.Ordinal))
            {
                throw new ArgumentException("The generation HTTP method is invalid.", nameof(method));
            }
            if (url == null || !url.IsAbsoluteUri)
                throw new ArgumentException("An absolute API URL is required.", nameof(url));
            if (timeoutSeconds < 1 || timeoutSeconds > 60)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            if (maxResponseBytes < 1024 || maxResponseBytes > 4 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

            Method = method;
            Url = url;
            JsonBody = jsonBody ?? string.Empty;
            TimeoutSeconds = timeoutSeconds;
            MaxResponseBytes = maxResponseBytes;
        }

        public string Method { get; }
        public Uri Url { get; }
        public string JsonBody { get; }
        public int TimeoutSeconds { get; }
        public int MaxResponseBytes { get; }

        public override string ToString()
        {
            return "GenerationHttpRequest(Method=" + Method +
                   ", Path=" + Url.AbsolutePath + ")";
        }
    }

    public sealed class GenerationHttpResponse
    {
        public GenerationHttpResponse(int statusCode, string body)
        {
            if (statusCode < 100 || statusCode > 599)
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            StatusCode = statusCode;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public int StatusCode { get; }
        public string Body { get; }
    }

    public sealed class GenerationDownloadRequest
    {
        public GenerationDownloadRequest(
            Uri url,
            string destinationPath,
            int timeoutSeconds,
            long expectedBytes,
            long maxBytes)
        {
            Url = url != null && url.IsAbsoluteUri
                ? url
                : throw new ArgumentException("An absolute download URL is required.", nameof(url));
            DestinationPath = !string.IsNullOrWhiteSpace(destinationPath)
                ? destinationPath
                : throw new ArgumentException("A destination path is required.", nameof(destinationPath));
            if (timeoutSeconds < 1 || timeoutSeconds > 60)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            if (expectedBytes <= 0 || maxBytes <= 0 || expectedBytes > maxBytes)
                throw new ArgumentOutOfRangeException(nameof(expectedBytes));

            TimeoutSeconds = timeoutSeconds;
            ExpectedBytes = expectedBytes;
            MaxBytes = maxBytes;
        }

        public Uri Url { get; }
        public string DestinationPath { get; }
        public int TimeoutSeconds { get; }
        public long ExpectedBytes { get; }
        public long MaxBytes { get; }

        public override string ToString()
        {
            // A signed URL is a short-lived credential. Never include it or its query in logs.
            return "GenerationDownloadRequest(ExpectedBytes=" + ExpectedBytes + ")";
        }
    }

    public interface IGenerationHttpTransport
    {
        Task<GenerationHttpResponse> SendAsync(
            GenerationHttpRequest request,
            CancellationToken cancellationToken);

        Task DownloadAsync(
            GenerationDownloadRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Strict client for the self-hosted generation API. Provider credentials never cross this
    /// boundary; the only download credential is the short-lived ticket URL, which is never put
    /// into exception messages or string representations.
    /// </summary>
    public sealed class GenerationApiClient : IGenerationJobGateway, IVerifiedMediaDownloader
    {
        private const string SchemaVersion = "1.0.0";

        private readonly GenerationRuntimeOptions _options;
        private readonly IGenerationHttpTransport _transport;
        private readonly HashSet<string> _allowedMediaHosts;

        public GenerationApiClient(
            GenerationRuntimeOptions options,
            IGenerationHttpTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _allowedMediaHosts = new HashSet<string>(
                options.AllowedMediaHosts,
                StringComparer.OrdinalIgnoreCase);
        }

        public Task<GenerationJobSnapshot> CreateOrGetAsync(
            GenerationPlaybackRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsureEnabled();
            if (!IsId(request.IdempotencyKey, 128) ||
                !GenerationHashValidation.IsSha256(request.InputHash))
            {
                throw InvalidResponse();
            }

            string body = "{\"schemaVersion\":\"" + SchemaVersion +
                          "\",\"idempotencyKey\":\"" + request.IdempotencyKey +
                          "\",\"inputHash\":\"" + request.InputHash + "\"}";
            return SendStatusAsync(
                "POST",
                new Uri(_options.ApiBaseUri, "api/v1/generation/jobs"),
                body,
                202,
                cancellationToken);
        }

        public Task<GenerationJobSnapshot> GetAsync(
            string jobId,
            CancellationToken cancellationToken)
        {
            EnsureEnabled();
            string canonicalJobId = CanonicalJobId(jobId);
            return SendStatusAsync(
                "GET",
                new Uri(_options.ApiBaseUri, "api/v1/generation/jobs/" + canonicalJobId),
                string.Empty,
                200,
                cancellationToken);
        }

        public async Task<GenerationDownloadTicket> CreateDownloadTicketAsync(
            string jobId,
            CancellationToken cancellationToken)
        {
            EnsureEnabled();
            string canonicalJobId = CanonicalJobId(jobId);
            var request = Request(
                "POST",
                new Uri(
                    _options.ApiBaseUri,
                    "api/v1/generation/jobs/" + canonicalJobId + "/download-ticket"),
                string.Empty);

            GenerationHttpResponse response = await SendAsync(request, cancellationToken);
            if (response.StatusCode != 200) throw ServerRejected();
            try
            {
                return ParseTicket(response.Body);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GenerationTransportException)
            {
                throw;
            }
            catch (Exception)
            {
                throw InvalidResponse();
            }
        }

        public Task DownloadAsync(
            GenerationDownloadTicket ticket,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            EnsureEnabled();
            ValidateTicketPolicy(ticket);
            var request = new GenerationDownloadRequest(
                new Uri(ticket.DownloadUrl, UriKind.Absolute),
                destinationPath,
                _options.RequestTimeoutSeconds,
                ticket.Length,
                _options.MaxDownloadBytes);
            return _transport.DownloadAsync(request, cancellationToken);
        }

        private async Task<GenerationJobSnapshot> SendStatusAsync(
            string method,
            Uri uri,
            string body,
            int expectedStatusCode,
            CancellationToken cancellationToken)
        {
            GenerationHttpResponse response = await SendAsync(
                Request(method, uri, body),
                cancellationToken);
            if (response.StatusCode != expectedStatusCode) throw ServerRejected();
            try
            {
                return ParseStatus(response.Body);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GenerationTransportException)
            {
                throw;
            }
            catch (Exception)
            {
                throw InvalidResponse();
            }
        }

        private async Task<GenerationHttpResponse> SendAsync(
            GenerationHttpRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                GenerationHttpResponse response =
                    await _transport.SendAsync(request, cancellationToken);
                if (response == null) throw InvalidResponse();
                if (Encoding.UTF8.GetByteCount(response.Body) > request.MaxResponseBytes)
                    throw InvalidResponse();
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GenerationTransportException)
            {
                throw;
            }
            catch (Exception)
            {
                throw InvalidResponse();
            }
        }

        private GenerationHttpRequest Request(string method, Uri uri, string body)
        {
            if (!IsAllowedApiUri(uri)) throw InvalidResponse();
            return new GenerationHttpRequest(
                method,
                uri,
                body,
                _options.RequestTimeoutSeconds,
                _options.MaxResponseBytes);
        }

        private bool IsAllowedApiUri(Uri uri)
        {
            return uri != null &&
                   uri.IsAbsoluteUri &&
                   string.Equals(
                       uri.Scheme,
                       _options.ApiBaseUri.Scheme,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       uri.IdnHost,
                       _options.ApiBaseUri.IdnHost,
                       StringComparison.OrdinalIgnoreCase) &&
                   uri.Port == _options.ApiBaseUri.Port &&
                   string.IsNullOrEmpty(uri.UserInfo) &&
                   string.IsNullOrEmpty(uri.Query) &&
                   string.IsNullOrEmpty(uri.Fragment);
        }

        private GenerationJobSnapshot ParseStatus(string json)
        {
            StoryJsonValue root = ParseObject(json);
            EnsureProperties(
                root,
                new[]
                {
                    "schemaVersion",
                    "requestId",
                    "jobId",
                    "status",
                    "terminal",
                    "pollAfterMilliseconds",
                    "failureCode"
                });

            RequireSchemaVersion(root);
            RequireId(root, "requestId", 160);
            string jobId = CanonicalJobId(RequiredString(root, "jobId"));
            string status = RequiredString(root, "status");
            bool terminal = RequiredBoolean(root, "terminal");
            int pollMilliseconds = RequiredInteger(root, "pollAfterMilliseconds", 0, 60000);
            string failureCode = OptionalNullableId(root, "failureCode", 160);

            GenerationJobState state;
            switch (status)
            {
                case "created": state = GenerationJobState.Created; break;
                case "queued": state = GenerationJobState.Queued; break;
                case "generating": state = GenerationJobState.Generating; break;
                case "moderating": state = GenerationJobState.Moderating; break;
                case "transcoding": state = GenerationJobState.Transcoding; break;
                case "ready": state = GenerationJobState.Ready; break;
                case "failed": state = GenerationJobState.Failed; break;
                case "expired": state = GenerationJobState.Expired; break;
                default: throw InvalidResponse();
            }

            bool expectedTerminal = state == GenerationJobState.Ready ||
                                    state == GenerationJobState.Failed ||
                                    state == GenerationJobState.Expired;
            if (terminal != expectedTerminal) throw InvalidResponse();
            if (expectedTerminal != (pollMilliseconds == 0)) throw InvalidResponse();
            if (!expectedTerminal && failureCode.Length != 0) throw InvalidResponse();
            if (state == GenerationJobState.Ready && failureCode.Length != 0)
                throw InvalidResponse();
            if (state == GenerationJobState.Failed &&
                !string.Equals(failureCode, "generation.failed", StringComparison.Ordinal) &&
                !string.Equals(failureCode, "moderation.rejected", StringComparison.Ordinal))
            {
                throw InvalidResponse();
            }
            if (state == GenerationJobState.Expired &&
                !string.Equals(failureCode, "generation.expired", StringComparison.Ordinal))
            {
                throw InvalidResponse();
            }
            return new GenerationJobSnapshot(jobId, state, failureCode);
        }

        private GenerationDownloadTicket ParseTicket(string json)
        {
            StoryJsonValue root = ParseObject(json);
            EnsureProperties(
                root,
                new[]
                {
                    "schemaVersion",
                    "requestId",
                    "mediaId",
                    "contentHash",
                    "downloadUrl",
                    "expiresAtUtc",
                    "contentType",
                    "length"
                });
            RequireSchemaVersion(root);
            RequireId(root, "requestId", 160);
            string mediaId = RequireId(root, "mediaId", 160);
            string hash = RequiredString(root, "contentHash");
            string url = RequiredString(root, "downloadUrl");
            string expiry = RequiredString(root, "expiresAtUtc");
            string contentType = RequiredString(root, "contentType");
            long length = RequiredLong(root, "length", 1, _options.MaxDownloadBytes);

            if (!GenerationHashValidation.IsSha256(hash) ||
                !string.Equals(contentType, "video/mp4", StringComparison.Ordinal) ||
                url.Length > 8192 ||
                !DateTimeOffset.TryParse(
                    expiry,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset expiresAtUtc))
            {
                throw InvalidResponse();
            }

            var ticket = new GenerationDownloadTicket(
                mediaId,
                hash,
                url,
                expiresAtUtc.ToUniversalTime(),
                contentType,
                length);
            ValidateTicketPolicy(ticket);
            return ticket;
        }

        private void ValidateTicketPolicy(GenerationDownloadTicket ticket)
        {
            if (ticket == null ||
                !GenerationHashValidation.IsSha256(ticket.ContentHash) ||
                ticket.Length <= 0 ||
                ticket.Length > _options.MaxDownloadBytes ||
                !string.Equals(ticket.ContentType, "video/mp4", StringComparison.Ordinal) ||
                !Uri.TryCreate(ticket.DownloadUrl, UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !uri.IsDefaultPort ||
                !_allowedMediaHosts.Contains(uri.IdnHost))
            {
                throw InvalidResponse();
            }
        }

        private static StoryJsonValue ParseObject(string json)
        {
            if (string.IsNullOrEmpty(json)) throw InvalidResponse();
            StoryJsonValue root = StoryJson.Parse(json);
            if (root.Kind != StoryJsonKind.Object) throw InvalidResponse();
            return root;
        }

        private static void EnsureProperties(
            StoryJsonValue root,
            IEnumerable<string> expectedProperties)
        {
            var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
            if (root.ObjectValue.Count != expected.Count) throw InvalidResponse();
            foreach (string property in root.ObjectValue.Keys)
            {
                if (!expected.Contains(property)) throw InvalidResponse();
            }
        }

        private static void RequireSchemaVersion(StoryJsonValue root)
        {
            if (!string.Equals(
                    RequiredString(root, "schemaVersion"),
                    SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw InvalidResponse();
            }
        }

        private static string RequireId(StoryJsonValue root, string property, int maxLength)
        {
            string value = RequiredString(root, property);
            if (!IsId(value, maxLength)) throw InvalidResponse();
            return value;
        }

        private static string OptionalNullableId(
            StoryJsonValue root,
            string property,
            int maxLength)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind == StoryJsonKind.Null) return string.Empty;
            if (value.Kind != StoryJsonKind.String || !IsId(value.StringValue, maxLength))
                throw InvalidResponse();
            return value.StringValue;
        }

        private static string RequiredString(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.String) throw InvalidResponse();
            return value.StringValue;
        }

        private static bool RequiredBoolean(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.Boolean) throw InvalidResponse();
            return value.BooleanValue;
        }

        private static int RequiredInteger(
            StoryJsonValue root,
            string property,
            int minimum,
            int maximum)
        {
            long value = RequiredLong(root, property, minimum, maximum);
            return checked((int)value);
        }

        private static long RequiredLong(
            StoryJsonValue root,
            string property,
            long minimum,
            long maximum)
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
                throw InvalidResponse();
            }
            return number;
        }

        private static StoryJsonValue RequiredProperty(StoryJsonValue root, string property)
        {
            if (!root.TryGetProperty(property, out StoryJsonValue value))
                throw InvalidResponse();
            return value;
        }

        private static string CanonicalJobId(string jobId)
        {
            if (!Guid.TryParseExact(jobId, "D", out Guid parsed)) throw InvalidResponse();
            return parsed.ToString("D");
        }

        private static bool IsId(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid = character >= 'A' && character <= 'Z' ||
                             character >= 'a' && character <= 'z' ||
                             character >= '0' && character <= '9' ||
                             character == '.' ||
                             character == '_' ||
                             character == ':' ||
                             character == '-';
                if (!valid || index == 0 && !IsAsciiAlphaNumeric(character)) return false;
            }
            return true;
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return value >= 'A' && value <= 'Z' ||
                   value >= 'a' && value <= 'z' ||
                   value >= '0' && value <= '9';
        }

        private void EnsureEnabled()
        {
            if (!_options.Enabled || _options.ApiBaseUri == null) throw ServerRejected();
        }

        private static GenerationTransportException InvalidResponse()
        {
            return new GenerationTransportException(GenerationTransportFailure.InvalidResponse);
        }

        private static GenerationTransportException ServerRejected()
        {
            return new GenerationTransportException(GenerationTransportFailure.ServerRejected);
        }
    }
}
