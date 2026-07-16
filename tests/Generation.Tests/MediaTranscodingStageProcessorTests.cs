using System.Reflection;
using Lingmai.RedMist.Generation.Media;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class MediaTranscodingStageProcessorTests
{
    private static readonly GenerationMediaResult SuccessfulResult = new(
        ObjectReference: "media.fixture.processed.v1",
        ContentHash: $"sha256:{new string('a', 64)}",
        ContentType: "video/mp4",
        Length: 4096);

    public static TheoryData<GenerationJobStatus> NonTranscodingStatuses() =>
        new()
        {
            GenerationJobStatus.Created,
            GenerationJobStatus.Queued,
            GenerationJobStatus.Generating,
            GenerationJobStatus.Moderating,
            GenerationJobStatus.Ready,
            GenerationJobStatus.Failed,
            GenerationJobStatus.Expired,
        };

    [Theory]
    [MemberData(nameof(NonTranscodingStatuses))]
    public async Task OnlyTranscodingJobsCanBeProcessed(GenerationJobStatus status)
    {
        var events = new List<string>();
        var pipeline = new RecordingPipeline(events, SuccessfulResult);
        var store = new RecordingResultStore(events);
        var processor = new MediaTranscodingStageProcessor(pipeline, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(JobAt(status), CancellationToken.None));

        Assert.Empty(events);
        Assert.Empty(store.Persisted);
    }

    [Fact]
    public async Task ReadyIsReturnedOnlyAfterPipelineResultIsPersisted()
    {
        var events = new List<string>();
        var pipeline = new RecordingPipeline(events, SuccessfulResult);
        var store = new RecordingResultStore(events);
        var processor = new MediaTranscodingStageProcessor(pipeline, store);
        GenerationJob job = JobAt(GenerationJobStatus.Transcoding);

        GenerationJobStatus status = await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Equal(GenerationJobStatus.Ready, status);
        Assert.Equal(["pipeline", "store"], events);
        var persisted = Assert.Single(store.Persisted);
        Assert.Equal(job.Id, persisted.JobId);
        Assert.Equal(SuccessfulResult, persisted.Result);
    }

    [Fact]
    public async Task PipelineFailureReturnsFailedAndNeverPersistsOrReturnsReady()
    {
        var events = new List<string>();
        var pipeline = new RecordingPipeline(
            events,
            (_, _) => throw new InvalidOperationException("fixture processing failure"));
        var store = new RecordingResultStore(events);
        var processor = new MediaTranscodingStageProcessor(pipeline, store);

        GenerationJobStatus status = await processor.ProcessAsync(
            JobAt(GenerationJobStatus.Transcoding),
            CancellationToken.None);

        Assert.Equal(GenerationJobStatus.Failed, status);
        Assert.Equal(["pipeline"], events);
        Assert.Empty(store.Persisted);
        Assert.NotEqual(GenerationJobStatus.Ready, status);
    }

    [Fact]
    public async Task PersistenceFailureReturnsFailedAndNeverReturnsReady()
    {
        var events = new List<string>();
        var pipeline = new RecordingPipeline(events, SuccessfulResult);
        var store = new RecordingResultStore(
            events,
            (_, _, _) => throw new InvalidOperationException("fixture persistence failure"));
        var processor = new MediaTranscodingStageProcessor(pipeline, store);

        GenerationJobStatus status = await processor.ProcessAsync(
            JobAt(GenerationJobStatus.Transcoding),
            CancellationToken.None);

        Assert.Equal(GenerationJobStatus.Failed, status);
        Assert.Equal(["pipeline", "store"], events);
        Assert.Empty(store.Persisted);
        Assert.NotEqual(GenerationJobStatus.Ready, status);
    }

    [Fact]
    public async Task PipelineCancellationPropagatesAndNeverPersists()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var events = new List<string>();
        var pipeline = new RecordingPipeline(
            events,
            (_, token) => Task.FromCanceled<GenerationMediaResult>(token));
        var store = new RecordingResultStore(events);
        var processor = new MediaTranscodingStageProcessor(pipeline, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(JobAt(GenerationJobStatus.Transcoding), cancellation.Token));

        Assert.Equal(["pipeline"], events);
        Assert.Empty(store.Persisted);
    }

    [Fact]
    public async Task PersistenceCancellationPropagatesAndNeverRecordsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var events = new List<string>();
        var pipeline = new RecordingPipeline(events, SuccessfulResult);
        var store = new RecordingResultStore(
            events,
            (_, _, token) => Task.FromCanceled(token));
        var processor = new MediaTranscodingStageProcessor(pipeline, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(JobAt(GenerationJobStatus.Transcoding), cancellation.Token));

        Assert.Equal(["pipeline", "store"], events);
        Assert.Empty(store.Persisted);
    }

    [Fact]
    public void PersistedMediaResultContainsStableMetadataButNoSignedUrlOrMediaPayload()
    {
        PropertyInfo[] properties = typeof(GenerationMediaResult).GetProperties();

        Assert.Equal(
            ["ObjectReference", "ContentHash", "ContentType", "Length"],
            properties.Select(property => property.Name).ToArray());
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Signed", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            property.PropertyType == typeof(byte[]) ||
            typeof(Stream).IsAssignableFrom(property.PropertyType));
    }

    private static GenerationJob JobAt(GenerationJobStatus status) =>
        new()
        {
            Id = Guid.Parse("6cf37337-8e30-453c-821f-d656d64f95ed"),
            IdempotencyKey = "fixture-media-stage",
            InputHash = $"sha256:{new string('b', 64)}",
            Status = status,
            CreatedAtUtc = new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero),
        };

    private sealed class RecordingPipeline : IGenerationMediaPipeline
    {
        private readonly List<string> _events;
        private readonly Func<GenerationJob, CancellationToken, Task<GenerationMediaResult>> _process;

        public RecordingPipeline(List<string> events, GenerationMediaResult result)
            : this(events, (_, _) => Task.FromResult(result))
        {
        }

        public RecordingPipeline(
            List<string> events,
            Func<GenerationJob, CancellationToken, Task<GenerationMediaResult>> process)
        {
            _events = events;
            _process = process;
        }

        public Task<GenerationMediaResult> ProcessAsync(
            GenerationJob job,
            CancellationToken cancellationToken)
        {
            _events.Add("pipeline");
            return _process(job, cancellationToken);
        }
    }

    private sealed class RecordingResultStore : IGenerationMediaResultStore
    {
        private readonly List<string> _events;
        private readonly Func<Guid, GenerationMediaResult, CancellationToken, Task> _persist;

        public RecordingResultStore(List<string> events)
            : this(events, (_, _, _) => Task.CompletedTask)
        {
        }

        public RecordingResultStore(
            List<string> events,
            Func<Guid, GenerationMediaResult, CancellationToken, Task> persist)
        {
            _events = events;
            _persist = persist;
        }

        public List<(Guid JobId, GenerationMediaResult Result)> Persisted { get; } = [];

        public async Task PersistAsync(
            Guid jobId,
            GenerationMediaResult result,
            CancellationToken cancellationToken)
        {
            _events.Add("store");
            await _persist(jobId, result, cancellationToken);
            Persisted.Add((jobId, result));
        }
    }
}
