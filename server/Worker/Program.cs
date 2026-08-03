using Lingmai.RedMist.Generation.Providers;
using Lingmai.RedMist.Worker;

var builder = Host.CreateApplicationBuilder(args);
GenerationProviderRuntimeSettings providerSettings =
    GenerationProviderRuntimeSettings.FromConfiguration(builder.Configuration);
BudgetRuntimeSettings budgetSettings =
    BudgetRuntimeSettings.FromConfiguration(builder.Configuration);
budgetSettings.Validate(providerSettings.UsesPaidNetworkProvider);
providerSettings.Validate();
builder.Services.AddMediaPipeline(builder.Configuration);

builder.Services.AddSingleton(budgetSettings);
builder.Services.AddSingleton(providerSettings);
builder.Services.AddSingleton<IVideoGenerationProvider>(_ =>
    ProviderBootstrap.CreateVideo(providerSettings));
builder.Services.AddSingleton<IImageGenerationProvider>(_ =>
    ProviderBootstrap.CreateImage(providerSettings));
builder.Services.AddSingleton<ITextGenerationProvider>(_ =>
    ProviderBootstrap.CreateText(providerSettings));

using var host = builder.Build();
await host.RunAsync();

internal sealed record GenerationProviderSelection(
    string Mode,
    string Endpoint,
    string ModelId,
    string CredentialEnvironmentVariable);

