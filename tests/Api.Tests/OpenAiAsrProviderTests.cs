using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Lingmai.RedMist.Api.Asr;

namespace Lingmai.RedMist.Api.Tests;

public sealed class OpenAiAsrProviderTests
{
    [Fact]
    public async Task SendsExactlyOneMultipartTranscriptionRequestAndParsesText()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"text\":\"山门回声\"}", Encoding.UTF8, "application/json"),
        });
        var options = Options();
        var provider = new OpenAiAsrProvider(new HttpClient(handler), options);
        byte[] audio = WavFixture.CreatePcm(durationSeconds: 1d);
        await using var stream = new MemoryStream(audio);
        var input = new AsrAudioInput(stream, "voice.wav", "audio/wav", audio.Length, 16_000, 1, 1d);

        AsrProviderResult result = await provider.TranscribeAsync(input, CancellationToken.None);

        Assert.Equal("山门回声", result.Text);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal(options.Endpoint, handler.RequestUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(options.ApiKey, handler.Authorization?.Parameter);
        Assert.Equal("multipart/form-data", handler.ContentType?.MediaType);
        Assert.Equal(options.ModelId, handler.FormFields["model"]);
        Assert.Equal("json", handler.FormFields["response_format"]);
        Assert.Equal("file", handler.FileFieldName);
        Assert.Equal("voice.wav", handler.FileName);
        Assert.Equal("audio/wav", handler.FileContentType);
        Assert.Equal(audio, handler.FileBytes);
    }

    [Fact]
    public async Task ProviderErrorIsSanitizedAndNeverRetried()
    {
        const string privateProviderBody = "private provider diagnostic";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(privateProviderBody),
        });
        var options = Options();
        var provider = new OpenAiAsrProvider(new HttpClient(handler), options);
        byte[] audio = WavFixture.CreatePcm();
        await using var stream = new MemoryStream(audio);
        var input = new AsrAudioInput(stream, "voice.wav", "audio/wav", audio.Length, 16_000, 1, 1d);

        AsrProviderException error = await Assert.ThrowsAsync<AsrProviderException>(
            () => provider.TranscribeAsync(input, CancellationToken.None));

        Assert.Equal(AsrErrorCodes.ProviderUnavailable, error.Code);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(options.ApiKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(privateProviderBody, error.Message, StringComparison.Ordinal);
    }

    private static OpenAiAsrOptions Options() => new()
    {
        Endpoint = new Uri("https://api.openai.test/v1/audio/transcriptions"),
        ApiKey = "unit-test-api-key",
        ModelId = "unit-test-transcribe-model",
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public int CallCount { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public MediaTypeHeaderValue? ContentType { get; private set; }
        public Dictionary<string, string> FormFields { get; } = new(StringComparer.Ordinal);
        public string? FileFieldName { get; private set; }
        public string? FileName { get; private set; }
        public string? FileContentType { get; private set; }
        public byte[]? FileBytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            ContentType = request.Content?.Headers.ContentType;

            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            foreach (HttpContent part in multipart)
            {
                ContentDispositionHeaderValue disposition = Assert.IsType<ContentDispositionHeaderValue>(part.Headers.ContentDisposition);
                string name = Unquote(disposition.Name);
                if (!string.IsNullOrEmpty(disposition.FileName))
                {
                    FileFieldName = name;
                    FileName = Unquote(disposition.FileName);
                    FileContentType = part.Headers.ContentType?.MediaType;
                    FileBytes = await part.ReadAsByteArrayAsync(cancellationToken);
                }
                else
                {
                    FormFields[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            }

            return _response(request);
        }

        private static string Unquote(string? value) => value?.Trim('"') ?? string.Empty;
    }
}
