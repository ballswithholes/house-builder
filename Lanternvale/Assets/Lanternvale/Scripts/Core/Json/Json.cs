// Lanternvale core JSON: a small, dependency-free parser, typed mapper and writer.
//
// Why not JsonUtility? It cannot read enums from strings, nested arrays, dictionaries or
// polymorphic data, and it silently ignores typos. This mapper reads string enums
// (case-insensitive), Vec2 from [x, y], lists/arrays/dictionaries, and reports every unknown
// key so the data validator can catch authoring mistakes. Pure C#: no UnityEngine.
//
// Parser accepts standard JSON plus // and /* */ comments and trailing commas.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Lanternvale.Json
{
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    /// <summary>Parses JSON text into Dictionary&lt;string,object&gt;, List&lt;object&gt;, string, double, bool or null.</summary>
    public static class JsonParser
    {
        public static object Parse(string text, string sourceName = "json")
        {
            var p = new Reader(text, sourceName);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("Unexpected trailing characters");
            return v;
        }

        sealed class Reader
        {
            readonly string s;
            readonly string name;
            int i;

            public Reader(string text, string sourceName) { s = text ?? ""; name = sourceName; }
            public bool End => i >= s.Length;

            public JsonException Error(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                {
                    if (s[k] == '\n') { line++; col = 1; } else col++;
                }
                return new JsonException($"{name}({line},{col}): {msg}");
            }

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { i++; continue; }
                    if (c == '/' && i + 1 < s.Length)
                    {
                        if (s[i + 1] == '/') { i += 2; while (i < s.Length && s[i] != '\n') i++; continue; }
                        if (s[i + 1] == '*')
                        {
                            i += 2;
                            while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                            i += 2;
                            continue;
                        }
                    }
                    break;
                }
            }

            public object ReadValue()
            {
                if (End) throw Error("Unexpected end of input");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; // {
                SkipWs();
                if (!End && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (s[i] == '}') { i++; return d; } // trailing comma
                    if (s[i] != '"') throw Error("Expected property name");
                    string key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw Error("Expected ':'");
                    i++;
                    SkipWs();
                    var v = ReadValue();
                    if (d.ContainsKey(key)) throw Error($"Duplicate key '{key}'");
                    d[key] = v;
                    SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("Expected ',' or '}'");
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                i++; // [
                SkipWs();
                if (!End && s[i] == ']') { i++; return l; }
                while (true)
                {
                    SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (s[i] == ']') { i++; return l; } // trailing comma
                    l.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ReadString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Bad escape");
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw Error("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error($"Bad escape '\\{e}'");
                    }
                }
            }

            double ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
                var tok = s.Substring(start, i - start);
                if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error($"Bad number '{tok}'");
                return d;
            }
        }
    }

    /// <summary>Collects mapping problems (unknown keys, bad enum names, type mismatches).</summary>
    public sealed class JsonMapContext
    {
        public readonly List<string> Problems = new List<string>();
        public string Source = "";
        public bool ReportUnknownKeys = true;
        public void Report(string path, string msg) => Problems.Add($"{Source}: {path}: {msg}");
    }

    /// <summary>Maps parsed JSON onto typed classes with public fields.</summary>
    public static class JsonMapper
    {
        static readonly Dictionary<Type, Dictionary<string, FieldInfo>> FieldCache = new Dictionary<Type, Dictionary<string, FieldInfo>>();

        static Dictionary<string, FieldInfo> Fields(Type t)
        {
            lock (FieldCache)
            {
                if (FieldCache.TryGetValue(t, out var f)) return f;
                f = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (fi.IsInitOnly || fi.IsLiteral || fi.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    f[fi.Name] = fi;
                }
                FieldCache[t] = f;
                return f;
            }
        }

        public static T To<T>(object node, JsonMapContext ctx = null, string path = "$")
        {
            return (T)Convert(node, typeof(T), ctx ?? new JsonMapContext(), path);
        }

        public static T FromJson<T>(string json, JsonMapContext ctx = null, string sourceName = "json")
        {
            return To<T>(JsonParser.Parse(json, sourceName), ctx, "$");
        }

        public static object Convert(object node, Type t, JsonMapContext ctx, string path)
        {
            var nullable = Nullable.GetUnderlyingType(t);
            if (node == null)
            {
                if (!t.IsValueType || nullable != null) return null;
                return Activator.CreateInstance(t);
            }
            if (nullable != null) t = nullable;

            if (t == typeof(object)) return node;
            if (t == typeof(string))
            {
                if (node is string str) return str;
                if (node is double dd) return dd.ToString(CultureInfo.InvariantCulture);
                if (node is bool bb) return bb ? "true" : "false";
                ctx.Report(path, "expected string");
                return null;
            }
            if (t == typeof(bool))
            {
                if (node is bool b) return b;
                if (node is double d0) return d0 != 0;
                ctx.Report(path, "expected bool");
                return false;
            }
            if (t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long))
            {
                double d;
                if (node is double nd) d = nd;
                else if (node is string ns && double.TryParse(ns, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) d = parsed;
                else if (node is bool nb) d = nb ? 1 : 0;
                else { ctx.Report(path, "expected number"); d = 0; }
                if (t == typeof(int)) return (int)Math.Round(d);
                if (t == typeof(long)) return (long)Math.Round(d);
                if (t == typeof(float)) return (float)d;
                return d;
            }
            if (t.IsEnum)
            {
                if (node is string es)
                {
                    if (es.Length == 0) return Activator.CreateInstance(t);
                    try { return Enum.Parse(t, es.Replace(" ", ""), true); }
                    catch
                    {
                        ctx.Report(path, $"unknown {t.Name} '{es}' (valid: {string.Join(", ", Enum.GetNames(t))})");
                        return Activator.CreateInstance(t);
                    }
                }
                if (node is double ed) return Enum.ToObject(t, (int)ed);
                ctx.Report(path, $"expected {t.Name} name");
                return Activator.CreateInstance(t);
            }
            if (t == typeof(Lanternvale.Util.Vec2))
            {
                if (node is List<object> vl && vl.Count >= 2)
                    return new Lanternvale.Util.Vec2(ToF(vl[0]), ToF(vl[1]));
                if (node is Dictionary<string, object> vd)
                {
                    vd.TryGetValue("x", out var x);
                    vd.TryGetValue("y", out var y);
                    return new Lanternvale.Util.Vec2(ToF(x), ToF(y));
                }
                ctx.Report(path, "expected [x, y]");
                return default(Lanternvale.Util.Vec2);
            }
            if (t.IsArray)
            {
                var et = t.GetElementType();
                var list = node as List<object> ?? new List<object> { node }; // a single value is a one-element list
                var arr = Array.CreateInstance(et, list.Count);
                for (int k = 0; k < list.Count; k++) arr.SetValue(Convert(list[k], et, ctx, $"{path}[{k}]"), k);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0];
                var list = node as List<object> ?? new List<object> { node };
                var result = (IList)Activator.CreateInstance(t);
                for (int k = 0; k < list.Count; k++) result.Add(Convert(list[k], et, ctx, $"{path}[{k}]"));
                return result;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = t.GetGenericArguments();
                if (args[0] != typeof(string)) throw new JsonException("Only string-keyed dictionaries are supported");
                var result = (IDictionary)Activator.CreateInstance(t);
                if (node is Dictionary<string, object> src)
                    foreach (var kv in src) result[kv.Key] = Convert(kv.Value, args[1], ctx, $"{path}.{kv.Key}");
                else ctx.Report(path, "expected object");
                return result;
            }
            if (t.IsClass || (t.IsValueType && !t.IsPrimitive))
            {
                if (!(node is Dictionary<string, object> obj))
                {
                    ctx.Report(path, $"expected object for {t.Name}");
                    return t.IsValueType ? Activator.CreateInstance(t) : null;
                }
                var inst = Activator.CreateInstance(t);
                var fields = Fields(t);
                foreach (var kv in obj)
                {
                    if (kv.Key.StartsWith("_") || kv.Key.StartsWith("$")) continue; // comment keys like "_note"
                    if (!fields.TryGetValue(kv.Key, out var fi))
                    {
                        if (ctx.ReportUnknownKeys) ctx.Report($"{path}.{kv.Key}", $"unknown key for {t.Name}");
                        continue;
                    }
                    fi.SetValue(inst, Convert(kv.Value, fi.FieldType, ctx, $"{path}.{kv.Key}"));
                }
                return inst;
            }
            ctx.Report(path, $"unsupported type {t.Name}");
            return null;
        }

        static float ToF(object o)
        {
            if (o is double d) return (float)d;
            if (o is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
            return 0f;
        }
    }

    /// <summary>Serializes objects with public fields (used for save games). Enums are written as names.</summary>
    public static class JsonWriter
    {
        public static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void Write(StringBuilder sb, object v, bool pretty, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(FormatNum(d)); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case Enum e: WriteString(sb, e.ToString()); return;
                case Lanternvale.Util.Vec2 p:
                    sb.Append('[').Append(FormatNum(p.x)).Append(',').Append(FormatNum(p.y)).Append(']');
                    return;
            }
            if (v is IDictionary dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Indent(sb, pretty, depth + 1);
                    WriteString(sb, kv.Key.ToString());
                    sb.Append(pretty ? ": " : ":");
                    Write(sb, kv.Value, pretty, depth + 1);
                }
                if (!first) Indent(sb, pretty, depth);
                sb.Append('}');
                return;
            }
            if (v is IEnumerable en)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Indent(sb, pretty, depth + 1);
                    Write(sb, item, pretty, depth + 1);
                }
                if (!first) Indent(sb, pretty, depth);
                sb.Append(']');
                return;
            }
            sb.Append('{');
            bool firstField = true;
            foreach (var fi in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (fi.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                var fv = fi.GetValue(v);
                if (fv == null) continue;
                if (!firstField) sb.Append(',');
                firstField = false;
                Indent(sb, pretty, depth + 1);
                WriteString(sb, fi.Name);
                sb.Append(pretty ? ": " : ":");
                Write(sb, fv, pretty, depth + 1);
            }
            if (!firstField) Indent(sb, pretty, depth);
            sb.Append('}');
        }

        static string FormatNum(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
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
    }
}
