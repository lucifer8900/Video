using Lingmai.RedMist.Contracts.Api;
using Microsoft.AspNetCore.RateLimiting;

namespace Lingmai.RedMist.Api.Baseline;

public static class ApiBaselineEndpoints
{
    public static IEndpointRouteBuilder MapApiBaselineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/health",
                (HttpContext context) => Results.Json(new ApiHealthResponse(
                    "1.0.0",
                    "healthy",
                    context.TraceIdentifier)))
            .Produces<ApiHealthResponse>(StatusCodes.Status200OK)
            .DisableRateLimiting()
            .WithName("ApiHealth");
        return endpoints;
    }
}
