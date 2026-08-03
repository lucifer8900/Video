using Microsoft.Extensions.Configuration;

namespace Lingmai.RedMist.Worker;

internal sealed record BudgetRuntimeSettings(
    bool Enabled,
    string Currency,
    string PriceVersion,
    string FallbackMediaRef,
    long AccountLimitMicros,
    long DeviceLimitMicros,
    long ChapterLimitMicros,
    long DailyLimitMicros,
    long ProjectLimitMicros)
{
    public static BudgetRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetRequiredSection("Budget");

        return new BudgetRuntimeSettings(
            section.GetValue<bool>("Enabled"),
            section["Currency"] ?? string.Empty,
            section["PriceVersion"] ?? string.Empty,
            section["FallbackMediaRef"] ?? string.Empty,
            section.GetValue<long>("AccountLimitMicros"),
            section.GetValue<long>("DeviceLimitMicros"),
            section.GetValue<long>("ChapterLimitMicros"),
            section.GetValue<long>("DailyLimitMicros"),
            section.GetValue<long>("ProjectLimitMicros"));
    }

    public void Validate(bool paidProviderActive)
    {
        if (!Enabled)
        {
            if (paidProviderActive)
            {
                throw new InvalidOperationException(
                    "Budget:Enabled must be true before a paid generation provider mode can start.");
            }

            return;
        }

        RequireText(Currency, "Currency");
        RequireText(PriceVersion, "PriceVersion");
        RequireText(FallbackMediaRef, "FallbackMediaRef");
        RequirePositive(AccountLimitMicros, "AccountLimitMicros");
        RequirePositive(DeviceLimitMicros, "DeviceLimitMicros");
        RequirePositive(ChapterLimitMicros, "ChapterLimitMicros");
        RequirePositive(DailyLimitMicros, "DailyLimitMicros");
        RequirePositive(ProjectLimitMicros, "ProjectLimitMicros");
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Budget:{name} is required when Budget:Enabled is true.");
    }

    private static void RequirePositive(long value, string name)
    {
        if (value <= 0)
            throw new InvalidOperationException($"Budget:{name} must be greater than zero when Budget:Enabled is true.");
    }
}
