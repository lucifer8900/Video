using System.Text.Json.Nodes;

namespace Lingmai.RedMist.ReferenceLibrary;

internal static class ArtDirectionValidator
{
    private static readonly string[] RequiredDirection =
    [
        "tang_grand_ceremonial",
        "song_elegant_daily",
        "original_xianxia",
    ];

    private static readonly string[] RequiredHumanoids =
    [
        "character.shen_yan",
        "character.chu_mingqi",
        "character.shi_jun",
        "character.celadon_scout",
        "character.cavern_ally",
        "character.formation_spirit",
    ];

    private static readonly string[] RequiredEmotions =
    [
        "neutral",
        "calm_warmth",
        "restrained_joy",
        "resolve",
        "vigilance",
        "suspicion",
        "concern",
        "grief",
        "anger_controlled",
        "fear_suppressed",
        "pain",
        "astonishment",
    ];

    private static readonly string[] RequiredEnvironmentDomains =
    [
        "celestial_palaces_gates",
        "gardens_herb_gardens",
        "mountains_cloud_seas",
        "caverns_subterranean_palaces",
        "formation_altars",
        "combat_spaces",
        "weather_celestial_phenomena",
        "outer_heaven_wonders",
    ];

    private static readonly HashSet<string> RequiredObservations =
    [
        "costume",
        "architecture",
        "landscape",
        "lighting",
    ];

    private static readonly HashSet<string> RequiredProhibitions =
    [
        "no_dirty_gray",
        "no_unmotivated_rags",
        "no_unmotivated_wear",
        "no_modern_leather_shoes",
        "no_qing_clothing",
        "no_plastic_armor",
        "no_actor_likeness",
        "no_exact_costume_copy",
        "no_exact_set_copy",
        "no_cross_work_mashup",
        "no_global_darkness",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> RequiredStages =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["character.shen_yan"] = ["early", "mid", "late"],
            ["character.chu_mingqi"] = ["early", "mid", "late"],
            ["character.shi_jun"] = ["primary", "secondary"],
            ["character.celadon_scout"] = ["primary", "secondary"],
            ["character.cavern_ally"] = ["single"],
            ["character.formation_spirit"] = ["single"],
        };

