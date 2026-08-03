using Lingmai.RedMist.Api.Asr;
using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Api.Intent;
using Lingmai.RedMist.Api.Media;
using Lingmai.RedMist.Api.Generation;
using Lingmai.RedMist.Api.Ledger;
using Lingmai.RedMist.Generation;
using Lingmai.RedMist.Generation.Ledger;
using Lingmai.RedMist.Generation.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

ApiBaselineOptions apiBaselineOptions = builder.Configuration
    .GetRequiredSection(ApiBaselineOptions.SectionName)
    .Get<ApiBaselineOptions>()
    ?? throw new InvalidOperationException("ApiBaseline configuration is required.");
ApiBaselineOptionsValidator.Validate(apiBaselineOptions);
AsrOptions asrOptions = builder.Configuration
    .GetRequiredSection("Asr")
    .Get<AsrOptions>()
    ?? throw new InvalidOperationException("Asr configuration is required.");
if (string.IsNullOrWhiteSpace(asrOptions.StagingDirectory))
    asrOptions.StagingDirectory = Path.Combine(Path.GetTempPath(), "lingmai-redmist", "asr");
ValidateAsrOptions(asrOptions);
IntentOptions intentOptions = builder.Configuration
    .GetRequiredSection("Intent")
    .Get<IntentOptions>()
    ?? throw new InvalidOperationException("Intent configuration is required.");
ValidateIntentOptions(intentOptions);
GenerationApiRuntimeOptions generationOptions =
    GenerationApiRuntimeOptions.FromConfiguration(builder.Configuration);
LedgerRuntimeOptions ledgerOptions =
    LedgerRuntimeOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(apiBaselineOptions);
builder.Services.AddRateLimiter(options =>
{
    ApiRateLimitOptions limits = apiBaselineOptions.RateLimit;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = limits.PermitLimit,
                QueueLimit = limits.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromSeconds(limits.WindowSeconds),
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter =
            limits.WindowSeconds.ToString(CultureInfo.InvariantCulture);
        await ApiErrorWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "api.rate_limited",
            "Too many requests. Try again later.",
            cancellationToken).ConfigureAwait(false);
    };
});
builder.Services.AddSingleton(asrOptions);
builder.Services.AddSingleton(asrOptions.OpenAi);
builder.Services.AddSingleton<WavAudioInspector>();
builder.Services.AddSingleton<TemporaryAudioStore>();
builder.Services.AddSingleton(new MockAsrProvider(new AsrProviderResult(
    asrOptions.MockTranscript,
    "zh",
    0d)));
builder.Services.AddHttpClient<OpenAiAsrProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(asrOptions.OpenAi.TimeoutSeconds);
});
builder.Services.AddScoped<IAsrProvider>(services =>
    string.Equals(asrOptions.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<OpenAiAsrProvider>()
        : services.GetRequiredService<MockAsrProvider>());
builder.Services.AddScoped<AsrTranscriptionService>();

builder.Services.AddSingleton(intentOptions);
builder.Services.AddSingleton(intentOptions.OpenAi);
var bundleIntentCatalog = new BundleIntentCatalog(intentOptions);
builder.Services.AddSingleton<IIntentCatalog>(bundleIntentCatalog);
builder.Services.AddSingleton<IVoiceInteractionCatalog>(
    new BundleVoiceInteractionCatalog(intentOptions, bundleIntentCatalog));
builder.Services.AddSingleton<InvalidInputDetector>();
builder.Services.AddSingleton<NpcConsequenceGate>();
builder.Services.AddSingleton<LocalKeywordIntentClassifier>();
builder.Services.AddSingleton(new MockIntentClassifier(new IntentDecision(
    ParseMockOutcome(intentOptions.MockOutcome),
    string.IsNullOrWhiteSpace(intentOptions.MockIntentId) ? null : intentOptions.MockIntentId,
    intentOptions.MockConfidence,
    "mock")));
builder.Services.AddHttpClient<OpenAiIntentClassifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(intentOptions.OpenAi.TimeoutSeconds);
});
builder.Services.AddScoped<IntentClassificationService>(services =>
{
    IIntentClassifier primary = intentOptions.Provider.ToUpperInvariant() switch
    {
        "OPENAI" => services.GetRequiredService<OpenAiIntentClassifier>(),
        "LOCAL" => services.GetRequiredService<LocalKeywordIntentClassifier>(),
        _ => services.GetRequiredService<MockIntentClassifier>(),
    };
    return new IntentClassificationService(
        primary,
        services.GetRequiredService<LocalKeywordIntentClassifier>());
});
builder.Services.AddScoped<VoiceInteractionService>();
builder.Services.AddSingleton<IMediaDeliveryService, UnavailableMediaDeliveryService>();
builder.Services.AddSingleton<IGenerationJobRepository, InMemoryGenerationJobRepository>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GenerationJobService>();
builder.Services.AddSingleton(generationOptions);
builder.Services.AddSingleton(new GenerationSubmissionGate(generationOptions.Enabled));
builder.Services.AddSingleton(ledgerOptions);
if (ledgerOptions.Enabled)
{
    builder.Services.AddSingleton<ILedgerRepository>(_ => ledgerOptions.UsePostgres
        ? new PostgresLedgerRepository(ledgerOptions.ConnectionString!)
        : new InMemoryLedgerRepository());
    builder.Services.AddSingleton<LedgerService>();
    if (ledgerOptions.UsePostgres)
        builder.Services.AddHostedService<LedgerDatabaseInitializationService>();
}

