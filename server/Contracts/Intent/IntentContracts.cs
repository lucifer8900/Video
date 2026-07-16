using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Intent;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntentClassificationRequest(
    string NodeId,
    string Transcript);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntentClassificationResponse(
    string SchemaVersion,
    string RequestId,
    string Outcome,
    string? IntentId,
    double Confidence,
    string Source);
