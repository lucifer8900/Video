using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lingmai.RedMist.Api.Asr;

public sealed class OpenAiAsrProvider : IAsrProvider
{
    private const string SanitizedFailureMessage = "The transcription provider is unavailable.";

    private readonly HttpClient _httpClient;
    private readonly OpenAiAsrOptions _options;

    public OpenAiAsrProvider(HttpClient httpClient, OpenAiAsrOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options;
    }

    public async Task<AsrProviderResult> TranscribeAsync(
        AsrAudioInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        byte[] audioBytes;
        using (var buffer = new MemoryStream())
        {
            await input.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            audioBytes = buffer.ToArray();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var multipart = new MultipartFormDataContent();
        using var file = new ByteArrayContent(audioBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(input.ContentType);
        multipart.Add(file, "file", input.FileName);
        multipart.Add(new StringContent(_options.ModelId, Encoding.UTF8), "model");
        multipart.Add(new StringContent("zh", Encoding.UTF8), "language");
        multipart.Add(new StringContent("json", Encoding.UTF8), "response_format");
        request.Content = multipart;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw Unavailable();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw Unavailable();

            string json;
            try
            {
                json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
            {
                throw Unavailable();
            }

            if (string.IsNullOrWhiteSpace(json)) throw Unavailable();

            string text;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("text", out JsonElement textElement) ||
                    textElement.ValueKind != JsonValueKind.String)
                {
                    throw Unavailable();
                }

                text = textElement.GetString()?.Trim() ?? string.Empty;
            }
            catch (JsonException)
            {
                throw Unavailable();
            }

            if (text.Length == 0) throw Unavailable();
            return new AsrProviderResult(text, "zh", input.DurationSeconds);
        }
    }

    private static AsrProviderException Unavailable() =>
        new(AsrErrorCodes.ProviderUnavailable, SanitizedFailureMessage);
}
