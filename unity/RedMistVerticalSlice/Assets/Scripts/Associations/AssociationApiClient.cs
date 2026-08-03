using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    public enum AssociationTransportFailure
    {
        InvalidRequest,
        NetworkUnavailable,
        ServerRejected,
        InvalidResponse
    }

    public sealed class AssociationTransportException : Exception
    {
        public AssociationTransportException(AssociationTransportFailure failure)
            : base("The association transport could not complete the request.")
        {
            Failure = failure;
        }

        public AssociationTransportFailure Failure { get; }
    }

    public sealed class AssociationHttpRequest
    {
        public AssociationHttpRequest(
            string method,
            Uri url,
            int timeoutSeconds,
            int maxResponseBytes)
        {
            if (!string.Equals(method, "GET", StringComparison.Ordinal))
                throw new ArgumentException("Association requests must use GET.", nameof(method));
            if (url == null || !url.IsAbsoluteUri)
                throw new ArgumentException("An absolute association URL is required.", nameof(url));
            if (timeoutSeconds != AssociationRuntimeOptionsLoader.RequiredHardTimeoutSeconds)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            if (maxResponseBytes != StoryThreadContractRules.MaximumDocumentBytes)
                throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

            Method = method;
            Url = url;
            TimeoutSeconds = timeoutSeconds;
            MaxResponseBytes = maxResponseBytes;
        }

        public string Method { get; }
        public Uri Url { get; }
        public int TimeoutSeconds { get; }
        public int MaxResponseBytes { get; }

        public override string ToString()
        {
            // The query binds an opaque player id and bundle identity. Do not put it in logs.
            return "AssociationHttpRequest(Method=" + Method +
                   ", Path=" + Url.AbsolutePath + ")";
        }
    }

    public sealed class AssociationHttpResponse
    {
        public AssociationHttpResponse(int statusCode, string body)
        {
            if (statusCode < 100 || statusCode > 599)
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            StatusCode = statusCode;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public int StatusCode { get; }
        public string Body { get; }
    }

    public interface IAssociationHttpTransport
    {
        Task<AssociationHttpResponse> SendAsync(
            AssociationHttpRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Strict client for the self-hosted association endpoint. The request sends only the
    /// anonymous player id and approved bundle routing context. Ledger entries, active facts,
    /// voice input, recordings, device information, and provider credentials never cross this
    /// boundary.
    /// </summary>
    public sealed class AssociationApiClient : IStoryThreadGateway
    {
        private const string EndpointPath = "association/next";
        private const string SchemaVersion = "1.0.0";
        private static readonly string[] ResponseProperties =
        {
            "schemaVersion",
            "chapterId",
            "bundleVersion",
            "bundleContentHash",
            "route",
            "upcomingNodeId",
            "thread"
        };

        private readonly AssociationRuntimeOptions _options;
        private readonly IAssociationHttpTransport _transport;

        public AssociationApiClient(
            AssociationRuntimeOptions options,
            IAssociationHttpTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<StoryThread> GetNextAsync(
            StoryThreadContext context,
            CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.Enabled || _options.ApiBaseUri == null)
                throw Failure(AssociationTransportFailure.InvalidRequest);

            Uri uri = BuildUri(context);
            if (!IsAllowedApiUri(uri))
                throw Failure(AssociationTransportFailure.InvalidRequest);
            var request = new AssociationHttpRequest(
                "GET",
                uri,
                _options.HardTimeoutSeconds,
                _options.MaxResponseBytes);

            AssociationHttpResponse response;
            try
            {
                response = await _transport.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AssociationTransportException)
            {
                throw;
            }
            catch (Exception)
            {
                throw Failure(AssociationTransportFailure.InvalidResponse);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (response == null ||
                Encoding.UTF8.GetByteCount(response.Body) > request.MaxResponseBytes)
            {
                throw Failure(AssociationTransportFailure.InvalidResponse);
            }
            if (response.StatusCode != 200)
                throw Failure(AssociationTransportFailure.ServerRejected);

            StoryThread thread;
            try
            {
                thread = ParseBoundResponse(response.Body, context);
            }
            catch (StoryThreadContractException)
            {
                throw Failure(AssociationTransportFailure.InvalidResponse);
            }
            catch (Exception)
            {
                throw Failure(AssociationTransportFailure.InvalidResponse);
            }

            if (!IsBoundToContext(thread, context))
                throw Failure(AssociationTransportFailure.InvalidResponse);
            return thread;
        }

        private static StoryThread ParseBoundResponse(
            string json,
            StoryThreadContext context)
        {
            StoryJsonValue root = StoryJson.Parse(json);
            RequireExactObject(root, ResponseProperties);
            if (!string.Equals(
                    RequiredString(root, "schemaVersion"),
                    SchemaVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(root, "chapterId"),
                    context.BundleIdentity.BundleId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(root, "bundleVersion"),
                    context.BundleIdentity.Version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(root, "bundleContentHash"),
                    context.BundleIdentity.ContentHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(root, "route"),
                    context.Route.ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(root, "upcomingNodeId"),
                    context.UpcomingNodeId,
                    StringComparison.Ordinal))
            {
                throw Failure(AssociationTransportFailure.InvalidResponse);
            }

            StoryJsonValue threadValue = Required(root, "thread");
            return StoryThreadCodec.DecodeValue(threadValue);
        }

        private static void RequireExactObject(
            StoryJsonValue value,
            IEnumerable<string> properties)
        {
            if (value == null || value.Kind != StoryJsonKind.Object)
                throw Failure(AssociationTransportFailure.InvalidResponse);
            var expected = new HashSet<string>(properties, StringComparer.Ordinal);
            if (value.ObjectValue.Count != expected.Count)
                throw Failure(AssociationTransportFailure.InvalidResponse);
            foreach (string property in value.ObjectValue.Keys)
            {
                if (!expected.Contains(property))
                    throw Failure(AssociationTransportFailure.InvalidResponse);
            }
        }

        private static string RequiredString(StoryJsonValue owner, string property)
        {
            StoryJsonValue value = Required(owner, property);
            if (value.Kind != StoryJsonKind.String)
                throw Failure(AssociationTransportFailure.InvalidResponse);
            return value.StringValue;
        }

        private static StoryJsonValue Required(StoryJsonValue owner, string property)
        {
            if (owner == null || owner.Kind != StoryJsonKind.Object ||
                !owner.TryGetProperty(property, out StoryJsonValue value))
            {
                throw Failure(AssociationTransportFailure.InvalidResponse);
            }
            return value;
        }

        private Uri BuildUri(StoryThreadContext context)
        {
            string query =
                "schemaVersion=" + Escape(SchemaVersion) +
                "&playerId=" + Escape(context.PlayerId) +
                "&chapterId=" + Escape(context.BundleIdentity.BundleId) +
                "&bundleVersion=" + Escape(context.BundleIdentity.Version) +
                "&bundleContentHash=" + Escape(context.BundleIdentity.ContentHash) +
                "&route=" + Escape(context.Route.ToString()) +
                "&upcomingNodeId=" + Escape(context.UpcomingNodeId);
            return new Uri(_options.ApiBaseUri, EndpointPath + "?" + query);
        }

        private bool IsAllowedApiUri(Uri uri)
        {
            return uri != null &&
                   uri.IsAbsoluteUri &&
                   string.Equals(uri.AbsolutePath, "/association/next", StringComparison.Ordinal) &&
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
                   string.IsNullOrEmpty(uri.Fragment);
        }

        private static bool IsBoundToContext(
            StoryThread thread,
            StoryThreadContext context)
        {
            return StoryThreadValidator.TryValidate(thread, context);
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static AssociationTransportException Failure(
            AssociationTransportFailure failure)
        {
            return new AssociationTransportException(failure);
        }
    }
}
