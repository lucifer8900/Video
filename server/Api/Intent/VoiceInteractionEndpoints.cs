using System.Text.Json;
using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Contracts.Api;
using Lingmai.RedMist.Contracts.Intent;

namespace Lingmai.RedMist.Api.Intent;

public static class VoiceInteractionEndpoints
{
    private static readonly HashSet<string> RequestProperties = new(StringComparer.Ordinal)
    {
        "nodeId",
        "transcript",
    };

    public static IEndpointRouteBuilder MapVoiceInteractionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/voice/interactions", HandleAsync)
            .Accepts<VoiceInteractionRequest>("application/json")
            .Produces<VoiceInteractionResponse>(StatusCodes.Status200OK)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        IVoiceInteractionCatalog catalog,
        VoiceInteractionService service,
        IntentOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(options);

        string mediaType = context.Request.ContentType?.Split(';', 2)[0].Trim() ?? string.Empty;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return ApiErrorWriter.Result(context, StatusCodes.Status415UnsupportedMediaType, IntentErrorCodes.InvalidRequest, "Content-Type must be application/json.");

        VoiceInteractionRequest request;
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
            if (root.ValueKind != JsonValueKind.Object || !HasExactProperties(root) ||
                !TryReadString(root, "nodeId", out string nodeId) ||
                !TryReadString(root, "transcript", out string transcript))
            {
                return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "Request JSON is invalid.");
            }
            request = new VoiceInteractionRequest(nodeId, transcript);
        }
        catch (JsonException)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "Request JSON is invalid.");
        }

        string nodeIdClean = request.NodeId.Trim();
        if (nodeIdClean.Length == 0 || request.Transcript.Length > options.MaxAcceptedTranscriptCharacters)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, IntentErrorCodes.InvalidRequest, "nodeId or transcript is outside the accepted limits.");
        }
        if (!catalog.TryGetNodePolicy(nodeIdClean, out NodeVoicePolicy policy))
            return ApiErrorWriter.Result(context, StatusCodes.Status404NotFound, IntentErrorCodes.UnknownNode, "The story node is unknown.");

        try
        {
            VoiceInteractionDecision decision = await service.ResolveAsync(
                request.Transcript,
                policy,
                options.MaxTranscriptCharacters,
                cancellationToken).ConfigureAwait(false);
            return Results.Json(new VoiceInteractionResponse(
                "1.0.0",
                context.TraceIdentifier,
                ToWireResolution(decision.Resolution),
                decision.InputKind is null ? "valid" : ToWireInputKind(decision.InputKind.Value),
                decision.IntentId,
                decision.Confidence,
                decision.Source,
                decision.NpcResponseId));
        }
        catch (IntentProviderException)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status503ServiceUnavailable, IntentErrorCodes.ProviderUnavailable, "Voice interaction is temporarily unavailable.");
        }
    }

    private static bool HasExactProperties(JsonElement root)
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

    private static string ToWireResolution(VoiceInteractionResolution resolution) => resolution switch
    {
        VoiceInteractionResolution.IntentMatched => "intent_matched",
        VoiceInteractionResolution.NpcReaction => "npc_reaction",
        VoiceInteractionResolution.ConfirmationRequired => "confirmation_required",
        _ => "fixed_choice_fallback",
    };

    private static string ToWireInputKind(InvalidInputKind kind) => kind switch
    {
        InvalidInputKind.Abuse => "abuse",
        InvalidInputKind.Irrelevant => "irrelevant",
        InvalidInputKind.TooLong => "too_long",
        InvalidInputKind.Silence => "silence",
        _ => "low_confidence",
    };
}
