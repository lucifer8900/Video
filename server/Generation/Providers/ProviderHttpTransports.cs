using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lingmai.RedMist.Generation.Providers;

public sealed class VeoProviderOptions
{
    public Uri Endpoint { get; set; } = new("https://localhost/");

    public string ApiKey { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;
}

public sealed class OpenAiImageProviderOptions
{
    public Uri Endpoint { get; set; } = new("https://localhost/v1/images/generations");

    public string ApiKey { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;
}

public sealed class OpenAiTextProviderOptions
{
    public Uri Endpoint { get; set; } = new("https://localhost/v1/responses");

    public string ApiKey { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;
}

public sealed class VeoVideoGenerationProvider : IVideoGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _modelId;

    public VeoVideoGenerationProvider(HttpClient httpClient, VeoProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        _endpoint = ProviderTransport.ValidateEndpoint(options.Endpoint, nameof(options));
        _apiKey = ProviderTransport.RequireValue(options.ApiKey, nameof(options.ApiKey));
        _modelId = ProviderTransport.RequireValue(options.ModelId, nameof(options.ModelId));
    }

    public async Task<ProviderOperation> StartAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = new JsonObject
        {
            ["model"] = _modelId,
            ["prompt"] = request.Prompt,
            ["seed"] = request.Seed,
            ["aspectRatio"] = request.AspectRatio,
            ["durationSeconds"] = request.DurationSeconds,
            ["inputHash"] = request.InputHash,
        };
        using HttpResponseMessage response = await ProviderTransport.SendJsonAsync(
            _httpClient,
            HttpMethod.Post,
            _endpoint,
            _apiKey,
            payload,
            cancellationToken).ConfigureAwait(false);
        string body = await ProviderTransport.ReadSuccessfulJsonAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return ParseOperation(body, ProviderOperationStatus.Pending);
    }

    public async Task<ProviderOperation> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        Uri endpoint = OperationUri(operationId, cancel: false);
        using HttpResponseMessage response = await ProviderTransport.SendJsonAsync(
            _httpClient,
            HttpMethod.Get,
            endpoint,
            _apiKey,
            payload: null,
            cancellationToken).ConfigureAwait(false);
        string body = await ProviderTransport.ReadSuccessfulJsonAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return ParseOperation(body, ProviderOperationStatus.Running);
    }

    public async Task CancelAsync(string operationId, CancellationToken cancellationToken)
    {
        Uri endpoint = OperationUri(operationId, cancel: true);
        using HttpResponseMessage response = await ProviderTransport.SendJsonAsync(
            _httpClient,
            HttpMethod.Post,
            endpoint,
            _apiKey,
            new JsonObject(),
            cancellationToken).ConfigureAwait(false);
        _ = await ProviderTransport.ReadSuccessfulJsonAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DownloadAsync(
        ProviderArtifact artifact,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(destination);
        if (artifact.DownloadUri is not { IsAbsoluteUri: true } source ||
            !string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(source.UserInfo) ||
            !string.Equals(source.Host, _endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.UnsafeDownload,
                "The provider artifact location is not allowed.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            throw ProviderTransport.Unavailable();
        }

        using (response)
        {
            ProviderTransport.EnsureSuccess(response);
            if (response.Content.Headers.ContentLength is long declared &&
                artifact.Length >= 0 && declared != artifact.Length)
            {
                throw ProviderTransport.InvalidResponse();
            }

            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (artifact.Length >= 0 && total > artifact.Length)
                    throw ProviderTransport.InvalidResponse();
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            string actualHash = ProviderTransport.FormatHash(hash.GetHashAndReset());
            if (total != artifact.Length ||
                !string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw ProviderTransport.InvalidResponse();
            }
        }
    }

    private Uri OperationUri(string operationId, bool cancel)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            operationId.Contains("..", StringComparison.Ordinal) ||
            operationId.Contains('?', StringComparison.Ordinal) ||
            operationId.Contains('#', StringComparison.Ordinal))
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.OperationNotFound,
                "The provider operation was not found.");
        }

        string path = operationId.TrimStart('/');
        if (cancel) path += ":cancel";
        return new Uri($"{_endpoint.Scheme}://{_endpoint.Authority}/{path}", UriKind.Absolute);
    }

    private static ProviderOperation ParseOperation(
        string body,
        ProviderOperationStatus incompleteStatus)
    {
        try
        {
            using JsonDocument document = ProviderTransport.ParseDocument(body);
            JsonElement root = document.RootElement;
            string operationId = root.GetProperty("name").GetString()
                ?? throw ProviderTransport.InvalidResponse();
            bool done = root.TryGetProperty("done", out JsonElement doneElement) &&
                        doneElement.ValueKind == JsonValueKind.True;
            if (!done) return new ProviderOperation(operationId, incompleteStatus);

            if (root.TryGetProperty("error", out JsonElement error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                return new ProviderOperation(
                    operationId,
                    ProviderOperationStatus.Failed,
                    FailureCode: GenerationProviderErrorCodes.Unavailable);
            }

            JsonElement artifactElement = root.TryGetProperty("artifact", out JsonElement direct)
                ? direct
                : root.GetProperty("response").GetProperty("artifact");
            ProviderArtifact artifact = ProviderTransport.ParseArtifact(artifactElement, operationId);
            return new ProviderOperation(operationId, ProviderOperationStatus.Succeeded, artifact);
        }
        catch (GenerationProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw ProviderTransport.InvalidResponse();
        }
    }
}

