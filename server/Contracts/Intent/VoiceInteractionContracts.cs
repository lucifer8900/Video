using System.Text.Json.Serialization;

namespace Lingmai.RedMist.Contracts.Intent;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoiceInteractionRequest(
    string NodeId,
    string Transcript);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoiceInteractionResponse(
    string SchemaVersion,
    string RequestId,
    string Resolution,
    string InputKind,
    string? IntentId,
    double Confidence,
    string Source,
    string? NpcResponseId);
