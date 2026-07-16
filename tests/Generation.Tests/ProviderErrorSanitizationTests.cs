using System.Net;
using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderErrorSanitizationTests
{
    private const string PrivateBody = "PRIVATE_PROVIDER_DIAGNOSTIC_DO_NOT_LEAK";
    private const string QuerySecret = "signed-query-secret-do-not-leak";

    [Fact]
    public async Task VeoErrorIsStableTransientAndSanitized()
    {
        const string apiKey = "private-video-api-key";
        var handler = FailureHandler();
        var provider = new VeoVideoGenerationProvider(
            new HttpClient(handler),
            new VeoProviderOptions
            {
                Endpoint = new Uri("https://veo.test/v1/videos:generate?signature=" + QuerySecret),
                ApiKey = apiKey,
                ModelId = "server-video-model",
            });

        await AssertSanitizedAsync(
            () => provider.StartAsync(ProviderTestRequests.Video(), CancellationToken.None),
            handler,
            apiKey);
    }

    [Fact]
    public async Task ImageErrorIsStableTransientAndSanitized()
    {
        const string apiKey = "private-image-api-key";
        var handler = FailureHandler();
        var provider = new OpenAiImageGenerationProvider(
            new HttpClient(handler),
            new OpenAiImageProviderOptions
            {
                Endpoint = new Uri("https://openai.test/v1/images/generations?signature=" + QuerySecret),
                ApiKey = apiKey,
                ModelId = "server-image-model",
            });

        await AssertSanitizedAsync(
            () => provider.GenerateAsync(ProviderTestRequests.Image(), CancellationToken.None),
            handler,
            apiKey);
    }

    [Fact]
    public async Task TextErrorIsStableTransientAndSanitized()
    {
        const string apiKey = "private-text-api-key";
        var handler = FailureHandler();
        var provider = new OpenAiTextGenerationProvider(
            new HttpClient(handler),
            new OpenAiTextProviderOptions
            {
                Endpoint = new Uri("https://openai.test/v1/responses?signature=" + QuerySecret),
                ApiKey = apiKey,
                ModelId = "server-text-model",
            });

        await AssertSanitizedAsync(
            () => provider.GenerateAsync(ProviderTestRequests.Text(), CancellationToken.None),
            handler,
            apiKey);
    }

    private static ScriptedHttpMessageHandler FailureHandler()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.InternalServerError, PrivateBody);
        return handler;
    }

    private static async Task AssertSanitizedAsync<T>(
        Func<Task<T>> operation,
        ScriptedHttpMessageHandler handler,
        string apiKey)
    {
        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(operation);

        Assert.Equal(GenerationProviderErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.IsTransient);
        Assert.DoesNotContain(apiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateBody, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(QuerySecret, exception.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }
}
