namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ChinaExpansionPlanValidator
{
    private static readonly string[] ProvinceRegionIds =
    [
        "beijing", "tianjin", "hebei", "shanxi", "inner_mongolia", "liaoning", "jilin",
        "heilongjiang", "shanghai", "jiangsu", "zhejiang", "anhui", "fujian", "jiangxi",
        "shandong", "henan", "hubei", "hunan", "guangdong", "guangxi", "hainan",
        "chongqing", "sichuan", "guizhou", "yunnan", "tibet", "shaanxi", "gansu",
        "qinghai", "ningxia", "xinjiang", "hong_kong", "macau", "taiwan",
    ];

    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "landscape", "architecture", "plants", "animals", "waters", "weather", "astronomy",
        "people", "costumes_textiles", "artifacts", "geology_caves", "light_fog_fire",
    };

    private static readonly string[] CategorySubjectIds =
    [
        "plants", "animals", "waters", "weather", "astronomy", "people",
        "costumes_textiles", "artifacts", "geology_caves", "light_fog_fire",
    ];

    public static IReadOnlyList<CatalogDiagnostic> Validate(ChinaExpansionPlan plan)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        if (plan.SchemaVersion != "2.0.0" || plan.PlanId != "cx507-china-visual-atlas-v1")
        {
            diagnostics.Add(new("plan.identity", "", "Expected the approved CX-507 v2 plan identity."));
        }

        if (!plan.ReferenceOnly || plan.ShipInBuild)
        {
            diagnostics.Add(new("plan.shipping_boundary", "", "CX-507 references must remain reference-only and outside builds."));
        }

        if (!plan.AllowedLicenses.SequenceEqual(["cc0", "pdm", "by"], StringComparer.Ordinal) ||
            !plan.CommercialUseRequired || !plan.ModificationRequired || !plan.RejectShareAlike)
        {
            diagnostics.Add(new("plan.license_policy", "/allowedLicenses", "Only strict CC0/PDM/CC BY commercial-modification sources are allowed."));
        }

        var shortfall = plan.ShortfallException;
        if (!shortfall.AllowUnverifiedSource || !shortfall.AllowReducedResolution ||
            !shortfall.RequiresManualReview || shortfall.ShipInBuild ||
            shortfall.MinimumWidth is < 320 or >= 800 ||
            shortfall.MinimumHeight is < 180 or >= 600 ||
            shortfall.MinimumWidth > plan.MinimumWidth ||
            shortfall.MinimumHeight > plan.MinimumHeight)
        {
            diagnostics.Add(new(
                "plan.shortfall_exception",
                "/shortfallException",
                "Shortfall exceptions must be manual-review-only, reference-only, lower bounded resolution and cannot weaken the strict default policy."));
        }

        var unverified = plan.UnverifiedSourcePolicy;
        if (!unverified.Allow || !unverified.PersonalUseOnly || !unverified.RequiresManualReview || unverified.ShipInBuild)
        {
            diagnostics.Add(new(
                "plan.unverified_source_policy",
                "/unverifiedSourcePolicy",
                "Unverified sources are allowed only for personal reference use, require manual review and cannot ship in builds."));
        }

        if (!plan.ArchitectureMustExcludePeople ||
            !new[] { "crowd", "tourist", "visitor", "people" }.All(required =>
                plan.ArchitecturePersonExclusionTerms.Contains(required, StringComparer.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new("plan.people_free_architecture", "/architecturePersonExclusionTerms", "Architecture references must exclude people and common visitor metadata."));
        }

        if (!new[] { "bronze", "artifact", "sculpture", "statue", "chariot" }.All(required =>
                plan.ArchitectureSubjectExclusionTerms.Contains(required, StringComparer.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new(
                "plan.architecture_subject_boundary",
                "/architectureSubjectExclusionTerms",
                "Architecture discovery must reject museum objects, sculpture and vehicle artifacts."));
        }

        if (plan.MinimumTotalAssets < 800 ||
            plan.ThumbnailWidth is < 1200 or > 4096 ||
            plan.PageSize is < 1 or > 50 ||
            plan.MaxPagesPerQuery is < 1 or > 10 ||
            plan.MaxDownloadBytes is < 64 or > 50_000_000 ||
            plan.MinimumWidth < 800 || plan.MinimumHeight < 600 ||
            plan.RequestDelayMilliseconds is < 0 or > 5000 ||
            !Uri.TryCreate(plan.DiscoveryEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(endpoint.Host, "commons.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(plan.INaturalistEndpoint, UriKind.Absolute, out var iNaturalistEndpoint) ||
            iNaturalistEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(iNaturalistEndpoint.Host, "api.inaturalist.org", StringComparison.OrdinalIgnoreCase) ||
            plan.INaturalistPlaceId != 6903 ||
            !Uri.TryCreate(plan.MetMuseumEndpoint, UriKind.Absolute, out var metMuseumEndpoint) ||
            metMuseumEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(metMuseumEndpoint.Host, "collectionapi.metmuseum.org", StringComparison.OrdinalIgnoreCase) ||
            !metMuseumEndpoint.AbsolutePath.Equals("/public/collection/v1", StringComparison.Ordinal) ||
            plan.MetMuseumDepartmentId != 6 ||
            !Uri.TryCreate(plan.ClevelandMuseumEndpoint, UriKind.Absolute, out var clevelandMuseumEndpoint) ||
            clevelandMuseumEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(clevelandMuseumEndpoint.Host, "openaccess-api.clevelandart.org", StringComparison.OrdinalIgnoreCase) ||
            !clevelandMuseumEndpoint.AbsolutePath.Equals("/api/artworks/", StringComparison.Ordinal) ||
            !Uri.TryCreate(plan.ArtInstituteChicagoEndpoint, UriKind.Absolute, out var artInstituteChicagoEndpoint) ||
            artInstituteChicagoEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(artInstituteChicagoEndpoint.Host, "api.artic.edu", StringComparison.OrdinalIgnoreCase) ||
            !artInstituteChicagoEndpoint.AbsolutePath.Equals("/api/v1/artworks", StringComparison.Ordinal))
        {
            diagnostics.Add(new("plan.bounds", "", "Acquisition bounds or discovery endpoint violate CX-507 policy."));
        }

        var requiredCategoryFloors = Categories.ToDictionary(
            category => category,
            category => category is "architecture" or "landscape" ? 160 : 40,
            StringComparer.Ordinal);
        if (plan.MinimumAssetsPerCategory.Count != requiredCategoryFloors.Count ||
            requiredCategoryFloors.Any(required =>
                !plan.MinimumAssetsPerCategory.TryGetValue(required.Key, out var actual) ||
                actual < required.Value))
        {
            diagnostics.Add(new(
                "coverage.category_floor",
                "/minimumAssetsPerCategory",
                "Architecture and landscape need 160 references each; every other category needs at least 40."));
        }

        var actualProvinceIds = plan.Coverage.ProvinceRegions.Select(target => target.Id).ToArray();
        if (!actualProvinceIds.SequenceEqual(ProvinceRegionIds, StringComparer.Ordinal))
        {
            diagnostics.Add(new("coverage.province_set", "/coverage/provinceRegions", "Exactly the approved 34 province-level region IDs are required."));
        }

        foreach (var region in plan.Coverage.ProvinceRegions)
        {
            if (region.MinimumAssets < 8 || region.MinimumArchitectureAssets < 4 ||
                region.MinimumNaturalAssets < 4 || region.Queries.Count < 4)
            {
                diagnostics.Add(new("coverage.province_quota", $"/coverage/provinceRegions/{region.Id}", "Each region needs 8 total, 4 architecture and 4 natural references."));
            }
        }

        var actualCategorySubjectIds = plan.Coverage.CategorySubjects.Select(target => target.Id).ToArray();
        if (!actualCategorySubjectIds.SequenceEqual(CategorySubjectIds, StringComparer.Ordinal))
        {
            diagnostics.Add(new(
                "coverage.category_subject_set",
                "/coverage/categorySubjects",
                "Exactly the approved ten non-core reference category targets are required in stable order."));
        }

        foreach (var target in plan.Coverage.CategorySubjects)
        {
            if (target.MinimumAssets < 40 || target.Queries.Count < 10 ||
                target.Queries.Any(query =>
                    !string.Equals(query.Category, target.Id, StringComparison.Ordinal) ||
                    query.MinimumAssets is null or < 4))
            {
                diagnostics.Add(new(
                    "coverage.category_subject_quota",
                    $"/coverage/categorySubjects/{target.Id}",
                    "Each non-core category needs ten category-matched queries with four references each."));
            }
        }

        var targets = EnumerateTargets(plan).ToArray();
        foreach (var duplicate in targets.GroupBy(target => target.TargetId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new("coverage.duplicate_target", "/coverage", duplicate.Key));
        }

        foreach (var descriptor in targets)
        {
            var target = descriptor.Target;
            if (string.IsNullOrWhiteSpace(target.Id) || string.IsNullOrWhiteSpace(target.Label) ||
                target.MinimumAssets < 1 || target.Queries.Count == 0 || target.EvidenceTerms.Count == 0)
            {
                diagnostics.Add(new("coverage.target", $"/coverage/{descriptor.TargetId}", "Target metadata, evidence and query quotas are required."));
            }

            foreach (var query in target.Queries)
            {
                if ((query.MinimumAssets.HasValue && string.IsNullOrWhiteSpace(query.Id)) ||
                    string.IsNullOrWhiteSpace(query.Search) ||
                    !Categories.Contains(query.Category) ||
                    query.MinimumAssets is < 1 or > 10 ||
                    !plan.SubjectEvidenceTerms.TryGetValue(query.Subject, out var subjectTerms) ||
                    subjectTerms.Count == 0)
                {
                    diagnostics.Add(new("coverage.query", $"/coverage/{descriptor.TargetId}/queries", "Query search, category and known subject are required."));
                }

                if (descriptor.Dimension == "category" &&
                    (query.EvidenceTerms.Count == 0 || query.EvidenceTerms.Any(string.IsNullOrWhiteSpace)))
                {
                    diagnostics.Add(new(
                        "coverage.query_evidence",
                        $"/coverage/{descriptor.TargetId}/queries/{query.Id}",
                        "Category queries require specific subject evidence terms to prevent cross-query reuse."));
                }

                if (descriptor.Dimension == "category" &&
                    query.Category is "plants" or "animals" &&
                    (string.IsNullOrWhiteSpace(query.INaturalistTaxon) || query.INaturalistTaxonId is null or <= 0))
                {
                    diagnostics.Add(new(
                        "coverage.inaturalist_taxon",
                        $"/coverage/{descriptor.TargetId}/queries/{query.Id}/iNaturalistTaxon",
                        "Plant and animal queries require an explicit iNaturalist taxon name and taxon ID within the pinned China place."));
                }

                if (descriptor.Dimension == "category" &&
                    query.Category is "artifacts" or "costumes_textiles" &&
                    string.IsNullOrWhiteSpace(query.MetMuseumQuery))
                {
                    diagnostics.Add(new(
                        "coverage.met_museum_query",
                        $"/coverage/{descriptor.TargetId}/queries/{query.Id}/metMuseumQuery",
                        "Artifact and costume queries require an explicit Met Open Access collection query."));
                }
            }
        }

        var plannedMinimum = targets.Sum(target => target.Target.MinimumAssets);
        if (plannedMinimum < plan.MinimumTotalAssets)
        {
            diagnostics.Add(new("coverage.total", "/coverage", $"Coverage targets plan {plannedMinimum}, below {plan.MinimumTotalAssets}."));
        }

        if (plan.Coverage.HistoricalCapitals.Count < 14 ||
            plan.Coverage.HistoricalPeriods.Count < 9 ||
            plan.Coverage.GardenTypes.Count < 6 ||
            plan.Coverage.Landforms.Count < 16 ||
            plan.Coverage.WeatherPhenomena.Count < 25 ||
            plan.Coverage.CategorySubjects.Count != CategorySubjectIds.Length)
        {
            diagnostics.Add(new("coverage.dimension", "/coverage", "Historical, garden, landform or weather target set is incomplete."));
        }

        return diagnostics;
    }

    public static IEnumerable<ExpansionTargetDescriptor> EnumerateTargets(ChinaExpansionPlan plan)
    {
        foreach (var target in plan.Coverage.ProvinceRegions) yield return new("province", target);
        foreach (var target in plan.Coverage.HistoricalCapitals) yield return new("capital", target);
        foreach (var target in plan.Coverage.HistoricalPeriods) yield return new("period", target);
        foreach (var target in plan.Coverage.GardenTypes) yield return new("garden", target);
        foreach (var target in plan.Coverage.Landforms) yield return new("landform", target);
        foreach (var target in plan.Coverage.WeatherPhenomena) yield return new("weather", target);
        foreach (var target in plan.Coverage.CategorySubjects) yield return new("category", target);
    }
}
