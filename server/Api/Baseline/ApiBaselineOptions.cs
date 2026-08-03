namespace Lingmai.RedMist.Api.Baseline;

public sealed class ApiBaselineOptions
{
    public const string SectionName = "ApiBaseline";

    public string ServiceName { get; set; } = string.Empty;
    public ApiRateLimitOptions RateLimit { get; set; } = new();
}

public sealed class ApiRateLimitOptions
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int QueueLimit { get; set; }
}

public static class ApiBaselineOptionsValidator
{
    public static void Validate(ApiBaselineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            throw new InvalidOperationException("ApiBaseline:ServiceName must not be empty.");
        if (options.ServiceName.Length > 128)
            throw new InvalidOperationException("ApiBaseline:ServiceName must not exceed 128 characters.");
        if (options.RateLimit is null)
            throw new InvalidOperationException("ApiBaseline:RateLimit is required.");
        if (options.RateLimit.PermitLimit is < 1 or > 10000)
            throw new InvalidOperationException("ApiBaseline:RateLimit:PermitLimit must be from 1 through 10000.");
        if (options.RateLimit.WindowSeconds is < 1 or > 3600)
            throw new InvalidOperationException("ApiBaseline:RateLimit:WindowSeconds must be from 1 through 3600.");
        if (options.RateLimit.QueueLimit != 0)
            throw new InvalidOperationException("ApiBaseline:RateLimit:QueueLimit must be zero.");
    }
}
