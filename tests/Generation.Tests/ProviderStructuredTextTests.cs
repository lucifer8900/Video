using System.Net;
using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderStructuredTextTests
{
    [Theory]
    [InlineData("not-json")]
    [InlineData("")]
    [InlineData("```json {\"summary\":\"wrapped\"} ```")]
    public async Task TextProviderRejectsAnythingThatIsNotBareStructuredJson(string output)
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ProviderHttpTransportTests.OpenAiTextResponse(output));
        var provider = new OpenAiTextGenerationProvider(
            new HttpClient(handler),
            new OpenAiTextProviderOptions
            {
                Endpoint = new Uri("https://openai.test/v1/responses"),
                ApiKey = "test-only-key",
                ModelId = "server-text-model",
            });

        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(
            () => provider.GenerateAsync(ProviderTestRequests.Text(), CancellationToken.None));

        Assert.Equal(GenerationProviderErrorCodes.InvalidResponse, exception.Code);
        Assert.False(exception.IsTransient);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TextProviderRejectsJsonThatViolatesTheRequestedSchema()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            ProviderHttpTransportTests.OpenAiTextResponse("{\"invented\":true}"));
        var provider = new OpenAiTextGenerationProvider(
            new HttpClient(handler),
            new OpenAiTextProviderOptions
            {
                Endpoint = new Uri("https://openai.test/v1/responses"),
                ApiKey = "test-only-key",
                ModelId = "server-text-model",
            });

        GenerationProviderException exception = await Assert.ThrowsAsync<GenerationProviderException>(
            () => provider.GenerateAsync(ProviderTestRequests.Text(), CancellationToken.None));

        Assert.Equal(GenerationProviderErrorCodes.InvalidResponse, exception.Code);
    }
}
