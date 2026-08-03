using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lingmai.RedMist
{
    public static class StoryThreadCodec
    {
        private static readonly string[] RootProperties =
        {
            "schemaVersion",
            "threadId",
            "templateId",
            "playerId",
            "resolvedParams",
            "injections",
            "mediaRefs",
            "expiresAtChapterEnd",
            "auditRef",
            "fallbackUsed"
        };

        private static readonly string[] InjectionProperties =
        {
            "nodeId",
            "point",
            "text",
            "effects"
        };

        private static readonly string[] RelationshipEffectProperties =
        {
            "op",
            "targetRef",
            "field",
            "delta"
        };

        private static readonly string[] ValueEffectProperties =
        {
            "op",
            "value"
        };

        public static StoryThread Decode(string json)
        {
            if (json == null ||
                Encoding.UTF8.GetByteCount(json) > StoryThreadContractRules.MaximumDocumentBytes)
            {
                throw Invalid();
            }

            try
            {
                return DecodeValue(StoryJson.Parse(json));
            }
            catch (StoryThreadContractException)
            {
                throw;
            }
            catch (Exception error) when (
                error is StoryJsonException ||
                error is InvalidDataException ||
                error is ArgumentException ||
                error is OverflowException ||
                error is FormatException)
            {
                throw Invalid();
            }
        }

        internal static StoryThread DecodeValue(StoryJsonValue root)
        {
            RequireExactObject(root, RootProperties);

            string schemaVersion = RequiredString(root, "schemaVersion");
            string threadId = RequiredString(root, "threadId");
            string templateId = RequiredString(root, "templateId");
            string playerId = RequiredString(root, "playerId");
            IReadOnlyDictionary<string, string> resolvedParams = ParseResolvedParams(
                Required(root, "resolvedParams"));
            IReadOnlyList<StoryThreadInjection> injections = ParseInjections(
                Required(root, "injections"));
            IReadOnlyList<string> mediaRefs = ParseIdArray(
                Required(root, "mediaRefs"),
                StoryThreadContractRules.MaximumCollectionCount);
            bool expiresAtChapterEnd = RequiredBoolean(root, "expiresAtChapterEnd");
            string auditRef = RequiredString(root, "auditRef");
            bool fallbackUsed = RequiredBoolean(root, "fallbackUsed");

            var thread = new StoryThread(
                schemaVersion,
                threadId,
                templateId,
                playerId,
                resolvedParams,
                injections,
                mediaRefs,
                expiresAtChapterEnd,
                auditRef,
                fallbackUsed);
            if (!StoryThreadContractRules.IsValid(thread)) throw Invalid();
            return thread;
        }

        public static bool TryDecode(string json, out StoryThread thread)
        {
            thread = null;
            try
            {
                thread = Decode(json);
                return true;
            }
            catch (StoryThreadContractException)
            {
                return false;
            }
        }

        private static IReadOnlyDictionary<string, string> ParseResolvedParams(
            StoryJsonValue value)
        {
            RequireKind(value, StoryJsonKind.Object);
            if (value.ObjectValue.Count > StoryThreadContractRules.MaximumCollectionCount)
                throw Invalid();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, StoryJsonValue> parameter in value.ObjectValue)
            {
                if (!StoryThreadContractRules.IsParameterName(parameter.Key) ||
                    !casing.Add(parameter.Key) ||
                    parameter.Value.Kind != StoryJsonKind.String ||
                    !StoryThreadContractRules.IsStableId(parameter.Value.StringValue))
                {
                    throw Invalid();
                }
                result.Add(parameter.Key, parameter.Value.StringValue);
            }
            return result;
        }

        private static IReadOnlyList<StoryThreadInjection> ParseInjections(
            StoryJsonValue value)
        {
            RequireKind(value, StoryJsonKind.Array);
            if (value.ArrayValue.Count < 1 ||
                value.ArrayValue.Count > StoryThreadContractRules.MaximumCollectionCount)
            {
                throw Invalid();
            }

            var result = new StoryThreadInjection[value.ArrayValue.Count];
            for (int index = 0; index < value.ArrayValue.Count; index++)
            {
                StoryJsonValue item = value.ArrayValue[index];
                RequireExactObject(item, InjectionProperties);
                string nodeId = RequiredString(item, "nodeId");
                string point = RequiredString(item, "point");
                string text = RequiredString(item, "text");
                IReadOnlyList<StoryThreadEffect> effects = ParseEffects(
                    Required(item, "effects"));
                var injection = new StoryThreadInjection(nodeId, point, text, effects);
                if (!StoryThreadContractRules.IsValid(injection)) throw Invalid();
                result[index] = injection;
            }
            return result;
        }

        private static IReadOnlyList<StoryThreadEffect> ParseEffects(StoryJsonValue value)
        {
            RequireKind(value, StoryJsonKind.Array);
            if (value.ArrayValue.Count > StoryThreadContractRules.MaximumCollectionCount)
                throw Invalid();

            var result = new StoryThreadEffect[value.ArrayValue.Count];
            for (int index = 0; index < value.ArrayValue.Count; index++)
                result[index] = ParseEffect(value.ArrayValue[index]);
            return result;
        }

        private static StoryThreadEffect ParseEffect(StoryJsonValue value)
        {
            RequireKind(value, StoryJsonKind.Object);
            string operation = RequiredString(value, "op");
            StoryThreadEffect effect;
            if (string.Equals(operation, "relationship", StringComparison.Ordinal))
            {
                RequireExactObject(value, RelationshipEffectProperties);
                effect = new StoryThreadEffect(
                    operation,
                    RequiredString(value, "targetRef"),
                    RequiredString(value, "field"),
                    RequiredFiniteNumber(value, "delta"),
                    null);
            }
            else if (string.Equals(operation, "clue", StringComparison.Ordinal) ||
                     string.Equals(operation, "branch_unlock", StringComparison.Ordinal))
            {
                RequireExactObject(value, ValueEffectProperties);
                effect = new StoryThreadEffect(
                    operation,
                    null,
                    null,
                    null,
                    RequiredString(value, "value"));
            }
            else
            {
                throw Invalid();
            }

            if (!StoryThreadContractRules.IsValid(effect)) throw Invalid();
            return effect;
        }

        private static IReadOnlyList<string> ParseIdArray(
            StoryJsonValue value,
            int maximumCount)
        {
            RequireKind(value, StoryJsonKind.Array);
            if (value.ArrayValue.Count > maximumCount) throw Invalid();
            var result = new string[value.ArrayValue.Count];
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < value.ArrayValue.Count; index++)
            {
                StoryJsonValue item = value.ArrayValue[index];
                if (item.Kind != StoryJsonKind.String ||
                    !StoryThreadContractRules.IsStableId(item.StringValue) ||
                    !unique.Add(item.StringValue))
                {
                    throw Invalid();
                }
                result[index] = item.StringValue;
            }
            return result;
        }

        private static void RequireExactObject(StoryJsonValue value, string[] properties)
        {
            RequireKind(value, StoryJsonKind.Object);
            if (value.ObjectValue.Count != properties.Length) throw Invalid();
            var expected = new HashSet<string>(properties, StringComparer.Ordinal);
            foreach (string property in value.ObjectValue.Keys)
            {
                if (!expected.Contains(property)) throw Invalid();
            }
        }

        private static StoryJsonValue Required(StoryJsonValue owner, string property)
        {
            if (owner == null ||
                owner.Kind != StoryJsonKind.Object ||
                !owner.TryGetProperty(property, out StoryJsonValue value))
            {
                throw Invalid();
            }
            return value;
        }

        private static string RequiredString(StoryJsonValue owner, string property)
        {
            StoryJsonValue value = Required(owner, property);
            if (value.Kind != StoryJsonKind.String) throw Invalid();
            return value.StringValue;
        }

        private static bool RequiredBoolean(StoryJsonValue owner, string property)
        {
            StoryJsonValue value = Required(owner, property);
            if (value.Kind != StoryJsonKind.Boolean) throw Invalid();
            return value.BooleanValue;
        }

        private static double RequiredFiniteNumber(StoryJsonValue owner, string property)
        {
            StoryJsonValue value = Required(owner, property);
            if (value.Kind != StoryJsonKind.Number ||
                !double.TryParse(
                    value.NumberToken,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number) ||
                double.IsNaN(number) ||
                double.IsInfinity(number))
            {
                throw Invalid();
            }
            return number;
        }

        private static void RequireKind(StoryJsonValue value, StoryJsonKind kind)
        {
            if (value == null || value.Kind != kind) throw Invalid();
        }

        private static StoryThreadContractException Invalid()
        {
            return new StoryThreadContractException(
                "The story thread response does not satisfy the approved contract.");
        }
    }
}
