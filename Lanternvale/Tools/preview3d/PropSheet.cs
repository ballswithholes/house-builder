// `props` mode: contact sheets of the prop library (PNG) with per-model stats and nav-collider footprint checks.
// Each tile: the model, its collider ellipse (red, on the ground), light anchors (magenta) and a 1.75 m person.
//   props <out.png> [key[,key@seed...]|all] [seed] [--views gqtfb] [--tile px] [--lit 0|1] [--open] [--stats]
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
static class PropSheet
{
    static readonly Dictionary<string, (float w, float h)> Colliders = new Dictionary<string, (float, float)>
    {
        ["prop_cottage_a"] = (5.0f, 2.2f), ["prop_cottage_b"] = (4.4f, 2.0f), ["prop_cottage_c"] = (5.0f, 2.2f),
        ["prop_inn"] = (7.0f, 2.6f), ["prop_smithy"] = (5.0f, 2.2f), ["prop_shop_stall"] = (3.0f, 1.1f),
        ["prop_windmill"] = (2.6f, 1.0f), ["prop_well"] = (1.8f, 0.9f), ["prop_fence"] = (3.0f, 0.35f),
        ["prop_lamp_post"] = (0.4f, 0.3f), ["prop_spirit_lantern"] = (0.9f, 0.5f), ["prop_spirit_lantern_dark"] = (0.9f, 0.5f),
        ["prop_tree_oak"] = (1.2f, 0.7f), ["prop_tree_pine"] = (1.0f, 0.6f), ["prop_tree_birch"] = (0.7f, 0.4f),
        ["prop_tree_dead"] = (1.0f, 0.6f), ["prop_tree_great"] = (3.4f, 2.6f), ["prop_bush_a"] = (1.2f, 0.6f),
        ["prop_bush_b"] = (1.2f, 0.6f), ["prop_rock_large"] = (2.0f, 1.0f), ["prop_stump"] = (0.9f, 0.5f),
        ["prop_log"] = (2.2f, 0.6f), ["prop_cart"] = (2.0f, 1.0f), ["prop_barrel"] = (0.8f, 0.5f), ["prop_crate"] = (0.9f, 0.6f),
        ["prop_hay"] = (1.2f, 0.7f), ["prop_signpost"] = (0.4f, 0.3f), ["prop_noticeboard"] = (1.6f, 0.5f),
        ["prop_bench"] = (1.4f, 0.4f), ["prop_campfire"] = (1.0f, 0.6f), ["prop_tent"] = (2.4f, 1.2f),
        ["prop_ruin_pillar"] = (1.1f, 0.6f), ["prop_spirit_statue"] = (0.8f, 0.5f), ["prop_blight_crystal"] = (1.2f, 0.7f),
        ["prop_banner"] = (0.4f, 0.3f), ["prop_stone_wall"] = (3.2f, 0.5f), ["prop_veg_patch"] = (2.8f, 1.4f),
        ["prop_washing_line"] = (3.4f, 0.3f),
    };

    public static readonly string[] AllKeys =
    {
        "prop_cottage_a", "prop_cottage_b", "prop_cottage_c", "prop_inn", "prop_smithy", "prop_windmill",
        "prop_shop_stall", "prop_well", "prop_fence", "prop_lamp_post", "prop_spirit_lantern", "prop_spirit_lantern_dark",
        "prop_tree_oak", "prop_tree_pine", "prop_tree_birch", "prop_tree_dead", "prop_tree_great",
        "prop_bush_a", "prop_bush_b", "prop_rock_large", "prop_rock_small", "prop_stump", "prop_log",
        "prop_cart", "prop_barrel", "prop_crate", "prop_hay", "prop_signpost", "prop_noticeboard", "prop_bench",
        "prop_campfire", "prop_tent", "prop_mushrooms", "prop_ruin_pillar", "prop_ruin_arch", "prop_shrine_gate",
        "prop_spirit_statue", "prop_blight_crystal", "prop_banner", "prop_bridge", "prop_stone_wall", "prop_veg_patch",
        "prop_washing_line", "prop_chest", "prop_chest_open",
        "fg_ferns", "fg_grass_a", "fg_grass_b", "fg_stones_a", "fg_stones_b", "fg_flowers_a", "fg_flowers_b",
    };

    // Ground = a child renderer the model leaves out of PropModel.Renderers on purpose (the Kusu plaza paving): it never
    // fades or tints with the prop and is not part of its solid footprint.
    sealed class Part { public Mesh Mesh; public Matrix4x4 M; public bool Outline; public string Name; public bool Ground; }

