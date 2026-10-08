// 无头测试的共用工具（每个 Harness*.cs 都与本文件、UnityStub.cs 和规则文件一起编译）。
using System;

// 网页版 SG.SeededRandom(seed) 的 C# 版（替代 System.Random，使 C# 与 JS 测试的随机玩家走同一序列）：
//   NextDouble() ∈ [0, 1)；Next(max) / Next(min, max) 不含 max；Next() ∈ [0, int.MaxValue)
public sealed class SeededRandom
{
    uint a;
    public SeededRandom(int seed) { a = unchecked((uint)seed ^ 0x9e3779b9u); }
    public double NextDouble()
    {
        unchecked
        {
            a += 0x6D2B79F5;
            uint t = (a ^ (a >> 15)) * (1u | a);
            t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
    public int Next() => (int)Math.Floor(NextDouble() * int.MaxValue);
    public int Next(int max) => (int)Math.Floor(NextDouble() * max);
    public int Next(int min, int max) => min + (int)Math.Floor(NextDouble() * (max - min));
}

// 生成数据的校验摘要：与导出工具（sanguozhi2-web/tools/export-unity-data.js 的 class Hash）逐字节相同的 FNV-1a 32 位。
//   S(字符串) = UTF-8 字节 + 0x00（null = 0x01 0x00）；I(整数) = 十进制文本；N(浮点) = floor(v × 10⁴ + 0.5) 的十进制文本
public sealed class DataHash
{
    uint h = 2166136261u;
    public int Count;
    void Bytes(byte[] b) { unchecked { foreach (var x in b) { h ^= x; h *= 16777619u; } } }
    public void S(string s)
    {
        Count++;
        if (s == null) { Bytes(new byte[] { 1, 0 }); return; }
        Bytes(System.Text.Encoding.UTF8.GetBytes(s)); Bytes(new byte[] { 0 });
    }
    public void I(int v) { S(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    public void I(int? v) { if (v.HasValue) I(v.Value); else S(null); }
    public void N(float v) { S(((long)Math.Floor((double)v * 1e4 + 0.5)).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    public void NA(float[] a) { I(a.Length); foreach (var v in a) N(v); }
    public void SA(string[] a) { I(a.Length); foreach (var v in a) S(v); }
    public uint Value => h;
    public string Hex => "0x" + h.ToString("x8");
}

// 简单的检查计数
public static class Check
{
    public static int Fails;
    public static bool That(bool cond, string msg) { if (!cond) { Fails++; Console.WriteLine("  FAIL " + msg); } return cond; }
}
