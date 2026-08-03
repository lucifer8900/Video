namespace Lingmai.RedMist.Contracts.Media;

public sealed record MediaDownloadTicketResponse(
    string SchemaVersion,
    string RequestId,
    string MediaId,
    string ContentHash,
    Uri DownloadUrl,
    DateTimeOffset ExpiresAtUtc,
    string ContentType,
    long Length);
