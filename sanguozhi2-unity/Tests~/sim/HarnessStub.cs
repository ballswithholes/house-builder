// (S) 替身自检：UnityStub 的 Random、SimLib 的 SeededRandom、Mathf.PerlinNoise 与网页版 core.js 的输出一致
// （参考值由 node 对 SG.Random.seed / SG.SeededRandom / SG.M.perlinNoise 求得）。
using System;

class PS
{
    static int Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        UnityEngine.Random.InitState(1);
        double[] a = { 0.6270739405881613, 0.002735721180215478, 0.5274470399599522, 0.9810509674716741, 0.9683778982143849 };
        foreach (var v in a) { double x = UnityEngine.Random.NextDouble(); Check.That(Math.Abs(x - v) < 1e-15, $"Random seed 1: {x} != {v}"); }
        UnityEngine.Random.InitState(12345);
        int[] b = { 979, 306, 484, 817, 509 };
        foreach (var v in b) { int x = UnityEngine.Random.Range(0, 1000); Check.That(x == v, $"Random.Range seed 12345: {x} != {v}"); }
        var r = new SeededRandom(7);
        int[] c = { 982, 334, 689, 126, 257 };
        foreach (var v in c) { int x = r.Next(1000); Check.That(x == v, $"SeededRandom(7).Next(1000): {x} != {v}"); }
        float[,] q = { { 0.3f, 0.7f }, { 12.5f, 3.25f }, { -4.2f, 7.9f }, { 100.01f, 55.5f }, { 1.12f, 0.28f } };
        double[] p = { 0.6747390657855696, 0.8373282619689817, 0.6844405763452455, 0.2933293243088317, 0.471066308098795 };
        // C# 的参数是 float（JS 是 double），容差放宽到 1e-5
        for (int i = 0; i < p.Length; i++) { float x = UnityEngine.Mathf.PerlinNoise(q[i, 0], q[i, 1]); Check.That(Math.Abs(x - p[i]) < 1e-5, $"PerlinNoise({q[i, 0]}, {q[i, 1]}): {x} != {p[i]}"); }
        Check.That(UnityEngine.Mathf.RoundToInt(2.5f) == 2 && UnityEngine.Mathf.RoundToInt(3.5f) == 4, "RoundToInt uses banker's rounding");
        UnityEngine.Color col;
        Check.That(UnityEngine.ColorUtility.TryParseHtmlString("#3ddc84", out col) && Math.Abs(col.g - 0xdc / 255f) < 1e-6 && col.a == 1, "TryParseHtmlString");
        Console.WriteLine(Check.Fails == 0 ? "ALL OK" : "FAILURES: " + Check.Fails);
        return Check.Fails;
    }
}
