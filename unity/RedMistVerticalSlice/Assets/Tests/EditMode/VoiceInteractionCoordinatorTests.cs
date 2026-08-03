using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class VoiceInteractionCoordinatorTests
    {
        [Test]
        public void AcceptedAllowlistedResponseProducesPresentationOnly()
        {
            var coordinator = new VoiceInteractionCoordinator();
            int generation = coordinator.Begin(
                "node.one",
                new[] { "response.safe" });
            VoiceInteractionClientResult result = VoiceInteractionClientResult.Succeeded(
                generation,
                "npc_reaction",
                "silence",
                null,
                1d,
                "guard",
                "response.safe",
                "request.one",
                200);

            VoicePresentationDecision decision = coordinator.Complete(
                generation,
                "node.one",
                result);

            Assert.AreEqual(VoicePresentationOutcome.PresentNpcResponse, decision.Outcome);
            Assert.AreEqual("response.safe", decision.NpcResponseId);
        }

        [Test]
        public void CompletionForPreviousNodeIsIgnoredEvenWhenNewNodeSupportsVoice()
        {
            var coordinator = new VoiceInteractionCoordinator();
            int generation = coordinator.Begin("node.one", new[] { "response.safe" });
            VoiceInteractionClientResult result = VoiceInteractionClientResult.Succeeded(
                generation,
                "npc_reaction",
                "irrelevant",
                null,
                0.4d,
                "local",
                "response.safe",
                "request.one",
                200);

            VoicePresentationDecision decision = coordinator.Complete(
                generation,
                "node.two",
                result);

            Assert.AreEqual(VoicePresentationOutcome.IgnoreStale, decision.Outcome);
        }

        [Test]
        public void LateCompletionAfterCancelIsIgnored()
        {
            var coordinator = new VoiceInteractionCoordinator();
            int generation = coordinator.Begin("node.one", new[] { "response.safe" });
            coordinator.Cancel();

            VoicePresentationDecision decision = coordinator.Complete(
                generation,
                "node.one",
                VoiceInteractionClientResult.Failed(generation, "network_unavailable"));

            Assert.AreEqual(VoicePresentationOutcome.IgnoreStale, decision.Outcome);
        }

        [Test]
        public void NewCaptureCancelsBothRequestsAndInvalidatesSameNodeCompletion()
        {
            var coordinator = new VoiceInteractionCoordinator();
            int generation = coordinator.Begin("node.one", new[] { "response.safe" });
            var asr = new CountingCanceller();
            var interaction = new CountingCanceller();

            VoiceCaptureBoundary.CancelPending(asr, interaction, coordinator);
            VoicePresentationDecision decision = coordinator.Complete(
                generation,
                "node.one",
                VoiceInteractionClientResult.Succeeded(
                    generation,
                    "npc_reaction",
                    "silence",
                    null,
                    1d,
                    "guard",
                    "response.safe",
                    "request.old",
                    200));

            Assert.AreEqual(1, asr.CancelCount);
            Assert.AreEqual(1, interaction.CancelCount);
            Assert.AreEqual(VoicePresentationOutcome.IgnoreStale, decision.Outcome);
        }

        [Test]
        public void UnknownResponseOrNetworkFailureKeepsFixedChoices()
        {
            var coordinator = new VoiceInteractionCoordinator();
            int first = coordinator.Begin("node.one", new[] { "response.safe" });
            VoicePresentationDecision unknown = coordinator.Complete(
                first,
                "node.one",
                VoiceInteractionClientResult.Succeeded(
                    first,
                    "npc_reaction",
                    "abuse",
                    null,
                    0.95d,
                    "guard",
                    "response.injected",
                    "request.one",
                    200));
            int second = coordinator.Begin("node.one", new[] { "response.safe" });
            VoicePresentationDecision network = coordinator.Complete(
                second,
                "node.one",
                VoiceInteractionClientResult.Failed(second, "network_unavailable"));

            Assert.AreEqual(VoicePresentationOutcome.KeepFixedChoices, unknown.Outcome);
            Assert.AreEqual(VoicePresentationOutcome.KeepFixedChoices, network.Outcome);
        }

        [Test]
        public void StrictParserRejectsEffectsNextNodeAndUnknownProperties()
        {
            const string prefix = "{\"schemaVersion\":\"1.0.0\",\"requestId\":\"r\",\"resolution\":\"npc_reaction\",\"inputKind\":\"silence\",\"intentId\":null,\"confidence\":1,\"source\":\"guard\",\"npcResponseId\":\"response.safe\"";

            Assert.IsFalse(VoiceInteractionResponseParser.TryParse(prefix + ",\"effects\":[]}", 1, 200, out _));
            Assert.IsFalse(VoiceInteractionResponseParser.TryParse(prefix + ",\"nextNodeId\":\"node.bad\"}", 1, 200, out _));
            Assert.IsFalse(VoiceInteractionResponseParser.TryParse(prefix + ",\"state\":{}}", 1, 200, out _));
            Assert.IsTrue(VoiceInteractionResponseParser.TryParse(prefix + "}", 1, 200, out VoiceInteractionClientResult valid));
            Assert.AreEqual("response.safe", valid.NpcResponseId);
        }

        private sealed class CountingCanceller : IVoiceRequestCanceller
        {
            public int CancelCount { get; private set; }

            public void Cancel()
            {
                CancelCount++;
            }
        }
    }
}
