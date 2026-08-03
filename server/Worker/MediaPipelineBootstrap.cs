using Lingmai.RedMist.MediaPipeline;

namespace Lingmai.RedMist.Worker;

internal sealed record MediaPipelineRuntimeSettings(
    bool Enabled,
    string ObjectStorageMode,
    long MaxInputBytes,
    int SignedUrlTtlSeconds,
    string FfmpegPath,
    string FfprobePath,
    int ProcessTimeoutSeconds,
    Uri MockDownloadBaseUrl,
    string GcsBucketName)
{
    public static MediaPipelineRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetRequiredSection("MediaPipeline");
        string baseUrl = section["MockDownloadBaseUrl"] ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? mockBaseUrl))
            throw new InvalidOperationException("MediaPipeline:MockDownloadBaseUrl must be absolute.");
        var settings = new MediaPipelineRuntimeSettings(
            section.GetValue<bool>("Enabled"),
            section["ObjectStorageMode"] ?? string.Empty,
            section.GetValue<long>("MaxInputBytes"),
            section.GetValue<int>("SignedUrlTtlSeconds"),
            section["FfmpegPath"] ?? string.Empty,
            section["FfprobePath"] ?? string.Empty,
            section.GetValue<int>("ProcessTimeoutSeconds"),
            mockBaseUrl,
            section["GcsBucketName"] ?? string.Empty);
        settings.Validate();
        return settings;
    }

    private void Validate()
    {
        if (!string.Equals(ObjectStorageMode, "Mock", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ObjectStorageMode, "Gcs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MediaPipeline:ObjectStorageMode must be Mock or Gcs.");
        if (MaxInputBytes <= 0 || MaxInputBytes > 256L * 1024 * 1024)
            throw new InvalidOperationException("MediaPipeline:MaxInputBytes must be from 1 through 256 MiB.");
        if (SignedUrlTtlSeconds is < 1 or > 900)
            throw new InvalidOperationException("MediaPipeline:SignedUrlTtlSeconds must be from 1 through 900.");
        if (ProcessTimeoutSeconds is < 1 or > 900 ||
            string.IsNullOrWhiteSpace(FfmpegPath) ||
            string.IsNullOrWhiteSpace(FfprobePath))
            throw new InvalidOperationException("MediaPipeline tool paths and timeout are required.");
        if (!string.Equals(MockDownloadBaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(MockDownloadBaseUrl.UserInfo) ||
            !string.IsNullOrEmpty(MockDownloadBaseUrl.Query))
            throw new InvalidOperationException("MediaPipeline mock URL must be HTTPS without credentials or query.");
        if (Enabled && string.Equals(ObjectStorageMode, "Gcs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "GCS media distribution remains disabled until cloud budget and credentials are approved.");
    }
}

internal static class MediaPipelineBootstrap
{
    public static IServiceCollection AddMediaPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        MediaPipelineRuntimeSettings settings =
            MediaPipelineRuntimeSettings.FromConfiguration(configuration);
        services.AddSingleton(settings);
        services.AddSingleton(new FfmpegCommandBuilder(new FfmpegToolOptions
        {
            FfmpegPath = settings.FfmpegPath,
            FfprobePath = settings.FfprobePath,
            ProcessTimeout = TimeSpan.FromSeconds(settings.ProcessTimeoutSeconds),
        }));
        services.AddSingleton<FfmpegProcessRunner>();
        services.AddSingleton<FfprobeResultParser>();
        services.AddSingleton<MediaContentHasher>();
        services.AddSingleton<IObjectStorage>(
            new MockObjectStorage(settings.MaxInputBytes));
        services.AddSingleton<IMediaUrlSigner>(new MockMediaUrlSigner(
            settings.MockDownloadBaseUrl,
            TimeProvider.System,
            TimeSpan.FromSeconds(settings.SignedUrlTtlSeconds)));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new MediaDistributionOptions
        {
            MaxInputBytes = settings.MaxInputBytes,
            SignedUrlTtl = TimeSpan.FromSeconds(settings.SignedUrlTtlSeconds),
        });
        services.AddSingleton<MediaDistributionPipeline>();
        return services;
    }
}