public sealed class OpenAiImageGenerationProvider : IImageGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _modelId;

    public OpenAiImageGenerationProvider(HttpClient httpClient, OpenAiImageProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        _endpoint = ProviderTransport.ValidateEndpoint(options.Endpoint, nameof(options));
        _apiKey = ProviderTransport.RequireValue(options.ApiKey, nameof(options.ApiKey));
        _modelId = ProviderTransport.RequireValue(options.ModelId, nameof(options.ModelId));
    }

    public async Task<ProviderArtifact> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = new JsonObject
        {
            ["model"] = _modelId,
            ["prompt"] = request.Prompt,
            ["seed"] = request.Seed,
            ["size"] = $"{request.Width}x{request.Height}",
            ["input_hash"] = request.InputHash,
        };
        using HttpResponseMessage response = await ProviderTransport.SendJsonAsync(
            _httpClient,
            HttpMethod.Post,
            _endpoint,
            _apiKey,
            payload,
            cancellationToken).ConfigureAwait(false);
        string body = await ProviderTransport.ReadSuccessfulJsonAsync(response, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using JsonDocument document = ProviderTransport.ParseDocument(body);
            JsonElement data = document.RootElement.GetProperty("data");
            JsonElement artifact = data.EnumerateArray().Single();
            return ProviderTransport.ParseArtifact(artifact, request.JobId.ToString("N"));
        }
        catch (GenerationProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw ProviderTransport.InvalidResponse();
        }
    }
}

public sealed class OpenAiTextGenerationProvider : ITextGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _modelId;

    public OpenAiTextGenerationProvider(HttpClient httpClient, OpenAiTextProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        _endpoint = ProviderTransport.ValidateEndpoint(options.Endpoint, nameof(options));
        _apiKey = ProviderTransport.RequireValue(options.ApiKey, nameof(options.ApiKey));
        _modelId = ProviderTransport.RequireValue(options.ModelId, nameof(options.ModelId));
    }

    public async Task<StructuredTextResult> GenerateAsync(
        TextGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        JsonNode schema = JsonNode.Parse(request.OutputSchema.GetRawText())
            ?? throw ProviderTransport.InvalidResponse();
        var payload = new JsonObject
        {
            ["model"] = _modelId,
            ["store"] = false,
            ["input"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.Instruction },
                new JsonObject { ["role"] = "user", ["content"] = request.Input.GetRawText() },
            },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "generation_result",
                    ["strict"] = true,
                    ["schema"] = schema,
                },
            },
        };
        using HttpResponseMessage response = await ProviderTransport.SendJsonAsync(
            _httpClient,
            HttpMethod.Post,
            _endpoint,
            _apiKey,
            payload,
            cancellationToken).ConfigureAwait(false);
        string body = await ProviderTransport.ReadSuccessfulJsonAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return ParseStructuredResult(body, request.OutputSchema);
    }

    private static StructuredTextResult ParseStructuredResult(string responseBody, JsonElement schema)
    {
        try
        {
            using JsonDocument envelope = ProviderTransport.ParseDocument(responseBody);
            string? text = null;
            foreach (JsonElement item in envelope.RootElement.GetProperty("output").EnumerateArray())
            {
                if (!item.TryGetProperty("type", out JsonElement itemType) ||
                    !string.Equals(itemType.GetString(), "message", StringComparison.Ordinal)) continue;
                foreach (JsonElement part in item.GetProperty("content").EnumerateArray())
                {
                    if (!part.TryGetProperty("type", out JsonElement partType) ||
                        !string.Equals(partType.GetString(), "output_text", StringComparison.Ordinal) ||
                        text is not null)
                    {
                        throw ProviderTransport.InvalidResponse();
                    }

                    text = part.GetProperty("text").GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(text)) throw ProviderTransport.InvalidResponse();
            using JsonDocument output = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            if (!StrictJsonSchemaValidator.IsValid(output.RootElement, schema))
                throw ProviderTransport.InvalidResponse();
            JsonElement clone = output.RootElement.Clone();
            string hash = ProviderTransport.FormatHash(SHA256.HashData(Encoding.UTF8.GetBytes(clone.GetRawText())));
            return new StructuredTextResult(clone, hash);
        }
        catch (GenerationProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw ProviderTransport.InvalidResponse();
        }
    }
}

