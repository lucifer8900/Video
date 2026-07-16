using System.Text.Json;

namespace Contracts.Schema.Tests;

public sealed class SchemaContractTests
{
    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";
    private const string SchemaVersion = "1.0.0";

    private static readonly string[] ContractSchemaFiles =
    [
        "api-error-response.schema.json",
        "api-health-response.schema.json",
        "story-fact.schema.json",
        "character-definition.schema.json",
        "character-state.schema.json",
        "quest-definition.schema.json",
        "scene-node.schema.json",
        "voice-intent.schema.json",
        "voice-evaluation-report.schema.json",
        "npc-response.schema.json",
        "combatant-state.schema.json",
        "encounter-definition.schema.json",
        "combat-resolution.schema.json",
        "media-asset.schema.json",
        "media-processing-result.schema.json",
        "media-download-ticket.schema.json",
        "budget-admission-result.schema.json",
        "budget-cost-record.schema.json",
        "generation-job-submit.schema.json",
        "generation-job-status.schema.json",
        "generation-job.schema.json",
        "generation-provider-result.schema.json",
        "generation-review-result.schema.json",
        "ledger-event.schema.json",
        "ledger-event-batch.schema.json",
        "ledger-event-batch-response.schema.json",
        "narrative-ledger-page.schema.json",
        "association-template.schema.json",
        "association-consistency-review.schema.json",
        "story-thread.schema.json",
        "save-game.schema.json",
        "story-bundle.schema.json",
    ];

