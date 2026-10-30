using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentrySix.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A JSON value as the server sends it. Read-only, and forgiving like the
    /// web pages' JavaScript: a missing key or index gives the shared
    /// <see cref="Null"/> value, never an exception, so
    /// <c>v["recordings"][0]["day"].Str</c> is null when any step is missing.
    /// </summary>
    public sealed class JsonValue
    {
        /// <summary>Nesting deeper than this is refused (stops a hostile reply from overflowing the stack).</summary>
        public const int MaxDepth = 1000;

        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);
        public static readonly JsonValue True = new JsonValue(JsonKind.Bool) { boolValue = true };
        public static readonly JsonValue False = new JsonValue(JsonKind.Bool) { boolValue = false };

        static readonly IReadOnlyList<JsonValue> NoItems = new List<JsonValue>().AsReadOnly();
        static readonly KeyValuePair<string, JsonValue>[] NoProps = new KeyValuePair<string, JsonValue>[0];

        readonly JsonKind kind;
        bool boolValue;
        double number;
        string text; // a string's value, or a number's JSON text
        List<JsonValue> items;
        List<KeyValuePair<string, JsonValue>> props;
        Dictionary<string, int> index;

        JsonValue(JsonKind kind)
        {
            this.kind = kind;
        }

        public JsonKind Kind { get { return kind; } }
        public bool IsNull { get { return kind == JsonKind.Null; } }
        public bool IsString { get { return kind == JsonKind.String; } }
        public bool IsNumber { get { return kind == JsonKind.Number; } }
        public bool IsBool { get { return kind == JsonKind.Bool; } }
        public bool IsArray { get { return kind == JsonKind.Array; } }
        public bool IsObject { get { return kind == JsonKind.Object; } }

        /// <summary>The string; a number or bool as its JSON text; null for null, arrays and objects.</summary>
        public string Str
        {
            get
            {
                switch (kind)
                {
                    case JsonKind.String:
                    case JsonKind.Number: return text;
                    case JsonKind.Bool: return boolValue ? "true" : "false";
                    default: return null;
                }
            }
        }

        /// <summary>The number; a numeric string is read as one; anything else is 0.</summary>
        public double Num
        {
            get
            {
                if (kind == JsonKind.Number) return number;
                double d;
                if (kind == JsonKind.String && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                if (kind == JsonKind.Bool) return boolValue ? 1 : 0;
                return 0;
            }
        }

        /// <summary>The number as a whole number (exact for integers beyond 2^53 too); fractions are truncated.</summary>
        public long Long
        {
            get
            {
                long l;
                if ((kind == JsonKind.Number || kind == JsonKind.String) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
                double d = Num;
                if (double.IsNaN(d)) return 0;
                if (d >= 9.2233720368547758E18) return long.MaxValue;
                if (d <= -9.2233720368547758E18) return long.MinValue;
                return (long)d;
            }
        }

        /// <summary>As JavaScript reads it in an if: false for null, false, 0, "" (arrays and objects are true).</summary>
        public bool Bool
        {
            get
            {
                switch (kind)
                {
                    case JsonKind.Bool: return boolValue;
                    case JsonKind.Number: return number != 0 && !double.IsNaN(number);
                    case JsonKind.String: return text.Length > 0;
                    case JsonKind.Array:
                    case JsonKind.Object: return true;
                    default: return false;
                }
            }
        }

        /// <summary>An array's items; empty for anything else.</summary>
        public IReadOnlyList<JsonValue> Items
        {
            get { return kind == JsonKind.Array ? (IReadOnlyList<JsonValue>)items.AsReadOnly() : NoItems; }
        }

        /// <summary>An object's properties in the server's order; empty for anything else.</summary>
        public IEnumerable<KeyValuePair<string, JsonValue>> Props
        {
            get { return kind == JsonKind.Object ? (IEnumerable<KeyValuePair<string, JsonValue>>)props.AsReadOnly() : NoProps; }
        }

        /// <summary>Items of an array, properties of an object, else 0.</summary>
        public int Count
        {
            get { return kind == JsonKind.Array ? items.Count : kind == JsonKind.Object ? props.Count : 0; }
        }

        public bool Has(string key)
        {
            return kind == JsonKind.Object && key != null && index.ContainsKey(key);
        }

        public JsonValue this[string key]
        {
            get
            {
                int i;
                if (kind != JsonKind.Object || key == null || !index.TryGetValue(key, out i)) return Null;
                return props[i].Value;
            }
        }

        public JsonValue this[int i]
        {
            get { return kind == JsonKind.Array && i >= 0 && i < items.Count ? items[i] : Null; }
        }

        public override string ToString()
        {
            return ToJson();
        }

        // ------------------------------------------------------------ building

        public static JsonValue String(string s)
        {
            if (s == null) return Null;
            JsonValue v = new JsonValue(JsonKind.String);
            v.text = s;
            return v;
        }

        public static JsonValue Number(double d)
        {
            JsonValue v = new JsonValue(JsonKind.Number);
            v.number = d;
            v.text = FormatNumber(d);
            return v;
        }

        public static JsonValue Number(long l)
        {
            JsonValue v = new JsonValue(JsonKind.Number);
            v.number = l;
            v.text = l.ToString(CultureInfo.InvariantCulture);
            return v;
        }

        public static JsonValue Array(IEnumerable<JsonValue> values)
        {
            JsonValue v = new JsonValue(JsonKind.Array);
            v.items = new List<JsonValue>();
            if (values != null) foreach (JsonValue x in values) v.items.Add(x ?? Null);
            return v;
        }

        /// <summary>An object from alternating keys and values: <c>Object("username", u, "password", p)</c>. Values go through <see cref="From"/>.</summary>
        public static JsonValue Object(params object[] keyValuePairs)
        {
            JsonValue v = NewObject();
            if (keyValuePairs == null) return v;
            if (keyValuePairs.Length % 2 != 0) throw new ArgumentException("Object takes key, value pairs.");
            for (int i = 0; i < keyValuePairs.Length; i += 2)
            {
                string key = keyValuePairs[i] as string;
                if (key == null) throw new ArgumentException("Object keys are strings.");
                v.Put(key, From(keyValuePairs[i + 1]));
            }
            return v;
        }

        /// <summary>
        /// A value from a .NET one: null, JsonValue, string, bool, any number,
        /// DateTime (ISO 8601 UTC), an IDictionary (an object), any other
        /// IEnumerable (an array). Anything else is refused.
        /// </summary>
        public static JsonValue From(object o)
        {
            if (o == null) return Null;
            JsonValue jv = o as JsonValue;
            if (jv != null) return jv;
            string s = o as string;
            if (s != null) return String(s);
            if (o is bool) return (bool)o ? True : False;
            if (o is int || o is long || o is short || o is sbyte || o is byte || o is ushort || o is uint) return Number(Convert.ToInt64(o, CultureInfo.InvariantCulture));
            if (o is ulong)
            {
                ulong u = (ulong)o;
                if (u <= long.MaxValue) return Number((long)u);
                JsonValue big = new JsonValue(JsonKind.Number);
                big.number = u;
                big.text = u.ToString(CultureInfo.InvariantCulture);
                return big;
            }
            if (o is double || o is float || o is decimal) return Number(Convert.ToDouble(o, CultureInfo.InvariantCulture));
            if (o is char) return String(o.ToString());
            if (o is DateTime) return String(((DateTime)o).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            if (o is Enum) return String(o.ToString());
            IDictionary dict = o as IDictionary;
            if (dict != null)
            {
                JsonValue obj = NewObject();
                foreach (DictionaryEntry e in dict) obj.Put(Convert.ToString(e.Key, CultureInfo.InvariantCulture), From(e.Value));
                return obj;
            }
            IEnumerable seq = o as IEnumerable;
            if (seq != null)
            {
                // a sequence of key/value pairs with string keys is an object
                List<JsonValue> list = new List<JsonValue>();
                JsonValue asObject = null;
                foreach (object x in seq)
                {
                    if (x is KeyValuePair<string, JsonValue>)
                    {
                        KeyValuePair<string, JsonValue> kv = (KeyValuePair<string, JsonValue>)x;
                        if (asObject == null) asObject = NewObject();
                        asObject.Put(kv.Key, kv.Value ?? Null);
                    }
                    else if (x is KeyValuePair<string, object>)
                    {
                        KeyValuePair<string, object> kv = (KeyValuePair<string, object>)x;
                        if (asObject == null) asObject = NewObject();
                        asObject.Put(kv.Key, From(kv.Value));
                    }
                    else list.Add(From(x));
                }
                if (asObject != null && list.Count == 0) return asObject;
                return Array(list);
            }
            throw new ArgumentException("JSON cannot hold a " + o.GetType().Name + ".");
        }

        static JsonValue NewObject()
        {
            JsonValue v = new JsonValue(JsonKind.Object);
            v.props = new List<KeyValuePair<string, JsonValue>>();
            v.index = new Dictionary<string, int>(StringComparer.Ordinal);
            return v;
        }

        void Put(string key, JsonValue value)
        {
            // a repeated key: the last one wins, in the first one's place (as JSON.parse)
            int i;
            if (index.TryGetValue(key, out i)) props[i] = new KeyValuePair<string, JsonValue>(key, value);
            else
            {
                index[key] = props.Count;
                props.Add(new KeyValuePair<string, JsonValue>(key, value));
            }
        }

        /// <summary>A copy of this object with one property set (null removes nothing: it sets JSON null).</summary>
        public JsonValue With(string key, object value)
        {
            JsonValue copy = NewObject();
            if (kind == JsonKind.Object) foreach (KeyValuePair<string, JsonValue> p in props) copy.Put(p.Key, p.Value);
            copy.Put(key, From(value));
            return copy;
        }

        // ------------------------------------------------------------ writing

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, 0);
            return sb.ToString();
        }

        void Write(StringBuilder sb, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("JSON nested too deeply.");
            switch (kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(boolValue ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(text); break;
                case JsonKind.String: Quote(sb, text); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        items[i].Write(sb, depth + 1);
                    }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < props.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Quote(sb, props[i].Key);
                        sb.Append(':');
                        props[i].Value.Write(sb, depth + 1);
                    }
                    sb.Append('}');
                    break;
            }
        }

        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 2);
            Quote(sb, s);
            return sb.ToString();
        }

        static void Quote(StringBuilder sb, string s)
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>As JavaScript's JSON.stringify writes a number (NaN and infinities as null).</summary>
        static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            // the shortest round-trip digits, laid out as ECMAScript Number::toString does
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            bool negative = r[0] == '-';
            if (negative) r = r.Substring(1);
            int exp = 0;
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            if (e >= 0)
            {
                exp = int.Parse(r.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                r = r.Substring(0, e);
            }
            int dot = r.IndexOf('.');
            string digits = dot < 0 ? r : r.Remove(dot, 1);
            int n = (dot < 0 ? r.Length : dot) + exp; // value = 0.digits x 10^n
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
            digits = digits.Substring(lead);
            n -= lead;
            digits = digits.TrimEnd('0');
            if (digits.Length == 0) return "0";
            int k = digits.Length;
            string s;
            if (k <= n && n <= 21) s = digits + new string('0', n - k);
            else if (0 < n && n <= 21) s = digits.Substring(0, n) + "." + digits.Substring(n);
            else if (-6 < n && n <= 0) s = "0." + new string('0', -n) + digits;
            else
            {
                int x = n - 1;
                string ex = "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString(CultureInfo.InvariantCulture);
                s = k == 1 ? digits + ex : digits.Substring(0, 1) + "." + digits.Substring(1) + ex;
            }
            return negative ? "-" + s : s;
        }

        // ------------------------------------------------------------ parsing

        /// <summary>Parses strict JSON (RFC 8259); throws FormatException with the position of the problem.</summary>
        public static JsonValue Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            Parser p = new Parser(json);
            p.SkipSpace();
            JsonValue v = p.Value(0);
            p.SkipSpace();
            if (p.Pos < json.Length) throw p.Error("unexpected text after the value");
            return v;
        }

        /// <summary>Parse, or null when it is not JSON (an empty or HTML reply, say).</summary>
        public static JsonValue TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return Parse(json);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        sealed class Parser
        {
            readonly string s;
            public int Pos;

            public Parser(string s)
            {
                this.s = s;
            }

            public FormatException Error(string what)
            {
                return new FormatException("Not valid JSON: " + what + " at position " + Pos.ToString(CultureInfo.InvariantCulture) + ".");
            }

            public void SkipSpace()
            {
                while (Pos < s.Length)
                {
                    char c = s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') Pos++;
                    else break;
                }
            }

            public JsonValue Value(int depth)
            {
                if (depth > MaxDepth) throw Error("nested too deeply");
                if (Pos >= s.Length) throw Error("unexpected end");
                char c = s[Pos];
                switch (c)
                {
                    case '{': return ObjectValue(depth);
                    case '[': return ArrayValue(depth);
                    case '"': return String(StringValue());
                    case 't': Word("true"); return True;
                    case 'f': Word("false"); return False;
                    case 'n': Word("null"); return Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return NumberValue();
                        throw Error("unexpected '" + c + "'");
                }
            }

            void Word(string w)
            {
                if (string.CompareOrdinal(s, Pos, w, 0, w.Length) != 0) throw Error("unexpected word");
                Pos += w.Length;
            }

            JsonValue ObjectValue(int depth)
            {
                Pos++; // {
                JsonValue obj = NewObject();
                SkipSpace();
                if (Pos < s.Length && s[Pos] == '}')
                {
                    Pos++;
                    return obj;
                }
                while (true)
                {
                    SkipSpace();
                    if (Pos >= s.Length || s[Pos] != '"') throw Error("expected a property name");
                    string key = StringValue();
                    SkipSpace();
                    if (Pos >= s.Length || s[Pos] != ':') throw Error("expected ':'");
                    Pos++;
                    SkipSpace();
                    obj.Put(key, Value(depth + 1));
                    SkipSpace();
                    if (Pos >= s.Length) throw Error("unexpected end in an object");
                    if (s[Pos] == ',')
                    {
                        Pos++;
                        continue;
                    }
                    if (s[Pos] == '}')
                    {
                        Pos++;
                        return obj;
                    }
                    throw Error("expected ',' or '}'");
                }
            }

            JsonValue ArrayValue(int depth)
            {
                Pos++; // [
                List<JsonValue> list = new List<JsonValue>();
                SkipSpace();
                if (Pos < s.Length && s[Pos] == ']')
                {
                    Pos++;
                    return Array(list);
                }
                while (true)
                {
                    SkipSpace();
                    list.Add(Value(depth + 1));
                    SkipSpace();
                    if (Pos >= s.Length) throw Error("unexpected end in an array");
                    if (s[Pos] == ',')
                    {
                        Pos++;
                        continue;
                    }
                    if (s[Pos] == ']')
                    {
                        Pos++;
                        return Array(list);
                    }
                    throw Error("expected ',' or ']'");
                }
            }

            string StringValue()
            {
                Pos++; // "
                StringBuilder sb = null;
                int start = Pos;
                while (true)
                {
                    if (Pos >= s.Length) throw Error("unterminated string");
                    char c = s[Pos];
                    if (c == '"')
                    {
                        string done = sb == null ? s.Substring(start, Pos - start) : sb.Append(s, start, Pos - start).ToString();
                        Pos++;
                        return done;
                    }
                    if (c < 0x20) throw Error("control character in a string");
                    if (c != '\\')
                    {
                        Pos++;
                        continue;
                    }
                    if (sb == null) sb = new StringBuilder();
                    sb.Append(s, start, Pos - start);
                    Pos++;
                    if (Pos >= s.Length) throw Error("unterminated escape");
                    char e = s[Pos++];
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
                            if (Pos + 4 > s.Length) throw Error("short \\u escape");
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                int h = Hex(s[Pos + i]);
                                if (h < 0) throw Error("bad \\u escape");
                                code = code * 16 + h;
                            }
                            Pos += 4;
                            sb.Append((char)code); // surrogate pairs come as two escapes, as UTF-16 wants
                            break;
                        default:
                            Pos--;
                            throw Error("bad escape '\\" + e + "'");
                    }
                    start = Pos;
                }
            }

            static int Hex(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            JsonValue NumberValue()
            {
                int start = Pos;
                if (s[Pos] == '-') Pos++;
                if (Pos >= s.Length) throw Error("bad number");
                if (s[Pos] == '0') Pos++;
                else if (s[Pos] >= '1' && s[Pos] <= '9') Digits();
                else throw Error("bad number");
                if (Pos < s.Length && s[Pos] == '.')
                {
                    Pos++;
                    if (Pos >= s.Length || s[Pos] < '0' || s[Pos] > '9') throw Error("bad fraction");
                    Digits();
                }
                if (Pos < s.Length && (s[Pos] == 'e' || s[Pos] == 'E'))
                {
                    Pos++;
                    if (Pos < s.Length && (s[Pos] == '+' || s[Pos] == '-')) Pos++;
                    if (Pos >= s.Length || s[Pos] < '0' || s[Pos] > '9') throw Error("bad exponent");
                    Digits();
                }
                JsonValue v = new JsonValue(JsonKind.Number);
                v.text = s.Substring(start, Pos - start);
                double d;
                if (!double.TryParse(v.text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    // older frameworks refuse what overflows; JavaScript reads it as Infinity
                    d = v.text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
                }
                v.number = d;
                return v;
            }

            void Digits()
            {
                while (Pos < s.Length && s[Pos] >= '0' && s[Pos] <= '9') Pos++;
            }
        }
    }
}
