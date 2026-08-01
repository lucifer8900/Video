using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lingmai.RedMist.Generation.Providers;

public sealed class Flow2ApiVideoProviderOptions
{
    public Uri Endpoint { get; set; } =
        new("http://127.0.0.1:18001/v1/chat/completions");

    public string ApiKey { get; set; } = string.Empty;

    public string TextToVideoModelId { get; set; } =
        Flow2ApiVideoGenerationProvider.TextToVideoLiteModel;

    public string ImageToVideoModelId { get; set; } =
        Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel;

    public long MaxArtifactBytes { get; set; } = 128L * 1024 * 1024;
}

public sealed partial class Flow2ApiVideoGenerationProvider : IVideoGenerationProvider
{
    public const string TextToVideoLiteModel = "veo_3_1_t2v_lite_landscape";
    public const string ImageToVideoLiteModel = "veo_3_1_i2v_lite_landscape";

    private const string ExpectedHost = "127.0.0.1";
    private const int ExpectedPort = 18001;
    private const string ExpectedPath = "/v1/chat/completions";
    private const string FlowContentHost = "flow-content.google";
    private const string FlowContentPathPrefix = "/video/";
    private const string FlowContentKeyName = "labs-flow-prod-cdn-key";
    private const int MaxInputImageBytes = 16 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _textModelId;
    private readonly string _imageModelId;
    private readonly long _maxArtifactBytes;
    private readonly ConcurrentDictionary<string, ProviderOperation> _operations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);

    public Flow2ApiVideoGenerationProvider(
        HttpClient httpClient,
        Flow2ApiVideoProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        _endpoint = ValidateEndpoint(options.Endpoint);
        _apiKey = ProviderTransport.RequireValue(options.ApiKey, nameof(options.ApiKey));
        _textModelId = RequireExactModel(
            options.TextToVideoModelId,
            TextToVideoLiteModel,
            nameof(options.TextToVideoModelId));
        _imageModelId = RequireExactModel(
            options.ImageToVideoModelId,
            ImageToVideoLiteModel,
            nameof(options.ImageToVideoModelId));
        if (options.MaxArtifactBytes <= 0 || options.MaxArtifactBytes > 256L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options.MaxArtifactBytes));
        _maxArtifactBytes = options.MaxArtifactBytes;
    }

    public async Task<ProviderOperation> StartAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);

        string operationId = "flow2api/" + request.JobId.ToString("N");
        var pending = new ProviderOperation(operationId, ProviderOperationStatus.Pending);
        if (!_operations.TryAdd(operationId, pending))
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.InvalidRequest,
                "The generation request has already been submitted.");

        using var providerCancellation = new CancellationTokenSource();
        if (!_active.TryAdd(operationId, providerCancellation))
            throw ProviderTransport.InvalidResponse();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            providerCancellation.Token);
        _operations[operationId] = pending with { Status = ProviderOperationStatus.Running };

        try
        {
            JsonObject payload = BuildPayload(request);
            using HttpResponseMessage response = await ProviderTransport.SendJsonAsync(
                _httpClient,
                HttpMethod.Post,
                _endpoint,
                _apiKey,
                payload,
                linked.Token).ConfigureAwait(false);
            ProviderTransport.EnsureSuccess(response);
            if (!string.Equals(
                    response.Content?.Headers.ContentType?.MediaType,
                    "text/event-stream",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw ProviderTransport.InvalidResponse();
            }

            Uri artifactUri = await ReadFinalArtifactUriAsync(response, linked.Token)
                .ConfigureAwait(false);
            ProviderArtifact artifact = await InspectArtifactAsync(
                    artifactUri,
                    operationId,
                    destination: null,
                    linked.Token)
                .ConfigureAwait(false);
            var completed = new ProviderOperation(
                operationId,
                ProviderOperationStatus.Succeeded,
                artifact);
            _operations[operationId] = completed;
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _operations[operationId] = pending with { Status = ProviderOperationStatus.Cancelled };
            throw;
        }
        catch (OperationCanceledException) when (providerCancellation.IsCancellationRequested)
        {
            var cancelled = pending with { Status = ProviderOperationStatus.Cancelled };
            _operations[operationId] = cancelled;
            return cancelled;
        }
        catch (GenerationProviderException)
        {
            _operations[operationId] = pending with
            {
                Status = ProviderOperationStatus.Failed,
                FailureCode = GenerationProviderErrorCodes.Unavailable,
            };
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            _operations[operationId] = pending with
            {
                Status = ProviderOperationStatus.Failed,
                FailureCode = GenerationProviderErrorCodes.InvalidResponse,
            };
            throw ProviderTransport.InvalidResponse();
        }
        finally
        {
            _active.TryRemove(operationId, out _);
        }
    }

    public Task<ProviderOperation> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(operationId) ||
            !_operations.TryGetValue(operationId, out ProviderOperation? operation))
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.OperationNotFound,
                "The provider operation was not found.");
        }

        return Task.FromResult(operation);
    }

    public Task CancelAsync(string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(operationId) || !_operations.ContainsKey(operationId))
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.OperationNotFound,
                "The provider operation was not found.");

        if (_active.TryGetValue(operationId, out CancellationTokenSource? active))
            active.Cancel();
        if (_operations.TryGetValue(operationId, out ProviderOperation? operation) &&
            operation.Status is ProviderOperationStatus.Pending or ProviderOperationStatus.Running)
        {
            _operations[operationId] = operation with { Status = ProviderOperationStatus.Cancelled };
        }

        return Task.CompletedTask;
    }

    public async Task DownloadAsync(
        ProviderArtifact artifact,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        bool known = _operations.Values.Any(operation =>
            operation.Artifact is not null &&
            string.Equals(operation.Artifact.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal) &&
            string.Equals(operation.Artifact.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase) &&
            operation.Artifact.Length == artifact.Length &&
            operation.Artifact.DownloadUri == artifact.DownloadUri);
        if (!known)
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.UnsafeDownload,
                "The provider artifact location is not allowed.");

        await using var buffer = new MemoryStream(
            artifact.Length is > 0 and <= int.MaxValue ? (int)artifact.Length : 0);
        ProviderArtifact downloaded = await InspectArtifactAsync(
                ValidateArtifactUri(artifact.DownloadUri),
                artifact.ArtifactId,
                buffer,
                cancellationToken)
            .ConfigureAwait(false);
        if (downloaded.Length != artifact.Length ||
            !string.Equals(downloaded.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw ProviderTransport.InvalidResponse();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject BuildPayload(VideoGenerationRequest request)
    {
        JsonNode content;
        string model;
        if (request.InputImages.Count == 0)
        {
            model = _textModelId;
            content = JsonValue.Create(request.Prompt)!;
        }
        else
        {
            model = _imageModelId;
            VideoGenerationInputImage image = request.InputImages[0];
            content = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = request.Prompt,
                },
                new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject
                    {
                        ["url"] = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}",
                    },
                },
            };
        }

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = content,
                },
            },
            ["stream"] = true,
        };
    }

    private static void ValidateRequest(VideoGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) ||
            !string.Equals(request.AspectRatio, "16:9", StringComparison.Ordinal) ||
            request.DurationSeconds != 8 ||
            request.InputImages is null ||
            request.InputImages.Count > 1)
        {
            throw new GenerationProviderException(
                GenerationProviderErrorCodes.InvalidRequest,
                "The Flow2API video request is invalid.");
        }

        foreach (VideoGenerationInputImage image in request.InputImages)
        {
            if (image.Bytes is null || image.Bytes.Length == 0 || image.Bytes.Length > MaxInputImageBytes ||
                image.ContentType is not ("image/png" or "image/jpeg") ||
                !HasValidImageSignature(image.ContentType, image.Bytes) ||
                !string.Equals(Hash(image.Bytes), image.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new GenerationProviderException(
                    GenerationProviderErrorCodes.InvalidRequest,
                    "The Flow2API input image is invalid.");
            }
        }
    }

    private async Task<Uri> ReadFinalArtifactUriAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 8192,
            leaveOpen: false);
        var content = new StringBuilder();
        var directUrls = new HashSet<string>(StringComparer.Ordinal);
        bool completed = false;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 || line.StartsWith(':')) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                throw ProviderTransport.InvalidResponse();
            string data = line[5..].TrimStart();
            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                if (completed) throw ProviderTransport.InvalidResponse();
                completed = true;
                continue;
            }
            if (completed) throw ProviderTransport.InvalidResponse();

            using JsonDocument document = ProviderTransport.ParseDocument(data);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out _))
                throw new GenerationProviderException(
                    GenerationProviderErrorCodes.Unavailable,
                    "The generation provider rejected the operation.",
                    isTransient: true);
            AppendStructuredArtifactUrls(root, directUrls);

            if (!root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (JsonElement choice in choices.EnumerateArray())
            {
                AppendContent(choice, "delta", content);
                AppendContent(choice, "message", content);
            }
        }

        if (!completed) throw ProviderTransport.InvalidResponse();
        string contentText = WebUtility.HtmlDecode(content.ToString());
        foreach (Match match in VideoUrlRegex().Matches(contentText))
            directUrls.Add(WebUtility.HtmlDecode(match.Groups[1].Value));
        foreach (Match match in FlowContentUrlRegex().Matches(contentText))
        {
            string candidate = match.Groups[1].Value;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? candidateUri) &&
                IsApprovedFlowContentArtifact(candidateUri))
            {
                directUrls.Add(candidate);
            }
        }
        try
        {
            using JsonDocument embedded = JsonDocument.Parse(contentText);
            AppendStructuredArtifactUrls(embedded.RootElement, directUrls);
        }
        catch (JsonException)
        {
            // Choice content may be prose or markup; the strict URL extractors above handle those forms.
        }
        if (directUrls.Count != 1 ||
            !Uri.TryCreate(directUrls.Single(), UriKind.Absolute, out Uri? artifactUri))
        {
            throw ProviderTransport.InvalidResponse();
        }

        return ValidateArtifactUri(artifactUri);
    }

    private static void AppendStructuredArtifactUrls(
        JsonElement root,
        ISet<string> destination)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        AppendStringProperty(root, "url", destination);
        if (root.TryGetProperty("generated_assets", out JsonElement assets) &&
            assets.ValueKind == JsonValueKind.Object)
        {
            AppendStringProperty(assets, "final_video_url", destination);
        }
    }

    private static void AppendStringProperty(
        JsonElement container,
        string property,
        ISet<string> destination)
    {
        if (container.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: > 0 } uri)
        {
            destination.Add(uri);
        }
    }

    private static void AppendContent(JsonElement choice, string property, StringBuilder destination)
    {
        if (!choice.TryGetProperty(property, out JsonElement container) ||
            container.ValueKind != JsonValueKind.Object ||
            !container.TryGetProperty("content", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        destination.Append(value.GetString());
    }

    private async Task<ProviderArtifact> InspectArtifactAsync(
        Uri source,
        string artifactId,
        Stream? destination,
        CancellationToken cancellationToken)
    {
        source = ValidateArtifactUri(source);
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
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new GenerationProviderException(
                    GenerationProviderErrorCodes.UnsafeDownload,
                    "The provider artifact location is not allowed.");
            ProviderTransport.EnsureSuccess(response);
            if (response.Content.Headers.ContentLength is long declared && declared > _maxArtifactBytes)
                throw new GenerationProviderException(
                    GenerationProviderErrorCodes.DownloadTooLarge,
                    "The provider artifact exceeds the configured limit.");

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] ioBuffer = new byte[81920];
            byte[] signature = new byte[12];
            int signatureLength = 0;
            long total = 0;
            while (true)
            {
                int read = await body.ReadAsync(ioBuffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > _maxArtifactBytes)
                    throw new GenerationProviderException(
                        GenerationProviderErrorCodes.DownloadTooLarge,
                        "The provider artifact exceeds the configured limit.");
                if (signatureLength < signature.Length)
                {
                    int copy = Math.Min(read, signature.Length - signatureLength);
                    Array.Copy(ioBuffer, 0, signature, signatureLength, copy);
                    signatureLength += copy;
                }
                hash.AppendData(ioBuffer, 0, read);
                if (destination is not null)
                    await destination.WriteAsync(ioBuffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
            }

            if (!HasMp4Signature(signature.AsSpan(0, signatureLength)))
                throw ProviderTransport.InvalidResponse();
            if (response.Content.Headers.ContentLength is long length && length != total)
                throw ProviderTransport.InvalidResponse();
            string sha256 = ProviderTransport.FormatHash(hash.GetHashAndReset());
            return new ProviderArtifact(artifactId, "video/mp4", total, sha256, source);
        }
    }

    private static Uri ValidateEndpoint(Uri? endpoint)
    {
        if (endpoint is not { IsAbsoluteUri: true } ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(endpoint.Host, ExpectedHost, StringComparison.Ordinal) ||
            endpoint.Port != ExpectedPort ||
            !string.Equals(endpoint.AbsolutePath, ExpectedPath, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "The exact Flow2API loopback tunnel endpoint is required.",
                nameof(endpoint));
        }

        return endpoint;
    }

    private static Uri ValidateArtifactUri(Uri? source)
    {
        if (source is { IsAbsoluteUri: true } &&
            (IsApprovedLoopbackArtifact(source) || IsApprovedFlowContentArtifact(source)))
        {
            return source;
        }

        throw new GenerationProviderException(
            GenerationProviderErrorCodes.UnsafeDownload,
            "The provider artifact location is not allowed.");
    }

    private static bool IsApprovedLoopbackArtifact(Uri source) =>
        string.Equals(source.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(source.Host, ExpectedHost, StringComparison.Ordinal) &&
        source.Port == ExpectedPort &&
        source.AbsolutePath.StartsWith("/tmp/", StringComparison.Ordinal) &&
        source.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(source.UserInfo) &&
        string.IsNullOrEmpty(source.Query) &&
        string.IsNullOrEmpty(source.Fragment);

    private static bool IsApprovedFlowContentArtifact(Uri source)
    {
        if (!string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.Host, FlowContentHost, StringComparison.OrdinalIgnoreCase) ||
            !source.IsDefaultPort ||
            !string.IsNullOrEmpty(source.UserInfo) ||
            !string.IsNullOrEmpty(source.Fragment) ||
            !source.AbsolutePath.StartsWith(FlowContentPathPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(source.AbsolutePath[FlowContentPathPrefix.Length..], "D", out _))
        {
            return false;
        }

        string query = source.Query;
        if (query.Length <= 1) return false;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string segment in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = segment.IndexOf('=');
                if (separator <= 0 || separator == segment.Length - 1) return false;
                string name = Uri.UnescapeDataString(segment[..separator]);
                string value = Uri.UnescapeDataString(segment[(separator + 1)..]);
                if (!parameters.TryAdd(name, value)) return false;
            }
        }
        catch (UriFormatException)
        {
            return false;
        }

        return parameters.Count == 3 &&
               parameters.TryGetValue("Expires", out string? expires) &&
               long.TryParse(expires, out long expiresAt) &&
               expiresAt > 0 &&
               parameters.TryGetValue("KeyName", out string? keyName) &&
               string.Equals(keyName, FlowContentKeyName, StringComparison.Ordinal) &&
               parameters.TryGetValue("Signature", out string? signature) &&
               FlowContentSignatureRegex().IsMatch(signature);
    }

    private static string RequireExactModel(string? value, string expected, string parameterName)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            throw new ArgumentException("The approved Flow2API Lite model is required.", parameterName);
        return expected;
    }

    private static bool HasValidImageSignature(string contentType, byte[] bytes) =>
        contentType switch
        {
            "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            _ => false,
        };

    private static bool HasMp4Signature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' &&
        bytes[6] == (byte)'y' && bytes[7] == (byte)'p';

    private static string Hash(byte[] bytes) =>
        ProviderTransport.FormatHash(SHA256.HashData(bytes));

    [GeneratedRegex("<video[^>]+src=['\\\"]([^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase)]
    private static partial Regex VideoUrlRegex();

    [GeneratedRegex("(https://flow-content\\.google/video/[0-9a-fA-F-]{36}\\?[A-Za-z0-9._~%=&+\\-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlowContentUrlRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{8,512}$", RegexOptions.CultureInvariant)]
    private static partial Regex FlowContentSignatureRegex();
}