    private static readonly HashSet<string> OfficialHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "iq.com",
        "www.iq.com",
        "youku.com",
        "www.youku.com",
        "v.youku.com",
        "vo.youku.com",
        "youku.tv",
        "www.youku.tv",
        "v.qq.com",
        "wetv.vip",
        "www.wetv.vip",
    };

    public static IReadOnlyList<CatalogDiagnostic> Validate(string manifestPath, string markdownPath)
    {
        var diagnostics = new List<CatalogDiagnostic>();
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("CX-516 art direction JSON was empty.");
        var markdown = File.ReadAllText(markdownPath);

        ValidateApproval(manifest, diagnostics);
        ValidateStyle(manifest, diagnostics);
        ValidateResearch(manifest, diagnostics);
        ValidateCharacterScope(manifest, diagnostics);
        ValidateCostumes(manifest, diagnostics);
        ValidateCreature(manifest, diagnostics);
        ValidateEnvironment(manifest, diagnostics);
        ValidateProductionPolicy(manifest, diagnostics);
        ValidateMarkdown(manifest, markdown, diagnostics);
        return diagnostics;
    }

    private static void ValidateApproval(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var approval = manifest["approval"] as JsonObject;
        if (Value(manifest, "cardId") != "CX-516" ||
            Value(approval, "status") != "approved" ||
            Value(approval, "approvedOn") != "2026-08-03" ||
            Values(approval?["decisions"]).Length < 6)
        {
            diagnostics.Add(new(
                "approval.missing",
                "$.approval",
                "CX-516 must preserve the user's 2026-08-03 approvals and all six recorded decisions."));
        }
    }

    private static void ValidateStyle(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var art = manifest["artDirection"] as JsonObject;
        if (!Values(art?["primaryDirection"]).SequenceEqual(RequiredDirection, StringComparer.Ordinal))
        {
            diagnostics.Add(new(
                "style.direction",
                "$.artDirection.primaryDirection",
                "The direction must remain Tang grand ceremonial + Song elegant daily + original xianxia, in that order."));
        }

        var prohibitions = Values(art?["prohibitions"]).ToHashSet(StringComparer.Ordinal);
        if (!RequiredProhibitions.IsSubsetOf(prohibitions))
        {
            diagnostics.Add(new(
                "style.prohibitions",
                "$.artDirection.prohibitions",
                "Dirty gray, rags, unjustified wear, modern shoes, Qing dress, plastic armor, likeness and exact-copy prohibitions are mandatory."));
        }

        var tone = art?["tonalPrinciples"] as JsonObject;
        if (Bool(tone, "noGlobalGloom") != true ||
            Bool(tone, "brightMajesty") != true ||
            Bool(tone, "controlledContrast") != true ||
            Bool(tone, "dayDuskNightContrast") != true ||
            Bool(tone, "physicalMaterials") != true)
        {
            diagnostics.Add(new(
                "style.no_global_darkness",
                "$.artDirection.tonalPrinciples",
                "Majesty requires readable physical materials and day/dusk/night contrast; global gloom is forbidden."));
        }

        var antiCopying = art?["antiCopying"] as JsonObject;
        if (Bool(antiCopying, "actorLikenessAllowed") != false ||
            Bool(antiCopying, "exactCostumeReplicationAllowed") != false ||
            Bool(antiCopying, "exactPatternReplicationAllowed") != false ||
            Bool(antiCopying, "exactSetReplicationAllowed") != false ||
            Bool(antiCopying, "directCrossWorkMashupAllowed") != false)
        {
            diagnostics.Add(new(
                "research.exact_replication",
                "$.artDirection.antiCopying",
                "Research may inform abstract principles only; likeness, exact copying and direct mashups are forbidden."));
        }
    }

    private static void ValidateResearch(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["researchPolicy"] as JsonObject;
        var sources = Objects(policy?["sources"]);
        if (sources.Length < 8 || Int(policy, "minimumWorks") != 8)
        {
            diagnostics.Add(new(
                "research.minimum_sources",
                "$.researchPolicy.sources",
                "At least eight distinct official work pages are required."));
        }

        var observations = sources
            .SelectMany(source => Values(source["observations"]))
            .ToHashSet(StringComparer.Ordinal);
        if (!RequiredObservations.IsSubsetOf(observations))
        {
            diagnostics.Add(new(
                "research.observation_coverage",
                "$.researchPolicy.sources[*].observations",
                "Research must cover costume, architecture, landscape and lighting."));
        }

        if (Bool(policy, "officialWorkPagesOnly") != true ||
            sources.Any(source => !IsOfficialPage(Value(source, "officialPageUrl"))))
        {
            diagnostics.Add(new(
                "research.official_page",
                "$.researchPolicy.sources[*].officialPageUrl",
                "Every checked-in research record must point to an HTTPS official platform page."));
        }

        var identityDesigns = Objects(manifest["identityDesigns"]);
        if (Bool(policy, "identityReuse") != false ||
            sources.Any(source => Bool(source, "identityReuse") != false) ||
            identityDesigns.Any(identity => Bool(identity, "noActorLikeness") != true))
        {
            diagnostics.Add(new(
                "research.identity_reuse",
                "$.researchPolicy.identityReuse",
                "Actor or reference-person identity reuse must remain disabled."));
        }

        if (sources.Any(source => Bool(source, "exactReplicationAllowed") != false))
        {
            diagnostics.Add(new(
                "research.exact_replication",
                "$.researchPolicy.sources[*].exactReplicationAllowed",
                "No source may authorize exact replication in the original game art."));
        }

        if (Bool(policy, "referenceOnly") != true ||
            Bool(policy, "shipInBuild") != false ||
            Bool(policy, "downloadedStillsTracked") != false ||
            sources.Any(source => Bool(source, "referenceOnly") != true || Bool(source, "shipInBuild") != false))
        {
            diagnostics.Add(new(
                "research.reference_only",
                "$.researchPolicy",
                "Research must remain private/reference-only, untracked and excluded from builds."));
        }

        var privateRoot = Value(policy, "privateReferenceRoot").Replace('\\', '/');
        if (privateRoot != "content/reference-library/private-research/cx516" ||
            privateRoot.StartsWith("unity/", StringComparison.OrdinalIgnoreCase) ||
            privateRoot.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) ||
            privateRoot.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
            privateRoot.Contains("/Addressables/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new(
                "research.runtime_path",
                "$.researchPolicy.privateReferenceRoot",
                "Private research may not enter Unity or another runtime asset path."));
        }
    }

    private static void ValidateCharacterScope(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var scope = manifest["characterScope"] as JsonObject;
        if (!Values(scope?["humanoidCharacterIds"]).SequenceEqual(RequiredHumanoids, StringComparer.Ordinal) ||
            !Values(scope?["creatureIds"]).SequenceEqual(["creature.ink_dragon"], StringComparer.Ordinal) ||
            !Objects(manifest["identityDesigns"]).Select(item => Value(item, "characterId"))
                .SequenceEqual(RequiredHumanoids, StringComparer.Ordinal))
        {
            diagnostics.Add(new(
                "scope.characters",
                "$.characterScope",
                "CX-516 is fixed to the ordered six humanoids and one ink dragon."));
        }

        var expressions = scope?["expressionReferenceSet"] as JsonObject;
        if (Value(expressions, "strategy") != "reference_assets" ||
            Int(expressions, "countPerHumanoid") != 12 ||
            Bool(expressions, "trainingModelPlanned") != false ||
            !Values(expressions?["emotionIds"]).SequenceEqual(RequiredEmotions, StringComparer.Ordinal))
        {
            diagnostics.Add(new(
                "expression.count",
                "$.characterScope.expressionReferenceSet",
                "Each humanoid requires the approved ordered set of 12 reference expressions, not a trainable model."));
        }
    }

    private static void ValidateCostumes(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var entries = Objects(manifest["costumePlan"]);
        if (entries.Any(entry => Value(entry, "characterId").StartsWith("creature.", StringComparison.Ordinal)))
        {
            diagnostics.Add(new(
                "creature.no_outfits",
                "$.costumePlan",
                "The ink dragon may not appear in the humanoid costume matrix."));
        }

        if (!entries.Select(entry => Value(entry, "characterId")).SequenceEqual(RequiredHumanoids, StringComparer.Ordinal))
        {
            diagnostics.Add(new(
                "scope.characters",
                "$.costumePlan",
                "Costume entries must follow the approved six-character order."));
        }

        var total = 0;
        foreach (var entry in entries)
        {
            var characterId = Value(entry, "characterId");
            var outfits = Objects(entry["outfits"]);
            total += outfits.Length;
            if (!RequiredStages.TryGetValue(characterId, out var expectedStages) ||
                !outfits.Select(outfit => Value(outfit, "stage")).SequenceEqual(expectedStages, StringComparer.Ordinal))
            {
                diagnostics.Add(new(
                    "costume.stage_count",
                    "$.costumePlan",
                    $"Costume stages for '{characterId}' do not match the approved matrix."));
            }
        }

        if (total != 12)
        {
            diagnostics.Add(new(
                "costume.total",
                "$.costumePlan",
                $"The approved matrix contains exactly 12 outfits, found {total}."));
        }
    }

    private static void ValidateCreature(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var creature = manifest["creatureDesign"] as JsonObject;
        var prohibitions = Values(creature?["prohibitions"]).ToHashSet(StringComparer.Ordinal);
        if (Value(creature, "creatureId") != "creature.ink_dragon" ||
            Value(creature, "category") != "original_chinese_jiao_dragon" ||
            Value(creature, "wings") != "none" ||
            Int(creature, "humanOutfitCount") != 0 ||
            !new[] { "no_lizard_anatomy", "no_wings", "no_extra_limbs", "no_western_dragon_head" }
                .All(prohibitions.Contains))
        {
            diagnostics.Add(new(
                "creature.no_outfits",
                "$.creatureDesign",
                "The ink dragon must be a four-limbed, wingless Chinese jiao with zero humanoid outfits."));
        }
    }

    private static void ValidateEnvironment(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var environments = Objects(manifest["environmentPlan"]);
        if (!environments.Select(item => Value(item, "domainId"))
            .SequenceEqual(RequiredEnvironmentDomains, StringComparer.Ordinal))
        {
            diagnostics.Add(new(
                "environment.coverage",
                "$.environmentPlan",
                "All eight ordered environment domains are required."));
        }

        if (environments.Any(item =>
            string.IsNullOrWhiteSpace(Value(item, "dayLook")) ||
            string.IsNullOrWhiteSpace(Value(item, "duskLook")) ||
            string.IsNullOrWhiteSpace(Value(item, "nightLook")) ||
            Values(item["interactionCues"]).Length == 0))
        {
            diagnostics.Add(new(
                "environment.day_dusk_night",
                "$.environmentPlan",
                "Every environment requires day, dusk, night and interaction definitions."));
        }
    }

    private static void ValidateProductionPolicy(JsonObject manifest, List<CatalogDiagnostic> diagnostics)
    {
        var policy = manifest["productionPolicy"] as JsonObject;
        var noGeneration = new[]
        {
            "imageGenerationThisCard",
            "videoGenerationThisCard",
            "flow2ApiUsed",
            "geminiUsed",
            "veoUsed",
            "overwriteExistingAssets",
            "plotChanges",
            "dialogueChanges",
            "characterNameChanges",
            "unityAdoption",
        }.All(name => Bool(policy, name) == false);
        var future = manifest["futureVideoConstraints"] as JsonObject;
        var safeFutureVideo = Value(future, "defaultModel") == "veo_3_1_i2v_lite_landscape" &&
            Value(future, "networkMode") == "ssh_loopback_when_enabled" &&
            Int(future, "maxClipsPerBatch") == 5 &&
            Int(future, "candidatesPerShot") == 1 &&
            Int(future, "automaticRetries") == 0 &&
            Bool(future, "stripVeoAudio") == true &&
            Bool(future, "voiceAndSoundIndependent") == true &&
            Bool(future, "dispatchEnabled") == false;
        if (!noGeneration || !safeFutureVideo)
        {
            diagnostics.Add(new(
                "policy.no_generation",
                "$.productionPolicy",
                "CX-516 generates nothing; future video remains tunnel-gated, Lite-only, 5-per-batch, one-per-shot, zero-retry and audio-separated."));
        }

        if (Value(policy, "cx514Status") != "paused")
        {
            diagnostics.Add(new(
                "policy.cx514_paused",
                "$.productionPolicy.cx514Status",
                "CX-514 must remain paused until prerequisite visual assets are completed and reviewed."));
        }
    }

    private static void ValidateMarkdown(
        JsonObject manifest,
        string markdown,
        List<CatalogDiagnostic> diagnostics)
    {
        var required = Values(manifest["markdownContract"]?["requiredPhrases"]);
        if (required.Length < 8 || required.Any(phrase => !markdown.Contains(phrase, StringComparison.Ordinal)))
        {
            diagnostics.Add(new(
                "markdown.mismatch",
                "$.markdownContract.requiredPhrases",
                "The human-readable art bible must contain every machine-manifest anchor phrase."));
        }
    }

    private static bool IsOfficialPage(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        OfficialHosts.Contains(uri.Host);

    private static JsonObject[] Objects(JsonNode? node) =>
        node is JsonArray array
            ? array.Where(item => item is JsonObject).Select(item => item!.AsObject()).ToArray()
            : [];

    private static string[] Values(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? string.Empty).ToArray()
            : [];

    private static string Value(JsonObject? node, string property) =>
        node?[property]?.GetValue<string>() ?? string.Empty;

    private static bool? Bool(JsonObject? node, string property) =>
        node?[property]?.GetValue<bool>();

    private static int? Int(JsonObject? node, string property) =>
        node?[property]?.GetValue<int>();
}
