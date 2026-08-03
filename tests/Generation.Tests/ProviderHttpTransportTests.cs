using System.Net;
using System.Text.Json;
using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderHttpTransportTests
{
    [Fact]
    public async Task VeoStartUsesOnlyTheServerConfiguredModel()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, "{\"name\":\"operations/test-video\",\"done\":false}");
        var options = new VeoProviderOptions
        {
            Endpoint = new Uri("https://veo.test/v1/videos:generate"),
            ApiKey = "server-video-key",
            ModelId = "server-only-video-model",
        };
        var provider = new VeoVideoGenerationProvider(new HttpClient(handler), options);

        ProviderOperation operation = await provider.StartAsync(
            ProviderTestRequests.Video(),
            CancellationToken.None);

        RecordedProviderRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(options.Endpoint, request.Uri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal(options.ApiKey, request.Authorization?.Parameter);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal(options.ModelId, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(17, body.RootElement.GetProperty("seed").GetInt64());
        Assert.DoesNotContain("client", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("operations/test-video", operation.OperationId);
        Assert.Equal(ProviderOperationStatus.Pending, operation.Status);
    }

    [Fact]
    public async Task OpenAiImageUsesOnlyTheServerConfiguredModel()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"data\":[{\"url\":\"https://artifacts.test/image.png\"," +
            "\"content_type\":\"image/png\",\"size_bytes\":128," +
            "\"sha256\":\"sha256:" + new string('4', 64) + "\"}]}");
        var options = new OpenAiImageProviderOptions
        {
            Endpoint = new Uri("https://openai.test/v1/images/generations"),
            ApiKey = "server-image-key",
            ModelId = "server-only-image-model",
        };
        var provider = new OpenAiImageGenerationProvider(new HttpClient(handler), options);

        ProviderArtifact artifact = await provider.GenerateAsync(
            ProviderTestRequests.Image(),
            CancellationToken.None);

        RecordedProviderRequest request = Assert.Single(handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal(options.ModelId, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(17, body.RootElement.GetProperty("seed").GetInt64());
        Assert.Equal("image/png", artifact.ContentType);
    }

    [Fact]
    public async Task OpenAiTextUsesOnlyTheServerConfiguredModelAndStrictJsonSchema()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, OpenAiTextResponse("{\"summary\":\"ok\"}"));
        var options = new OpenAiTextProviderOptions
        {
            Endpoint = new Uri("https://openai.test/v1/responses"),
            ApiKey = "server-text-key",
            ModelId = "server-only-text-model",
        };
        var provider = new OpenAiTextGenerationProvider(new HttpClient(handler), options);

        StructuredTextResult result = await provider.GenerateAsync(
            ProviderTestRequests.Text(),
            CancellationToken.None);

        RecordedProviderRequest request = Assert.Single(handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal(options.ModelId, body.RootElement.GetProperty("model").GetString());
        JsonElement format = body.RootElement.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("ok", result.Output.GetProperty("summary").GetString());
    }

    internal static string OpenAiTextResponse(string outputText) =>
        "{\"output\":[{\"type\":\"message\",\"content\":[{" +
        "\"type\":\"output_text\",\"text\":" + JsonSerializer.Serialize(outputText) + "}]}]}";
}
