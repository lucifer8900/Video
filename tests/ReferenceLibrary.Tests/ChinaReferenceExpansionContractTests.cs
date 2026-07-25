using System.Text.Json;
using Lingmai.RedMist.ReferenceLibrary;

namespace ReferenceLibrary.Tests;

public sealed class ChinaReferenceExpansionContractTests
{
    private static readonly string[] RequiredProvinceRegions =
    [
        "beijing", "tianjin", "hebei", "shanxi", "inner_mongolia", "liaoning", "jilin",
        "heilongjiang", "shanghai", "jiangsu", "zhejiang", "anhui", "fujian", "jiangxi",
        "shandong", "henan", "hubei", "hunan", "guangdong", "guangxi", "hainan",
        "chongqing", "sichuan", "guizhou", "yunnan", "tibet", "shaanxi", "gansu",
        "qinghai", "ningxia", "xinjiang", "hong_kong", "macau", "taiwan",
    ];

    private static readonly string[] ExactCommonsRecoveryTargets =
    [
        "province.tianjin", "province.heilongjiang", "province.zhejiang", "province.hunan",
        "province.guangdong", "province.hainan", "province.chongqing", "province.sichuan",
        "province.guizhou", "province.gansu", "province.macau",
        "capital.anyang_yin", "capital.zhengzhou_shang", "capital.xianyang_qin",
        "capital.changan_xian", "capital.luoyang", "capital.nanjing",
        "capital.hangzhou_linan", "capital.beijing_capital", "capital.datong_pingcheng",
        "capital.linzi_qi", "capital.chengdu_shu", "capital.shenyang_shengjing",
        "period.qin_han", "period.liao_jin_xixia", "period.yuan", "period.ming", "period.qing",
        "garden.royal_garden", "garden.lingnan_garden", "garden.temple_garden",
        "garden.mountain_garden", "garden.water_garden",
        "landform.forest", "landform.grassland", "landform.coast", "landform.volcano",
        "landform.stone_forest",
    ];

    private static readonly string[] ShortfallTargetsAfterCx507Expansion =
    [
        "province.tianjin", "province.heilongjiang", "province.hunan", "province.guangdong",
        "province.chongqing", "province.gansu",
        "capital.anyang_yin", "capital.zhengzhou_shang", "capital.xianyang_qin",
        "capital.changan_xian", "capital.nanjing", "capital.hangzhou_linan",
        "capital.datong_pingcheng", "capital.linzi_qi", "capital.chengdu_shu",
        "capital.shenyang_shengjing", "period.liao_jin_xixia", "period.yuan", "period.ming",
        "period.qing", "garden.royal_garden", "garden.lingnan_garden", "garden.temple_garden",
        "garden.mountain_garden", "landform.forest", "landform.coast", "landform.volcano",
    ];

    private static readonly string[] ShortfallTargetsAfterFirstRecovery =
    [
        "province.tianjin", "province.heilongjiang", "province.chongqing", "province.gansu",
        "capital.zhengzhou_shang", "capital.xianyang_qin", "capital.changan_xian",
        "capital.nanjing", "capital.hangzhou_linan", "capital.datong_pingcheng",
        "capital.linzi_qi", "period.liao_jin_xixia", "period.yuan", "period.qing",
        "garden.royal_garden", "garden.lingnan_garden", "garden.temple_garden",
        "garden.mountain_garden", "landform.forest", "landform.coast", "landform.volcano",
    ];

    private static readonly string[] ShortfallTargetsAfterSecondRecovery =
    [
        "province.tianjin", "province.heilongjiang", "province.chongqing",
        "capital.zhengzhou_shang", "capital.xianyang_qin", "capital.changan_xian",
        "capital.nanjing", "capital.hangzhou_linan", "capital.datong_pingcheng",
        "capital.linzi_qi", "period.yuan", "period.qing", "garden.lingnan_garden",
        "garden.temple_garden", "garden.mountain_garden", "landform.coast", "landform.volcano",
    ];

    private static readonly string[] ShortfallTargetsAfterThirdRecovery =
    [
        "province.tianjin", "province.heilongjiang", "province.chongqing",
        "capital.zhengzhou_shang", "capital.xianyang_qin", "capital.changan_xian",
        "capital.hangzhou_linan", "capital.datong_pingcheng", "capital.linzi_qi",
        "period.yuan", "period.qing", "garden.lingnan_garden", "garden.temple_garden",
        "garden.mountain_garden", "landform.coast", "landform.volcano",
    ];

    private static readonly string[] ShortfallTargetsAfterFourthRecovery =
    [
        "province.tianjin", "province.heilongjiang", "province.chongqing",
        "capital.zhengzhou_shang", "capital.xianyang_qin", "capital.changan_xian",
        "capital.datong_pingcheng", "capital.linzi_qi", "period.yuan", "period.qing",
        "garden.lingnan_garden", "garden.temple_garden", "garden.mountain_garden",
        "landform.coast", "landform.volcano",
    ];

    private static readonly string[] ShortfallTargetsAfterFifthRecovery =
    [
        "province.tianjin", "province.heilongjiang", "province.chongqing",
        "capital.zhengzhou_shang", "capital.xianyang_qin", "capital.changan_xian",
        "capital.linzi_qi", "period.yuan", "period.qing", "garden.lingnan_garden",
        "garden.temple_garden", "landform.coast", "landform.volcano",
    ];

    [Fact]
    public void OpenverseNullDimensionsAreTreatedAsMissingMetadata()
    {
        using var document = JsonDocument.Parse("""{"width":null,"height":"unknown"}""");

        Assert.Equal(0, WikimediaExpansionAcquirer.GetInt(document.RootElement, "width"));
        Assert.Equal(0, WikimediaExpansionAcquirer.GetInt(document.RootElement, "height"));
        Assert.Equal(0, WikimediaExpansionAcquirer.GetInt(document.RootElement, "missing"));
    }

    [Fact]
    public void Cx507PlanLocksStrictLicenseAndEightHundredAssetFloor()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var root = document.RootElement;

        Assert.Equal("2.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("cx507-china-visual-atlas-v1", root.GetProperty("planId").GetString());
        Assert.Equal(800, root.GetProperty("minimumTotalAssets").GetInt32());
        Assert.Equal(
            ["cc0", "pdm", "by"],
            root.GetProperty("allowedLicenses").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.True(root.GetProperty("commercialUseRequired").GetBoolean());
        Assert.True(root.GetProperty("modificationRequired").GetBoolean());
        Assert.True(root.GetProperty("rejectShareAlike").GetBoolean());
        Assert.True(root.GetProperty("referenceOnly").GetBoolean());
        Assert.False(root.GetProperty("shipInBuild").GetBoolean());

        var unverifiedPolicy = root.GetProperty("unverifiedSourcePolicy");
        Assert.True(unverifiedPolicy.GetProperty("allow").GetBoolean());
        Assert.True(unverifiedPolicy.GetProperty("personalUseOnly").GetBoolean());
        Assert.True(unverifiedPolicy.GetProperty("requiresManualReview").GetBoolean());
        Assert.False(unverifiedPolicy.GetProperty("shipInBuild").GetBoolean());
    }

    [Fact]
    public void Cx507PlanRequiresBalancedCategoryFloors()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var floors = document.RootElement.GetProperty("minimumAssetsPerCategory");

        Assert.Equal(160, floors.GetProperty("architecture").GetInt32());
        Assert.Equal(160, floors.GetProperty("landscape").GetInt32());
        foreach (var category in new[]
                 {
                     "plants", "animals", "waters", "weather", "astronomy", "people",
                     "costumes_textiles", "artifacts", "geology_caves", "light_fog_fire",
                 })
        {
            Assert.True(floors.GetProperty(category).GetInt32() >= 40, category);
        }
    }

    [Fact]
    public void Cx507PlanRequiresPeopleFreeArchitectureReferences()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var root = document.RootElement;

