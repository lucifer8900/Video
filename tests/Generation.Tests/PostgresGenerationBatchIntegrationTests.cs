using Lingmai.RedMist.Generation.Batches;
using Lingmai.RedMist.Generation.Persistence;
using Lingmai.RedMist.Generation.Providers;
using Npgsql;

namespace Lingmai.RedMist.Generation.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresGenerationBatchIntegrationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BatchAttemptsReviewsAndMetricsSurviveRepositoryRestart()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();

        var clock = new FixedTimeProvider(FixedNow);
        var batchRepository = new PostgresGenerationBatchRepository(database.ConnectionString);
        var jobRepository = new PostgresGenerationJobRepository(database.ConnectionString);
        var service = new GenerationBatchService(
            batchRepository,
            new MockShotAttemptExecutor(
                jobRepository,
                new MockVideoGenerationProvider(),
                clock),
            clock);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);

        GenerationBatch preview = await service.StartPreviewAsync(
            new StartGenerationBatchRequest("postgres.batch.preview", manifest, ["shot.001"]),
            CancellationToken.None);
        GenerationBatch reviewed = await service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "postgres.batch.review",
                preview.Id,
                [BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);
        var restartedRepository = new PostgresGenerationBatchRepository(database.ConnectionString);
        GenerationBatch? restored = await restartedRepository.GetAsync(
            reviewed.Id,
            CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(GenerationBatchStatus.Reviewed, restored.Status);
        Assert.Equal(GenerationBatchItemStatus.Adopted, restored.Items[0].Status);
        Assert.Single(restored.Items[0].Attempts);
        GenerationBatchItem expectedItem = reviewed.Items[0];
        GenerationBatchItem restoredItem = restored.Items[0];
        Assert.Equal(expectedItem.DialogueBinding, restoredItem.DialogueBinding);
        Assert.Equal(expectedItem.FirstFrame, restoredItem.FirstFrame);
        Assert.Equal(expectedItem.LastFrame, restoredItem.LastFrame);
        Assert.Equal(expectedItem.PrimaryMedia, restoredItem.PrimaryMedia);
        Assert.Equal(expectedItem.FallbackMedia, restoredItem.FallbackMedia);
        Assert.Equal("video", restoredItem.Attempts[0].Artifact.MediaType);
        GenerationBatchReport report = GenerationBatchReportCalculator.Calculate(restored);
        Assert.Equal(1m, report.AdoptionRate);
        Assert.Equal(0m, report.UnitCostPerFinishedSecondMicros);
        Assert.Equal(1, await restartedRepository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredRunLeaseCanBeReclaimedByAnotherRepositoryInstance()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var first = new PostgresGenerationBatchRepository(database.ConnectionString);
        var second = new PostgresGenerationBatchRepository(database.ConnectionString);
        GenerationBatchSubmission submission = PendingSubmission("postgres.batch.lease");
        GenerationBatchCreateResult created = await first.CreateOrGetAsync(
            submission,
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);

        GenerationBatchRunLease? abandoned = await first.TryAcquireRunAsync(
            created.Batch.Id,
            "runner-before-crash",
            FixedNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        GenerationBatchRunLease? blocked = await second.TryAcquireRunAsync(
            created.Batch.Id,
            "runner-too-early",
            FixedNow.AddMinutes(4),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        GenerationBatchRunLease? recovered = await second.TryAcquireRunAsync(
            created.Batch.Id,
            "runner-after-restart",
            FixedNow.AddMinutes(6),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.NotNull(abandoned);
        Assert.Null(blocked);
        Assert.NotNull(recovered);
        Assert.Equal("runner-after-restart", recovered.Owner);
    }

    [Fact]
    public async Task SameOwnerReclaimGetsNewFencingTokenAndOldReleaseCannotClearIt()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var first = new PostgresGenerationBatchRepository(database.ConnectionString);
        var second = new PostgresGenerationBatchRepository(database.ConnectionString);
        GenerationBatchCreateResult created = await first.CreateOrGetAsync(
            PendingSubmission("postgres.batch.same-owner-fence"),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        GenerationBatchRunLease initial = (await first.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.reused",
            FixedNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None))!;
        GenerationBatchRunLease replacement = (await second.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.reused",
            FixedNow.AddMinutes(6),
            TimeSpan.FromMinutes(5),
            CancellationToken.None))!;

        await first.ReleaseRunAsync(
            created.Batch.Id,
            "runner.reused",
            initial.FencingToken,
            CancellationToken.None);
        GenerationBatchRunLease? incorrectlyAvailable = await first.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.other",
            FixedNow.AddMinutes(7),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.True(replacement.FencingToken > initial.FencingToken);
        Assert.Null(incorrectlyAvailable);
    }

    [Fact]
    public async Task ExpiredPostgresLeaseTokenCannotAppendOrFailAfterReplacement()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var repository = new PostgresGenerationBatchRepository(database.ConnectionString);
        GenerationBatchCreateResult created = await repository.CreateOrGetAsync(
            PendingSubmission("postgres.batch.stale-writer"),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        GenerationBatchRunLease initial = (await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.stale",
            FixedNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None))!;
        GenerationBatchRunLease replacement = (await repository.TryAcquireRunAsync(
            created.Batch.Id,
            "runner.stale",
            FixedNow.AddMinutes(6),
            TimeSpan.FromMinutes(5),
            CancellationToken.None))!;
        GenerationBatchShot shot = BatchFixture.Dispatchable(1).Shots[0];
        GenerationBatchAttemptReceipt receipt = await new MockShotAttemptExecutor(
                new PostgresGenerationJobRepository(database.ConnectionString),
                new MockVideoGenerationProvider(),
                new FixedTimeProvider(FixedNow.AddMinutes(6)))
            .ExecuteAsync(
                new GenerationBatchAttemptRequest(
                    created.Batch.Id,
                    shot.ShotId,
                    shot.InputHash,
                    GenerationBatchTier.PreviewFast,
                    AttemptNumber: 1,
                    shot.TargetDurationMilliseconds,
                    shot.AspectRatio,
                    shot.DialogueBinding,
                    shot.FirstFrame,
                    shot.LastFrame,
                    shot.PrimaryMedia,
                    shot.FallbackMedia),
                CancellationToken.None);
        var attempt = new GenerationBatchAttempt(
            1,
            receipt.GenerationJobId,
            receipt.Artifact,
            receipt.ActualDurationMilliseconds,
            receipt.ActualCostMicros,
            receipt.CostSettled,
            FixedNow.AddMinutes(6));

        GenerationBatchGateException staleAppend =
            await Assert.ThrowsAsync<GenerationBatchGateException>(
                () => repository.AppendAttemptAsync(
                    created.Batch.Id,
                    created.Batch.Version,
                    shot.ShotId,
                    attempt,
                    "runner.stale",
                    initial.FencingToken,
                    FixedNow.AddMinutes(6),
                    CancellationToken.None));
        GenerationBatchGateException staleFail =
            await Assert.ThrowsAsync<GenerationBatchGateException>(
                () => repository.FailItemAsync(
                    created.Batch.Id,
                    created.Batch.Version,
                    shot.ShotId,
                    "provider.mock_failure",
                    "runner.stale",
                    initial.FencingToken,
                    FixedNow.AddMinutes(6),
                    CancellationToken.None));
        GenerationBatch unchanged = (await repository.GetAsync(
            created.Batch.Id,
            CancellationToken.None))!;
        GenerationBatch appended = await repository.AppendAttemptAsync(
            created.Batch.Id,
            created.Batch.Version,
            shot.ShotId,
            attempt,
            "runner.stale",
            replacement.FencingToken,
            FixedNow.AddMinutes(6),
            CancellationToken.None);

        Assert.Equal("batch.run_lease_lost", staleAppend.Code);
        Assert.Equal("batch.run_lease_lost", staleFail.Code);
        Assert.Equal(GenerationBatchStatus.Running, unchanged.Status);
        Assert.Empty(unchanged.Items[0].Attempts);
        Assert.Equal(GenerationBatchStatus.AwaitingReview, appended.Status);
    }

    [Fact]
    public async Task DatabaseRejectsMalformedPinnedMedia()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var repository = new PostgresGenerationBatchRepository(database.ConnectionString);
        GenerationBatchCreateResult created = await repository.CreateOrGetAsync(
            PendingSubmission("postgres.batch.pin-constraint"),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE generation_batch_items SET fallback_media = '{}'::jsonb WHERE batch_id = @batch_id",
            connection);
        command.Parameters.AddWithValue("batch_id", created.Batch.Id);

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task RepositoryFailsClosedWhenMalformedPinBypassesDatabaseConstraint()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var repository = new PostgresGenerationBatchRepository(database.ConnectionString);
        GenerationBatchCreateResult created = await repository.CreateOrGetAsync(
            PendingSubmission("postgres.batch.pin-load"),
            Guid.NewGuid(),
            FixedNow,
            CancellationToken.None);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using (var dropConstraint = new NpgsqlCommand(
                             "ALTER TABLE generation_batch_items DROP CONSTRAINT ck_generation_batch_items_pin_objects",
                             connection))
            {
                await dropConstraint.ExecuteNonQueryAsync();
            }
            await using var corrupt = new NpgsqlCommand(
                "UPDATE generation_batch_items SET fallback_media = '{}'::jsonb WHERE batch_id = @batch_id",
                connection);
            corrupt.Parameters.AddWithValue("batch_id", created.Batch.Id);
            await corrupt.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.GetAsync(created.Batch.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PostgresReplayAndPinnedMediaConflictAreDurableAcrossRepositoryInstances()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var clock = new FixedTimeProvider(FixedNow);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(1);
        var firstService = new GenerationBatchService(
            new PostgresGenerationBatchRepository(database.ConnectionString),
            new MockShotAttemptExecutor(
                new PostgresGenerationJobRepository(database.ConnectionString),
                new MockVideoGenerationProvider(),
                clock),
            clock);
        var restartedService = new GenerationBatchService(
            new PostgresGenerationBatchRepository(database.ConnectionString),
            new MockShotAttemptExecutor(
                new PostgresGenerationJobRepository(database.ConnectionString),
                new MockVideoGenerationProvider(),
                clock),
            clock);
        var request = new StartGenerationBatchRequest(
            "postgres.batch.replay-pins",
            manifest,
            ["shot.001"]);
        GenerationBatch first = await firstService.StartPreviewAsync(request, CancellationToken.None);
        GenerationBatch replay = await restartedService.StartPreviewAsync(request, CancellationToken.None);
        GenerationBatchShot shot = manifest.Shots[0];
        GenerationBatchManifest changedPins = manifest with
        {
            Shots =
            [
                shot with
                {
                    FallbackMedia = shot.FallbackMedia with
                    {
                        ContentHash = BatchFixture.Hash("postgres.revised-fallback"),
                    },
                },
            ],
        };

        await Assert.ThrowsAsync<GenerationBatchIdempotencyConflictException>(
            () => restartedService.StartPreviewAsync(
                new StartGenerationBatchRequest(
                    "postgres.batch.replay-pins",
                    changedPins,
                    ["shot.001"]),
                CancellationToken.None));
        GenerationBatch restored = (await new PostgresGenerationBatchRepository(database.ConnectionString)
            .GetAsync(first.Id, CancellationToken.None))!;

        Assert.Equal(first.Id, replay.Id);
        Assert.Single(restored.Items[0].Attempts);
    }

    [Fact]
    public async Task FailedMultiShotBatchPersistsWithoutPendingItems()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var clock = new FixedTimeProvider(FixedNow);
        var batchRepository = new PostgresGenerationBatchRepository(database.ConnectionString);
        var inner = new MockShotAttemptExecutor(
            new PostgresGenerationJobRepository(database.ConnectionString),
            new MockVideoGenerationProvider(),
            clock);
        var executor = new FailSecondPostgresExecutor(inner);
        var service = new GenerationBatchService(batchRepository, executor, clock);
        GenerationBatchManifest manifest = BatchFixture.Dispatchable(3);

        GenerationBatch failed = await service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "postgres.batch.failed-multi",
                manifest,
                manifest.Shots.Select(shot => shot.ShotId).ToArray()),
            CancellationToken.None);
        GenerationBatch restored = (await new PostgresGenerationBatchRepository(database.ConnectionString)
            .GetAsync(failed.Id, CancellationToken.None))!;

        Assert.Equal(GenerationBatchStatus.Failed, restored.Status);
        Assert.All(restored.Items, item => Assert.Equal(GenerationBatchItemStatus.Failed, item.Status));
        Assert.Equal(2, executor.CallCount);
    }

    [Fact]
    public async Task OpenPostgresLaneIsLockedByManifestIdAcrossContentRevisions()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var clock = new FixedTimeProvider(FixedNow);
        var service = new GenerationBatchService(
            new PostgresGenerationBatchRepository(database.ConnectionString),
            new MockShotAttemptExecutor(
                new PostgresGenerationJobRepository(database.ConnectionString),
                new MockVideoGenerationProvider(),
                clock),
            clock);
        GenerationBatchManifest first = BatchFixture.Dispatchable(1);
        await service.StartPreviewAsync(
            new StartGenerationBatchRequest("postgres.batch.lane.one", first, ["shot.001"]),
            CancellationToken.None);
        GenerationBatchManifest revised = first with
        {
            ContentHash = BatchFixture.Hash("postgres.manifest.revised"),
            Shots = [first.Shots[0] with { InputHash = BatchFixture.Hash("postgres.input.revised") }],
        };

        GenerationBatchGateException blocked = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest("postgres.batch.lane.two", revised, ["shot.001"]),
                CancellationToken.None));

        Assert.Equal("batch.previous_review_pending", blocked.Code);
    }

    [Fact]
    public async Task PostgresPromotionRequiresAdoptedReviewedPreviewAndIsUnique()
    {
        await using PostgresBatchTestDatabase? database =
            await PostgresBatchTestDatabase.TryCreateAsync();
        if (database is null) return;
        await database.MigrateAsync();
        var clock = new FixedTimeProvider(FixedNow);
        var service = new GenerationBatchService(
            new PostgresGenerationBatchRepository(database.ConnectionString),
            new MockShotAttemptExecutor(
                new PostgresGenerationJobRepository(database.ConnectionString),
                new MockVideoGenerationProvider(),
                clock),
            clock);
        GenerationBatch preview = await service.StartPreviewAsync(
            new StartGenerationBatchRequest(
                "postgres.batch.promotion.preview",
                BatchFixture.Dispatchable(1),
                ["shot.001"]),
            CancellationToken.None);
        GenerationBatch reviewedPreview = await service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "postgres.review.promotion.preview",
                preview.Id,
                [BatchFixture.Review(preview, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);
        GenerationBatch final = await service.PromoteAdoptedAsync(
            new PromoteGenerationBatchRequest(
                "postgres.batch.promotion.final",
                reviewedPreview.Id,
                ["shot.001"]),
            CancellationToken.None);
        await service.ReviewAsync(
            new ReviewGenerationBatchRequest(
                "postgres.review.promotion.final",
                final.Id,
                [BatchFixture.Review(final, "shot.001", GenerationBatchReviewDecision.Adopt)]),
            CancellationToken.None);

        GenerationBatchGateException duplicate = await Assert.ThrowsAsync<GenerationBatchGateException>(
            () => service.PromoteAdoptedAsync(
                new PromoteGenerationBatchRequest(
                    "postgres.batch.promotion.final.duplicate",
                    reviewedPreview.Id,
                    ["shot.001"]),
                CancellationToken.None));

        Assert.Equal(GenerationBatchStatus.AwaitingReview, final.Status);
        Assert.Equal("batch.promotion_already_exists", duplicate.Code);
    }

    private static GenerationBatchSubmission PendingSubmission(string idempotencyKey)
    {
        GenerationBatchShot shot = BatchFixture.Dispatchable(1).Shots[0];
        return new GenerationBatchSubmission(
            idempotencyKey,
            BatchFixture.Hash("request:" + idempotencyKey),
            "manifest.postgres",
            BatchFixture.Hash("manifest.postgres"),
            GenerationBatchTier.PreviewFast,
            SourcePreviewBatchId: null,
            "USD",
            [
                new GenerationBatchItem
                {
                    ShotId = shot.ShotId,
                    InputHash = shot.InputHash,
                    RequestedTier = shot.RequestedTier,
                    TargetTier = GenerationBatchTier.PreviewFast,
                    TargetDurationMilliseconds = shot.TargetDurationMilliseconds,
                    AspectRatio = shot.AspectRatio,
                    MaximumCostMicros = shot.MaximumCostMicros,
                    DialogueBinding = shot.DialogueBinding,
                    FirstFrame = shot.FirstFrame,
                    LastFrame = shot.LastFrame,
                    PrimaryMedia = shot.PrimaryMedia,
                    FallbackMedia = shot.FallbackMedia,
                    Status = GenerationBatchItemStatus.Pending,
                    Attempts = [],
                },
            ]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailSecondPostgresExecutor(MockShotAttemptExecutor inner)
        : IGenerationBatchAttemptExecutor
    {
        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public int CallCount { get; private set; }

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 2) throw new InvalidOperationException("terminal mock failure");
            return inner.ExecuteAsync(request, cancellationToken);
        }
    }

    private sealed class PostgresBatchTestDatabase : IAsyncDisposable
    {
        private const string ConnectionStringEnvironmentVariable =
            "REDMIST_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private PostgresBatchTestDatabase(
            string adminConnectionString,
            string connectionString,
            string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            _schemaName = schemaName;
        }

        public string ConnectionString { get; }

        public static async Task<PostgresBatchTestDatabase?> TryCreateAsync()
        {
            string? configured = Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured)) return null;

            string schemaName = "cx502_" + Guid.NewGuid().ToString("N");
            var isolatedBuilder = new NpgsqlConnectionStringBuilder(configured)
            {
                SearchPath = schemaName,
                IncludeErrorDetail = false,
                Pooling = false,
            };
            await using var connection = new NpgsqlConnection(configured);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", connection);
            await command.ExecuteNonQueryAsync();
            return new PostgresBatchTestDatabase(
                configured,
                isolatedBuilder.ConnectionString,
                schemaName);
        }

        public async Task MigrateAsync()
        {
            var migrator = new GenerationDatabaseMigrator(
                ConnectionString,
                Path.Combine(
                    FindRepositoryRoot(),
                    "server",
                    "Generation",
                    "Persistence",
                    "Migrations"));
            await migrator.MigrateAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS \"{_schemaName}\" CASCADE",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "Video.sln")))
                current = current.Parent;
            return current?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        }
    }
}
