// 无头测试用的 UnityEngine 最小替身：只提供规则文件（Data、Model、Strategy 规则、BattleModel、Specials）
// 用到的类型与函数，语义与 Unity 一致。不进入 Unity 工程（Tests~ 目录被 Unity 忽略）。
//   Random    与网页版 SG.Random.seed(s) 完全相同的 mulberry32 序列（InitState / Reset 设种子），
//             因此同一种子下 C# 与 JS 的随机数序列一致（统计结果可对照）。
//   Mathf.PerlinNoise  移植自网页版 core.js（与 Unity 同尺度：(n + 0.69) / 1.483）。
//   PlayerPrefs        内存字典；JsonUtility 为空实现（规则测试不读写存档）。
// 需要更多 API 时直接在这里补，保持与 Unity 的签名一致。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(float k, Vector2 a) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator /(Vector2 a, float k) => new Vector2(a.x / k, a.y / k);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 v && v.x == x && v.y == y;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized { get { var m = magnitude; return m > 1E-05f ? this / m : zero; } }
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public override string ToString() => $"({x:F1}, {y:F1})";
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new Vector2Int(0, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new Vector2Int(a.x - b.x, a.y - b.y);
        public static Vector2Int operator *(Vector2Int a, int k) => new Vector2Int(a.x * k, a.y * k);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
        public override bool Equals(object o) => o is Vector2Int v && v == this;
        public override int GetHashCode() => x * 397 ^ y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static float Distance(Vector2Int a, Vector2Int b) => (a - b).magnitude;
        public static implicit operator Vector2(Vector2Int v) => new Vector2(v.x, v.y);
        public override string ToString() => $"({x}, {y})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && v.x == x && v.y == y && v.z == z;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { var m = magnitude; return m > 1E-05f ? this / m : zero; } }
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color red => new Color(1, 0, 0);
        public static Color green => new Color(0, 1, 0);
        public static Color blue => new Color(0, 0, 1);
        public static Color yellow => new Color(1, 0.921568632f, 0.0156862754f);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f);
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a * k);
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color Lerp(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t); }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) => new Color32(B(c.r), B(c.g), B(c.b), B(c.a));
        static byte B(float v) => (byte)Math.Round(Mathf.Clamp01(v) * 255f);
    }

    public static class ColorUtility
    {
        // 接受 #rgb、#rrggbb、#rrggbbaa（与 Unity 相同；颜色名只认常用的几个）
        public static bool TryParseHtmlString(string s, out Color c)
        {
            c = new Color(0, 0, 0, 0);
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.ToLowerInvariant())
            {
                case "white": c = Color.white; return true;
                case "black": c = Color.black; return true;
                case "red": c = Color.red; return true;
                case "green": c = Color.green; return true;
                case "blue": c = Color.blue; return true;
            }
            if (s[0] != '#') return false;
            string h = s.Substring(1);
            if (h.Length == 3 || h.Length == 4) { var t = ""; foreach (var ch in h) t += new string(ch, 2); h = t; }
            if (h.Length != 6 && h.Length != 8) return false;
            uint v;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            if (h.Length == 6) v = (v << 8) | 0xff;
            c = new Color32((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
        public static string ToHtmlStringRGB(Color c) { Color32 k = c; return $"{k.r:X2}{k.g:X2}{k.b:X2}"; }
        public static string ToHtmlStringRGBA(Color c) { Color32 k = c; return $"{k.r:X2}{k.g:X2}{k.b:X2}{k.a:X2}"; }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Epsilon = 1.401298E-45f;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(params int[] v) { int m = v[0]; foreach (var x in v) if (x < m) m = x; return m; }
        public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) if (x < m) m = x; return m; }
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(params int[] v) { int m = v[0]; foreach (var x in v) if (x > m) m = x; return m; }
        public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) if (x > m) m = x; return m; }
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        // Unity 的 RoundToInt / Round 用银行家舍入（Math.Round 的缺省行为）
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log(float f, float p) => (float)Math.Log(f, p);
        public static float Log10(float f) => (float)Math.Log10(f);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Atan(float f) => (float)Math.Atan(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Acos(float f) => (float)Math.Acos(f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return b * t + a * (1f - t); }
        public static float Repeat(float t, float len) => Clamp(t - Floor(t / len) * len, 0f, len);
        public static float PingPong(float t, float len) { t = Repeat(t, len * 2f); return len - Abs(t - len); }
        public static float DeltaAngle(float a, float b) { float d = Repeat(b - a, 360f); if (d > 180f) d -= 360f; return d; }
        public static float LerpAngle(float a, float b, float t) { float d = Repeat(b - a, 360f); if (d > 180f) d -= 360f; return a + d * Clamp01(t); }
        public static float MoveTowards(float c, float t, float d) => Abs(t - c) <= d ? t : c + Sign(t - c) * d;
        public static bool Approximately(float a, float b) => Abs(b - a) < Max(1E-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);

        // ---- Perlin 噪声：移植自网页版 core.js（同一置换表、同一归一化）----
        static readonly byte[] perm = BuildPerm();
        static byte[] BuildPerm()
        {
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            long s = 1337;
            for (int i = 255; i > 0; i--)
            {
                s = (s * 16807) % 2147483647;
                int j = (int)(s % (i + 1));
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            var r = new byte[512];
            for (int i = 0; i < 512; i++) r[i] = (byte)p[i & 255];
            return r;
        }
        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        static double Grad(int h, double x, double y)
        {
            switch (h & 7)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }
        public static float PerlinNoise(float fx, float fy)
        {
            double x = fx, y = fy;
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            int X = xi & 255, Y = yi & 255;
            double xf = x - xi, yf = y - yi;
            double u = Fade(xf), v = Fade(yf);
            int aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1], ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
            double x1 = Grad(aa, xf, yf) + (Grad(ba, xf - 1, yf) - Grad(aa, xf, yf)) * u;
            double x2 = Grad(ab, xf, yf - 1) + (Grad(bb, xf - 1, yf - 1) - Grad(ab, xf, yf - 1)) * u;
            double n = x1 + (x2 - x1) * v;
            return (float)((n + 0.69) / 1.483);
        }
    }

    // UnityEngine.Random 的语义：Range(int a, int b) 不含 b（b <= a 时返回 a）；Range(float a, float b) 含两端。
    // 序列 = 网页版 SG.Random.seed(s) 的 mulberry32；未设种子时种子为 1。
    public static class Random
    {
        static uint a = 1;
        public static void InitState(int seed) { a = (uint)seed; }
        public static void Reset(int seed) { InitState(seed); }
        // 与 JS 相同：返回 [0, 1) 的双精度数
        public static double NextDouble()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        public static float value => ValueOverride != null ? (float)ValueOverride() : (float)NextDouble();
        // 仅测试用（Unity 无此成员）：替换 Random.value（如 () => 0 令概率判定必定成功），null = 正常
        public static Func<double> ValueOverride;
        public static int Range(int min, int maxExclusive) => maxExclusive <= min ? min : min + (int)Math.Floor(NextDouble() * (maxExclusive - min));
        public static float Range(float min, float max) => (float)(min + NextDouble() * (max - min));
        public static Vector2 insideUnitCircle { get { double r = Math.Sqrt(NextDouble()), t = NextDouble() * 2 * Math.PI; return new Vector2((float)(r * Math.Cos(t)), (float)(r * Math.Sin(t))); } }
    }

    public static class Application
    {
        public static string persistentDataPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sanguo-sim");
        public static bool isPlaying => false;
    }

    public static class Debug
    {
        public static void Log(object o) => Console.WriteLine(o);
        public static void LogWarning(object o) => Console.WriteLine("WARN " + o);
        public static void LogError(object o) => Console.WriteLine("ERROR " + o);
        public static void Assert(bool c, object o = null) { if (!c) throw new Exception("Assert failed: " + o); }
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) => "{}";
        public static string ToJson(object o, bool pretty) => "{}";
        public static T FromJson<T>(string s) => default(T);
    }

    public static class PlayerPrefs
    {
        static readonly Dictionary<string, object> d = new Dictionary<string, object>();
        public static bool HasKey(string k) => d.ContainsKey(k);
        public static void DeleteKey(string k) => d.Remove(k);
        public static void DeleteAll() => d.Clear();
        public static void Save() { }
        public static void SetString(string k, string v) => d[k] = v;
        public static string GetString(string k, string def = "") => d.TryGetValue(k, out var v) && v is string s ? s : def;
        public static void SetInt(string k, int v) => d[k] = v;
        public static int GetInt(string k, int def = 0) => d.TryGetValue(k, out var v) && v is int i ? i : def;
        public static void SetFloat(string k, float v) => d[k] = v;
        public static float GetFloat(string k, float def = 0) => d.TryGetValue(k, out var v) && v is float f ? f : def;
    }

    public static class Time
    {
        public static float time, deltaTime = 1f / 60f, unscaledDeltaTime = 1f / 60f, realtimeSinceStartup;
        public static int frameCount;
    }
}