internal static class ProviderTransport
{
    private const string InvalidResponseMessage = "The generation provider returned an invalid response.";
    private const string UnavailableMessage = "The generation provider is unavailable.";

    public static string RequireValue(string? value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A server-side provider setting is required.", parameterName);

    public static Uri ValidateEndpoint(Uri? endpoint, string parameterName)
    {
        if (endpoint is not { IsAbsoluteUri: true } ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new ArgumentException("An absolute HTTPS provider endpoint is required.", parameterName);
        }

        return endpoint;
    }

    public static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient httpClient,
        HttpMethod method,
        Uri endpoint,
        string apiKey,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (payload is not null)
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            throw Unavailable();
        }
    }

    public static async Task<string> ReadSuccessfulJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        EnsureSuccess(response);
        try
        {
            return response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            throw Unavailable();
        }
    }

    public static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.OperationNotFound,
                "The provider operation was not found.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.AuthenticationFailed,
                "The generation provider rejected server credentials.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.InvalidRequest,
                "The generation provider rejected the request.");
        throw Unavailable();
    }

    public static JsonDocument ParseDocument(string json) => JsonDocument.Parse(json, new JsonDocumentOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    });

    public static ProviderArtifact ParseArtifact(JsonElement element, string fallbackId)
    {
        string? url = TryString(element, "url") ?? TryString(element, "uri");
        Uri? downloadUri = url is null ? null : new Uri(url, UriKind.Absolute);
        string contentType = TryString(element, "content_type") ??
                             TryString(element, "mimeType") ??
                             "application/octet-stream";
        long length = TryInt64(element, "size_bytes") ?? TryInt64(element, "sizeBytes") ?? 0;
        string sha256 = TryString(element, "sha256") ?? string.Empty;
        string artifactId = TryString(element, "id") ?? fallbackId;
        if (downloadUri is null || length < 0 || string.IsNullOrWhiteSpace(sha256))
            throw InvalidResponse();
        return new ProviderArtifact(artifactId, contentType, length, sha256, downloadUri);
    }

    public static GenerationProviderException InvalidResponse() =>
        new(GenerationProviderErrorCodes.InvalidResponse, InvalidResponseMessage);

    public static GenerationProviderException Unavailable() =>
        new(GenerationProviderErrorCodes.Unavailable, UnavailableMessage, isTransient: true);

    public static string FormatHash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();

    private static string? TryString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? TryInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long number)
            ? number
            : null;
}

internal static class StrictJsonSchemaValidator
{
    public static bool IsValid(JsonElement instance, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return false;
        if (schema.TryGetProperty("enum", out JsonElement allowed) &&
            allowed.ValueKind == JsonValueKind.Array &&
            !allowed.EnumerateArray().Any(value =>
                string.Equals(value.GetRawText(), instance.GetRawText(), StringComparison.Ordinal)))
        {
            return false;
        }

        if (schema.TryGetProperty("type", out JsonElement type) &&
            type.ValueKind == JsonValueKind.String &&
            !MatchesType(instance, type.GetString()))
        {
            return false;
        }

        return instance.ValueKind switch
        {
            JsonValueKind.Object => ValidateObject(instance, schema),
            JsonValueKind.Array => ValidateArray(instance, schema),
            _ => true,
        };
    }

    private static bool ValidateObject(JsonElement instance, JsonElement schema)
    {
        var required = schema.TryGetProperty("required", out JsonElement requiredElement) &&
                       requiredElement.ValueKind == JsonValueKind.Array
            ? requiredElement.EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => value is not null)
                .Select(value => value!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        JsonElement properties = schema.TryGetProperty("properties", out JsonElement propertySchemas)
            ? propertySchemas
            : default;
        bool rejectExtras = schema.TryGetProperty("additionalProperties", out JsonElement additional) &&
                            additional.ValueKind == JsonValueKind.False;

        foreach (JsonProperty property in instance.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return false;
            if (properties.ValueKind == JsonValueKind.Object &&
                properties.TryGetProperty(property.Name, out JsonElement propertySchema))
            {
                if (!IsValid(property.Value, propertySchema)) return false;
            }
            else if (rejectExtras)
            {
                return false;
            }
        }

        return required.All(name => name is not null && seen.Contains(name));
    }

    private static bool ValidateArray(JsonElement instance, JsonElement schema)
    {
        if (!schema.TryGetProperty("items", out JsonElement itemSchema)) return true;
        return instance.EnumerateArray().All(item => IsValid(item, itemSchema));
    }

    private static bool MatchesType(JsonElement instance, string? type) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => false,
    };
}
