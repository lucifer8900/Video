using System.Text.Json;
using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class ProviderRuntimeConfigurationTests
{
    [Fact]
    public void WorkerDefaultsCannotReachPaidGenerationProviders()
    {
        string repositoryRoot = FindRepositoryRoot();
        string configurationPath = Path.Combine(repositoryRoot, "server", "Worker", "appsettings.json");

        Assert.True(File.Exists(configurationPath), "Worker provider defaults are missing.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationPath));
        JsonElement providers = document.RootElement.GetProperty("GenerationProviders");

        Assert.False(providers.GetProperty("NetworkEnabled").GetBoolean());
        Assert.Equal("ManualPrompt", providers.GetProperty("Video").GetProperty("Mode").GetString());
        Assert.Equal("Mock", providers.GetProperty("Image").GetProperty("Mode").GetString());
        Assert.Equal("Mock", providers.GetProperty("Text").GetProperty("Mode").GetString());
        Assert.Equal(
            "http://127.0.0.1:18001/v1/chat/completions",
            providers.GetProperty("Video").GetProperty("Endpoint").GetString());
        Assert.Equal(
            Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel,
            providers.GetProperty("Video").GetProperty("ModelId").GetString());
        Assert.Equal(
            "Flow2API",
            providers.GetProperty("Video").GetProperty("CredentialEnvironmentVariable").GetString());
        Assert.Equal(string.Empty, providers.GetProperty("Image").GetProperty("ModelId").GetString());
        Assert.Equal(string.Empty, providers.GetProperty("Text").GetProperty("ModelId").GetString());

        string serialized = providers.GetRawText();
        Assert.DoesNotContain("ApiKey", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GEMINI", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("23.95.205.140", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerMediaPipelineDefaultsAreOfflineAndBounded()
    {
        string repositoryRoot = FindRepositoryRoot();
        string configurationPath = Path.Combine(repositoryRoot, "server", "Worker", "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationPath));
        JsonElement media = document.RootElement.GetProperty("MediaPipeline");

        Assert.False(media.GetProperty("Enabled").GetBoolean());
        Assert.Equal("Mock", media.GetProperty("ObjectStorageMode").GetString());
        Assert.InRange(media.GetProperty("MaxInputBytes").GetInt64(), 1, 256L * 1024 * 1024);
        Assert.InRange(media.GetProperty("SignedUrlTtlSeconds").GetInt32(), 1, 900);
        Assert.Equal("ffmpeg", media.GetProperty("FfmpegPath").GetString());
        Assert.Equal("ffprobe", media.GetProperty("FfprobePath").GetString());

        string serialized = media.GetRawText();
        Assert.DoesNotContain("private_key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Goog-Signature", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkerBudgetDefaultsCannotAuthorizeRealSpending()
    {
        string repositoryRoot = FindRepositoryRoot();
        string configurationPath = Path.Combine(repositoryRoot, "server", "Worker", "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationPath));
        JsonElement budget = document.RootElement.GetProperty("Budget");

        Assert.False(budget.GetProperty("Enabled").GetBoolean());
        Assert.Equal(string.Empty, budget.GetProperty("Currency").GetString());
        Assert.Equal(string.Empty, budget.GetProperty("PriceVersion").GetString());
        Assert.Equal(string.Empty, budget.GetProperty("FallbackMediaRef").GetString());
        foreach (string name in new[]
                 {
                     "AccountLimitMicros", "DeviceLimitMicros", "ChapterLimitMicros",
                     "DailyLimitMicros", "ProjectLimitMicros",
                 })
        {
            Assert.Equal(0, budget.GetProperty(name).GetInt64());
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Video.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Video repository root.");
    }
}
