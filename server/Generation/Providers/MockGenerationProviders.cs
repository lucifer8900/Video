using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lingmai.RedMist.Generation.Providers;

public sealed class MockVideoGenerationProvider : IVideoGenerationProvider
{
    private readonly ConcurrentDictionary<string, MockVideoOperation> _operations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _artifacts =
        new(StringComparer.Ordinal);

    public Task<ProviderOperation> StartAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateVideoRequest(request);
        string requestDigest = Digest(CanonicalVideoRequest(request));
        string operationId = "mock-video-operation-" + requestDigest;
        MockVideoOperation operation = _operations.GetOrAdd(
            operationId,
            _ => new MockVideoOperation(operationId, requestDigest));
        return Task.FromResult(operation.Snapshot());
    }

    public Task<ProviderOperation> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(operationId) ||
            !_operations.TryGetValue(operationId, out MockVideoOperation? operation))
        {
            throw NotFound("The mock video operation was not found.");
        }

        ProviderOperation result = operation.Poll(CreateArtifact);
        return Task.FromResult(result);
    }

    public Task CancelAsync(string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(operationId) ||
            !_operations.TryGetValue(operationId, out MockVideoOperation? operation))
        {
            throw NotFound("The mock video operation was not found.");
        }

        operation.Cancel();
        return Task.CompletedTask;
    }

    public async Task DownloadAsync(
        ProviderArtifact artifact,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        if (!_artifacts.TryGetValue(artifact.ArtifactId, out byte[]? content) ||
            content.LongLength != artifact.Length ||
            !string.Equals(HashBytes(content), artifact.Sha256, StringComparison.Ordinal))
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.ArtifactNotFound,
                "The mock video artifact was not found.");
        }

        await destination.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private ProviderArtifact CreateArtifact(string requestDigest)
    {
        string artifactId = "mock-video-artifact-" + requestDigest;
        byte[] content = Encoding.UTF8.GetBytes(
            "LINGMAI_TEST_ONLY_MOCK_VIDEO_V1\n" + requestDigest);
        _artifacts.TryAdd(artifactId, content);
        return new ProviderArtifact(
            artifactId,
            "video/mp4",
            content.LongLength,
            HashBytes(content));
    }

    private static string CanonicalVideoRequest(VideoGenerationRequest request) =>
        string.Join(
            "\n",
            "mock-video-v1",
            request.JobId.ToString("D"),
            request.Prompt,
            request.InputHash,
            request.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.AspectRatio,
            request.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void ValidateVideoRequest(VideoGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt) ||
            string.IsNullOrWhiteSpace(request.InputHash) ||
            string.IsNullOrWhiteSpace(request.AspectRatio) ||
            request.DurationSeconds <= 0)
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.InvalidRequest,
                "The video generation request is invalid.");
        }
    }

    private static GenerationProviderException NotFound(string message) =>
        new(GenerationProviderErrorCodes.OperationNotFound, message);

    private sealed class MockVideoOperation(string operationId, string requestDigest)
    {
        private readonly object _gate = new();
        private int _pollCount;
        private bool _cancelled;
        private ProviderArtifact? _artifact;

        public ProviderOperation Snapshot()
        {
            lock (_gate)
            {
                if (_cancelled)
                    return new ProviderOperation(operationId, ProviderOperationStatus.Cancelled);
                if (_artifact is not null)
                    return new ProviderOperation(
                        operationId,
                        ProviderOperationStatus.Succeeded,
                        _artifact);
                return new ProviderOperation(operationId, ProviderOperationStatus.Pending);
            }
        }

        public ProviderOperation Poll(Func<string, ProviderArtifact> createArtifact)
        {
            lock (_gate)
            {
                if (_cancelled)
                    return new ProviderOperation(operationId, ProviderOperationStatus.Cancelled);
                if (_artifact is not null)
                    return new ProviderOperation(
                        operationId,
                        ProviderOperationStatus.Succeeded,
                        _artifact);

                _pollCount++;
                if (_pollCount == 1)
                    return new ProviderOperation(operationId, ProviderOperationStatus.Running);

                _artifact = createArtifact(requestDigest);
                return new ProviderOperation(
                    operationId,
                    ProviderOperationStatus.Succeeded,
                    _artifact);
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_artifact is null) _cancelled = true;
            }
        }
    }

    internal static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string HashBytes(byte[] value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

public sealed class MockImageGenerationProvider : IImageGenerationProvider
{
    public Task<ProviderArtifact> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt) ||
            string.IsNullOrWhiteSpace(request.InputHash) ||
            request.Width <= 0 ||
            request.Height <= 0)
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.InvalidRequest,
                "The image generation request is invalid.");
        }

        string canonical = string.Join(
            "\n",
            "mock-image-v1",
            request.JobId.ToString("D"),
            request.Prompt,
            request.InputHash,
            request.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string digest = MockVideoGenerationProvider.Digest(canonical);
        byte[] content = Encoding.UTF8.GetBytes("LINGMAI_TEST_ONLY_MOCK_IMAGE_V1\n" + digest);
        return Task.FromResult(new ProviderArtifact(
            "mock-image-artifact-" + digest,
            "image/png",
            content.LongLength,
            MockVideoGenerationProvider.HashBytes(content)));
    }
}

public sealed class MockTextGenerationProvider : ITextGenerationProvider
{
    public Task<StructuredTextResult> GenerateAsync(
        TextGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Instruction) ||
            string.IsNullOrWhiteSpace(request.InputHash) ||
            request.Input.ValueKind == JsonValueKind.Undefined ||
            request.OutputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.InvalidRequest,
                "The text generation request is invalid.");
        }

        string canonical = string.Join(
            "\n",
            "mock-text-v1",
            request.JobId.ToString("D"),
            request.Instruction,
            request.Input.GetRawText(),
            request.InputHash,
            request.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.OutputSchema.GetRawText());
        string digest = MockVideoGenerationProvider.Digest(canonical);
        string json = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summary"] = "mock:" + digest,
        });
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement output = document.RootElement.Clone();
        return Task.FromResult(new StructuredTextResult(
            output,
            MockVideoGenerationProvider.HashBytes(Encoding.UTF8.GetBytes(output.GetRawText()))));
    }
}