    private static readonly string[] FixtureFiles =
    [
        "valid/api-error-response.json",
        "valid/api-health-response.json",
        "valid/story-fact.json",
        "valid/character-definition.json",
        "valid/character-state.json",
        "valid/quest-definition.json",
        "valid/scene-node.json",
        "valid/voice-intent.json",
        "valid/voice-evaluation-report.json",
        "valid/npc-response.json",
        "valid/combatant-state.json",
        "valid/encounter-definition.json",
        "valid/combat-resolution.json",
        "valid/media-asset.json",
        "valid/media-processing-result.succeeded.json",
        "valid/media-processing-result.failed.json",
        "valid/media-download-ticket.json",
        "valid/budget-admission-result.admitted.json",
        "valid/budget-admission-result.fallback-exceeded.json",
        "valid/budget-admission-result.fallback-unavailable.json",
        "valid/budget-cost-record.reserved.json",
        "valid/budget-cost-record.reconciliation-pending.json",
        "valid/budget-cost-record.settled.json",
        "valid/budget-cost-record.released.json",
        "valid/budget-cost-record.denied.json",
        "valid/generation-job-submit.json",
        "valid/generation-job-status.queued.json",
        "valid/generation-job-status.ready.json",
        "valid/generation-job-status.failed.json",
        "valid/generation-job-status.expired.json",
        "valid/generation-job.json",
        "valid/generation-job.created.json",
        "valid/generation-job.generating.json",
        "valid/generation-job.moderating.json",
        "valid/generation-job.transcoding.json",
        "valid/generation-job.ready.json",
        "valid/generation-job.failed.json",
        "valid/generation-job.expired.json",
        "valid/generation-provider-result.running.json",
        "valid/generation-provider-result.succeeded.json",
        "valid/generation-provider-result.failed.json",
        "valid/generation-review-result.ready.json",
        "valid/generation-review-result.rejected.json",
        "valid/ledger-event.json",
        "valid/ledger-event.item-gained.json",
        "valid/ledger-event-batch.json",
        "valid/ledger-event-batch-response.json",
        "valid/narrative-ledger-page.json",
        "valid/association-template.needs-review.json",
        "valid/association-template.no-parameters.needs-review.json",
        "valid/association-consistency-review.pass.json",
        "valid/association-consistency-review.reject.json",
        "valid/story-thread.json",
        "valid/save-game.json",
        "valid/story-bundle.json",
        "invalid/story-fact.missing-id.json",
        "invalid/scene-node.voice-without-invalid-input-rules.json",
        "invalid/scene-node.invalid-input-kind.json",
        "invalid/npc-response.severe-without-confirmation.json",
        "invalid/scene-node.severe-without-confirmation.json",
        "invalid/scene-node.invalid-injection-point.json",
        "invalid/story-bundle.dangling-reference.json",
        "invalid/generation-job.legacy-running.json",
        "invalid/generation-job.legacy-succeeded.json",
        "invalid/generation-job.legacy-cancelled.json",
        "invalid/generation-job.ready-missing-completed-at.json",
        "invalid/generation-job.ready-missing-result-media-ref.json",
        "invalid/generation-job.ready-missing-output-hash.json",
        "invalid/generation-job.failed-missing-completed-at.json",
        "invalid/generation-job.failed-missing-failure-code.json",
        "invalid/generation-provider-result.succeeded-missing-output-ref.json",
        "invalid/generation-provider-result.succeeded-missing-output-hash.json",
        "invalid/generation-provider-result.failed-missing-error-code.json",
        "invalid/generation-provider-result.additional-property.json",
        "invalid/generation-review-result.ready-with-failed-check.json",
        "invalid/generation-review-result.additional-property.json",
        "invalid/media-processing-result.succeeded-missing-profile.json",
        "invalid/media-processing-result.succeeded-missing-probe.json",
        "invalid/media-processing-result.succeeded-missing-content-hash.json",
        "invalid/media-processing-result.succeeded-missing-object-ref.json",
        "invalid/media-processing-result.succeeded-missing-length.json",
        "invalid/media-processing-result.failed-missing-error-code.json",
        "invalid/media-processing-result.additional-property.json",
        "invalid/media-download-ticket.bytes.json",
        "invalid/media-download-ticket.base64.json",
        "invalid/media-download-ticket.object-credential.json",
        "invalid/budget-admission-result.admitted-missing-reservation.json",
        "invalid/budget-admission-result.admitted-with-fallback.json",
        "invalid/budget-admission-result.fallback-missing-media.json",
        "invalid/budget-admission-result.fallback-missing-reason.json",
        "invalid/budget-admission-result.fallback-with-reservation.json",
        "invalid/budget-admission-result.account-id.json",
        "invalid/budget-admission-result.device-id.json",
        "invalid/budget-admission-result.scope-keys.json",
        "invalid/budget-cost-record.negative-estimate.json",
        "invalid/budget-cost-record.settled-missing-actual.json",
        "invalid/budget-cost-record.released-missing-actual.json",
        "invalid/budget-cost-record.denied-nonzero-actual.json",
        "invalid/budget-cost-record.account-id.json",
        "invalid/budget-cost-record.device-id.json",
        "invalid/budget-cost-record.scope-keys.json",
        "invalid/generation-job-submit.additional-property.json",
        "invalid/generation-job-status.queued-terminal.json",
        "invalid/generation-job-status.failed-missing-code.json",
        "invalid/generation-job-status.leaks-input-hash.json",
        "invalid/ledger-event.additional-property.json",
        "invalid/ledger-event.invalid-player-id.json",
        "invalid/ledger-event.nested-payload.json",
        "invalid/ledger-event.unknown-payload-key.json",
        "invalid/ledger-event.mismatched-payload-key.json",
        "invalid/ledger-event-batch.empty.json",
        "invalid/ledger-event-batch-response.leaks-fingerprint.json",
        "invalid/association-template.additional-property.json",
        "invalid/association-template.missing-fallback.json",
        "invalid/association-template.elevated-effect.json",
        "invalid/association-template.branch-extra-field.json",
        "invalid/association-template.clock-overflow.json",
        "invalid/association-template.relationship-range-overflow.json",
        "invalid/association-template.protected-relationship-field.json",
        "invalid/association-consistency-review.pass-with-reject-reason.json",
        "invalid/association-consistency-review.reject-with-none.json",
        "invalid/association-consistency-review.unknown-reason.json",
        "invalid/association-consistency-review.additional-property.json",
        "invalid/story-thread.additional-property.json",
        "invalid/story-thread.elevated-effect.json",
        "invalid/story-thread.persists-past-chapter.json",
        "invalid/story-thread.text-too-long.json",
        "invalid/story-thread.unsafe-control-character.json",
        "invalid/story-thread.relationship-with-value.json",
        "invalid/story-thread.invalid-thread-id.json",
        "invalid/story-thread.empty-injections.json",
    ];

