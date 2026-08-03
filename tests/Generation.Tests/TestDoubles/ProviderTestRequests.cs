using System.Text.Json;
using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Tests.TestDoubles;

internal static class ProviderTestRequests
{
    private static readonly Guid TestJobId =
        Guid.Parse("8ec94d59-8885-4a60-a02b-19683cd99b48");

    public static VideoGenerationRequest Video(long seed = 17) => new()
    {
        JobId = TestJobId,
        Prompt = "TEST_ONLY_VIDEO_PROMPT",
        InputHash = $"sha256:{new string('a', 64)}",
        Seed = seed,
        AspectRatio = "16:9",
        DurationSeconds = 8,
    };

    public static ImageGenerationRequest Image(long seed = 17) => new()
    {
        JobId = TestJobId,
        Prompt = "TEST_ONLY_IMAGE_PROMPT",
        InputHash = $"sha256:{new string('b', 64)}",
        Seed = seed,
        Width = 1024,
        Height = 1024,
    };

    public static TextGenerationRequest Text(long seed = 17) => new()
    {
        JobId = TestJobId,
        Instruction = "TEST_ONLY_RETURN_STRUCTURED_DATA",
        Input = ParseElement("{\"testInput\":true}"),
        InputHash = $"sha256:{new string('c', 64)}",
        Seed = seed,
        OutputSchema = ParseElement(
            "{\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"summary\"],\"properties\":{" +
            "\"summary\":{\"type\":\"string\"}}}"),
    };

    private static JsonElement ParseElement(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
