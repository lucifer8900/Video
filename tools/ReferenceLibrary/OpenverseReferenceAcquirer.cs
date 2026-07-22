using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lingmai.RedMist.ReferenceLibrary;

internal sealed class OpenverseReferenceAcquirer(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<ReferenceCatalog> AcquireAsync(
        AcquisitionPlan plan,
        string downloadsRoot,
        CancellationToken cancellationToken)
    {
        ValidatePlan(plan);
        Directory.CreateDirectory(downloadsRoot);
        var assets = new List<ReferenceAsset>();
        var usedCandidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var category in plan.Categories)
        {
            var categoryAssets = await AcquireCategoryAsync(
                plan,
                category,
                downloadsRoot,
                usedCandidates,
                cancellationToken);
            assets.AddRange(categoryAssets);
        }

        if (assets.Count < plan.MinimumTotalAssets)
        {
            throw new InvalidOperationException($"Acquired {assets.Count} assets, below minimum {plan.MinimumTotalAssets}.");
        }

        return new ReferenceCatalog
        {
            SchemaVersion = "1.0.0",
            LibraryId = "cx506-real-reference-library-v1",
            ReferenceOnly = true,
            ShipInBuild = false,
            MinimumAssetsPerCategory = plan.Categories.Min(category => category.MinimumAssets),
            AllowedLicenses = ["cc0", "pdm", "by"],
            Categories = plan.Categories.Select(category => category.Id).ToArray(),
            Assets = assets,
        };
    }

    private async Task<IReadOnlyList<ReferenceAsset>> AcquireCategoryAsync(
        AcquisitionPlan plan,
        AcquisitionCategory category,
        string downloadsRoot,
        ISet<string> usedCandidates,
        CancellationToken cancellationToken)
    {
        var assets = new List<ReferenceAsset>();
        var diverseQueries = category.Queries.Count > 0 ? category.Queries : [category.Query];
        for (var queryIndex = 0; queryIndex < diverseQueries.Count; queryIndex++)
        {
            var query = diverseQueries[queryIndex];
            var queryRequiredAnyTerms = category.QueryRequiredAnyTerms[queryIndex];
            var addedForQuery = false;
            for (var page = 1; page <= plan.MaxPagesPerCategory && !addedForQuery; page++)
            {
                var candidates = await SearchAsync(
                    plan,
                    category,
                    query,
                    queryRequiredAnyTerms,
                    page,
                    cancellationToken);
                foreach (var candidate in candidates)
                {
                    if (await TryAddCandidateAsync(
                            plan,
                            category,
                            candidate,
                            downloadsRoot,
                            usedCandidates,
                            assets,
                            cancellationToken))
                    {
                        addedForQuery = true;
                        break;
                    }
                }
            }

            if (!addedForQuery && category.RequireOnePerQuery)
            {
                throw new InvalidOperationException(
                    $"Category {category.Id} found no usable title-matched asset for query: {query}");
            }
        }

        for (var page = 1;
             !category.RequireOnePerQuery &&
             page <= plan.MaxPagesPerCategory &&
             assets.Count < category.MinimumAssets;
             page++)
        {
            var candidates = await SearchAsync(
                plan,
                category,
                category.Query,
                category.RequiredAnyTerms,
                page,
                cancellationToken);
            foreach (var candidate in candidates)
            {
                if (assets.Count >= category.MinimumAssets)
                {
                    break;
                }

                await TryAddCandidateAsync(
                    plan,
                    category,
                    candidate,
                    downloadsRoot,
                    usedCandidates,
                    assets,
                    cancellationToken);
            }
        }

        if (assets.Count < category.MinimumAssets)
        {
            throw new InvalidOperationException(
                $"Category {category.Id} acquired {assets.Count}/{category.MinimumAssets} usable assets.");
        }

        return assets;
    }

    private async Task<bool> TryAddCandidateAsync(
        AcquisitionPlan plan,
        AcquisitionCategory category,
        OpenverseCandidate candidate,
        string downloadsRoot,
        ISet<string> usedCandidates,
        ICollection<ReferenceAsset> assets,
        CancellationToken cancellationToken)
    {
        if (!usedCandidates.Add(candidate.Id))
        {
            return false;
        }

        try
        {
            var sequence = assets.Count + 1;
            var safeCandidateId = new string(candidate.Id.Where(char.IsLetterOrDigit).Take(10).ToArray()).ToLowerInvariant();
            if (safeCandidateId.Length < 4)
            {
                safeCandidateId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate.Id)))[..10].ToLowerInvariant();
            }

            var categoryDirectory = Path.Combine(downloadsRoot, category.Id);
            Directory.CreateDirectory(categoryDirectory);
            var fileStem = $"ref-{category.Id.Replace('_', '-')}-{sequence:000}-{safeCandidateId}";
            var downloaded = await ReadCachedOrDownloadAsync(
                categoryDirectory,
                fileStem,
                candidate.DownloadUrl,
                plan.MaxDownloadBytes,
                cancellationToken);
            var fileName = $"{fileStem}{downloaded.Extension}";
            var filePath = Path.Combine(categoryDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, downloaded.Bytes, cancellationToken);

            assets.Add(new ReferenceAsset
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Category = category.Id,
                Title = candidate.Title,
                Creator = candidate.Creator,
                CreatorUrl = candidate.CreatorUrl,
                Source = candidate.Source,
                SourcePageUrl = candidate.SourcePageUrl.AbsoluteUri,
                DownloadUrl = candidate.DownloadUrl.AbsoluteUri,
                License = candidate.License,
                LicenseVersion = candidate.LicenseVersion,
                LicenseUrl = candidate.LicenseUrl.AbsoluteUri,
                CommercialUseAllowed = true,
                ModificationsAllowed = true,
                ShareAlikeRequired = false,
                ReferenceOnly = true,
                ShipInBuild = false,
                IdentityReuse = false,
                ReferenceRoles = category.ReferenceRoles,
                LocalRelativePath = $"content/reference-library/raw/{category.Id}/{fileName}",
                MediaType = downloaded.MediaType,
                ByteLength = downloaded.Bytes.LongLength,
                Width = candidate.Width,
                Height = candidate.Height,
                Sha256 = Convert.ToHexString(SHA256.HashData(downloaded.Bytes)).ToLowerInvariant(),
                DownloadedAtUtc = DateTimeOffset.UtcNow,
            });
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Console.Error.WriteLine($"candidate.skipped {category.Id} {candidate.Id}: {exception.Message}");
            return false;
        }
    }

    private async Task<DownloadedImage> ReadCachedOrDownloadAsync(
        string categoryDirectory,
        string fileStem,
        Uri downloadUrl,
        long maxDownloadBytes,
        CancellationToken cancellationToken)
    {
        foreach (var cachedPath in Directory.EnumerateFiles(categoryDirectory, $"{fileStem}.*"))
        {
            var bytes = await File.ReadAllBytesAsync(cachedPath, cancellationToken);
            if (bytes.LongLength > maxDownloadBytes)
            {
                continue;
            }

            try
            {
                var cached = ReferenceImageInspector.Inspect(bytes, null);
                Console.Error.WriteLine($"candidate.cache_hit {Path.GetFileName(cachedPath)}");
                return cached;
            }
            catch (InvalidDataException)
            {
                // Ignore a stale or damaged cache entry and fetch a fresh source file.
            }
        }

        return await DownloadAsync(downloadUrl, maxDownloadBytes, cancellationToken);
    }

    private async Task<IReadOnlyList<OpenverseCandidate>> SearchAsync(
        AcquisitionPlan plan,
        AcquisitionCategory category,
        string query,
        IReadOnlyList<string> queryRequiredAnyTerms,
        int page,
        CancellationToken cancellationToken)
    {
        var endpoint = new UriBuilder(plan.DiscoveryEndpoint)
        {
            Query = string.Join("&",
                "q=" + Uri.EscapeDataString(query),
                "license=cc0%2Cpdm%2Cby",
                "license_type=commercial%2Cmodification",
                "mature=false",
                "page_size=" + plan.PageSize,
                "page=" + page),
        }.Uri;

        using var response = await GetWithRetryAsync(endpoint, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var candidates = new List<OpenverseCandidate>();
        foreach (var item in document.RootElement.GetProperty("results").EnumerateArray())
        {
            if (TryParseCandidate(item, plan, out var candidate) &&
                MatchesRelevance(candidate!, category, queryRequiredAnyTerms))
            {
                candidates.Add(candidate!);
            }
        }

        return candidates;
    }

    private static bool TryParseCandidate(
        JsonElement item,
        AcquisitionPlan plan,
        out OpenverseCandidate? candidate)
    {
        candidate = null;
        var license = GetString(item, "license").ToLowerInvariant();
        var creator = GetString(item, "creator");
        var width = GetInt(item, "width");
        var height = GetInt(item, "height");
        if (!plan.AllowedLicenses.Contains(license, StringComparer.Ordinal) ||
            width < plan.MinimumWidth ||
            height < plan.MinimumHeight ||
            !TryHttpsUri(GetString(item, "foreign_landing_url"), out var sourcePage) ||
            !TryHttpsUri(GetString(item, "url"), out var downloadUrl))
        {
            return false;
        }

        if (license == "by" && string.IsNullOrWhiteSpace(creator))
        {
            return false;
        }

        creator = string.IsNullOrWhiteSpace(creator) ? "Uncredited (source metadata)" : creator.Trim();
        var version = GetString(item, "license_version");
        var licenseUrlText = GetString(item, "license_url");
        if (!TryHttpsUri(licenseUrlText, out var licenseUrl))
        {
            licenseUrl = license switch
            {
                "cc0" => new Uri("https://creativecommons.org/publicdomain/zero/1.0/"),
                "pdm" => new Uri("https://creativecommons.org/publicdomain/mark/1.0/"),
                _ => new Uri($"https://creativecommons.org/licenses/by/{(string.IsNullOrWhiteSpace(version) ? "2.0" : version)}/"),
            };
        }

        var title = string.IsNullOrWhiteSpace(GetString(item, "title"))
            ? "Untitled reference photograph"
            : GetString(item, "title").Trim();
        candidate = new OpenverseCandidate(
            GetString(item, "id"),
            title,
            creator,
            HttpsOrNull(GetString(item, "creator_url")),
            string.Join('/', new[] { GetString(item, "provider"), GetString(item, "source") }.Where(value => !string.IsNullOrWhiteSpace(value))),
            sourcePage,
            downloadUrl,
            license,
            string.IsNullOrWhiteSpace(version) ? "unspecified" : version,
            licenseUrl,
            width,
            height,
            BuildEvidenceText(item, title));
        return !string.IsNullOrWhiteSpace(candidate.Id);
    }

    private static bool MatchesRelevance(
        OpenverseCandidate candidate,
        AcquisitionCategory category,
        IReadOnlyList<string> queryRequiredAnyTerms)
    {
        var evidence = candidate.EvidenceText;
        if (category.ExcludedTerms.Any(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (category.RequiredAnyTerms.Count > 0 &&
            !category.RequiredAnyTerms.Any(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (queryRequiredAnyTerms.Count == 0 ||
            !queryRequiredAnyTerms.Any(term => candidate.Title.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !category.RequireChinaEvidence ||
               category.ChinaEvidenceTerms.Any(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildEvidenceText(JsonElement item, string title)
    {
        var evidence = new List<string> { title };
        if (item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    evidence.Add(tag.GetString() ?? string.Empty);
                }
                else if (tag.ValueKind == JsonValueKind.Object)
                {
                    evidence.Add(GetString(tag, "name"));
                }
            }
        }

        return string.Join(' ', evidence.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private async Task<DownloadedImage> DownloadAsync(
        Uri uri,
        long maxDownloadBytes,
        CancellationToken cancellationToken)
    {
        using var response = await GetWithRetryAsync(uri, cancellationToken);
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxDownloadBytes)
        {
            throw new InvalidDataException($"Image is {length} bytes, over {maxDownloadBytes}.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maxDownloadBytes)
            {
                throw new InvalidDataException($"Image exceeded {maxDownloadBytes} bytes while downloading.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return ReferenceImageInspector.Inspect(
            output.ToArray(),
            response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is 429 or 500 or 502 or 503 or 504 && attempt < 3)
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (Exception exception) when (
                attempt < 3 && exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        throw new HttpRequestException($"Request failed after retries: {uri.Host}", lastException);
    }

    private static void ValidatePlan(AcquisitionPlan plan)
    {
        if (plan.SchemaVersion != "1.0.0" ||
            plan.Categories.Count < 12 ||
            plan.MinimumTotalAssets < 48 ||
            plan.PageSize is < 1 or > 20 ||
            plan.MaxPagesPerCategory is < 1 or > 10 ||
            plan.MaxDownloadBytes is < 1 or > 50_000_000 ||
            !plan.CommercialUseRequired ||
            !plan.ModificationRequired ||
            !plan.AllowedLicenses.SequenceEqual(["cc0", "pdm", "by"], StringComparer.Ordinal) ||
            plan.Categories.Any(category =>
                category.Queries.Count < category.MinimumAssets ||
                category.QueryMatchField != "title" ||
                !category.RequireOnePerQuery ||
                category.Queries.Any(query => !query.Contains("China", StringComparison.OrdinalIgnoreCase)) ||
                category.QueryRequiredAnyTerms.Count != category.Queries.Count ||
                category.QueryRequiredAnyTerms.Any(terms => terms.Count == 0) ||
                category.RequiredAnyTerms.Count == 0 ||
                category.ExcludedTerms.Count == 0 ||
                (category.RequireChinaEvidence && category.ChinaEvidenceTerms.Count == 0)))
        {
            throw new InvalidDataException("Acquisition plan violates CX-506 policy bounds.");
        }
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : 0;

    private static string? HttpsOrNull(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;

    private static bool TryHttpsUri(string value, out Uri uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
        uri = parsed ?? new Uri("https://invalid.invalid/");
        return valid;
    }
}
