using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Lingmai.RedMist
{
    public sealed class VoiceInteractionClientResult
    {
        private VoiceInteractionClientResult(
            int generation,
            bool success,
            string resolution,
            string inputKind,
            string intentId,
            double confidence,
            string source,
            string npcResponseId,
            string requestId,
            string errorCode,
            long httpStatusCode)
        {
            Generation = generation;
            Success = success;
            Resolution = resolution ?? string.Empty;
            InputKind = inputKind ?? string.Empty;
            IntentId = intentId ?? string.Empty;
            Confidence = confidence;
            Source = source ?? string.Empty;
            NpcResponseId = npcResponseId ?? string.Empty;
            RequestId = requestId ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            HttpStatusCode = httpStatusCode;
        }

        public int Generation { get; }
        public bool Success { get; }
        public string Resolution { get; }
        public string InputKind { get; }
        public string IntentId { get; }
        public double Confidence { get; }
        public string Source { get; }
        public string NpcResponseId { get; }
        public string RequestId { get; }
        public string ErrorCode { get; }
        public long HttpStatusCode { get; }

        public static VoiceInteractionClientResult Succeeded(
            int generation,
            string resolution,
            string inputKind,
            string intentId,
            double confidence,
            string source,
            string npcResponseId,
            string requestId,
            long httpStatusCode) =>
            new VoiceInteractionClientResult(
                generation,
                true,
                resolution,
                inputKind,
                intentId,
                confidence,
                source,
                npcResponseId,
                requestId,
                string.Empty,
                httpStatusCode);

        public static VoiceInteractionClientResult Failed(
            int generation,
            string errorCode,
            long httpStatusCode = 0) =>
            new VoiceInteractionClientResult(
                generation,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                0d,
                string.Empty,
                string.Empty,
                string.Empty,
                errorCode,
                httpStatusCode);
    }

    public static class VoiceInteractionResponseParser
    {
        private static readonly HashSet<string> Properties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion",
            "requestId",
            "resolution",
            "inputKind",
            "intentId",
            "confidence",
            "source",
            "npcResponseId"
        };

        private static readonly HashSet<string> Resolutions = new HashSet<string>(StringComparer.Ordinal)
        {
            "intent_matched",
            "npc_reaction",
            "confirmation_required",
            "fixed_choice_fallback"
        };

        private static readonly HashSet<string> InputKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "valid",
            "abuse",
            "irrelevant",
            "too_long",
            "silence",
            "low_confidence"
        };

        public static bool TryParse(
            string json,
            int generation,
            long httpStatusCode,
            out VoiceInteractionClientResult result)
        {
            result = VoiceInteractionClientResult.Failed(generation, "invalid_response", httpStatusCode);
            try
            {
                StoryJsonValue root = StoryJson.Parse(json);
                if (root.Kind != StoryJsonKind.Object || root.ObjectValue.Count != Properties.Count)
                    return false;
                foreach (string property in root.ObjectValue.Keys)
                {
                    if (!Properties.Contains(property)) return false;
                }

                string schemaVersion = RequiredString(root, "schemaVersion");
                if (!string.Equals(schemaVersion, "1.0.0", StringComparison.Ordinal)) return false;
                string requestId = RequiredString(root, "requestId");
                string resolution = RequiredString(root, "resolution");
                string inputKind = RequiredString(root, "inputKind");
                string intentId = RequiredNullableString(root, "intentId");
                string npcResponseId = RequiredNullableString(root, "npcResponseId");
                string source = RequiredString(root, "source");
                double confidence = RequiredUnitNumber(root, "confidence");
                if (!Resolutions.Contains(resolution) || !InputKinds.Contains(inputKind)) return false;
                bool fallback = string.Equals(resolution, "fixed_choice_fallback", StringComparison.Ordinal);
                if (fallback != string.IsNullOrEmpty(npcResponseId)) return false;
                if (string.Equals(resolution, "intent_matched", StringComparison.Ordinal) !=
                    string.Equals(inputKind, "valid", StringComparison.Ordinal)) return false;

                result = VoiceInteractionClientResult.Succeeded(
                    generation,
                    resolution,
                    inputKind,
                    intentId,
                    confidence,
                    source,
                    npcResponseId,
                    requestId,
                    httpStatusCode);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string RequiredString(StoryJsonValue owner, string property)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value) ||
                value.Kind != StoryJsonKind.String ||
                string.IsNullOrWhiteSpace(value.StringValue))
            {
                throw new InvalidDataException("Missing string: " + property);
            }
            return value.StringValue;
        }

        private static string RequiredNullableString(StoryJsonValue owner, string property)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value))
                throw new InvalidDataException("Missing nullable string: " + property);
            if (value.Kind == StoryJsonKind.Null) return string.Empty;
            if (value.Kind != StoryJsonKind.String || string.IsNullOrWhiteSpace(value.StringValue))
                throw new InvalidDataException("Invalid nullable string: " + property);
            return value.StringValue;
        }

        private static double RequiredUnitNumber(StoryJsonValue owner, string property)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value) ||
                value.Kind != StoryJsonKind.Number ||
                !double.TryParse(value.NumberToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                double.IsNaN(number) || double.IsInfinity(number) || number < 0d || number > 1d)
            {
                throw new InvalidDataException("Invalid confidence.");
            }
            return number;
        }
    }

    public enum VoicePresentationOutcome
    {
        PresentNpcResponse,
        KeepFixedChoices,
        IgnoreStale
    }

    public sealed class VoicePresentationDecision
    {
        public VoicePresentationDecision(VoicePresentationOutcome outcome, string npcResponseId)
        {
            Outcome = outcome;
            NpcResponseId = npcResponseId ?? string.Empty;
        }

        public VoicePresentationOutcome Outcome { get; }
        public string NpcResponseId { get; }
    }

    public interface IVoiceRequestCanceller
    {
        void Cancel();
    }

    public static class VoiceCaptureBoundary
    {
        public static void CancelPending(
            IVoiceRequestCanceller asrClient,
            IVoiceRequestCanceller interactionClient,
            VoiceInteractionCoordinator coordinator)
        {
            asrClient?.Cancel();
            interactionClient?.Cancel();
            coordinator?.Cancel();
        }
    }

    public sealed class VoiceInteractionCoordinator
    {
        private int _generation;
        private string _nodeId = string.Empty;
        private HashSet<string> _allowedResponseIds = new HashSet<string>(StringComparer.Ordinal);

        public int Begin(string nodeId, IEnumerable<string> allowedResponseIds)
        {
            _generation++;
            _nodeId = nodeId ?? string.Empty;
            _allowedResponseIds = new HashSet<string>(
                allowedResponseIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            return _generation;
        }

        public void Cancel()
        {
            _generation++;
            _nodeId = string.Empty;
            _allowedResponseIds.Clear();
        }

        public VoicePresentationDecision Complete(
            int generation,
            string currentNodeId,
            VoiceInteractionClientResult result)
        {
            if (generation != _generation ||
                !string.Equals(_nodeId, currentNodeId, StringComparison.Ordinal))
            {
                return new VoicePresentationDecision(VoicePresentationOutcome.IgnoreStale, string.Empty);
            }

            _nodeId = string.Empty;
            if (result == null || !result.Success ||
                string.Equals(result.Resolution, "fixed_choice_fallback", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(result.NpcResponseId) ||
                !_allowedResponseIds.Contains(result.NpcResponseId))
            {
                _allowedResponseIds.Clear();
                return new VoicePresentationDecision(VoicePresentationOutcome.KeepFixedChoices, string.Empty);
            }

            string responseId = result.NpcResponseId;
            _allowedResponseIds.Clear();
            return new VoicePresentationDecision(VoicePresentationOutcome.PresentNpcResponse, responseId);
        }
    }
}
