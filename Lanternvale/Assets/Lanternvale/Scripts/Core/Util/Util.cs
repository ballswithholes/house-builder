// Small engine-independent utilities shared by the rules engine and the Unity layer.
using System;
using System.Collections.Generic;

namespace Lanternvale.Util
{
    /// <summary>2D vector in world units (1 unit = 1 metre). The rules engine never touches UnityEngine.</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x, y;

        public Vec2(float x, float y) { this.x = x; this.y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);
        public static readonly Vec2 Right = new Vec2(1, 0);

        public float Length => (float)Math.Sqrt(x * x + y * y);
        public float SqrLength => x * x + y * y;
        public Vec2 Normalized { get { var l = Length; return l > 1e-6f ? new Vec2(x / l, y / l) : Zero; } }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.x / s, a.y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.y * b.y;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        /// <summary>Moves from a towards b by at most maxDistance.</summary>
        public static Vec2 MoveTowards(Vec2 a, Vec2 b, float maxDistance)
        {
            var d = b - a;
            var len = d.Length;
            if (len <= maxDistance || len < 1e-6f) return b;
            return a + d / len * maxDistance;
        }

        /// <summary>Unsigned angle in degrees between two directions.</summary>
        public static float Angle(Vec2 a, Vec2 b)
        {
            var la = a.Length; var lb = b.Length;
            if (la < 1e-6f || lb < 1e-6f) return 0;
            var c = Math.Max(-1f, Math.Min(1f, Dot(a, b) / (la * lb)));
            return (float)(Math.Acos(c) * 180.0 / Math.PI);
        }

        public bool Equals(Vec2 o) => this == o;
        public override bool Equals(object obj) => obj is Vec2 o && this == o;
        public override int GetHashCode() => x.GetHashCode() * 397 ^ y.GetHashCode();
        public override string ToString() => $"({x:0.##}, {y:0.##})";
    }

    /// <summary>Deterministic, seedable random number generator (xorshift128+). Serializable state for saves.</summary>
    [Serializable]
    public sealed class Rng
    {
        public ulong s0, s1;

        public Rng() : this((ulong)DateTime.UtcNow.Ticks) { }

        public Rng(ulong seed)
        {
            // splitmix64 to spread the seed
            s0 = SplitMix(ref seed);
            s1 = SplitMix(ref seed);
            if (s0 == 0 && s1 == 0) s1 = 1;
        }

        static ulong SplitMix(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong x = s0, y = s1;
            s0 = y;
            x ^= x << 23;
            s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return s1 + y;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Value => (NextULong() >> 40) / (float)(1UL << 24);

        /// <summary>Uniform float in [min, max].</summary>
        public float Range(float min, float max) => max <= min ? min : min + (max - min) * Value;

        /// <summary>Uniform int in [min, maxInclusive].</summary>
        public int Range(int min, int maxInclusive)
        {
            if (maxInclusive <= min) return min;
            return min + (int)(NextULong() % (ulong)(maxInclusive - min + 1));
        }

        /// <summary>True with the given percent chance (0-100).</summary>
        public bool Chance(float percent) => percent >= 100f || (percent > 0f && Value * 100f < percent);

        /// <summary>Rolls one d20 (1-20).</summary>
        public int D20() => Range(1, 20);

        public T Pick<T>(IList<T> list) => list == null || list.Count == 0 ? default : list[Range(0, list.Count - 1)];
    }

    /// <summary>Logging hook. The Unity layer points these at Debug.Log; tests print to console.</summary>
    public static class Log
    {
        public static Action<string> InfoHandler = s => Console.WriteLine(s);
        public static Action<string> WarnHandler = s => Console.WriteLine("WARN: " + s);
        public static Action<string> ErrorHandler = s => Console.WriteLine("ERROR: " + s);

        public static void Info(string s) => InfoHandler?.Invoke(s);
        public static void Warn(string s) => WarnHandler?.Invoke(s);
        public static void Error(string s) => ErrorHandler?.Invoke(s);
    }

    public static class MathUtil
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));

        /// <summary>WoW yards to world metres. All ranges/radii in data are in yards (WoW numbers).</summary>
        public const float YardsToMetres = 0.4f;
        public static float Yd(float yards) => yards * YardsToMetres;

        /// <summary>One combat round = 6 seconds of game time.</summary>
        public const float RoundSeconds = 6f;
    }
}