    public static int Run(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "props.png";
        string keysArg = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "all";
        int seed = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 0;
        int lit = -1; bool open = false; string views = "fgt"; int tile = 380; bool statsOnly = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--lit") lit = int.Parse(args[++i]);
            else if (args[i] == "--open") open = true;
            else if (args[i] == "--views") views = args[++i];
            else if (args[i] == "--tile") tile = int.Parse(args[++i]);
            else if (args[i] == "--stats") statsOnly = true;
        }
        if (outPath.Length == 0) statsOnly = true;
        var keys = keysArg == "all" ? AllKeys : keysArg.Split(',');
        var seeds = new List<(string key, int seed)>();
        foreach (var k in keys)
        {
            if (k.Contains('@')) { var p = k.Split('@'); seeds.Add((p[0], int.Parse(p[1]))); }
            else seeds.Add((k, seed));
        }

        int cols = views.Length;
        int perRow = Math.Max(1, 4 / Math.Max(1, cols)) * 1;
        if (cols == 1) perRow = 6;
        int rows = (seeds.Count + perRow - 1) / perRow;
        var img = statsOnly ? null : new SheetImage(tile * cols * perRow, tile * rows);
        int idx = 0;
        int grandTris = 0;
        foreach (var (key, sd) in seeds)
        {
            PropModel model;
            try { model = PropModels.Create(key, sd); }
            catch (Exception e) { Console.WriteLine($"{key}: EXCEPTION {e}"); idx++; continue; }
            if (lit >= 0) model.SetLit?.Invoke(lit == 1);
            if (open && model.Lid != null) model.Lid.localRotation = Quaternion.Euler(105f, 0f, 0f);
            var parts = Collect(model);
            int tris = parts.Sum(p => p.Mesh.T.Count / 3);
            grandTris += tris;
            Stats(key, sd, model, parts, tris);
            if (img != null)
            {
                int ox = (idx % perRow) * tile * cols, oy = (idx / perRow) * tile;
                for (int v = 0; v < cols; v++)
                    RenderTile(img, ox + v * tile, oy, tile, key, model, parts, views[v]);
            }
            idx++;
        }
        Console.WriteLine($"TOTAL tris {grandTris}");
        img?.Save(outPath);
        return 0;
    }

    static List<Part> Collect(PropModel model)
    {
        var list = new List<Part>();
        var inv = model.Root.transform.localToWorldMatrix.inverse;
        void Walk(Transform t, bool active)
        {
            active &= t.gameObject.activeSelf;
            var mf = t.gameObject.GetComponent<MeshFilter>();
            var mr = t.gameObject.GetComponent<MeshRenderer>();
            if (active && mf != null && mr != null && mf.sharedMesh != null && mr.enabled)
                list.Add(new Part { Mesh = mf.sharedMesh, M = inv * t.localToWorldMatrix, Outline = mr.sharedMaterials.Length > 1, Name = t.gameObject.name,
                                    Ground = model.Renderers.Count > 0 && !model.Renderers.Contains(mr) });
            foreach (var c in t.children.ToArray()) Walk(c, active);
        }
        Walk(model.Root.transform, true);
        return list;
    }

    static void Stats(string key, int seed, PropModel model, List<Part> parts, int tris)
    {
        var b = new Bounds(); bool first = true;
        float minY = float.MaxValue;
        foreach (var p in parts)
            foreach (var v in p.Mesh.V)
            {
                var w = p.M.MultiplyPoint3x4(v);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                minY = Math.Min(minY, w.y);
            }
        string partInfo = string.Join(" ", parts.Select(p => $"{p.Name}:{p.Mesh.T.Count / 3}{(p.Outline ? "" : "(noOL)")}{(p.Ground ? "(ground)" : "")}"));
        Console.WriteLine($"{key}@{seed}: tris {tris} [{partInfo}] H={model.Height:F2} R={model.Radius:F2} meshBounds min{b.min} max{b.max} LocalBounds min{model.LocalBounds.min} max{model.LocalBounds.max} lights[{string.Join(" ", model.LightAnchors)}] lid={(model.Lid != null)} lit={(model.SetLit != null)} sways={model.Sways}");
        if (minY < -0.02f) Console.WriteLine($"   WARN below ground: minY {minY:F2}");
        if (Colliders.TryGetValue(key, out var c))
        {
            float a = c.w * 0.5f, bb = c.h * 0.5f;
            float worst = 0f; Vector3 worstP = Vector3.zero; float backMax = 0f, sideMax = 0f;
            foreach (var p in parts)
            {
                if (p.Ground) continue;   // ground parts (plaza paving) are walkable, not solid
                foreach (var v in p.Mesh.V)
                {
                    var w = p.M.MultiplyPoint3x4(v);
                    if (w.y > 1.7f || w.y < -0.01f) continue;
                    backMax = Math.Max(backMax, w.z);
                    sideMax = Math.Max(sideMax, Math.Abs(w.x));
                    if (w.z > 0f) continue;
                    float viol;
                    if (Math.Abs(w.x) >= a) viol = Math.Abs(w.x) - a + Math.Abs(w.z);
                    else { float zf = -bb * (float)Math.Sqrt(1 - (w.x / a) * (w.x / a)); viol = zf - w.z; }
                    if (viol > worst) { worst = viol; worstP = w; }
                }
            }
            Console.WriteLine($"   footprint vs collider {c.w}x{c.h}: worst front violation {worst:F2} m at {worstP}; low parts |x|max {sideMax:F2} (a={a:F2}), back z max {backMax:F2}");
        }
    }

    // ---------------------------------------------------------------- rendering

    static readonly Vector3 Sun = new Vector3(-0.35f, 0.75f, -0.55f).normalized;

    static float Lin(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);
    static float Srgb(float c) { c = Math.Clamp(c, 0f, 1f); return c <= 0.0031308f ? c * 12.92f : 1.055f * (float)Math.Pow(c, 1 / 2.4f) - 0.055f; }

    static Vector3 Shade(Vector3 n, Color c)
    {
        var alb = new Vector3(Lin(c.r), Lin(c.g), Lin(c.b));
        float ndl = Vector3.Dot(n, Sun);
        float wrap = Math.Clamp((ndl + 0.45f) / 1.45f, 0f, 1f);
        float ramp = wrap * wrap * (3f - 2f * wrap);
        var sunCol = new Vector3(Lin(1f), Lin(0.93f), Lin(0.8f)) * 0.95f;
        float hemi = n.y * 0.5f + 0.5f;
        var sky = new Vector3(Lin(0.62f), Lin(0.68f), Lin(0.82f)); var gnd = new Vector3(Lin(0.42f), Lin(0.38f), Lin(0.34f));
        var amb = (gnd + (sky - gnd) * hemi) * 0.75f;
        var light = sunCol * ramp + amb;
        var col = Vector3.Scale(alb, light);
        float e = Math.Clamp(c.a, 0f, 1f);
        col = col + (alb * 1.15f - col) * e;
        return new Vector3(Srgb(col.x), Srgb(col.y), Srgb(col.z));
    }

    sealed class Cam
    {
        public Vector3 Pos, F, R, U; public float Fov = 38f; public bool Ortho; public float OrthoSize;
        public float Cx, Cy, Scale; public int Size, Ox, Oy;
        public bool Project(Vector3 p, out float sx, out float sy, out float depth)
        {
            var d = p - Pos;
            depth = Vector3.Dot(d, F);
            float x = Vector3.Dot(d, R), y = Vector3.Dot(d, U);
            if (!Ortho)
            {
                if (depth < 0.05f) { sx = sy = 0; return false; }
                float k = 1f / ((float)Math.Tan(Fov * 0.5f * Math.PI / 180f) * depth);
                x *= k; y *= k;
            }
            sx = Ox + (x - Cx) * Scale + Size * 0.5f;
            sy = Oy + Size * 0.5f - (y - Cy) * Scale;
            return true;
        }
    }

    static void RenderTile(SheetImage img, int ox, int oy, int size, string key, PropModel model, List<Part> parts, char view)
    {
        // bounds of the model
        var pts = new List<Vector3>();
        foreach (var p in parts) foreach (var v in p.Mesh.V) pts.Add(p.M.MultiplyPoint3x4(v));
        if (pts.Count == 0) return;
        var b = new Bounds(pts[0], Vector3.zero); foreach (var q in pts) b.Encapsulate(q);
        float personX = b.max.x + 0.7f;
        var personB = new Bounds(new Vector3(personX, 0.875f, 0f), new Vector3(0.5f, 1.75f, 0.5f));
        b.Encapsulate(personB.min); b.Encapsulate(personB.max);

        var cam = new Cam { Size = size, Ox = ox, Oy = oy };
        var target = new Vector3(b.center.x, b.center.y, b.center.z);
        float yaw = 0f, pitch = 50f;
        if (view == 'g') { yaw = 0f; pitch = 48f; }
        else if (view == 'f') { yaw = 0f; pitch = 12f; }
        else if (view == 'q') { yaw = 38f; pitch = 28f; }
        else if (view == 'b') { yaw = 160f; pitch = 25f; }
        else if (view == 't') { yaw = 0f; pitch = 89.9f; cam.Ortho = true; }
        float yr = yaw * Mathf.Deg2Rad, pr = pitch * Mathf.Deg2Rad;
        // camera sits in front (-Z), yaw > 0 moves it to the right (+X)
        var dir = new Vector3((float)Math.Sin(yr) * (float)Math.Cos(pr), (float)Math.Sin(pr), -(float)Math.Cos(yr) * (float)Math.Cos(pr));
        float dist = b.extents.magnitude * 3.2f + 2f;
        cam.Pos = target + dir * dist;
        cam.F = (target - cam.Pos).normalized;
        cam.R = Vector3.Cross(Vector3.up, cam.F).normalized;
        if (cam.R.sqrMagnitude < 0.5f) cam.R = Vector3.right;
        cam.U = Vector3.Cross(cam.F, cam.R);
        // fit
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        cam.Scale = 1f; cam.Cx = cam.Cy = 0; cam.Ox = 0; cam.Oy = 0; cam.Size = 0;
        var corners = new List<Vector3>(pts);
        foreach (var c in new[] { personB.min, personB.max }) corners.Add(c);
        foreach (var q in corners)
        {
            if (!cam.Project(q, out var sx, out var sy, out _)) continue;
            minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, -sy); maxY = Math.Max(maxY, -sy);
        }
        cam.Cx = (minX + maxX) * 0.5f; cam.Cy = (minY + maxY) * 0.5f;
        cam.Scale = (size * 0.9f) / Math.Max(maxX - minX, maxY - minY);
        cam.Ox = ox; cam.Oy = oy; cam.Size = size;

        var zb = new float[size * size];
        var id = new int[size * size];
        for (int i = 0; i < zb.Length; i++) { zb[i] = float.MaxValue; id[i] = -1; }
        // background
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = (float)y / size;
                img.Set(ox + x, oy + y, new Vector3(0.80f - 0.1f * t, 0.86f - 0.08f * t, 0.92f - 0.05f * t));
            }

        // ground grid (1 m), drawn as dots in the tile
        float gx0 = (float)Math.Floor(b.min.x) - 2, gx1 = (float)Math.Ceiling(b.max.x) + 2, gz0 = (float)Math.Floor(b.min.z) - 2, gz1 = (float)Math.Ceiling(b.max.z) + 2;
        DrawTri(img, cam, zb, id, -2, new Vector3(gx0, 0, gz0), new Vector3(gx0, 0, gz1), new Vector3(gx1, 0, gz1), new Vector3(0.70f, 0.74f, 0.62f), true);
        DrawTri(img, cam, zb, id, -2, new Vector3(gx0, 0, gz0), new Vector3(gx1, 0, gz1), new Vector3(gx1, 0, gz0), new Vector3(0.70f, 0.74f, 0.62f), true);
        for (float gx = gx0; gx <= gx1; gx += 1f)
            for (float gz = gz0; gz <= gz1; gz += 0.05f) Dot(img, cam, zb, new Vector3(gx, 0.001f, gz), new Vector3(0.6f, 0.64f, 0.54f));
        for (float gz = gz0; gz <= gz1; gz += 1f)
            for (float gx = gx0; gx <= gx1; gx += 0.05f) Dot(img, cam, zb, new Vector3(gx, 0.001f, gz), new Vector3(0.6f, 0.64f, 0.54f));

        // person reference
        var pmb = new MeshBuilder(1) { Color = new Color(0.35f, 0.45f, 0.75f) };
        pmb.Capsule(new Vector3(personX, 0f, 0f), 0.22f, 1.75f, 8);
        var pm = pmb.ToMesh("person");
        DrawMesh(img, cam, zb, id, pm, Matrix4x4.identity, 9999);

        int pid = 0;
        foreach (var p in parts) DrawMesh(img, cam, zb, id, p.Mesh, p.M, p.Outline ? pid++ : 5000 + pid++);

        // collider ellipse
        if (Colliders.TryGetValue(key, out var col))
            for (int i = 0; i < 400; i++)
            {
                float a = i * Mathf.PI * 2f / 400;
                var q = new Vector3((float)Math.Cos(a) * col.w * 0.5f, 0.01f, (float)Math.Sin(a) * col.h * 0.5f);
                Dot(img, cam, zb, q, new Vector3(0.9f, 0.1f, 0.1f), true);
            }
        // light anchors
        foreach (var la in model.LightAnchors) Dot(img, cam, null, la, new Vector3(1f, 0.2f, 1f), true, 3);

        // ink: silhouettes of outlined parts and depth breaks
        var ink = new Vector3(0.23f, 0.18f, 0.26f);
        var mark = new bool[size * size];
        for (int y = 1; y < size - 1; y++)
            for (int x = 1; x < size - 1; x++)
            {
                int i = y * size + x; int me = id[i];
                if (me < 0 || me >= 5000) continue;
                for (int k = 0; k < 4; k++)
                {
                    int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - size : i + size;
                    int o = id[j];
                    if (o < 0 || (o != me && o < 5000 && zb[j] > zb[i] * 1.02f) || (o == me && zb[j] > zb[i] * 1.04f + 0.05f)) { mark[i] = true; break; }
                }
            }
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (mark[y * size + x]) img.Set(ox + x, oy + y, ink);
        img.Text(ox + 4, oy + 4, key + " " + view);
    }

    static void DrawMesh(SheetImage img, Cam cam, float[] zb, int[] id, Mesh m, Matrix4x4 M, int pid)
    {
        var V = m.V; var T = m.T; var C = m.C; var N = m.N;
        bool smooth = N.Count == V.Count;
        for (int i = 0; i < T.Count; i += 3)
        {
            var a = M.MultiplyPoint3x4(V[T[i]]); var b = M.MultiplyPoint3x4(V[T[i + 1]]); var c = M.MultiplyPoint3x4(V[T[i + 2]]);
            var n = Vector3.Cross(b - a, c - a);
            // Unity: clockwise = front (left-handed); Cross(b-a, c-a) points at the viewer for a front face
            var toCam = cam.Ortho ? -cam.F : cam.Pos - a;
            if (Vector3.Dot(n, toCam) <= 0f) continue;   // back face culled
            n = n.normalized;
            if (smooth)
            {
                // the mesh's own vertex normals (flat-shaded parts have split vertices = face normals; soft foliage is
                // smooth), shaded per vertex and interpolated like the map renders
                Vector3 Vn(int k) { var vn = M.MultiplyVector(N[T[k]]); return vn.sqrMagnitude > 1e-12f ? vn.normalized : n; }
                Color Vc(int k) => C.Count > 0 ? C[T[k]] : Color.white;
                DrawTri(img, cam, zb, id, pid, a, b, c, Shade(Vn(i), Vc(i)), false, Shade(Vn(i + 1), Vc(i + 1)), Shade(Vn(i + 2), Vc(i + 2)));
                continue;
            }
            var cc = C.Count > 0 ? (C[T[i]] + C[T[i + 1]] + C[T[i + 2]]) * (1f / 3f) : Color.white;
            var shade = Shade(n, cc);
            DrawTri(img, cam, zb, id, pid, a, b, c, shade, false);
        }
    }

    // colB / colC given: the colour is interpolated across the triangle (col at a)
    static void DrawTri(SheetImage img, Cam cam, float[] zb, int[] id, int pid, Vector3 a, Vector3 b, Vector3 c, Vector3 col, bool ground,
                        Vector3? colB = null, Vector3? colC = null)
    {
        if (!cam.Project(a, out var ax, out var ay, out var az) || !cam.Project(b, out var bx, out var by, out var bz) || !cam.Project(c, out var cx, out var cy, out var cz)) return;
        int size = cam.Size;
        ax -= cam.Ox; bx -= cam.Ox; cx -= cam.Ox; ay -= cam.Oy; by -= cam.Oy; cy -= cam.Oy;
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (Math.Abs(area) < 1e-6f) return;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = w0 * az + w1 * bz + w2 * cz;
                int i = y * size + x;
                if (z >= zb[i]) continue;
                zb[i] = ground ? z + 0.02f : z;
                id[i] = pid;
                img.Set(cam.Ox + x, cam.Oy + y, colB.HasValue ? col * w0 + colB.Value * w1 + colC.Value * w2 : col);
            }
    }

    static void Dot(SheetImage img, Cam cam, float[] zb, Vector3 p, Vector3 col, bool onTop = false, int r = 0)
    {
        if (!cam.Project(p, out var sx, out var sy, out var z)) return;
        int x = (int)sx - cam.Ox, y = (int)sy - cam.Oy;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= cam.Size || yy >= cam.Size) continue;
                if (zb != null && !onTop && z > zb[yy * cam.Size + xx] + 0.01f) continue;
                if (zb != null && onTop && z > zb[yy * cam.Size + xx] + 0.05f) { if ((xx + yy) % 2 == 0) continue; }
                img.Set(cam.Ox + xx, cam.Oy + yy, col);
            }
    }
}

