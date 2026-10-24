using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketOrbits.Core.Utilities
{
    /// <summary>
    /// Small dependency-free JSON reader/writer. Objects become Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers double. Used for level files and save data so the same
    /// code runs inside Unity, in dotnet tests and in tools.
    /// </summary>
    public static class MiniJson
    {
        public sealed class JsonException : Exception
        {
            public JsonException(string message) : base(message) { }
        }

        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("JSON text is null");
            var p = new Parser(text);
            p.SkipWhitespace();
            object value = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new JsonException("Unexpected trailing characters at " + p.Position);
            return value;
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }
            public bool AtEnd => _i >= _s.Length;
            public int Position => _i;

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { _i++; continue; }
                    // Allow // line comments in authored level files.
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }
                    break;
                }
            }

            public object ParseValue()
            {
                if (AtEnd) throw new JsonException("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new JsonException("Unexpected character '" + c + "' at " + _i);
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new JsonException("Expected '" + word + "' at " + _i);
                _i += word.Length;
            }

            Dictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                _i++;
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw new JsonException("Expected property name at " + _i);
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw new JsonException("Expected ':' at " + _i);
                    _i++;
                    SkipWhitespace();
                    d[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw new JsonException("Expected ',' or '}' at " + _i);
                }
            }

            List<object> ParseArray()
            {
                var list = new List<object>();
                _i++;
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw new JsonException("Expected ',' or ']' at " + _i);
                }
            }

            string ParseString()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (AtEnd) throw new JsonException("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw new JsonException("Unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new JsonException("Bad unicode escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new JsonException("Bad escape '\\" + e + "'");
                    }
                }
            }

            double ParseNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                string num = _s.Substring(start, _i - start);
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new JsonException("Bad number '" + num + "' at " + start);
                return d;
            }
        }

        public static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, bool pretty, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(FormatNumber(d)); break;
                case float f: sb.Append(FormatNumber(f)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, pretty, indent + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, indent + 1);
                    }
                    if (!first) NewLine(sb, pretty, indent);
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, pretty, indent + 1);
                        Write(sb, item, pretty, indent + 1);
                    }
                    if (!first) NewLine(sb, pretty, indent);
                    sb.Append(']');
                    break;
                }
                default: throw new JsonException("Cannot serialize " + v.GetType());
            }
        }

        static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- Typed access helpers -------------------------------------------------------------

        public static Dictionary<string, object> Obj(object o, string context)
        {
            if (o is Dictionary<string, object> d) return d;
            throw new JsonException(context + ": expected object");
        }

        public static List<object> Arr(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return new List<object>();
            if (v is List<object> l) return l;
            throw new JsonException("'" + key + "' must be an array");
        }

        public static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return fallback;
            if (v is double n) return n;
            throw new JsonException("'" + key + "' must be a number");
        }

        public static int Int(Dictionary<string, object> d, string key, int fallback)
        {
            return (int)Math.Round(Num(d, key, fallback));
        }

        public static string Str(Dictionary<string, object> d, string key, string fallback)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return fallback;
            if (v is string s) return s;
            throw new JsonException("'" + key + "' must be a string");
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool fallback)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return fallback;
            if (v is bool b) return b;
            throw new JsonException("'" + key + "' must be true or false");
        }
    }
}
