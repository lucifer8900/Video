using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class OpenAiIntentClassifierTests
{
    [Fact]
    public async Task SendsOneStrictSchemaRequestAndAcceptsAnAllowedIntent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ResponseFor(
            "{\"outcome\":\"matched\",\"intentId\":\"intent.help\",\"confidence\":0.91}"));
        IntentOptions options = Options();
        var classifier = new OpenAiIntentClassifier(new HttpClient(handler), options.OpenAi);

        IntentDecision result = await classifier.ClassifyAsync(Context(), CancellationToken.None);

        Assert.Equal(IntentOutcome.Matched, result.Outcome);
        Assert.Equal("intent.help", result.IntentId);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(options.OpenAi.Endpoint, handler.Uri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(options.OpenAi.ApiKey, handler.Authorization?.Parameter);

        using JsonDocument request = JsonDocument.Parse(handler.Body!);
        JsonElement root = request.RootElement;
        Assert.Equal(options.OpenAi.ModelId, root.GetProperty("model").GetString());
        JsonElement format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        JsonElement schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["confidence", "intentId", "outcome"],
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Order().ToArray());
        string schemaJson = schema.GetRawText();
        Assert.Contains("intent.help", schemaJson, StringComparison.Ordinal);
        Assert.Contains("intent.refuse", schemaJson, StringComparison.Ordinal);
        Assert.Contains("low_confidence", schemaJson, StringComparison.Ordinal);
        Assert.Contains("irrelevant", schemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("effects", schemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("state", schemaJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(UntrustedOutputs))]
    public async Task RejectsMalformedExtraFieldAndUnknownIntentOutputs(string outputText)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ResponseFor(outputText));
        var classifier = new OpenAiIntentClassifier(new HttpClient(handler), Options().OpenAi);

        IntentProviderException error = await Assert.ThrowsAsync<IntentProviderException>(
            () => classifier.ClassifyAsync(Context(), CancellationToken.None));

        Assert.Equal(IntentErrorCodes.InvalidProviderResponse, error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProviderErrorIsSanitizedAndNeverRetried()
    {
        const string privateBody = "private provider details";
        IntentOptions options = Options();
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, privateBody);
        var classifier = new OpenAiIntentClassifier(new HttpClient(handler), options.OpenAi);

        IntentProviderException error = await Assert.ThrowsAsync<IntentProviderException>(
            () => classifier.ClassifyAsync(Context(), CancellationToken.None));

        Assert.Equal(IntentErrorCodes.ProviderUnavailable, error.Code);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(options.OpenAi.ApiKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(privateBody, error.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> UntrustedOutputs()
    {
        yield return ["not-json"];
        yield return ["{\"outcome\":\"matched\",\"intentId\":\"intent.help\",\"confidence\":0.9,\"effects\":[]}"];
        yield return ["{\"outcome\":\"matched\",\"intentId\":\"intent.invented\",\"confidence\":0.99}"];
        yield return ["{\"outcome\":\"matched\",\"intentId\":\"intent.help\",\"confidence\":1.1}"];
    }

    private static IntentClassificationContext Context() => new(
        "我愿意帮忙",
        [
            new IntentCandidate("intent.help", ["帮忙"], 0.7d),
            new IntentCandidate("intent.refuse", ["拒绝"], 0.7d),
        ]);

    private static IntentOptions Options() => new()
    {
        Provider = "OpenAI",
        OpenAi = new OpenAiIntentOptions
        {
            Endpoint = new Uri("https://api.openai.test/v1/responses"),
            ApiKey = "unit-test-intent-key",
            ModelId = "unit-test-intent-model",
        },
    };

    private static string ResponseFor(string outputText)
    {
        string encoded = JsonSerializer.Serialize(outputText);
        return "{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":" + encoded + "}]}]}";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public RecordingHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
