using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class GcsV4MediaUrlSigner : IMediaUrlSigner
{
    private const string StorageHost = "storage.googleapis.com";
    private readonly string _bucketName;
    private readonly string _serviceAccountEmail;
    private readonly TimeSpan _maxTtl;
    private readonly IV4UrlSignatureProvider _signatureProvider;
    private readonly TimeProvider _timeProvider;

    public GcsV4MediaUrlSigner(
        GcsV4SignedUrlOptions options,
        IV4UrlSignatureProvider signatureProvider,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _signatureProvider = signatureProvider ??
            throw new ArgumentNullException(nameof(signatureProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (string.IsNullOrWhiteSpace(options.BucketName) || options.BucketName.Contains('/'))
            throw new ArgumentException("GCS bucket name is invalid.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ServiceAccountEmail) ||
            !options.ServiceAccountEmail.Contains('@', StringComparison.Ordinal) ||
            options.ServiceAccountEmail.Any(char.IsControl))
        {
            throw new ArgumentException("Service account email is invalid.", nameof(options));
        }

        if (options.MaxTtl <= TimeSpan.Zero || options.MaxTtl > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(options));

        _bucketName = options.BucketName;
        _serviceAccountEmail = options.ServiceAccountEmail;
        _maxTtl = options.MaxTtl;
    }

    public async Task<SignedMediaUrl> CreateReadUrlAsync(
        string objectKey,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        MediaDistributionValidation.EnsureObjectKey(objectKey);
        if (ttl <= TimeSpan.Zero ||
            ttl > _maxTtl ||
            ttl.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new MediaPipelineException(
                "media.signed_url_ttl_invalid",
                "The signed media URL lifetime is invalid.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        string date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string timestamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string scope = $"{date}/auto/storage/goog4_request";
        string credential = $"{_serviceAccountEmail}/{scope}";
        string canonicalUri = BuildCanonicalPath(_bucketName, objectKey);
        string canonicalQuery = string.Join(
            "&",
            new[]
            {
                Pair("X-Goog-Algorithm", "GOOG4-RSA-SHA256"),
                Pair("X-Goog-Credential", credential),
                Pair("X-Goog-Date", timestamp),
                Pair(
                    "X-Goog-Expires",
                    ((long)ttl.TotalSeconds).ToString(CultureInfo.InvariantCulture)),
                Pair("X-Goog-SignedHeaders", "host"),
            }.Order(StringComparer.Ordinal));
        string canonicalRequest =
            $"GET\n{canonicalUri}\n{canonicalQuery}\nhost:{StorageHost}\n\nhost\nUNSIGNED-PAYLOAD";
        string canonicalRequestHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))
            .ToLowerInvariant();
        string stringToSign =
            $"GOOG4-RSA-SHA256\n{timestamp}\n{scope}\n{canonicalRequestHash}";

        string signature;
        try
        {
            signature = await _signatureProvider
                .SignHexAsync(stringToSign, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new MediaPipelineException(
                "media.signing_unavailable",
                "Media URL signing is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(signature) ||
            signature.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new MediaPipelineException(
                "media.signing_unavailable",
                "Media URL signing is unavailable.");
        }

        var url = new Uri(
            $"https://{StorageHost}{canonicalUri}?{canonicalQuery}" +
            $"&{Pair("X-Goog-Signature", signature.ToLowerInvariant())}");
        return new SignedMediaUrl(url, now.Add(ttl));
    }

    private static string BuildCanonicalPath(string bucketName, string objectKey) =>
        "/" + Encode(bucketName) + "/" + string.Join('/', objectKey.Split('/').Select(Encode));

    private static string Pair(string name, string value) => $"{Encode(name)}={Encode(value)}";

    private static string Encode(string value) => Uri.EscapeDataString(value)
        .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
}
