using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class Cx515Flow2ApiProviderTests
{
    private static readonly Uri Endpoint =
        new("http://127.0.0.1:18001/v1/chat/completions");

    private const string T2VModel = "veo_3_1_t2v_lite_landscape";
    private const string I2VModel = "veo_3_1_i2v_lite_landscape";
    private const string ApprovedFlowContentUrl =
        "https://flow-content.google/video/73795b2d-fa31-4ccd-82e4-155dff205d68" +
        "?Expires=1893456000&KeyName=labs-flow-prod-cdn-key&Signature=test_signature-123";

    [Theory]
    [InlineData("http://23.95.205.140:8001/v1/chat/completions")]
    [InlineData("http://localhost:18001/v1/chat/completions")]
    [InlineData("http://127.0.0.1:8001/v1/chat/completions")]
    [InlineData("http://127.0.0.1:18001/v1/models")]
    [InlineData("http://user:secret@127.0.0.1:18001/v1/chat/completions")]
    [InlineData("http://127.0.0.1:18001/v1/chat/completions?key=secret")]
    [InlineData("https://127.0.0.1:18001/v1/chat/completions")]
    public void ProviderRejectsAnythingExceptTheExactLoopbackTunnel(string endpoint)
    {
        var options = Options();
        options.Endpoint = new Uri(endpoint);

        Assert.Throws<ArgumentException>(() => Create(new ScriptedHttpMessageHandler(), options));
    }

    [Theory]
    [InlineData("veo_3_1_t2v_fast_landscape", I2VModel)]
    [InlineData(T2VModel, "veo_3_1_i2v_s_fast_fl")]
    [InlineData("", I2VModel)]
    [InlineData(T2VModel, "")]
    public void ProviderRejectsNonLiteOrBlankModelIds(string textModel, string imageModel)
    {
        var options = Options();
        options.TextToVideoModelId = textModel;
        options.ImageToVideoModelId = imageModel;

        Assert.Throws<ArgumentException>(() => Create(new ScriptedHttpMessageHandler(), options));
    }

    [Fact]
    public async Task StartPostsBearerStreamingLiteI2VAndDownloadsVerifiedMp4()
    {
        byte[] mp4 = MinimalMp4();
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"10%\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"<video src=\\\"http://127.0.0.1:18001/tmp/cx515.mp4\\\"></video>\"}}]}\n\n" +
            "data: [DONE]\n\n")));
        handler.Enqueue((_, _) => Task.FromResult(Mp4Response(mp4)));
        handler.Enqueue((_, _) => Task.FromResult(Mp4Response(mp4)));
        const string apiKey = "cx515-test-key-never-log";
        var provider = Create(handler, Options(apiKey));
        VideoGenerationRequest request = ProviderTestRequests.Video() with
        {
            InputImages = [InputPng()],
        };

        ProviderOperation operation = await provider.StartAsync(request, CancellationToken.None);

        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.NotNull(operation.Artifact);
        Assert.Equal(mp4.Length, operation.Artifact.Length);
        Assert.Equal(Hash(mp4), operation.Artifact.Sha256);
        RecordedProviderRequest post = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal(Endpoint, post.Uri);
        Assert.Equal("Bearer", post.Authorization?.Scheme);
        Assert.Equal(apiKey, post.Authorization?.Parameter);
        using JsonDocument body = JsonDocument.Parse(post.Body);
        Assert.Equal(I2VModel, body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        JsonElement content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        string dataUrl = content[1].GetProperty("image_url").GetProperty("url").GetString()!;
        Assert.StartsWith("data:image/png;base64,", dataUrl, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, post.Body, StringComparison.Ordinal);

        await using var destination = new MemoryStream();
        await provider.DownloadAsync(operation.Artifact, destination, CancellationToken.None);
        Assert.Equal(mp4, destination.ToArray());
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Get, handler.Requests[2].Method);
        Assert.All(handler.Requests.Skip(1), recorded => Assert.Null(recorded.Authorization));
    }

    [Fact]
    public async Task StartWithoutImageUsesTheFixedTextToVideoLiteModel()
    {
        byte[] mp4 = MinimalMp4();
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(SuccessSse("t2v.mp4"))));
        handler.Enqueue((_, _) => Task.FromResult(Mp4Response(mp4)));
        var provider = Create(handler, Options());

        ProviderOperation operation = await provider.StartAsync(
            ProviderTestRequests.Video(),
            CancellationToken.None);

        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        using JsonDocument body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(T2VModel, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "TEST_ONLY_VIDEO_PROMPT",
            body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task StartAcceptsApprovedStructuredFlowContentResultWithoutFileExtension()
    {
        byte[] mp4 = MinimalMp4();
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(
            "data: {\"status\":\"success\",\"url\":\"" + ApprovedFlowContentUrl +
            "\",\"generated_assets\":{\"type\":\"video\",\"final_video_url\":\"" +
            ApprovedFlowContentUrl + "\"}}\n\n" +
            "data: [DONE]\n\n")));
        handler.Enqueue((_, _) => Task.FromResult(Mp4Response(mp4)));
        handler.Enqueue((_, _) => Task.FromResult(Mp4Response(mp4)));
        var provider = Create(handler, Options());

        ProviderOperation operation = await provider.StartAsync(
            ProviderTestRequests.Video() with { InputImages = [InputPng()] },
            CancellationToken.None);

        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.Equal(new Uri(ApprovedFlowContentUrl), operation.Artifact?.DownloadUri);
        await using var destination = new MemoryStream();
        await provider.DownloadAsync(operation.Artifact!, destination, CancellationToken.None);
        Assert.Equal(mp4, destination.ToArray());
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task StartExtractsApprovedFlowContentUrlFromChoiceText()
    {
        byte[] mp4 = MinimalMp4();
        var handler = new ScriptedHttpMessageHandler();
        string escapedResult = JsonSerializer.Serialize(new
        {
            status = "success",
            url = ApprovedFlowContentUrl,
        });
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(
            "data: " + JsonSerializer.Serialize(new
            {
                choices = new[] { new { delta = new { content = escapedResult } } },
            }) + "\n\n" +
            "data: [DONE]\n\n")));
        handler.Enqueue((_, _) => Task.FromResult(Mp4Response(mp4)));
        var provider = Create(handler, Options());

        ProviderOperation operation = await provider.StartAsync(
            ProviderTestRequests.Video(),
            CancellationToken.None);

        Assert.Equal(new Uri(ApprovedFlowContentUrl), operation.Artifact?.DownloadUri);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ProviderRejectsMalformedOrIncompleteSseWithoutLeakingSecrets()
    {
        const string apiKey = "private-cx515-key";
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"still working\"}}]}\n\n")));
        var provider = Create(handler, Options(apiKey));

        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(
            () => provider.StartAsync(ProviderTestRequests.Video(), CancellationToken.None));

        Assert.Equal(GenerationProviderErrorCodes.InvalidResponse, exception.Code);
        Assert.DoesNotContain(apiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("still working", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderRejectsPublicOrCrossOriginArtifactUrlsBeforeDownloading()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"<video src=\\\"http://23.95.205.140:8001/tmp/forbidden.mp4\\\"></video>\"}}]}\n\n" +
            "data: [DONE]\n\n")));
        var provider = Create(handler, Options());

        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(
            () => provider.StartAsync(ProviderTestRequests.Video(), CancellationToken.None));

        Assert.Equal(GenerationProviderErrorCodes.UnsafeDownload, exception.Code);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://flow-content.google/video/73795b2d-fa31-4ccd-82e4-155dff205d68?Expires=1893456000&KeyName=labs-flow-prod-cdn-key&Signature=test")]
    [InlineData("https://flow-content.google.evil.example/video/73795b2d-fa31-4ccd-82e4-155dff205d68?Expires=1893456000&KeyName=labs-flow-prod-cdn-key&Signature=test")]
    [InlineData("https://flow-content.google/not-video/73795b2d-fa31-4ccd-82e4-155dff205d68?Expires=1893456000&KeyName=labs-flow-prod-cdn-key&Signature=test")]
    [InlineData("https://flow-content.google/video/not-a-uuid?Expires=1893456000&KeyName=labs-flow-prod-cdn-key&Signature=test")]
    [InlineData("https://flow-content.google/video/73795b2d-fa31-4ccd-82e4-155dff205d68?Expires=1893456000&KeyName=labs-flow-prod-cdn-key")]
    [InlineData("https://flow-content.google/video/73795b2d-fa31-4ccd-82e4-155dff205d68?Expires=1893456000&KeyName=labs-flow-prod-cdn-key&Signature=test&next=https%3A%2F%2Fevil.example")]
    public async Task ProviderRejectsMalformedOrLookalikeFlowContentUrlsBeforeDownloading(string artifactUrl)
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue((_, _) => Task.FromResult(SseResponse(
            "data: " + JsonSerializer.Serialize(new { status = "success", url = artifactUrl }) + "\n\n" +
            "data: [DONE]\n\n")));
        var provider = Create(handler, Options());

        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(
            () => provider.StartAsync(ProviderTestRequests.Video(), CancellationToken.None));

        Assert.Equal(GenerationProviderErrorCodes.UnsafeDownload, exception.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProviderRejectsOversizedOrNonMp4Artifacts()
    {
        var oversizedHandler = new ScriptedHttpMessageHandler();
        oversizedHandler.Enqueue((_, _) => Task.FromResult(SseResponse(SuccessSse("large.mp4"))));
        var oversized = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[65]),
        };
        oversized.Content.Headers.ContentType = new("video/mp4");
        oversizedHandler.Enqueue((_, _) => Task.FromResult(oversized));
        var limitedOptions = Options();
        limitedOptions.MaxArtifactBytes = 64;

        GenerationProviderException tooLarge = await Assert.ThrowsAsync<GenerationProviderException>(
            () => Create(oversizedHandler, limitedOptions).StartAsync(
                ProviderTestRequests.Video(),
                CancellationToken.None));
        Assert.Equal(GenerationProviderErrorCodes.DownloadTooLarge, tooLarge.Code);

        var invalidHandler = new ScriptedHttpMessageHandler();
        invalidHandler.Enqueue((_, _) => Task.FromResult(SseResponse(SuccessSse("not-mp4.mp4"))));
        invalidHandler.Enqueue((_, _) => Task.FromResult(Mp4Response(Encoding.UTF8.GetBytes("not an mp4"))));

        GenerationProviderException invalid = await Assert.ThrowsAsync<GenerationProviderException>(
            () => Create(invalidHandler, Options()).StartAsync(
                ProviderTestRequests.Video(),
                CancellationToken.None));
        Assert.Equal(GenerationProviderErrorCodes.InvalidResponse, invalid.Code);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        var provider = Create(handler, Options());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.StartAsync(ProviderTestRequests.Video(), cancellation.Token));
    }

    [Fact]
    public void WorkerAcceptsOnlyTheApprovedFlow2ApiRuntimeSelection()
    {
        string? original = Environment.GetEnvironmentVariable("Flow2API");
        try
        {
            Environment.SetEnvironmentVariable("Flow2API", "unit-test-only-flow-key");
            GenerationProviderRuntimeSettings settings = RuntimeSettings(
                "http://127.0.0.1:18001/v1/chat/completions");

            settings.Validate();
            Assert.IsType<Flow2ApiVideoGenerationProvider>(ProviderBootstrap.CreateVideo(settings));

            GenerationProviderRuntimeSettings publicEndpoint = RuntimeSettings(
                "http://23.95.205.140:8001/v1/chat/completions");
            Assert.Throws<InvalidOperationException>(publicEndpoint.Validate);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Flow2API", original);
        }
    }

    [Fact]
    public void WorkerFailsClosedWhenFlow2ApiCredentialIsUnavailable()
    {
        string? original = Environment.GetEnvironmentVariable("Flow2API");
        try
        {
            Environment.SetEnvironmentVariable("Flow2API", null);
            Assert.Throws<InvalidOperationException>(() => RuntimeSettings(
                "http://127.0.0.1:18001/v1/chat/completions").Validate());
        }
        finally
        {
            Environment.SetEnvironmentVariable("Flow2API", original);
        }
    }

    [Fact]
    public async Task MockVideoIdentityIncludesTheInputImageHash()
    {
        var provider = new MockVideoGenerationProvider();
        VideoGenerationInputImage first = InputPng();
        byte[] alternateBytes = first.Bytes.Concat(new byte[] { 1 }).ToArray();
        var alternate = new VideoGenerationInputImage(
            "image/png",
            alternateBytes,
            Hash(alternateBytes));

        ProviderOperation firstOperation = await provider.StartAsync(
            ProviderTestRequests.Video() with { InputImages = [first] },
            CancellationToken.None);
        ProviderOperation secondOperation = await provider.StartAsync(
            ProviderTestRequests.Video() with { InputImages = [alternate] },
            CancellationToken.None);

        Assert.NotEqual(firstOperation.OperationId, secondOperation.OperationId);
    }

    private static Flow2ApiVideoGenerationProvider Create(
        ScriptedHttpMessageHandler handler,
        Flow2ApiVideoProviderOptions options) =>
        new(new HttpClient(handler), options);

    private static GenerationProviderRuntimeSettings RuntimeSettings(string endpoint) => new(
        NetworkEnabled: true,
        Video: new GenerationProviderSelection(
            "Flow2API",
            endpoint,
            I2VModel,
            "Flow2API"),
        Image: new GenerationProviderSelection("Mock", "", "", "OPENAI_API_KEY"),
        Text: new GenerationProviderSelection("Mock", "", "", "OPENAI_API_KEY"));

    private static Flow2ApiVideoProviderOptions Options(string apiKey = "test-flow2api-key") => new()
    {
        Endpoint = Endpoint,
        ApiKey = apiKey,
        TextToVideoModelId = T2VModel,
        ImageToVideoModelId = I2VModel,
        MaxArtifactBytes = 1024,
    };

    private static VideoGenerationInputImage InputPng()
    {
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        return new VideoGenerationInputImage("image/png", bytes, Hash(bytes));
    }

    private static HttpResponseMessage SseResponse(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };
        return response;
    }

    private static HttpResponseMessage Mp4Response(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Content.Headers.ContentType = new("video/mp4");
        response.Content.Headers.ContentLength = bytes.Length;
        return response;
    }

    private static string SuccessSse(string fileName) =>
        "data: {\"choices\":[{\"delta\":{\"content\":\"<video src=\\\"http://127.0.0.1:18001/tmp/" +
        fileName + "\\\"></video>\"}}]}\n\n" +
        "data: [DONE]\n\n";

    private static byte[] MinimalMp4() =>
    [
        0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
        (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0,
        (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'m', (byte)'p', (byte)'4', (byte)'2',
    ];

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