var app = builder.Build();

app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    HttpContext context = statusContext.HttpContext;
    (string code, string message) = context.Response.StatusCode switch
    {
        StatusCodes.Status404NotFound => ("api.not_found", "The requested resource was not found."),
        StatusCodes.Status405MethodNotAllowed => ("api.method_not_allowed", "The HTTP method is not allowed for this resource."),
        _ => ("api.http_error", "The request could not be completed."),
    };
    await ApiErrorWriter.WriteAsync(
        context,
        context.Response.StatusCode,
        code,
        message,
        context.RequestAborted).ConfigureAwait(false);
});
app.UseRouting();
app.UseRateLimiter();
app.MapApiBaselineEndpoints();
app.MapAsrEndpoints();
app.MapIntentEndpoints();
app.MapVoiceInteractionEndpoints();
app.MapMediaDownloadEndpoints();
app.MapGenerationJobEndpoints();
if (ledgerOptions.Enabled) app.MapLedgerEndpoints();
app.Run();

static void ValidateAsrOptions(AsrOptions options)
{
    bool mock = string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase);
    bool openAi = string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase);
    if (!mock && !openAi) throw new InvalidOperationException("Asr:Provider must be Mock or OpenAI.");
    if (options.MaxAudioBytes <= 0 || options.MaxDurationSeconds <= 0d)
        throw new InvalidOperationException("ASR size and duration limits must be positive.");
    if (options.AllowedSampleRatesHz is null || options.AllowedSampleRatesHz.Length == 0)
        throw new InvalidOperationException("ASR must allow at least one sample rate.");
    if (openAi && (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey) || string.IsNullOrWhiteSpace(options.OpenAi.ModelId)))
        throw new InvalidOperationException("OpenAI ASR requires a server-side API key and model ID.");
    if (options.OpenAi.TimeoutSeconds is < 1 or > 60)
        throw new InvalidOperationException("OpenAI ASR timeout must be between 1 and 60 seconds.");
}

static void ValidateIntentOptions(IntentOptions options)
{
    bool mock = string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase);
    bool local = string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase);
    bool openAi = string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase);
    if (!mock && !local && !openAi)
        throw new InvalidOperationException("Intent:Provider must be Mock, Local, or OpenAI.");
    if (string.IsNullOrWhiteSpace(options.StoryBundlePath))
        throw new InvalidOperationException("Intent:StoryBundlePath must not be empty.");
    if (options.MaxTranscriptCharacters is < 1 or > 4096)
        throw new InvalidOperationException("Intent transcript limit must be from 1 through 4096 characters.");
    if (options.MaxAcceptedTranscriptCharacters < options.MaxTranscriptCharacters ||
        options.MaxAcceptedTranscriptCharacters > 16384)
    {
        throw new InvalidOperationException("Intent accepted transcript limit must include the story limit and be at most 16384 characters.");
    }
    if (!double.IsFinite(options.MockConfidence) || options.MockConfidence is < 0d or > 1d)
        throw new InvalidOperationException("Intent mock confidence must be from 0 through 1.");
    ParseMockOutcome(options.MockOutcome);
    if (openAi && (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey) || string.IsNullOrWhiteSpace(options.OpenAi.ModelId)))
        throw new InvalidOperationException("OpenAI intent classification requires a server-side API key and model ID.");
    if (options.OpenAi.TimeoutSeconds is < 1 or > 60)
        throw new InvalidOperationException("OpenAI intent timeout must be between 1 and 60 seconds.");
}

static IntentOutcome ParseMockOutcome(string value) => value?.Trim().ToLowerInvariant() switch
{
    "matched" => IntentOutcome.Matched,
    "irrelevant" => IntentOutcome.Irrelevant,
    "low_confidence" => IntentOutcome.LowConfidence,
    _ => throw new InvalidOperationException("Intent:MockOutcome must be matched, irrelevant, or low_confidence."),
};

public partial class Program
{
}
