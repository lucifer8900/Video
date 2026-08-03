using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.ReferenceLibrary;

internal sealed partial class WikimediaExpansionAcquirer(HttpClient httpClient)
{
    internal const int MaxDiversityCandidateAttemptsPerQuery = 4;
    internal const int MetMuseumObjectPageSize = 8;
    internal const int MetMuseumMaxConcurrentObjectRequests = 1;
    internal const int CandidateDownloadTimeoutSeconds = 45;
    internal const int MaxConcurrentDiversityQueries = 2;
    internal const int ArtInstituteChicagoMaxConcurrentRequests = 1;
    internal const int ArtInstituteChicagoMinimumRequestDelayMilliseconds = 1000;
    private const int ArtInstituteChicagoImageWidth = 1686;

    private static readonly SemaphoreSlim ArtInstituteChicagoRequestGate =
        new(ArtInstituteChicagoMaxConcurrentRequests, ArtInstituteChicagoMaxConcurrentRequests);
    private static DateTimeOffset _artInstituteChicagoLastRequestUtc = DateTimeOffset.MinValue;

    private static readonly HashSet<string> NaturalSceneCategories = new(StringComparer.Ordinal)
    {
        "landscape", "plants", "animals", "waters", "weather", "astronomy",
        "geology_caves", "light_fog_fire",
    };

    private static readonly string[] NaturalSceneObjectExclusionTerms =
    [
        "artwork", "sculpture", "carving", "ornament", "textile", "decorative pattern",
        "fuchi", "tsuba", "woodblock print", "museum object", "book binding",
    ];

    private static readonly string[] PeopleOutOfScopeEvidenceTerms =
    [
        "artwork", "bronze sculpture", "sculpture", "statue", "figurine",
        "engraving", "lithograph", "woodcut", "wax figure", "mural", "Ainu people",
        "beheaded", "beheading", "decapitation", "goitre", "goiter", "bound feet",
    ];

    private readonly HttpClient _httpClient = httpClient;
    private readonly ConcurrentDictionary<string, string> _wikimediaCategoryContinuations =
        new(StringComparer.Ordinal);

    public async Task<ReferenceCatalog> ExpandAsync(
        ChinaExpansionPlan plan,
        ReferenceCatalog baseCatalog,
        string downloadsRoot,
        string cacheRoot,
        Action<ReferenceCatalog>? checkpoint,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? focusedCategories = null,
        IReadOnlySet<string>? focusedQueryIds = null,
        IReadOnlySet<string>? focusedTargetIds = null)
    {
        Directory.CreateDirectory(downloadsRoot);
        Directory.CreateDirectory(cacheRoot);
        var assets = TagExistingCategoryCoverage(
            plan,
            baseCatalog.Assets.Select(UpgradeLegacyAsset).ToArray()).ToList();
        var sourceIndex = BuildIndex(assets, asset => asset.SourcePageUrl);
        var downloadIndex = BuildIndex(assets, asset => asset.DownloadUrl);
        var hashIndex = BuildIndex(assets, asset => asset.Sha256);
        var usedIds = assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var categorySequences = assets.GroupBy(asset => asset.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var descriptor in OrderTargetsForAcquisition(plan, focusedCategories, focusedTargetIds))
        {
            var acquisitionQueries = QueriesForAcquisition(descriptor, focusedQueryIds);
            if (ShouldUseDiversityAcquisition(focusedQueryIds) &&
                !TargetSatisfied(assets, descriptor) &&
                acquisitionQueries.Any(query =>
                    query.MinimumAssets.HasValue &&
                    !QueryQuotaSatisfied(assets, descriptor, query) &&
                    !QueryAttempted(cacheRoot, descriptor, query)))
            {
                await AcquireDiversityTargetAsync(
                    plan,
                    descriptor,
                    assets,
                    sourceIndex,
                    downloadIndex,
                    hashIndex,
                    usedIds,
                    categorySequences,
                    downloadsRoot,
                    cacheRoot,
                    cancellationToken,
                    focusedQueryIds);
                checkpoint?.Invoke(BuildCatalog(plan, baseCatalog, assets));
            }

            if (TargetSatisfied(assets, descriptor))
            {
                Console.WriteLine($"cx507.target cache_complete {descriptor.TargetId} total={assets.Count}");
                continue;
            }

            var quotaQueries = focusedQueryIds is { Count: > 0 }
                ? acquisitionQueries
                : FallbackQueries(descriptor);
            foreach (var query in quotaQueries)
            {
                if (descriptor.Dimension == "province" &&
                    !string.IsNullOrWhiteSpace(query.Id) &&
                    QueryAttempted(cacheRoot, descriptor, query))
                {
                    continue;
                }

                if (QueryQuotaSatisfied(assets, descriptor, query))
                {
                    continue;
                }

                for (var page = 0;
                     page < plan.MaxPagesPerQuery &&
                     !TargetSatisfied(assets, descriptor) &&
                     !QueryQuotaSatisfied(assets, descriptor, query);
                     page++)
                {
                    var candidates = await SearchAsync(
                        plan,
                        descriptor,
                        query,
                        page,
                        cancellationToken,
                        allowShortfallException: plan.UnverifiedSourcePolicy.Allow);
                    if (candidates.Count == 0)
                    {
                        break;
                    }

                    var pending = new List<WikimediaCandidate>();
                    foreach (var candidate in candidates)
                    {
                        if (TargetSatisfied(assets, descriptor) || QueryQuotaSatisfied(assets, descriptor, query))
                        {
                            break;
                        }

                        if (sourceIndex.TryGetValue(candidate.SourcePageUrl.AbsoluteUri, out var sourceAssetIndex))
                        {
                            assets[sourceAssetIndex] = AddCoverage(assets[sourceAssetIndex], descriptor, query);
                            continue;
                        }

                        if (downloadIndex.TryGetValue(candidate.DownloadUrl.AbsoluteUri, out var downloadAssetIndex))
                        {
                            assets[downloadAssetIndex] = AddCoverage(assets[downloadAssetIndex], descriptor, query);
                            continue;
                        }

                        if (pending.All(item =>
                                item.SourcePageUrl != candidate.SourcePageUrl &&
                                item.DownloadUrl != candidate.DownloadUrl))
                        {
                            pending.Add(candidate);
                        }

                        if (pending.Count >= Math.Min(4, Math.Max(1, RemainingForQuery(assets, descriptor, query) + 1)))
                        {
                            break;
                        }
                    }

                    var downloadTasks = pending.Select(candidate => DownloadCandidateSafeAsync(
                        plan,
                        descriptor,
                        query.Category,
                        candidate,
                        cacheRoot,
                        cancellationToken));
                    var results = await Task.WhenAll(downloadTasks);
                    foreach (var result in results.Where(result => result is not null).Cast<CandidateDownload>())
                    {
                        if (TargetSatisfied(assets, descriptor) || QueryQuotaSatisfied(assets, descriptor, query))
                        {
                            break;
                        }

                        var candidate = result.Candidate;
                        var downloaded = result.Image;
                        var sha = Convert.ToHexString(SHA256.HashData(downloaded.Bytes)).ToLowerInvariant();
                        if (hashIndex.TryGetValue(sha, out var hashAssetIndex))
                        {
                            assets[hashAssetIndex] = AddCoverage(assets[hashAssetIndex], descriptor, query);
                            continue;
                        }

                        var sequence = categorySequences.GetValueOrDefault(query.Category) + 1;
                        categorySequences[query.Category] = sequence;
                        var candidateToken = CandidateToken(candidate.Source, candidate.Id);
                        var fileName = $"ref-{query.Category.Replace('_', '-')}-{sequence:000}-{candidateToken}{downloaded.Extension}";
                        var categoryDirectory = Path.Combine(downloadsRoot, query.Category);
                        Directory.CreateDirectory(categoryDirectory);
                        var localPath = Path.Combine(categoryDirectory, fileName);
                        await File.WriteAllBytesAsync(localPath, downloaded.Bytes, cancellationToken);

                        var id = Path.GetFileNameWithoutExtension(fileName);
                        if (!usedIds.Add(id))
                        {
                            throw new InvalidDataException($"Generated duplicate asset id: {id}");
                        }

                        var asset = AddCoverage(new ReferenceAsset
                        {
                            Id = id,
                            Category = query.Category,
                            Title = candidate.Title,
                            Creator = candidate.Creator,
                            CreatorUrl = candidate.CreatorUrl,
                            Source = candidate.Source,
                            SourcePageUrl = candidate.SourcePageUrl.AbsoluteUri,
                            DownloadUrl = candidate.DownloadUrl.AbsoluteUri,
                            License = candidate.License,
                            LicenseVersion = candidate.LicenseVersion,
                            LicenseUrl = candidate.LicenseUrl?.AbsoluteUri,
                            CommercialUseAllowed = candidate.License != "unverified",
                            ModificationsAllowed = candidate.License != "unverified",
                            ShareAlikeRequired = false,
                            ReferenceOnly = true,
                            ShipInBuild = false,
                            IdentityReuse = false,
                            ReferenceRoles = RolesFor(query.Category),
                            LocalRelativePath = $"content/reference-library/raw/{query.Category}/{fileName}",
                            MediaType = downloaded.MediaType,
                            ByteLength = downloaded.Bytes.LongLength,
                            Width = downloaded.Width,
                            Height = downloaded.Height,
                            Sha256 = sha,
                            DownloadedAtUtc = DateTimeOffset.UtcNow,
                            CurationStatus = "needs_review",
                            SourceVerification = candidate.License == "unverified" ? "unverified" : "verified",
                            ShortfallException = ShortfallExceptionFor(
                                plan,
                                candidate,
                                downloaded,
                                targetShortfall: !TargetSatisfied(assets, descriptor)),
                        }, descriptor, query);
                        var newIndex = assets.Count;
                        assets.Add(asset);
                        sourceIndex[asset.SourcePageUrl] = newIndex;
                        downloadIndex[asset.DownloadUrl] = newIndex;
                        hashIndex[asset.Sha256] = newIndex;

                        // Accepted bytes live only in raw; cache is for rejected/retry candidates.
                        if (File.Exists(result.CachePath))
                        {
                            File.Delete(result.CachePath);
                        }
                    }

                    checkpoint?.Invoke(BuildCatalog(plan, baseCatalog, assets));
                }
            }

            var targetCount = TargetAssets(assets, descriptor).Count;
            Console.WriteLine($"cx507.target {(TargetSatisfied(assets, descriptor) ? "complete" : "shortfall")} {descriptor.TargetId} {targetCount}/{descriptor.Target.MinimumAssets} total={assets.Count}");
            checkpoint?.Invoke(BuildCatalog(plan, baseCatalog, assets));
        }

        return BuildCatalog(plan, baseCatalog, assets);
    }