        Assert.True(root.GetProperty("architectureMustExcludePeople").GetBoolean());
        var exclusions = root.GetProperty("architecturePersonExclusionTerms")
            .EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("crowd", exclusions);
        Assert.Contains("tourist", exclusions);
        Assert.Contains("visitor", exclusions);
        Assert.Contains("people", exclusions);
    }

    [Fact]
    public void Cx507PlanRejectsMuseumObjectsFromArchitectureReferences()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var exclusions = document.RootElement.GetProperty("architectureSubjectExclusionTerms")
            .EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("bronze", exclusions);
        Assert.Contains("artifact", exclusions);
        Assert.Contains("sculpture", exclusions);
        Assert.Contains("statue", exclusions);
        Assert.Contains("chariot", exclusions);
    }

    [Fact]
    public void Cx507PlanRequiresHighResolutionReferenceSources()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var root = document.RootElement;

        Assert.True(root.GetProperty("thumbnailWidth").GetInt32() >= 2560);
        Assert.True(root.GetProperty("minimumWidth").GetInt32() >= 1600);
        Assert.True(root.GetProperty("minimumHeight").GetInt32() >= 900);

        var excluded = root.GetProperty("excludedTerms").EnumerateArray()
            .Select(item => item.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("museum display", excluded);
        Assert.Contains("architectural model", excluded);
        Assert.Contains("modern building", excluded);
    }

    [Fact]
    public void Cx507StrictHighResolutionDiscoveryPrefersWikimediaCommonsBeforeOpenverse()
    {
        Assert.Equal(
            new[] { "wikimedia_commons", "openverse" },
            WikimediaExpansionAcquirer.PreferredSourcesForCategory("architecture"));
        Assert.Equal(
            new[] { "wikimedia_commons", "openverse" },
            WikimediaExpansionAcquirer.PreferredSourcesForCategory("landscape"));
        Assert.Equal(
            new[] { "inaturalist", "wikimedia_commons", "openverse" },
            WikimediaExpansionAcquirer.PreferredSourcesForCategory("plants"));
    }

    [Fact]
    public void Cx507PlanPinsStrictChinaINaturalistSpeciesQueries()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var root = document.RootElement;

        Assert.Equal("https://api.inaturalist.org/v1/observations", root.GetProperty("iNaturalistEndpoint").GetString());
        Assert.Equal(6903, root.GetProperty("iNaturalistPlaceId").GetInt32());
        var categoryTargets = root.GetProperty("coverage").GetProperty("categorySubjects")
            .EnumerateArray().Where(target => target.GetProperty("id").GetString() is "plants" or "animals");
        Assert.All(categoryTargets.SelectMany(target => target.GetProperty("queries").EnumerateArray()), query =>
        {
            Assert.False(string.IsNullOrWhiteSpace(query.GetProperty("iNaturalistTaxon").GetString()));
            Assert.True(query.GetProperty("iNaturalistTaxonId").GetInt32() > 0);
        });
    }

    [Fact]
    public void Cx507INaturalistParserAcceptsOnlyStrictLicensedHighResolutionChinaPhotos()
    {
        const string json = """
        {
          "results": [
            {
              "id": 347203671,
              "user": { "login": "chrisdt", "name": null },
              "taxon": { "name": "Ailuropoda melanoleuca melanoleuca", "preferred_common_name": "Sichuan Giant Panda" },
              "photos": [
                {
                  "id": 633028127,
                  "license_code": "cc-by",
                  "original_dimensions": { "width": 1536, "height": 2048 },
                  "url": "https://inaturalist-open-data.s3.amazonaws.com/photos/633028127/square.jpg",
                  "attribution": "(c) chrisdt, some rights reserved (CC BY)",
                  "hidden": false
                },
                {
                  "id": 633028128,
                  "license_code": "cc-by-nc",
                  "original_dimensions": { "width": 3000, "height": 2000 },
                  "url": "https://inaturalist-open-data.s3.amazonaws.com/photos/633028128/square.jpg",
                  "hidden": false
                },
                {
                  "id": 633028129,
                  "license_code": "cc0",
                  "original_dimensions": { "width": 1024, "height": 768 },
                  "url": "https://inaturalist-open-data.s3.amazonaws.com/photos/633028129/square.jpg",
                  "hidden": false
                }
              ]
            }
          ]
        }
        """;
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var animals = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.animals");
        var panda = animals.Target.Queries.Single(query => query.Id == "panda");
        using var document = JsonDocument.Parse(json);

        var candidates = WikimediaExpansionAcquirer.ParseINaturalistCandidates(
            document.RootElement, plan, animals, panda);

        var candidate = Assert.Single(candidates);
        Assert.Equal("inaturalist", candidate.Source);
        Assert.Equal("by", candidate.License);
        Assert.Equal("4.0", candidate.LicenseVersion);
        Assert.Equal(1536, candidate.Width);
        Assert.Equal(2048, candidate.Height);
        Assert.Equal("https://www.inaturalist.org/observations/347203671", candidate.SourcePageUrl.AbsoluteUri);
        Assert.Equal("https://inaturalist-open-data.s3.amazonaws.com/photos/633028127/original.jpg", candidate.DownloadUrl.AbsoluteUri);
    }

    [Fact]
    public void Cx507MuseumSourcesUsePublicDomainChineseOpenAccessObjectsForArtifactsAndCostumes()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        Assert.Equal(
            "https://collectionapi.metmuseum.org/public/collection/v1",
            plan.MetMuseumEndpoint);
        Assert.Equal(6, plan.MetMuseumDepartmentId);
        Assert.Equal(
            "https://openaccess-api.clevelandart.org/api/artworks/",
            plan.ClevelandMuseumEndpoint);
        Assert.Equal(
            "https://api.artic.edu/api/v1/artworks",
            plan.ArtInstituteChicagoEndpoint);
        Assert.Equal(
            new[] { "art_institute_chicago", "cleveland_museum", "met_museum", "wikimedia_commons", "openverse" },
            WikimediaExpansionAcquirer.PreferredSourcesForCategory("artifacts"));
        Assert.Equal(
            new[] { "art_institute_chicago", "cleveland_museum", "met_museum", "wikimedia_commons", "openverse" },
            WikimediaExpansionAcquirer.PreferredSourcesForCategory("costumes_textiles"));
        Assert.Equal(1, WikimediaExpansionAcquirer.ArtInstituteChicagoMaxConcurrentRequests);
        Assert.True(WikimediaExpansionAcquirer.ArtInstituteChicagoMinimumRequestDelayMilliseconds >= 1000);

        var costumes = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.costumes_textiles");
        Assert.Equal(
            new[]
            {
                "robe", "dragon_robe", "court_robe", "ceremonial_robe", "armor",
                "embroidery", "headdress", "shoes", "belt", "silk_textile",
                "rank_badge", "skirt_qun", "sleeve_band", "boots", "surcoat",
                "han_silk", "tang_textile", "tang_footwear", "song_textile",
                "yuan_textile", "ming_robe", "ming_rank_badge", "ming_textile",
                "liao_footwear", "liao_robe", "jin_brocade", "song_canopy",
                "yuan_embroidery",
                "commons_ming_rank_badges", "commons_ming_textiles",
                "commons_yuan_textiles", "commons_tang_textiles", "commons_han_textiles",
            },
            costumes.Target.Queries.Select(query => query.Id).ToArray());
        Assert.All(costumes.Target.Queries, query => Assert.False(string.IsNullOrWhiteSpace(query.MetMuseumQuery)));
        Assert.All(costumes.Target.Queries, query => Assert.False(string.IsNullOrWhiteSpace(query.ArtInstituteChicagoQuery)));

        var artifacts = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.artifacts");
        Assert.Equal(
            new[]
            {
                "bronze_ding", "porcelain", "jade", "sword", "guqin", "lantern", "furniture",
                "bamboo_slips", "seal", "incense_burner", "bronze_mirror", "bronze_bell", "ritual_vessel",
            },
            artifacts.Target.Queries.Select(query => query.Id).ToArray());
        Assert.All(artifacts.Target.Queries, query => Assert.False(string.IsNullOrWhiteSpace(query.MetMuseumQuery)));
        Assert.All(artifacts.Target.Queries, query => Assert.False(string.IsNullOrWhiteSpace(query.ArtInstituteChicagoQuery)));
        var bronze = artifacts.Target.Queries.Single(query => query.Id == "bronze_ding");
        using var acceptedDocument = JsonDocument.Parse("""
        {
          "objectID": 44797,
          "isPublicDomain": true,
          "primaryImage": "https://images.metmuseum.org/CRDImages/as/original/DP251144.jpg",
          "objectURL": "https://www.metmuseum.org/art/collection/search/44797",
          "title": "Ritual bronze ding vessel",
          "objectName": "Ritual vessel",
          "culture": "China",
          "dynasty": "Shang dynasty",
          "period": "Bronze Age",
          "classification": "Bronzes",
          "medium": "Bronze",
          "artistDisplayName": ""
        }
        """);
        using var rejectedDocument = JsonDocument.Parse("""
        {
          "objectID": 44798,
          "isPublicDomain": true,
          "primaryImage": "https://images.metmuseum.org/CRDImages/as/original/DP251145.jpg",
          "objectURL": "https://www.metmuseum.org/art/collection/search/44798",
          "title": "Ritual bronze ding vessel",
          "objectName": "Ritual vessel",
          "culture": "Japan",
          "classification": "Bronzes",
          "medium": "Bronze"
        }
        """);

        Assert.True(WikimediaExpansionAcquirer.TryParseMetMuseumCandidate(
            acceptedDocument.RootElement, plan, artifacts, bronze, out var candidate));
        Assert.NotNull(candidate);
        Assert.Equal("met_museum", candidate!.Source);
        Assert.Equal("pdm", candidate.License);
        Assert.Equal("https://www.metmuseum.org/art/collection/search/44797", candidate.SourcePageUrl.AbsoluteUri);
        Assert.False(WikimediaExpansionAcquirer.TryParseMetMuseumCandidate(
            rejectedDocument.RootElement, plan, artifacts, bronze, out _));
    }

    [Fact]
    public void Cx507ClevelandMuseumParserAcceptsOnlyCC0ChineseHighResolutionObjects()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var costumes = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.costumes_textiles");
        var ceremonial = costumes.Target.Queries.Single(query => query.Id == "ceremonial_robe");
        using var acceptedDocument = JsonDocument.Parse("""
        {
          "id": 153759,
          "title": "Buddhist Priest's Ceremonial Robe",
          "share_license_status": "CC0",
          "culture": ["China, Ming dynasty (1368–1644)"],
          "type": "Garment",
          "department": "Textiles",
          "tombstone": "Ceremonial robe, China, silk and gold thread; embroidery",
          "url": "https://clevelandart.org/art/1987.57",
          "images": {
            "print": {
              "url": "https://openaccess-cdn.clevelandart.org/1987.57/1987.57_print.jpg",
              "width": "3400",
              "height": "1413"
            }
          },
          "creators": []
        }
        """);
        using var rejectedDocument = JsonDocument.Parse("""
        {
          "id": 153760,
          "title": "Chinese Ceremonial Robe",
          "share_license_status": "Copyrighted",
          "culture": ["China"],
          "type": "Garment",
          "tombstone": "Chinese ceremonial robe",
          "url": "https://clevelandart.org/art/1987.58",
          "images": {
            "print": {
              "url": "https://openaccess-cdn.clevelandart.org/1987.58/1987.58_print.jpg",
              "width": "3400",
              "height": "1413"
            }
          }
        }
        """);

        Assert.True(WikimediaExpansionAcquirer.TryParseClevelandMuseumCandidate(
            acceptedDocument.RootElement, plan, costumes, ceremonial, out var candidate));
        Assert.NotNull(candidate);
        Assert.Equal("cleveland_museum", candidate!.Source);
        Assert.Equal("cc0", candidate.License);
        Assert.Equal(3400, candidate.Width);
        Assert.Equal(1413, candidate.Height);
        Assert.False(WikimediaExpansionAcquirer.TryParseClevelandMuseumCandidate(
            rejectedDocument.RootElement, plan, costumes, ceremonial, out _));
    }

    [Fact]
    public void Cx507ArtInstituteChicagoParserAcceptsOnlyCC0ChineseHighResolutionObjects()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var artifacts = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.artifacts");
        var bronze = artifacts.Target.Queries.Single(query => query.Id == "bronze_ding");
        var bell = artifacts.Target.Queries.Single(query => query.Id == "bronze_bell");
        using var acceptedDocument = JsonDocument.Parse("""
        {
          "data": [
            {
              "id": 12086,
              "title": "Ritual bronze ding vessel",
              "thumbnail": {
                "width": 5228,
                "height": 6535,
                "alt_text": "A Chinese ritual bronze ding vessel with geometric decoration."
              },
              "date_display": "Western Zhou dynasty (1046–771 B.C.)",
              "artist_display": "China, probably Hunan province",
              "place_of_origin": "China",
              "medium_display": "Bronze",
              "is_public_domain": true,
              "artwork_type_title": "Metalwork",
              "department_title": "Arts of Asia",
              "classification_title": "bronze",
              "image_id": "7942dae0-6fdd-e6a1-9a23-b64d5c08e8fc"
            }
          ],
          "config": { "iiif_url": "https://www.artic.edu/iiif/2" }
        }
        """);
        using var rejectedDocument = JsonDocument.Parse("""
        {
          "data": [
            {
              "id": 12087,
              "title": "Ritual bronze ding vessel",
              "thumbnail": { "width": 5228, "height": 6535 },
              "place_of_origin": "China",
              "medium_display": "Bronze",
              "is_public_domain": false,
              "classification_title": "bronze",
              "image_id": "8942dae0-6fdd-e6a1-9a23-b64d5c08e8fc"
            }
          ],
          "config": { "iiif_url": "https://www.artic.edu/iiif/2" }
        }
        """);

        var accepted = WikimediaExpansionAcquirer.ParseArtInstituteChicagoCandidates(
            acceptedDocument.RootElement, plan, artifacts, bronze);
        var candidate = Assert.Single(accepted);
        Assert.Equal("art_institute_chicago", candidate.Source);
        Assert.Equal("cc0", candidate.License);
        Assert.Equal("https://www.artic.edu/artworks/12086", candidate.SourcePageUrl.AbsoluteUri);
        Assert.Equal(
            "https://www.artic.edu/iiif/2/7942dae0-6fdd-e6a1-9a23-b64d5c08e8fc/full/1686,/0/default.jpg",
            candidate.DownloadUrl.AbsoluteUri);
        Assert.Empty(WikimediaExpansionAcquirer.ParseArtInstituteChicagoCandidates(
            rejectedDocument.RootElement, plan, artifacts, bronze));

        using var deityWithBellDocument = JsonDocument.Parse("""
        {
          "data": [
            {
              "id": 9611,
              "title": "Vajrasattva Seated on Lotus Flower with a Thunderbolt and Bell",
              "thumbnail": {
                "width": 3798,
                "height": 4747,
                "alt_text": "A seated deity holding a ritual bell."
              },
              "artist_display": "China",
              "place_of_origin": "China",
              "medium_display": "Gilt bronze",
              "is_public_domain": true,
              "artwork_type_title": "Sculpture",
              "department_title": "Arts of Asia",
              "classification_title": "bronze",
              "image_id": "923bf4271-c10e-82df-ac26-6e1243c1bcf9"
            }
          ],
          "config": { "iiif_url": "https://www.artic.edu/iiif/2" }
        }
        """);
        Assert.Empty(WikimediaExpansionAcquirer.ParseArtInstituteChicagoCandidates(
            deityWithBellDocument.RootElement, plan, artifacts, bell));
    }

    [Fact]
    public void Cx507ArtInstituteChicagoSearchUsesStrictTitleAndPublicDomainQuery()
    {
        var payload = WikimediaExpansionAcquirer.BuildArtInstituteChicagoSearchPayload(
            new ExpansionQuery { ArtInstituteChicagoQuery = "rank badge" }, page: 0, pageSize: 20);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var must = root.GetProperty("query").GetProperty("bool").GetProperty("must");

        Assert.Equal("rank badge", must[0].GetProperty("match").GetProperty("title").GetProperty("query").GetString());
        Assert.Equal("and", must[0].GetProperty("match").GetProperty("title").GetProperty("operator").GetString());
        Assert.True(must[1].GetProperty("term").GetProperty("is_public_domain").GetBoolean());
        Assert.Equal(20, root.GetProperty("limit").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
    }

    [Fact]
    public void Cx507DownloadedImagesExposeActualPixelDimensions()
    {
        var onePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        var image = ReferenceImageInspector.Inspect(onePixelPng, "image/png");

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
    }

    [Fact]
    public void Cx507CategoryDiscoveryRemovesSearchBoilerplate()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Where(target => target.Dimension == "category").ToArray();

        Assert.Equal(
            "China bamboo grove",
            WikimediaExpansionAcquirer.BuildDiscoverySearch(
                targets.Single(target => target.Target.Id == "plants"),
                targets.Single(target => target.Target.Id == "plants").Target.Queries[0]));
        Assert.Equal(
            "Chinese robe",
            WikimediaExpansionAcquirer.BuildDiscoverySearch(
                targets.Single(target => target.Target.Id == "costumes_textiles"),
                targets.Single(target => target.Target.Id == "costumes_textiles").Target.Queries[0]));
        Assert.Equal(
            "ancient Chinese bronze ding",
            WikimediaExpansionAcquirer.BuildDiscoverySearch(
                targets.Single(target => target.Target.Id == "artifacts"),
                targets.Single(target => target.Target.Id == "artifacts").Target.Queries[0]));
    }

    [Fact]
    public void Cx507PeopleReferencesUsePracticalHighYieldPhotoSubjects()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var people = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.people");

        Assert.Equal(
            new[]
            {
                "male_portrait", "female_portrait", "street_people", "workers", "elderly",
                "martial_stance", "sitting", "group", "farmer", "artisan", "general_people",
            },
            people.Target.Queries.Select(query => query.Id).ToArray());
        Assert.Equal(
            new[]
            {
                "incategory:\"Men of China\"", "incategory:\"Women of China\"",
                "incategory:\"Street photography in China\"", "incategory:\"Workers in China\"",
                "incategory:\"Old people of China\"", "incategory:\"Martial artists from China\"",
                "incategory:\"People of China\" sitting", "incategory:\"Groups of people in China\"",
                "incategory:\"Farmers in China\"", "incategory:\"People of China\" artisan",
                "incategory:\"People of China\"",
            },
            people.Target.Queries.Select(query => query.Search).ToArray());
        Assert.All(people.Target.Queries.Where(query => query.Id is not ("female_portrait" or "general_people")),
            query => Assert.Equal(4, query.MinimumAssets));
        Assert.Equal(10, people.Target.Queries.Single(query => query.Id == "female_portrait").MinimumAssets);
        Assert.Equal(10, people.Target.Queries.Single(query => query.Id == "general_people").MinimumAssets);
        Assert.Equal(
            "incategory:\"Men of China\"",
            WikimediaExpansionAcquirer.BuildDiscoverySearch(people, people.Target.Queries[0]));
    }

    [Fact]
    public void Cx507UnderfilledPhenomenaUseVerifiedCommonsCategories()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Where(target => target.Dimension == "category")
            .ToDictionary(target => target.Target.Id, StringComparer.Ordinal);

        var astronomy = targets["astronomy"].Target.Queries.ToDictionary(query => query.Id, StringComparer.Ordinal);
        Assert.Equal("incategory:\"Full moon\"", astronomy["full_moon"].Search);
        Assert.Equal("incategory:\"Star trails\"", astronomy["star_trails"].Search);
        Assert.Equal("incategory:\"Solar eclipses\"", astronomy["solar_eclipse"].Search);
        Assert.Equal("incategory:\"Conjunctions (astronomy)\"", astronomy["planet_moon"].Search);

        var geology = targets["geology_caves"].Target.Queries.ToDictionary(query => query.Id, StringComparer.Ordinal);
        Assert.Equal("incategory:\"Karst caves in China\"", geology["karst_cave"].Search);
        Assert.Equal("incategory:\"Canyons\"", geology["canyon"].Search);
        Assert.Equal("incategory:\"Stalactites\"", geology["stalactite"].Search);
        Assert.Equal("incategory:\"Caves in China\"", geology["china_caves"].Search);
        Assert.Equal(9, geology["china_caves"].MinimumAssets);
        Assert.Equal("incategory:\"Rock formations in China\"", geology["china_rock_formations"].Search);
        Assert.Equal("incategory:\"Deserts of China\"", geology["china_deserts"].Search);
        Assert.Equal("incategory:\"Reed Flute Cave\"", geology["reed_flute_cave"].Search);
        Assert.Equal("incategory:\"Zhijin Cave\"", geology["zhijin_cave"].Search);
        Assert.Equal("incategory:\"Silver Cave\"", geology["silver_cave"].Search);
        Assert.Equal("incategory:\"Boyue Cave\"", geology["boyue_cave"].Search);

        var light = targets["light_fog_fire"].Target.Queries.ToDictionary(query => query.Id, StringComparer.Ordinal);
        Assert.Equal("incategory:\"Light beams\"", light["forest_sunbeam"].Search);
        Assert.Equal("incategory:\"Candles\"", light["candle"].Search);
        Assert.Equal("incategory:\"Sparks\"", light["fire_sparks"].Search);
        Assert.Equal("incategory:\"Crepuscular rays\"", light["god_rays"].Search);

        var weatherEvidence = plan.SubjectEvidenceTerms["weather"];
        Assert.All(new[] { "light", "fire", "flame", "smoke", "candle", "spark", "haze" },
            term => Assert.Contains(term, weatherEvidence));
    }

    [Fact]
    public void Cx507ExactCommonsCategoryQueriesUseCategoryMembersApi()
    {
        Assert.True(WikimediaExpansionAcquirer.TryGetExactWikimediaCategory(
            "incategory:\"Full moon\"", out var category));
        Assert.Equal("Full moon", category);
        Assert.False(WikimediaExpansionAcquirer.TryGetExactWikimediaCategory(
            "incategory:\"Night sky\" \"Milky Way\"", out _));

        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var parameters = WikimediaExpansionAcquirer.BuildWikimediaCategoryQueryParameters(
            plan, "Full moon", continuation: null);

        Assert.Equal("categorymembers", parameters["generator"]);
        Assert.Equal("Category:Full moon", parameters["gcmtitle"]);
        Assert.Equal("6", parameters["gcmnamespace"]);
        Assert.Equal("file", parameters["gcmtype"]);
        Assert.Equal(plan.PageSize.ToString(), parameters["gcmlimit"]);
        Assert.DoesNotContain("gsrsearch", parameters.Keys);
    }

    [Fact]
    public void Cx507ExactCommonsCategoriesPreferCommonsBeforeMuseumSearches()
    {
        var exact = new ExpansionQuery
        {
            Category = "costumes_textiles",
            Search = "incategory:\"Rank badges of the Ming Dynasty\"",
        };
        var broad = new ExpansionQuery
        {
            Category = "costumes_textiles",
            Search = "Ming dynasty Chinese rank badge museum",
        };

        Assert.Equal("wikimedia_commons", WikimediaExpansionAcquirer.PreferredSourcesForQuery(exact)[0]);
        Assert.Equal("art_institute_chicago", WikimediaExpansionAcquirer.PreferredSourcesForQuery(broad)[0]);
    }

    [Fact]
    public void Cx507EvidenceExclusionsAreCategoryAware()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));

        Assert.False(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "artifacts" },
            "ancient Chinese ceramic porcelain vase museum artifact"));
        Assert.False(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "costumes_textiles" },
            "traditional Chinese costume robe textile"));
        Assert.False(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "adult portrait with worried expression"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "Portrait of a young Chinese man, bronze sculpture in a museum"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "Chinese artisan shown in a nineteenth-century lithograph"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "1904 photograph of a group of Ainu people, PD China"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "Eastern Han dynasty tomb mural of women"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "Canton, China; a woman being beheaded. Photograph, 1927."));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "A Chinese woman suffering from a goitre, medical photograph"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "Bound feet; bound and unbound medical study"));
        Assert.False(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "people" },
            "documentary photograph of a Chinese worker"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "architecture" },
            "ancient Qin bronze horse and chariot museum artifact"));
        Assert.False(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "architecture" },
            "ancient stone city gate and defensive wall"));
        Assert.True(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "plants" },
            "museum fuchi carving with seven sages in a bamboo grove"));
        Assert.False(WikimediaExpansionAcquirer.HasExcludedEvidence(
            plan,
            new ExpansionQuery { Category = "plants" },
            "living bamboo grove in a mountain forest"));
    }

    [Fact]
    public void Cx507ChineseArtifactsAndCostumesRejectForeignLookalikes()
    {
        Assert.True(WikimediaExpansionAcquirer.HasRequiredCategoryGeographyEvidence(
            new ExpansionQuery { Category = "artifacts" },
            "Han dynasty Chinese bronze sword"));
        Assert.True(WikimediaExpansionAcquirer.HasRequiredCategoryGeographyEvidence(
            new ExpansionQuery { Category = "costumes_textiles" },
            "court robe, China, Qing dynasty"));
        Assert.False(WikimediaExpansionAcquirer.HasRequiredCategoryGeographyEvidence(
            new ExpansionQuery { Category = "artifacts" },
            "Ancient Greece Bronze Age Bronze Swords"));
        Assert.True(WikimediaExpansionAcquirer.HasRequiredCategoryGeographyEvidence(
            new ExpansionQuery { Category = "people" },
            "Documentary photograph of a Chinese woman in Beijing"));
        Assert.False(WikimediaExpansionAcquirer.HasRequiredCategoryGeographyEvidence(
            new ExpansionQuery { Category = "people" },
            "Ainu group photograph from Hokkaido, Japan"));
    }

    [Fact]
    public void Cx507MetMuseumDiscoveryUsesPoliteSequentialBatches()
    {
        Assert.InRange(WikimediaExpansionAcquirer.MetMuseumObjectPageSize, 1, 8);
        Assert.Equal(1, WikimediaExpansionAcquirer.MetMuseumMaxConcurrentObjectRequests);
        Assert.InRange(WikimediaExpansionAcquirer.CandidateDownloadTimeoutSeconds, 30, 60);
        Assert.InRange(WikimediaExpansionAcquirer.MaxConcurrentDiversityQueries, 1, 2);
        Assert.InRange(
            WikimediaExpansionAcquirer.SourceSearchTimeoutSeconds("wikimedia_commons"),
            45,
            90);
        Assert.Equal(90, WikimediaExpansionAcquirer.CandidateDownloadTimeoutSecondsForCategory("people"));
        Assert.Equal(
            WikimediaExpansionAcquirer.CandidateDownloadTimeoutSeconds,
            WikimediaExpansionAcquirer.CandidateDownloadTimeoutSecondsForCategory("artifacts"));
    }

    [Fact]
    public void Cx507ProvinceFallbackUsesOnlyNamedSiteQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var province = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .First(target => target.Dimension == "province");

        var queries = WikimediaExpansionAcquirer.ProvinceFallbackQueries(province);

        Assert.NotEmpty(queries);
        Assert.All(queries, query => Assert.False(string.IsNullOrWhiteSpace(query.Id)));
    }

    [Fact]
    public void Cx507NonProvinceTargetsPreferNamedLandmarkQueriesWhenAvailable()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var kaifeng = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "capital.kaifeng");
        var highMountain = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "landform.high_mountain");

        var namedQueries = WikimediaExpansionAcquirer.FallbackQueries(kaifeng);
        var genericQueries = WikimediaExpansionAcquirer.FallbackQueries(highMountain);

        Assert.Equal(4, namedQueries.Count);
        Assert.All(namedQueries, query => Assert.False(string.IsNullOrWhiteSpace(query.Id)));
        Assert.Equal(highMountain.Target.Queries.Count, genericQueries.Count);
    }

    [Fact]
    public void Cx507DiversityDiscoveryCapsCandidateAttemptsPerNamedQuery()
    {
        Assert.InRange(WikimediaExpansionAcquirer.MaxDiversityCandidateAttemptsPerQuery, 1, 4);
    }

    [Fact]
    public void Cx507NonCoreDiversityQueriesCanAcquireTheirFourAssetQuota()
    {
        Assert.Equal(4, WikimediaExpansionAcquirer.DiversityResultLimit(remainingForQuery: 4));
        Assert.Equal(4, WikimediaExpansionAcquirer.DiversityCandidateAttemptBudget("architecture", remainingForQuery: 4));
        Assert.InRange(
            WikimediaExpansionAcquirer.DiversityCandidateAttemptBudget("plants", remainingForQuery: 4),
            8,
            16);
    }

    [Fact]
    public void Cx507QueryEvidencePreventsCrossSubjectReuseAndSkipsCoveredCandidates()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var animals = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.animals");
        var crane = animals.Target.Queries.Single(query => query.Id == "crane");
        var deer = animals.Target.Queries.Single(query => query.Id == "deer");
        const string sourcePage = "https://commons.wikimedia.org/wiki/File:crane.jpg";
        const string download = "https://upload.wikimedia.org/crane.jpg";
        var assets = new[]
        {
            new ReferenceAsset
            {
                SourcePageUrl = sourcePage,
                DownloadUrl = download,
                CoverageTargetIds = ["category.animals", "query.category_animals_crane"],
            },
        };

        Assert.True(WikimediaExpansionAcquirer.MatchesQueryEvidence(
            crane, "Red-crowned crane, Grus japonica, in wetland"));
        Assert.False(WikimediaExpansionAcquirer.MatchesQueryEvidence(
            deer, "Red-crowned crane, Grus japonica, in wetland"));
        Assert.True(WikimediaExpansionAcquirer.CandidateAlreadyCovered(
            assets, animals, crane, sourcePage, download));
        Assert.False(WikimediaExpansionAcquirer.CandidateAlreadyCovered(
            assets, animals, deer, sourcePage, download));
    }

    [Fact]
    public void Cx507CandidateCacheKeysIncludeStableCandidateIdentity()
    {
        var firstINaturalist = WikimediaExpansionAcquirer.CandidateToken("inaturalist", "inat-633028127");
        var secondINaturalist = WikimediaExpansionAcquirer.CandidateToken("inaturalist", "inat-633028128");
        var firstCommons = WikimediaExpansionAcquirer.CandidateToken("wikimedia_commons", "12345");
        var secondCommons = WikimediaExpansionAcquirer.CandidateToken("wikimedia_commons", "67890");

        Assert.NotEqual(firstINaturalist, secondINaturalist);
        Assert.NotEqual(firstCommons, secondCommons);
        Assert.Equal(firstINaturalist, WikimediaExpansionAcquirer.CandidateToken("inaturalist", "inat-633028127"));
        Assert.Matches("^[a-z0-9]+-[a-f0-9]{12}$", firstINaturalist);
    }

    [Fact]
    public void Cx507AcquisitionPrioritizesUnderrepresentedCategoryTargets()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));

        var ordered = WikimediaExpansionAcquirer.OrderTargetsForAcquisition(plan).ToArray();

        Assert.Equal(10, ordered.TakeWhile(target => target.Dimension == "category").Count());
        Assert.Equal(
            new[]
            {
                "category.plants", "category.animals", "category.waters", "category.weather",
                "category.astronomy", "category.people", "category.costumes_textiles",
                "category.artifacts", "category.geology_caves", "category.light_fog_fire",
            },
            ordered.Take(10).Select(target => target.TargetId).ToArray());
        Assert.All(ordered.Take(10), target =>
            Assert.False(WikimediaExpansionAcquirer.ShouldPersistDiversityAttemptMarker(target)));
        Assert.True(WikimediaExpansionAcquirer.ShouldPersistDiversityAttemptMarker(
            ordered.First(target => target.Dimension == "province")));
    }

    [Fact]
    public void Cx507FocusedAcquisitionRunsOnlyRequestedCategories()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));

        var focused = WikimediaExpansionAcquirer.OrderTargetsForAcquisition(
            plan,
            new HashSet<string>(["artifacts", "costumes_textiles"], StringComparer.Ordinal));

        Assert.Equal(
            new[] { "category.costumes_textiles", "category.artifacts" },
            focused.Select(target => target.TargetId).ToArray());
    }

    [Fact]
    public void Cx507FocusedAcquisitionRunsOnlyRequestedQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var astronomy = WikimediaExpansionAcquirer.OrderTargetsForAcquisition(
                plan,
                new HashSet<string>(["astronomy"], StringComparer.Ordinal))
            .Single();

        var focused = WikimediaExpansionAcquirer.QueriesForAcquisition(
            astronomy,
            new HashSet<string>(["full_moon"], StringComparer.Ordinal));

        var query = Assert.Single(focused);
        Assert.Equal("full_moon", query.Id);
    }

    [Fact]
    public void Cx507FocusedAcquisitionRunsOnlyRequestedTargets()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));

        var focused = WikimediaExpansionAcquirer.OrderTargetsForAcquisition(
            plan,
            focusedTargetIds: new HashSet<string>(
                ["province.tianjin", "landform.volcano"],
                StringComparer.Ordinal));

        Assert.Equal(
            new[] { "province.tianjin", "landform.volcano" },
            focused.Select(target => target.TargetId).ToArray());
    }

    [Fact]
    public void Cx507FocusedTargetQueriesCanSelectRecoveryQueryWithoutCategoryFilter()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var tianjin = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "province.tianjin");

        var focused = WikimediaExpansionAcquirer.QueriesForAcquisition(
            tianjin,
            new HashSet<string>(["recovery6_tianjin_unrestricted_architecture"], StringComparer.Ordinal));

        var query = Assert.Single(focused);
        Assert.Equal("recovery6_tianjin_unrestricted_architecture", query.Id);
    }

    [Fact]
    public void Cx507UnverifiedPolicyAcceptsRestrictedOpenverseLicenseAsReferenceOnly()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var garden = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "garden.lingnan_garden");
        var query = garden.Target.Queries
            .Single(item => item.Id == "recovery6_lingnan_unrestricted_garden");
        using var document = JsonDocument.Parse("""
        {
          "id": "openverse-lingnan-restricted",
          "title": "Lingnan garden pavilion in China",
          "creator": "Reference photographer",
          "license": "by-nc-sa",
          "license_version": "4.0",
          "width": 1024,
          "height": 768,
          "foreign_landing_url": "https://example.test/lingnan-garden",
          "url": "https://example.test/lingnan-garden.jpg",
          "tags": [{"name": "Lingnan garden"}, {"name": "China"}],
          "provider": "example",
          "source": "example"
        }
        """);

        Assert.True(WikimediaExpansionAcquirer.TryParseOpenverseCandidate(
            document.RootElement, plan, garden, query, allowShortfallException: true, out var candidate));
        Assert.NotNull(candidate);
        Assert.Equal("unverified", candidate!.License);
        Assert.Equal("unverified", candidate.LicenseVersion);
        Assert.Null(candidate.LicenseUrl);
    }

    [Fact]
    public void Cx507UnverifiedPolicyMapsRestrictedWikimediaLicenseToUnverified()
    {
        Assert.True(WikimediaExpansionAcquirer.TryNormalizeLicense(
            "CC BY-NC-SA 4.0",
            allowUnverified: true,
            out var license,
            out var version,
            out var licenseUrl));
        Assert.Equal("unverified", license);
        Assert.Equal("unverified", version);
        Assert.Null(licenseUrl);
        Assert.False(WikimediaExpansionAcquirer.TryNormalizeLicense(
            "CC BY-NC-SA 4.0",
            allowUnverified: false,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void Cx507Recovery6QueriesUseBroadSearchFallbacks()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recovery6_heilongjiang_unrestricted_architecture"] = "Heilongjiang architecture",
            ["recovery6_heilongjiang_old_city"] = "Harbin historic building",
            ["recovery6_chongqing_unrestricted_architecture"] = "Chongqing ancient town",
            ["recovery6_chongqing_riverside_building"] = "Chongqing historic building",
            ["recovery6_xianyang_unrestricted_ruins"] = "Xianyang Qin ruins",
            ["recovery6_xianyang_archaeological_site"] = "Xianyang archaeological site",
            ["recovery6_linzi_unrestricted_ruins"] = "Linzi ruins",
            ["recovery6_linzi_city_walls"] = "Qi State ruins",
            ["recovery6_lingnan_unrestricted_garden"] = "Lingnan garden",
            ["recovery6_lingnan_water_garden"] = "Guangzhou garden",
            ["recovery6_temple_unrestricted_garden"] = "Chinese Buddhist temple garden",
            ["recovery6_temple_pond_garden"] = "China temple garden pond",
            ["recovery6_coast_unrestricted_seascape"] = "China coast",
            ["recovery6_coast_island"] = "Chinese coastal island",
            ["recovery6_volcano_unrestricted_landscape"] = "China volcano",
            ["recovery6_volcano_lava_lake"] = "China volcanic landscape",
        };

        var actual = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .SelectMany(target => target.Target.Queries)
            .Where(query => expected.ContainsKey(query.Id))
            .ToDictionary(query => query.Id, query => query.Search, StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Cx507LinziRecoveryIncludesShandongRegionalArchitectureFallbacks()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var linzi = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "capital.linzi_qi");

        Assert.Contains("Shandong", linzi.Target.EvidenceTerms);
        Assert.Contains(linzi.Target.Queries, query =>
            query.Id == "recovery7_linzi_shandong_temple" &&
            query.Search == "Shandong temple architecture");
        Assert.Contains(linzi.Target.Queries, query =>
            query.Id == "recovery7_linzi_qufu_temple" &&
            query.Search == "Qufu Confucius temple architecture");
        Assert.Contains(linzi.Target.Queries, query =>
            query.Id == "recovery7_linzi_shandong_city_wall" &&
            query.Search == "Shandong historic city wall");
    }

    [Fact]
    public void Cx507FocusedQueryAcquisitionBypassesSerialDiversityDownloads()
    {
        Assert.True(WikimediaExpansionAcquirer.ShouldUseDiversityAcquisition(null));
        Assert.False(WikimediaExpansionAcquirer.ShouldUseDiversityAcquisition(
            new HashSet<string>(["full_moon"], StringComparer.Ordinal)));
    }

    [Fact]
    public void Cx507CategoryTargetsCountOnlyAssetsOwnedByThatCategory()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var geology = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .Single(target => target.TargetId == "category.geology_caves");
        var crossTagged = Enumerable.Range(1, geology.Target.MinimumAssets)
            .Select(index => new ReferenceAsset
            {
                Id = $"landscape-{index}",
                Category = "landscape",
                CoverageTargetIds = [geology.TargetId],
            })
            .ToArray();
        var owned = crossTagged.Select(asset => asset with { Category = "geology_caves" }).ToArray();

        Assert.False(WikimediaExpansionAcquirer.TargetSatisfied(crossTagged, geology));
        Assert.True(WikimediaExpansionAcquirer.TargetSatisfied(owned, geology));
    }

    [Fact]
    public void Cx507CostumeAcquisitionRejectsQingPeriodEvidence()
    {
        var query = new ExpansionQuery { Category = "costumes_textiles" };

        Assert.True(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            query, "China, Qing dynasty, late 18th century court robe"));
        Assert.True(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            query, "清早期 彩绒龙袍料"));
        Assert.True(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            query, "Chinese woman's garment, 19th century"));
        Assert.True(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            query, "Chinese robe dated 1768"));
        Assert.False(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            query, "China, Ming dynasty, Wanli period court robe"));
        Assert.False(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            query, "Modern 2025 reconstruction based on Tang dynasty ruqun"));
        Assert.False(WikimediaExpansionAcquirer.IsQingCostumeEvidence(
            new ExpansionQuery { Category = "artifacts" },
            "Qing dynasty bronze vessel"));
    }

    [Fact]
    public void Cx507CostumeCurationQuarantinesOnlyReviewedQingAssets()
    {
        var qing = new ReferenceAsset
        {
            Id = "costume-qing",
            Category = "costumes_textiles",
            LocalRelativePath = "content/reference-library/raw/costumes_textiles/qing.jpg",
        };
        var ming = new ReferenceAsset
        {
            Id = "costume-ming",
            Category = "costumes_textiles",
            LocalRelativePath = "content/reference-library/raw/costumes_textiles/ming.jpg",
        };
        var catalog = new ReferenceCatalog
        {
            LibraryId = "cx507-test",
            Assets = [qing, ming],
        };
        var decision = new CostumeCurationDecision
        {
            SchemaVersion = "1.0.0",
            CatalogLibraryId = catalog.LibraryId,
            ExpectedCostumeAssets = 2,
            CostumeOrderSha256 = CostumeCuration.ComputeOrderSha256(catalog.Assets),
            QuarantinedAssets =
            [
                new CostumeQuarantineDecision
                {
                    AssetId = qing.Id,
                    Reason = "Official museum metadata identifies Qing dynasty (1644–1911).",
                    EvidenceSourceUrl = "https://example.test/museum/qing",
                },
            ],
        };

        var result = CostumeCuration.Apply(catalog, decision);

        Assert.Equal(new[] { ming.Id }, result.Catalog.Assets.Select(asset => asset.Id));
        Assert.Equal(new[] { qing.Id }, result.QuarantinedAssets.Select(asset => asset.Id));
    }

    [Fact]
    public void Cx507ExistingNonCoreAssetsCountTowardCategoryTargets()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var catalog = CatalogFile.Load(ProjectPath("content/reference-library/catalog.json"));

        var tagged = WikimediaExpansionAcquirer.TagExistingCategoryCoverage(plan, catalog.Assets);

        Assert.True(tagged.Count(asset =>
            asset.Category == "plants" &&
            asset.CoverageTargetIds.Contains("category.plants", StringComparer.Ordinal)) >= 40);
        Assert.True(tagged.Count(asset =>
            asset.Category == "weather" &&
            asset.CoverageTargetIds.Contains("category.weather", StringComparer.Ordinal)) >= 40);
        Assert.DoesNotContain(tagged.Where(asset => asset.Category == "architecture"), asset =>
            asset.CoverageTargetIds.Contains("category.architecture", StringComparer.Ordinal));
    }

    [Fact]
    public void Cx507PrivateResearchRejectsStockLowResolutionAndUnsafeCandidates()
    {
        const string json = """
        {
          "results": [
            {
              "title": "Humble Administrator's Garden - official culture portal",
              "image": "https://images.example.gov.cn/zhuozhengyuan.jpg",
              "url": "https://example.gov.cn/heritage/zhuozhengyuan",
              "width": 2400,
              "height": 1350
            },
            {
              "title": "Premium stock photo",
              "image": "https://img.freepik.com/garden.jpg",
              "url": "https://freepik.com/premium/garden",
              "width": 3000,
              "height": 2000
            },
            {
              "title": "Small tourism thumbnail",
              "image": "https://travel.example.com/garden.jpg",
              "url": "https://travel.example.com/garden",
              "width": 800,
              "height": 600
            },
            {
              "title": "Unsafe candidate",
              "image": "file:///c:/garden.jpg",
              "url": "https://example.com/garden",
              "width": 2400,
              "height": 1350
            }
          ]
        }
        """;

        var candidates = DuckDuckGoPrivateResearchAcquirer.ParseCandidates(json, 1600, 900);

        var candidate = Assert.Single(candidates);
        Assert.Equal("https://example.gov.cn/heritage/zhuozhengyuan", candidate.SourcePageUrl.AbsoluteUri);
    }

    [Fact]
    public void Cx507PrivateResearchRequiresNamedLandmarkEvidence()
    {
        var relevant = new PrivateImageCandidate(
            "Huangyaguan Great Wall in Tianjin",
            new Uri("https://images.example.com/huangyaguan.jpg"),
            new Uri("https://example.com/huangyaguan-great-wall"),
            2400,
            1350);
        var unrelated = new PrivateImageCandidate(
            "China marriage rates hit record lows",
            new Uri("https://images.example.com/header.jpg"),
            new Uri("https://example.com/population-news"),
            2400,
            1350);

        Assert.True(DuckDuckGoPrivateResearchAcquirer.MatchesNamedLandmark(
            relevant,
            "Huangyaguan Great Wall Tianjin"));
        Assert.False(DuckDuckGoPrivateResearchAcquirer.MatchesNamedLandmark(
            unrelated,
            "Huangyaguan Great Wall Tianjin"));
    }

    [Fact]
    public void Cx507PrivateResearchManifestIsPersonalOnlyAndNeverShips()
    {
        var manifest = PrivateResearchManifest.Empty("architecture");

        Assert.True(manifest.PersonalUseOnly);
        Assert.True(manifest.ReferenceOnly);
        Assert.False(manifest.ShipInBuild);
        Assert.Empty(manifest.Assets);
    }

    [Fact]
    public void Cx507PrivateArchitectureSearchRequiresRealEmptyPeopleFreeReferences()
    {
        var query = DuckDuckGoPrivateResearchAcquirer.BuildSearchQuery(
            "Humble Administrators Garden Suzhou",
            "architecture");

        Assert.Contains("Humble Administrators Garden Suzhou", query, StringComparison.Ordinal);
        Assert.Contains("real photograph", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no people", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-stock", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cx507PrivateResearchOutputCannotEscapeIgnoredPrivateRoot()
    {
        var repositoryRoot = ReferenceLibraryProcessHarness.RepositoryRoot.FullName;
        var privateRoot = ProjectPath("content/reference-library/private-research/architecture");
        var publicRawRoot = ProjectPath("content/reference-library/raw");

        Assert.Equal(Path.GetFullPath(privateRoot),
            DuckDuckGoPrivateResearchAcquirer.ValidatePrivateOutputRoot(repositoryRoot, privateRoot));
        Assert.Throws<InvalidDataException>(() =>
            DuckDuckGoPrivateResearchAcquirer.ValidatePrivateOutputRoot(repositoryRoot, publicRawRoot));
    }

    [Fact]
    public void Cx507PrivateArchitectureQueriesCoverAllProvinceRegions()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));

        var queries = DuckDuckGoPrivateResearchAcquirer.BuildQueries(plan, "architecture");
        var provinceIds = queries
            .Where(query => query.Dimension == "province")
            .Select(query => query.TargetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(34, provinceIds.Length);
        Assert.All(queries, query => Assert.Equal("architecture", query.Category));
        Assert.All(queries, query => Assert.False(string.IsNullOrWhiteSpace(query.Search)));
    }

    [Fact]
    public async Task Cx507ReferenceToolAdvertisesPrivateResearchAcquisitionCommand()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("private-expand", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("personal", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cx507ArchitectureCurationDecisionCoversTheReviewedCatalogSnapshot()
    {
        using var document = LoadJson("content/reference-library/architecture-curation-decisions.cx507.final.json");
        var root = document.RootElement;
        var confirmedIndices = root.GetProperty("confirmedAbsentIndices")
            .EnumerateArray().Select(item => item.GetInt32()).ToArray();

        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(210, root.GetProperty("expectedArchitectureAssets").GetInt32());
        Assert.Equal(14, root.GetProperty("reviewedContactSheets").GetArrayLength());
        Assert.Equal(160, confirmedIndices.Length);
        Assert.Equal(confirmedIndices.Length, confirmedIndices.Distinct().Count());
        Assert.All(confirmedIndices, index => Assert.InRange(index, 1, 210));
        Assert.Contains(208, confirmedIndices);
        Assert.DoesNotContain(128, confirmedIndices);
        Assert.DoesNotContain(210, confirmedIndices);
    }

    [Fact]
    public void Cx507ArchitectureCurationProducesOnlyReviewedPeopleFreeArchitecture()
    {
        var catalog = CatalogFile.Load(ProjectPath("content/reference-library/catalog.json"));
        var architecture = catalog.Assets.Where(asset => asset.Category == "architecture").ToArray();
        Assert.True(architecture.Length >= 160);
        Assert.Equal(160, architecture.Count(asset => asset.Taxonomy.PeoplePresence == "confirmed_absent"));
        Assert.All(architecture, asset =>
            Assert.Contains(asset.Taxonomy.PeoplePresence, ["confirmed_absent", "unknown_needs_review"]));
        Assert.All(catalog.Assets.Where(asset => asset.Category != "architecture"), asset =>
            Assert.Equal("not_applicable", asset.Taxonomy.PeoplePresence));
    }

    [Fact]
    public void Cx507FailedCoverageTargetsUseExactCommonsRecoveryQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ExactCommonsRecoveryTargets, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                WikimediaExpansionAcquirer.TryGetExactWikimediaCategory(query.Search, out _));
        });
    }

    [Fact]
    public void Cx507ShortfallTargetsHaveStableNamedRecoveryQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ShortfallTargetsAfterCx507Expansion, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                !string.IsNullOrWhiteSpace(query.Id) &&
                query.Id.StartsWith("recovery_", StringComparison.Ordinal) &&
                !query.MinimumAssets.HasValue);
        });
    }

    [Fact]
    public void Cx507SecondRecoveryTargetsHaveStableNamedQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ShortfallTargetsAfterFirstRecovery, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                !string.IsNullOrWhiteSpace(query.Id) &&
                query.Id.StartsWith("recovery2_", StringComparison.Ordinal) &&
                !query.MinimumAssets.HasValue);
        });
    }

    [Fact]
    public void Cx507ThirdRecoveryTargetsHaveStableNamedQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ShortfallTargetsAfterSecondRecovery, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                !string.IsNullOrWhiteSpace(query.Id) &&
                query.Id.StartsWith("recovery3_", StringComparison.Ordinal) &&
                !query.MinimumAssets.HasValue);
        });
    }

    [Fact]
    public void Cx507FourthRecoveryTargetsHaveStableNamedQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ShortfallTargetsAfterThirdRecovery, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                !string.IsNullOrWhiteSpace(query.Id) &&
                query.Id.StartsWith("recovery4_", StringComparison.Ordinal) &&
                !query.MinimumAssets.HasValue);
        });
    }

    [Fact]
    public void Cx507FifthRecoveryTargetsHaveStableNamedQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ShortfallTargetsAfterFourthRecovery, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                !string.IsNullOrWhiteSpace(query.Id) &&
                query.Id.StartsWith("recovery5_", StringComparison.Ordinal) &&
                !query.MinimumAssets.HasValue);
        });
    }

    [Fact]
    public void Cx507ShortfallExceptionRecoveryTargetsHaveStableNamedQueries()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var targets = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);

        Assert.All(ShortfallTargetsAfterFifthRecovery, targetId =>
        {
            Assert.True(targets.TryGetValue(targetId, out var descriptor), $"Missing target {targetId}.");
            Assert.Contains(descriptor!.Target.Queries, query =>
                !string.IsNullOrWhiteSpace(query.Id) &&
                query.Id.StartsWith("recovery6_", StringComparison.Ordinal) &&
                !query.MinimumAssets.HasValue);
        });
    }

    [Fact]
    public void Cx507GeneratedAssetIdentifiersMatchCatalogSchemaPatterns()
    {
        using var document = LoadJson("server/Contracts/schemas/reference-library-catalog.schema.json");
        var properties = document.RootElement.GetProperty("$defs").GetProperty("asset").GetProperty("properties");
        var idPattern = properties.GetProperty("id").GetProperty("pattern").GetString()!;
        var pathPattern = properties.GetProperty("localRelativePath").GetProperty("pattern").GetString()!;
        var token = WikimediaExpansionAcquirer.CandidateToken("wikimedia_commons", "12345");
        var id = $"ref-architecture-238-{token}";

        Assert.Matches(idPattern, id);
        Assert.Matches(pathPattern, $"content/reference-library/raw/architecture/{id}.jpg");
    }

    [Fact]
    public void Cx507ArchitectureCurationRejectsCatalogOrderDrift()
    {
        var catalog = CatalogFile.Load(ProjectPath("content/reference-library/catalog.json"));
        var architecture = catalog.Assets.Where(asset => asset.Category == "architecture").ToArray();
        var decision = new ArchitectureCurationDecision
        {
            SchemaVersion = "1.0.0",
            CatalogLibraryId = catalog.LibraryId,
            QuarantineUnlisted = true,
            ExpectedArchitectureAssets = architecture.Length,
            ArchitectureOrderSha256 = ArchitectureCuration.ComputeOrderSha256(architecture),
            ConfirmedAbsentIndices = Enumerable.Range(1, architecture.Length).ToArray(),
        };
        var changed = catalog with { Assets = catalog.Assets.Reverse().ToArray() };

        var exception = Assert.Throws<InvalidDataException>(() => ArchitectureCuration.Apply(changed, decision));

        Assert.Contains("fingerprint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cx507PlanCoversAllThirtyFourProvinceRegionsWithBalancedQuotas()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var regions = document.RootElement.GetProperty("coverage").GetProperty("provinceRegions")
            .EnumerateArray().ToArray();

        Assert.Equal(RequiredProvinceRegions, regions.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(regions, region =>
        {
            Assert.True(region.GetProperty("minimumAssets").GetInt32() >= 8);
            Assert.True(region.GetProperty("minimumArchitectureAssets").GetInt32() >= 4);
            Assert.True(region.GetProperty("minimumNaturalAssets").GetInt32() >= 4);
            Assert.True(region.GetProperty("queries").GetArrayLength() >= 4);
            Assert.NotEmpty(region.GetProperty("evidenceTerms").EnumerateArray());
        });
    }

    [Fact]
    public void Cx507PlanDefinesHistoricalGardenLandformAndWeatherQuotas()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var coverage = document.RootElement.GetProperty("coverage");
        var capitals = coverage.GetProperty("historicalCapitals").EnumerateArray().ToArray();
        var periods = coverage.GetProperty("historicalPeriods").EnumerateArray().ToArray();
        var gardens = coverage.GetProperty("gardenTypes").EnumerateArray().ToArray();
        var landforms = coverage.GetProperty("landforms").EnumerateArray().ToArray();
        var phenomena = coverage.GetProperty("weatherPhenomena").EnumerateArray().ToArray();

        Assert.True(capitals.Length >= 14);
        Assert.All(capitals, item => Assert.True(item.GetProperty("minimumAssets").GetInt32() >= 10));
        Assert.Equal(
            ["pre_qin", "qin_han", "wei_jin_northern_southern", "sui_tang", "five_dynasties_song",
             "liao_jin_xixia", "yuan", "ming", "qing"],
            periods.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(periods, item => Assert.True(item.GetProperty("minimumAssets").GetInt32() >= 10));
        Assert.True(gardens.Length >= 6);
        Assert.All(gardens, item => Assert.True(item.GetProperty("minimumAssets").GetInt32() >= 12));
        Assert.True(landforms.Length >= 16);
        Assert.All(landforms, item => Assert.True(item.GetProperty("minimumAssets").GetInt32() >= 8));
        Assert.True(phenomena.Length >= 25);
        Assert.All(phenomena, item => Assert.True(item.GetProperty("minimumAssets").GetInt32() >= 4));

        var requiredPhenomena = new[]
        {
            "wind", "rain", "thunderstorm", "lightning", "hail", "snow", "fog", "rime",
            "cloud_sea", "rainbow", "sandstorm", "typhoon",
        };
        var phenomenonIds = phenomena.Select(item => item.GetProperty("id").GetString()).ToHashSet();
        Assert.All(requiredPhenomena, id => Assert.Contains(id, phenomenonIds));

        var plannedMinimum = coverage.EnumerateObject()
            .SelectMany(property => property.Value.EnumerateArray())
            .Sum(item => item.GetProperty("minimumAssets").GetInt32());
        Assert.True(plannedMinimum >= 800, $"Coverage targets only plan {plannedMinimum} assets.");
    }

    [Fact]
    public void Cx507PlanDefinesDiverseQueriesForEveryNonCoreCategory()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var targets = document.RootElement.GetProperty("coverage").GetProperty("categorySubjects")
            .EnumerateArray().ToArray();
        var expected = new[]
        {
            "plants", "animals", "waters", "weather", "astronomy", "people",
            "costumes_textiles", "artifacts", "geology_caves", "light_fog_fire",
        };

        Assert.Equal(expected, targets.Select(target => target.GetProperty("id").GetString()).ToArray());
        Assert.All(targets, target =>
        {
            Assert.True(target.GetProperty("minimumAssets").GetInt32() >= 40);
            var queries = target.GetProperty("queries").EnumerateArray().ToArray();
            Assert.True(queries.Length >= 10);
            Assert.All(queries, query =>
            {
                Assert.Equal(target.GetProperty("id").GetString(), query.GetProperty("category").GetString());
                Assert.False(string.IsNullOrWhiteSpace(query.GetProperty("id").GetString()));
                Assert.NotEmpty(query.GetProperty("evidenceTerms").EnumerateArray());
            });
        });
    }

    [Fact]
    public void Cx507PlanDeclaresScopedShortfallSourceAndResolutionException()
    {
        using var document = LoadJson("content/reference-library/china-expansion-plan.cx507.json");
        var exception = document.RootElement.GetProperty("shortfallException");

        Assert.True(exception.GetProperty("allowUnverifiedSource").GetBoolean());
        Assert.True(exception.GetProperty("allowReducedResolution").GetBoolean());
        Assert.True(exception.GetProperty("requiresManualReview").GetBoolean());
        Assert.Equal(640, exception.GetProperty("minimumWidth").GetInt32());
        Assert.Equal(360, exception.GetProperty("minimumHeight").GetInt32());
        Assert.False(exception.GetProperty("shipInBuild").GetBoolean());
    }

    [Fact]
    public void Cx507ShortfallExceptionDimensionsAreLowerButBounded()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var descriptor = ChinaExpansionPlanValidator.EnumerateTargets(plan)
            .First(target => target.TargetId == "province.tianjin");

        Assert.False(WikimediaExpansionAcquirer.MeetsMinimumDimensions(
            plan, descriptor, 639, 360));
        Assert.True(WikimediaExpansionAcquirer.MeetsMinimumDimensions(
            plan, descriptor, 640, 360));
        Assert.True(WikimediaExpansionAcquirer.MeetsMinimumDimensions(
            plan, descriptor, 1600, 900));
    }

    [Fact]
    public async Task Cx507PlanPassesStrictValidatePlanCommand()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "validate-plan",
            "--plan", ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("plan valid", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("34", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("800", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Cx507CoverageEvaluatorReportsUnderfilledCategories()
    {
        var plan = CatalogFile.LoadExpansionPlan(
            ProjectPath("content/reference-library/china-expansion-plan.cx507.json"));
        var catalog = CatalogFile.Load(ProjectPath("content/reference-library/catalog.json"));
        var underfilled = catalog with
        {
            Assets = catalog.Assets.Where(asset => asset.Category != "plants").ToArray(),
        };

        var report = ExpansionCoverageEvaluator.Evaluate(underfilled, plan);

        Assert.Contains("plants", report.FailedCategoryIds);
        Assert.Equal(12, report.Categories.Count);
        Assert.False(report.Categories.Single(category => category.CategoryId == "plants").Passes);
    }

    [Fact]
    public async Task Cx507CoverageCommandWritesProgressReportBeforeEightHundredAssets()
    {
        var temporaryDirectory = ReferenceLibraryProcessHarness.CreateTemporaryDirectory();
        try
        {
            var reportPath = Path.Combine(temporaryDirectory, "coverage-progress.json");
            var catalogPath = Path.Combine(temporaryDirectory, "catalog-progress.json");
            var catalog = CatalogFile.Load(ProjectPath("content/reference-library/catalog.json"));
            CatalogFile.WriteAtomic(catalogPath, catalog with
            {
                Assets = catalog.Assets.Take(100).ToArray(),
            });
            var result = await ReferenceLibraryProcessHarness.RunToolAsync(
                "coverage",
                "--catalog", catalogPath,
                "--plan", ProjectPath("content/reference-library/china-expansion-plan.cx507.json"),
                "--report", reportPath);

            Assert.Equal(2, result.ExitCode);
            Assert.True(File.Exists(reportPath));
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Contains("plants", report.RootElement.GetProperty("failedCategoryIds")
                .EnumerateArray().Select(item => item.GetString()));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void Cx507CatalogSchemaRequiresCoverageTaxonomyAndReviewState()
    {
        using var document = LoadJson("server/Contracts/schemas/reference-library-catalog.schema.json");
        var root = document.RootElement;
        Assert.Equal("CX-507 China visual reference atlas", root.GetProperty("title").GetString());
        Assert.Equal(800, root.GetProperty("properties").GetProperty("assets").GetProperty("minItems").GetInt32());

        var required = root.GetProperty("$defs").GetProperty("asset").GetProperty("required")
            .EnumerateArray().Select(item => item.GetString()).ToHashSet();
        Assert.Contains("curationStatus", required);
        Assert.Contains("coverageTargetIds", required);
        Assert.Contains("taxonomy", required);
    }

    [Fact]
    public void CheckedInCatalogHasEightHundredUniqueStrictlyLicensedTaxonomyAssets()
    {
        using var document = LoadJson("content/reference-library/catalog.json");
        var root = document.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();

        Assert.Equal("2.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("cx507-china-visual-atlas-v1", root.GetProperty("coveragePlanId").GetString());
        Assert.True(assets.Length >= 800, $"Expected at least 800 assets, found {assets.Length}.");
        Assert.Equal(assets.Length, assets.Select(item => item.GetProperty("sourcePageUrl").GetString()).Distinct().Count());
        Assert.Equal(assets.Length, assets.Select(item => item.GetProperty("sha256").GetString()).Distinct().Count());

        Assert.All(assets, asset =>
        {
            var license = asset.GetProperty("license").GetString();
            if (license == "unverified")
            {
                Assert.Equal("unverified", asset.GetProperty("sourceVerification").GetString());
                Assert.Equal("unverified", asset.GetProperty("licenseVersion").GetString());
                Assert.False(asset.GetProperty("commercialUseAllowed").GetBoolean());
                Assert.False(asset.GetProperty("modificationsAllowed").GetBoolean());
                Assert.Equal(JsonValueKind.Null, asset.GetProperty("licenseUrl").ValueKind);
            }
            else
            {
                Assert.Contains(license, new[] { "cc0", "pdm", "by" });
            }
            Assert.False(asset.GetProperty("shareAlikeRequired").GetBoolean());
            Assert.Equal("needs_review", asset.GetProperty("curationStatus").GetString());
            Assert.NotEmpty(asset.GetProperty("coverageTargetIds").EnumerateArray());
            var taxonomy = asset.GetProperty("taxonomy");
            Assert.True(taxonomy.TryGetProperty("authenticity", out _));
            Assert.True(taxonomy.TryGetProperty("provinceRegionIds", out _));
            Assert.True(taxonomy.TryGetProperty("historicalPeriodIds", out _));
            Assert.True(taxonomy.TryGetProperty("architectureTypeIds", out _));
            Assert.True(taxonomy.TryGetProperty("gardenTypeIds", out _));
            Assert.True(taxonomy.TryGetProperty("landformIds", out _));
            Assert.True(taxonomy.TryGetProperty("weatherPhenomenonIds", out _));
        });
    }

    [Fact]
    public void Cx507ShortfallExceptionRequiresExplicitNonCommercialReviewMarker()
    {
        var catalog = CatalogFile.Load(ProjectPath("content/reference-library/catalog.json"));
        var original = catalog.Assets[0];
        var exceptionAsset = original with
        {
            License = "unverified",
            LicenseVersion = "unverified",
            LicenseUrl = null,
            CommercialUseAllowed = false,
            ModificationsAllowed = false,
            Width = 640,
            Height = 360,
            SourceVerification = "unverified",
            ShortfallException = new ShortfallExceptionAsset
            {
                Reason = "material_shortage",
                SourceStatus = "unverified",
                QualityStatus = "reduced_resolution",
                RequiresManualReview = true,
            },
        };
        var exceptionCatalog = catalog with
        {
            Assets = catalog.Assets.Skip(1).Prepend(exceptionAsset).ToArray(),
        };

        var accepted = CatalogPolicyValidator.Validate(
            exceptionCatalog,
            ProjectPath("content/reference-library/raw"));
        Assert.DoesNotContain(accepted, diagnostic => diagnostic.Code is "license.shortfall_exception" or "license.unverified_source");

        var unmarked = exceptionCatalog with
        {
            Assets = exceptionCatalog.Assets.Skip(1).Prepend(exceptionAsset with
            {
                SourceVerification = "verified",
                ShortfallException = null,
            }).ToArray(),
        };
        var rejected = CatalogPolicyValidator.Validate(
            unmarked,
            ProjectPath("content/reference-library/raw"));
        Assert.Contains(rejected, diagnostic => diagnostic.Code == "license.not_allowed");
    }

    [Fact]
    public void CheckedInArchitectureAssetsDiscloseDirectDownloadReviewState()
    {
        using var document = LoadJson("content/reference-library/catalog.json");
        var architectureAssets = document.RootElement.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("category").GetString() == "architecture")
            .ToArray();

        Assert.NotEmpty(architectureAssets);
        Assert.Equal(160, architectureAssets.Count(asset =>
            asset.GetProperty("taxonomy").GetProperty("peoplePresence").GetString() == "confirmed_absent"));
        Assert.All(architectureAssets, asset => Assert.Contains(
            asset.GetProperty("taxonomy").GetProperty("peoplePresence").GetString(),
            ["confirmed_absent", "unknown_needs_review"]));
    }

    [Fact]
    public void CheckedInReplicaObjectsAreMarkedReconstructed()
    {
        using var document = LoadJson("content/reference-library/catalog.json");
        var replicaTerms = new[] { "copy", "replica", "reproduction", "reconstructed" };
        var replicas = document.RootElement.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("category").GetString() is "artifacts" or "costumes_textiles")
            .Where(asset => replicaTerms.Any(term =>
                asset.GetProperty("title").GetString()!.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.NotEmpty(replicas);
        Assert.All(replicas, asset =>
            Assert.Equal("reconstructed", asset.GetProperty("taxonomy").GetProperty("authenticity").GetString()));
    }

    [Fact]
    public async Task CheckedInCatalogPassesCx507CoverageCommand()
    {
        var result = await ReferenceLibraryProcessHarness.RunToolAsync(
            "coverage",
            "--catalog", ProjectPath("content/reference-library/catalog.json"),
            "--plan", ProjectPath("content/reference-library/china-expansion-plan.cx507.json"),
            "--report", ProjectPath("content/reference-library/reports/china-coverage.cx507.json"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("800", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("coverage valid", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckedInCoverageReportHasNoFailedTargets()
    {
        using var document = LoadJson("content/reference-library/reports/china-coverage.cx507.json");
        var root = document.RootElement;
        Assert.Equal("2.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("passes").GetBoolean());
        Assert.True(root.GetProperty("totalAssets").GetInt32() >= 800);
        Assert.Empty(root.GetProperty("failedTargetIds").EnumerateArray());
        Assert.All(root.GetProperty("targets").EnumerateArray(), target =>
            Assert.True(target.GetProperty("passes").GetBoolean()));
    }

    [Fact]
    public async Task CandidateCacheIsIgnoredByGit()
    {
        var ignored = await ReferenceLibraryProcessHarness.RunGitAsync(
            "check-ignore", "--quiet", "content/reference-library/cache/sentinel.jpg");
        Assert.Equal(0, ignored.ExitCode);
    }

    private static JsonDocument LoadJson(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(ProjectPath(relativePath)));

    private static string ProjectPath(string relativePath) => Path.Combine(
        ReferenceLibraryProcessHarness.RepositoryRoot.FullName,
        relativePath.Replace('/', Path.DirectorySeparatorChar));
}
