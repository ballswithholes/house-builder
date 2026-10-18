// 冒烟测试用：无头编译（SANGUO_HEADLESS）时 Raster 没有 Unity 部分，这里按 Raster.ToTexture 的做法补一个扩展方法（替身纹理）
using UnityEngine;
namespace Sanguo
{
    static class FigureUnityExt
    {
        public static Texture2D ToTexture(this Raster r, bool mipmaps = false, bool readable = true)
        {
            var t = new Texture2D(r.W, r.H, TextureFormat.RGBA32, mipmaps);
            var b = r.ToRGBA8(); var px = new Color32[r.W * r.H];
            for (int y = 0; y < r.H; y++) for (int x = 0; x < r.W; x++) { int i = ((r.H - 1 - y) * r.W + x) * 4; px[y * r.W + x] = new Color32(b[i], b[i + 1], b[i + 2], b[i + 3]); }
            t.SetPixels32(px); t.Apply(mipmaps, !readable);
            return t;
        }
    }
}
