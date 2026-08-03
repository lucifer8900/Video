using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    public enum LedgerTransportFailure
    {
        NetworkUnavailable,
        ServerRejected,
        InvalidRequest,
        InvalidResponse
    }

    public sealed class LedgerTransportException : Exception
    {
        public LedgerTransportException(LedgerTransportFailure failure)
            : base("The ledger transport operation failed: " + failure + ".")
        {
            Failure = failure;
        }

        public LedgerTransportFailure Failure { get; }
    }

    public sealed class LedgerHttpRequest
    {
        public LedgerHttpRequest(
            string method,
            Uri url,
            string jsonBody,
            int timeoutSeconds,
            int maxResponseBytes)
        {
            if (!string.Equals(method, "POST", StringComparison.Ordinal))
                throw new ArgumentException("The ledger HTTP method is invalid.", nameof(method));
            if (url == null || !url.IsAbsoluteUri)
                throw new ArgumentException("An absolute ledger API URL is required.", nameof(url));
            if (jsonBody == null) throw new ArgumentNullException(nameof(jsonBody));
            if (timeoutSeconds < 1 || timeoutSeconds > 60)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            if (maxResponseBytes < 1024 || maxResponseBytes > 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
            Method = method;
            Url = url;
            JsonBody = jsonBody;
            TimeoutSeconds = timeoutSeconds;
            MaxResponseBytes = maxResponseBytes;
        }

        public string Method { get; }
        public Uri Url { get; }
        public string JsonBody { get; }
        public int TimeoutSeconds { get; }
        public int MaxResponseBytes { get; }

        public override string ToString() => "LedgerHttpRequest(Method=" + Method + ")";
    }

    public sealed class LedgerHttpResponse
    {
        public LedgerHttpResponse(int statusCode, string body)
        {
            if (statusCode < 100 || statusCode > 599)
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            StatusCode = statusCode;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public int StatusCode { get; }
        public string Body { get; }
    }

    public interface ILedgerHttpTransport
    {
        Task<LedgerHttpResponse> SendAsync(
            LedgerHttpRequest request,
            CancellationToken cancellationToken);
    }

    public enum LedgerAcknowledgementStatus
    {
        Accepted,
        Duplicate
    }

    public sealed class LedgerAcknowledgement
    {
        public LedgerAcknowledgement(
            LedgerEntryKey key,
            LedgerAcknowledgementStatus status)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            if (!Enum.IsDefined(typeof(LedgerAcknowledgementStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            Status = status;
        }

        public LedgerEntryKey Key { get; }
        public LedgerAcknowledgementStatus Status { get; }

        public override string ToString() => "LedgerAcknowledgement(Status=" + Status + ")";
    }

    public sealed class LedgerUploadResult
    {
        private readonly ReadOnlyCollection<LedgerAcknowledgement> _acknowledgements;

        public LedgerUploadResult(IReadOnlyList<LedgerAcknowledgement> acknowledgements)
        {
            if (acknowledgements == null) throw new ArgumentNullException(nameof(acknowledgements));
            var copy = new LedgerAcknowledgement[acknowledgements.Count];
            for (int index = 0; index < copy.Length; index++)
                copy[index] = acknowledgements[index] ?? throw new ArgumentException(
                    "A ledger acknowledgement cannot be null.", nameof(acknowledgements));
            _acknowledgements = Array.AsReadOnly(copy);
        }

        public IReadOnlyList<LedgerAcknowledgement> Acknowledgements => _acknowledgements;
    }

    public interface ILedgerBatchClient
    {
        Task<LedgerUploadResult> UploadAsync(
            IReadOnlyList<LedgerEvent> events,
            CancellationToken cancellationToken);
    }

    public sealed class LedgerApiClient : ILedgerBatchClient
    {
        private const string SchemaVersion = "1.0.0";
        private const string EndpointPath = "api/v1/ledger/events";
        private const int MaximumRequestBytes = 50 * LedgerEventCodec.MaximumSerializedBytes + 1024;
        private readonly LedgerRuntimeOptions _options;
        private readonly ILedgerHttpTransport _transport;

        public LedgerApiClient(
            LedgerRuntimeOptions options,
            ILedgerHttpTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<LedgerUploadResult> UploadAsync(
            IReadOnlyList<LedgerEvent> events,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.Enabled || _options.ApiBaseUri == null ||
                events == null || events.Count < 1 || events.Count > 50)
            {
                throw Failure(LedgerTransportFailure.InvalidRequest);
            }

            string playerId = null;
            var submitted = new HashSet<LedgerEntryKey>();
            var encoded = new string[events.Count];
            try
            {
                for (int index = 0; index < events.Count; index++)
                {
                    LedgerEvent item = events[index];
                    if (item == null) throw Failure(LedgerTransportFailure.InvalidRequest);
                    if (playerId == null) playerId = item.PlayerId;
                    else if (!string.Equals(playerId, item.PlayerId, StringComparison.Ordinal))
                        throw Failure(LedgerTransportFailure.InvalidRequest);
                    if (!submitted.Add(item.Key))
                        throw Failure(LedgerTransportFailure.InvalidRequest);
                    encoded[index] = LedgerEventCodec.Encode(item);
                }
            }
            catch (LedgerTransportException)
            {
                throw;
            }
            catch (Exception error) when (
                error is LedgerContractException || error is ArgumentException)
            {
                throw Failure(LedgerTransportFailure.InvalidRequest);
            }

            string body = EncodeBatch(encoded);
            if (Encoding.UTF8.GetByteCount(body) > MaximumRequestBytes)
                throw Failure(LedgerTransportFailure.InvalidRequest);
            var request = new LedgerHttpRequest(
                "POST",
                new Uri(_options.ApiBaseUri, EndpointPath),
                body,
                _options.RequestTimeoutSeconds,
                _options.MaxResponseBytes);

            LedgerHttpResponse response = await _transport.SendAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (response.StatusCode != 200)
                throw Failure(LedgerTransportFailure.ServerRejected);
            if (Encoding.UTF8.GetByteCount(response.Body) > _options.MaxResponseBytes)
                throw Failure(LedgerTransportFailure.InvalidResponse);
            try
            {
                return ParseResponse(response.Body, submitted);
            }
            catch (LedgerTransportException)
            {
                throw;
            }
            catch (Exception error) when (
                error is StoryJsonException || error is ArgumentException ||
                error is FormatException || error is OverflowException)
            {
                throw Failure(LedgerTransportFailure.InvalidResponse);
            }
        }

        private static string EncodeBatch(string[] encoded)
        {
            var output = new StringBuilder(64 + encoded.Length * 512);
            output.Append("{\"schemaVersion\":\"");
            output.Append(SchemaVersion);
            output.Append("\",\"events\":[");
            for (int index = 0; index < encoded.Length; index++)
            {
                if (index > 0) output.Append(',');
                output.Append(encoded[index]);
            }
            output.Append("]}");
            return output.ToString();
        }

        private static LedgerUploadResult ParseResponse(
            string json,
            HashSet<LedgerEntryKey> submitted)
        {
            StoryJsonValue root = StoryJson.Parse(json);
            RequireExactObject(root, new[] { "schemaVersion", "acknowledgements" });
            if (!string.Equals(RequiredString(root, "schemaVersion"), SchemaVersion, StringComparison.Ordinal))
                throw Failure(LedgerTransportFailure.InvalidResponse);
            StoryJsonValue values = RequiredProperty(root, "acknowledgements");
            if (values.Kind != StoryJsonKind.Array || values.ArrayValue.Count < 1 ||
                values.ArrayValue.Count > 50)
            {
                throw Failure(LedgerTransportFailure.InvalidResponse);
            }

            var result = new List<LedgerAcknowledgement>();
            var unique = new HashSet<LedgerEntryKey>();
            for (int index = 0; index < values.ArrayValue.Count; index++)
            {
                StoryJsonValue value = values.ArrayValue[index];
                RequireExactObject(value, new[] { "playerId", "entryId", "status" });
                var key = new LedgerEntryKey(
                    RequiredString(value, "playerId"),
                    RequiredString(value, "entryId"));
                if (!submitted.Contains(key) || !unique.Add(key))
                    throw Failure(LedgerTransportFailure.InvalidResponse);
                string status = RequiredString(value, "status");
                LedgerAcknowledgementStatus parsed;
                if (string.Equals(status, "accepted", StringComparison.Ordinal))
                    parsed = LedgerAcknowledgementStatus.Accepted;
                else if (string.Equals(status, "duplicate", StringComparison.Ordinal))
                    parsed = LedgerAcknowledgementStatus.Duplicate;
                else
                    throw Failure(LedgerTransportFailure.InvalidResponse);
                result.Add(new LedgerAcknowledgement(key, parsed));
            }
            return new LedgerUploadResult(result);
        }

        private static string RequiredString(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            if (value.Kind != StoryJsonKind.String)
                throw Failure(LedgerTransportFailure.InvalidResponse);
            return value.StringValue;
        }

        private static StoryJsonValue RequiredProperty(StoryJsonValue root, string property)
        {
            if (!root.TryGetProperty(property, out StoryJsonValue value))
                throw Failure(LedgerTransportFailure.InvalidResponse);
            return value;
        }

        private static void RequireExactObject(StoryJsonValue root, string[] properties)
        {
            if (root == null || root.Kind != StoryJsonKind.Object ||
                root.ObjectValue.Count != properties.Length)
            {
                throw Failure(LedgerTransportFailure.InvalidResponse);
            }
            for (int index = 0; index < properties.Length; index++)
            {
                if (!root.ObjectValue.ContainsKey(properties[index]))
                    throw Failure(LedgerTransportFailure.InvalidResponse);
            }
        }

        private static LedgerTransportException Failure(LedgerTransportFailure failure)
        {
            return new LedgerTransportException(failure);
        }
    }
}
