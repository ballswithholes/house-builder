// Managed stand-in for the parts of UnityEngine the 3D world code uses, so MeshBuilder, the prop library, MapTerrain,
// MapBackdrop, MapSky, DayNight and SceneLighting run unchanged under plain .NET (Tools/preview3d).
//
// Maths follow Unity's conventions exactly (left-handed, clockwise = front face, Euler order Z-X-Y, Cross/LookRotation
// as in Unity). Engine objects only keep the state the renderer needs: the transform hierarchy, meshes, materials with
// their properties, property blocks, textures with pixels. Mathf.PerlinNoise is a classic gradient noise in 0..1
// (Unity's exact noise field is not public: terrain detail differs, its statistics do not).
#pragma warning disable CS0660, CS0661, CS1591, CS0067
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 down => new Vector2(0, -1);
        public static Vector2 left => new Vector2(-1, 0);
        public static Vector2 right => new Vector2(1, 0);
        public float this[int i] { get => i == 0 ? x : y; set { if (i == 0) x = value; else y = value; } }
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x / m, y / m) : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(float k, Vector2 a) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static Vector2 operator /(Vector2 a, float k) => new Vector2(a.x / k, a.y / k);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 ClampMagnitude(Vector2 v, float m) => v.sqrMagnitude > m * m ? v.normalized * m : v;
        public static Vector2 MoveTowards(Vector2 c, Vector2 t, float d) { var v = t - c; float m = v.magnitude; return m <= d || m < 1e-6f ? t : c + v / m * d; }
        public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public static Vector2 Scale(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public override string ToString() => $"({x:F2}, {y:F2})";
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : z; set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector3(x / m, y / m, z / m) : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public static Vector3 Normalize(Vector3 v) => v.normalized;
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { float d = Dot(n, n); return d < 1e-10f ? v : v - n * (Dot(v, n) / d); }
        public static Vector3 Project(Vector3 v, Vector3 n) { float d = Dot(n, n); return d < 1e-10f ? zero : n * (Dot(v, n) / d); }
        public static Vector3 ClampMagnitude(Vector3 v, float m) => v.sqrMagnitude > m * m ? v.normalized * m : v;
        public static Vector3 MoveTowards(Vector3 c, Vector3 t, float d) { var v = t - c; float m = v.magnitude; return m <= d || m < 1e-6f ? t : c + v / m * d; }
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => Lerp(a, b, t);
        public static float Angle(Vector3 a, Vector3 b) { float d = Mathf.Clamp(Dot(a.normalized, b.normalized), -1f, 1f); return (float)Math.Acos(d) * Mathf.Rad2Deg; }
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
    }

    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int x, y, z;
        public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public bool Equals(Vector3Int o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object o) => o is Vector3Int v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color red => new Color(1, 0, 0, 1);
        public static Color green => new Color(0, 1, 0, 1);
        public static Color blue => new Color(0, 0, 1, 1);
        public static Color yellow => new Color(1, 0.92156863f, 0.015686275f, 1);
        public static Color cyan => new Color(0, 1, 1, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1);
        public static Color grey => gray;
        public static Color magenta => new Color(1, 0, 1, 1);
        public float this[int i] { get => i == 0 ? r : i == 1 ? g : i == 2 ? b : a; set { if (i == 0) r = value; else if (i == 1) g = value; else if (i == 2) b = value; else a = value; } }
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a * k);
        public static Color operator *(float k, Color c) => c * k;
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator /(Color c, float k) => new Color(c.r / k, c.g / k, c.b / k, c.a / k);
        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public static Color Lerp(Color a, Color b, float t) { t = Mathf.Clamp01(t); return LerpUnclamped(a, b, t); }
        public static Color LerpUnclamped(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;
        public float maxColorComponent => Math.Max(r, Math.Max(g, b));
        public Color linear => new Color(Mathf.GammaToLinearSpace(r), Mathf.GammaToLinearSpace(g), Mathf.GammaToLinearSpace(b), a);
        public Color gamma => new Color(Mathf.LinearToGammaSpace(r), Mathf.LinearToGammaSpace(g), Mathf.LinearToGammaSpace(b), a);
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)Math.Round(Mathf.Clamp01(c.r) * 255f), (byte)Math.Round(Mathf.Clamp01(c.g) * 255f), (byte)Math.Round(Mathf.Clamp01(c.b) * 255f), (byte)Math.Round(Mathf.Clamp01(c.a) * 255f));
        public static implicit operator Vector3(Color c) => new Vector3(c.r, c.g, c.b);
        public static void RGBToHSV(Color c, out float h, out float s, out float v)
        {
            float max = Math.Max(c.r, Math.Max(c.g, c.b)), min = Math.Min(c.r, Math.Min(c.g, c.b));
            v = max; float d = max - min; s = max > 0 ? d / max : 0;
            if (d <= 0) { h = 0; return; }
            if (max == c.r) h = ((c.g - c.b) / d) % 6f; else if (max == c.g) h = (c.b - c.r) / d + 2f; else h = (c.r - c.g) / d + 4f;
            h /= 6f; if (h < 0) h += 1f;
        }
        public static Color HSVToRGB(float h, float s, float v)
        {
            h = (h % 1f + 1f) % 1f * 6f; int i = (int)Math.Floor(h); float f = h - i;
            float p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            switch (i % 6) { case 0: return new Color(v, t, p); case 1: return new Color(q, v, p); case 2: return new Color(p, v, t); case 3: return new Color(p, q, v); case 4: return new Color(t, p, v); default: return new Color(v, p, q); }
        }
        public override string ToString() => $"RGBA({r:F3}, {g:F3}, {b:F3}, {a:F3})";
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color32 Lerp(Color32 x, Color32 y, float t) => Color.Lerp(x, y, t);
    }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string s, out Color c)
        {
            c = Color.magenta;
            if (string.IsNullOrEmpty(s) || s[0] != '#') return false;
            s = s.Substring(1);
            try
            {
                if (s.Length == 3 || s.Length == 4)
                {
                    float Q(int i) => Convert.ToInt32(new string(s[i], 2), 16) / 255f;
                    c = new Color(Q(0), Q(1), Q(2), s.Length == 4 ? Q(3) : 1f);
                    return true;
                }
                if (s.Length == 6 || s.Length == 8)
                {
                    float P(int i) => Convert.ToInt32(s.Substring(i, 2), 16) / 255f;
                    c = new Color(P(0), P(2), P(4), s.Length == 8 ? P(6) : 1f);
                    return true;
                }
            }
            catch { }
            return false;
        }
        public static string ToHtmlStringRGB(Color c) { Color32 k = c; return $"{k.r:X2}{k.g:X2}{k.b:X2}"; }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Epsilon = 1.401298E-45f;
        public const float Infinity = float.PositiveInfinity;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Acos(float f) => (float)Math.Acos(f);
        public static float Atan(float f) => (float)Math.Atan(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) m = Math.Min(m, x); return m; }
        public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) m = Math.Max(m, x); return m; }
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Exp(float a) => (float)Math.Exp(a);
        public static float Log(float a) => (float)Math.Log(a);
        public static float Log(float a, float b) => (float)Math.Log(a, b);
        public static float Log10(float a) => (float)Math.Log10(a);
        public static float Floor(float a) => (float)Math.Floor(a);
        public static float Ceil(float a) => (float)Math.Ceiling(a);
        public static float Round(float a) => (float)Math.Round(a, MidpointRounding.ToEven);
        public static int FloorToInt(float a) => (int)Math.Floor(a);
        public static int CeilToInt(float a) => (int)Math.Ceiling(a);
        public static int RoundToInt(float a) => (int)Math.Round(a, MidpointRounding.ToEven);
        public static float Sign(float a) => a >= 0 ? 1f : -1f;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float Repeat(float t, float l) => Clamp(t - Floor(t / l) * l, 0f, l);
        public static float PingPong(float t, float l) { t = Repeat(t, l * 2f); return l - Abs(t - l); }
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return b * t + a * (1f - t); }
        public static bool Approximately(float a, float b) => Abs(b - a) < Max(1E-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);
        public static float MoveTowards(float c, float t, float d) => Abs(t - c) <= d ? t : c + Sign(t - c) * d;
        public static float DeltaAngle(float c, float t) { float d = Repeat(t - c, 360f); if (d > 180f) d -= 360f; return d; }
        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * Clamp01(t);
        public static float MoveTowardsAngle(float c, float t, float d) { float dd = DeltaAngle(c, t); return -d < dd && dd < d ? t : MoveTowards(c, c + dd, d); }
        public static float SmoothDamp(float c, float t, ref float v, float st, float max = float.PositiveInfinity, float dt = 1f / 60f) { v = 0; return t; }
        public static float GammaToLinearSpace(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);
        public static float LinearToGammaSpace(float c) { c = Math.Max(0f, c); return c <= 0.0031308f ? c * 12.92f : 1.055f * (float)Math.Pow(c, 1 / 2.4f) - 0.055f; }
        public static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
        public static int NextPowerOfTwo(int v) { int p = 1; while (p < v) p <<= 1; return p; }

        // ---- gradient noise (Perlin) in about 0..1, like Unity's
        static readonly int[] Perm = BuildPerm();
        static int[] BuildPerm()
        {
            int[] p =
            {
                151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
                88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,
                80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,
                189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,
                12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180,
            };
            var r = new int[512];
            for (int i = 0; i < 512; i++) r[i] = p[i & 255];
            return r;
        }
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }
        public static float PerlinNoise(float x, float y)
        {
            int xi = (int)Math.Floor(x) & 255, yi = (int)Math.Floor(y) & 255;
            float xf = x - (float)Math.Floor(x), yf = y - (float)Math.Floor(y);
            float u = Fade(xf), v = Fade(yf);
            int aa = Perm[Perm[xi] + yi], ab = Perm[Perm[xi] + yi + 1], ba = Perm[Perm[xi + 1] + yi], bb = Perm[Perm[xi + 1] + yi + 1];
            float x1 = Grad(aa, xf, yf) + (Grad(ba, xf - 1f, yf) - Grad(aa, xf, yf)) * u;
            float x2 = Grad(ab, xf, yf - 1f) + (Grad(bb, xf - 1f, yf - 1f) - Grad(ab, xf, yf - 1f)) * u;
            float n = x1 + (x2 - x1) * v;   // about −1..1
            return 0.5f + 0.5f * n;
        }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z);
            var t = 2f * Vector3.Cross(u, v);
            return v + q.w * t + Vector3.Cross(u, t);
        }
        public static bool operator ==(Quaternion a, Quaternion b) => Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w) > 0.999999f;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            axis = axis.normalized; float h = deg * Mathf.Deg2Rad * 0.5f; float s = (float)Math.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
        }
        public static Quaternion Euler(float x, float y, float z) => AngleAxis(y, Vector3.up) * AngleAxis(x, Vector3.right) * AngleAxis(z, Vector3.forward);
        public static Quaternion Euler(Vector3 e) => Euler(e.x, e.y, e.z);
        public static Quaternion Inverse(Quaternion q) { float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w; return new Quaternion(-q.x / n, -q.y / n, -q.z / n, q.w / n); }
        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        public static float Angle(Quaternion a, Quaternion b) { float d = Math.Min(Math.Abs(Dot(a, b)), 1f); return d > 0.999999f ? 0f : (float)Math.Acos(d) * 2f * Mathf.Rad2Deg; }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            from = from.normalized; to = to.normalized;
            float d = Vector3.Dot(from, to);
            if (d > 0.999999f) return identity;
            if (d < -0.999999f)
            {
                var axis = Vector3.Cross(Vector3.right, from);
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(Vector3.up, from);
                return AngleAxis(180f, axis);
            }
            var c = Vector3.Cross(from, to);
            return Normalize(new Quaternion(c.x, c.y, c.z, 1f + d));
        }
        public static Quaternion Normalize(Quaternion q) { float n = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n); }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            forward = forward.normalized;
            var right = Vector3.Cross(up, forward).normalized;
            if (right.sqrMagnitude < 1e-8f) return FromToRotation(Vector3.forward, forward);
            var u = Vector3.Cross(forward, right);
            float m00 = right.x, m01 = u.x, m02 = forward.x;
            float m10 = right.y, m11 = u.y, m12 = forward.y;
            float m20 = right.z, m21 = u.z, m22 = forward.z;
            float tr = m00 + m11 + m22;
            Quaternion q;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1f) * 2f; q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s); }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f; q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f; q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            else { float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f; q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
            return Normalize(q);
        }
        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            t = Mathf.Clamp01(t);
            float d = Dot(a, b);
            if (d < 0) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); }
            return Normalize(new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t));
        }
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => Slerp(a, b, t);
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float maxDeg) { float ang = Angle(a, b); return ang < 1e-4f ? b : Slerp(a, b, Math.Min(1f, maxDeg / ang)); }
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public float this[int r, int c]
        {
            get { switch (r * 4 + c) { case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03; case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13; case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23; case 12: return m30; case 13: return m31; case 14: return m32; default: return m33; } }
            set { switch (r * 4 + c) { case 0: m00 = value; break; case 1: m01 = value; break; case 2: m02 = value; break; case 3: m03 = value; break; case 4: m10 = value; break; case 5: m11 = value; break; case 6: m12 = value; break; case 7: m13 = value; break; case 8: m20 = value; break; case 9: m21 = value; break; case 10: m22 = value; break; case 11: m23 = value; break; case 12: m30 = value; break; case 13: m31 = value; break; case 14: m32 = value; break; default: m33 = value; break; } }
        }
        public static Matrix4x4 identity { get { var m = new Matrix4x4(); m.m00 = m.m11 = m.m22 = m.m33 = 1f; return m; } }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { float s = 0; for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j]; r[i, j] = s; }
            return r;
        }
        public static Matrix4x4 Translate(Vector3 t) { var m = identity; m.m03 = t.x; m.m13 = t.y; m.m23 = t.z; return m; }
        public static Matrix4x4 Scale(Vector3 s) { var m = identity; m.m00 = s.x; m.m11 = s.y; m.m22 = s.z; return m; }
        public static Matrix4x4 Rotate(Quaternion q)
        {
            float x = q.x * 2f, y = q.y * 2f, z = q.z * 2f, xx = q.x * x, yy = q.y * y, zz = q.z * z, xy = q.x * y, xz = q.x * z, yz = q.y * z, wx = q.w * x, wy = q.w * y, wz = q.w * z;
            var m = identity;
            m.m00 = 1f - (yy + zz); m.m10 = xy + wz; m.m20 = xz - wy;
            m.m01 = xy - wz; m.m11 = 1f - (xx + zz); m.m21 = yz + wx;
            m.m02 = xz + wy; m.m12 = yz - wx; m.m22 = 1f - (xx + yy);
            return m;
        }
        public static Matrix4x4 TRS(Vector3 t, Quaternion q, Vector3 s) => Translate(t) * Rotate(q) * Scale(s);
        public Matrix4x4 transpose { get { var r = new Matrix4x4(); for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[i, j] = this[j, i]; return r; } }
        public float determinant
        {
            get
            {
                double[,] a = new double[4, 4]; for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) a[i, j] = this[i, j];
                double det = 1;
                for (int c = 0; c < 4; c++)
                {
                    int p = c; for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                    if (Math.Abs(a[p, c]) < 1e-12) return 0;
                    if (p != c) { for (int k = 0; k < 4; k++) { var t = a[p, k]; a[p, k] = a[c, k]; a[c, k] = t; } det = -det; }
                    det *= a[c, c];
                    for (int r = c + 1; r < 4; r++) { double f = a[r, c] / a[c, c]; for (int k = c; k < 4; k++) a[r, k] -= f * a[c, k]; }
                }
                return (float)det;
            }
        }
        public Matrix4x4 inverse
        {
            get
            {
                double[,] a = new double[4, 8];
                for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) a[i, j] = this[i, j]; a[i, 4 + i] = 1; }
                for (int c = 0; c < 4; c++)
                {
                    int p = c; for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                    if (Math.Abs(a[p, c]) < 1e-12) return new Matrix4x4();
                    if (p != c) for (int k = 0; k < 8; k++) { var t = a[p, k]; a[p, k] = a[c, k]; a[c, k] = t; }
                    double d = a[c, c]; for (int k = 0; k < 8; k++) a[c, k] /= d;
                    for (int r = 0; r < 4; r++) if (r != c) { double f = a[r, c]; for (int k = 0; k < 8; k++) a[r, k] -= f * a[c, k]; }
                }
                var m = new Matrix4x4(); for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) m[i, j] = (float)a[i, 4 + j];
                return m;
            }
        }
        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        public Vector3 MultiplyPoint(Vector3 p) { var v = MultiplyPoint3x4(p); float w = m30 * p.x + m31 * p.y + m32 * p.z + m33; return w != 0 ? v / w : v; }
        public Vector3 MultiplyVector(Vector3 v) => new Vector3(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z);
        public Vector4 GetColumn(int c) => new Vector4(this[0, c], this[1, c], this[2, c], this[3, c]);
    }

    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 c, Vector3 size) { center = c; extents = size * 0.5f; }
        public Vector3 size { get => extents * 2f; set => extents = value * 0.5f; }
        public Vector3 min { get => center - extents; set => SetMinMax(value, max); }
        public Vector3 max { get => center + extents; set => SetMinMax(min, value); }
        public void SetMinMax(Vector3 mn, Vector3 mx) { extents = (mx - mn) * 0.5f; center = mn + extents; }
        public void Encapsulate(Vector3 p) => SetMinMax(Vector3.Min(min, p), Vector3.Max(max, p));
        public void Encapsulate(Bounds b) { Encapsulate(b.min); Encapsulate(b.max); }
        public void Expand(float a) { extents += new Vector3(a, a, a) * 0.5f; }
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        public bool Intersects(Bounds b) => min.x <= b.max.x && max.x >= b.min.x && min.y <= b.max.y && max.y >= b.min.y && min.z <= b.max.z && max.z >= b.min.z;
        public override string ToString() => $"Center: {center}, Extents: {extents}";
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public Rect(Vector2 pos, Vector2 size) { x = pos.x; y = pos.y; width = size.x; height = size.y; }
        public float xMin { get => x; set { float mx = xMax; x = value; width = mx - x; } }
        public float yMin { get => y; set { float my = yMax; y = value; height = my - y; } }
        public float xMax { get => x + width; set => width = value - x; }
        public float yMax { get => y + height; set => height = value - y; }
        public Vector2 center => new Vector2(x + width * 0.5f, y + height * 0.5f);
        public Vector2 position => new Vector2(x, y);
        public Vector2 size => new Vector2(width, height);
        public Vector2 min => new Vector2(xMin, yMin);
        public Vector2 max => new Vector2(xMax, yMax);
        public static Rect MinMaxRect(float x0, float y0, float x1, float y1) => new Rect(x0, y0, x1 - x0, y1 - y0);
        public bool Contains(Vector2 p) => p.x >= xMin && p.x < xMax && p.y >= yMin && p.y < yMax;
        public bool Overlaps(Rect o) => o.xMax > xMin && o.xMin < xMax && o.yMax > yMin && o.yMin < yMax;
    }

    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 o, Vector3 d) { origin = o; direction = d.normalized; }
        public Vector3 GetPoint(float t) => origin + direction * t;
    }

    public struct BoneWeight { public int boneIndex0; public float weight0; }

    public enum HideFlags { None = 0, DontSave = 52, HideAndDontSave = 61 }
    public enum Space { World, Self }
    public enum TextureFormat { RGBA32 = 4, ARGB32 = 5, RGB24 = 3, Alpha8 = 1 }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
    public enum SpriteMeshType { FullRect, Tight }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute { }

    public static class Debug
    {
        public static bool Quiet;
        public static void Log(object o) { if (!Quiet) Console.Error.WriteLine(o); }
        public static void LogWarning(object o) { if (!Quiet) Console.Error.WriteLine("WARN " + o); }
        public static void LogError(object o) => Console.Error.WriteLine("ERROR " + o);
        public static void LogException(Exception e) => Console.Error.WriteLine("EXCEPTION " + e);
    }

    public static class Time { public static float time, deltaTime = 1f / 60f, unscaledTime, unscaledDeltaTime = 1f / 60f, realtimeSinceStartup; public static int frameCount; }
    public static class Application { public static bool isPlaying; public static string dataPath = ""; }
    public static class QualitySettings { public static ColorSpace activeColorSpace = ColorSpace.Linear; public static int antiAliasing; }
    public static class Screen { public static int width = 1600, height = 900; }

    public static class Random
    {
        static System.Random rng = new System.Random(1234);
        public static void InitState(int seed) { rng = new System.Random(seed); }
        public static float value => (float)rng.NextDouble();
        public static float Range(float a, float b) => a + (b - a) * value;
        public static int Range(int a, int b) => rng.Next(a, b);
    }

    public class Object
    {
        public string name = "";
        public HideFlags hideFlags;
        public static void Destroy(Object o) { if (o is GameObject g) g.destroyed = true; }
        public static void DestroyImmediate(Object o) => Destroy(o);
        public static void DontDestroyOnLoad(Object o) { }
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
    }

    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        public List<Vector3> V = new List<Vector3>();
        public List<Vector3> N = new List<Vector3>();
        public List<Color> C = new List<Color>();
        public List<Vector2> UV0 = new List<Vector2>();
        public List<Vector2> UV1 = new List<Vector2>();
        public List<int> T = new List<int>();
        public BoneWeight[] boneWeights;
        public Matrix4x4[] bindposes;
        public Bounds bounds;
        public void Clear() { V.Clear(); N.Clear(); C.Clear(); UV0.Clear(); UV1.Clear(); T.Clear(); }
        public void SetVertices(List<Vector3> v) { V = new List<Vector3>(v); }
        public void SetNormals(List<Vector3> v) { N = new List<Vector3>(v); }
        public void SetColors(List<Color> v) { C = new List<Color>(v); }
        public void SetColors(List<Color32> v) { C = new List<Color>(v.Count); foreach (var c in v) C.Add(c); }
        public void SetUVs(int ch, List<Vector2> v) { if (ch == 0) UV0 = new List<Vector2>(v); else if (ch == 1) UV1 = new List<Vector2>(v); }
        public void SetUVs(int ch, List<Vector4> v) { var l = new List<Vector2>(v.Count); foreach (var q in v) l.Add(new Vector2(q.x, q.y)); SetUVs(ch, l); }
        public void SetTangents(List<Vector4> v) { }
        public void SetTriangles(List<int> t, int sub, bool calc = true) { T = new List<int>(t); if (calc) RecalculateBounds(); }
        public void SetTriangles(int[] t, int sub, bool calc = true) { T = new List<int>(t); if (calc) RecalculateBounds(); }
        public void SetIndices(int[] t, MeshTopology topo, int sub) { T = new List<int>(t); }
        public Vector3[] vertices { get => V.ToArray(); set { V = new List<Vector3>(value); } }
        public Vector3[] normals { get => N.ToArray(); set { N = new List<Vector3>(value); } }
        public Color[] colors { get => C.ToArray(); set { C = new List<Color>(value); } }
        public Color32[] colors32 { get { var r = new Color32[C.Count]; for (int i = 0; i < r.Length; i++) r[i] = C[i]; return r; } set { C = new List<Color>(value.Length); foreach (var c in value) C.Add(c); } }
        public Vector2[] uv { get => UV0.ToArray(); set { UV0 = new List<Vector2>(value); } }
        public int[] triangles { get => T.ToArray(); set { T = new List<int>(value); } }
        public int vertexCount => V.Count;
        public void RecalculateBounds()
        {
            if (V.Count == 0) { bounds = new Bounds(); return; }
            var b = new Bounds(V[0], Vector3.zero);
            foreach (var p in V) b.Encapsulate(p);
            bounds = b;
        }
        public void RecalculateNormals() { }
        public void UploadMeshData(bool b) { }
        public void MarkDynamic() { }
    }

    public enum MeshTopology { Triangles, Quads, Lines }

    /// <summary>Shader handle; name selects the preview renderer's shading model.</summary>
    public class Shader : Object
    {
        public bool isSupported = true;
        static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        static readonly List<string> names = new List<string>();
        public static Shader Find(string n) => new Shader { name = n };
        public static int PropertyToID(string n)
        {
            lock (ids)
            {
                if (ids.TryGetValue(n, out int id)) return id;
                id = names.Count; names.Add(n); ids[n] = id; return id;
            }
        }
        public static string NameOf(int id) => id >= 0 && id < names.Count ? names[id] : "";
        public static void SetGlobalVector(int id, Vector4 v) { }
        public static void SetGlobalFloat(int id, float v) { }
        public static void SetGlobalColor(int id, Color v) { }
        public static void SetGlobalVectorArray(int id, Vector4[] v) { }
    }

    public class Texture : Object
    {
        public int width, height;
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        public int anisoLevel;
    }

    /// <summary>Texture with real pixels (RGBA, row 0 = bottom like Unity).</summary>
    public class Texture2D : Texture
    {
        public Color32[] Pixels;
        static Texture2D white;
        public static Texture2D whiteTexture => white ?? (white = Solid(255));
        static Texture2D Solid(byte v) { var t = new Texture2D(4, 4); for (int i = 0; i < 16; i++) t.Pixels[i] = new Color32(v, v, v, 255); t.name = "white"; return t; }
        public Texture2D(int w, int h) { width = w; height = h; Pixels = new Color32[w * h]; }
        public Texture2D(int w, int h, TextureFormat f, bool mips) : this(w, h) { }
        public Texture2D(int w, int h, TextureFormat f, bool mips, bool linear) : this(w, h) { }
        public void SetPixels32(Color32[] px) { Array.Copy(px, Pixels, Math.Min(px.Length, Pixels.Length)); }
        public void SetPixels(Color[] px) { for (int i = 0; i < Math.Min(px.Length, Pixels.Length); i++) Pixels[i] = px[i]; }
        public void SetPixel(int x, int y, Color c) { Pixels[y * width + x] = c; }
        public Color GetPixel(int x, int y) => Pixels[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];
        public Color32[] GetPixels32() => (Color32[])Pixels.Clone();
        public void Apply() { }
        public void Apply(bool mips) { }
        public void Apply(bool mips, bool makeNoLongerReadable) { }
        public bool LoadImage(byte[] data) => false;
    }

    public class Sprite : Object { }

    public class Material : Object
    {
        public Shader shader;
        public int renderQueue = -1;
        public Vector2 mainTextureOffset;
        public Vector2 mainTextureScale = Vector2.one;
        public readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
        public readonly Dictionary<int, Color> Colors = new Dictionary<int, Color>();
        public readonly Dictionary<int, Texture> Textures = new Dictionary<int, Texture>();
        public Material(Shader s) { shader = s; }
        public Material(Material m)
        {
            shader = m.shader; renderQueue = m.renderQueue; mainTextureOffset = m.mainTextureOffset; name = m.name;
            foreach (var kv in m.Floats) Floats[kv.Key] = kv.Value;
            foreach (var kv in m.Colors) Colors[kv.Key] = kv.Value;
            foreach (var kv in m.Textures) Textures[kv.Key] = kv.Value;
        }
        public Texture mainTexture { get => GetTexture("_MainTex"); set => SetTexture("_MainTex", value); }
        public Color color { get => GetColor("_Color"); set => SetColor("_Color", value); }
        public void SetFloat(int id, float v) => Floats[id] = v;
        public void SetFloat(string n, float v) => Floats[Shader.PropertyToID(n)] = v;
        public void SetInt(int id, int v) => Floats[id] = v;
        public void SetColor(int id, Color v) => Colors[id] = v;
        public void SetColor(string n, Color v) => Colors[Shader.PropertyToID(n)] = v;
        public void SetVector(int id, Vector4 v) => Colors[id] = new Color(v.x, v.y, v.z, v.w);
        public void SetTexture(int id, Texture t) => Textures[id] = t;
        public void SetTexture(string n, Texture t) => Textures[Shader.PropertyToID(n)] = t;
        public float GetFloat(int id) => Floats.TryGetValue(id, out var v) ? v : 0f;
        public float GetFloat(string n) => GetFloat(Shader.PropertyToID(n));
        public Color GetColor(int id) => Colors.TryGetValue(id, out var v) ? v : Color.white;
        public Color GetColor(string n) => GetColor(Shader.PropertyToID(n));
        public Texture GetTexture(int id) => Textures.TryGetValue(id, out var v) ? v : null;
        public Texture GetTexture(string n) => GetTexture(Shader.PropertyToID(n));
        public bool HasProperty(int id) => true;
        public bool HasProperty(string n) => true;
        public void EnableKeyword(string k) { }
        public void DisableKeyword(string k) { }
    }

    public class MaterialPropertyBlock
    {
        public readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
        public readonly Dictionary<int, Color> Colors = new Dictionary<int, Color>();
        public readonly Dictionary<int, Texture> Textures = new Dictionary<int, Texture>();
        public bool isEmpty => Floats.Count == 0 && Colors.Count == 0 && Textures.Count == 0;
        public void Clear() { Floats.Clear(); Colors.Clear(); Textures.Clear(); }
        public void SetColor(int id, Color c) => Colors[id] = c;
        public void SetFloat(int id, float f) => Floats[id] = f;
        public void SetVector(int id, Vector4 v) => Colors[id] = new Color(v.x, v.y, v.z, v.w);
        public void SetTexture(int id, Texture t) => Textures[id] = t;
        public void CopyFrom(MaterialPropertyBlock o)
        {
            Clear();
            foreach (var kv in o.Floats) Floats[kv.Key] = kv.Value;
            foreach (var kv in o.Colors) Colors[kv.Key] = kv.Value;
            foreach (var kv in o.Textures) Textures[kv.Key] = kv.Value;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string tag = "";
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : Component => gameObject.GetComponentInChildren<T>();
    }

    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }

    public class Transform : Component
    {
        Transform parentT;
        public readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition = Vector3.zero;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public Transform parent { get => parentT; set => SetParent(value, true); }
        public void SetParent(Transform p) => SetParent(p, true);
        public void SetParent(Transform p, bool worldPositionStays)
        {
            var wp = position; var wr = rotation;
            parentT?.children.Remove(this);
            parentT = p;
            p?.children.Add(this);
            if (worldPositionStays) { position = wp; rotation = wr; }
        }
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        public Matrix4x4 localToWorldMatrix => (parentT != null ? parentT.localToWorldMatrix : Matrix4x4.identity) * Matrix4x4.TRS(localPosition, localRotation, localScale);
        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;
        public Vector3 position
        {
            get => localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
            set => localPosition = parentT != null ? parentT.localToWorldMatrix.inverse.MultiplyPoint3x4(value) : value;
        }
        public Quaternion rotation
        {
            get => parentT != null ? parentT.rotation * localRotation : localRotation;
            set => localRotation = parentT != null ? Quaternion.Inverse(parentT.rotation) * value : value;
        }
        public Vector3 localEulerAngles { set => localRotation = Quaternion.Euler(value); }
        public Vector3 eulerAngles { set => rotation = Quaternion.Euler(value); }
        public Vector3 lossyScale => parentT != null ? Vector3.Scale(parentT.lossyScale, localScale) : localScale;
        public Vector3 right => rotation * Vector3.right;
        public Vector3 up => rotation * Vector3.up;
        public Vector3 forward => rotation * Vector3.forward;
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
        public Vector3 TransformPoint(Vector3 p) => localToWorldMatrix.MultiplyPoint3x4(p);
        public Vector3 InverseTransformPoint(Vector3 p) => worldToLocalMatrix.MultiplyPoint3x4(p);
        public Vector3 TransformDirection(Vector3 d) => rotation * d;
        public void Rotate(Vector3 axis, float angle, Space s = Space.Self) { localRotation = localRotation * Quaternion.AngleAxis(angle, axis); }
        public void Rotate(float x, float y, float z) { localRotation = localRotation * Quaternion.Euler(x, y, z); }
        public void SetAsLastSibling() { }
    }

    public class GameObject : Object
    {
        readonly List<Component> comps = new List<Component>();
        public Transform transform;
        public bool activeSelf = true;
        internal bool destroyed;
        public int layer;
        public GameObject() : this("GameObject") { }
        public GameObject(string n) { name = n; transform = new Transform { gameObject = this }; comps.Add(transform); }
        public bool activeInHierarchy { get { if (!activeSelf || destroyed) return false; var p = transform.parent; return p == null || p.gameObject.activeInHierarchy; } }
        public T AddComponent<T>() where T : Component
        {
            var c = (T)Activator.CreateInstance(typeof(T));
            c.gameObject = this;
            comps.Add(c);
            return c;
        }
        public T GetComponent<T>() where T : Component { foreach (var c in comps) if (c is T t) return t; return null; }
        public T GetComponentInChildren<T>() where T : Component
        {
            var t = GetComponent<T>(); if (t != null) return t;
            foreach (var ch in transform.children) { var r = ch.gameObject.GetComponentInChildren<T>(); if (r != null) return r; }
            return null;
        }
        public void SetActive(bool b) { activeSelf = b; }
        public IEnumerable<Component> Components => comps;
    }

    public class Renderer : Component
    {
        public Material[] sharedMaterials = new Material[0];
        public Material sharedMaterial { get => sharedMaterials.Length > 0 ? sharedMaterials[0] : null; set => sharedMaterials = new[] { value }; }
        public Material material { get => sharedMaterial; set => sharedMaterial = value; }
        public Rendering.ShadowCastingMode shadowCastingMode;
        public Rendering.LightProbeUsage lightProbeUsage;
        public Rendering.ReflectionProbeUsage reflectionProbeUsage;
        public bool receiveShadows;
        public bool enabled = true;
        public int sortingOrder;
        public int sortingLayerID;
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        public void SetPropertyBlock(MaterialPropertyBlock b) { if (b == null) Block.Clear(); else Block.CopyFrom(b); }
        public void GetPropertyBlock(MaterialPropertyBlock b) { b.CopyFrom(Block); }
        public bool HasPropertyBlock() => !Block.isEmpty;
        public virtual Mesh RenderMesh => GetComponent<MeshFilter>()?.sharedMesh;
        public Bounds bounds
        {
            get
            {
                var m = RenderMesh;
                if (m == null) return new Bounds(transform.position, Vector3.zero);
                var lb = m.bounds; var M = transform.localToWorldMatrix;
                Bounds b = new Bounds(M.MultiplyPoint3x4(lb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    b.Encapsulate(M.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1))));
                return b;
            }
        }
    }

    public class MeshRenderer : Renderer { }
    public enum SkinQuality { Auto, Bone1, Bone2, Bone4 }
    /// <summary>Skinning happens in the preview's Collector (rigid: bone.localToWorld × bindpose per vertex).</summary>
    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh; public Transform[] bones; public Transform rootBone;
        public SkinQuality quality; public bool updateWhenOffscreen, skinnedMotionVectors; public Bounds localBounds;
        public override Mesh RenderMesh => sharedMesh;
    }
    public class MeshFilter : Component { public Mesh sharedMesh; public Mesh mesh { get => sharedMesh; set => sharedMesh = value; } }

    public class Camera : Behaviour
    {
        public float fieldOfView = 60f, nearClipPlane = 0.3f, farClipPlane = 1000f, aspect = 16f / 9f;
        public Color backgroundColor;
        public bool allowMSAA;
        public static Camera main;
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum LightProbeUsage { Off, BlendProbes }
    public enum ReflectionProbeUsage { Off, BlendProbes }
    public enum GraphicsDeviceType { Null = 4 }
}
