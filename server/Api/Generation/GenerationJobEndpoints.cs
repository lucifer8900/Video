using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Contracts.Generation;
using Lingmai.RedMist.Generation;

namespace Lingmai.RedMist.Api.Generation;

public static class GenerationJobEndpoints
{
    private const string SchemaVersion = "1.0.0";
    private const int PollAfterMilliseconds = 1000;

    public static IEndpointRouteBuilder MapGenerationJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/generation/jobs", HandleSubmitAsync);
        endpoints.MapGet("/api/v1/generation/jobs/{jobId:guid}", HandleGetAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleSubmitAsync(
        HttpContext context,
        GenerationJobSubmitRequest request,
        GenerationSubmissionGate gate,
        GenerationJobService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(service);

        if (!gate.Enabled)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "generation.disabled",
                "Online generation is disabled.");
        }

        if (!string.Equals(request.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status400BadRequest,
                "generation.invalid_request",
                "The generation request is invalid.");
        }

        try
        {
            GenerationJob job = await service.SubmitAndQueueAsync(
                new GenerationJobSubmission(request.IdempotencyKey, request.InputHash),
                cancellationToken).ConfigureAwait(false);
            return Results.Json(
                Response(context, job),
                statusCode: StatusCodes.Status202Accepted,
                contentType: "application/json");
        }
        catch (IdempotencyConflictException)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status409Conflict,
                "generation.idempotency_conflict",
                "The idempotency key was already used for a different request.");
        }
        catch (ArgumentException)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status400BadRequest,
                "generation.invalid_request",
                "The generation request is invalid.");
        }
    }

    public static async Task<IResult> HandleGetAsync(
        HttpContext context,
        Guid jobId,
        GenerationJobService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(service);
        GenerationJob? job = await service.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return ApiErrorWriter.Result(
                context,
                StatusCodes.Status404NotFound,
                "generation.not_found",
                "The generation job was not found.");
        }

        return Results.Json(Response(context, job), contentType: "application/json");
    }

    private static GenerationJobStatusResponse Response(HttpContext context, GenerationJob job)
    {
        string status = ToContractStatus(job.Status);
        bool terminal = job.Status is
            GenerationJobStatus.Ready or
            GenerationJobStatus.Failed or
            GenerationJobStatus.Expired;
        string? failureCode = job.Status switch
        {
            GenerationJobStatus.Failed => FailureCode(job),
            GenerationJobStatus.Expired => "generation.expired",
            _ => null,
        };
        return new GenerationJobStatusResponse(
            SchemaVersion,
            RequestId(context),
            job.Id,
            status,
            terminal,
            terminal ? 0 : PollAfterMilliseconds,
            failureCode);
    }

    private static string RequestId(HttpContext context) =>
        string.IsNullOrWhiteSpace(context.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : context.TraceIdentifier;

    private static string FailureCode(GenerationJob job) => job.FailureCode switch
    {
        "moderation.rejected" => "moderation.rejected",
        _ => "generation.failed",
    };

    private static string ToContractStatus(GenerationJobStatus status) => status switch
    {
        GenerationJobStatus.Created => GenerationJobStatuses.Created,
        GenerationJobStatus.Queued => GenerationJobStatuses.Queued,
        GenerationJobStatus.Generating => GenerationJobStatuses.Generating,
        GenerationJobStatus.Moderating => GenerationJobStatuses.Moderating,
        GenerationJobStatus.Transcoding => GenerationJobStatuses.Transcoding,
        GenerationJobStatus.Ready => GenerationJobStatuses.Ready,
        GenerationJobStatus.Failed => GenerationJobStatuses.Failed,
        GenerationJobStatus.Expired => GenerationJobStatuses.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
