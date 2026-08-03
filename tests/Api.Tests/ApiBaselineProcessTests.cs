using System.Net;
using System.Text;
using System.Text.Json;

namespace Lingmai.RedMist.Api.Tests;

[Collection(ApiProcessCollection.Name)]
public sealed class ApiBaselineProcessTests
{
    private const string RequestIdHeader = "X-Request-ID";

    [Fact]
    public async Task HealthReturnsExactContractAndRequestId()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();

        using HttpResponseMessage response = await server.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        string requestId = SingleHeader(response, RequestIdHeader);
        using JsonDocument body = await ReadJsonAsync(response);
        JsonElement root = body.RootElement;
        Assert.Equal(
            ["requestId", "schemaVersion", "status"],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal(requestId, root.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task RequestIdPropagatesOnlyWhenItIsSafe()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();
        const string supplied = "cx301-client-request_01";

        using var validRequest = new HttpRequestMessage(HttpMethod.Get, "/missing");
        validRequest.Headers.TryAddWithoutValidation(RequestIdHeader, supplied);
        using HttpResponseMessage validResponse = await server.Client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.NotFound, validResponse.StatusCode);
        Assert.Equal(supplied, SingleHeader(validResponse, RequestIdHeader));
        using (JsonDocument body = await ReadJsonAsync(validResponse))
            Assert.Equal(supplied, body.RootElement.GetProperty("requestId").GetString());

        string unsafeId = new('x', 65);
        using var unsafeRequest = new HttpRequestMessage(HttpMethod.Get, "/missing");
        unsafeRequest.Headers.TryAddWithoutValidation(RequestIdHeader, unsafeId);
        using HttpResponseMessage unsafeResponse = await server.Client.SendAsync(unsafeRequest);
        string replacement = SingleHeader(unsafeResponse, RequestIdHeader);
        Assert.NotEqual(unsafeId, replacement);
        Assert.NotEmpty(replacement);
        using JsonDocument unsafeBody = await ReadJsonAsync(unsafeResponse);
        Assert.Equal(replacement, unsafeBody.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task UnknownRouteAndWrongMethodUseUnifiedErrors()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();

        using HttpResponseMessage notFound = await server.Client.GetAsync("/missing");
        await AssertErrorAsync(notFound, HttpStatusCode.NotFound, "api.not_found");

        using HttpResponseMessage methodNotAllowed = await server.Client.GetAsync(
            "/api/v1/intent/classifications");
        await AssertErrorAsync(
            methodNotAllowed,
            HttpStatusCode.MethodNotAllowed,
            "api.method_not_allowed");
    }

    [Fact]
    public async Task BuiltInRateLimiterReturnsUnified429AndHealthDoesNotConsumePermits()
    {
        var configuration = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ApiBaseline__RateLimit__PermitLimit"] = "3",
            ["ApiBaseline__RateLimit__WindowSeconds"] = "60",
            ["ApiBaseline__RateLimit__QueueLimit"] = "0",
        };
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync(configuration);

        for (var index = 0; index < 5; index++)
        {
            using HttpResponseMessage health = await server.Client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        const string json = "{\"nodeId\":\"prologue\",\"transcript\":\"普通测试表达\"}";
        for (var index = 0; index < 3; index++)
        {
            using HttpResponseMessage allowed = await PostJsonAsync(
                server.Client,
                "/api/v1/intent/classifications",
                json);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using HttpResponseMessage rejected = await PostJsonAsync(
            server.Client,
            "/api/v1/intent/classifications",
            json);
        await AssertErrorAsync(rejected, HttpStatusCode.TooManyRequests, "api.rate_limited");
    }

    [Fact]
    public async Task MissingCriticalConfigurationStopsBeforeListening()
    {
        var configuration = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ApiBaseline__ServiceName"] = " ",
        };

        ApiProcessExitResult result = await ApiProcessHarness.RunToExitAsync(
            configuration,
            TimeSpan.FromSeconds(10));

        Assert.False(result.TimedOut, "The API kept running with a missing critical setting." + Environment.NewLine + result.Logs);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ApiBaseline", result.Logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Now listening", result.Logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StructuredRequestLogOmitsQuerySignatureAndRequestBody()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();
        const string requestId = "cx301-log-probe";
        const string querySecret = "cx301_query_signature_8d90f5";
        const string bodySecret = "cx301_body_secret_7a42be";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/voice/interactions?X-Goog-Signature=" + querySecret);
        request.Headers.TryAddWithoutValidation(RequestIdHeader, requestId);
        request.Content = new StringContent(
            "{\"nodeId\":\"prologue\",\"transcript\":\"" + bodySecret + "\"}",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await server.Client.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        await server.WaitForLogAsync(requestId, TimeSpan.FromSeconds(5));

        string logs = server.Logs;
        Assert.DoesNotContain(querySecret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(bodySecret, logs, StringComparison.Ordinal);
        string[] correlatedLines = server.LogLines
            .Where(line => line.Contains(requestId, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(correlatedLines);
        Assert.Contains(correlatedLines, IsStructuredRequestCompletion);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string path,
        string json) =>
        await client.PostAsync(
            path,
            new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        string requestId = SingleHeader(response, RequestIdHeader);
        using JsonDocument body = await ReadJsonAsync(response);
        JsonElement root = body.RootElement;
        Assert.Equal(
            ["code", "message", "requestId", "schemaVersion"],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(requestId, root.GetProperty("requestId").GetString());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }

    private static string SingleHeader(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues(name, out IEnumerable<string>? values));
        return Assert.Single(values);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(json));
        return JsonDocument.Parse(json);
    }

    private static bool IsStructuredRequestCompletion(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            return HasProperty(root, "RequestId") &&
                   HasProperty(root, "Method") &&
                   HasProperty(root, "Path") &&
                   HasProperty(root, "StatusCode");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                    HasProperty(property.Value, propertyName))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (HasProperty(item, propertyName)) return true;
            }
        }

        return false;
    }
}