    [Fact]
    public void Cx101InventoryIsComplete()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedPaths = ExpectedPaths();
        var missingPaths = expectedPaths
            .Where(path => !File.Exists(Path.Combine(repositoryRoot.FullName, ToPlatformPath(path))))
            .ToArray();

        Assert.True(
            missingPaths.Length == 0,
            $"CX-101 required files are missing:{Environment.NewLine}{string.Join(Environment.NewLine, missingPaths)}");
    }

    [Fact]
    public void ContractSchemasDeclareDraftAndVersion()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        foreach (var schemaFile in ContractSchemaFiles)
        {
            var schemaPath = SchemaPath(repositoryRoot, schemaFile);
            using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var root = document.RootElement;

            Assert.Equal(Draft202012, root.GetProperty("$schema").GetString());
            Assert.True(Uri.TryCreate(root.GetProperty("$id").GetString(), UriKind.Absolute, out _), schemaFile);

            var required = root.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray();
            Assert.Contains("schemaVersion", required);
            Assert.Equal(
                SchemaVersion,
                root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
        }
    }

    [Fact]
    public void ApiBaselineResponsesDeclareExactStrictShapes()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var errorDocument = JsonDocument.Parse(File.ReadAllText(
            SchemaPath(repositoryRoot, "api-error-response.schema.json")));
        AssertStrictShape(
            errorDocument.RootElement,
            ["schemaVersion", "requestId", "code", "message"]);

        using var healthDocument = JsonDocument.Parse(File.ReadAllText(
            SchemaPath(repositoryRoot, "api-health-response.schema.json")));
        JsonElement health = healthDocument.RootElement;
        AssertStrictShape(health, ["schemaVersion", "status", "requestId"]);
        Assert.Equal(
            "healthy",
            health.GetProperty("properties").GetProperty("status").GetProperty("const").GetString());
    }

    [Fact]
    public void SceneNodeDeclaresTheApprovedInjectionPoints()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(SchemaPath(repositoryRoot, "scene-node.schema.json")));
        var root = document.RootElement;
        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var allowed = root.GetProperty("properties")
            .GetProperty("injectionPoints")
            .GetProperty("items")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Contains("injectionPoints", required);
        Assert.Equal(["node_intro", "travel_event", "npc_mention"], allowed);
    }

    [Fact]
    public void GenerationJobDeclaresTheExactCx302States()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(
            SchemaPath(repositoryRoot, "generation-job.schema.json")));
        var status = document.RootElement.GetProperty("properties").GetProperty("jobStatus");
        var states = status.GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Equal(
            ["created", "queued", "generating", "moderating", "transcoding", "ready", "failed", "expired"],
            states);
    }

    [Fact]
    public void GenerationJobDeclaresCreatedAsTheInitialStatus()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(
            SchemaPath(repositoryRoot, "generation-job.schema.json")));
        var status = document.RootElement.GetProperty("properties").GetProperty("jobStatus");

        Assert.Equal("created", status.GetProperty("default").GetString());
    }

    [Fact]
    public void GenerationJobTerminalStatesRequireTheirCompletionFields()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(
            SchemaPath(repositoryRoot, "generation-job.schema.json")));
        var conditions = document.RootElement.GetProperty("allOf").EnumerateArray().ToArray();

        AssertConditionalRequirements(
            conditions,
            "jobStatus",
            "ready",
            ["completedAtUtc", "resultMediaRef", "outputHash"]);
        AssertConditionalRequirements(
            conditions,
            "jobStatus",
            "failed",
            ["completedAtUtc", "failureCode"]);
    }

    [Fact]
    public void GenerationClientSubmitAndStatusContractsAreStrictAndSanitized()
    {
        var repositoryRoot = FindRepositoryRoot();
        var submitPath = SchemaPath(repositoryRoot, "generation-job-submit.schema.json");
        var statusPath = SchemaPath(repositoryRoot, "generation-job-status.schema.json");
        Assert.True(File.Exists(submitPath), "CX-306 generation submit schema is missing.");
        Assert.True(File.Exists(statusPath), "CX-306 generation status schema is missing.");

        using var submitDocument = JsonDocument.Parse(File.ReadAllText(submitPath));
        AssertStrictShape(
            submitDocument.RootElement,
            ["schemaVersion", "idempotencyKey", "inputHash"]);
        AssertAllowedProperties(
            submitDocument.RootElement,
            ["schemaVersion", "idempotencyKey", "inputHash"]);

        using var statusDocument = JsonDocument.Parse(File.ReadAllText(statusPath));
        JsonElement status = statusDocument.RootElement;
        AssertStrictShape(
            status,
            [
                "schemaVersion", "requestId", "jobId", "status", "terminal",
                "pollAfterMilliseconds", "failureCode",
            ]);
        AssertAllowedProperties(
            status,
            [
                "schemaVersion", "requestId", "jobId", "status", "terminal",
                "pollAfterMilliseconds", "failureCode",
            ]);
        Assert.Equal(
            ["created", "queued", "generating", "moderating", "transcoding", "ready", "failed", "expired"],
            status.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        JsonElement failedCondition = status.GetProperty("allOf")
            .EnumerateArray()
            .Single(condition =>
                condition.GetProperty("if").GetProperty("properties")
                    .GetProperty("status").TryGetProperty("const", out JsonElement value) &&
                value.GetString() == "failed");
        Assert.Equal(
            ["generation.failed", "moderation.rejected"],
            failedCondition.GetProperty("then").GetProperty("properties")
                .GetProperty("failureCode").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.DoesNotContain("idempotencyKey", status.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("inputHash", status.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("lease", status.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerationProviderResultDeclaresStrictOperationOutcomes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = SchemaPath(repositoryRoot, "generation-provider-result.schema.json");
        Assert.True(File.Exists(schemaPath), "CX-303 generation provider result schema is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = document.RootElement;
        AssertAllowedProperties(
            root,
            [
                "schemaVersion", "providerKey", "operationId", "status", "modelSnapshot",
                "outputRef", "outputHash", "errorCode",
            ]);
        Assert.Equal(
            ["submitted", "running", "succeeded", "failed", "cancelled"],
            root.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());

        var conditions = root.GetProperty("allOf").EnumerateArray().ToArray();
        AssertConditionalRequirements(conditions, "status", "succeeded", ["outputRef", "outputHash"]);
        AssertConditionalRequirements(conditions, "status", "failed", ["errorCode"]);
    }

    [Fact]
    public void GenerationReviewResultRequiresEveryGateBeforeReady()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = SchemaPath(repositoryRoot, "generation-review-result.schema.json");
        Assert.True(File.Exists(schemaPath), "CX-303 generation review result schema is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = document.RootElement;
        AssertAllowedProperties(
            root,
            [
                "schemaVersion", "promptSafe", "playerInputSafe", "sourceRecorded",
                "characterConsistent", "dialogueConsistent", "humanApproved",
                "canEnterReady", "rejectionCodes",
            ]);
        AssertStrictShape(
            root,
            [
                "schemaVersion", "promptSafe", "playerInputSafe", "sourceRecorded",
                "characterConsistent", "dialogueConsistent", "humanApproved",
                "canEnterReady", "rejectionCodes",
            ]);

        var readyCondition = root.GetProperty("allOf").EnumerateArray().Single(item =>
            item.GetProperty("if").GetProperty("properties")
                .GetProperty("canEnterReady").GetProperty("const").GetBoolean());
        var requiredChecks = readyCondition.GetProperty("then").GetProperty("properties");
        foreach (var check in new[]
                 {
                     "promptSafe", "playerInputSafe", "sourceRecorded",
                     "characterConsistent", "dialogueConsistent", "humanApproved",
                 })
        {
            Assert.True(requiredChecks.GetProperty(check).GetProperty("const").GetBoolean(), check);
        }
    }

    [Fact]
    public void MediaProcessingResultDeclaresStrictSuccessAndFailureOutcomes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = SchemaPath(repositoryRoot, "media-processing-result.schema.json");
        Assert.True(File.Exists(schemaPath), "CX-304 media processing result schema is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = document.RootElement;
        AssertAllowedProperties(
            root,
            [
                "schemaVersion", "mediaId", "status", "profile", "probe",
                "contentHash", "objectRef", "length", "errorCode",
            ]);
        Assert.Equal(
            ["succeeded", "failed"],
            root.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());

        var probe = root.GetProperty("properties").GetProperty("probe");
        AssertAllowedProperties(
            probe,
            [
                "container", "width", "height", "frameRate", "durationMilliseconds",
                "hasAudio", "integratedLoudnessLufs", "truePeakDbtp",
            ]);

        var conditions = root.GetProperty("allOf").EnumerateArray().ToArray();
        AssertConditionalRequirements(
            conditions,
            "status",
            "succeeded",
            ["profile", "probe", "contentHash", "objectRef", "length"]);
        AssertConditionalRequirements(conditions, "status", "failed", ["errorCode"]);
    }

    [Fact]
    public void MediaDownloadTicketContainsMetadataButNeverMediaBytesOrCredentials()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = SchemaPath(repositoryRoot, "media-download-ticket.schema.json");
        Assert.True(File.Exists(schemaPath), "CX-304 media download ticket schema is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        AssertStrictShape(
            document.RootElement,
            [
                "schemaVersion", "requestId", "mediaId", "contentHash", "downloadUrl",
                "expiresAtUtc", "contentType", "length",
            ]);
        AssertAllowedProperties(
            document.RootElement,
            [
                "schemaVersion", "requestId", "mediaId", "contentHash", "downloadUrl",
                "expiresAtUtc", "contentType", "length",
            ]);
    }

    [Fact]
    public void BudgetAdmissionResultDeclaresMutuallyExclusiveAdmittedAndFallbackOutcomes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = SchemaPath(repositoryRoot, "budget-admission-result.schema.json");
        Assert.True(File.Exists(schemaPath), "CX-305 budget admission result schema is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = document.RootElement;
        AssertAllowedProperties(
            root,
            ["schemaVersion", "outcome", "reservationId", "fallbackMediaRef", "reasonCode"]);
        Assert.Equal(
            ["admitted", "fallback_budget_exceeded", "fallback_budget_unavailable"],
            root.GetProperty("properties").GetProperty("outcome").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());

        var conditions = root.GetProperty("allOf").EnumerateArray().ToArray();
        var admitted = conditions.Single(item =>
            item.GetProperty("if").GetProperty("properties")
                .GetProperty("outcome").TryGetProperty("const", out JsonElement value) &&
            value.GetString() == "admitted");
        Assert.Equal(
            ["reservationId"],
            admitted.GetProperty("then").GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            ["fallbackMediaRef", "reasonCode"],
            admitted.GetProperty("then").GetProperty("not").GetProperty("anyOf")
                .EnumerateArray()
                .Select(item => item.GetProperty("required")[0].GetString())
                .ToArray());

        var fallback = conditions.Single(item =>
            item.GetProperty("if").GetProperty("properties")
                .GetProperty("outcome").TryGetProperty("enum", out _));
        Assert.Equal(
            ["fallbackMediaRef", "reasonCode"],
            fallback.GetProperty("then").GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            ["reservationId"],
            fallback.GetProperty("then").GetProperty("not").GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void BudgetCostRecordUsesNonnegativeMicrosAndAuditableStatuses()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = SchemaPath(repositoryRoot, "budget-cost-record.schema.json");
        Assert.True(File.Exists(schemaPath), "CX-305 budget cost record schema is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = document.RootElement;
        AssertAllowedProperties(
            root,
            [
                "schemaVersion", "reservationId", "currency", "estimatedMicros",
                "maximumMicros", "actualMicros", "status", "priceVersion",
                "providerKey", "modelSnapshot",
            ]);
        Assert.Equal(
            ["reserved", "dispatching", "reconciliation_pending", "settled", "released", "denied"],
            root.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());

        foreach (string field in new[] { "estimatedMicros", "maximumMicros", "actualMicros" })
        {
            JsonElement money = root.GetProperty("properties").GetProperty(field);
            Assert.Equal("integer", money.GetProperty("type").GetString());
            Assert.Equal(0, money.GetProperty("minimum").GetInt64());
        }

        var conditions = root.GetProperty("allOf").EnumerateArray().ToArray();
        AssertConditionalRequirements(conditions, "status", "settled", ["actualMicros"]);
        AssertConditionalRequirements(conditions, "status", "released", ["actualMicros"]);
        var denied = conditions.Single(item =>
            item.GetProperty("if").GetProperty("properties")
                .GetProperty("status").GetProperty("const").GetString() == "denied");
        Assert.Equal(
            0,
            denied.GetProperty("then").GetProperty("properties")
                .GetProperty("actualMicros").GetProperty("const").GetInt64());
    }

    [Fact]
    public void SceneNodeDeclaresCompleteInvalidInputReactionRulesForVoiceNodes()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(SchemaPath(repositoryRoot, "scene-node.schema.json")));
        var root = document.RootElement;
        var rules = root.GetProperty("properties").GetProperty("invalid_input_rules");
        var requiredKinds = rules.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Equal(
            ["abuse", "irrelevant", "too_long", "silence", "low_confidence"],
            requiredKinds);
        Assert.False(rules.GetProperty("additionalProperties").GetBoolean());
        Assert.True(root.GetProperty("allOf").GetArrayLength() > 0);
    }

    [Fact]
    public void VoiceEvaluationReportDeclaresStrictCompleteShapesAndSafetyThresholds()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(
            SchemaPath(repositoryRoot, "voice-evaluation-report.schema.json")));
        var root = document.RootElement;

        AssertStrictShape(
            root,
            [
                "schemaVersion", "evaluationScope", "productionReadiness", "datasetId",
                "datasetSha256", "classifierId", "sampleCount", "audioSampleCount",
                "thresholds", "metrics", "passed", "classes", "samples",
            ]);

        var thresholds = root.GetProperty("properties").GetProperty("thresholds");
        AssertStrictShape(
            thresholds,
            ["minimumValidIntentAccuracy", "maximumSevereFalsePositiveRateExclusive"]);
        Assert.Equal(
            0.9d,
            thresholds.GetProperty("properties")
                .GetProperty("minimumValidIntentAccuracy")
                .GetProperty("minimum")
                .GetDouble());
        Assert.Equal(
            0.01d,
            thresholds.GetProperty("properties")
                .GetProperty("maximumSevereFalsePositiveRateExclusive")
                .GetProperty("maximum")
                .GetDouble());

        AssertStrictShape(
            root.GetProperty("properties").GetProperty("metrics"),
            [
                "overallCorrect", "totalSamples", "overallAccuracy", "validIntentCorrect",
                "validIntentTotal", "validIntentAccuracy", "severeFalsePositiveCount",
                "nonSevereSampleTotal", "severeFalsePositiveRate",
            ]);
        AssertStrictShape(
            root.GetProperty("$defs").GetProperty("classResult"),
            ["expectedOutcome", "total", "correct", "accuracy"]);
        AssertStrictShape(
            root.GetProperty("$defs").GetProperty("sampleResult"),
            [
                "id", "nodeId", "expectedOutcome", "expectedIntentId", "actualOutcome",
                "actualIntentId", "confidence", "source", "correct", "severeFalsePositive",
            ]);
    }

    [Fact]
    public void LedgerSchemasAreVersionedStrictAndBounded()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var ledgerEvent = JsonDocument.Parse(
            File.ReadAllText(SchemaPath(repositoryRoot, "ledger-event.schema.json")));
        using var batch = JsonDocument.Parse(
            File.ReadAllText(SchemaPath(repositoryRoot, "ledger-event-batch.schema.json")));
        using var acknowledgements = JsonDocument.Parse(
            File.ReadAllText(SchemaPath(repositoryRoot, "ledger-event-batch-response.schema.json")));
        using var page = JsonDocument.Parse(
            File.ReadAllText(SchemaPath(repositoryRoot, "narrative-ledger-page.schema.json")));

        AssertStrictShape(
            ledgerEvent.RootElement,
            [
                "schemaVersion", "entryId", "playerId", "type", "actors", "severity",
                "chapter", "worldClock", "sourceNodeId", "factRefs", "payload",
            ]);
        Assert.Equal(11, ledgerEvent.RootElement.GetProperty("allOf").GetArrayLength());
        Assert.True(ledgerEvent.RootElement.GetProperty("properties")
            .GetProperty("actors").GetProperty("uniqueItems").GetBoolean());
        Assert.True(ledgerEvent.RootElement.GetProperty("properties")
            .GetProperty("factRefs").GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(
            50,
            batch.RootElement.GetProperty("properties").GetProperty("events").GetProperty("maxItems").GetInt32());
        AssertStrictShape(batch.RootElement, ["schemaVersion", "events"]);
        AssertStrictShape(
            acknowledgements.RootElement,
            ["schemaVersion", "acknowledgements"]);
        AssertStrictShape(
            page.RootElement,
            ["schemaVersion", "entries", "nextCursor"]);
    }

    [Fact]
    public void AssociationTemplateDeclaresStrictL2Boundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(
            File.ReadAllText(SchemaPath(repositoryRoot, "association-template.schema.json")));
        var root = document.RootElement;

        AssertStrictShape(
            root,
            [
                "schemaVersion", "templateId", "kind", "authorityLevel", "preconditions",
                "parameterSlots", "injectionPoints", "textPolicy", "allowedEffects",
                "mediaPolicy", "fallbackThreadId", "cooldownWorldClock",
                "maxTriggersPerPlayer", "approvalStatus",
            ]);
        Assert.Equal("L2", root.GetProperty("properties").GetProperty("authorityLevel").GetProperty("const").GetString());
        Assert.Equal(
            ["relationship", "clue", "branch_unlock"],
            root.GetProperty("$defs")
                .GetProperty("allowedEffect")
                .GetProperty("properties")
                .GetProperty("op")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());
        Assert.Equal(
            "common.schema.json#/$defs/status",
            root.GetProperty("properties").GetProperty("approvalStatus").GetProperty("$ref").GetString());
        Assert.Equal(
            int.MaxValue,
            root.GetProperty("$defs")
                .GetProperty("ledgerRequirement")
                .GetProperty("properties")
                .GetProperty("maxAgeWorldClock")
                .GetProperty("maximum")
                .GetInt32());
        Assert.Equal(
            int.MaxValue,
            root.GetProperty("properties")
                .GetProperty("cooldownWorldClock")
                .GetProperty("maximum")
                .GetInt32());
        var effect = root.GetProperty("$defs").GetProperty("allowedEffect");
        Assert.Equal(
            ["trust", "respect", "fear", "debt", "suspicion", "affection"],
            effect.GetProperty("properties")
                .GetProperty("field")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());
        var rangeItems = root.GetProperty("$defs")
            .GetProperty("effectRange")
            .GetProperty("prefixItems")
            .EnumerateArray()
            .ToArray();
        Assert.All(rangeItems, item => Assert.Equal(-10, item.GetProperty("minimum").GetInt32()));
        Assert.All(rangeItems, item => Assert.Equal(10, item.GetProperty("maximum").GetInt32()));
    }

    [Fact]
    public void StoryThreadDeclaresStrictChapterScopedL2Boundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(
            File.ReadAllText(SchemaPath(repositoryRoot, "story-thread.schema.json")));
        JsonElement root = document.RootElement;

        AssertStrictShape(
            root,
            [
                "schemaVersion", "threadId", "templateId", "playerId", "resolvedParams",
                "injections", "mediaRefs", "expiresAtChapterEnd", "auditRef", "fallbackUsed",
            ]);
        Assert.True(root.GetProperty("properties").GetProperty("expiresAtChapterEnd").GetProperty("const").GetBoolean());

        JsonElement injection = root.GetProperty("$defs").GetProperty("injection");
        AssertStrictShape(injection, ["nodeId", "point", "text", "effects"]);
        JsonElement text = injection.GetProperty("properties").GetProperty("text");
        Assert.Equal(1, text.GetProperty("minLength").GetInt32());
        Assert.Equal(512, text.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "^(?=.*\\S)[^\\u0000-\\u001F\\u007F-\\u009F\\u00AD\\u061C\\u200E\\u200F\\u2028-\\u202E\\u2066-\\u2069\\uFEFF]*$",
            text.GetProperty("pattern").GetString());

        JsonElement effect = root.GetProperty("$defs").GetProperty("effect");
        AssertStrictShape(effect, ["op"]);
        Assert.Equal(
            ["relationship", "clue", "branch_unlock"],
            effect.GetProperty("properties").GetProperty("op").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void StoryFixturesProduceExpectedDiagnostics()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!InventoryExists(repositoryRoot))
        {
            return;
        }

        var validator = SchemaFixtureValidator.Load(repositoryRoot.FullName);
        var cases = LoadFixtureCases(repositoryRoot);
        var validSchemaCoverage = cases
            .Where(fixture => fixture.ExpectedValid)
            .Select(fixture => fixture.SchemaFile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var schemaFile in ContractSchemaFiles)
        {
            Assert.Contains(schemaFile, validSchemaCoverage);
        }

        foreach (var fixture in cases)
        {
            var diagnostics = validator.Validate(fixture);

            if (fixture.ExpectedValid)
            {
                Assert.True(
                    diagnostics.Count == 0,
                    $"{fixture.Name} should be valid but produced:{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}");
                continue;
            }

            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Code == fixture.ExpectedCode && diagnostic.Path == fixture.ExpectedPath);
        }
    }

    private static IReadOnlyList<FixtureCase> LoadFixtureCases(DirectoryInfo repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot.FullName, "tests", "StoryFixtures", "fixture-cases.json");
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return manifest?.Cases ?? throw new InvalidDataException("Fixture manifest does not contain cases.");
    }

    private static void AssertStrictShape(JsonElement schema, string[] expectedRequired)
    {
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            expectedRequired,
            schema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());
    }

    private static void AssertAllowedProperties(JsonElement schema, string[] expectedProperties)
    {
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            expectedProperties,
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static void AssertConditionalRequirements(
        JsonElement[] conditions,
        string statusProperty,
        string status,
        string[] expectedRequired)
    {
        var condition = conditions.Single(item =>
            item.GetProperty("if")
                .GetProperty("properties")
                .GetProperty(statusProperty)
                .GetProperty("const")
                .GetString() == status);
        var required = condition.GetProperty("then")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Equal(expectedRequired, required);
    }

    private static IEnumerable<string> ExpectedPaths()
    {
        yield return "server/Contracts/schemas/common.schema.json";

        foreach (var schemaFile in ContractSchemaFiles)
        {
            yield return $"server/Contracts/schemas/{schemaFile}";
        }

        yield return "tests/StoryFixtures/fixture-cases.json";

        foreach (var fixtureFile in FixtureFiles)
        {
            yield return $"tests/StoryFixtures/{fixtureFile}";
        }
    }

    private static bool InventoryExists(DirectoryInfo repositoryRoot) =>
        ExpectedPaths().All(path => File.Exists(Path.Combine(repositoryRoot.FullName, ToPlatformPath(path))));

    private static string SchemaPath(DirectoryInfo repositoryRoot, string schemaFile) =>
        Path.Combine(repositoryRoot.FullName, "server", "Contracts", "schemas", schemaFile);

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory;
            }
        }

        throw new DirectoryNotFoundException("Could not find the Video repository root from the test output directory.");
    }

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);
}
