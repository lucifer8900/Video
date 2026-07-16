using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class VoiceInteractionServiceTests
{
    [Fact]
    public async Task SilenceUsesNodeReactionWithoutCallingClassifier()
    {
        var classifier = new CountingClassifier(new IntentDecision(
            IntentOutcome.Matched,
            "intent.allowed",
            1d,
            "should-not-run"));
        VoiceInteractionService service = Service(classifier);
        NodeVoicePolicy policy = Policy();

        VoiceInteractionDecision decision = await service.ResolveAsync(
            string.Empty,
            policy,
            512,
            CancellationToken.None);

        Assert.Equal(0, classifier.CallCount);
        Assert.Equal(VoiceInteractionResolution.NpcReaction, decision.Resolution);
        Assert.Equal(InvalidInputKind.Silence, decision.InputKind);
        Assert.Equal("response.silence", decision.NpcResponseId);
    }

    [Theory]
    [InlineData(IntentOutcome.Irrelevant, InvalidInputKind.Irrelevant, "response.irrelevant")]
    [InlineData(IntentOutcome.LowConfidence, InvalidInputKind.LowConfidence, "response.low")]
    public async Task GuardedNonMatchUsesConfiguredInvalidReaction(
        IntentOutcome outcome,
        InvalidInputKind expectedKind,
        string expectedResponse)
    {
        var classifier = new CountingClassifier(new IntentDecision(outcome, null, 0.4d, "fixture"));
        VoiceInteractionDecision decision = await Service(classifier).ResolveAsync(
            "无法匹配的普通表达",
            Policy(),
            512,
            CancellationToken.None);

        Assert.Equal(1, classifier.CallCount);
        Assert.Equal(expectedKind, decision.InputKind);
        Assert.Equal(expectedResponse, decision.NpcResponseId);
    }

    [Fact]
    public async Task MissingRuleOrBlockedSevereReactionKeepsFixedChoices()
    {
        var classifier = new CountingClassifier(new IntentDecision(
            IntentOutcome.Irrelevant,
            null,
            0.2d,
            "fixture"));
        NodeVoicePolicy policy = Policy();
        policy.InvalidInputResponses.Remove(InvalidInputKind.Irrelevant);

        VoiceInteractionDecision missing = await Service(classifier).ResolveAsync(
            "普通表达",
            policy,
            512,
            CancellationToken.None);

        Assert.Equal(VoiceInteractionResolution.FixedChoiceFallback, missing.Resolution);
        Assert.Null(missing.NpcResponseId);
    }

    private static VoiceInteractionService Service(IIntentClassifier classifier) => new(
        new InvalidInputDetector(),
        new IntentClassificationService(classifier, classifier),
        new NpcConsequenceGate());

    internal static NodeVoicePolicy Policy()
    {
        var responses = new Dictionary<string, NpcResponsePolicy>(StringComparer.Ordinal)
        {
            ["response.matched"] = new("response.matched", []),
            ["response.abuse"] = new("response.abuse", []),
            ["response.irrelevant"] = new("response.irrelevant", []),
            ["response.long"] = new("response.long", []),
            ["response.silence"] = new("response.silence", []),
            ["response.low"] = new("response.low", []),
        };
        return new NodeVoicePolicy(
            [new IntentCandidate("intent.allowed", ["同意"], 0.7d)],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["intent.allowed"] = "response.matched",
            },
            new Dictionary<InvalidInputKind, string>
            {
                [InvalidInputKind.Abuse] = "response.abuse",
                [InvalidInputKind.Irrelevant] = "response.irrelevant",
                [InvalidInputKind.TooLong] = "response.long",
                [InvalidInputKind.Silence] = "response.silence",
                [InvalidInputKind.LowConfidence] = "response.low",
            },
            responses);
    }

    internal sealed class CountingClassifier : IIntentClassifier
    {
        private readonly IntentDecision _decision;

        public CountingClassifier(IntentDecision decision) => _decision = decision;

        public int CallCount { get; private set; }

        public Task<IntentDecision> ClassifyAsync(
            IntentClassificationContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_decision);
        }
    }
}
