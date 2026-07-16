namespace Lingmai.RedMist.Generation;

public enum GenerationJobStatus
{
    Created,
    Queued,
    Generating,
    Moderating,
    Transcoding,
    Ready,
    Failed,
    Expired,
}

public static class GenerationJobStateMachine
{
    public static bool CanTransition(GenerationJobStatus from, GenerationJobStatus to)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to)) return false;

        return (from, to) switch
        {
            (GenerationJobStatus.Created, GenerationJobStatus.Queued) => true,
            (GenerationJobStatus.Queued, GenerationJobStatus.Generating) => true,
            (GenerationJobStatus.Generating, GenerationJobStatus.Moderating) => true,
            (GenerationJobStatus.Moderating, GenerationJobStatus.Transcoding) => true,
            (GenerationJobStatus.Transcoding, GenerationJobStatus.Ready) => true,

            (GenerationJobStatus.Created, GenerationJobStatus.Failed) => true,
            (GenerationJobStatus.Queued, GenerationJobStatus.Failed) => true,
            (GenerationJobStatus.Generating, GenerationJobStatus.Failed) => true,
            (GenerationJobStatus.Moderating, GenerationJobStatus.Failed) => true,
            (GenerationJobStatus.Transcoding, GenerationJobStatus.Failed) => true,

            (GenerationJobStatus.Created, GenerationJobStatus.Expired) => true,
            (GenerationJobStatus.Queued, GenerationJobStatus.Expired) => true,
            (GenerationJobStatus.Generating, GenerationJobStatus.Expired) => true,
            (GenerationJobStatus.Moderating, GenerationJobStatus.Expired) => true,
            (GenerationJobStatus.Transcoding, GenerationJobStatus.Expired) => true,
            _ => false,
        };
    }

    public static void EnsureCanTransition(GenerationJobStatus from, GenerationJobStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidGenerationJobTransitionException(from, to);
    }
}

public sealed class InvalidGenerationJobTransitionException : InvalidOperationException
{
    public InvalidGenerationJobTransitionException(
        GenerationJobStatus from,
        GenerationJobStatus to)
        : base($"Generation job cannot transition from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }

    public GenerationJobStatus From { get; }

    public GenerationJobStatus To { get; }
}
