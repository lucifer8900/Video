using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Ledger;
using Microsoft.AspNetCore.Mvc;

namespace Lingmai.RedMist.Api.Ledger;

public static class LedgerEndpoints
{
    public const long MaximumRequestBytes = 1_048_576;
    private const int DefaultPageSize = 50;

    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/ledger/events", HandleAppendAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBytes));
        endpoints.MapGet(
            "/api/v1/ledger/events",
            (
                HttpContext context,
                string playerId,
                string chapter,
                int? limit,
                string? cursor,
                LedgerService service,
                CancellationToken cancellationToken) => HandleQueryAsync(
                    context,
                    playerId,
                    chapter,
                    limit ?? DefaultPageSize,
                    cursor,
                    service,
                    cancellationToken));
        return endpoints;
    }

    public static async Task<IResult> HandleAppendAsync(
        HttpContext context,
        LedgerEventBatchRequest request,
        LedgerService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(service);
        DisableCaching(context);

        if (context.Request.ContentLength > MaximumRequestBytes)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "ledger.payload_too_large",
                "The ledger request payload is too large.");
        }

        if (!string.Equals(request.SchemaVersion, LedgerService.SchemaVersion, StringComparison.Ordinal) ||
            request.Events is null)
        {
            return InvalidRequest(context);
        }

        try
        {
            LedgerEventData[] values = request.Events.Select(ToData).ToArray();
            IReadOnlyList<LedgerAppendResult> results = await service.AppendAsync(
                values,
                cancellationToken).ConfigureAwait(false);
            var response = new LedgerEventBatchResponse(
                LedgerService.SchemaVersion,
                results.Select(result => new LedgerEventAcknowledgement(
                    result.Entry.Event.PlayerId,
                    result.Entry.Event.EntryId,
                    result.Disposition == LedgerAppendDisposition.Accepted
                        ? "accepted"
                        : "duplicate")).ToArray());
            return Results.Json(response, contentType: "application/json");
        }
        catch (LedgerIdempotencyConflictException)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status409Conflict,
                "ledger.idempotency_conflict",
                "A ledger event conflicts with an earlier submission.");
        }
        catch (LedgerPlayerDeletedException)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status410Gone,
                "ledger.player_deleted",
                "The player ledger is no longer available.");
        }
        catch (LedgerValidationException)
        {
            return InvalidRequest(context);
        }
    }

    public static async Task<IResult> HandleQueryAsync(
        HttpContext context,
        string playerId,
        string chapter,
        int limit,
        string? cursor,
        LedgerService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(service);
        DisableCaching(context);
        try
        {
            NarrativeLedgerPage page = await service.QueryAsync(
                playerId,
                chapter,
                limit,
                cursor,
                cancellationToken).ConfigureAwait(false);
            var response = new NarrativeLedgerPageResponse(
                LedgerService.SchemaVersion,
                page.Entries.Select(entry => new NarrativeLedgerEntryContract(
                    ToContract(entry.Event),
                    entry.ReceivedAtUtc)).ToArray(),
                page.NextCursor);
            return Results.Json(response, contentType: "application/json");
        }
        catch (LedgerValidationException)
        {
            return InvalidRequest(context);
        }
    }

    private static LedgerEventData ToData(LedgerEventContract value)
    {
        if (value is null || value.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new LedgerValidationException();
        return new LedgerEventData(
            value.SchemaVersion,
            value.EntryId,
            value.PlayerId,
            value.Type,
            value.Actors,
            value.Severity,
            value.Chapter,
            value.WorldClock,
            value.SourceNodeId,
            value.FactRefs,
            value.Payload.Clone());
    }

    private static LedgerEventContract ToContract(LedgerEventData value) => new(
        value.SchemaVersion,
        value.EntryId,
        value.PlayerId,
        value.Type,
        value.Actors,
        value.Severity,
        value.Chapter,
        value.WorldClock,
        value.SourceNodeId,
        value.FactRefs,
        value.Payload.Clone());

    private static IResult InvalidRequest(HttpContext context) => ApiErrorWriter.Result(
        context,
        StatusCodes.Status400BadRequest,
        "ledger.invalid_request",
        "The ledger request is invalid.");

    private static void DisableCaching(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
