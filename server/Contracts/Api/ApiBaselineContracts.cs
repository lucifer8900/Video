using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApiErrorResponse(
    string SchemaVersion,
    string RequestId,
    string Code,
    string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApiHealthResponse(
    string SchemaVersion,
    string Status,
    string RequestId);
