using System.Net;
using System.Net.Http.Headers;

namespace Lingmai.RedMist.Generation.Tests.TestDoubles;

internal sealed record RecordedProviderRequest(
    HttpMethod Method,
    Uri? Uri,
    AuthenticationHeaderValue? Authorization,
    string Body);

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>
        _responses = new();

    public List<RecordedProviderRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string json) =>
        Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        }));

    public void Enqueue(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) =>
        _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedProviderRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization,
            body));
        if (_responses.Count == 0)
            throw new InvalidOperationException("No fake HTTP response was queued.");
        return await _responses.Dequeue()(request, cancellationToken);
    }
}
