using Lingmai.RedMist.Worker;
using Microsoft.Extensions.Configuration;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetRuntimeSettingsTests
{
    [Fact]
    public void MissingBudgetSectionFailsClosed()
    {
        IConfiguration configuration = Configuration([]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BudgetRuntimeSettings.FromConfiguration(configuration));

        Assert.Contains("Budget", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineMockAndManualPromptModesAllowDisabledZeroBudget()
    {
        BudgetRuntimeSettings budget = DisabledBudget();
        GenerationProviderRuntimeSettings providers = Providers(
            videoMode: "ManualPrompt",
            imageMode: "Mock",
            textMode: "Mock");

        Assert.False(providers.UsesPaidNetworkProvider);
        budget.Validate(providers.UsesPaidNetworkProvider);
    }

    [Theory]
    [InlineData("Veo", "Mock", "Mock")]
    [InlineData("ManualPrompt", "OpenAI", "Mock")]
    [InlineData("ManualPrompt", "Mock", "OpenAI")]
    public void EveryPaidProviderModeRequiresEnabledBudget(
        string videoMode,
        string imageMode,
        string textMode)
    {
        BudgetRuntimeSettings budget = DisabledBudget();
        GenerationProviderRuntimeSettings providers = Providers(videoMode, imageMode, textMode);

        Assert.True(providers.UsesPaidNetworkProvider);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => budget.Validate(providers.UsesPaidNetworkProvider));

        Assert.Contains("Budget:Enabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledBudgetWithSyntheticPositiveLimitsPassesValidation()
    {
        BudgetRuntimeSettings budget = ValidEnabledBudget();

        budget.Validate(paidProviderActive: true);
    }

    [Theory]
    [InlineData("Currency")]
    [InlineData("PriceVersion")]
    [InlineData("FallbackMediaRef")]
    [InlineData("AccountLimitMicros")]
    [InlineData("DeviceLimitMicros")]
    [InlineData("ChapterLimitMicros")]
    [InlineData("DailyLimitMicros")]
    [InlineData("ProjectLimitMicros")]
    public void EnabledBudgetRejectsEveryMissingRequiredValue(string invalidField)
    {
        BudgetRuntimeSettings valid = ValidEnabledBudget();
        BudgetRuntimeSettings invalid = invalidField switch
        {
            "Currency" => valid with { Currency = " " },
            "PriceVersion" => valid with { PriceVersion = string.Empty },
            "FallbackMediaRef" => valid with { FallbackMediaRef = "\t" },
            "AccountLimitMicros" => valid with { AccountLimitMicros = 0 },
            "DeviceLimitMicros" => valid with { DeviceLimitMicros = 0 },
            "ChapterLimitMicros" => valid with { ChapterLimitMicros = -1 },
            "DailyLimitMicros" => valid with { DailyLimitMicros = 0 },
            "ProjectLimitMicros" => valid with { ProjectLimitMicros = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => invalid.Validate(paidProviderActive: false));

        Assert.Contains($"Budget:{invalidField}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetSectionBindsEveryFieldWithoutFloatingPointValues()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Budget:Enabled"] = "true",
            ["Budget:Currency"] = "TEST",
            ["Budget:PriceVersion"] = "synthetic-v1",
            ["Budget:FallbackMediaRef"] = "offline://fallback",
            ["Budget:AccountLimitMicros"] = "11",
            ["Budget:DeviceLimitMicros"] = "12",
            ["Budget:ChapterLimitMicros"] = "13",
            ["Budget:DailyLimitMicros"] = "14",
            ["Budget:ProjectLimitMicros"] = "15",
        });

        BudgetRuntimeSettings budget = BudgetRuntimeSettings.FromConfiguration(configuration);

        Assert.True(budget.Enabled);
        Assert.Equal("TEST", budget.Currency);
        Assert.Equal("synthetic-v1", budget.PriceVersion);
        Assert.Equal("offline://fallback", budget.FallbackMediaRef);
        Assert.Equal(11, budget.AccountLimitMicros);
        Assert.Equal(12, budget.DeviceLimitMicros);
        Assert.Equal(13, budget.ChapterLimitMicros);
        Assert.Equal(14, budget.DailyLimitMicros);
        Assert.Equal(15, budget.ProjectLimitMicros);
    }

    private static BudgetRuntimeSettings DisabledBudget() => new(
        Enabled: false,
        Currency: string.Empty,
        PriceVersion: string.Empty,
        FallbackMediaRef: string.Empty,
        AccountLimitMicros: 0,
        DeviceLimitMicros: 0,
        ChapterLimitMicros: 0,
        DailyLimitMicros: 0,
        ProjectLimitMicros: 0);

    private static BudgetRuntimeSettings ValidEnabledBudget() => new(
        Enabled: true,
        Currency: "TEST",
        PriceVersion: "synthetic-v1",
        FallbackMediaRef: "offline://fallback",
        AccountLimitMicros: 1,
        DeviceLimitMicros: 1,
        ChapterLimitMicros: 1,
        DailyLimitMicros: 1,
        ProjectLimitMicros: 1);

    private static GenerationProviderRuntimeSettings Providers(
        string videoMode,
        string imageMode,
        string textMode) => new(
            NetworkEnabled: false,
            Video: Selection(videoMode),
            Image: Selection(imageMode),
            Text: Selection(textMode));

    private static GenerationProviderSelection Selection(string mode) => new(
        Mode: mode,
        Endpoint: string.Empty,
        ModelId: string.Empty,
        CredentialEnvironmentVariable: string.Empty);

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
