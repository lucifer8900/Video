using Lingmai.RedMist.Generation;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class GenerationJobStateMachineTests
{
    private static readonly HashSet<(GenerationJobStatus From, GenerationJobStatus To)> LegalEdges =
    [
        (GenerationJobStatus.Created, GenerationJobStatus.Queued),
        (GenerationJobStatus.Queued, GenerationJobStatus.Generating),
        (GenerationJobStatus.Generating, GenerationJobStatus.Moderating),
        (GenerationJobStatus.Moderating, GenerationJobStatus.Transcoding),
        (GenerationJobStatus.Transcoding, GenerationJobStatus.Ready),

        (GenerationJobStatus.Created, GenerationJobStatus.Failed),
        (GenerationJobStatus.Queued, GenerationJobStatus.Failed),
        (GenerationJobStatus.Generating, GenerationJobStatus.Failed),
        (GenerationJobStatus.Moderating, GenerationJobStatus.Failed),
        (GenerationJobStatus.Transcoding, GenerationJobStatus.Failed),

        (GenerationJobStatus.Created, GenerationJobStatus.Expired),
        (GenerationJobStatus.Queued, GenerationJobStatus.Expired),
        (GenerationJobStatus.Generating, GenerationJobStatus.Expired),
        (GenerationJobStatus.Moderating, GenerationJobStatus.Expired),
        (GenerationJobStatus.Transcoding, GenerationJobStatus.Expired),
    ];

    public static TheoryData<GenerationJobStatus, GenerationJobStatus> LegalTransitions()
    {
        var data = new TheoryData<GenerationJobStatus, GenerationJobStatus>();
        foreach ((GenerationJobStatus from, GenerationJobStatus to) in LegalEdges)
            data.Add(from, to);
        return data;
    }

    public static TheoryData<GenerationJobStatus, GenerationJobStatus> IllegalTransitions()
    {
        var data = new TheoryData<GenerationJobStatus, GenerationJobStatus>();
        GenerationJobStatus[] states = Enum.GetValues<GenerationJobStatus>();
        foreach (GenerationJobStatus from in states)
        foreach (GenerationJobStatus to in states)
        {
            if (!LegalEdges.Contains((from, to))) data.Add(from, to);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void AllowsOnlyDeclaredForwardOrTerminalTransitions(
        GenerationJobStatus from,
        GenerationJobStatus to)
    {
        Assert.True(GenerationJobStateMachine.CanTransition(from, to));
        GenerationJobStateMachine.EnsureCanTransition(from, to);
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void RejectsSkippedBackwardRepeatedAndTerminalTransitions(
        GenerationJobStatus from,
        GenerationJobStatus to)
    {
        Assert.False(GenerationJobStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidGenerationJobTransitionException>(
            () => GenerationJobStateMachine.EnsureCanTransition(from, to));
    }

    [Theory]
    [InlineData(GenerationJobStatus.Ready)]
    [InlineData(GenerationJobStatus.Failed)]
    [InlineData(GenerationJobStatus.Expired)]
    public void TerminalStatesHaveNoOutgoingEdges(GenerationJobStatus terminal)
    {
        Assert.All(
            Enum.GetValues<GenerationJobStatus>(),
            target => Assert.False(GenerationJobStateMachine.CanTransition(terminal, target)));
    }

    [Fact]
    public void UndefinedEnumValuesAreRejected()
    {
        var undefined = (GenerationJobStatus)int.MaxValue;

        Assert.False(GenerationJobStateMachine.CanTransition(undefined, GenerationJobStatus.Created));
        Assert.False(GenerationJobStateMachine.CanTransition(GenerationJobStatus.Created, undefined));
    }
}
