using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderOptionsTests
{
    [Fact]
    public void VeoRejectsBlankServerModelId()
    {
        var options = new VeoProviderOptions
        {
            Endpoint = new Uri("https://veo.test/v1/videos:generate"),
            ApiKey = "test-key",
            ModelId = " ",
        };

        Assert.Throws<ArgumentException>(
            () => new VeoVideoGenerationProvider(
                new HttpClient(new ScriptedHttpMessageHandler()),
                options));
    }

    [Fact]
    public void ImageProviderRejectsBlankServerModelId()
    {
        var options = new OpenAiImageProviderOptions
        {
            Endpoint = new Uri("https://openai.test/v1/images/generations"),
            ApiKey = "test-key",
            ModelId = string.Empty,
        };

        Assert.Throws<ArgumentException>(
            () => new OpenAiImageGenerationProvider(
                new HttpClient(new ScriptedHttpMessageHandler()),
                options));
    }

    [Fact]
    public void TextProviderRejectsBlankServerModelId()
    {
        var options = new OpenAiTextProviderOptions
        {
            Endpoint = new Uri("https://openai.test/v1/responses"),
            ApiKey = "test-key",
            ModelId = "\t",
        };

        Assert.Throws<ArgumentException>(
            () => new OpenAiTextGenerationProvider(
                new HttpClient(new ScriptedHttpMessageHandler()),
                options));
    }
}
