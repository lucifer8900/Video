namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ExpansionCoverageEvaluator
{
    private static readonly HashSet<string> NaturalCategories = new(StringComparer.Ordinal)
    {
        "landscape", "waters", "weather", "astronomy", "geology_caves", "light_fog_fire",
    };

    public static ExpansionCoverageReport Evaluate(ReferenceCatalog catalog, ChinaExpansionPlan plan)
    {
        var diagnostics = new List<string>();
        if (catalog.SchemaVersion != "2.0.0" || catalog.CoveragePlanId != plan.PlanId)
        {
            diagnostics.Add("catalog.plan_binding: catalog is not bound to the CX-507 v2 plan.");
        }

        if (catalog.Assets.Count < plan.MinimumTotalAssets)
        {
            diagnostics.Add($"catalog.minimum: {catalog.Assets.Count}/{plan.MinimumTotalAssets} assets.");
        }

        AddDuplicateDiagnostics(catalog.Assets, asset => asset.SourcePageUrl, "sourcePageUrl", diagnostics);
        AddDuplicateDiagnostics(catalog.Assets, asset => asset.DownloadUrl, "downloadUrl", diagnostics);
        AddDuplicateDiagnostics(catalog.Assets, asset => asset.Sha256, "sha256", diagnostics);

        var categoryResults = plan.MinimumAssetsPerCategory.Select(required =>
        {
            var actual = catalog.Assets.Count(asset => asset.Category == required.Key);
            var passes = actual >= required.Value;
            if (!passes)
            {
                diagnostics.Add($"coverage.category: {required.Key} has {actual}/{required.Value} assets.");
            }

            return new CategoryCoverageResult
            {
                CategoryId = required.Key,
                RequiredAssets = required.Value,
                ActualAssets = actual,
                Passes = passes,
            };
        }).ToArray();

        var targetResults = new List<ExpansionTargetResult>();
        foreach (var descriptor in ChinaExpansionPlanValidator.EnumerateTargets(plan))
        {
            var assets = catalog.Assets
                .Where(asset => asset.CoverageTargetIds.Contains(descriptor.TargetId, StringComparer.Ordinal))
                .ToArray();
            var architectureCount = assets.Count(asset => asset.Category == "architecture");
            var naturalCount = assets.Count(asset => NaturalCategories.Contains(asset.Category));
            var target = descriptor.Target;
            var passes = assets.Length >= target.MinimumAssets &&
                         (!target.MinimumArchitectureAssets.HasValue || architectureCount >= target.MinimumArchitectureAssets.Value) &&
                         (!target.MinimumNaturalAssets.HasValue || naturalCount >= target.MinimumNaturalAssets.Value);
            if (!passes)
            {
                diagnostics.Add($"coverage.target: {descriptor.TargetId} has {assets.Length}/{target.MinimumAssets} assets.");
            }

            targetResults.Add(new ExpansionTargetResult
            {
                TargetId = descriptor.TargetId,
                Dimension = descriptor.Dimension,
                Label = target.Label,
                RequiredAssets = target.MinimumAssets,
                ActualAssets = assets.Length,
                RequiredArchitectureAssets = target.MinimumArchitectureAssets,
                ActualArchitectureAssets = target.MinimumArchitectureAssets.HasValue ? architectureCount : null,
                RequiredNaturalAssets = target.MinimumNaturalAssets,
                ActualNaturalAssets = target.MinimumNaturalAssets.HasValue ? naturalCount : null,
                Passes = passes,
            });
        }

        var failedTargetIds = targetResults.Where(target => !target.Passes).Select(target => target.TargetId).ToArray();
        var failedCategoryIds = categoryResults.Where(category => !category.Passes)
            .Select(category => category.CategoryId).ToArray();
        return new ExpansionCoverageReport
        {
            PlanId = plan.PlanId,
            CatalogLibraryId = catalog.LibraryId,
            TotalAssets = catalog.Assets.Count,
            Passes = diagnostics.Count == 0 && failedTargetIds.Length == 0,
            FailedTargetIds = failedTargetIds,
            FailedCategoryIds = failedCategoryIds,
            Categories = categoryResults,
            Targets = targetResults,
            Diagnostics = diagnostics,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static void AddDuplicateDiagnostics(
        IReadOnlyList<ReferenceAsset> assets,
        Func<ReferenceAsset, string> selector,
        string field,
        ICollection<string> diagnostics)
    {
        foreach (var duplicate in assets.GroupBy(selector, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add($"catalog.duplicate_{field}: {duplicate.Key}");
        }
    }
}