    internal static IReadOnlyList<ExpansionTargetDescriptor> OrderTargetsForAcquisition(
        ChinaExpansionPlan plan,
        IReadOnlySet<string>? focusedCategories = null,
        IReadOnlySet<string>? focusedTargetIds = null)
    {
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan).ToArray();
        if (focusedTargetIds is { Count: > 0 })
        {
            targets = targets
                .Where(target => focusedTargetIds.Contains(target.TargetId))
                .ToArray();
        }
        if (focusedCategories is { Count: > 0 })
        {
            targets = targets
                .Where(target => target.Dimension == "category" && focusedCategories.Contains(target.Target.Id))
                .ToArray();
        }

        return targets.Where(target => target.Dimension == "category")
            .Concat(targets.Where(target => target.Dimension != "category"))
            .ToArray();
    }

    internal static IReadOnlyList<ExpansionQuery> QueriesForAcquisition(
        ExpansionTargetDescriptor descriptor,
        IReadOnlySet<string>? focusedQueryIds = null)
    {
        return focusedQueryIds is { Count: > 0 }
            ? descriptor.Target.Queries
                .Where(query =>
                    !string.IsNullOrWhiteSpace(query.Id) && focusedQueryIds.Contains(query.Id))
                .ToArray()
            : descriptor.Target.Queries;
    }

    internal static bool ShouldUseDiversityAcquisition(
        IReadOnlySet<string>? focusedQueryIds) => focusedQueryIds is not { Count: > 0 };

    private static ReferenceCatalog BuildCatalog(
        ChinaExpansionPlan plan,
        ReferenceCatalog baseCatalog,
        IReadOnlyList<ReferenceAsset> assets) => new()
        {
            SchemaVersion = "2.0.0",
            LibraryId = "cx507-china-reference-library-v2",
            CoveragePlanId = plan.PlanId,
            ReferenceOnly = true,
            ShipInBuild = false,
            MinimumAssetsPerCategory = 4,
            AllowedLicenses = ["cc0", "pdm", "by", "unverified"],
            Categories = baseCatalog.Categories.Count > 0
                ? baseCatalog.Categories
                : ["landscape", "architecture", "plants", "animals", "waters", "weather", "astronomy", "people", "costumes_textiles", "artifacts", "geology_caves", "light_fog_fire"],
            Assets = assets,
        };

    private async Task AcquireDiversityTargetAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        List<ReferenceAsset> assets,
        IDictionary<string, int> sourceIndex,
        IDictionary<string, int> downloadIndex,
        IDictionary<string, int> hashIndex,
        ISet<string> usedIds,
        IDictionary<string, int> categorySequences,
        string downloadsRoot,
        string cacheRoot,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? focusedQueryIds = null)
    {
        var missingQueries = QueriesForAcquisition(descriptor, focusedQueryIds)
            .Where(query =>
                !QueryQuotaSatisfied(assets, descriptor, query) &&
                !QueryAttempted(cacheRoot, descriptor, query))
            .ToArray();
        using var gate = new SemaphoreSlim(MaxConcurrentDiversityQueries);
        var tasks = missingQueries.Select(async query =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var results = await FindDiversityCandidatesAsync(
                    plan,
                    descriptor,
                    query,
                    RemainingForQuery(assets, descriptor, query),
                    assets,
                    sourceIndex,
                    downloadIndex,
                    cacheRoot,
                    cancellationToken);
                if (ShouldPersistDiversityAttemptMarker(descriptor))
                {
                    MarkQueryAttempted(cacheRoot, descriptor, query);
                }
                return results;
            }
            finally
            {
                gate.Release();
            }
        });
        var queryResults = await Task.WhenAll(tasks);
        var results = queryResults.SelectMany(result => result).ToArray();

        foreach (var result in results)
        {
            var query = result.Query;
            var candidate = result.Candidate;
            if (QueryQuotaSatisfied(assets, descriptor, query))
            {
                continue;
            }

            if (sourceIndex.TryGetValue(candidate.SourcePageUrl.AbsoluteUri, out var sourceAssetIndex))
            {
                assets[sourceAssetIndex] = AddCoverage(assets[sourceAssetIndex], descriptor, query);
                continue;
            }

            if (downloadIndex.TryGetValue(candidate.DownloadUrl.AbsoluteUri, out var downloadAssetIndex))
            {
                assets[downloadAssetIndex] = AddCoverage(assets[downloadAssetIndex], descriptor, query);
                continue;
            }

            if (result.Download is null)
            {
                continue;
            }

            var downloaded = result.Download.Image;
            var sha = Convert.ToHexString(SHA256.HashData(downloaded.Bytes)).ToLowerInvariant();
            if (hashIndex.TryGetValue(sha, out var hashAssetIndex))
            {
                assets[hashAssetIndex] = AddCoverage(assets[hashAssetIndex], descriptor, query);
                continue;
            }

            var sequence = (categorySequences.TryGetValue(query.Category, out var currentSequence)
                ? currentSequence
                : 0) + 1;
            categorySequences[query.Category] = sequence;
            var candidateToken = CandidateToken(candidate.Source, candidate.Id);
            var fileName = $"ref-{query.Category.Replace('_', '-')}-{sequence:000}-{candidateToken}{downloaded.Extension}";
            var categoryDirectory = Path.Combine(downloadsRoot, query.Category);
            Directory.CreateDirectory(categoryDirectory);
            await File.WriteAllBytesAsync(Path.Combine(categoryDirectory, fileName), downloaded.Bytes, cancellationToken);
            var id = Path.GetFileNameWithoutExtension(fileName);
            if (!usedIds.Add(id))
            {
                throw new InvalidDataException($"Generated duplicate asset id: {id}");
            }

            var asset = AddCoverage(new ReferenceAsset
            {
                Id = id,
                Category = query.Category,
                Title = candidate.Title,
                Creator = candidate.Creator,
                CreatorUrl = candidate.CreatorUrl,
                Source = candidate.Source,
                SourcePageUrl = candidate.SourcePageUrl.AbsoluteUri,
                DownloadUrl = candidate.DownloadUrl.AbsoluteUri,
                License = candidate.License,
                LicenseVersion = candidate.LicenseVersion,
                LicenseUrl = candidate.LicenseUrl?.AbsoluteUri,
                CommercialUseAllowed = candidate.License != "unverified",
                ModificationsAllowed = candidate.License != "unverified",
                ShareAlikeRequired = false,
                ReferenceOnly = true,
                ShipInBuild = false,
                IdentityReuse = false,
                ReferenceRoles = RolesFor(query.Category),
                LocalRelativePath = $"content/reference-library/raw/{query.Category}/{fileName}",
                MediaType = downloaded.MediaType,
                ByteLength = downloaded.Bytes.LongLength,
                Width = downloaded.Width,
                Height = downloaded.Height,
                Sha256 = sha,
                DownloadedAtUtc = DateTimeOffset.UtcNow,
                CurationStatus = "needs_review",
                SourceVerification = candidate.License == "unverified" ? "unverified" : "verified",
                ShortfallException = ShortfallExceptionFor(
                    plan,
                    candidate,
                    downloaded,
                    targetShortfall: !TargetSatisfied(assets, descriptor)),
            }, descriptor, query);
            var newIndex = assets.Count;
            assets.Add(asset);
            sourceIndex[asset.SourcePageUrl] = newIndex;
            downloadIndex[asset.DownloadUrl] = newIndex;
            hashIndex[asset.Sha256] = newIndex;
            if (File.Exists(result.Download.CachePath))
            {
                File.Delete(result.Download.CachePath);
            }
        }
    }

    private async Task<IReadOnlyList<QueryCandidateResult>> FindDiversityCandidatesAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int remainingForQuery,
        IReadOnlyList<ReferenceAsset> assets,
        IDictionary<string, int> sourceIndex,
        IDictionary<string, int> downloadIndex,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        var results = new List<QueryCandidateResult>();
        var selectedSourcePages = new HashSet<string>(StringComparer.Ordinal);
        var selectedDownloads = new HashSet<string>(StringComparer.Ordinal);
        var resultLimit = DiversityResultLimit(remainingForQuery);
        var attemptBudget = DiversityCandidateAttemptBudget(query.Category, remainingForQuery);
        var attemptedCandidates = 0;
        for (var page = 0; page < plan.MaxPagesPerQuery; page++)
        {
            var candidates = await SearchAsync(
                plan,
                descriptor,
                query,
                page,
                cancellationToken,
                allowShortfallException: plan.UnverifiedSourcePolicy.Allow);
            if (candidates.Count == 0) break;
            foreach (var candidate in candidates)
            {
                if (CandidateAlreadyCovered(
                        assets,
                        descriptor,
                        query,
                        candidate.SourcePageUrl.AbsoluteUri,
                        candidate.DownloadUrl.AbsoluteUri))
                {
                    continue;
                }

                if (attemptedCandidates >= attemptBudget || results.Count >= resultLimit)
                {
                    return results;
                }

                attemptedCandidates++;
                if (!selectedSourcePages.Add(candidate.SourcePageUrl.AbsoluteUri) ||
                    !selectedDownloads.Add(candidate.DownloadUrl.AbsoluteUri))
                {
                    continue;
                }

                if (sourceIndex.ContainsKey(candidate.SourcePageUrl.AbsoluteUri) ||
                    downloadIndex.ContainsKey(candidate.DownloadUrl.AbsoluteUri))
                {
                    results.Add(new QueryCandidateResult(query, candidate, null));
                    continue;
                }

                var downloaded = await DownloadCandidateSafeAsync(
                    plan,
                    descriptor,
                    query.Category,
                    candidate,
                    cacheRoot,
                    cancellationToken);
                if (downloaded is not null)
                {
                    results.Add(new QueryCandidateResult(query, candidate, downloaded));
                }
            }
        }

        return results;
    }

    internal static int DiversityResultLimit(int remainingForQuery) =>
        Math.Clamp(remainingForQuery, 1, 10);

    internal static int DiversityCandidateAttemptBudget(string category, int remainingForQuery) =>
        category == "architecture"
            ? MaxDiversityCandidateAttemptsPerQuery
            : Math.Clamp(Math.Max(8, remainingForQuery * 3), 8, 16);

    internal static bool ShouldPersistDiversityAttemptMarker(ExpansionTargetDescriptor descriptor) =>
        descriptor.Dimension != "category";

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken,
        bool allowShortfallException)
    {
        foreach (var source in PreferredSourcesForQuery(query))
        {
            var candidates = source switch
            {
                "inaturalist" => await SearchSourceSafeAsync(
                    source,
                    token => SearchINaturalistAsync(plan, descriptor, query, page, token),
                    cancellationToken),
                "cleveland_museum" => await SearchSourceSafeAsync(
                    source,
                    token => SearchClevelandMuseumAsync(plan, descriptor, query, page, token),
                    cancellationToken),
                "art_institute_chicago" => await SearchSourceSafeAsync(
                    source,
                    token => SearchArtInstituteChicagoAsync(plan, descriptor, query, page, token),
                    cancellationToken),
                "met_museum" => await SearchSourceSafeAsync(
                    source,
                    token => SearchMetMuseumAsync(plan, descriptor, query, page, token),
                    cancellationToken),
                "wikimedia_commons" => await SearchSourceSafeAsync(
                    source,
                    token => SearchWikimediaAsync(plan, descriptor, query, page, token, allowShortfallException),
                    cancellationToken),
                _ => await SearchSourceSafeAsync(
                    source,
                    token => SearchOpenverseAsync(plan, descriptor, query, page, token, allowShortfallException),
                    cancellationToken),
            };
            if (candidates.Count > 0)
            {
                return candidates
                    .DistinctBy(candidate => candidate.SourcePageUrl.AbsoluteUri, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        return [];
    }

    internal static IReadOnlyList<string> PreferredSourcesForCategory(string category) =>
        category is "plants" or "animals"
            ? ["inaturalist", "wikimedia_commons", "openverse"]
            : category is "artifacts" or "costumes_textiles"
                ? ["art_institute_chicago", "cleveland_museum", "met_museum", "wikimedia_commons", "openverse"]
            : ["wikimedia_commons", "openverse"];

    internal static IReadOnlyList<string> PreferredSourcesForQuery(ExpansionQuery query)
    {
        var sources = PreferredSourcesForCategory(query.Category);
        return TryGetExactWikimediaCategory(query.Search, out _)
            ? ["wikimedia_commons", .. sources.Where(source => source != "wikimedia_commons")]
            : sources;
    }

    private static async Task<IReadOnlyList<WikimediaCandidate>> SearchSourceSafeAsync(
        string source,
        Func<CancellationToken, Task<IReadOnlyList<WikimediaCandidate>>> search,
        CancellationToken cancellationToken)
    {
        using var sourceTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sourceTimeout.CancelAfter(TimeSpan.FromSeconds(SourceSearchTimeoutSeconds(source)));
        try
        {
            return await search(sourceTimeout.Token);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            Console.Error.WriteLine($"cx507.search_skipped {source}: {exception.Message}");
            return [];
        }
    }

    internal static int SourceSearchTimeoutSeconds(string source) => source switch
    {
        "wikimedia_commons" => 60,
        "met_museum" or "art_institute_chicago" => 45,
        _ => 30,
    };

    private static async Task<T> WithArtInstituteChicagoThrottleAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await ArtInstituteChicagoRequestGate.WaitAsync(cancellationToken);
        try
        {
            var nextRequestAt = _artInstituteChicagoLastRequestUtc.AddMilliseconds(
                ArtInstituteChicagoMinimumRequestDelayMilliseconds);
            var delay = nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return await action();
        }
        finally
        {
            _artInstituteChicagoLastRequestUtc = DateTimeOffset.UtcNow;
            ArtInstituteChicagoRequestGate.Release();
        }
    }

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchINaturalistAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.INaturalistTaxon) || query.INaturalistTaxonId is null or <= 0)
        {
            return [];
        }

        if (plan.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["place_id"] = plan.INaturalistPlaceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["taxon_id"] = query.INaturalistTaxonId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["photos"] = "true",
            ["quality_grade"] = "research",
            ["photo_license"] = "cc0,cc-by",
            ["per_page"] = Math.Min(200, plan.PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["page"] = (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["order_by"] = "created_at",
            ["order"] = "desc",
        };
        var builder = new UriBuilder(plan.INaturalistEndpoint)
        {
            Query = string.Join("&", parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))),
        };
        using var response = await GetWithRetryAsync(builder.Uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseINaturalistCandidates(document.RootElement, plan, descriptor, query);
    }

    internal static IReadOnlyList<WikimediaCandidate> ParseINaturalistCandidates(
        JsonElement root,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        if (!root.TryGetProperty("results", out var observations) || observations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<WikimediaCandidate>();
        foreach (var observation in observations.EnumerateArray())
        {
            var observationId = GetInt(observation, "id");
            if (observationId <= 0 ||
                !observation.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array ||
                !observation.TryGetProperty("taxon", out var taxon) || taxon.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var taxonName = CleanText(GetString(taxon, "name"));
            var commonName = CleanText(GetString(taxon, "preferred_common_name"));
            var userLogin = string.Empty;
            var userName = string.Empty;
            if (observation.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                userLogin = CleanText(GetString(user, "login"));
                userName = CleanText(GetString(user, "name"));
            }

            var creator = string.IsNullOrWhiteSpace(userName) ? userLogin : userName;
            foreach (var photo in photos.EnumerateArray())
            {
                if (photo.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var photoId = GetInt(photo, "id");
                var licenseCode = GetString(photo, "license_code").ToLowerInvariant();
                if (photoId <= 0 || licenseCode is not ("cc0" or "cc-by") ||
                    (licenseCode == "cc-by" && string.IsNullOrWhiteSpace(creator)) ||
                    !photo.TryGetProperty("original_dimensions", out var dimensions) ||
                    dimensions.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var width = GetInt(dimensions, "width");
                var height = GetInt(dimensions, "height");
                if (!MeetsMinimumDimensions(plan, width, height))
                {
                    continue;
                }

                var originalUrl = GetString(photo, "url")
                    .Replace("/square.", "/original.", StringComparison.OrdinalIgnoreCase);
                if (!TryHttpsUri(originalUrl, out var downloadUrl))
                {
                    continue;
                }

                var classification = query.Category == "plants" ? "plant" : "animal";
                var evidence = CleanText(string.Join(' ',
                    classification,
                    taxonName,
                    commonName,
                    GetString(photo, "attribution")));
                if (HasExcludedEvidence(plan, query, evidence) ||
                    HasArchitecturePeopleEvidence(plan, query, evidence) ||
                    (descriptor.Target.RequireEvidence && !descriptor.Target.EvidenceTerms.Any(term => ContainsTerm(evidence, term))) ||
                    !MatchesQueryEvidence(query, evidence) ||
                    !plan.SubjectEvidenceTerms[query.Subject].Any(term => ContainsTerm(evidence, term)))
                {
                    continue;
                }

                var license = licenseCode == "cc0" ? "cc0" : "by";
                var licenseVersion = license == "cc0" ? "1.0" : "4.0";
                var licenseUrl = license == "cc0"
                    ? new Uri("https://creativecommons.org/publicdomain/zero/1.0/")
                    : new Uri("https://creativecommons.org/licenses/by/4.0/");
                candidates.Add(new WikimediaCandidate(
                    $"inat-{photoId}",
                    $"{(string.IsNullOrWhiteSpace(commonName) ? taxonName : commonName)} — iNaturalist observation {observationId}",
                    string.IsNullOrWhiteSpace(creator) ? "iNaturalist contributor (CC0)" : creator,
                    string.IsNullOrWhiteSpace(userLogin)
                        ? null
                        : $"https://www.inaturalist.org/people/{Uri.EscapeDataString(userLogin)}",
                    "inaturalist",
                    new Uri($"https://www.inaturalist.org/observations/{observationId}"),
                    downloadUrl,
                    license,
                    licenseVersion,
                    licenseUrl,
                    width,
                    height,
                    evidence));
            }
        }

        return candidates
            .DistinctBy(candidate => candidate.DownloadUrl.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchWikimediaAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken,
        bool allowShortfallException)
    {
        var discoverySearch = BuildDiscoverySearch(descriptor, query);
        if (TryGetExactWikimediaCategory(discoverySearch, out var category))
        {
            return await SearchWikimediaCategoryAsync(
                plan, descriptor, query, category, page, cancellationToken, allowShortfallException);
        }

        if (plan.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "query",
            ["generator"] = "search",
            ["gsrnamespace"] = "6",
            ["gsrlimit"] = plan.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["gsroffset"] = (page * plan.PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["gsrsearch"] = discoverySearch,
            ["prop"] = "imageinfo",
            ["iiprop"] = "url|extmetadata|size|mime",
            ["iiurlwidth"] = plan.ThumbnailWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["format"] = "json",
            ["formatversion"] = "2",
        };
        var builder = new UriBuilder(plan.DiscoveryEndpoint)
        {
            Query = string.Join("&", parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))),
        };

        using var response = await GetWithRetryAsync(builder.Uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseWikimediaQueryCandidates(document.RootElement, plan, descriptor, query, allowShortfallException);
    }

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchWikimediaCategoryAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        string category,
        int page,
        CancellationToken cancellationToken,
        bool allowShortfallException)
    {
        string? continuation = null;
        if (page > 0 && !_wikimediaCategoryContinuations.TryGetValue(
                WikimediaCategoryContinuationKey(category, page), out continuation))
        {
            return [];
        }

        if (plan.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
        }

        var parameters = BuildWikimediaCategoryQueryParameters(plan, category, continuation);
        var builder = new UriBuilder(plan.DiscoveryEndpoint)
        {
            Query = string.Join("&", parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))),
        };
        using var response = await GetWithRetryAsync(builder.Uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("continue", out var continueRoot))
        {
            var next = GetString(continueRoot, "gcmcontinue");
            if (!string.IsNullOrWhiteSpace(next))
            {
                _wikimediaCategoryContinuations[WikimediaCategoryContinuationKey(category, page + 1)] = next;
            }
        }

        return ParseWikimediaQueryCandidates(document.RootElement, plan, descriptor, query, allowShortfallException);
    }

    internal static bool TryGetExactWikimediaCategory(string search, out string category)
    {
        var match = ExactWikimediaCategoryRegex().Match(search);
        category = match.Success ? match.Groups["category"].Value.Trim() : string.Empty;
        return match.Success && !string.IsNullOrWhiteSpace(category);
    }

    internal static IReadOnlyDictionary<string, string> BuildWikimediaCategoryQueryParameters(
        ChinaExpansionPlan plan,
        string category,
        string? continuation)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "query",
            ["generator"] = "categorymembers",
            ["gcmtitle"] = "Category:" + category,
            ["gcmnamespace"] = "6",
            ["gcmtype"] = "file",
            ["gcmlimit"] = plan.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["prop"] = "imageinfo",
            ["iiprop"] = "url|extmetadata|size|mime",
            ["iiurlwidth"] = plan.ThumbnailWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["format"] = "json",
            ["formatversion"] = "2",
        };
        if (!string.IsNullOrWhiteSpace(continuation))
        {
            parameters["gcmcontinue"] = continuation;
        }

        return parameters;
    }

    private static IReadOnlyList<WikimediaCandidate> ParseWikimediaQueryCandidates(
        JsonElement root,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        bool allowShortfallException)
    {
        if (!root.TryGetProperty("query", out var queryRoot) ||
            !queryRoot.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<WikimediaCandidate>();
        foreach (var item in pages.EnumerateArray())
        {
            if (TryParseCandidate(item, plan, descriptor, query, allowShortfallException, out var candidate))
            {
                candidates.Add(candidate!);
            }
        }

        return candidates;
    }

    private static string WikimediaCategoryContinuationKey(string category, int page) =>
        category + "\n" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchMetMuseumAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.MetMuseumQuery))
        {
            return [];
        }

        if (plan.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
        }

        var searchBuilder = new UriBuilder(plan.MetMuseumEndpoint.TrimEnd('/') + "/search")
        {
            Query = string.Join("&",
                "hasImages=true",
                "departmentId=" + plan.MetMuseumDepartmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "q=" + Uri.EscapeDataString(query.MetMuseumQuery)),
        };
        using var response = await GetWithRetryAsync(searchBuilder.Uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("objectIDs", out var objectIds) ||
            objectIds.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = objectIds.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _))
            .Select(item => item.GetInt32())
            .Skip(page * MetMuseumObjectPageSize)
            .Take(MetMuseumObjectPageSize)
            .ToArray();
        var candidates = new List<WikimediaCandidate>();
        foreach (var id in ids)
        {
            var item = await FetchMetMuseumObjectSafeAsync(plan, id, cancellationToken);
            if (item.HasValue && TryParseMetMuseumCandidate(item.Value, plan, descriptor, query, out var candidate))
            {
                candidates.Add(candidate!);
            }

            if (plan.RequestDelayMilliseconds > 0)
            {
                await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
            }
        }

        return candidates
            .DistinctBy(candidate => candidate.SourcePageUrl.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchArtInstituteChicagoAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.ArtInstituteChicagoQuery))
        {
            return [];
        }

        return await WithArtInstituteChicagoThrottleAsync(async () =>
        {
            var builder = new UriBuilder(plan.ArtInstituteChicagoEndpoint.TrimEnd('/') + "/search")
            {
                Query = "params=" + Uri.EscapeDataString(BuildArtInstituteChicagoSearchPayload(
                    query, page, Math.Min(20, plan.PageSize))),
            };
            using var response = await GetWithRetryAsync(builder.Uri, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseArtInstituteChicagoCandidates(document.RootElement, plan, descriptor, query);
        }, cancellationToken);
    }

    internal static string BuildArtInstituteChicagoSearchPayload(
        ExpansionQuery query,
        int page,
        int pageSize) => JsonSerializer.Serialize(new
        {
            query = new
            {
                @bool = new
                {
                    must = new object[]
                    {
                        new
                        {
                            match = new
                            {
                                title = new
                                {
                                    query = query.ArtInstituteChicagoQuery ?? string.Empty,
                                    @operator = "and",
                                },
                            },
                        },
                        new { term = new { is_public_domain = true } },
                    },
                },
            },
            limit = pageSize,
            page = page + 1,
            fields = new[]
            {
                "id", "title", "image_id", "is_public_domain", "place_of_origin",
                "date_display", "medium_display", "artist_display", "classification_title",
                "artwork_type_title", "department_title", "thumbnail",
            },
        });

    internal static IReadOnlyList<WikimediaCandidate> ParseArtInstituteChicagoCandidates(
        JsonElement root,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object ||
            !TryHttpsUri(GetString(config, "iiif_url"), out var iiifBase) ||
            !string.Equals(iiifBase.Host, "www.artic.edu", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var candidates = new List<WikimediaCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            var objectId = GetInt(item, "id");
            var imageId = GetString(item, "image_id");
            if (objectId <= 0 || string.IsNullOrWhiteSpace(imageId) ||
                !item.TryGetProperty("is_public_domain", out var publicDomain) ||
                publicDomain.ValueKind != JsonValueKind.True ||
                !item.TryGetProperty("thumbnail", out var thumbnail) ||
                thumbnail.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var originalWidth = GetFlexibleInt(thumbnail, "width");
            var originalHeight = GetFlexibleInt(thumbnail, "height");
            if (originalWidth <= 0 || originalHeight <= 0)
            {
                continue;
            }

            var width = Math.Min(ArtInstituteChicagoImageWidth, originalWidth);
            var height = (int)Math.Round(originalHeight * (double)width / originalWidth);
            if (!MeetsMinimumDimensions(plan, width, height))
            {
                continue;
            }

            var title = CleanText(GetString(item, "title"));
            var evidence = CleanText(string.Join(' ',
                title,
                GetString(item, "place_of_origin"),
                GetString(item, "artist_display"),
                GetString(item, "date_display"),
                GetString(item, "medium_display"),
                GetString(item, "classification_title"),
                GetString(item, "artwork_type_title"),
                GetString(item, "department_title"),
                GetString(thumbnail, "alt_text")));
            if (!MatchesArtInstituteChicagoObjectForm(query, title))
            {
                continue;
            }

            if (query.Category == "costumes_textiles" &&
                !new[] { "costume", "textile", "robe", "garment", "silk", "embroidery", "armor", "armour", "headdress", "hat", "shoe", "boot", "belt" }
                    .Any(term => ContainsTerm(evidence, term)))
            {
                continue;
            }

            if (HasExcludedEvidence(plan, query, evidence) ||
                HasArchitecturePeopleEvidence(plan, query, evidence) ||
                !HasRequiredCategoryGeographyEvidence(query, evidence) ||
                (descriptor.Target.RequireEvidence && !descriptor.Target.EvidenceTerms.Any(term => ContainsTerm(evidence, term))) ||
                !MatchesMuseumQueryEvidence(query, evidence) ||
                !plan.SubjectEvidenceTerms[query.Subject].Any(term => ContainsTerm(evidence, term)))
            {
                continue;
            }

            if (!TryHttpsUri($"https://www.artic.edu/artworks/{objectId}", out var sourcePage) ||
                !TryHttpsUri(
                    $"{iiifBase.AbsoluteUri.TrimEnd('/')}/{Uri.EscapeDataString(imageId)}/full/{ArtInstituteChicagoImageWidth},/0/default.jpg",
                    out var downloadUrl))
            {
                continue;
            }

            var artist = CleanText(GetString(item, "artist_display"));
            candidates.Add(new WikimediaCandidate(
                "aic-" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(title) ? $"Art Institute of Chicago Open Access object {objectId}" : title,
                string.IsNullOrWhiteSpace(artist) ? "The Art Institute of Chicago Open Access" : artist,
                null,
                "art_institute_chicago",
                sourcePage,
                downloadUrl,
                "cc0",
                "1.0",
                new Uri("https://creativecommons.org/publicdomain/zero/1.0/"),
                width,
                height,
                evidence));
        }

        return candidates
            .DistinctBy(candidate => candidate.SourcePageUrl.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool MatchesArtInstituteChicagoObjectForm(ExpansionQuery query, string title) =>
        query.Id != "bronze_bell" ||
        title.StartsWith("Bell", StringComparison.OrdinalIgnoreCase) ||
        title.StartsWith("Suspension Bell", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchClevelandMuseumAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.MetMuseumQuery))
        {
            return [];
        }

        if (plan.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
        }

        var builder = new UriBuilder(plan.ClevelandMuseumEndpoint)
        {
            Query = string.Join("&",
                "q=" + Uri.EscapeDataString(query.MetMuseumQuery),
                "cc0",
                "has_image=1",
                "limit=" + plan.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "skip=" + (page * plan.PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        using var response = await GetWithRetryAsync(builder.Uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<WikimediaCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            if (TryParseClevelandMuseumCandidate(item, plan, descriptor, query, out var candidate))
            {
                candidates.Add(candidate!);
            }
        }

        return candidates
            .DistinctBy(candidate => candidate.SourcePageUrl.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool TryParseClevelandMuseumCandidate(
        JsonElement item,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        out WikimediaCandidate? candidate)
    {
        candidate = null;
        var objectId = GetInt(item, "id");
        if (objectId <= 0 ||
            !GetString(item, "share_license_status").Equals("CC0", StringComparison.OrdinalIgnoreCase) ||
            !TryHttpsUri(GetString(item, "url"), out var sourcePage) ||
            !item.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object ||
            !images.TryGetProperty("print", out var print) || print.ValueKind != JsonValueKind.Object ||
            !TryHttpsUri(GetString(print, "url"), out var downloadUrl))
        {
            return false;
        }

        var width = GetFlexibleInt(print, "width");
        var height = GetFlexibleInt(print, "height");
        if (!MeetsMinimumDimensions(plan, width, height))
        {
            return false;
        }

        var title = CleanText(GetString(item, "title"));
        var culture = JoinJsonText(item, "culture");
        var evidence = CleanText(string.Join(' ',
            title,
            GetString(item, "tombstone"),
            culture,
            GetString(item, "type"),
            GetString(item, "department"),
            GetString(item, "technique")));
        if (query.Category == "costumes_textiles" &&
            !new[] { "costume", "textile", "robe", "garment", "silk", "embroidery", "armor", "armour", "headdress", "hat", "shoe", "boot", "belt" }
                .Any(term => ContainsTerm(evidence, term)))
        {
            return false;
        }

        if (HasExcludedEvidence(plan, query, evidence) ||
            HasArchitecturePeopleEvidence(plan, query, evidence) ||
            !HasRequiredCategoryGeographyEvidence(query, evidence) ||
            (descriptor.Target.RequireEvidence && !descriptor.Target.EvidenceTerms.Any(term => ContainsTerm(evidence, term))) ||
            !MatchesQueryEvidence(query, evidence) ||
            !plan.SubjectEvidenceTerms[query.Subject].Any(term => ContainsTerm(evidence, term)))
        {
            return false;
        }

        var creator = "The Cleveland Museum of Art Open Access";
        if (item.TryGetProperty("creators", out var creators) && creators.ValueKind == JsonValueKind.Array)
        {
            var described = creators.EnumerateArray()
                .Select(value => CleanText(GetString(value, "description")))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(described)) creator = described;
        }

        candidate = new WikimediaCandidate(
            "cma-" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(title) ? $"Cleveland Museum Open Access object {objectId}" : title,
            creator,
            null,
            "cleveland_museum",
            sourcePage,
            downloadUrl,
            "cc0",
            "1.0",
            new Uri("https://creativecommons.org/publicdomain/zero/1.0/"),
            width,
            height,
            evidence);
        return true;
    }

    private async Task<JsonElement?> FetchMetMuseumObjectSafeAsync(
        ChinaExpansionPlan plan,
        int objectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(plan.MetMuseumEndpoint.TrimEnd('/') + "/objects/" +
                objectId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var response = await GetWithRetryAsync(uri, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            Console.Error.WriteLine($"cx507.met_object_skipped {objectId}: {exception.Message}");
            return null;
        }
    }

    internal static bool TryParseMetMuseumCandidate(
        JsonElement item,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        out WikimediaCandidate? candidate)
    {
        candidate = null;
        var objectId = GetInt(item, "objectID");
        if (objectId <= 0 ||
            !item.TryGetProperty("isPublicDomain", out var publicDomain) ||
            publicDomain.ValueKind != JsonValueKind.True ||
            !TryHttpsUri(GetString(item, "primaryImage"), out var downloadUrl) ||
            !TryHttpsUri(GetString(item, "objectURL"), out var sourcePage))
        {
            return false;
        }

        var locationEvidence = CleanText(string.Join(' ',
            GetString(item, "culture"),
            GetString(item, "country"),
            GetString(item, "region"),
            GetString(item, "subregion"),
            GetString(item, "locale"),
            GetString(item, "locus")));
        if (!ContainsTerm(locationEvidence, "China") && !ContainsTerm(locationEvidence, "Chinese"))
        {
            return false;
        }

        var title = CleanText(GetString(item, "title"));
        var evidenceParts = new List<string>
        {
            title,
            GetString(item, "objectName"),
            locationEvidence,
            GetString(item, "dynasty"),
            GetString(item, "reign"),
            GetString(item, "period"),
            GetString(item, "classification"),
            GetString(item, "medium"),
        };
        if (item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            evidenceParts.AddRange(tags.EnumerateArray().Select(tag => GetString(tag, "term")));
        }

        var evidence = CleanText(string.Join(' ', evidenceParts));
        if (query.Category == "costumes_textiles" &&
            !new[] { "costume", "textile", "robe", "garment", "silk", "embroidery", "armor", "armour", "headdress", "hat", "shoe", "boot", "belt" }
                .Any(term => ContainsTerm(evidence, term)))
        {
            return false;
        }

        if (HasExcludedEvidence(plan, query, evidence) ||
            HasArchitecturePeopleEvidence(plan, query, evidence) ||
            !HasRequiredCategoryGeographyEvidence(query, evidence) ||
            (descriptor.Target.RequireEvidence && !descriptor.Target.EvidenceTerms.Any(term => ContainsTerm(evidence, term))) ||
            !MatchesQueryEvidence(query, evidence) ||
            !plan.SubjectEvidenceTerms[query.Subject].Any(term => ContainsTerm(evidence, term)))
        {
            return false;
        }

        var artist = CleanText(GetString(item, "artistDisplayName"));
        candidate = new WikimediaCandidate(
            "met-" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(title) ? $"Met Open Access object {objectId}" : title,
            string.IsNullOrWhiteSpace(artist) ? "The Metropolitan Museum of Art Open Access" : artist,
            null,
            "met_museum",
            sourcePage,
            downloadUrl,
            "pdm",
            "1.0",
            new Uri("https://creativecommons.org/publicdomain/mark/1.0/"),
            plan.MinimumWidth,
            plan.MinimumHeight,
            evidence);
        return true;
    }

    private async Task<IReadOnlyList<WikimediaCandidate>> SearchOpenverseAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        int page,
        CancellationToken cancellationToken,
        bool allowShortfallException)
    {
        if (plan.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(plan.RequestDelayMilliseconds, cancellationToken);
        }

        var parameters = new List<string>
        {
            "q=" + Uri.EscapeDataString(BuildOpenverseSearch(descriptor, query)),
            "mature=false",
            "page_size=20",
            "page=" + (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!allowShortfallException)
        {
            parameters.Add("license=cc0%2Cpdm%2Cby");
            parameters.Add("license_type=commercial%2Cmodification");
        }

        var builder = new UriBuilder("https://api.openverse.org/v1/images/")
        {
            Query = string.Join("&", parameters),
        };
        using var response = await GetWithRetryAsync(builder.Uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<WikimediaCandidate>();
        foreach (var item in results.EnumerateArray())
        {
            if (TryParseOpenverseCandidate(item, plan, descriptor, query, allowShortfallException, out var candidate))
            {
                candidates.Add(candidate!);
            }
        }

        return candidates;
    }

    private static string BuildOpenverseSearch(
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        if (descriptor.Dimension == "category")
        {
            return BuildDiscoverySearch(descriptor, query);
        }

        if (!string.IsNullOrWhiteSpace(query.Id) && !query.MinimumAssets.HasValue)
        {
            return query.Search;
        }

        var primaryEvidence = descriptor.Target.EvidenceTerms.FirstOrDefault(term =>
            term.Any(character => character <= 127 && char.IsLetter(character)) &&
            !term.Equals("China", StringComparison.OrdinalIgnoreCase) &&
            !term.Equals("Chinese", StringComparison.OrdinalIgnoreCase)) ?? descriptor.Target.Label;

        if (descriptor.Dimension == "province" && !query.MinimumAssets.HasValue)
        {
            return $"{primaryEvidence} {(query.Subject == "architecture" ? "temple" : "landscape")}";
        }

        if (descriptor.Dimension == "capital")
        {
            var facet = query.Search.Contains("palace", StringComparison.OrdinalIgnoreCase)
                ? "palace"
                : query.Search.Contains("historic site", StringComparison.OrdinalIgnoreCase)
                    ? "ruins"
                    : "architecture";
            return $"{primaryEvidence} {facet}";
        }

        if (descriptor.Dimension == "period")
        {
            var facet = query.Search.Contains("temple", StringComparison.OrdinalIgnoreCase)
                ? "temple"
                : query.Search.Contains("city gate", StringComparison.OrdinalIgnoreCase)
                    ? "ruins"
                    : "architecture";
            return $"{primaryEvidence} {facet}";
        }

        if (descriptor.Dimension == "garden")
        {
            var gardenIndex = query.Search.IndexOf(" garden", StringComparison.OrdinalIgnoreCase);
            var gardenBase = gardenIndex >= 0
                ? query.Search[..(gardenIndex + " garden".Length)]
                : primaryEvidence;
            var facet = query.Search.Contains("pond", StringComparison.OrdinalIgnoreCase)
                ? "pond"
                : query.Search.Contains("pavilion", StringComparison.OrdinalIgnoreCase)
                    ? "pavilion"
                    : string.Empty;
            return string.Join(' ', new[] { gardenBase, facet }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        var simplified = query.Search;
        foreach (var suffix in new[] { " photograph nature", " photograph", " scenic" })
        {
            simplified = simplified.Replace(suffix, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return simplified;
    }

    internal static string BuildDiscoverySearch(
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        if (descriptor.Dimension != "category")
        {
            return query.Search;
        }

        var simplified = query.Search;
        foreach (var boilerplate in new[]
                 {
                     " museum textile detail",
                     " water landscape photograph",
                     " botanical photograph",
                     " wildlife photograph",
                     " landscape photograph",
                     " weather photograph",
                     " night sky photograph",
                     " portrait photograph",
                     " museum garment",
                     " museum textile",
                     " museum costume",
                     " museum artifact",
                     " geology photograph",
                     " atmospheric photograph",
                     " photograph",
                 })
        {
            simplified = simplified.Replace(boilerplate, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return WhitespaceRegex().Replace(simplified, " ").Trim();
    }

    internal static bool TryParseOpenverseCandidate(
        JsonElement item,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        bool allowShortfallException,
        out WikimediaCandidate? candidate)
    {
        candidate = null;
        var license = GetString(item, "license").ToLowerInvariant();
        var creator = CleanText(GetString(item, "creator"));
        var width = GetInt(item, "width");
        var height = GetInt(item, "height");
        var strictLicense = license is "cc0" or "pdm" or "by";
        if ((!strictLicense && !allowShortfallException) ||
            (license == "by" && string.IsNullOrWhiteSpace(creator)) ||
            !MeetsMinimumDimensions(plan, descriptor, width, height) ||
            !TryHttpsUri(GetString(item, "foreign_landing_url"), out var sourcePage) ||
            !TryHttpsUri(GetString(item, "url"), out var downloadUrl))
        {
            return false;
        }

        var title = CleanText(GetString(item, "title"));
        var evidenceParts = new List<string> { title };
        if (item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                evidenceParts.Add(tag.ValueKind == JsonValueKind.String
                    ? tag.GetString() ?? string.Empty
                    : GetString(tag, "name"));
            }
        }

        var evidence = CleanText(string.Join(' ', evidenceParts));
        if (HasExcludedEvidence(plan, query, evidence) ||
            HasArchitecturePeopleEvidence(plan, query, evidence) ||
            !HasRequiredCategoryGeographyEvidence(query, evidence) ||
            (descriptor.Target.RequireEvidence && !descriptor.Target.EvidenceTerms.Any(term => ContainsTerm(evidence, term))) ||
            !MatchesQueryEvidence(query, evidence) ||
            !plan.SubjectEvidenceTerms[query.Subject].Any(term => ContainsTerm(evidence, term)))
        {
            return false;
        }

        Uri? licenseUrl = null;
        var version = CleanText(GetString(item, "license_version"));
        if (!strictLicense)
        {
            license = "unverified";
            version = "unverified";
        }
        else if (string.IsNullOrWhiteSpace(version))
        {
            version = license == "by" ? "4.0" : "1.0";
        }

        if (strictLicense && !TryHttpsUri(GetString(item, "license_url"), out licenseUrl))
        {
            licenseUrl = license switch
            {
                "cc0" => new Uri("https://creativecommons.org/publicdomain/zero/1.0/"),
                "pdm" => new Uri("https://creativecommons.org/publicdomain/mark/1.0/"),
                _ => new Uri($"https://creativecommons.org/licenses/by/{version}/"),
            };
        }

        var provider = CleanText(GetString(item, "provider"));
        var source = CleanText(GetString(item, "source"));
        candidate = new WikimediaCandidate(
            GetString(item, "id"),
            string.IsNullOrWhiteSpace(title) ? "Untitled Openverse reference" : title,
            string.IsNullOrWhiteSpace(creator) ? "Uncredited (Openverse source metadata)" : creator,
            HttpsOrNull(GetString(item, "creator_url")),
            string.Join('/', new[] { "openverse", provider, source }.Where(value => !string.IsNullOrWhiteSpace(value))),
            sourcePage,
            downloadUrl,
            license,
            version,
            licenseUrl,
            width,
            height,
            evidence);
        return !string.IsNullOrWhiteSpace(candidate.Id);
    }

    private static bool TryParseCandidate(
        JsonElement item,
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        bool allowShortfallException,
        out WikimediaCandidate? candidate)
    {
        candidate = null;
        if (!item.TryGetProperty("imageinfo", out var imageInfoArray) || imageInfoArray.GetArrayLength() == 0)
        {
            return false;
        }

        var info = imageInfoArray[0];
        var mediaType = GetString(info, "mime");
        if (mediaType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            return false;
        }

        var width = GetInt(info, "thumbwidth");
        var height = GetInt(info, "thumbheight");
        if (width == 0 || height == 0)
        {
            width = GetInt(info, "width");
            height = GetInt(info, "height");
        }

        if (!MeetsMinimumDimensions(plan, descriptor, width, height) ||
            !TryHttpsUri(GetString(info, "descriptionurl"), out var sourcePage) ||
            !TryHttpsUri(GetString(info, "thumburl", GetString(info, "url")), out var downloadUrl))
        {
            return false;
        }

        if (!info.TryGetProperty("extmetadata", out var metadata))
        {
            return false;
        }

        var rawLicense = Metadata(metadata, "LicenseShortName");
        if (!TryNormalizeLicense(
                rawLicense,
                allowShortfallException,
                out var license,
                out var version,
                out var licenseUrl))
        {
            return false;
        }

        var creator = CleanText(Metadata(metadata, "Artist"));
        if (license == "by" && string.IsNullOrWhiteSpace(creator))
        {
            return false;
        }

        creator = string.IsNullOrWhiteSpace(creator) ? "Uncredited (Wikimedia source metadata)" : creator;
        var title = GetString(item, "title");
        if (title.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
        {
            title = title[5..];
        }

        var evidence = CleanText(string.Join(' ',
            title,
            Metadata(metadata, "ObjectName"),
            Metadata(metadata, "ImageDescription"),
            Metadata(metadata, "Categories"),
            Metadata(metadata, "Location")));
        if (HasExcludedEvidence(plan, query, evidence) ||
            HasArchitecturePeopleEvidence(plan, query, evidence) ||
            !HasRequiredCategoryGeographyEvidence(query, evidence) ||
            (descriptor.Target.RequireEvidence && !descriptor.Target.EvidenceTerms.Any(term => ContainsTerm(evidence, term))) ||
            !MatchesQueryEvidence(query, evidence) ||
            !plan.SubjectEvidenceTerms[query.Subject].Any(term => ContainsTerm(evidence, term)))
        {
            return false;
        }

        var id = item.TryGetProperty("pageid", out var pageId) ? pageId.GetInt32().ToString() : title;
        candidate = new WikimediaCandidate(
            id,
            string.IsNullOrWhiteSpace(title) ? "Untitled Wikimedia reference" : title,
            creator,
            null,
            "wikimedia_commons",
            sourcePage,
            downloadUrl,
            license,
            version,
            licenseUrl,
            width,
            height,
            evidence);
        return true;
    }

    private async Task<(DownloadedImage Image, string CachePath)> ReadCacheOrDownloadAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        WikimediaCandidate candidate,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.Combine(cacheRoot, descriptor.Dimension, descriptor.Target.Id);
        Directory.CreateDirectory(targetDirectory);
        var stem = CandidateToken(candidate.Source, candidate.Id);
        foreach (var path in Directory.EnumerateFiles(targetDirectory, stem + ".*"))
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (bytes.LongLength <= plan.MaxDownloadBytes)
            {
                try
                {
                    return (ReferenceImageInspector.Inspect(bytes, null), path);
                }
                catch (InvalidDataException)
                {
                    // Keep searching or replace a damaged cache file.
                }
            }
        }

        if (candidate.Source == "art_institute_chicago")
        {
            return await WithArtInstituteChicagoThrottleAsync(
                () => DownloadAndCacheAsync(plan, candidate, targetDirectory, stem, cancellationToken),
                cancellationToken);
        }

        return await DownloadAndCacheAsync(plan, candidate, targetDirectory, stem, cancellationToken);
    }

    private async Task<(DownloadedImage Image, string CachePath)> DownloadAndCacheAsync(
        ChinaExpansionPlan plan,
        WikimediaCandidate candidate,
        string targetDirectory,
        string stem,
        CancellationToken cancellationToken)
    {
        using var response = await GetWithRetryAsync(candidate.DownloadUrl, cancellationToken);
        if (response.Content.Headers.ContentLength is > 0 and var length && length > plan.MaxDownloadBytes)
        {
            throw new InvalidDataException($"Image is {length} bytes, over {plan.MaxDownloadBytes}.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > plan.MaxDownloadBytes)
            {
                throw new InvalidDataException($"Image exceeded {plan.MaxDownloadBytes} bytes while downloading.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var inspected = ReferenceImageInspector.Inspect(output.ToArray(), response.Content.Headers.ContentType?.MediaType);
        var cachePath = Path.Combine(targetDirectory, stem + inspected.Extension);
        await File.WriteAllBytesAsync(cachePath, inspected.Bytes, cancellationToken);
        return (inspected, cachePath);
    }

    private async Task<CandidateDownload?> DownloadCandidateSafeAsync(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        string category,
        WikimediaCandidate candidate,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        using var candidateTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        candidateTimeout.CancelAfter(TimeSpan.FromSeconds(
            CandidateDownloadTimeoutSecondsForCategory(category)));
        try
        {
            var (image, cachePath) = await ReadCacheOrDownloadAsync(
                plan,
                descriptor,
                candidate,
                cacheRoot,
                candidateTimeout.Token);
            if (!MeetsMinimumDimensions(plan, descriptor, image.Width, image.Height))
            {
                throw new InvalidDataException(
                    $"Downloaded image is {image.Width}x{image.Height}, below {plan.MinimumWidth}x{plan.MinimumHeight} orientation-aware minimum.");
            }
            return new CandidateDownload(candidate, image, cachePath);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Console.Error.WriteLine($"cx507.candidate_skipped {descriptor.TargetId} {candidate.Id}: {exception.Message}");
            return null;
        }
    }

    internal static int CandidateDownloadTimeoutSecondsForCategory(string category) =>
        category == "people" ? 90 : CandidateDownloadTimeoutSeconds;

    private async Task<HttpResponseMessage> GetWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var metMuseumForbidden = response.StatusCode == HttpStatusCode.Forbidden &&
                    uri.Host.Equals("collectionapi.metmuseum.org", StringComparison.OrdinalIgnoreCase);
                if (((int)response.StatusCode is 429 or 500 or 502 or 503 or 504 || metMuseumForbidden) && attempt < 5)
                {
                    var delay = response.Headers.RetryAfter?.Delta ??
                        TimeSpan.FromSeconds(metMuseumForbidden ? attempt * 3 : attempt * 2);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (Exception exception) when (attempt < 5 && exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        throw new HttpRequestException($"Request failed after retries: {uri.Host}", lastException);
    }

    private static ReferenceAsset UpgradeLegacyAsset(ReferenceAsset asset) => asset with
    {
        CurationStatus = "needs_review",
        CoverageTargetIds = asset.CoverageTargetIds.Count == 0 ? ["legacy.cx506"] : asset.CoverageTargetIds,
        Taxonomy = NormalizePeoplePresence(asset, asset.Taxonomy ?? new ReferenceTaxonomy()),
    };

    internal static IReadOnlyList<ReferenceAsset> TagExistingCategoryCoverage(
        ChinaExpansionPlan plan,
        IReadOnlyList<ReferenceAsset> assets)
    {
        var categoryTargets = plan.Coverage.CategorySubjects
            .Select(target => target.Id)
            .ToHashSet(StringComparer.Ordinal);
        return assets.Select(asset =>
        {
            if (!categoryTargets.Contains(asset.Category))
            {
                return asset;
            }

            var targetIds = asset.CoverageTargetIds
                .Append($"category.{asset.Category}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return asset with { CoverageTargetIds = targetIds };
        }).ToArray();
    }

    private static ReferenceAsset AddCoverage(
        ReferenceAsset asset,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        var targetIds = asset.CoverageTargetIds.Append(descriptor.TargetId).Distinct(StringComparer.Ordinal).Order().ToArray();
        if (query.MinimumAssets.HasValue)
        {
            targetIds = targetIds.Append(QueryTargetId(descriptor, query)).Distinct(StringComparer.Ordinal).Order().ToArray();
        }
        var taxonomy = asset.Taxonomy ?? new ReferenceTaxonomy();
        taxonomy = descriptor.Dimension switch
        {
            "province" => taxonomy with
            {
                ProvinceRegionIds = Add(taxonomy.ProvinceRegionIds, descriptor.Target.Id),
                LocationLabels = Add(taxonomy.LocationLabels, descriptor.Target.Label),
                ArchitectureTypeIds = query.Category == "architecture"
                    ? Add(taxonomy.ArchitectureTypeIds, "regional_historic_architecture")
                    : taxonomy.ArchitectureTypeIds,
            },
            "capital" => taxonomy with
            {
                HistoricalCapitalIds = Add(taxonomy.HistoricalCapitalIds, descriptor.Target.Id),
                LocationLabels = Add(taxonomy.LocationLabels, descriptor.Target.Label),
                ArchitectureTypeIds = Add(taxonomy.ArchitectureTypeIds, "capital_complex"),
            },
            "period" => taxonomy with
            {
                HistoricalPeriodIds = Add(taxonomy.HistoricalPeriodIds, descriptor.Target.Id),
                ArchitectureTypeIds = Add(taxonomy.ArchitectureTypeIds, "period_architecture"),
            },
            "garden" => taxonomy with
            {
                GardenTypeIds = Add(taxonomy.GardenTypeIds, descriptor.Target.Id),
                ArchitectureTypeIds = query.Category == "architecture"
                    ? Add(taxonomy.ArchitectureTypeIds, "garden_architecture")
                    : taxonomy.ArchitectureTypeIds,
            },
            "landform" => taxonomy with { LandformIds = Add(taxonomy.LandformIds, descriptor.Target.Id) },
            "weather" => taxonomy with { WeatherPhenomenonIds = Add(taxonomy.WeatherPhenomenonIds, descriptor.Target.Id) },
            _ => taxonomy,
        };
        taxonomy = NormalizePeoplePresence(asset, taxonomy with { });
        return asset with { CoverageTargetIds = targetIds, Taxonomy = taxonomy, CurationStatus = "needs_review" };
    }

    private static ReferenceTaxonomy NormalizePeoplePresence(
        ReferenceAsset asset,
        ReferenceTaxonomy taxonomy) => taxonomy with
        {
            PeoplePresence = asset.Category == "architecture"
                ? taxonomy.PeoplePresence
                : "not_applicable",
        };

    internal static bool TargetSatisfied(IReadOnlyList<ReferenceAsset> assets, ExpansionTargetDescriptor descriptor)
    {
        var tagged = TargetAssets(assets, descriptor);
        if (tagged.Count < descriptor.Target.MinimumAssets) return false;
        if (descriptor.Target.MinimumArchitectureAssets is int architecture && tagged.Count(asset => asset.Category == "architecture") < architecture) return false;
        if (descriptor.Target.MinimumNaturalAssets is int natural && tagged.Count(asset => asset.Category is "landscape" or "waters" or "weather" or "astronomy" or "geology_caves" or "light_fog_fire") < natural) return false;
        return true;
    }

    private static IReadOnlyList<ReferenceAsset> TargetAssets(
        IReadOnlyList<ReferenceAsset> assets,
        ExpansionTargetDescriptor descriptor) => assets
        .Where(asset =>
            asset.CoverageTargetIds.Contains(descriptor.TargetId, StringComparer.Ordinal) &&
            (descriptor.Dimension != "category" || asset.Category == descriptor.Target.Id))
        .ToArray();

    internal static IReadOnlyList<ExpansionQuery> ProvinceFallbackQueries(ExpansionTargetDescriptor descriptor)
    {
        return descriptor.Target.Queries
            .Select(query => query with { MinimumAssets = null })
            .ToArray();
    }

    internal static IReadOnlyList<ExpansionQuery> FallbackQueries(ExpansionTargetDescriptor descriptor)
    {
        if (descriptor.Dimension == "province")
        {
            return ProvinceFallbackQueries(descriptor);
        }

        var eligible = descriptor.Target.Queries
            .Where(query => !query.MinimumAssets.HasValue)
            .ToArray();
        var named = eligible
            .Where(query => !string.IsNullOrWhiteSpace(query.Id))
            .ToArray();
        return named.Length > 0 ? named : eligible;
    }

    private static bool QueryQuotaSatisfied(
        IReadOnlyList<ReferenceAsset> assets,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        if (query.MinimumAssets is int queryRequired)
        {
            var queryTargetId = QueryTargetId(descriptor, query);
            return assets.Count(asset =>
                asset.Category == query.Category &&
                asset.CoverageTargetIds.Contains(queryTargetId, StringComparer.Ordinal)) >= queryRequired;
        }

        if (descriptor.Dimension != "province")
        {
            return false;
        }

        var tagged = TargetAssets(assets, descriptor);
        return query.Subject switch
        {
            "architecture" when descriptor.Target.MinimumArchitectureAssets is int required =>
                tagged.Count(asset => asset.Category == "architecture") >= required,
            "natural" when descriptor.Target.MinimumNaturalAssets is int required =>
                tagged.Count(asset => asset.Category is "landscape" or "waters" or "weather" or "astronomy" or "geology_caves" or "light_fog_fire") >= required,
            _ => false,
        };
    }

    private static int RemainingForQuery(
        IReadOnlyList<ReferenceAsset> assets,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        var tagged = TargetAssets(assets, descriptor);
        if (query.MinimumAssets is int queryRequired)
        {
            var queryTargetId = QueryTargetId(descriptor, query);
            return Math.Max(0, queryRequired - assets.Count(asset =>
                asset.Category == query.Category &&
                asset.CoverageTargetIds.Contains(queryTargetId, StringComparer.Ordinal)));
        }

        if (descriptor.Dimension == "province")
        {
            return query.Subject switch
            {
                "architecture" when descriptor.Target.MinimumArchitectureAssets is int required =>
                    Math.Max(0, required - tagged.Count(asset => asset.Category == "architecture")),
                "natural" when descriptor.Target.MinimumNaturalAssets is int required =>
                    Math.Max(0, required - tagged.Count(asset => asset.Category is "landscape" or "waters" or "weather" or "astronomy" or "geology_caves" or "light_fog_fire")),
                _ => Math.Max(0, descriptor.Target.MinimumAssets - tagged.Count),
            };
        }

        return Math.Max(0, descriptor.Target.MinimumAssets - tagged.Count);
    }

    private static string QueryTargetId(ExpansionTargetDescriptor descriptor, ExpansionQuery query) =>
        $"query.{descriptor.Dimension}_{descriptor.Target.Id}_{query.Id}";

    internal static bool CandidateAlreadyCovered(
        IReadOnlyList<ReferenceAsset> assets,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query,
        string sourcePageUrl,
        string downloadUrl)
    {
        var queryTargetId = QueryTargetId(descriptor, query);
        return assets.Any(asset =>
            asset.CoverageTargetIds.Contains(queryTargetId, StringComparer.Ordinal) &&
            (string.Equals(asset.SourcePageUrl, sourcePageUrl, StringComparison.Ordinal) ||
             string.Equals(asset.DownloadUrl, downloadUrl, StringComparison.Ordinal)));
    }

    private static bool QueryAttempted(
        string cacheRoot,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query) => File.Exists(QueryAttemptMarker(cacheRoot, descriptor, query));

    private static void MarkQueryAttempted(
        string cacheRoot,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query)
    {
        var path = QueryAttemptMarker(cacheRoot, descriptor, query);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "cx507 strict-license discovery attempted" + Environment.NewLine);
    }

    private static string QueryAttemptMarker(
        string cacheRoot,
        ExpansionTargetDescriptor descriptor,
        ExpansionQuery query) => Path.Combine(
            cacheRoot,
            "attempted",
            QueryTargetId(descriptor, query) + ".sources-v2.done");

    private static Dictionary<string, int> BuildIndex(IReadOnlyList<ReferenceAsset> assets, Func<ReferenceAsset, string> selector) =>
        assets.Select((asset, index) => (Value: selector(asset), Index: index))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);

    private static IReadOnlyList<string> Add(IReadOnlyList<string> values, string value) =>
        values.Append(value).Distinct(StringComparer.Ordinal).Order().ToArray();

    private static IReadOnlyList<string> RolesFor(string category) => category switch
    {
        "architecture" => ["structure", "material", "composition", "surface_detail"],
        "weather" => ["weather", "lighting", "color", "composition"],
        "astronomy" => ["lighting", "color", "composition"],
        "light_fog_fire" => ["lighting", "weather", "color", "composition"],
        _ => ["composition", "lighting", "color", "surface_detail"],
    };

    private static ShortfallExceptionAsset? ShortfallExceptionFor(
        ChinaExpansionPlan plan,
        WikimediaCandidate candidate,
        DownloadedImage downloaded,
        bool targetShortfall)
    {
        if (!targetShortfall)
        {
            return null;
        }

        var reducedResolution = Math.Max(downloaded.Width, downloaded.Height) < plan.MinimumWidth ||
                                Math.Min(downloaded.Width, downloaded.Height) < plan.MinimumHeight;
        var unverifiedSource = string.Equals(candidate.License, "unverified", StringComparison.Ordinal);
        return (reducedResolution || unverifiedSource) && plan.ShortfallException.RequiresManualReview
            ? new ShortfallExceptionAsset
            {
                Reason = "material_shortage",
                SourceStatus = unverifiedSource ? "unverified" : "verified",
                QualityStatus = reducedResolution ? "reduced_resolution" : "minimum_resolution",
                RequiresManualReview = true,
            }
            : null;
    }

    internal static bool TryNormalizeLicense(
        string raw,
        bool allowUnverified,
        out string license,
        out string version,
        out Uri? licenseUrl)
    {
        var normalized = CleanText(raw);
        license = string.Empty;
        version = "unspecified";
        licenseUrl = null;
        var restricted = normalized.Contains("SA", StringComparison.OrdinalIgnoreCase) ||
                         normalized.Contains("NC", StringComparison.OrdinalIgnoreCase) ||
                         normalized.Contains("ND", StringComparison.OrdinalIgnoreCase);
        if (restricted && !allowUnverified)
        {
            return false;
        }

        if (restricted)
        {
            license = "unverified";
            version = "unverified";
            return true;
        }

        if (normalized.StartsWith("CC0", StringComparison.OrdinalIgnoreCase))
        {
            license = "cc0";
            version = ExtractVersion(normalized, "1.0");
            licenseUrl = new Uri("https://creativecommons.org/publicdomain/zero/1.0/");
            return true;
        }

        if (normalized.StartsWith("CC BY", StringComparison.OrdinalIgnoreCase))
        {
            license = "by";
            version = ExtractVersion(normalized, "4.0");
            licenseUrl = new Uri($"https://creativecommons.org/licenses/by/{version}/");
            return true;
        }

        if (normalized.Contains("public domain", StringComparison.OrdinalIgnoreCase) || normalized.Equals("PDM", StringComparison.OrdinalIgnoreCase))
        {
            license = "pdm";
            version = "1.0";
            licenseUrl = new Uri("https://creativecommons.org/publicdomain/mark/1.0/");
            return true;
        }

        if (allowUnverified)
        {
            license = "unverified";
            version = "unverified";
            return true;
        }

        return false;
    }

    private static string ExtractVersion(string value, string fallback)
    {
        var match = VersionRegex().Match(value);
        return match.Success ? match.Value : fallback;
    }

    private static string Metadata(JsonElement metadata, string name)
    {
        if (!metadata.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Object ||
            !property.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static string CleanText(string value)
    {
        var noTags = HtmlTagRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(noTags), " ").Trim();
    }

    private static bool ContainsTerm(string text, string term) =>
        text.Contains(term, StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesQueryEvidence(ExpansionQuery query, string evidence) =>
        TryGetExactWikimediaCategory(query.Search, out _) ||
        query.EvidenceTerms.Count == 0 ||
        query.EvidenceTerms.Any(term => ContainsTerm(evidence, term));

    internal static bool MatchesMuseumQueryEvidence(ExpansionQuery query, string evidence) =>
        MatchesQueryEvidence(query, evidence) ||
        query.EvidenceTerms.Any(term => Regex.Matches(term, @"[\p{L}\p{Nd}]+")
            .Select(match => match.Value)
            .All(token => token.Equals("Chinese", StringComparison.OrdinalIgnoreCase)
                ? ContainsTerm(evidence, "China") || ContainsTerm(evidence, "Chinese")
                : ContainsTerm(evidence, token)));

    internal static bool HasRequiredCategoryGeographyEvidence(
        ExpansionQuery query,
        string evidence) =>
        query.Category is not ("artifacts" or "costumes_textiles" or "people") ||
        ContainsTerm(evidence, "China") ||
        ContainsTerm(evidence, "Chinese");

    internal static bool HasExcludedEvidence(
        ChinaExpansionPlan plan,
        ExpansionQuery query,
        string evidence)
    {
        if (IsQingCostumeEvidence(query, evidence))
        {
            return true;
        }

        var categoryExceptions = query.Category switch
        {
            "people" => new HashSet<string>(["portrait"], StringComparer.OrdinalIgnoreCase),
            "costumes_textiles" => new HashSet<string>(["costume"], StringComparer.OrdinalIgnoreCase),
            "artifacts" => new HashSet<string>(["ceramic", "vase"], StringComparer.OrdinalIgnoreCase),
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
        if (plan.ExcludedTerms.Any(term =>
                !categoryExceptions.Contains(term) && ContainsTerm(evidence, term)))
        {
            return true;
        }

        if (NaturalSceneCategories.Contains(query.Category) &&
            NaturalSceneObjectExclusionTerms.Any(term => ContainsTerm(evidence, term)))
        {
            return true;
        }

        if (query.Category == "people" &&
            PeopleOutOfScopeEvidenceTerms.Any(term => ContainsTerm(evidence, term)))
        {
            return true;
        }

        return query.Category == "architecture" &&
               plan.ArchitectureSubjectExclusionTerms.Any(term => ContainsTerm(evidence, term));
    }

    internal static bool IsQingCostumeEvidence(ExpansionQuery query, string evidence)
    {
        if (query.Category != "costumes_textiles")
        {
            return false;
        }

        if (Regex.IsMatch(evidence, @"\bQing\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(evidence, @"清(?:朝|代|初|末|早期|中期|晚期|前期|后期)"))
        {
            return true;
        }

        if (Regex.IsMatch(
                evidence,
                @"\b(?:18th|19th)\s+century\b|\b(?:late|second\s+half\s+of\s+the)\s+17th\s+century\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        var years = Regex.Matches(evidence, @"(?<!\d)(1\d{3})(?!\d)")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (years.Length == 0)
        {
            return false;
        }

        // A museum range such as "Ming dynasty (1368-1644)" names the terminal
        // Ming year; it is not evidence that the garment belongs to the Qing.
        if (ContainsTerm(evidence, "Ming") && years.All(year => year <= 1644))
        {
            return false;
        }

        return years.Any(year => year is >= 1644 and <= 1911);
    }

    private static bool HasArchitecturePeopleEvidence(
        ChinaExpansionPlan plan,
        ExpansionQuery query,
        string evidence) =>
        plan.ArchitectureMustExcludePeople &&
        query.Category == "architecture" &&
        plan.ArchitecturePersonExclusionTerms.Any(term => Regex.IsMatch(
            evidence,
            $@"(?<!\p{{L}}){Regex.Escape(term)}(?!\p{{L}})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    internal static string CandidateToken(string source, string candidateId)
    {
        var prefix = new string(source.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToLowerInvariant();
        if (prefix.Length == 0)
        {
            prefix = "source";
        }

        var identity = source + "\n" + candidateId;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12].ToLowerInvariant();
        return $"{prefix}-{hash}";
    }

    private static string GetString(JsonElement item, string propertyName, string fallback = "") =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    internal static int GetInt(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : 0;

    private static int GetFlexibleInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static string JoinJsonText(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(' ', value.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString() ?? string.Empty)),
            _ => string.Empty,
        };
    }

    internal static bool MeetsMinimumDimensions(ChinaExpansionPlan plan, int width, int height) =>
        Math.Max(width, height) >= plan.MinimumWidth &&
        Math.Min(width, height) >= plan.MinimumHeight;

    internal static bool MeetsMinimumDimensions(
        ChinaExpansionPlan plan,
        ExpansionTargetDescriptor descriptor,
        int width,
        int height)
    {
        var minimumWidth = plan.ShortfallException.AllowReducedResolution
            ? plan.ShortfallException.MinimumWidth
            : plan.MinimumWidth;
        var minimumHeight = plan.ShortfallException.AllowReducedResolution
            ? plan.ShortfallException.MinimumHeight
            : plan.MinimumHeight;
        return Math.Max(width, height) >= minimumWidth &&
               Math.Min(width, height) >= minimumHeight;
    }

    private static bool TryHttpsUri(string value, out Uri uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
        uri = parsed ?? new Uri("https://invalid.invalid/");
        return valid;
    }

    private static string? HttpsOrNull(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^incategory:\"(?<category>[^\"]+)\"$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExactWikimediaCategoryRegex();

    [GeneratedRegex("[0-9]+\\.[0-9]+")]
    private static partial Regex VersionRegex();

    private sealed record CandidateDownload(
        WikimediaCandidate Candidate,
        DownloadedImage Image,
        string CachePath);

    private sealed record QueryCandidateResult(
        ExpansionQuery Query,
        WikimediaCandidate Candidate,
        CandidateDownload? Download);
}
