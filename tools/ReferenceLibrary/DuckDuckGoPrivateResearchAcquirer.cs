using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal sealed class DuckDuckGoPrivateResearchAcquirer(HttpClient httpClient)
{
    private const long MaxDownloadBytes = 25 * 1024 * 1024;
    private readonly HttpClient _httpClient = httpClient;

    private static readonly string[] RejectedTokens =
    [
        "alamy", "dreamstime", "freepik", "gettyimages", "istockphoto", "pinterest",
        "shutterstock", "stock photo", "stock image", "premium photo", "visual china",
        "vcg.com", "699pic", "huaban", "nipic", "watermark", "wallpaper", "movie poster",
        "modern architecture", "reimagined", "reimagines",
    ];

    private static readonly HashSet<string> LandmarkStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "china", "chinese", "ancient", "historic", "historical", "traditional", "architecture",
        "building", "buildings", "exterior", "beijing", "tianjin", "hebei", "shanxi", "liaoning",
        "jilin", "heilongjiang", "shanghai", "jiangsu", "zhejiang", "anhui", "fujian", "jiangxi",
        "shandong", "henan", "hubei", "hunan", "guangdong", "guangxi", "hainan", "chongqing",
        "sichuan", "guizhou", "yunnan", "tibet", "shaanxi", "gansu", "qinghai", "ningxia",
        "xinjiang", "hong", "kong", "macau", "taiwan", "inner", "mongolia",
    };

    internal static IReadOnlyList<PrivateImageCandidate> ParseCandidates(
        string json,
        int minimumWidth,
        int minimumHeight)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray()
            .Select(ParseCandidate)
            .Where(candidate => candidate is not null)
            .Cast<PrivateImageCandidate>()
            .Where(candidate =>
                candidate.Width >= minimumWidth &&
                candidate.Height >= minimumHeight &&
                candidate.Width >= candidate.Height)
            .Where(candidate => !ContainsRejectedToken(candidate))
            .DistinctBy(candidate => candidate.ImageUrl.AbsoluteUri, StringComparer.Ordinal)
            .OrderByDescending(SourceQualityScore)
            .ThenByDescending(candidate => (long)candidate.Width * candidate.Height)
            .ToArray();
    }

    internal static string BuildSearchQuery(string search, string category)
    {
        var qualityTerms = category == "architecture"
            ? "China traditional architecture exterior real photograph empty no people high resolution"
            : "China real photograph no people high resolution";
        return $"{search} {qualityTerms} -stock -watermark -pinterest -alamy -dreamstime -shutterstock -freepik";
    }

    internal static IReadOnlyList<PrivateResearchQuery> BuildQueries(
        ChinaExpansionPlan plan,
        string category)
    {
        return ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .SelectMany(descriptor =>
            {
                var queries = descriptor.Dimension == "province"
                    ? descriptor.Target.Queries
                    : WikimediaExpansionAcquirer.FallbackQueries(descriptor);
                return queries
                    .Where(query => query.Category == category)
                    .Select(query => new PrivateResearchQuery(
                        descriptor.Dimension,
                        descriptor.Target.Id,
                        descriptor.Target.Label,
                        query.Category,
                        query.Search));
            })
            .Where(query => !string.IsNullOrWhiteSpace(query.Search))
            .DistinctBy(
                query => $"{query.Dimension}:{query.TargetId}:{query.Search}",
                StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool MatchesNamedLandmark(PrivateImageCandidate candidate, string search)
    {
        var evidence = Regex.Matches(search.ToLowerInvariant(), "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(token => token.Length >= 4 && !LandmarkStopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (evidence.Length == 0) return true;
        var candidateText = string.Join(' ',
            candidate.Title,
            candidate.ImageUrl.AbsoluteUri,
            candidate.SourcePageUrl.AbsoluteUri);
        return evidence.Any(token => candidateText.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    internal static string ValidatePrivateOutputRoot(string repositoryRoot, string outputRoot)
    {
        var repository = Path.GetFullPath(repositoryRoot);
        var allowed = Path.GetFullPath(Path.Combine(
            repository,
            "content",
            "reference-library",
            "private-research"));
        var candidate = Path.GetFullPath(outputRoot);
        var allowedWithSeparator = allowed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.Equals(allowed, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(allowedWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Private research output must stay under content/reference-library/private-research.");
        }

        return candidate;
    }

    internal async Task<PrivateResearchManifest> AcquireAsync(
        ChinaExpansionPlan plan,
        PrivateResearchManifest manifest,
        string repositoryRoot,
        string outputRoot,
        int perQuery,
        int maxQueries,
        Action<PrivateResearchManifest>? checkpoint,
        CancellationToken cancellationToken)
    {
        outputRoot = ValidatePrivateOutputRoot(repositoryRoot, outputRoot);
        if (perQuery is < 1 or > 4) throw new InvalidDataException("--per-query must be between 1 and 4.");
        if (maxQueries < 0) throw new InvalidDataException("--max-queries must be zero or greater.");

        var assets = manifest.Assets.ToList();
        var knownDownloads = assets.Select(asset => asset.DownloadUrl).ToHashSet(StringComparer.Ordinal);
        var knownHashes = assets.Select(asset => asset.Sha256).ToHashSet(StringComparer.Ordinal);
        var queries = BuildQueries(plan, manifest.Category)
            .Where(query => !QueryAttempted(outputRoot, manifest.Category, query))
            .Take(maxQueries == 0 ? int.MaxValue : maxQueries)
            .ToArray();
        foreach (var query in queries)
        {
            IReadOnlyList<PrivateImageCandidate> candidates;
            try
            {
                candidates = await SearchAsync(query, plan.MinimumWidth, plan.MinimumHeight, cancellationToken);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                Console.Error.WriteLine($"cx507.private_search_skipped {query.Dimension}.{query.TargetId}: {exception.Message}");
                MarkQueryAttempted(outputRoot, manifest.Category, query);
                continue;
            }

            var acceptedForQuery = 0;
            foreach (var candidate in candidates)
            {
                if (acceptedForQuery >= perQuery) break;
                if (knownDownloads.Contains(candidate.ImageUrl.AbsoluteUri)) continue;
                DownloadedImage downloaded;
                try
                {
                    downloaded = await DownloadAsync(candidate.ImageUrl, cancellationToken);
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
                {
                    Console.Error.WriteLine($"cx507.private_candidate_skipped {candidate.ImageUrl.Host}: {exception.Message}");
                    continue;
                }

                var sha = Convert.ToHexString(SHA256.HashData(downloaded.Bytes)).ToLowerInvariant();
                if (!knownHashes.Add(sha)) continue;
                knownDownloads.Add(candidate.ImageUrl.AbsoluteUri);
                var token = sha[..16];
                var fileName = $"private-{SafeToken(query.TargetId)}-{token}{downloaded.Extension}";
                var directory = Path.Combine(
                    outputRoot,
                    manifest.Category,
                    SafeToken(query.Dimension),
                    SafeToken(query.TargetId));
                Directory.CreateDirectory(directory);
                var localPath = Path.Combine(directory, fileName);
                await File.WriteAllBytesAsync(localPath, downloaded.Bytes, cancellationToken);
                assets.Add(new PrivateResearchAsset
                {
                    Id = Path.GetFileNameWithoutExtension(fileName),
                    Category = manifest.Category,
                    Dimension = query.Dimension,
                    TargetId = query.TargetId,
                    TargetLabel = query.TargetLabel,
                    SearchQuery = BuildSearchQuery(query.Search, manifest.Category),
                    Title = candidate.Title,
                    SourcePageUrl = candidate.SourcePageUrl.AbsoluteUri,
                    DownloadUrl = candidate.ImageUrl.AbsoluteUri,
                    LocalRelativePath = Path.GetRelativePath(repositoryRoot, localPath).Replace('\\', '/'),
                    MediaType = downloaded.MediaType,
                    ByteLength = downloaded.Bytes.LongLength,
                    Width = candidate.Width,
                    Height = candidate.Height,
                    Sha256 = sha,
                    DownloadedAtUtc = DateTimeOffset.UtcNow,
                });
                acceptedForQuery++;
                manifest = manifest with { UpdatedAtUtc = DateTimeOffset.UtcNow, Assets = assets.ToArray() };
                checkpoint?.Invoke(manifest);
            }

            MarkQueryAttempted(outputRoot, manifest.Category, query);
            Console.WriteLine(
                $"cx507.private_query {query.Dimension}.{query.TargetId} accepted={acceptedForQuery} total={assets.Count}");
        }

        return manifest with { UpdatedAtUtc = DateTimeOffset.UtcNow, Assets = assets.ToArray() };
    }

    private async Task<IReadOnlyList<PrivateImageCandidate>> SearchAsync(
        PrivateResearchQuery query,
        int minimumWidth,
        int minimumHeight,
        CancellationToken cancellationToken)
    {
        var search = BuildSearchQuery(query.Search, query.Category);
        var encoded = Uri.EscapeDataString(search);
        var landing = new Uri($"https://duckduckgo.com/?q={encoded}&iax=images&ia=images");
        var html = await _httpClient.GetStringAsync(landing, cancellationToken);
        var match = Regex.Match(
            html,
            "vqd=['\\\"](?<value>[^'\\\"]+)['\\\"]",
            RegexOptions.CultureInvariant);
        if (!match.Success) return [];

        var vqd = Uri.EscapeDataString(WebUtility.HtmlDecode(match.Groups["value"].Value));
        var endpoint = new Uri(
            $"https://duckduckgo.com/i.js?l=us-en&o=json&q={encoded}&vqd={vqd}&f=size%3ALarge%2Clayout%3AWide&type=photo&p=1");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Referrer = new Uri("https://duckduckgo.com/");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseCandidates(json, minimumWidth, minimumHeight)
            .Where(candidate => MatchesNamedLandmark(candidate, query.Search))
            .ToArray();
    }

    private async Task<DownloadedImage> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new InvalidDataException("Private research image exceeds the 25 MB limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, timeout.Token);
            if (read == 0) break;
            if (output.Length + read > MaxDownloadBytes)
            {
                throw new InvalidDataException("Private research image exceeded the 25 MB limit while downloading.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }

        return ReferenceImageInspector.Inspect(
            output.ToArray(),
            response.Content.Headers.ContentType?.MediaType);
    }

    private static bool QueryAttempted(string outputRoot, string category, PrivateResearchQuery query) =>
        File.Exists(QueryMarker(outputRoot, category, query));

    private static void MarkQueryAttempted(string outputRoot, string category, PrivateResearchQuery query)
    {
        var marker = QueryMarker(outputRoot, category, query);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "cx507 private personal research query attempted" + Environment.NewLine);
    }

    private static string QueryMarker(string outputRoot, string category, PrivateResearchQuery query)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query.Search))).ToLowerInvariant()[..16];
        return Path.Combine(
            outputRoot,
            ".state",
            SafeToken(category),
            $"{SafeToken(query.Dimension)}-{SafeToken(query.TargetId)}-{hash}.done");
    }

    private static string SafeToken(string value)
    {
        var token = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(token) ? "item" : token;
    }

    private static PrivateImageCandidate? ParseCandidate(JsonElement element)
    {
        var title = GetString(element, "title");
        var image = GetString(element, "image");
        var source = GetString(element, "url");
        if (!TryHttpUri(image, out var imageUrl) || !TryHttpUri(source, out var sourcePageUrl))
        {
            return null;
        }

        return new PrivateImageCandidate(
            title,
            imageUrl,
            sourcePageUrl,
            GetInt(element, "width"),
            GetInt(element, "height"));
    }

    private static bool TryHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool ContainsRejectedToken(PrivateImageCandidate candidate)
    {
        var text = string.Join(' ',
            candidate.Title,
            candidate.ImageUrl.Host,
            candidate.ImageUrl.AbsolutePath,
            candidate.SourcePageUrl.Host,
            candidate.SourcePageUrl.AbsolutePath);
        return RejectedTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static int SourceQualityScore(PrivateImageCandidate candidate)
    {
        var host = candidate.SourcePageUrl.Host;
        if (host.EndsWith(".gov.cn", StringComparison.OrdinalIgnoreCase)) return 50;
        if (host.EndsWith(".edu.cn", StringComparison.OrdinalIgnoreCase)) return 45;
        if (host.Contains("unesco.org", StringComparison.OrdinalIgnoreCase)) return 40;
        if (host.Contains("museum", StringComparison.OrdinalIgnoreCase)) return 35;
        if (host.Contains("wikipedia.org", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("wikimedia.org", StringComparison.OrdinalIgnoreCase)) return 30;
        if (host.EndsWith(".org", StringComparison.OrdinalIgnoreCase)) return 20;
        return 0;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : 0;
    }
}
