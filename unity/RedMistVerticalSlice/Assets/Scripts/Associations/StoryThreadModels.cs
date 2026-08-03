using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Lingmai.RedMist
{
    public sealed class StoryThreadContractException : Exception
    {
        public StoryThreadContractException(string message)
            : base(string.IsNullOrWhiteSpace(message)
                ? "The story thread contract is invalid."
                : message)
        {
        }
    }

    public sealed class StoryThreadEffect
    {
        public StoryThreadEffect(
            string op,
            string targetRef,
            string field,
            double? delta,
            string value)
        {
            Op = op;
            TargetRef = targetRef;
            Field = field;
            Delta = delta;
            Value = value;
        }

        public string Op { get; }
        public string TargetRef { get; }
        public string Field { get; }
        public double? Delta { get; }
        public string Value { get; }
    }

    public sealed class StoryThreadInjection
    {
        public StoryThreadInjection(
            string nodeId,
            string point,
            string text,
            IEnumerable<StoryThreadEffect> effects)
        {
            NodeId = nodeId;
            Point = point;
            Text = text;
            Effects = Array.AsReadOnly(Copy(effects));
        }

        public string NodeId { get; }
        public string Point { get; }
        public string Text { get; }
        public IReadOnlyList<StoryThreadEffect> Effects { get; }

        private static StoryThreadEffect[] Copy(IEnumerable<StoryThreadEffect> source)
        {
            if (source == null) return Array.Empty<StoryThreadEffect>();
            var result = new List<StoryThreadEffect>();
            foreach (StoryThreadEffect effect in source) result.Add(effect);
            return result.ToArray();
        }
    }

    public sealed class StoryThread
    {
        public StoryThread(
            string schemaVersion,
            string threadId,
            string templateId,
            string playerId,
            IReadOnlyDictionary<string, string> resolvedParams,
            IEnumerable<StoryThreadInjection> injections,
            IEnumerable<string> mediaRefs,
            bool expiresAtChapterEnd,
            string auditRef,
            bool fallbackUsed)
        {
            SchemaVersion = schemaVersion;
            ThreadId = threadId;
            TemplateId = templateId;
            PlayerId = playerId;
            ResolvedParams = FreezeDictionary(resolvedParams);
            Injections = Array.AsReadOnly(Copy(injections));
            MediaRefs = Array.AsReadOnly(Copy(mediaRefs));
            ExpiresAtChapterEnd = expiresAtChapterEnd;
            AuditRef = auditRef;
            FallbackUsed = fallbackUsed;
        }

        public string SchemaVersion { get; }
        public string ThreadId { get; }
        public string TemplateId { get; }
        public string PlayerId { get; }
        public IReadOnlyDictionary<string, string> ResolvedParams { get; }
        public IReadOnlyList<StoryThreadInjection> Injections { get; }
        public IReadOnlyList<string> MediaRefs { get; }
        public bool ExpiresAtChapterEnd { get; }
        public string AuditRef { get; }
        public bool FallbackUsed { get; }

        internal StoryThread RebindPlayer(string playerId)
        {
            return new StoryThread(
                SchemaVersion,
                ThreadId,
                TemplateId,
                playerId,
                ResolvedParams,
                Injections,
                MediaRefs,
                ExpiresAtChapterEnd,
                AuditRef,
                FallbackUsed);
        }

        private static IReadOnlyDictionary<string, string> FreezeDictionary(
            IReadOnlyDictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (KeyValuePair<string, string> item in source)
                    result.Add(item.Key, item.Value);
            }
            return new ReadOnlyDictionary<string, string>(result);
        }

        private static StoryThreadInjection[] Copy(IEnumerable<StoryThreadInjection> source)
        {
            if (source == null) return Array.Empty<StoryThreadInjection>();
            var result = new List<StoryThreadInjection>();
            foreach (StoryThreadInjection injection in source) result.Add(injection);
            return result.ToArray();
        }

        private static string[] Copy(IEnumerable<string> source)
        {
            if (source == null) return Array.Empty<string>();
            var result = new List<string>();
            foreach (string value in source) result.Add(value);
            return result.ToArray();
        }
    }

    public static class StoryThreadValidator
    {
        public static void Validate(StoryThread thread, StoryThreadContext context)
        {
            string failure;
            if (!TryValidate(thread, context, out failure))
                throw new StoryThreadContractException(failure);
        }

        public static void Validate(StoryThreadContext context, StoryThread thread)
        {
            Validate(thread, context);
        }

        public static bool TryValidate(StoryThread thread, StoryThreadContext context)
        {
            string ignored;
            return TryValidate(thread, context, out ignored);
        }

        public static bool TryValidate(StoryThreadContext context, StoryThread thread)
        {
            return TryValidate(thread, context);
        }

        public static bool TryValidate(
            StoryThread thread,
            StoryThreadContext context,
            out string failure)
        {
            failure = "The story thread contract is invalid.";
            if (!StoryThreadContractRules.IsValid(thread)) return false;
            if (!StoryThreadContractRules.IsValid(context))
            {
                failure = "The story thread context is invalid.";
                return false;
            }
            if (!string.Equals(thread.PlayerId, context.PlayerId, StringComparison.Ordinal))
            {
                failure = "The story thread belongs to another player.";
                return false;
            }

            foreach (StoryThreadInjection injection in thread.Injections)
            {
                if (!context.AllowsInjection(injection.NodeId, injection.Point))
                {
                    failure = "The story thread targets an unavailable injection point.";
                    return false;
                }
            }

            foreach (string mediaRef in thread.MediaRefs)
            {
                if (!context.MediaAssets.ContainsKey(mediaRef))
                {
                    failure = "The story thread references unavailable media.";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        public static bool TryValidate(
            StoryThreadContext context,
            StoryThread thread,
            out string failure)
        {
            return TryValidate(thread, context, out failure);
        }
    }

    internal static class StoryThreadContractRules
    {
        internal const string SchemaVersion = "1.0.0";
        internal const int MaximumDocumentBytes = 65536;
        internal const int MaximumCollectionCount = 16;
        internal const int MaximumTextLength = 512;

        private static readonly HashSet<string> InjectionPoints =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "node_intro",
                "travel_event",
                "npc_mention"
            };

        private static readonly HashSet<string> RelationshipFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "trust",
                "respect",
                "fear",
                "debt",
                "suspicion",
                "affection"
            };

        internal static bool IsValid(StoryThread thread)
        {
            if (thread == null ||
                !string.Equals(thread.SchemaVersion, SchemaVersion, StringComparison.Ordinal) ||
                !IsPrefixedId(thread.ThreadId, "thr.") ||
                !IsPrefixedId(thread.TemplateId, "assoc.") ||
                !IsPlayerId(thread.PlayerId) ||
                !IsPrefixedId(thread.AuditRef, "genjob.") ||
                !thread.ExpiresAtChapterEnd ||
                thread.ResolvedParams == null ||
                thread.ResolvedParams.Count > MaximumCollectionCount ||
                thread.Injections == null ||
                thread.Injections.Count < 1 ||
                thread.Injections.Count > MaximumCollectionCount ||
                thread.MediaRefs == null ||
                thread.MediaRefs.Count > MaximumCollectionCount)
            {
                return false;
            }

            var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> parameter in thread.ResolvedParams)
            {
                if (!IsParameterName(parameter.Key) ||
                    !parameterNames.Add(parameter.Key) ||
                    !IsStableId(parameter.Value)) return false;
            }

            foreach (StoryThreadInjection injection in thread.Injections)
            {
                if (!IsValid(injection)) return false;
            }

            var media = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string mediaRef in thread.MediaRefs)
            {
                if (!IsStableId(mediaRef) || !media.Add(mediaRef)) return false;
            }

            return true;
        }

        internal static bool IsValid(StoryThreadInjection injection)
        {
            if (injection == null ||
                !IsStableId(injection.NodeId) ||
                !IsInjectionPoint(injection.Point) ||
                !IsSafeText(injection.Text) ||
                injection.Effects == null ||
                injection.Effects.Count > MaximumCollectionCount)
            {
                return false;
            }

            foreach (StoryThreadEffect effect in injection.Effects)
            {
                if (!IsValid(effect)) return false;
            }
            return true;
        }

        internal static bool IsValid(StoryThreadEffect effect)
        {
            if (effect == null) return false;
            if (string.Equals(effect.Op, "relationship", StringComparison.Ordinal))
            {
                return IsStableId(effect.TargetRef) &&
                       RelationshipFields.Contains(effect.Field) &&
                       effect.Delta.HasValue &&
                       IsFinite(effect.Delta.Value) &&
                       effect.Delta.Value >= -10d &&
                       effect.Delta.Value <= 10d &&
                       effect.Value == null;
            }

            string prefix;
            if (string.Equals(effect.Op, "clue", StringComparison.Ordinal))
                prefix = "clue.";
            else if (string.Equals(effect.Op, "branch_unlock", StringComparison.Ordinal))
                prefix = "branch.";
            else
                return false;

            return effect.TargetRef == null &&
                   effect.Field == null &&
                   !effect.Delta.HasValue &&
                   IsPrefixedId(effect.Value, prefix);
        }

        internal static bool IsValid(StoryThreadContext context)
        {
            if (context == null ||
                !IsPlayerId(context.PlayerId) ||
                context.BundleIdentity == null ||
                !IsStableId(context.BundleIdentity.BundleId) ||
                !IsVersion(context.BundleIdentity.Version) ||
                !IsSha256(context.BundleIdentity.ContentHash) ||
                (context.Route != PlayerRoute.ShenYan &&
                 context.Route != PlayerRoute.ChuMingqi) ||
                !IsStableId(context.UpcomingNodeId) ||
                context.Nodes == null ||
                !context.Nodes.ContainsKey(context.UpcomingNodeId) ||
                context.MediaAssets == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, StoryNode> pair in context.Nodes)
            {
                StoryNode node = pair.Value;
                if (!IsStableId(pair.Key) ||
                    node == null ||
                    !string.Equals(pair.Key, node.id, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            foreach (KeyValuePair<string, StoryMediaAsset> pair in context.MediaAssets)
            {
                if (!IsStableId(pair.Key) ||
                    pair.Value == null ||
                    !string.Equals(pair.Key, pair.Value.Id, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsInjectionPoint(string value)
        {
            return value != null && InjectionPoints.Contains(value);
        }

        internal static bool IsStableId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 160) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid = (character >= 'A' && character <= 'Z') ||
                             (character >= 'a' && character <= 'z') ||
                             (character >= '0' && character <= '9') ||
                             (index > 0 && (character == '.' || character == '_' ||
                                            character == ':' || character == '-'));
                if (!valid) return false;
            }
            return true;
        }

        internal static bool IsPrefixedId(string value, string prefix)
        {
            return IsStableId(value) &&
                   value.Length > prefix.Length &&
                   value.StartsWith(prefix, StringComparison.Ordinal) &&
                   IsAsciiAlphaNumeric(value[prefix.Length]);
        }

        internal static bool IsPlayerId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 3 || value.Length > 96 ||
                !value.StartsWith("p.", StringComparison.Ordinal)) return false;
            bool needsValue = true;
            for (int index = 2; index < value.Length; index++)
            {
                char character = value[index];
                if ((character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9'))
                {
                    needsValue = false;
                }
                else if ((character == '.' || character == '_' || character == '-') &&
                         !needsValue)
                {
                    needsValue = true;
                }
                else
                {
                    return false;
                }
            }
            return !needsValue;
        }

        internal static bool IsParameterName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiLetter(value[0]))
                return false;
            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAsciiLetter(character) &&
                    !(character >= '0' && character <= '9') &&
                    character != '_') return false;
            }
            return true;
        }

        internal static bool IsSafeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextLength) return false;
            foreach (char character in value)
            {
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsControl(character) ||
                    char.IsSurrogate(character) ||
                    category == UnicodeCategory.Format ||
                    category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator)
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 71 ||
                !value.StartsWith("sha256:", StringComparison.Ordinal)) return false;
            for (int index = 7; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static bool IsVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
            foreach (char character in value)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character)) return false;
            }
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                   (value >= 'a' && value <= 'z');
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return IsAsciiLetter(value) || (value >= '0' && value <= '9');
        }
    }
}
