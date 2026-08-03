using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lingmai.RedMist.Generation.Batches;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ShotBatchManifestReaderTests
{
    [Fact]
    public async Task ApprovedCx501FixtureMapsToPinnedBatchManifest()
    {
        var reader = new ShotBatchManifestReader();
        string fixture = await File.ReadAllTextAsync(RepositoryPath(
            "tests/StoryFixtures/valid/shot-manifest.ready.json"));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Rehash(fixture)));

        GenerationBatchManifest manifest = await reader.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(GenerationBatchManifestApproval.Approved, manifest.ApprovalStatus);
        Assert.True(manifest.ShotDispatchAllowed);
        GenerationBatchShot shot = Assert.Single(manifest.Shots);
        Assert.Equal("shot.fixture.primary", shot.ShotId);
        Assert.Equal(GenerationBatchTier.FinalQuality, shot.RequestedTier);
        Assert.Equal(8_000, shot.TargetDurationMilliseconds);
        Assert.Equal("16:9", shot.AspectRatio);
        Assert.Equal("bound_to_primary_media", shot.DialogueBinding);
        Assert.Equal("fixture-first-frame", shot.FirstFrame!.MediaRef);
        Assert.Equal("fixture-last-frame", shot.LastFrame!.MediaRef);
        Assert.Equal("fixture-video", shot.PrimaryMedia!.MediaRef);
        Assert.Equal("fixture-background", shot.FallbackMedia.MediaRef);
        Assert.StartsWith("sha256:", shot.InputHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckedInProductionManifestIsReadButDispatchRemainsFailClosed()
    {
        var reader = new ShotBatchManifestReader();
        await using FileStream stream = File.OpenRead(RepositoryPath(
            "content/story/red-mist/shot-manifest.json"));
        GenerationBatchManifest manifest = await reader.ReadAsync(stream, CancellationToken.None);
        var repository = new InMemoryGenerationBatchRepository();
        var executor = new NeverCalledExecutor();
        var service = new GenerationBatchService(repository, executor, TimeProvider.System);

        GenerationBatchValidationException exception = await Assert.ThrowsAsync<GenerationBatchValidationException>(
            () => service.StartPreviewAsync(
                new StartGenerationBatchRequest(
                    "batch.production.blocked",
                    manifest,
                    [manifest.Shots[0].ShotId]),
                CancellationToken.None));

        Assert.Equal(GenerationBatchManifestApproval.NeedsReview, manifest.ApprovalStatus);
        Assert.False(manifest.ShotDispatchAllowed);
        Assert.Equal(14, manifest.Shots.Count);
        Assert.Equal("manifest.not_dispatchable", exception.Code);
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task TamperedManifestContentHashIsRejected()
    {
        string json = await File.ReadAllTextAsync(RepositoryPath(
            "tests/StoryFixtures/valid/shot-manifest.ready.json"));
        json = Rehash(json);
        json = json.Replace(
            "shot.fixture.primary",
            "shot.fixture.tampered",
            StringComparison.Ordinal);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new ShotBatchManifestReader();

        GenerationBatchValidationException exception = await Assert.ThrowsAsync<GenerationBatchValidationException>(
            () => reader.ReadAsync(stream, CancellationToken.None));

        Assert.Equal("manifest.content_hash", exception.Code);
    }

    [Fact]
    public async Task RehashedReadyShotMissingApprovedNestedPinsIsRejected()
    {
        string json = await File.ReadAllTextAsync(RepositoryPath(
            "tests/StoryFixtures/valid/shot-manifest.ready.json"));
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject shot = root["shots"]!.AsArray()[0]!.AsObject();
        shot.Remove("firstFrame");
        await using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(Rehash(root.ToJsonString())));
        var reader = new ShotBatchManifestReader();

        GenerationBatchValidationException exception = await Assert.ThrowsAsync<GenerationBatchValidationException>(
            () => reader.ReadAsync(stream, CancellationToken.None));

        Assert.Equal("manifest.shot_contract", exception.Code);
    }

    private static string RepositoryPath(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Video.sln")))
            current = current.Parent;
        if (current is null)
            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        return Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string Rehash(string json)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject content = root.DeepClone().AsObject();
        content.Remove("contentHash");
        using var contentStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(contentStream)) WriteCanonical(writer, content);
        root["contentHash"] = "sha256:" +
            Convert.ToHexString(SHA256.HashData(contentStream.ToArray())).ToLowerInvariant();
        return root.ToJsonString();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, JsonNode?> property in value.OrderBy(
                             property => property.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (JsonNode? item in value) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private sealed class NeverCalledExecutor : IGenerationBatchAttemptExecutor
    {
        public GenerationBatchProviderMode Mode => GenerationBatchProviderMode.Mock;

        public int CallCount { get; private set; }

        public Task<GenerationBatchAttemptReceipt> ExecuteAsync(
            GenerationBatchAttemptRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("The provider must not be called.");
        }
    }
}
