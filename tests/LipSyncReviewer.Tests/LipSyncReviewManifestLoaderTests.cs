using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace LipSyncReviewer.Tests;

public sealed class LipSyncReviewManifestLoaderTests
{
    [Fact]
    public async Task ActualManifestProducesThirtyBlockedAndNoReviewableItems()
    {
        DirectoryInfo root = TestRepository.FindRoot();
        var loader = new LipSyncReviewManifestLoader(
            Path.Combine(root.FullName, "server", "Contracts", "schemas"));

        LoadedLipSyncReviewManifest manifest = await loader.LoadAsync(
            Path.Combine(root.FullName, "content", "story", "red-mist", "shot-manifest.json"),
            CancellationToken.None);

        Assert.Empty(manifest.ReviewableItems);
        Assert.Equal(30, manifest.BlockedItems.Count);
        Assert.All(
            manifest.BlockedItems,
            item =>
            {
                Assert.Equal("blocked", item.SourceReadinessStatus);
                Assert.Contains("audio.missing", item.IssueCodes);
                Assert.Contains("lip_sync.placeholder_media", item.IssueCodes);
            });
    }

    [Fact]
    public async Task ManifestContentMutationIsRejectedWithSanitizedCode()
    {
        DirectoryInfo root = TestRepository.FindRoot();
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "cx503-tampered-" + Guid.NewGuid().ToString("N") + ".json");
        File.Copy(
            Path.Combine(root.FullName, "content", "story", "red-mist", "shot-manifest.json"),
            temporaryPath);
        JsonObject rootNode = JsonNode.Parse(await File.ReadAllTextAsync(temporaryPath))!.AsObject();
        rootNode["responseRequirements"]![0]!["text"]!["text"] = "篡改后的台词";
        await File.WriteAllTextAsync(
            temporaryPath,
            rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            LipSyncReviewInputException exception =
                await Assert.ThrowsAsync<LipSyncReviewInputException>(
                    () => new LipSyncReviewManifestLoader(
                            Path.Combine(root.FullName, "server", "Contracts", "schemas"))
                        .LoadAsync(temporaryPath, CancellationToken.None));

            Assert.Equal("manifest.content_hash", exception.Code);
            Assert.DoesNotContain(temporaryPath, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    [Fact]
    public async Task CompleteNeedsReviewResponseLocksDialogueAndAllPresentationPins()
    {
        DirectoryInfo root = TestRepository.FindRoot();
        string sourcePath = Path.Combine(
            root.FullName,
            "content",
            "story",
            "red-mist",
            "shot-manifest.json");
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))!.AsObject();
        JsonObject response = manifest["responseRequirements"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["responseId"]!.GetValue<string>() == "npc.invalid.calm.abuse");
        response["lipSyncMedia"] = MediaPin("fixture.lipsync", Hash("video"), "video");
        response["audioMedia"] = MediaPin("fixture.audio", Hash("audio"), "audio");
        response["fallbackMedia"] = MediaPin("fixture.fallback", Hash("fallback"), "image");
        response["readinessStatus"] = "needs_review";
        response["issueCodes"] = new JsonArray("human_review.required", "source.original_missing");
        Rehash(manifest);
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "cx503-reviewable-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(
            temporaryPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            LoadedLipSyncReviewManifest loaded = await new LipSyncReviewManifestLoader(
                    Path.Combine(root.FullName, "server", "Contracts", "schemas"))
                .LoadAsync(temporaryPath, CancellationToken.None);

            LipSyncReviewCandidate candidate = Assert.Single(loaded.ReviewableItems);
            Assert.Equal(29, loaded.BlockedItems.Count);
            Assert.Equal("npc.invalid.calm.abuse", candidate.ResponseId);
            Assert.Equal("video", candidate.LipSyncMedia.MediaType);
            Assert.Equal("audio", candidate.AudioMedia.MediaType);
            Assert.Equal("image", candidate.FallbackMedia.MediaType);
            Assert.StartsWith("sha256:", candidate.DialogueContentHash, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", candidate.PresentationHash, StringComparison.Ordinal);
            Assert.NotEqual(candidate.DialogueContentHash, candidate.PresentationHash);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static JsonObject MediaPin(string mediaRef, string hash, string mediaType) =>
        new()
        {
            ["mediaRef"] = mediaRef,
            ["assetVersion"] = "v1",
            ["contentHash"] = hash,
            ["mediaType"] = mediaType,
        };

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void Rehash(JsonObject root)
    {
        root.Remove("contentHash");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, root);
        root["contentHash"] =
            "sha256:" + Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
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
}
