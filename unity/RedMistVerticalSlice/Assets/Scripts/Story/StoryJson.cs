using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lingmai.RedMist
{
    internal enum StoryJsonKind
    {
        Object,
        Array,
        String,
        Number,
        Boolean,
        Null
    }

    /// <summary>
    /// Small, dependency-free JSON DOM used by the story bundle loader. It deliberately keeps
    /// number tokens verbatim so canonical serialization has the same numeric representation as
    /// System.Text.Json's JsonNode parser/writer pair.
    /// </summary>
    internal sealed class StoryJsonValue
    {
        private StoryJsonValue(StoryJsonKind kind)
        {
            Kind = kind;
        }

        public StoryJsonKind Kind { get; }
        public Dictionary<string, StoryJsonValue> ObjectValue { get; private set; }
        public List<StoryJsonValue> ArrayValue { get; private set; }
        public string StringValue { get; private set; }
        public string NumberToken { get; private set; }
        public bool BooleanValue { get; private set; }

        public static StoryJsonValue Object(Dictionary<string, StoryJsonValue> value) =>
            new StoryJsonValue(StoryJsonKind.Object) { ObjectValue = value };

        public static StoryJsonValue Array(List<StoryJsonValue> value) =>
            new StoryJsonValue(StoryJsonKind.Array) { ArrayValue = value };

        public static StoryJsonValue String(string value) =>
            new StoryJsonValue(StoryJsonKind.String) { StringValue = value };

        public static StoryJsonValue Number(string token) =>
            new StoryJsonValue(StoryJsonKind.Number) { NumberToken = token };

        public static StoryJsonValue Boolean(bool value) =>
            new StoryJsonValue(StoryJsonKind.Boolean) { BooleanValue = value };

        public static StoryJsonValue Null() => new StoryJsonValue(StoryJsonKind.Null);

        public bool TryGetProperty(string name, out StoryJsonValue value)
        {
            value = null;
            return Kind == StoryJsonKind.Object && ObjectValue.TryGetValue(name, out value);
        }
    }

    internal sealed class StoryJsonException : Exception
    {
        public StoryJsonException(string message, int index)
            : base(message + " (character " + index.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Index = index;
        }

        public int Index { get; }
    }

    internal static class StoryJson
    {
        public static StoryJsonValue Parse(string json)
        {
            if (json == null) throw new StoryJsonException("JSON input is null", 0);
            var parser = new Parser(json);
            return parser.ParseDocument();
        }

        public static byte[] SerializeCanonical(
            StoryJsonValue value,
            string omittedRootProperty = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var output = new StringBuilder();
            WriteCanonical(output, value, omittedRootProperty, true);
            return Encoding.UTF8.GetBytes(output.ToString());
        }

        private static void WriteCanonical(
            StringBuilder output,
            StoryJsonValue value,
            string omittedRootProperty,
            bool isRoot)
        {
            switch (value.Kind)
            {
                case StoryJsonKind.Object:
                    output.Append('{');
                    var keys = new List<string>(value.ObjectValue.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    bool firstProperty = true;
                    foreach (string key in keys)
                    {
                        if (isRoot && string.Equals(key, omittedRootProperty, StringComparison.Ordinal))
                            continue;
                        if (!firstProperty) output.Append(',');
                        firstProperty = false;
                        WriteString(output, key);
                        output.Append(':');
                        WriteCanonical(output, value.ObjectValue[key], null, false);
                    }
                    output.Append('}');
                    break;

                case StoryJsonKind.Array:
                    output.Append('[');
                    for (int index = 0; index < value.ArrayValue.Count; index++)
                    {
                        if (index > 0) output.Append(',');
                        WriteCanonical(output, value.ArrayValue[index], null, false);
                    }
                    output.Append(']');
                    break;

                case StoryJsonKind.String:
                    WriteString(output, value.StringValue);
                    break;

                case StoryJsonKind.Number:
                    output.Append(value.NumberToken);
                    break;

                case StoryJsonKind.Boolean:
                    output.Append(value.BooleanValue ? "true" : "false");
                    break;

                case StoryJsonKind.Null:
                    output.Append("null");
                    break;

                default:
                    throw new InvalidOperationException("Unsupported JSON value kind: " + value.Kind);
            }
        }

        private static void WriteString(StringBuilder output, string value)
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
                        if (IsDefaultEncoderSafeAscii(character))
                        {
                            output.Append(character);
                        }
                        else
                        {
                            AppendUnicodeEscape(output, character);
                        }
                        break;
                }
            }
            output.Append('"');
        }

        // System.Text.Encodings.Web.JavaScriptEncoder.Default permits Basic Latin while blocking
        // HTML-sensitive characters (including '+') in addition to JSON syntax/control chars.
        private static bool IsDefaultEncoderSafeAscii(char value)
        {
            if (value < 0x20 || value > 0x7E) return false;
            switch (value)
            {
                case '"':
                case '&':
                case '\'':
                case '+':
                case '<':
                case '>':
                case '`':
                case '\\':
                    return false;
                default:
                    return true;
            }
        }

        private static void AppendUnicodeEscape(StringBuilder output, char value)
        {
            output.Append("\\u");
            output.Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
        }

        private sealed class Parser
        {
            private readonly string _input;
            private int _index;

            public Parser(string input)
            {
                _input = input;
            }

            public StoryJsonValue ParseDocument()
            {
                SkipWhitespace();
                StoryJsonValue value = ParseValue();
                SkipWhitespace();
                if (_index != _input.Length)
                    throw Error("Unexpected trailing content");
                return value;
            }

            private StoryJsonValue ParseValue()
            {
                if (_index >= _input.Length) throw Error("Expected a JSON value");
                switch (_input[_index])
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return StoryJsonValue.String(ParseString());
                    case 't': ReadLiteral("true"); return StoryJsonValue.Boolean(true);
                    case 'f': ReadLiteral("false"); return StoryJsonValue.Boolean(false);
                    case 'n': ReadLiteral("null"); return StoryJsonValue.Null();
                    default:
                        if (_input[_index] == '-' || IsDigit(_input[_index]))
                            return StoryJsonValue.Number(ParseNumber());
                        throw Error("Unexpected character while reading a JSON value");
                }
            }

            private StoryJsonValue ParseObject()
            {
                _index++;
                SkipWhitespace();
                var properties = new Dictionary<string, StoryJsonValue>(StringComparer.Ordinal);
                if (TryConsume('}')) return StoryJsonValue.Object(properties);

                while (true)
                {
                    if (_index >= _input.Length || _input[_index] != '"')
                        throw Error("Expected an object property name");
                    string name = ParseString();
                    if (properties.ContainsKey(name))
                        throw Error("Duplicate object property '" + name + "'");
                    SkipWhitespace();
                    Consume(':');
                    SkipWhitespace();
                    properties.Add(name, ParseValue());
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    Consume(',');
                    SkipWhitespace();
                }

                return StoryJsonValue.Object(properties);
            }

            private StoryJsonValue ParseArray()
            {
                _index++;
                SkipWhitespace();
                var items = new List<StoryJsonValue>();
                if (TryConsume(']')) return StoryJsonValue.Array(items);

                while (true)
                {
                    items.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']')) break;
                    Consume(',');
                    SkipWhitespace();
                }

                return StoryJsonValue.Array(items);
            }

            private string ParseString()
            {
                Consume('"');
                var value = new StringBuilder();
                while (_index < _input.Length)
                {
                    char character = _input[_index++];
                    if (character == '"') return value.ToString();
                    if (character < 0x20)
                        throw Error("Unescaped control character in string");
                    if (character == '\\')
                    {
                        if (_index >= _input.Length) throw Error("Incomplete string escape");
                        char escape = _input[_index++];
                        switch (escape)
                        {
                            case '"': value.Append('"'); break;
                            case '\\': value.Append('\\'); break;
                            case '/': value.Append('/'); break;
                            case 'b': value.Append('\b'); break;
                            case 'f': value.Append('\f'); break;
                            case 'n': value.Append('\n'); break;
                            case 'r': value.Append('\r'); break;
                            case 't': value.Append('\t'); break;
                            case 'u': AppendEscapedUnicode(value); break;
                            default: throw Error("Invalid string escape");
                        }
                    }
                    else if (char.IsHighSurrogate(character))
                    {
                        if (_index >= _input.Length || !char.IsLowSurrogate(_input[_index]))
                            throw Error("Unpaired high surrogate in string");
                        value.Append(character);
                        value.Append(_input[_index++]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        throw Error("Unpaired low surrogate in string");
                    }
                    else
                    {
                        value.Append(character);
                    }
                }

                throw Error("Unterminated string");
            }

            private void AppendEscapedUnicode(StringBuilder value)
            {
                char first = ReadHexCharacter();
                if (char.IsLowSurrogate(first))
                    throw Error("Unpaired low surrogate escape in string");
                value.Append(first);
                if (!char.IsHighSurrogate(first)) return;

                if (_index + 1 >= _input.Length || _input[_index] != '\\' || _input[_index + 1] != 'u')
                    throw Error("High surrogate escape must be followed by a low surrogate escape");
                _index += 2;
                char second = ReadHexCharacter();
                if (!char.IsLowSurrogate(second))
                    throw Error("High surrogate escape must be followed by a low surrogate escape");
                value.Append(second);
            }

            private char ReadHexCharacter()
            {
                if (_index + 4 > _input.Length) throw Error("Incomplete unicode escape");
                int value = 0;
                for (int offset = 0; offset < 4; offset++)
                {
                    int digit = HexValue(_input[_index++]);
                    if (digit < 0) throw Error("Invalid unicode escape");
                    value = (value << 4) | digit;
                }
                return (char)value;
            }

            private string ParseNumber()
            {
                int start = _index;
                if (TryConsume('-'))
                {
                    if (_index >= _input.Length) throw Error("Incomplete number");
                }

                if (TryConsume('0'))
                {
                    if (_index < _input.Length && IsDigit(_input[_index]))
                        throw Error("Leading zero is not allowed in a JSON number");
                }
                else
                {
                    if (_index >= _input.Length || !IsDigitOneToNine(_input[_index]))
                        throw Error("Invalid JSON number");
                    while (_index < _input.Length && IsDigit(_input[_index])) _index++;
                }

                if (TryConsume('.'))
                {
                    if (_index >= _input.Length || !IsDigit(_input[_index]))
                        throw Error("Fraction must contain at least one digit");
                    while (_index < _input.Length && IsDigit(_input[_index])) _index++;
                }

                if (_index < _input.Length && (_input[_index] == 'e' || _input[_index] == 'E'))
                {
                    _index++;
                    if (_index < _input.Length && (_input[_index] == '+' || _input[_index] == '-')) _index++;
                    if (_index >= _input.Length || !IsDigit(_input[_index]))
                        throw Error("Exponent must contain at least one digit");
                    while (_index < _input.Length && IsDigit(_input[_index])) _index++;
                }

                return _input.Substring(start, _index - start);
            }

            private void ReadLiteral(string literal)
            {
                if (_index + literal.Length > _input.Length ||
                    !string.Equals(
                        _input.Substring(_index, literal.Length),
                        literal,
                        StringComparison.Ordinal))
                {
                    throw Error("Invalid JSON literal");
                }
                _index += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (_index < _input.Length)
                {
                    char value = _input[_index];
                    if (value != ' ' && value != '\t' && value != '\r' && value != '\n') break;
                    _index++;
                }
            }

            private void Consume(char expected)
            {
                if (!TryConsume(expected)) throw Error("Expected '" + expected + "'");
            }

            private bool TryConsume(char expected)
            {
                if (_index >= _input.Length || _input[_index] != expected) return false;
                _index++;
                return true;
            }

            private StoryJsonException Error(string message) => new StoryJsonException(message, _index);

            private static bool IsDigit(char value) => value >= '0' && value <= '9';
            private static bool IsDigitOneToNine(char value) => value >= '1' && value <= '9';

            private static int HexValue(char value)
            {
                if (value >= '0' && value <= '9') return value - '0';
                if (value >= 'A' && value <= 'F') return value - 'A' + 10;
                if (value >= 'a' && value <= 'f') return value - 'a' + 10;
                return -1;
            }
        }
    }
}