internal sealed record GenerationProviderRuntimeSettings(
    bool NetworkEnabled,
    GenerationProviderSelection Video,
    GenerationProviderSelection Image,
    GenerationProviderSelection Text)
{
    public bool UsesPaidNetworkProvider =>
        string.Equals(Video.Mode, "Veo", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Video.Mode, "Flow2API", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Image.Mode, "OpenAI", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Text.Mode, "OpenAI", StringComparison.OrdinalIgnoreCase);

    public static GenerationProviderRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection root = configuration.GetRequiredSection("GenerationProviders");
        return new GenerationProviderRuntimeSettings(
            root.GetValue<bool>("NetworkEnabled"),
            ReadSelection(root.GetRequiredSection("Video")),
            ReadSelection(root.GetRequiredSection("Image")),
            ReadSelection(root.GetRequiredSection("Text")));
    }

    public void Validate()
    {
        ValidateMode(Video, "Video", ["ManualPrompt", "Mock", "Veo", "Flow2API"]);
        ValidateMode(Image, "Image", ["Mock", "OpenAI"]);
        ValidateMode(Text, "Text", ["Mock", "OpenAI"]);

        bool usesNetwork = UsesPaidNetworkProvider;
        if (usesNetwork && !NetworkEnabled)
            throw new InvalidOperationException(
                "Generation provider network access is disabled. Use ManualPrompt/Mock modes.");

        if (usesNetwork)
        {
            ValidateNetworkSelection(Video, "Video", string.Equals(Video.Mode, "Veo", StringComparison.OrdinalIgnoreCase));
            ValidateFlow2ApiSelection(Video);
            ValidateNetworkSelection(Image, "Image", string.Equals(Image.Mode, "OpenAI", StringComparison.OrdinalIgnoreCase));
            ValidateNetworkSelection(Text, "Text", string.Equals(Text.Mode, "OpenAI", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static GenerationProviderSelection ReadSelection(IConfigurationSection section) => new(
        section["Mode"] ?? string.Empty,
        section["Endpoint"] ?? string.Empty,
        section["ModelId"] ?? string.Empty,
        section["CredentialEnvironmentVariable"] ?? string.Empty);

    private static void ValidateMode(
        GenerationProviderSelection selection,
        string name,
        IReadOnlyList<string> allowed)
    {
        if (!allowed.Contains(selection.Mode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GenerationProviders:{name}:Mode is invalid.");
    }

    private static void ValidateNetworkSelection(
        GenerationProviderSelection selection,
        string name,
        bool active)
    {
        if (!active) return;
        if (!Uri.TryCreate(selection.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(selection.ModelId) ||
            string.IsNullOrWhiteSpace(selection.CredentialEnvironmentVariable))
        {
            throw new InvalidOperationException(
                $"GenerationProviders:{name} requires an HTTPS endpoint, server model ID, and credential environment-variable name.");
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(selection.CredentialEnvironmentVariable)))
            throw new InvalidOperationException(
                $"GenerationProviders:{name} credential environment variable is not set.");
    }

    private static void ValidateFlow2ApiSelection(GenerationProviderSelection selection)
    {
        if (!string.Equals(selection.Mode, "Flow2API", StringComparison.OrdinalIgnoreCase)) return;
        if (!Uri.TryCreate(selection.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(endpoint.Host, "127.0.0.1", StringComparison.Ordinal) ||
            endpoint.Port != 18001 ||
            !string.Equals(endpoint.AbsolutePath, "/v1/chat/completions", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !string.Equals(selection.ModelId, Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel, StringComparison.Ordinal) ||
            !string.Equals(selection.CredentialEnvironmentVariable, "Flow2API", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "GenerationProviders:Video Flow2API mode requires the approved loopback tunnel, Lite I2V model, and Flow2API credential environment-variable name.");
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Flow2API")))
            throw new InvalidOperationException(
                "GenerationProviders:Video credential environment variable is not set.");
    }
}

internal static class ProviderBootstrap
{
    public static IVideoGenerationProvider CreateVideo(GenerationProviderRuntimeSettings settings)
    {
        if (string.Equals(settings.Video.Mode, "Flow2API", StringComparison.OrdinalIgnoreCase))
        {
            return new Flow2ApiVideoGenerationProvider(
                CreateClient(TimeSpan.FromMinutes(26)),
                new Flow2ApiVideoProviderOptions
                {
                    Endpoint = new Uri(settings.Video.Endpoint),
                    ApiKey = ReadCredential(settings.Video),
                    TextToVideoModelId = Flow2ApiVideoGenerationProvider.TextToVideoLiteModel,
                    ImageToVideoModelId = settings.Video.ModelId,
                });
        }
        if (!string.Equals(settings.Video.Mode, "Veo", StringComparison.OrdinalIgnoreCase))
            return new MockVideoGenerationProvider();
        return new VeoVideoGenerationProvider(
            CreateClient(TimeSpan.FromMinutes(2)),
            new VeoProviderOptions
            {
                Endpoint = new Uri(settings.Video.Endpoint),
                ModelId = settings.Video.ModelId,
                ApiKey = ReadCredential(settings.Video),
            });
    }

    public static IImageGenerationProvider CreateImage(GenerationProviderRuntimeSettings settings)
    {
        if (!string.Equals(settings.Image.Mode, "OpenAI", StringComparison.OrdinalIgnoreCase))
            return new MockImageGenerationProvider();
        return new OpenAiImageGenerationProvider(
            CreateClient(TimeSpan.FromMinutes(2)),
            new OpenAiImageProviderOptions
            {
                Endpoint = new Uri(settings.Image.Endpoint),
                ModelId = settings.Image.ModelId,
                ApiKey = ReadCredential(settings.Image),
            });
    }

    public static ITextGenerationProvider CreateText(GenerationProviderRuntimeSettings settings)
    {
        if (!string.Equals(settings.Text.Mode, "OpenAI", StringComparison.OrdinalIgnoreCase))
            return new MockTextGenerationProvider();
        return new OpenAiTextGenerationProvider(
            CreateClient(TimeSpan.FromMinutes(2)),
            new OpenAiTextProviderOptions
            {
                Endpoint = new Uri(settings.Text.Endpoint),
                ModelId = settings.Text.ModelId,
                ApiKey = ReadCredential(settings.Text),
            });
    }

    private static HttpClient CreateClient(TimeSpan timeout) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = timeout,
    };

    private static string ReadCredential(GenerationProviderSelection selection) =>
        Environment.GetEnvironmentVariable(selection.CredentialEnvironmentVariable)
        ?? throw new InvalidOperationException("The generation provider credential is unavailable.");
}
