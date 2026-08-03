using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderRequestContractTests
{
    [Theory]
    [InlineData(typeof(VideoGenerationRequest))]
    [InlineData(typeof(ImageGenerationRequest))]
    [InlineData(typeof(TextGenerationRequest))]
    public void ClientControlledRequestsCannotSelectProviderModels(Type requestType)
    {
        string[] forbiddenNames =
        [
            "Model",
            "ModelId",
            "ModelName",
            "ModelSnapshot",
            "ProviderModel",
        ];
        string[] propertyNames = requestType.GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name => forbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                    name.Contains("Model", StringComparison.OrdinalIgnoreCase));
    }
}
