using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationTemplateLoaderTests
{
    private const string ApprovedFallbackThreadId = "assoc.fallback.fixture";

    [Fact]
    public void LoadsValidatedTemplatesInOrdinalPathOrder()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("z-primary.json", Template("assoc.primary.v1", ApprovedFallbackThreadId));
        directory.Write("a-fallback.json", Template("assoc.fallback.v1", ApprovedFallbackThreadId));

        AssociationTemplateCatalog catalog = CreateLoader().LoadDirectory(directory.Path);

        Assert.Equal(
            new[] { "a-fallback.json", "z-primary.json" },
            catalog.AllTemplates.Select(item => item.SourcePath));
        Assert.Equal(2, catalog.ApprovedTemplates.Count);
    }

    [Fact]
    public void NeedsReviewTemplatesAreLoadedButNeverSignable()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write(
            "needs-review.json",
            Template("assoc.review.v1", ApprovedFallbackThreadId, approvalStatus: "needs_review"));

        AssociationTemplateCatalog catalog = CreateLoader().LoadDirectory(directory.Path);

        Assert.Single(catalog.AllTemplates);
        Assert.Empty(catalog.ApprovedTemplates);
        Assert.Equal("needs_review", catalog.AllTemplates[0].Template.ApprovalStatus);
    }

    [Fact]
    public void UnknownEntityIsRejectedWithSourceAndJsonPath()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid["preconditions"]!["forbidsFacts"]![0] = "fact.not_registered";
        directory.Write("unknown-entity.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("unknown-entity.json", error.SourcePath);
        Assert.Equal("$.preconditions.forbidsFacts[0]", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.UnknownEntity, error.Code);
        Assert.Contains("unknown-entity.json", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fact.not_registered", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityRegistryComparisonIsOrdinal()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid["preconditions"]!["forbidsFacts"]![0] = "fact.Known";
        directory.Write("case-sensitive-entity.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal(AssociationTemplateErrorCodes.UnknownEntity, error.Code);
    }

    [Fact]
    public void EntityRegisteredInWrongBucketIsStillRejected()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("wrong-bucket.json", Template("assoc.invalid.v1", ApprovedFallbackThreadId));
        var entities = new AssociationEntityRegistry(
            factIds: Array.Empty<string>(),
            chapterIds: new[] { "chapter.red_mist" },
            clueIds: new[] { "clue.known", "fact.known" },
            branchIds: Array.Empty<string>());

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader(entities).LoadDirectory(directory.Path));

        Assert.Equal("$.preconditions.forbidsFacts[0]", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.UnknownEntity, error.Code);
    }

    [Fact]
    public void DeclaredTemplateSlotsDeferDynamicEntityResolution()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject dynamic = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        dynamic["preconditions"]!["forbidsFacts"]![0] = "fact.{target}_unavailable";
        dynamic["allowedEffects"]![0]!["value"] = "clue.{target}_watching";
        directory.Write("dynamic-reference.json", dynamic);

        AssociationTemplateCatalog catalog = CreateLoader().LoadDirectory(directory.Path);

        Assert.Single(catalog.AllTemplates);
        Assert.Empty(catalog.ApprovedTemplates);
    }

    [Fact]
    public void UnknownTemplateSlotIsRejectedAtExactReferencePath()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        invalid["preconditions"]!["forbidsFacts"]![0] = "fact.{missing}_unavailable";
        directory.Write("unknown-slot.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.preconditions.forbidsFacts[0]", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.UnknownParameterSlot, error.Code);
    }

    [Fact]
    public void DynamicReferenceMustKeepItsTypedPrefix()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        invalid["preconditions"]!["forbidsFacts"]![0] = "clue.{target}_not_a_fact";
        directory.Write("wrong-prefix.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.preconditions.forbidsFacts[0]", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.UnknownEntity, error.Code);
    }

    [Fact]
    public void EmptyParameterSlotsAreAllowedWhenNoRelationshipEffectExists()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject valid = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        valid["parameterSlots"] = new JsonObject();
        valid["allowedEffects"] = new JsonArray();
        directory.Write("no-parameters.json", valid);

        AssociationTemplateCatalog catalog = CreateLoader().LoadDirectory(directory.Path);

        Assert.Single(catalog.AllTemplates);
        Assert.Empty(catalog.ApprovedTemplates);
    }

    [Fact]
    public void RelationshipEffectRequiresTargetParameterSlot()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        invalid["parameterSlots"] = new JsonObject();
        invalid["allowedEffects"] = new JsonArray(
            new JsonObject
            {
                ["op"] = "relationship",
                ["field"] = "trust",
                ["range"] = new JsonArray(-2, 3),
            });
        directory.Write("relationship-without-target.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.parameterSlots.target", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidContract, error.Code);
    }

    [Fact]
    public void RelationshipTargetMustResolveFromLedgerActor()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        invalid["parameterSlots"]!["target"]!["source"] = "route.upcomingNode.location";
        invalid["allowedEffects"] = new JsonArray(
            new JsonObject
            {
                ["op"] = "relationship",
                ["field"] = "trust",
                ["range"] = new JsonArray(-2, 3),
            });
        directory.Write("relationship-location-target.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.parameterSlots.target.source", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidContract, error.Code);
    }

    [Fact]
    public void RelationshipRangeMustBeOrdered()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.review.v1", ApprovedFallbackThreadId, "needs_review");
        invalid["allowedEffects"] = new JsonArray(
            new JsonObject
            {
                ["op"] = "relationship",
                ["field"] = "trust",
                ["range"] = new JsonArray(4, -4),
            });
        directory.Write("reversed-range.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.allowedEffects[0].range", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidContract, error.Code);
    }

    [Fact]
    public void UnauthorizedEffectIsRejectedWithSourceAndJsonPath()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid["allowedEffects"]![0]!["op"] = "attack";
        directory.Write("unauthorized-effect.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("unauthorized-effect.json", error.SourcePath);
        Assert.Equal("$.allowedEffects[0].op", error.JsonPath);
        Assert.Equal("/allowedEffects/0/op", error.JsonPointer);
        Assert.Equal(AssociationTemplateErrorCodes.UnauthorizedEffect, error.Code);
    }

    [Fact]
    public void MissingFallbackIsRejectedWithSourceAndJsonPath()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid.Remove("fallbackThreadId");
        directory.Write("missing-fallback.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("missing-fallback.json", error.SourcePath);
        Assert.Equal("$.fallbackThreadId", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.MissingFallback, error.Code);
    }

    [Fact]
    public void MissingTemplateIdReturnsStableValidationError()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid.Remove("templateId");
        directory.Write("missing-template-id.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("missing-template-id.json", error.SourcePath);
        Assert.Equal("$.templateId", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidContract, error.Code);
    }

    [Fact]
    public void MissingCooldownIsRejectedEvenThoughZeroWouldBeValid()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid.Remove("cooldownWorldClock");
        directory.Write("missing-cooldown.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.cooldownWorldClock", error.JsonPath);
        Assert.Equal("/cooldownWorldClock", error.JsonPointer);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidContract, error.Code);
    }

    [Fact]
    public void MissingLedgerMaxAgeIsRejectedEvenThoughZeroWouldBeValid()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        ((JsonObject)invalid["preconditions"]!["requiresLedger"]![0]!)
            .Remove("maxAgeWorldClock");
        directory.Write("missing-max-age.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.preconditions.requiresLedger[0].maxAgeWorldClock", error.JsonPath);
        Assert.Equal("/preconditions/requiresLedger/0/maxAgeWorldClock", error.JsonPointer);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidContract, error.Code);
    }

    [Fact]
    public void PositionalContractJsonRequiredRejectsMissingAllowedZeroField()
    {
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid.Remove("cooldownWorldClock");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssociationTemplateContract>(invalid.ToJsonString(), options));
    }

    [Fact]
    public void UnknownFallbackIsRejected()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("unknown-fallback.json", Template("assoc.primary.v1", "assoc.absent.v1"));

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("$.fallbackThreadId", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.UnknownFallback, error.Code);
    }

    [Fact]
    public void LoadedTemplateIdCannotMasqueradeAsFallbackThreadId()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("approved.json", Template("assoc.primary.v1", "assoc.review.v1"));
        directory.Write(
            "review.json",
            Template("assoc.review.v1", ApprovedFallbackThreadId, approvalStatus: "needs_review"));

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("approved.json", error.SourcePath);
        Assert.Equal(AssociationTemplateErrorCodes.UnknownFallback, error.Code);
    }

    [Fact]
    public void ApprovedTemplateRejectsNeedsReviewFallbackThread()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("approved.json", Template("assoc.primary.v1", "assoc.fallback.review"));
        AssociationFallbackThreadRegistry fallbacks =
            AssociationFallbackThreadRegistry.CreateForValidationTests(
                new[]
                {
                    new AssociationFallbackThreadValidationFixture(
                        "assoc.fallback.review",
                        "needs_review"),
                });

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader(fallbackThreads: fallbacks).LoadDirectory(directory.Path));

        Assert.Equal("$.fallbackThreadId", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.UnapprovedFallback, error.Code);
    }

    [Fact]
    public void EmptyProductionFallbackRegistryFailsClosed()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write(
            "review.json",
            Template("assoc.review.v1", ApprovedFallbackThreadId, approvalStatus: "needs_review"));

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader(fallbackThreads: AssociationFallbackThreadRegistry.Empty)
                .LoadDirectory(directory.Path));

        Assert.Equal(AssociationTemplateErrorCodes.UnknownFallback, error.Code);
    }

    [Fact]
    public void TemplateIdCaseCollisionIsRejected()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("first.json", Template("assoc.Case.v1", ApprovedFallbackThreadId));
        directory.Write("second.json", Template("assoc.case.v1", ApprovedFallbackThreadId));

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("second.json", error.SourcePath);
        Assert.Equal("$.templateId", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.DuplicateTemplateId, error.Code);
    }

    [Fact]
    public void DuplicateJsonPropertyIsRejectedBeforeDeserialization()
    {
        using var directory = new TemporaryTemplateDirectory();
        string json = Template("assoc.duplicate.v1", ApprovedFallbackThreadId).ToJsonString();
        json = json.Replace(
            "\"templateId\":\"assoc.duplicate.v1\"",
            "\"templateId\":\"assoc.duplicate.v1\",\"templateId\":\"assoc.other.v1\"",
            StringComparison.Ordinal);
        directory.WriteRaw("duplicate-property.json", json);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("duplicate-property.json", error.SourcePath);
        Assert.Equal("$.templateId", error.JsonPath);
        Assert.Equal(AssociationTemplateErrorCodes.DuplicateJsonProperty, error.Code);
    }

    [Fact]
    public void UnknownJsonMemberIsRejectedWithoutLeakingItsValue()
    {
        using var directory = new TemporaryTemplateDirectory();
        JsonObject invalid = Template("assoc.invalid.v1", ApprovedFallbackThreadId);
        invalid["secretText"] = "do-not-leak-this-value";
        directory.Write("unknown-member.json", invalid);

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal("unknown-member.json", error.SourcePath);
        Assert.Equal("/secretText", error.JsonPointer);
        Assert.Equal(AssociationTemplateErrorCodes.InvalidJson, error.Code);
        Assert.DoesNotContain("do-not-leak-this-value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedFileIsRejectedBeforeJsonParsing()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.WriteRaw(
            "oversized.json",
            new string(' ', AssociationTemplateLoader.MaximumTemplateBytes + 1));

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal(AssociationTemplateErrorCodes.FileTooLarge, error.Code);
        Assert.Equal("oversized.json", error.SourcePath);
    }

    [Fact]
    public void FileCountLimitStopsAtFirstExcessJsonFile()
    {
        using var directory = new TemporaryTemplateDirectory();
        for (int index = 0; index <= AssociationTemplateLoader.MaximumFileCount; index++)
        {
            directory.WriteRaw($"{index:D3}.json", "{}");
        }

        AssociationTemplateLoadException error = Assert.Throws<AssociationTemplateLoadException>(
            () => CreateLoader().LoadDirectory(directory.Path));

        Assert.Equal(AssociationTemplateErrorCodes.TooManyFiles, error.Code);
        Assert.Equal(".", error.SourcePath);
    }

    [Fact]
    public void EntityRegistryRejectsCaseFoldCollisions()
    {
        Assert.Throws<ArgumentException>(() => new AssociationEntityRegistry(
            factIds: new[] { "fact.Known", "fact.known" },
            chapterIds: Array.Empty<string>(),
            clueIds: Array.Empty<string>(),
            branchIds: Array.Empty<string>()));
    }

    [Fact]
    public void ApprovedSnapshotDoesNotShareMutableContractCollections()
    {
        using var directory = new TemporaryTemplateDirectory();
        directory.Write("approved.json", Template("assoc.primary.v1", ApprovedFallbackThreadId));

        AssociationTemplateCatalog catalog = CreateLoader().LoadDirectory(directory.Path);
        var authoringEffects = Assert.IsType<List<AssociationAllowedEffectContract>>(
            catalog.AllTemplates[0].Template.AllowedEffects);
        authoringEffects.Clear();

        Assert.Single(catalog.ApprovedTemplates[0].Template.AllowedEffects);
        Assert.True(
            Assert.IsAssignableFrom<ICollection<AssociationAllowedEffectContract>>(
                catalog.ApprovedTemplates[0].Template.AllowedEffects).IsReadOnly);
    }

    [Fact]
    public void RepositoryPlaceholdersLoadForAuthoringButCannotBeSigned()
    {
        string repositoryRoot = FindRepositoryRoot();
        string directory = System.IO.Path.Combine(repositoryRoot, "content", "templates", "associations");
        var entities = new AssociationEntityRegistry(
            factIds: Array.Empty<string>(),
            chapterIds: new[] { "chapter.red_mist" },
            clueIds: Array.Empty<string>(),
            branchIds: Array.Empty<string>());
        AssociationFallbackThreadRegistry fallbacks =
            AssociationFallbackThreadRegistry.CreateForValidationTests(
                new[]
                {
                    new AssociationFallbackThreadValidationFixture(
                        "assoc.placeholder.offline_noop.v1",
                        "needs_review"),
                });
        var loader = new AssociationTemplateLoader(
            new AssociationTemplateValidator(entities, fallbacks));

        AssociationTemplateCatalog catalog = loader.LoadDirectory(directory);

        Assert.Equal(2, catalog.AllTemplates.Count);
        Assert.Empty(catalog.ApprovedTemplates);
        Assert.All(
            catalog.AllTemplates,
            item => Assert.Equal("needs_review", item.Template.ApprovalStatus));
    }

    private static AssociationTemplateLoader CreateLoader(
        AssociationEntityRegistry? registry = null,
        AssociationFallbackThreadRegistry? fallbackThreads = null)
    {
        registry ??= new AssociationEntityRegistry(
            factIds: new[] { "fact.known" },
            chapterIds: new[] { "chapter.red_mist" },
            clueIds: new[] { "clue.known" },
            branchIds: Array.Empty<string>());
        fallbackThreads ??=
            AssociationFallbackThreadRegistry.CreateForValidationTests(
                new[]
                {
                    new AssociationFallbackThreadValidationFixture(
                        ApprovedFallbackThreadId,
                        "approved"),
                });
        return new AssociationTemplateLoader(
            new AssociationTemplateValidator(registry, fallbackThreads));
    }

    private static JsonObject Template(
        string templateId,
        string fallbackThreadId,
        string approvalStatus = "approved") =>
        (JsonObject)JsonNode.Parse(
            $$"""
            {
              "schemaVersion": "1.0.0",
              "templateId": "{{templateId}}",
              "kind": "callback",
              "authorityLevel": "L2",
              "preconditions": {
                "requiresLedger": [
                  {
                    "type": "enemy_spared",
                    "minSeverity": 2,
                    "maxAgeWorldClock": 1000
                  }
                ],
                "forbidsFacts": ["fact.known"],
                "chapterWindow": ["chapter.red_mist"]
              },
              "parameterSlots": {
                "target": { "source": "ledger.actors[1]" }
              },
              "injectionPoints": ["npc_mention"],
              "textPolicy": "llm_variant_within_style_guide",
              "allowedEffects": [
                { "op": "clue", "value": "clue.known" }
              ],
              "mediaPolicy": "reuse_only",
              "fallbackThreadId": "{{fallbackThreadId}}",
              "cooldownWorldClock": 100,
              "maxTriggersPerPlayer": 1,
              "approvalStatus": "{{approvalStatus}}"
            }
            """)!;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Video.sln")) &&
                Directory.Exists(System.IO.Path.Combine(directory.FullName, "content")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class TemporaryTemplateDirectory : IDisposable
    {
        public TemporaryTemplateDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "redmist-association-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string fileName, JsonObject value) =>
            WriteRaw(fileName, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        public void WriteRaw(string fileName, string value) =>
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), value);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
