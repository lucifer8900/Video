using System.Buffers;
using System.Net;

namespace Lingmai.RedMist.Generation.Providers;

public sealed class ProviderArtifactDownloadOptions
{
    public IReadOnlyCollection<string> AllowedHosts { get; set; } = Array.Empty<string>();

    public long MaxArtifactBytes { get; set; }
}

public sealed class ProviderArtifactDownloadException : Exception
{
    public ProviderArtifactDownloadException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ProviderArtifactDownloader
{
    private const string UrlRejected = "provider.artifact_url_rejected";
    private const string RedirectRejected = "provider.artifact_redirect_rejected";
    private const string TooLarge = "provider.artifact_too_large";
    private const string Unavailable = "provider.artifact_unavailable";
    private const int CopyBufferBytes = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _allowedHosts;
    private readonly long _maxArtifactBytes;

    public ProviderArtifactDownloader(
        HttpClient httpClient,
        ProviderArtifactDownloadOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxArtifactBytes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum artifact size must be positive.");
        if (options.AllowedHosts is null || options.AllowedHosts.Count == 0)
            throw new ArgumentException(
                "At least one artifact host is required.",
                nameof(options));

        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string configuredHost in options.AllowedHosts)
        {
            string host = NormalizeConfiguredHost(configuredHost);
            if (!_allowedHosts.Add(host))
                throw new ArgumentException(
                    "Artifact hosts must be unique.",
                    nameof(options));
        }

        _maxArtifactBytes = options.MaxArtifactBytes;
    }

    public async Task DownloadAsync(
        Uri source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        ValidateSource(source);

        // This transport must use a dedicated unauthenticated HttpClient. Failing closed
        // prevents provider bearer credentials from being copied to an artifact origin.
        if (_httpClient.DefaultRequestHeaders.Authorization is not null ||
            _httpClient.DefaultRequestHeaders.Contains("Authorization"))
        {
            throw Sanitized(UrlRejected, "The provider artifact URL was rejected.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (IsTransportFailure(error))
        {
            throw Sanitized(Unavailable, "The provider artifact is unavailable.");
        }

        using (response)
        {
            if (IsRedirect(response.StatusCode) || WasRedirected(source, response.RequestMessage?.RequestUri))
            {
                throw Sanitized(
                    RedirectRejected,
                    "Provider artifact redirects are not allowed.");
            }

            if (!response.IsSuccessStatusCode)
            {
                // Do not read or embed a provider error body. It can contain signed URLs,
                // credentials, prompts, or other vendor-controlled sensitive data.
                throw Sanitized(Unavailable, "The provider artifact is unavailable.");
            }

            long? declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength is < 0 || declaredLength > _maxArtifactBytes)
            {
                throw Sanitized(TooLarge, "The provider artifact exceeds the size limit.");
            }

            try
            {
                await CopyWithLimitAsync(
                        response.Content,
                        destination,
                        _maxArtifactBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ProviderArtifactDownloadException)
            {
                throw;
            }
            catch (Exception error) when (IsTransportFailure(error))
            {
                throw Sanitized(Unavailable, "The provider artifact is unavailable.");
            }
        }
    }

    private static async Task CopyWithLimitAsync(
        HttpContent content,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using Stream source = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        long copied = 0;
        try
        {
            while (true)
            {
                int read = await source
                    .ReadAsync(buffer.AsMemory(0, CopyBufferBytes), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) return;

                long remaining = maxBytes - copied;
                int accepted = (int)Math.Min(read, Math.Max(remaining, 0));
                if (accepted > 0)
                {
                    await destination
                        .WriteAsync(buffer.AsMemory(0, accepted), cancellationToken)
                        .ConfigureAwait(false);
                    copied += accepted;
                }

                if (accepted != read)
                {
                    throw Sanitized(
                        TooLarge,
                        "The provider artifact exceeds the size limit.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private void ValidateSource(Uri source)
    {
        bool rejected =
            !source.IsAbsoluteUri ||
            !string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !source.IsDefaultPort ||
            !string.IsNullOrEmpty(source.UserInfo) ||
            !string.IsNullOrEmpty(source.Fragment) ||
            source.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 or UriHostNameType.Unknown ||
            !_allowedHosts.Contains(source.IdnHost);
        if (rejected)
            throw Sanitized(UrlRejected, "The provider artifact URL was rejected.");
    }

    private static string NormalizeConfiguredHost(string configuredHost)
    {
        if (string.IsNullOrWhiteSpace(configuredHost) ||
            !string.Equals(configuredHost, configuredHost.Trim(), StringComparison.Ordinal) ||
            configuredHost.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException("Artifact hosts must be exact DNS names.");
        }

        if (!Uri.TryCreate($"https://{configuredHost}/", UriKind.Absolute, out Uri? probe) ||
            probe.HostNameType != UriHostNameType.Dns ||
            !string.IsNullOrEmpty(probe.UserInfo) ||
            !probe.IsDefaultPort ||
            string.Equals(probe.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            probe.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            probe.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Artifact hosts must be public exact DNS names.");
        }

        return probe.IdnHost;
    }

    private static bool WasRedirected(Uri requested, Uri? effective) =>
        effective is not null &&
        Uri.Compare(
            requested,
            effective,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.Ordinal) != 0;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        (int)statusCode is >= 300 and <= 399;

    private static bool IsTransportFailure(Exception error) =>
        error is HttpRequestException or IOException or OperationCanceledException;

    private static ProviderArtifactDownloadException Sanitized(
        string code,
        string message) => new(code, message);
}
