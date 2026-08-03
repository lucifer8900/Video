using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Lingmai.RedMist
{
    public sealed class LedgerContractException : Exception
    {
        public LedgerContractException(string message)
            : base(message)
        {
        }
    }

    public enum LedgerEventType
    {
        DebtIncurred,
        DebtRepaid,
        SecretExposed,
        PromiseMade,
        PromiseBroken,
        NpcRescued,
        NpcAbandoned,
        EnemySpared,
        ItemGained,
        QuestExpired,
        TrumpCardRevealed
    }

    public sealed class LedgerEntryKey : IEquatable<LedgerEntryKey>
    {
        public LedgerEntryKey(string playerId, string entryId)
        {
            if (!LedgerContractRules.IsPlayerId(playerId))
                throw new ArgumentException("The player identifier is invalid.", nameof(playerId));
            if (!LedgerContractRules.IsEntryId(entryId))
                throw new ArgumentException("The ledger entry identifier is invalid.", nameof(entryId));
            PlayerId = playerId;
            EntryId = entryId;
        }

        public string PlayerId { get; }
        public string EntryId { get; }

        public bool Equals(LedgerEntryKey other)
        {
            return other != null &&
                   string.Equals(PlayerId, other.PlayerId, StringComparison.Ordinal) &&
                   string.Equals(EntryId, other.EntryId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as LedgerEntryKey);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(PlayerId) * 397) ^
                       StringComparer.Ordinal.GetHashCode(EntryId);
            }
        }

        public override string ToString() => "LedgerEntryKey(Redacted)";
    }

    public sealed class LedgerEvent
    {
        private readonly ReadOnlyCollection<string> _actors;
        private readonly ReadOnlyCollection<string> _factRefs;
        private readonly ReadOnlyDictionary<string, object> _payload;

        public LedgerEvent(
            string entryId,
            string playerId,
            LedgerEventType type,
            IReadOnlyList<string> actors,
            int severity,
            string chapter,
            long worldClock,
            string sourceNodeId,
            IReadOnlyList<string> factRefs,
            IReadOnlyDictionary<string, string> payload)
            : this(
                entryId,
                playerId,
                type,
                actors,
                severity,
                chapter,
                worldClock,
                sourceNodeId,
                factRefs,
                ConvertStringPayload(payload))
        {
        }

        public LedgerEvent(
            string entryId,
            string playerId,
            LedgerEventType type,
            IReadOnlyList<string> actors,
            int severity,
            string chapter,
            long worldClock,
            string sourceNodeId,
            IReadOnlyList<string> factRefs,
            IReadOnlyDictionary<string, object> payload)
        {
            if (!LedgerContractRules.IsEntryId(entryId))
                throw new ArgumentException("The ledger entry identifier is invalid.", nameof(entryId));
            if (!LedgerContractRules.IsPlayerId(playerId))
                throw new ArgumentException("The player identifier is invalid.", nameof(playerId));
            if (!Enum.IsDefined(typeof(LedgerEventType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            if (actors == null) throw new ArgumentNullException(nameof(actors));
            if (actors.Count < 1 || actors.Count > 8)
                throw new ArgumentOutOfRangeException(nameof(actors));
            if (severity < 1 || severity > 5)
                throw new ArgumentOutOfRangeException(nameof(severity));
            if (!LedgerContractRules.IsChapter(chapter))
                throw new ArgumentException("The chapter identifier is invalid.", nameof(chapter));
            if (worldClock < 0 || worldClock > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(worldClock));
            if (!LedgerContractRules.IsSourceNodeId(sourceNodeId))
                throw new ArgumentException("The source node identifier is invalid.", nameof(sourceNodeId));
            if (factRefs == null) throw new ArgumentNullException(nameof(factRefs));
            if (factRefs.Count > 16)
                throw new ArgumentOutOfRangeException(nameof(factRefs));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Count > 16)
                throw new ArgumentOutOfRangeException(nameof(payload));

            string[] actorCopy = ValidateUniqueIds(actors, false, nameof(actors));
            string[] factCopy = ValidateUniqueIds(factRefs, true, nameof(factRefs));
            var payloadCopy = new Dictionary<string, object>(StringComparer.Ordinal);
            var payloadKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in payload)
            {
                if (!LedgerContractRules.IsPayloadKey(pair.Key) ||
                    LedgerContractRules.IsSensitivePayloadKey(pair.Key) ||
                    !payloadKeys.Add(pair.Key))
                {
                    throw new ArgumentException("The ledger payload key is invalid.", nameof(payload));
                }
                ValidatePayloadValue(pair.Value, nameof(payload));
                payloadCopy.Add(pair.Key, pair.Value);
            }
            ValidatePayloadWhitelist(type, payloadCopy, nameof(payload));

            EntryId = entryId;
            PlayerId = playerId;
            Type = type;
            _actors = Array.AsReadOnly(actorCopy);
            Severity = severity;
            Chapter = chapter;
            WorldClock = worldClock;
            SourceNodeId = sourceNodeId;
            _factRefs = Array.AsReadOnly(factCopy);
            _payload = new ReadOnlyDictionary<string, object>(payloadCopy);
        }

        public string SchemaVersion => LedgerEventCodec.SchemaVersion;
        public string EntryId { get; }
        public string PlayerId { get; }
        public LedgerEventType Type { get; }
        public IReadOnlyList<string> Actors => _actors;
        public int Severity { get; }
        public string Chapter { get; }
        public long WorldClock { get; }
        public string SourceNodeId { get; }
        public IReadOnlyList<string> FactRefs => _factRefs;
        public IReadOnlyDictionary<string, object> Payload => _payload;
        public LedgerEntryKey Key => new LedgerEntryKey(PlayerId, EntryId);

        public override string ToString()
        {
            return "LedgerEvent(Type=" + LedgerEventCodec.ToWireType(Type) +
                   ", Severity=" + Severity.ToString(CultureInfo.InvariantCulture) +
                   ", ActorCount=" + Actors.Count.ToString(CultureInfo.InvariantCulture) +
                   ", PayloadFieldCount=" + Payload.Count.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static IReadOnlyDictionary<string, object> ConvertStringPayload(
            IReadOnlyDictionary<string, string> payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in payload) result.Add(pair.Key, pair.Value);
            return result;
        }

        private static string[] ValidateUniqueIds(
            IReadOnlyList<string> source,
            bool fact,
            string parameterName)
        {
            var copy = new string[source.Count];
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < source.Count; index++)
            {
                string value = source[index];
                bool valid = fact
                    ? LedgerContractRules.IsFactId(value)
                    : LedgerContractRules.IsEntityId(value);
                if (!valid || !unique.Add(value))
                    throw new ArgumentException("A ledger reference is invalid or duplicated.", parameterName);
                copy[index] = value;
            }
            return copy;
        }

        private static void ValidatePayloadValue(object value, string parameterName)
        {
            if (value == null || value is bool) return;
            if (value is string text)
            {
                if (text.Length > 512)
                    throw new ArgumentOutOfRangeException(parameterName);
                return;
            }
            if (!LedgerContractRules.TryGetBoundedNumber(value, out _))
                throw new ArgumentException("The ledger payload value is invalid.", parameterName);
        }

        private static void ValidatePayloadWhitelist(
            LedgerEventType type,
            IReadOnlyDictionary<string, object> payload,
            string parameterName)
        {
            foreach (KeyValuePair<string, object> pair in payload)
            {
                bool valid;
                switch (type)
                {
                    case LedgerEventType.DebtIncurred:
                        valid = IsTokenField(pair, "debtKind");
                        break;
                    case LedgerEventType.DebtRepaid:
                        valid = IsLedgerIdField(pair, "debtRef");
                        break;
                    case LedgerEventType.SecretExposed:
                        valid = IsStableIdField(pair, "secretRef");
                        break;
                    case LedgerEventType.PromiseMade:
                    case LedgerEventType.PromiseBroken:
                        valid = IsStableIdField(pair, "promiseRef");
                        break;
                    case LedgerEventType.NpcRescued:
                    case LedgerEventType.NpcAbandoned:
                    case LedgerEventType.EnemySpared:
                        valid = IsStableIdField(pair, "npcRef");
                        break;
                    case LedgerEventType.ItemGained:
                        valid = IsStableIdField(pair, "itemRef") ||
                                IsQuantityField(pair, "quantity");
                        break;
                    case LedgerEventType.QuestExpired:
                        valid = IsStableIdField(pair, "questRef");
                        break;
                    case LedgerEventType.TrumpCardRevealed:
                        valid = IsStableIdField(pair, "abilityRef");
                        break;
                    default:
                        valid = false;
                        break;
                }
                if (!valid)
                    throw new ArgumentException(
                        "The ledger payload is not allowed for this event type.",
                        parameterName);
            }
        }

        private static bool IsTokenField(KeyValuePair<string, object> pair, string expectedKey)
        {
            return string.Equals(pair.Key, expectedKey, StringComparison.Ordinal) &&
                   pair.Value is string value && LedgerContractRules.IsToken(value);
        }

        private static bool IsStableIdField(KeyValuePair<string, object> pair, string expectedKey)
        {
            return string.Equals(pair.Key, expectedKey, StringComparison.Ordinal) &&
                   pair.Value is string value && LedgerContractRules.IsEntityId(value);
        }

        private static bool IsLedgerIdField(KeyValuePair<string, object> pair, string expectedKey)
        {
            return string.Equals(pair.Key, expectedKey, StringComparison.Ordinal) &&
                   pair.Value is string value && LedgerContractRules.IsEntryId(value);
        }

        private static bool IsQuantityField(KeyValuePair<string, object> pair, string expectedKey)
        {
            return string.Equals(pair.Key, expectedKey, StringComparison.Ordinal) &&
                   LedgerContractRules.TryGetInteger(pair.Value, out long quantity) &&
                   quantity >= 1 && quantity <= 999999;
        }
    }

    public static class LedgerEventCodec
    {
        public const string SchemaVersion = "1.0.0";
        public const int MaximumPayloadBytes = 4096;
        public const int MaximumSerializedBytes = 16384;

        private static readonly string[] ExactProperties =
        {
            "schemaVersion", "entryId", "playerId", "type", "actors", "severity",
            "chapter", "worldClock", "sourceNodeId", "factRefs", "payload"
        };

        public static string Encode(LedgerEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var output = new StringBuilder(512);
            output.Append('{');
            AppendProperty(output, "schemaVersion", SchemaVersion, true);
            AppendProperty(output, "entryId", item.EntryId, false);
            AppendProperty(output, "playerId", item.PlayerId, false);
            AppendProperty(output, "type", ToWireType(item.Type), false);
            output.Append(",\"actors\":");
            AppendStringArray(output, item.Actors);
            output.Append(",\"severity\":");
            output.Append(item.Severity.ToString(CultureInfo.InvariantCulture));
            AppendProperty(output, "chapter", item.Chapter, false);
            output.Append(",\"worldClock\":");
            output.Append(item.WorldClock.ToString(CultureInfo.InvariantCulture));
            AppendProperty(output, "sourceNodeId", item.SourceNodeId, false);
            output.Append(",\"factRefs\":");
            AppendStringArray(output, item.FactRefs);
            output.Append(",\"payload\":");
            var payloadOutput = new StringBuilder(256);
            payloadOutput.Append('{');
            var payloadKeys = new List<string>(item.Payload.Keys);
            payloadKeys.Sort(StringComparer.Ordinal);
            for (int index = 0; index < payloadKeys.Count; index++)
            {
                if (index > 0) payloadOutput.Append(',');
                string key = payloadKeys[index];
                AppendString(payloadOutput, key);
                payloadOutput.Append(':');
                AppendPayloadValue(payloadOutput, item.Payload[key]);
            }
            payloadOutput.Append('}');
            string payloadJson = payloadOutput.ToString();
            if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
                throw new LedgerContractException("The ledger payload exceeds its size limit.");
            output.Append(payloadJson);
            output.Append('}');
            string json = output.ToString();
            if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes)
                throw new LedgerContractException("The ledger event exceeds its size limit.");
            return json;
        }

        public static LedgerEvent Decode(string json)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes)
                throw Invalid();
            if (!TryGetRootPayloadBytes(json, out int payloadBytes) ||
                payloadBytes > MaximumPayloadBytes)
            {
                throw Invalid();
            }
            try
            {
                StoryJsonValue root = StoryJson.Parse(json);
                RequireKind(root, StoryJsonKind.Object);
                EnsureExactProperties(root, ExactProperties);
                if (!string.Equals(RequiredString(root, "schemaVersion"), SchemaVersion, StringComparison.Ordinal))
                    throw Invalid();

                string entryId = RequiredString(root, "entryId");
                string playerId = RequiredString(root, "playerId");
                LedgerEventType type = ParseWireType(RequiredString(root, "type"));
                string[] actors = RequiredStringArray(root, "actors", 1, 8);
                int severity = checked((int)RequiredInteger(root, "severity"));
                string chapter = RequiredString(root, "chapter");
                long worldClock = RequiredInteger(root, "worldClock");
                string sourceNodeId = RequiredString(root, "sourceNodeId");
                string[] facts = RequiredStringArray(root, "factRefs", 0, 16);
                IReadOnlyDictionary<string, object> payload = RequiredPayload(root);
                return new LedgerEvent(
                    entryId, playerId, type, actors, severity, chapter, worldClock,
                    sourceNodeId, facts, payload);
            }
            catch (LedgerContractException)
            {
                throw;
            }
            catch (Exception error) when (
                error is StoryJsonException ||
                error is ArgumentException ||
                error is OverflowException ||
                error is FormatException)
            {
                throw Invalid();
            }
        }

        public static string ToWireType(LedgerEventType type)
        {
            switch (type)
            {
                case LedgerEventType.DebtIncurred: return "debt_incurred";
                case LedgerEventType.DebtRepaid: return "debt_repaid";
                case LedgerEventType.SecretExposed: return "secret_exposed";
                case LedgerEventType.PromiseMade: return "promise_made";
                case LedgerEventType.PromiseBroken: return "promise_broken";
                case LedgerEventType.NpcRescued: return "npc_rescued";
                case LedgerEventType.NpcAbandoned: return "npc_abandoned";
                case LedgerEventType.EnemySpared: return "enemy_spared";
                case LedgerEventType.ItemGained: return "item_gained";
                case LedgerEventType.QuestExpired: return "quest_expired";
                case LedgerEventType.TrumpCardRevealed: return "trump_card_revealed";
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static LedgerEventType ParseWireType(string value)
        {
            switch (value)
            {
                case "debt_incurred": return LedgerEventType.DebtIncurred;
                case "debt_repaid": return LedgerEventType.DebtRepaid;
                case "secret_exposed": return LedgerEventType.SecretExposed;
                case "promise_made": return LedgerEventType.PromiseMade;
                case "promise_broken": return LedgerEventType.PromiseBroken;
                case "npc_rescued": return LedgerEventType.NpcRescued;
                case "npc_abandoned": return LedgerEventType.NpcAbandoned;
                case "enemy_spared": return LedgerEventType.EnemySpared;
                case "item_gained": return LedgerEventType.ItemGained;
                case "quest_expired": return LedgerEventType.QuestExpired;
                case "trump_card_revealed": return LedgerEventType.TrumpCardRevealed;
                default: throw Invalid();
            }
        }

        internal static void AppendString(StringBuilder output, string value)
        {
            output.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (character >= 0x20 && character <= 0x7e)
                        {
                            output.Append(character);
                        }
                        else
                        {
                            output.Append("\\u");
                            output.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        break;
                }
            }
            output.Append('"');
        }

        private static void AppendProperty(
            StringBuilder output,
            string property,
            string value,
            bool first)
        {
            if (!first) output.Append(',');
            AppendString(output, property);
            output.Append(':');
            AppendString(output, value);
        }

        private static void AppendStringArray(StringBuilder output, IReadOnlyList<string> values)
        {
            output.Append('[');
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0) output.Append(',');
                AppendString(output, values[index]);
            }
            output.Append(']');
        }

        private static void AppendPayloadValue(StringBuilder output, object value)
        {
            if (value == null)
            {
                output.Append("null");
                return;
            }
            if (value is string text)
            {
                AppendString(output, text);
                return;
            }
            if (value is bool boolean)
            {
                output.Append(boolean ? "true" : "false");
                return;
            }
            if (!LedgerContractRules.TryGetBoundedNumber(value, out string number)) throw Invalid();
            output.Append(number);
        }

        private static IReadOnlyDictionary<string, object> RequiredPayload(StoryJsonValue root)
        {
            StoryJsonValue value = RequiredProperty(root, "payload");
            RequireKind(value, StoryJsonKind.Object);
            if (value.ObjectValue.Count > 16) throw Invalid();
            if (StoryJson.SerializeCanonical(value).Length > MaximumPayloadBytes) throw Invalid();
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StoryJsonValue> pair in value.ObjectValue)
            {
                switch (pair.Value.Kind)
                {
                    case StoryJsonKind.String:
                        result.Add(pair.Key, pair.Value.StringValue);
                        break;
                    case StoryJsonKind.Boolean:
                        result.Add(pair.Key, pair.Value.BooleanValue);
                        break;
                    case StoryJsonKind.Null:
                        result.Add(pair.Key, null);
                        break;
                    case StoryJsonKind.Number:
                        if (!decimal.TryParse(
                                pair.Value.NumberToken,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out decimal number) ||
                            number < -1000000000m || number > 1000000000m)
                        {
                            throw Invalid();
                        }
                        result.Add(pair.Key, number);
                        break;
                    default:
                        throw Invalid();
                }
            }
            return result;
        }

        private static string[] RequiredStringArray(
            StoryJsonValue root,
            string property,
            int minimum,
            int maximum)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            RequireKind(value, StoryJsonKind.Array);
            if (value.ArrayValue.Count < minimum || value.ArrayValue.Count > maximum) throw Invalid();
            var result = new string[value.ArrayValue.Count];
            for (int index = 0; index < result.Length; index++)
            {
                RequireKind(value.ArrayValue[index], StoryJsonKind.String);
                result[index] = value.ArrayValue[index].StringValue;
            }
            return result;
        }

        private static long RequiredInteger(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            RequireKind(value, StoryJsonKind.Number);
            if (!long.TryParse(value.NumberToken, NumberStyles.None, CultureInfo.InvariantCulture, out long result))
                throw Invalid();
            return result;
        }

        private static string RequiredString(StoryJsonValue root, string property)
        {
            StoryJsonValue value = RequiredProperty(root, property);
            RequireKind(value, StoryJsonKind.String);
            return value.StringValue;
        }

        private static StoryJsonValue RequiredProperty(StoryJsonValue root, string property)
        {
            if (!root.TryGetProperty(property, out StoryJsonValue value)) throw Invalid();
            return value;
        }

        private static void EnsureExactProperties(StoryJsonValue root, string[] properties)
        {
            if (root.ObjectValue.Count != properties.Length) throw Invalid();
            for (int index = 0; index < properties.Length; index++)
            {
                if (!root.ObjectValue.ContainsKey(properties[index])) throw Invalid();
            }
        }

        private static void RequireKind(StoryJsonValue value, StoryJsonKind kind)
        {
            if (value == null || value.Kind != kind) throw Invalid();
        }

        private static LedgerContractException Invalid()
        {
            return new LedgerContractException("The ledger event contract is invalid.");
        }

        private static bool TryGetRootPayloadBytes(string json, out int payloadBytes)
        {
            payloadBytes = 0;
            int index = 0;
            SkipWhitespace(json, ref index);
            if (!TryConsume(json, ref index, '{')) return false;
            bool found = false;
            SkipWhitespace(json, ref index);
            if (TryConsume(json, ref index, '}')) return false;
            while (index < json.Length)
            {
                int propertyStart = index;
                if (!SkipString(json, ref index)) return false;
                bool isPayload = index - propertyStart == 9 &&
                                 string.CompareOrdinal(json, propertyStart, "\"payload\"", 0, 9) == 0;
                SkipWhitespace(json, ref index);
                if (!TryConsume(json, ref index, ':')) return false;
                SkipWhitespace(json, ref index);
                int valueStart = index;
                if (!SkipValue(json, ref index)) return false;
                int valueEnd = index;
                if (isPayload)
                {
                    if (found) return false;
                    found = true;
                    payloadBytes = Encoding.UTF8.GetByteCount(
                        json.Substring(valueStart, valueEnd - valueStart));
                }
                SkipWhitespace(json, ref index);
                if (TryConsume(json, ref index, '}'))
                {
                    SkipWhitespace(json, ref index);
                    return found && index == json.Length;
                }
                if (!TryConsume(json, ref index, ',')) return false;
                SkipWhitespace(json, ref index);
            }
            return false;
        }

        private static bool SkipValue(string json, ref int index)
        {
            if (index >= json.Length) return false;
            if (json[index] == '"') return SkipString(json, ref index);
            if (json[index] == '{' || json[index] == '[')
            {
                var stack = new Stack<char>();
                stack.Push(json[index] == '{' ? '}' : ']');
                index++;
                while (index < json.Length && stack.Count > 0)
                {
                    char character = json[index];
                    if (character == '"')
                    {
                        if (!SkipString(json, ref index)) return false;
                        continue;
                    }
                    if (character == '{') stack.Push('}');
                    else if (character == '[') stack.Push(']');
                    else if (character == '}' || character == ']')
                    {
                        if (stack.Pop() != character) return false;
                    }
                    index++;
                }
                return stack.Count == 0;
            }
            int start = index;
            while (index < json.Length)
            {
                char character = json[index];
                if (character == ',' || character == '}' ||
                    character == ' ' || character == '\t' ||
                    character == '\r' || character == '\n') break;
                index++;
            }
            return index > start;
        }

        private static bool SkipString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') return false;
            index++;
            while (index < json.Length)
            {
                char character = json[index++];
                if (character == '"') return true;
                if (character != '\\') continue;
                if (index >= json.Length) return false;
                char escape = json[index++];
                if (escape != 'u') continue;
                if (index + 4 > json.Length) return false;
                index += 4;
            }
            return false;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length)
            {
                char character = json[index];
                if (character != ' ' && character != '\t' &&
                    character != '\r' && character != '\n') return;
                index++;
            }
        }

        private static bool TryConsume(string json, ref int index, char expected)
        {
            if (index >= json.Length || json[index] != expected) return false;
            index++;
            return true;
        }
    }

    internal static class LedgerContractRules
    {
        private static readonly string[] SensitivePayloadFragments =
        {
            "transcript", "audio", "recording", "device", "email", "path", "displaytext"
        };

        public static bool IsEntryId(string value) => IsOpaqueId(value, "led.", 5, 96);
        public static bool IsPlayerId(string value) => IsOpaqueId(value, "p.", 3, 96);

        public static bool IsEntityId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 160 || !IsAsciiAlphaNumeric(value[0]))
                return false;
            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAsciiAlphaNumeric(character) && character != '.' && character != '_' &&
                    character != ':' && character != '-') return false;
            }
            return true;
        }

        public static bool IsFactId(string value)
        {
            return value != null && value.Length >= 6 && value.Length <= 160 &&
                   value.StartsWith("fact.", StringComparison.Ordinal) && IsEntityId(value);
        }

        public static bool IsSourceNodeId(string value) =>
            value != null && value.Length <= 160 && IsEntityId(value);

        public static bool IsChapter(string value) =>
            value != null && value.Length <= 64 && IsEntityId(value);

        public static bool IsPayloadKey(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiAlpha(value[0]))
                return false;
            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAsciiAlphaNumeric(character) && character != '_') return false;
            }
            return true;
        }

        public static bool IsToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 ||
                value[0] < 'a' || value[0] > 'z') return false;
            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '_')) return false;
            }
            return true;
        }

        public static bool IsSensitivePayloadKey(string value)
        {
            var compact = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = char.ToLowerInvariant(value[index]);
                if ((character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9')) compact.Append(character);
            }
            string lower = compact.ToString();
            for (int index = 0; index < SensitivePayloadFragments.Length; index++)
            {
                string fragment = SensitivePayloadFragments[index];
                if (string.Equals(fragment, "path", StringComparison.Ordinal))
                {
                    if (lower.StartsWith(fragment, StringComparison.Ordinal) ||
                        lower.EndsWith(fragment, StringComparison.Ordinal)) return true;
                }
                else if (lower.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool TryGetBoundedNumber(object value, out string formatted)
        {
            formatted = null;
            try
            {
                decimal number;
                switch (value)
                {
                    case byte item: number = item; break;
                    case sbyte item: number = item; break;
                    case short item: number = item; break;
                    case ushort item: number = item; break;
                    case int item: number = item; break;
                    case uint item: number = item; break;
                    case long item: number = item; break;
                    case ulong item: number = item; break;
                    case float item when !float.IsNaN(item) && !float.IsInfinity(item): number = (decimal)item; break;
                    case double item when !double.IsNaN(item) && !double.IsInfinity(item): number = (decimal)item; break;
                    case decimal item: number = item; break;
                    default: return false;
                }
                if (number < -1000000000m || number > 1000000000m) return false;
                formatted = number.ToString("G29", CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        public static bool TryGetInteger(object value, out long result)
        {
            result = 0;
            try
            {
                switch (value)
                {
                    case byte item: result = item; return true;
                    case sbyte item: result = item; return true;
                    case short item: result = item; return true;
                    case ushort item: result = item; return true;
                    case int item: result = item; return true;
                    case uint item: result = item; return true;
                    case long item: result = item; return true;
                    case ulong item when item <= long.MaxValue: result = (long)item; return true;
                    case float item when !float.IsNaN(item) && !float.IsInfinity(item) && item == Math.Truncate(item):
                        result = checked((long)item); return true;
                    case double item when !double.IsNaN(item) && !double.IsInfinity(item) && item == Math.Truncate(item):
                        result = checked((long)item); return true;
                    case decimal item when item == decimal.Truncate(item):
                        result = checked((long)item); return true;
                    default: return false;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool IsOpaqueId(string value, string prefix, int minimum, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length < minimum || value.Length > maximum ||
                !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
            bool expectValue = true;
            for (int index = prefix.Length; index < value.Length; index++)
            {
                char character = value[index];
                if (IsLowerAlphaNumeric(character))
                {
                    expectValue = false;
                }
                else if (character == '.' || character == '_' || character == '-')
                {
                    if (expectValue) return false;
                    expectValue = true;
                }
                else
                {
                    return false;
                }
            }
            return !expectValue;
        }

        private static bool IsAsciiAlpha(char value) =>
            (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');

        private static bool IsAsciiAlphaNumeric(char value) =>
            IsAsciiAlpha(value) || (value >= '0' && value <= '9');

        private static bool IsLowerAlphaNumeric(char value) =>
            (value >= 'a' && value <= 'z') || (value >= '0' && value <= '9');

    }
}
