using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityAR.Core
{
    /// <summary>JSON 值的类型。</summary>
    public enum JsonKind
    {
        Null,
        Object,
        Array,
        String,
        Number,
        Boolean
    }

    /// <summary>
    /// 一个极简的 JSON 解析器（不依赖任何第三方库）。
    ///
    /// 为什么不用 UnityEngine.JsonUtility：
    ///   - 它不支持顶层数组（后端的 GET /api/issues 返回的就是顶层数组）；
    ///   - 字段缺失会直接抛异常，解析半截的响应时很难定位问题；
    ///   - 无法表达「字段不存在」与「字段是 null」的区别。
    ///
    /// 这个解析器放在 Core 里（不依赖 UnityEngine），因此可以被单元测试完整覆盖。
    /// </summary>
    public class JsonValue
    {
        private static readonly JsonValue NullValue = new JsonValue { Kind = JsonKind.Null };

        public JsonKind Kind { get; private set; }

        private string _stringValue;
        private double _numberValue;
        private bool _boolValue;
        private Dictionary<string, JsonValue> _object;
        private List<JsonValue> _array;

        public static JsonValue Null
        {
            get { return NullValue; }
        }

        public bool IsNull
        {
            get { return Kind == JsonKind.Null; }
        }

        /// <summary>对象成员；不是对象时返回 null。</summary>
        public Dictionary<string, JsonValue> Members
        {
            get { return _object; }
        }

        /// <summary>数组元素；不是数组时返回空列表。</summary>
        public List<JsonValue> Items
        {
            get { return _array ?? new List<JsonValue>(); }
        }

        public int Count
        {
            get
            {
                if (Kind == JsonKind.Array) return _array.Count;
                if (Kind == JsonKind.Object) return _object.Count;
                return 0;
            }
        }

        /// <summary>按 key 取成员；不存在返回 null。</summary>
        public JsonValue this[string key]
        {
            get
            {
                JsonValue v;
                if (_object != null && _object.TryGetValue(key, out v))
                {
                    return v;
                }
                return null;
            }
        }

        public string AsString
        {
            get
            {
                switch (Kind)
                {
                    case JsonKind.String: return _stringValue;
                    case JsonKind.Number: return _numberValue.ToString(CultureInfo.InvariantCulture);
                    case JsonKind.Boolean: return _boolValue ? "true" : "false";
                    default: return null;
                }
            }
        }

        public double AsDouble
        {
            get
            {
                if (Kind == JsonKind.Number) return _numberValue;
                double parsed;
                if (Kind == JsonKind.String && double.TryParse(_stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
                return 0d;
            }
        }

        public int AsInt
        {
            get { return (int)Math.Round(AsDouble); }
        }

        public bool AsBool
        {
            get { return Kind == JsonKind.Boolean && _boolValue; }
        }

        // ------------------------------------------------------------ 解析

        /// <summary>解析一段 JSON 文本。失败时返回 null（不抛异常）。</summary>
        public static JsonValue Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            int index = 0;
            try
            {
                var value = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                return value;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
            {
                throw new System.FormatException("unexpected end of json");
            }

            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { Kind = JsonKind.String, _stringValue = ParseString(s, ref i) };
                case 't':
                    Expect(s, ref i, "true");
                    return new JsonValue { Kind = JsonKind.Boolean, _boolValue = true };
                case 'f':
                    Expect(s, ref i, "false");
                    return new JsonValue { Kind = JsonKind.Boolean, _boolValue = false };
                case 'n':
                    Expect(s, ref i, "null");
                    return new JsonValue { Kind = JsonKind.Null };
                default:
                    return new JsonValue { Kind = JsonKind.Number, _numberValue = ParseNumber(s, ref i) };
            }
        }

        private static JsonValue ParseObject(string s, ref int i)
        {
            var result = new JsonValue
            {
                Kind = JsonKind.Object,
                _object = new Dictionary<string, JsonValue>()
            };

            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return result;
            }

            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                var key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new System.FormatException("expected ':'");
                }
                i++;
                result._object[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return result;
                }
                throw new System.FormatException("expected ',' or '}'");
            }

            throw new System.FormatException("unterminated object");
        }

        private static JsonValue ParseArray(string s, ref int i)
        {
            var result = new JsonValue
            {
                Kind = JsonKind.Array,
                _array = new List<JsonValue>()
            };

            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return result;
            }

            while (i < s.Length)
            {
                result._array.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return result;
                }
                throw new System.FormatException("expected ',' or ']'");
            }

            throw new System.FormatException("unterminated array");
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
            {
                throw new System.FormatException("expected string");
            }
            i++;

            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length)
                {
                    break;
                }

                var escaped = s[i++];
                switch (escaped)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            var hex = s.Substring(i, 4);
                            int code;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(escaped); break;
                }
            }

            throw new System.FormatException("unterminated string");
        }

        private static double ParseNumber(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
            {
                i++;
            }

            double value;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new System.FormatException("invalid number");
            }
            return value;
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
            {
                throw new System.FormatException("expected " + literal);
            }
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }
    }
}
