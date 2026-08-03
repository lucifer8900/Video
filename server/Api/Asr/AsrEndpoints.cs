using Lingmai.RedMist.Api.Baseline;
using Lingmai.RedMist.Contracts.Api;
using Lingmai.RedMist.Contracts.Asr;
using Microsoft.AspNetCore.Http.Features;

namespace Lingmai.RedMist.Api.Asr;

public static class AsrEndpoints
{
    public static IEndpointRouteBuilder MapAsrEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/asr/transcriptions", HandleAsync)
            .Accepts<byte[]>("audio/wav")
            .Produces<AsrTranscriptionResponse>(StatusCodes.Status200OK)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<ApiErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        AsrOptions options,
        AsrTranscriptionService service,
        CancellationToken cancellationToken)
    {
        IHttpMaxRequestBodySizeFeature? bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = options.MaxAudioBytes;

        if (context.Request.ContentLength is 0)
            return ApiErrorWriter.Result(context, StatusCodes.Status400BadRequest, "asr.audio_missing", "Audio is required.");
        if (context.Request.ContentLength > options.MaxAudioBytes)
            return ApiErrorWriter.Result(context, StatusCodes.Status413PayloadTooLarge, AsrErrorCodes.AudioTooLarge, "Audio exceeds the configured size limit.");

        string contentType = context.Request.ContentType?.Split(';', 2)[0].Trim() ?? string.Empty;
        if (!string.Equals(contentType, "audio/wav", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status415UnsupportedMediaType, "asr.unsupported_media_type", "Content-Type must be audio/wav.");
        }

        try
        {
            AsrProviderResult result = await service.TranscribeAsync(
                context.Request.Body,
                "capture.wav",
                contentType,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Text))
                throw new AsrProviderException(AsrErrorCodes.ProviderUnavailable, "The transcription provider returned no text.");

            var response = new AsrTranscriptionResponse(
                "1.0.0",
                context.TraceIdentifier,
                "transcribed",
                result.Text,
                string.IsNullOrWhiteSpace(result.Language) ? "zh" : result.Language,
                checked((long)Math.Round(result.DurationSeconds * 1000d)));
            return Results.Ok(response);
        }
        catch (AsrValidationException error)
        {
            int status = error.Code == AsrErrorCodes.AudioTooLarge
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status422UnprocessableEntity;
            return ApiErrorWriter.Result(
                context,
                status,
                error.Code,
                "Audio validation failed.");
        }
        catch (AsrProviderException error)
        {
            return ApiErrorWriter.Result(context, StatusCodes.Status503ServiceUnavailable, error.Code, "Transcription is temporarily unavailable.");
        }
    }
}
