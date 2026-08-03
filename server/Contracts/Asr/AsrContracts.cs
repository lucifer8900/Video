namespace Lingmai.RedMist.Contracts.Asr;

public sealed record AsrTranscriptionResponse(
    string SchemaVersion,
    string RequestId,
    string Status,
    string Text,
    string Language,
    long DurationMilliseconds);