sealed class SheetImage
{
    readonly int w, h; readonly byte[] px;
    public SheetImage(int w, int h) { this.w = w; this.h = h; px = new byte[w * h * 3]; }
    public void Set(int x, int y, Vector3 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int i = (y * w + x) * 3;
        px[i] = (byte)Math.Clamp((int)(c.x * 255f), 0, 255); px[i + 1] = (byte)Math.Clamp((int)(c.y * 255f), 0, 255); px[i + 2] = (byte)Math.Clamp((int)(c.z * 255f), 0, 255);
    }

    // 3x5 pixel font for labels
    static readonly Dictionary<char, string> Font = new Dictionary<char, string>
    {
        ['a'] = "010101111101101", ['b'] = "110101110101110", ['c'] = "011100100100011", ['d'] = "110101101101110", ['e'] = "111100110100111",
        ['f'] = "111100110100100", ['g'] = "011100101101011", ['h'] = "101101111101101", ['i'] = "111010010010111", ['j'] = "001001001101010",
        ['k'] = "101101110101101", ['l'] = "100100100100111", ['m'] = "101111111101101", ['n'] = "110101101101101", ['o'] = "010101101101010",
        ['p'] = "110101110100100", ['q'] = "010101101110011", ['r'] = "110101110101101", ['s'] = "011100010001110", ['t'] = "111010010010010",
        ['u'] = "101101101101111", ['v'] = "101101101101010", ['w'] = "101101111111101", ['x'] = "101101010101101", ['y'] = "101101010010010",
        ['z'] = "111001010100111", ['_'] = "000000000000111", [' '] = "000000000000000", ['0'] = "111101101101111", ['1'] = "010110010010111",
        ['2'] = "110001010100111", ['3'] = "110001010001110", ['4'] = "101101111001001", ['5'] = "111100110001110", ['6'] = "011100111101111",
        ['7'] = "111001010010010", ['8'] = "111101111101111", ['9'] = "111101111001110", ['@'] = "010101111100011",
        ['-'] = "000000111000000", ['.'] = "000000000000010", ['/'] = "001001010100100", [':'] = "000010000010000",
        ['+'] = "000010111010000", ['('] = "010100100100010", [')'] = "010001001001010", [','] = "000000000010100",
        ['='] = "000111000111000", ['%'] = "101001010100101", ['>'] = "100010001010100",
    };

