using System.Text.Json;
using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Contracts.Api;
using Lingmai.RedMist.Contracts.Intent;

namespace Lingmai.RedMist.Api.Intent;

public static class IntentEndpoints
{
    private static readonly HashSet<string> RequestProperties = new(StringComparer.Ordinal)
    {
        "nodeId",
        "transcript",
    };

    public static IEndpointRouteBuilder MapIntentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/intent/classifications", HandleAsync)
            .Accepts<IntentClassificationRequest>("application/json")
            .Produces<IntentClassificationResponse>(StatusCodes.Status200OK)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        IIntentCatalog catalog,
        IntentClassificationService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(service);

        string mediaType = context.Request.ContentType?.Split(';', 2)[0].Trim() ?? string.Empty;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                IntentErrorCodes.InvalidRequest,
                "Content-Type must be application/json.");
        }

        IntentClassificationRequest request;
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(
                context.Request.Body,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                },
                cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactRequestProperties(root))
                return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "Request JSON is invalid.");
            if (!TryReadString(root, "nodeId", out string nodeId) ||
                !TryReadString(root, "transcript", out string transcript))
            {
                return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "nodeId and transcript are required strings.");
            }

            request = new IntentClassificationRequest(nodeId, transcript);
        }
        catch (JsonException)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "Request JSON is invalid.");
        }

        string cleanNodeId = request.NodeId.Trim();
        string cleanTranscript = request.Transcript.Trim();
        IntentOptions options = context.RequestServices.GetService<IntentOptions>() ?? new IntentOptions();
        if (cleanNodeId.Length == 0 || cleanTranscript.Length == 0 ||
            cleanTranscript.Length > options.MaxTranscriptCharacters)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "nodeId or transcript is outside the allowed limits.");
        }

        if (!catalog.TryGetAllowedIntents(cleanNodeId, out IReadOnlyList<IntentCandidate> allowed))
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status404NotFound, IntentErrorCodes.UnknownNode, "The story node is unknown.");
        }

        try
        {
            IntentDecision decision = await service.ClassifyAsync(
                new IntentClassificationContext(cleanTranscript, allowed),
                cancellationToken).ConfigureAwait(false);
            var response = new IntentClassificationResponse(
                "1.0.0",
                context.TraceIdentifier,
                ToWireOutcome(decision.Outcome),
                decision.IntentId,
                decision.Confidence,
                decision.Source);
            return Results.Json(response);
        }
        catch (IntentProviderException)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status503ServiceUnavailable, IntentErrorCodes.ProviderUnavailable, "Intent classification is temporarily unavailable.");
        }
    }

    private static bool HasExactRequestProperties(JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!RequestProperties.Contains(property.Name) || !seen.Add(property.Name)) return false;
        }

        return seen.SetEquals(RequestProperties);
    }

    private static bool TryReadString(JsonElement root, string property, out string value)
    {
        if (root.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string ToWireOutcome(IntentOutcome outcome) => outcome switch
    {
        IntentOutcome.Matched => "matched",
        IntentOutcome.Irrelevant => "irrelevant",
        IntentOutcome.LowConfidence => "low_confidence",
        _ => "low_confidence",
    };
}