    public void Text(int x, int y, string s)
    {
        foreach (var ch in s.ToLowerInvariant())
        {
            if (Font.TryGetValue(ch, out var g))
                for (int r = 0; r < 5; r++) for (int c = 0; c < 3; c++)
                        if (g[r * 3 + c] == '1') for (int k = 0; k < 4; k++) Set(x + c * 2 + k % 2, y + r * 2 + k / 2, new Vector3(0.1f, 0.05f, 0.15f));
            x += 8;
        }
    }

    public void Fill(int x0, int y0, int fw, int fh, Vector3 c)
    {
        for (int y = y0; y < y0 + fh; y++) for (int x = x0; x < x0 + fw; x++) Set(x, y, c);
    }

    /// <summary>Copies a top-down RGB block (Frame.Output) into the sheet.</summary>
    public void Blit(int x0, int y0, int bw, int bh, byte[] rgb)
    {
        for (int y = 0; y < bh; y++)
        {
            int ty = y0 + y;
            if (ty < 0 || ty >= h) continue;
            for (int x = 0; x < bw; x++)
            {
                int tx = x0 + x;
                if (tx < 0 || tx >= w) continue;
                int s = (y * bw + x) * 3, d = (ty * w + tx) * 3;
                px[d] = rgb[s]; px[d + 1] = rgb[s + 1]; px[d + 2] = rgb[s + 2];
            }
        }
    }

    public void Save(string path) => Png.SaveRgb(path, w, h, px);
}
}
