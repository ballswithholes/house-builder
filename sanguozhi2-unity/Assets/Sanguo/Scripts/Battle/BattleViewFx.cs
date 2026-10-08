// 三国志II 霸王的大陆 · 必杀技特效（网页版 js/battle-view.js「必杀技特效」一节的移植，第二版 DESIGN-V2 §4D）
//
// 公开接口（UNITY-V2 §5）
//   IEnumerator BattleView.SpecialFx(BUnit caster, List<BUnit> targets, string fx, Color color[, SpecialFxInfo info])   约 1–1.5 秒
//     caster：施展者；targets：受影响的部队（主目标在前）；fx：风格名（未知风格按 slash）；color：招式色（Specials 的 sp.color）。
//     info（可省略）：{ sp, res（BattleModel.UseSpecial 的结果）, target, onHit(i) }。命中瞬间回调 onHit(i)（i 为 res.hits 的下标），
//     控制层借此在命中时显示伤害数字；突击 / 击退的位移按 res.moved / res.pushed 播放，结束时部队已在新格。
//   风格：slash 斩击弧光 · dragon 青龙 · havoc 无双 · sweep 横扫 · dash 突击残影 · whirl 往来连斩 · arrows 箭雨 · arrow 一箭穿杨 ·
//         fire 火海 · wind 风助火势 · lightning 雷击 · water 水淹 · shock 怒吼冲击波 · aura 金光 · blossom 桃花 · spirit 符咒 ·
//         shield 护盾 · heal 治愈之光 · poison 毒雾 · shadow 暗影刺杀 · drain 吸魂 · haste 疾风 · claw 猛虎爪痕 · rock 落石
//   UnitVisual 另显示限时加成（BattleView.cs）；燃烧中的格子显示持续的小火苗与烟。
//
// 实现：网页版的自定义着色器（弧光、闪电、光柱、水墙、护盾）移植为 Resources/Shaders/SpecialFx.shader（_Mode 0–4）；
// 粒子为自带的轻量粒子池（面向镜头的方片，逐帧重建网格），参数与网页版逐一对应（寿命、初速、尺寸、重力、阻力、渐入、透明度曲线）。
// 书法大字用 UGUI（标签层），每帧投影并保持在画面内。全屏闪光 / 压暗为标签层最底下的 Image。
// 坐标：网页版 three 坐标 (x, y, z) 换成 Unity (x, y, -z)；装饰性随机用独立的 System.Random，不动规则随机数（UnityEngine.Random）。
// 所有临时网格、材质在特效结束或战场销毁时释放；共享贴图（同 Art.SoftDot）常驻。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Sanguo
{
    // 特效的附加信息（网页版 specialFx 的 info）
    public class SpecialFxInfo
    {
        public Special sp;
        public SpecialResult res;
        public BUnit target;
        public Action<int> onHit;
    }

    // 协程工具：逐层执行嵌套的 IEnumerator，异常时回调并停止（不让异常卡死调用方的协程）
    public static class Co
    {
        public static IEnumerator Guard(IEnumerator inner, Action<Exception> onError = null)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                object cur = null; bool next;
                try { next = top.MoveNext(); if (next) cur = top.Current; }
                catch (Exception e)
                {
                    foreach (var it in stack) { var d = it as IDisposable; if (d != null) { try { d.Dispose(); } catch (Exception) { /* 忽略 */ } } }
                    if (onError != null) onError(e); else Debug.LogException(e);
                    yield break;
                }
                if (!next) { stack.Pop(); continue; }
                var nested = cur as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return cur;
            }
        }
    }

    public partial class BattleView
    {
        // ======================================================== 公共入口 --
        static readonly Dictionary<string, string> SpGlyphOf = new Dictionary<string, string>
        {
            { "slash", "斩" }, { "dragon", "龙" }, { "havoc", "霸" }, { "sweep", "扫" }, { "dash", "突" }, { "whirl", "闪" }, { "arrows", "箭" }, { "arrow", "穿" },
            { "fire", "火" }, { "wind", "风" }, { "lightning", "雷" }, { "water", "水" }, { "shock", "喝" }, { "aura", "令" }, { "blossom", "义" }, { "spirit", "谋" },
            { "shield", "守" }, { "heal", "愈" }, { "poison", "毒" }, { "shadow", "杀" }, { "drain", "收" }, { "haste", "疾" }, { "claw", "虎" }, { "rock", "石" },
        };

        public IEnumerator SpecialFx(BUnit caster, List<BUnit> targets, string fx, Color color) { return SpecialFx(caster, targets, fx, color, null); }
        public IEnumerator SpecialFx(BUnit caster, List<BUnit> targets, string fx, Color color, SpecialFxInfo info)
        {
            if (this == null || M == null || caster == null) yield break;
            EnsurePools();
            info = info ?? new SpecialFxInfo();
            string style = !string.IsNullOrEmpty(fx) ? fx : (info.sp != null && !string.IsNullOrEmpty(info.sp.fx) ? info.sp.fx : "slash");
            var res = info.res;
            var list = (targets ?? new List<BUnit>()).Where(x => x != null).ToList();
            string glyph;
            if (!SpGlyphOf.TryGetValue(style, out glyph)) glyph = "斩";
            var c = new SpCtx
            {
                u = caster, v = VisOf(caster), style = style, color = color, res = res, info = info, targets = list,
                target = info.target ?? (list.Count > 0 ? list[0] : caster),
                hits = res != null ? res.hits : new List<SpecialHit>(), glyph = glyph,
            };
            if (res != null && res.kind == "rally" && style == "aura") c.glyph = "励";
            if (res != null && res.kind == "command" && style == "aura") c.glyph = "令";
            yield return Co.Guard(SpStyle(c), e => Debug.LogException(e));
            // 位移收尾：突击者与被击退者落到模型所在的格子
            if (res != null && this != null)
            {
                if (res.moved != null) SpFixPos(caster);
                foreach (var p in res.pushed) SpFixPos(p.unit);
            }
        }
        void SpFixPos(BUnit x) { var v = VisOf(x); if (v != null) v.transform.position = Tile(x.x, x.y); }

        IEnumerator SpStyle(SpCtx c)
        {
            switch (c.style)
            {
                case "dragon": return Sp_dragon(c);
                case "havoc": return Sp_havoc(c);
                case "sweep": return Sp_sweep(c);
                case "dash": return Sp_dash(c);
                case "whirl": return Sp_whirl(c);
                case "arrows": return Sp_arrows(c);
                case "arrow": return Sp_arrow(c);
                case "fire": return Sp_fire(c);
                case "wind": return Sp_wind(c);
                case "lightning": return Sp_lightning(c);
                case "water": return Sp_water(c);
                case "shock": return Sp_shock(c);
                case "aura": return Sp_aura(c);
                case "blossom": return Sp_blossom(c);
                case "spirit": return Sp_spirit(c);
                case "shield": return Sp_shield(c);
                case "heal": return Sp_heal(c);
                case "poison": return Sp_poison(c);
                case "shadow": return Sp_shadow(c);
                case "drain": return Sp_drain(c);
                case "haste": return Sp_haste(c);
                case "claw": case "roar": return Sp_claw(c);
                case "rock": return Sp_rock(c);
                default: return Sp_slash(c);
            }
        }

        sealed class SpCtx
        {
            public BUnit u, target; public UnitVisual v; public string style, glyph; public Color color;
            public SpecialResult res; public SpecialFxInfo info; public List<BUnit> targets; public List<SpecialHit> hits;
            public void Hit(int i)
            {
                if (info == null || info.onHit == null || i < 0 || i >= hits.Count) return;
                try { info.onHit(i); } catch (Exception e) { Debug.LogException(e); }
            }
            public void HitUnit(BUnit x) { for (int i = 0; i < hits.Count; i++) if (hits[i].unit == x) Hit(i); }
            public void HitAll() { for (int i = 0; i < hits.Count; i++) Hit(i); }
            public float Radius(float dflt) { return res != null && res.sp != null ? (float)res.sp.radius : dflt; }
        }

        // ======================================================== 工具 --
        static readonly System.Random fxR = new System.Random();
        static float Rnd() { return (float)fxR.NextDouble(); }
        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Color Rgb(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f); }
        static readonly Vector3 Up = Vector3.up;
        static float Ease(float t) { t = Mathf.Clamp01(t); return 1 - Mathf.Pow(1 - t, 3); }
        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1 - t;
            return u * u * a + 2 * u * t * b + t * t * c;
        }
        static WaitForSeconds W(float s) { return new WaitForSeconds(s); }
        Camera FxCam()
        {
            if (Game.I != null && Game.I.Rig != null && Game.I.Rig.Cam != null) return Game.I.Rig.Cam;
            return Camera.main;
        }
        Vector3 SpPos(BUnit u, float y = 0)
        {
            var v = VisOf(u);
            var p = v != null ? v.transform.position : Tile(u.x, u.y);
            p.y += y;
            return p;
        }
        // 按帧推进：fn(t)，t 从 0 递增；最后保证 fn(1)，再调用 done
        IEnumerator SpAnim(float seconds, Action<float> fn, Action done = null)
        {
            float t = 0;
            seconds = Mathf.Max(0.0001f, seconds);
            while (t < 1)
            {
                fn(t);
                yield return null;
                t += Mathf.Min(0.1f, Time.deltaTime) / seconds;
            }
            fn(1);
            if (done != null) done();
        }
        // 同上但不补最后一帧（网页版 _loop）
        IEnumerator SpLoop(float seconds, Action<float> fn)
        {
            float t = 0;
            while (t < 1)
            {
                fn(t);
                yield return null;
                t += Mathf.Min(0.1f, Time.deltaTime) / seconds;
            }
        }
        // 不等待的子动画（网页版未 await 的 Promise）
        void SpRun(IEnumerator e) { if (this != null && isActiveAndEnabled) StartCoroutine(Co.Guard(e)); }
        void SpLater(float s, Action a) { SpRun(LaterCo(s, a)); }
        IEnumerator LaterCo(float s, Action a) { yield return new WaitForSeconds(s); a(); }

        // ---- 临时物体（特效结束时 SpFree；战场销毁时全部释放）
        sealed class SpObj { public GameObject go; public Mesh mesh; public Material mat; public MeshRenderer mr; }
        readonly List<SpObj> spObjs = new List<SpObj>();
        readonly List<GameObject> spUi = new List<GameObject>();      // 书法大字（标签层）
        SpObj SpMake(string name, Mesh mesh, bool ownMesh, Material mat, bool ownMat, int queue = -1, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (queue > 0 && ownMat) mat.renderQueue = queue;
            var o = new SpObj { go = go, mesh = ownMesh ? mesh : null, mat = ownMat ? mat : null, mr = mr };
            spObjs.Add(o);
            return o;
        }
        void SpFree(SpObj o)
        {
            if (o == null) return;
            spObjs.Remove(o);
            if (o.go != null) Destroy(o.go);
            if (o.mesh != null) Destroy(o.mesh);
            if (o.mat != null) Destroy(o.mat);
            o.go = null; o.mesh = null; o.mat = null;
        }

        // ---- 材质
        static Shader fxShader;
        static Material FxMat(int mode, Color col, bool additive, bool zAlways = false, bool cullBack = false)
        {
            if (fxShader == null) fxShader = Art.LoadShader("SpecialFx");
            var m = new Material(fxShader);
            col.a = 1;
            m.SetColor("_Color", col);
            m.SetFloat("_Mode", mode);
            m.SetFloat("_Opacity", 1);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZTest", (float)(zAlways ? CompareFunction.Always : CompareFunction.LessEqual));
            m.SetFloat("_Cull", (float)(cullBack ? CullMode.Back : CullMode.Off));
            return m;
        }
        static Material AddMat(Color col, Texture tex)
        {
            var m = Art.NewAdditive(col);
            m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            return m;
        }
        static Material AlphaMat(Color col, Texture tex) { return Art.NewUnlit(col, tex != null ? tex : Texture2D.whiteTexture); }
        static void SetAlpha(Material m, Color col, float a) { if (m == null) return; col.a = a; m.color = col; }

        // ======================================================== 粒子 --
        sealed class SpPool
        {
            struct P { public Vector3 p, v; public float age, life, a, size, sizeEnd, grav, drag, fadeIn, ac; public Color c0, c1; }
            readonly int max;
            readonly List<P> parts = new List<P>();
            public readonly Mesh mesh; public readonly Material mat; readonly GameObject go;
            readonly List<Vector3> vs = new List<Vector3>(); readonly List<Color> cs = new List<Color>();
            readonly List<Vector2> us = new List<Vector2>(); readonly List<int> ts = new List<int>();
            bool drawnEmpty;
            public int Count { get { return parts.Count; } }
            public SpPool(Transform parent, string name, int max, bool additive, Texture tex, int queue)
            {
                this.max = max;
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
                if (max * 4 > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mat = additive ? AddMat(Color.white, tex != null ? tex : Art.SoftDot) : AlphaMat(Color.white, tex != null ? tex : Art.SoftDot);
                mat.renderQueue = queue;
                go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            public void Add(Vector3 p, Vector3 v, float life, Color c0, Color c1, float a, float size, float sizeEnd, float grav, float drag, float fadeIn, float ac)
            {
                if (parts.Count >= max) parts.RemoveAt(0);
                parts.Add(new P { p = p, v = v, age = 0, life = Mathf.Max(0.001f, life), c0 = c0, c1 = c1, a = a, size = size, sizeEnd = sizeEnd, grav = grav, drag = drag, fadeIn = fadeIn, ac = ac });
            }
            public void Simulate(float dt)
            {
                int n = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    p.age += dt;
                    if (p.age >= p.life) continue;
                    p.v.y -= p.grav * 9.81f * dt;
                    if (p.drag > 0) { float k = Mathf.Max(0, 1 - p.drag * dt); p.v *= k; }
                    p.p += p.v * dt;
                    parts[n++] = p;
                }
                if (n < parts.Count) parts.RemoveRange(n, parts.Count - n);
            }
            public void Draw(Camera cam)
            {
                if (parts.Count == 0)
                {
                    if (!drawnEmpty) { mesh.Clear(); drawnEmpty = true; }
                    return;
                }
                drawnEmpty = false;
                Vector3 right = cam != null ? cam.transform.right : Vector3.right, up = cam != null ? cam.transform.up : Vector3.up;
                vs.Clear(); cs.Clear(); us.Clear(); ts.Clear();
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    float t = p.age / p.life;
                    float fade = p.fadeIn > 0 ? Mathf.Min(1, t / p.fadeIn) : 1;
                    var c = Color.Lerp(p.c0, p.c1, t);
                    // 透明度 1 → 0（ac > 1 时前半段保持不透明，如火舌 a·(1−t²)）
                    c.a = p.a * (p.ac == 1 ? 1 - t : 1 - Mathf.Pow(t, p.ac)) * fade;
                    float h = p.size * Mathf.LerpUnclamped(1, p.sizeEnd, t) * 0.5f;
                    Vector3 r = right * h, u = up * h;
                    int b = vs.Count;
                    vs.Add(p.p - r - u); vs.Add(p.p - r + u); vs.Add(p.p + r + u); vs.Add(p.p + r - u);
                    cs.Add(c); cs.Add(c); cs.Add(c); cs.Add(c);
                    us.Add(new Vector2(0, 0)); us.Add(new Vector2(0, 1)); us.Add(new Vector2(1, 1)); us.Add(new Vector2(1, 0));
                    ts.Add(b); ts.Add(b + 1); ts.Add(b + 2); ts.Add(b); ts.Add(b + 2); ts.Add(b + 3);
                }
                mesh.Clear();
                mesh.SetVertices(vs); mesh.SetColors(cs); mesh.SetUVs(0, us); mesh.SetTriangles(ts, 0);
                mesh.RecalculateBounds();
            }
            public void Clear() { parts.Clear(); }
            public void Dispose()
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                if (mat != null) UnityEngine.Object.Destroy(mat);
            }
        }

        // 发射参数（网页版 _emit 的 o）
        sealed class EO
        {
            public Color color = Color.white; public Color? color2, colorEnd; public float mix = 0.35f;
            public float alpha = 1, life = 0.5f, speed, size = 0.25f, sizeEnd = 0.3f, gravity, radius, lifeVar, speedVar, sizeVar, drag, fadeIn, alphaCurve = 1;
            public Vector3? box; public float jitter = 0.25f;
            // 拖尾（_spTrail）专用
            public SpPool pool; public bool linear; public float head; public Color? headColor;
        }
        sealed class SpEmitter { public SpPool pool; public Vector3 pos; public float rate, acc, end; public EO o; public bool dead; }

        SpPool glow, flame, heat, dust, petals;
        readonly List<SpEmitter> emitters = new List<SpEmitter>();
        float spTime;
        void EnsurePools()
        {
            if (glow != null || root == null) return;
            dust = new SpPool(root, "FxDust", 900, false, DustTex, 3011);       // 烟尘
            heat = new SpPool(root, "FxHeat", 500, true, Art.SoftDot, 3012);
            flame = new SpPool(root, "FxFlame", 1400, false, FlameTex, 3013);
            glow = new SpPool(root, "FxGlow", 1200, true, Art.SoftDot, 3014);   // 火花、法术光点、火芯
        }
        SpPool PetalPool()
        {
            if (petals == null) petals = new SpPool(root, "FxPetals", 500, false, PetalTex, 3016);
            return petals;
        }
        static void InSphere(out Vector3 s, out float d)
        {
            for (;;)
            {
                float x = Rnd() * 2 - 1, y = Rnd() * 2 - 1, z = Rnd() * 2 - 1;
                float q = x * x + y * y + z * z;
                if (q <= 1 && q > 1e-6f) { s = new Vector3(x, y, z); d = Mathf.Sqrt(q); return; }
            }
        }
        void Emit(SpPool pool, Vector3 pos, int n, EO o)
        {
            if (pool == null) { EnsurePools(); pool = glow; }
            for (int i = 0; i < n; i++)
            {
                Vector3 p, v;
                if (o.box.HasValue)
                {
                    var bx = o.box.Value;
                    p = pos + new Vector3((Rnd() - 0.5f) * bx.x, (Rnd() - 0.5f) * bx.y, (Rnd() - 0.5f) * bx.z);
                    float sp = o.speed * (0.55f + Rnd() * 0.6f);
                    v = new Vector3((Rnd() - 0.5f) * 2 * o.jitter, sp, (Rnd() - 0.5f) * 2 * o.jitter);
                }
                else
                {
                    Vector3 s; float d;
                    InSphere(out s, out d);
                    p = pos + s * o.radius;
                    float sp = o.speed * (o.speedVar > 0 ? 1 - o.speedVar * Rnd() : 1);
                    v = s / d * sp;
                }
                var c = o.color;
                if (o.color2.HasValue && Rnd() < o.mix) c = o.color2.Value;
                var ce = o.colorEnd.HasValue ? o.colorEnd.Value : c;
                pool.Add(p, v, o.life * (o.lifeVar > 0 ? 1 - o.lifeVar * Rnd() : 1), c, ce, o.alpha,
                    o.size * (o.sizeVar > 0 ? 1 - o.sizeVar * Rnd() : 1), o.sizeEnd, o.gravity, o.drag, o.fadeIn, o.alphaCurve);
            }
        }
        SpEmitter Emitter(SpPool pool, Vector3 pos, float rate, EO o, float duration)
        {
            var e = new SpEmitter { pool = pool, pos = pos, rate = rate, o = o, end = spTime + duration };
            emitters.Add(e);
            return e;
        }

        // ---- 光源（借用两盏空闲的点光源；没有空闲的就不闪）
        sealed class SpLightSlot { public Light l; public bool busy; public float baseI, cur, seed; public int token; }
        readonly List<SpLightSlot> spLights = new List<SpLightSlot>();
        void SpLight(Vector3 pos, Color col, float peak, float dur = 0.3f)
        {
            if (spLights.Count == 0)
                for (int i = 0; i < 2; i++)
                {
                    var l = new GameObject("FxLight").AddComponent<Light>();
                    l.transform.SetParent(root, false);
                    l.type = LightType.Point; l.range = 6; l.intensity = 0; l.shadows = LightShadows.None; l.enabled = false;
                    spLights.Add(new SpLightSlot { l = l, seed = i * 17.3f });
                }
            var s = spLights.FirstOrDefault(x => !x.busy);
            if (s == null) return;
            s.busy = true;
            int token = ++s.token;
            s.l.color = col;
            s.l.transform.position = pos;
            s.baseI = peak; s.cur = peak;
            SpLater(dur, () => { if (s.token == token) s.busy = false; });
        }

        // ---- 镜头震动（叠加到相机位置上；CameraRig 每帧重新摆位时自然复原）
        float shkAmp, shkDur, shkT; bool shkOn, shkHas; Vector3 shkLastPos, shkLastOff;
        void SpShake(float amp, float dur)
        {
            if (!shkOn || shkAmp * (1 - shkT / shkDur) < amp) { shkAmp = amp; shkDur = dur; shkT = 0; shkOn = true; }
        }

        // ---- 全屏闪光与压暗（标签层最底下）
        Image flashImg, dimImg;
        void SpFlash(Color col, float alpha, float dur = 0.3f)
        {
            if (UIKit.LabelLayer == null) return;
            if (flashImg == null) flashImg = SpOverlay("FxFlash", null);
            col.a = 0; flashImg.color = col;
            var img = flashImg;
            SpRun(SpAnim(dur, t => { if (img != null) { var c = img.color; float e = 1 - (1 - t) * (1 - t); c.a = alpha * (1 - e); img.color = c; } }));
        }
        void SpDim(float alpha, float hold = 1)
        {
            if (UIKit.LabelLayer == null) return;
            if (dimImg == null) dimImg = SpOverlay("FxDim", DimSprite);
            var img = dimImg;
            SpRun(SpAnim(hold, t =>
            {
                if (img == null) return;
                float a = t < 0.15f ? alpha * Mathf.SmoothStep(0, 1, t / 0.15f) : t < 0.8f ? alpha : alpha * (1 - Mathf.SmoothStep(0, 1, (t - 0.8f) / 0.2f));
                img.color = new Color(1, 1, 1, a);
            }));
        }
        Image SpOverlay(string name, Sprite sp)
        {
            var im = UIKit.Img(UIKit.LabelLayer, sp, new Color(1, 1, 1, 0), name);
            im.raycastTarget = false;
            UIKit.Stretch(im.rectTransform);
            im.rectTransform.SetAsFirstSibling();
            return im;
        }

        // ---- 书法大字：墨色描边 + 招式色字芯 + 外层辉光；目标靠近画面上沿时整体下移，保持在画面内
        void SpGlyph(Vector3 pos, string ch, Color col, float size = 3.4f)
        {
            if (UIKit.LabelLayer == null || string.IsNullOrEmpty(ch)) return;
            var font = UIKit.Title != null ? UIKit.Title : UIKit.Body;
            if (font == null) return;
            float s = size * 0.56f;
            pos.y += 0.9f;
            var rootRt = UIKit.NewRect("FxGlyph", UIKit.LabelLayer);
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(160, 160);
            var cg = rootRt.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
            spUi.Add(rootRt.gameObject);
            Func<string, Color, Text> mk = (nm, c) =>
            {
                var rt = UIKit.NewRect(nm, rootRt);
                UIKit.Stretch(rt);
                var t = rt.gameObject.AddComponent<Text>();
                t.font = font; t.fontSize = 100; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter; t.text = ch; t.color = c;
                t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
                return t;
            };
            var glowC = col; glowC.a = 0.55f;
            var glowT = mk("Glow", glowC);
            glowT.rectTransform.localScale = Vector3.one * 1.15f;
            var go1 = glowT.gameObject.AddComponent<Outline>(); go1.effectColor = new Color(col.r, col.g, col.b, 0.5f); go1.effectDistance = new Vector2(6, -6);
            var go2 = glowT.gameObject.AddComponent<Outline>(); go2.effectColor = new Color(col.r, col.g, col.b, 0.3f); go2.effectDistance = new Vector2(10, 10);
            var ink = mk("Ink", Art.Shade(col, 0.35f));
            var o1 = ink.gameObject.AddComponent<Outline>(); o1.effectColor = new Color(20 / 255f, 10 / 255f, 6 / 255f, 0.92f); o1.effectDistance = new Vector2(4, -4);
            var o2 = ink.gameObject.AddComponent<Outline>(); o2.effectColor = new Color(20 / 255f, 10 / 255f, 6 / 255f, 0.92f); o2.effectDistance = new Vector2(-4, 4);
            float minY = pos.y - 2.3f, drop = 0;
            var inkC = ink.color;
            SpRun(SpAnim(1.0f, t =>
            {
                if (rootRt == null) return;
                var cam = FxCam();
                float pop = t < 0.14f ? 1.5f - 0.5f * Ease(t / 0.14f) : 1 + (t - 0.14f) * 0.12f;
                float a = t < 0.6f ? 1 : 1 - (t - 0.6f) / 0.4f;
                float y0 = pos.y + t * 0.5f;
                drop = Mathf.Min(drop + GlyphFit(cam, pos, y0 - drop, s * pop), y0 - minY);
                var wp = new Vector3(pos.x, y0 - drop, pos.z);
                ink.color = new Color(inkC.r, inkC.g, inkC.b, a * 0.95f);
                glowT.color = new Color(col.r, col.g, col.b, a * 0.55f * (t < 0.14f ? 1 : 0.6f));
                if (cam == null) { cg.alpha = 0; return; }
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0.1f) { cg.alpha = 0; return; }
                cg.alpha = 1;
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rootRt.parent, sp, null, out local);
                rootRt.anchoredPosition = local;
                // 世界尺寸 s·pop 的方片，字面约占 196/256 → 屏幕像素 → 画布单位
                float px = s * pop * 0.766f / (2 * sp.z * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2)) * Screen.height;
                float sf = UIKit.Canvas != null && UIKit.Canvas.scaleFactor > 0 ? UIKit.Canvas.scaleFactor : 1;
                rootRt.localScale = Vector3.one * (px / sf / 100f);
            }, () => { if (rootRt != null) { spUi.Remove(rootRt.gameObject); Destroy(rootRt.gameObject); } }));
        }
        // 字的上沿投影超出 NDC y≈0.86 时需要下移的世界高度（只下移不回弹）
        static float GlyphFit(Camera cam, Vector3 pos, float y, float sc)
        {
            if (cam == null || cam.orthographic) return 0;
            var up = cam.transform.up; var fwd = cam.transform.forward;
            var tmp = new Vector3(pos.x, y, pos.z) + up * (sc * 0.42f);
            float depth = Vector3.Dot(tmp - cam.transform.position, fwd);
            if (depth <= 0.1f) return 0;
            float over = cam.WorldToViewportPoint(tmp).y * 2 - 1 - 0.86f;
            if (over <= 0 || up.y < 0.2f) return 0;
            return over * depth * Mathf.Tan(cam.fieldOfView * Mathf.PI / 360) / up.y;
        }

        // ---- 地面光环：r0 → r1 扩散并淡出
        sealed class RO { public float alpha = 1, spin, y = 0.12f; public bool hold, normal, additive, vertical; public Texture tex; }
        void SpRing(Vector3 pos, Color col, float r0, float r1, float dur = 0.5f, RO o = null)
        {
            o = o ?? new RO();
            Material mat;
            if (o.additive) mat = AddMat(col, Art.SoftDot);
            else if (o.normal) mat = AlphaMat(col, o.tex != null ? o.tex : Art.Ring);
            else mat = AddMat(col, o.tex != null ? o.tex : Art.Ring);
            var ob = SpMake("FxRing", UnitDisc, false, mat, true, 3006);
            var tr = ob.go.transform;
            tr.position = pos + Up * o.y;
            var baseRot = o.vertical ? Quaternion.Euler(-(90 - 0.85f * Mathf.Rad2Deg), 0, 0) : Quaternion.identity;
            tr.rotation = baseRot;
            SpRun(SpAnim(dur, t =>
            {
                if (ob.go == null) return;
                float k = Ease(t), r = r0 + (r1 - r0) * k;
                tr.localScale = new Vector3(r, 1, r);
                if (o.spin != 0) tr.rotation = baseRot * Quaternion.Euler(0, -t * o.spin * Mathf.Rad2Deg, 0);
                SetAlpha(mat, col, o.alpha * (1 - Mathf.Pow(t, o.hold ? 3 : 1.4f)));
            }, () => SpFree(ob)));
        }

        // ---- 斩击弧光：在 pos 处、绕镜头方向的平面内扫过（tilt 为弧面倾角，弧度）
        sealed class AO { public float radius = 1.4f, width = 0.5f, start = -0.4f, sweep = 2.6f, tilt, lean, mid, dur = 0.42f, len = 0.75f, grow; public bool flat, stay, glowOnly; }
        static Mesh ArcMesh(float radius, float width, float start, float sweep, int n)
        {
            var vs = new Vector3[(n + 1) * 2]; var uv = new Vector2[(n + 1) * 2]; var tri = new int[n * 6];
            float r0 = radius - width * 0.5f, r1 = radius + width * 0.5f;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n, a = start + sweep * t, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                vs[i * 2] = new Vector3(ca * r0, sa * r0, 0); vs[i * 2 + 1] = new Vector3(ca * r1, sa * r1, 0);
                uv[i * 2] = new Vector2(t, 0); uv[i * 2 + 1] = new Vector2(t, 1);
                if (i < n) { int k = i * 2, j = i * 6; tri[j] = k; tri[j + 1] = k + 1; tri[j + 2] = k + 2; tri[j + 3] = k + 1; tri[j + 4] = k + 3; tri[j + 5] = k + 2; }
            }
            var m = new Mesh { name = "fxArc" };
            m.vertices = vs; m.uv = uv; m.triangles = tri; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
        void SpArc(Vector3 pos, Color col, AO o)
        {
            // 实色刀光（普通混合）+ 外层：深色墨晕（普通混合，明亮的草地上也能衬出刀光）；glowOnly 时改为叠加辉光
            var mat = FxMat(0, col, o.glowOnly, true);
            mat.SetFloat("_Len", o.len); mat.SetFloat("_Soft", 0);
            var gmat = o.glowOnly ? FxMat(0, Art.Shade(col, 0.25f), true, true) : FxMat(0, Art.Shade(col, -0.62f), false, true);
            gmat.SetFloat("_Len", o.len); gmat.SetFloat("_Soft", 1);
            var gm = SpMake("FxArcGlow", ArcMesh(o.radius + o.width * 0.3f, o.width * 1.9f, o.start, o.sweep, 48), true, gmat, true, 3032);
            var m = SpMake("FxArc", ArcMesh(o.radius, o.width, o.start, o.sweep, 48), true, mat, true, 3033);
            Quaternion q;
            if (o.flat) q = Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, 0, o.tilt * Mathf.Rad2Deg);
            else
            {
                var cam = FxCam();
                var fwd = cam != null ? pos - cam.transform.position : Vector3.forward;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                q = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0, 0, o.tilt * Mathf.Rad2Deg);
                if (o.lean != 0) q = q * Quaternion.Euler(-o.lean * Mathf.Rad2Deg, 0, 0);
            }
            var p = pos;
            // mid：把弧心挪开，使半径为 mid 的弧的中点正好落在 pos 上（同一 mid 的几道弧同心 → 平行的爪痕）
            if (o.mid != 0) { float ma = o.start + o.sweep / 2; p -= q * new Vector3(Mathf.Cos(ma) * o.mid, Mathf.Sin(ma) * o.mid, 0); }
            foreach (var x in new[] { m, gm }) { x.go.transform.position = p; x.go.transform.rotation = q; }
            float draw = o.stay ? 0.3f : 0.55f;
            SpRun(SpAnim(o.dur, t =>
            {
                if (m.go == null) return;
                float head = o.stay ? Mathf.Min(1, t / draw) * Mathf.Max(1, o.len) : Mathf.Min(1, t / draw) * (1 + o.len);
                mat.SetFloat("_Head", head); gmat.SetFloat("_Head", head);
                float a = t < draw ? 1 : 1 - Mathf.Pow((t - draw) / (1 - draw), o.stay ? 2 : 1);
                mat.SetFloat("_Opacity", a);
                gmat.SetFloat("_Opacity", a * (o.glowOnly ? 0.55f : 0.42f));
                if (o.grow != 0) { float s = 1 + t * o.grow; m.go.transform.localScale = gm.go.transform.localScale = new Vector3(s, s, s); }
            }, () => { SpFree(m); SpFree(gm); }));
        }

        // ---- 沿折线的带子，朝向镜头（uv.x 沿线，uv.y 横向）
        void SetRibbon(Mesh mesh, List<Vector3> points, float width, Func<float, float> taper)
        {
            var cam = FxCam();
            var camPos = cam != null ? cam.transform.position : new Vector3(0, 40, -40);
            int n = points.Count;
            var vs = new Vector3[n * 2]; var uv = new Vector2[n * 2]; var tri = new int[Mathf.Max(0, n - 1) * 6];
            float len = 0; var L = new float[n];
            for (int i = 1; i < n; i++) { len += Vector3.Distance(points[i], points[i - 1]); L[i] = len; }
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                var tan = (points[Mathf.Min(n - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                var view = (camPos - p).normalized;
                float w = width * 0.5f * (taper != null ? taper(n > 1 ? (float)i / (n - 1) : 1) : 1);
                var side = Vector3.Cross(tan, view).normalized * w;
                vs[i * 2] = p - side; vs[i * 2 + 1] = p + side;
                float t = len > 0 ? L[i] / len : 0;
                uv[i * 2] = new Vector2(t, 0); uv[i * 2 + 1] = new Vector2(t, 1);
                if (i < n - 1) { int k = i * 2, j = i * 6; tri[j] = k; tri[j + 1] = k + 1; tri[j + 2] = k + 2; tri[j + 3] = k + 1; tri[j + 4] = k + 3; tri[j + 5] = k + 2; }
            }
            mesh.Clear();
            mesh.vertices = vs; mesh.uv = uv; mesh.triangles = tri;
            mesh.RecalculateBounds();
        }
        SpObj SpRibbonObj(Material mat, int queue)
        {
            var mesh = new Mesh { name = "fxRibbon" };
            mesh.MarkDynamic();
            return SpMake("FxRibbon", mesh, true, mat, true, queue);
        }

        // ---- 闪电：从 a 到 b 的折线（不时重新生成形状）
        void SpBolt(Vector3 a, Vector3 b, Color col, float dur = 0.32f, float width = 0.55f)
        {
            var mat = FxMat(1, col, true);
            var ob = SpRibbonObj(mat, 3026);
            var pts = new List<Vector3>(14);
            Action mk = () =>
            {
                pts.Clear(); pts.Add(a);
                const int n = 12;
                var dir = b - a;
                var perp = new Vector3(-dir.z, 0, dir.x).normalized;
                for (int i = 1; i < n; i++)
                {
                    float t = (float)i / n;
                    var p = Vector3.Lerp(a, b, t);
                    float j = (1 - Mathf.Abs(t - 0.5f) * 1.2f) * 0.9f;
                    p += perp * ((Rnd() - 0.5f) * j);
                    p.x += (Rnd() - 0.5f) * 0.25f * j; p.z += (Rnd() - 0.5f) * 0.25f * j;
                    pts.Add(p);
                }
                pts.Add(b);
                if (ob.mesh != null) SetRibbon(ob.mesh, pts, width, null);
            };
            mk();
            float lastSwap = 0;
            SpRun(SpAnim(dur, t =>
            {
                if (ob.go == null) return;
                if (t - lastSwap > 0.18f) { lastSwap = t; mk(); }
                mat.SetFloat("_Opacity", (t < 0.15f ? 1.4f : 1 - (t - 0.15f) / 0.85f) * (0.75f + Rnd() * 0.5f));
            }, () => SpFree(ob)));
        }

        // ---- 光柱
        static Mesh PillarMesh(float r, float h)
        {
            const int seg = 28;
            var vs = new Vector3[(seg + 1) * 2]; var uv = new Vector2[(seg + 1) * 2]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg, a = u * Mathf.PI * 2, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                vs[i * 2] = new Vector3(ca * r * 1.15f, 0, sa * r * 1.15f); vs[i * 2 + 1] = new Vector3(ca * r, h, sa * r);
                uv[i * 2] = new Vector2(u, 0); uv[i * 2 + 1] = new Vector2(u, 1);
                if (i < seg) { int k = i * 2, j = i * 6; tri[j] = k; tri[j + 1] = k + 1; tri[j + 2] = k + 2; tri[j + 3] = k + 1; tri[j + 4] = k + 3; tri[j + 5] = k + 2; }
            }
            var m = new Mesh { name = "fxPillar" };
            m.vertices = vs; m.uv = uv; m.triangles = tri; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
        void SpPillar(Vector3 pos, Color col, float r, float h, float dur = 1, float alpha = 1)
        {
            var mat = FxMat(2, col, true);
            var ob = SpMake("FxPillar", PillarMesh(r, h), true, mat, true, 3022);
            ob.go.transform.position = pos;
            ob.go.transform.localScale = new Vector3(0.4f, 0.0001f, 0.4f);
            SpRun(SpAnim(dur, t =>
            {
                if (ob.go == null) return;
                mat.SetFloat("_FxTime", t * dur);
                float k = t < 0.2f ? Ease(t / 0.2f) : 1;
                float s = 0.4f + 0.6f * k + t * 0.15f;
                ob.go.transform.localScale = new Vector3(s, Mathf.Max(0.0001f, k), s);
                mat.SetFloat("_Opacity", alpha * (t < 0.65f ? 1 : 1 - (t - 0.65f) / 0.35f));
            }, () => SpFree(ob)));
        }

        // ---- 残影：在部队位置（偏移 off）放一具半透明的同形网格，life 秒内淡出（共享部队网格）
        void SpGhost(UnitVisual v, Color col, float life = 0.4f, float alpha = 0.5f) { SpGhostAt(v, col, life, alpha, Vector3.zero); }
        void SpGhostAt(UnitVisual v, Color col, float life, float alpha, Vector3 off)
        {
            if (v == null || v.SharedMesh == null) return;
            var mat = AddMat(col, null);
            SetAlpha(mat, col, alpha);
            var ob = SpMake("FxGhost", v.SharedMesh, false, mat, true, 3009);
            ob.go.transform.position = v.transform.position + off;
            ob.go.transform.rotation = v.transform.rotation;
            SpRun(SpAnim(life, t => SetAlpha(mat, col, alpha * (1 - t)), () => SpFree(ob)));
        }

        // ---- 沿路径移动的发射头：path(t) → 位置；每帧在头部喷粒子
        IEnumerator SpTrail(Func<float, Vector3> path, float dur, int perFrame, EO o)
        {
            EnsurePools();
            var pool = o.pool ?? glow;
            return SpAnim(dur, t =>
            {
                var p = path(o.linear ? t : Ease(t));
                Emit(pool, p, perFrame, o);
                if (o.head > 0) Emit(glow, p, 1, new EO { color = o.headColor ?? o.color, alpha = 0.9f, life = 0.12f, speed = 0, size = o.head, sizeEnd = 0.6f, gravity = 0, radius = 0.02f });
            });
        }

        // ---- 地面范围格子发光
        void SpTiles(List<Vector2Int> tiles, Color col, float dur = 1, float alpha = 0.7f)
        {
            if (tiles == null || tiles.Count == 0) return;
            var mat = AddMat(col, TileTex);
            mat.renderQueue = 3005;
            ownMats.Add(mat);
            SetAlpha(mat, col, 0);
            var ms = new List<SpObj>();
            foreach (var t in tiles)
            {
                if (!M.InBounds(t.x, t.y)) continue;
                var ob = SpMake("FxTile", UnitDisc, false, mat, false);
                ob.go.transform.position = Tile(t.x, t.y) + Up * 0.09f;
                ms.Add(ob);
            }
            SpRun(SpAnim(dur, t => SetAlpha(mat, col, alpha * (t < 0.15f ? t / 0.15f : t > 0.7f ? (1 - t) / 0.3f : 1)),
                () => { foreach (var m in ms) SpFree(m); Destroy(mat); }));
        }
        List<Vector2Int> SpAreaTiles(SpCtx c, int r)
        {
            if (c.res != null && c.res.area != null && c.res.area.Count > 0) return c.res.area;
            var t = c.target; var o = new List<Vector2Int>();
            for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++)
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) <= r && M.InBounds(t.x + dx, t.y + dy)) o.Add(new Vector2Int(t.x + dx, t.y + dy));
            return o;
        }

        void SpSparks(Vector3 pos, Color col, int n = 24, float speed = 4, float size = 0.2f)
        {
            EnsurePools();
            Emit(glow, pos, n, new EO { color = col, color2 = Color.white, mix = 0.35f, colorEnd = Art.Shade(col, -0.2f), alpha = 1, life = 0.6f, speed = speed, size = size, sizeEnd = 0.2f, gravity = 0.7f, radius = 0.25f, lifeVar = 0.4f, speedVar = 0.5f });
            Emit(glow, pos, 2, new EO { color = col, alpha = 0.9f, life = 0.22f, speed = 0, size = 2.2f, sizeEnd = 0.4f, gravity = 0, radius = 0.05f });
        }
        void SpDust(Vector3 pos, int n = 12, float speed = 2.2f, Color? col = null)
        {
            EnsurePools();
            Emit(dust, pos, n, new EO { color = col ?? C(0.62f, 0.56f, 0.45f), alpha = 0.5f, life = 0.9f, speed = speed, size = 0.7f, sizeEnd = 2.2f, gravity = 0.05f, radius = 0.4f, lifeVar = 0.3f, drag = 2.2f, fadeIn = 0.05f });
        }
        void SpDustPuff(Vector3 pos, float k)
        {
            EnsurePools();
            Emit(dust, pos, 1, new EO { color = C(0.62f, 0.56f, 0.45f), alpha = 0.35f * k, life = 0.7f, speed = 0.35f, size = 0.55f, sizeEnd = 1.6f, gravity = -0.02f, radius = 0.45f, lifeVar = 0.3f, drag = 1.2f, fadeIn = 0.1f });
        }
        void SpRockImpact(Vector3 pos)
        {
            EnsurePools();
            Emit(dust, pos, 6, new EO { color = C(0.58f, 0.52f, 0.44f), alpha = 0.5f, life = 0.8f, speed = 1.6f, size = 0.5f, sizeEnd = 1.5f, gravity = 0.15f, radius = 0.25f, lifeVar = 0.3f, drag = 2.0f });
            Emit(glow, pos, 4, new EO { color = C(1, 0.85f, 0.6f), alpha = 0.7f, life = 0.3f, speed = 2.4f, size = 0.14f, gravity = 0.8f, radius = 0.1f });
        }

        // 受击的通用表现：闪白、火花、轻微震动
        void SpImpact(BUnit x, Color col, bool big)
        {
            var v = VisOf(x);
            if (v != null) v.FlashHit();
            SpSparks(SpPos(x, 0.7f), col, big ? 34 : 20, big ? 5 : 3.5f, big ? 0.26f : 0.2f);
            if (big) SpDust(SpPos(x, 0.2f), 10, 2.6f);
        }
        // 施展者的蓄势：脚下光环收缩 + 光点向内聚集
        IEnumerator SpCharge(SpCtx c, float dur = 0.3f)
        {
            EnsurePools();
            var p = SpPos(c.u);
            if (c.v != null) c.v.Tint(c.color, 0.9f);
            SpRing(p, c.color, 2.4f, 0.6f, dur, new RO { alpha = 0.9f });
            float sp = 1 / dur;
            for (int i = 0; i < 18; i++)
            {
                float a = Rnd() * Mathf.PI * 2, r = 1.6f + Rnd() * 0.8f;
                glow.Add(new Vector3(p.x + Mathf.Cos(a) * r, p.y + 0.3f + Rnd() * 1.2f, p.z + Mathf.Sin(a) * r),
                    new Vector3(-Mathf.Cos(a) * r * sp, 0.2f, -Mathf.Sin(a) * r * sp), dur, c.color, Color.white, 1, 0.22f, 0.5f, 0, 0, 0.2f, 1);
            }
            yield return W(dur);
        }
        // 冲向目标再回位
        IEnumerator SpLunge(SpCtx c, float k = 0.45f, float dur = 0.16f)
        {
            var v = c.v;
            if (v == null || c.target == null || c.target == c.u) yield break;
            var from = v.transform.position; var to = SpPos(c.target);
            v.Face(to - from);
            yield return SpAnim(dur, t => v.transform.position = Vector3.Lerp(from, to, Ease(t) * k));
            SpRun(SpAnim(0.22f, t => v.transform.position = Vector3.Lerp(from, to, (1 - t) * k), () => { if (v != null) v.transform.position = from; }));
        }

        // ======================================================== 每帧 --
        float burnCheck;
        readonly Dictionary<int, SpEmitter[]> burnFx = new Dictionary<int, SpEmitter[]>();
        readonly List<int> burnGone = new List<int>();
        void SpUpdate(float dt)
        {
            spTime += dt;
            burnCheck -= dt;
            if (burnCheck <= 0) { burnCheck = 0.2f; SpSyncBurning(); }
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                var e = emitters[i];
                if (e.dead || spTime >= e.end) { emitters.RemoveAt(i); continue; }
                e.acc += e.rate * dt;
                int n = Mathf.FloorToInt(e.acc);
                if (n > 0) { e.acc -= n; Emit(e.pool, e.pos, n, e.o); }
            }
            if (glow != null) { glow.Simulate(dt); flame.Simulate(dt); heat.Simulate(dt); dust.Simulate(dt); }
            if (petals != null) petals.Simulate(dt);
            float time = Time.time;
            foreach (var s in spLights)
            {
                float target = s.busy ? s.baseI : 0;
                s.cur += (target - s.cur) * Mathf.Min(1, dt * (s.busy ? 12 : 3));
                float flicker = 1 + Mathf.Sin(time * 23 + s.seed) * 0.12f + Mathf.Sin(time * 37.7f + s.seed * 2) * 0.08f;
                // 网页版 three 的强度（decay 1）→ Unity：约 ×0.75（火光 4 ≈ C# 3）
                s.l.intensity = Mathf.Max(0, s.cur * flicker) * 0.75f;
                s.l.enabled = s.l.intensity > 0.01f;
            }
        }
        // 燃烧中的格子：持续的小火苗与烟（model.burning > 0）
        void SpSyncBurning()
        {
            if (M == null || M.burning == null) return;
            for (int x = 0; x < M.W; x++)
                for (int y = 0; y < M.H; y++)
                {
                    if (M.burning[x, y] <= 0) continue;
                    int key = x * 1000 + y;
                    if (burnFx.ContainsKey(key)) continue;
                    EnsurePools();
                    var p = Tile(x, y);
                    var fl = Emitter(flame, p + Up * 0.15f, 16, new EO
                    {
                        color = C(1, 0.66f, 0.2f), colorEnd = C(0.7f, 0.14f, 0.04f), alpha = 0.9f, life = 0.75f, speed = 1.1f, size = 0.55f, sizeEnd = 0.35f, gravity = -0.2f,
                        box = new Vector3(1.5f, 0.1f, 1.5f), jitter = 0.15f, lifeVar = 0.4f, sizeVar = 0.4f, alphaCurve = 2,
                    }, 1e9f);
                    var sm = Emitter(dust, p + Up * 0.9f, 3, new EO
                    {
                        color = C(0.24f, 0.22f, 0.2f), alpha = 0.32f, life = 2.2f, speed = 0.7f, size = 0.9f, sizeEnd = 2.2f, gravity = -0.02f,
                        box = new Vector3(1.2f, 0.2f, 1.2f), jitter = 0.2f, lifeVar = 0.3f, drag = 0.4f, fadeIn = 0.15f,
                    }, 1e9f);
                    burnFx[key] = new[] { fl, sm };
                }
            burnGone.Clear();
            foreach (var kv in burnFx)
            {
                int x = kv.Key / 1000, y = kv.Key % 1000;
                if (!M.InBounds(x, y) || M.burning[x, y] <= 0) { foreach (var e in kv.Value) e.dead = true; burnGone.Add(kv.Key); }
            }
            foreach (var k in burnGone) burnFx.Remove(k);
        }
        void LateUpdate()
        {
            var cam = FxCam();
            if (cam != null)
            {
                var ct = cam.transform;
                // 上一帧的震动偏移若没被相机控制器覆盖，先撤回，避免累积
                if (shkHas && ct.position == shkLastPos) ct.position -= shkLastOff;
                shkHas = false;
                if (shkOn)
                {
                    shkT += Time.deltaTime;
                    if (shkT >= shkDur) shkOn = false;
                    else
                    {
                        float k = shkAmp * Mathf.Pow(1 - shkT / shkDur, 2), tt = spTime;
                        var off = new Vector3(Mathf.Sin(tt * 61) * k, Mathf.Sin(tt * 47 + 1.3f) * k * 0.6f, Mathf.Cos(tt * 53) * k);
                        ct.position += off;
                        shkLastPos = ct.position; shkLastOff = off; shkHas = true;
                    }
                }
            }
            if (glow != null) { dust.Draw(cam); heat.Draw(cam); flame.Draw(cam); glow.Draw(cam); }
            if (petals != null) petals.Draw(cam);
        }
        void SpDispose()
        {
            var cam = FxCam();
            if (cam != null && shkHas && cam.transform.position == shkLastPos) cam.transform.position -= shkLastOff;
            shkHas = shkOn = false;
            foreach (var o in spObjs.ToArray()) SpFree(o);
            spObjs.Clear();
            foreach (var g in spUi) if (g != null) Destroy(g);
            spUi.Clear();
            emitters.Clear(); burnFx.Clear();
            if (glow != null) { glow.Dispose(); flame.Dispose(); heat.Dispose(); dust.Dispose(); glow = flame = heat = dust = null; }
            if (petals != null) { petals.Dispose(); petals = null; }
            if (flashImg != null) Destroy(flashImg.gameObject);
            if (dimImg != null) Destroy(dimImg.gameObject);
            flashImg = dimImg = null;
            SpLabelsDim = false;
        }

        // ======================================================== 风格 --
        // ---- slash 斩击弧光（单体重击）：两道交叉的巨大刀痕（十字斩）留在敌阵上，随后崩散
        IEnumerator Sp_slash(SpCtx c)
        {
            var t = c.target;
            yield return SpCharge(c, 0.22f);
            yield return SpLunge(c, 0.5f, 0.14f);
            var p = SpPos(t, 0.9f);
            float R = 2.3f, sw = 1.25f, st = Mathf.PI / 2 - sw / 2;
            Sfx.Play("hit", 0.9f);
            SpArc(p, c.color, new AO { radius = R, width = 0.6f, start = st, sweep = sw, tilt = -0.85f, mid = R, dur = 0.62f, len = 1, stay = true });
            SpArc(p, Color.white, new AO { radius = R + 0.1f, width = 0.16f, start = st, sweep = sw, tilt = -0.85f, mid = R, dur = 0.45f, len = 1, stay = true });
            SpImpact(t, c.color, false);
            yield return W(0.12f);
            Sfx.Play("hit", 1);
            SpArc(p, c.color, new AO { radius = R, width = 0.6f, start = st, sweep = sw, tilt = 0.85f, mid = R, dur = 0.6f, len = 1, stay = true });
            SpArc(p, Color.white, new AO { radius = R + 0.1f, width = 0.16f, start = st, sweep = sw, tilt = 0.85f, mid = R, dur = 0.43f, len = 1, stay = true });
            yield return W(0.06f);
            SpImpact(t, c.color, true);
            SpRing(SpPos(t), c.color, 0.4f, 2.8f, 0.5f);
            SpRing(SpPos(t), Color.white, 0.3f, 1.6f, 0.3f);
            SpLight(p, c.color, 7, 0.3f);
            SpShake(0.26f, 0.32f);
            SpFlash(Color.white, 0.2f, 0.18f);
            c.HitAll();
            // 刀痕崩散成碎光
            Emit(glow, p, 26, new EO { color = c.color, color2 = Color.white, mix = 0.5f, alpha = 1, life = 0.5f, speed = 4.5f, size = 0.18f, sizeEnd = 0.05f, gravity = 0.4f, radius = 0.6f, speedVar = 0.5f });
            SpGlyph(SpPos(t, 3.0f), c.glyph, c.color);
            yield return W(0.55f);
        }

        // ---- dragon 青龙：盘旋的龙气自施展者升起扑向敌军，再一记巨大的弧光
        IEnumerator Sp_dragon(SpCtx c)
        {
            EnsurePools();
            var t = c.target; var col = c.color;
            Vector3 a = SpPos(c.u, 0.4f), b = SpPos(t, 1.0f);
            if (c.v != null) c.v.Tint(col, 1);
            SpRing(SpPos(c.u), col, 0.5f, 2.4f, 0.5f);
            SpPillar(SpPos(c.u), col, 0.7f, 4.5f, 0.9f, 0.55f);
            Sfx.Play("magic", 0.5f);
            // 龙气：先绕施展者盘旋升空（一圈半），再自高空俯冲扑向敌军
            var top = a + new Vector3(-0.9f, 3.4f, 0);
            var ctrl = Vector3.Lerp(top, b, 0.5f); ctrl.y += 2.4f;
            Func<float, Vector3> path = k =>
            {
                if (k < 0.5f)
                {
                    float q = k / 0.5f, ang = q * Mathf.PI * 3, r = 0.95f * (1 - q * 0.15f);
                    return a + new Vector3(Mathf.Cos(ang) * r, 0.2f + q * 3.2f, -Mathf.Sin(ang) * r);
                }
                float qq = (k - 0.5f) / 0.5f;
                return Bez(top, ctrl, b, Ease(qq) * 0.85f + qq * 0.15f);
            };
            // 龙身：沿路径生长的发光长带（头粗尾细），身后洒落龙鳞光点
            Func<float, float> taper = q => 0.2f + 0.8f * Mathf.Pow(q, 0.7f);
            var bodyMat = FxMat(1, col, false); var glowMat = FxMat(1, C(0.85f, 1, 0.92f), true);
            var body = SpRibbonObj(bodyMat, 3024); var glowBody = SpRibbonObj(glowMat, 3025);
            var pts = new List<Vector3>(37);
            yield return SpAnim(0.7f, k =>
            {
                if (body.mesh == null) return;
                float head = k, tail = Mathf.Max(0, head - 0.42f);
                pts.Clear();
                for (int i = 0; i <= 36; i++) pts.Add(path(tail + (head - tail) * i / 36f));
                SetRibbon(body.mesh, pts, 1.1f, taper); SetRibbon(glowBody.mesh, pts, 0.4f, taper);
                bodyMat.SetFloat("_Opacity", 1.1f); glowMat.SetFloat("_Opacity", 0.95f);
                var hp = path(head);
                Emit(glow, hp, 5, new EO { color = col, color2 = C(0.85f, 1, 0.9f), mix = 0.3f, colorEnd = Art.Shade(col, -0.3f), alpha = 1, life = 0.55f, speed = 1.0f, size = 0.4f, sizeEnd = 0.12f, gravity = 0.25f, radius = 0.3f, lifeVar = 0.3f });
                Emit(glow, hp, 1, new EO { color = C(0.9f, 1, 0.95f), alpha = 1, life = 0.12f, speed = 0, size = 2.4f, sizeEnd = 0.6f, gravity = 0, radius = 0.02f });
            });
            SpRun(SpAnim(0.3f, k => { bodyMat.SetFloat("_Opacity", 1.1f * (1 - k)); glowMat.SetFloat("_Opacity", 0.95f * (1 - k)); }, () => { SpFree(body); SpFree(glowBody); }));
            Sfx.Play("hit", 1);
            SpArc(b, col, new AO { radius = 2.1f, width = 0.95f, tilt = -0.6f, sweep = 2.9f, start = -0.5f, dur = 0.5f, len = 0.9f });
            SpArc(b, Color.white, new AO { radius = 1.9f, width = 0.25f, tilt = -0.6f, sweep = 2.7f, start = -0.45f, dur = 0.38f, len = 0.5f });
            SpImpact(t, col, true);
            SpRing(SpPos(t), col, 0.5f, 3.2f, 0.6f);
            SpRing(SpPos(t), Color.white, 0.3f, 1.8f, 0.35f);
            SpLight(b, col, 8, 0.35f);
            SpShake(0.3f, 0.4f);
            SpFlash(Rgb(200, 255, 220), 0.22f, 0.22f);
            c.HitAll();
            SpGlyph(SpPos(t, 3.2f), c.glyph, col, 3.8f);
            yield return W(0.6f);
        }

        // ---- havoc 无双：血色巨刃 + 双重冲击波
        IEnumerator Sp_havoc(SpCtx c)
        {
            var t = c.target;
            yield return SpCharge(c, 0.32f);
            SpBolt(SpPos(c.u, 0.2f), SpPos(c.u, 3.4f), c.color, 0.3f, 0.5f);
            yield return SpLunge(c, 0.55f, 0.12f);
            var p = SpPos(t, 0.9f);
            Sfx.Play("hit", 1); Sfx.Play("rock", 0.5f);
            SpArc(p, c.color, new AO { radius = 2.5f, width = 1.1f, tilt = 0.25f, sweep = 3.4f, start = -0.9f, dur = 0.55f, len = 1.0f });
            SpArc(p, Color.white, new AO { radius = 2.3f, width = 0.25f, tilt = 0.25f, sweep = 3.1f, start = -0.85f, dur = 0.4f, len = 0.6f });
            yield return W(0.07f);
            SpArc(p, c.color, new AO { radius = 1.6f, width = 0.6f, tilt = -1.2f, sweep = 2.4f, start = 0.2f, dur = 0.45f });
            SpImpact(t, c.color, true);
            var g = SpPos(t);
            SpRing(g, c.color, 0.4f, 3.6f, 0.6f);
            SpLater(0.12f, () => SpRing(g, Art.Hex("#ffd0d8"), 0.4f, 2.6f, 0.5f));
            SpDust(SpPos(t, 0.2f), 22, 4);
            SpLight(p, c.color, 9, 0.4f);
            SpShake(0.42f, 0.55f);
            SpFlash(Rgb(255, 40, 60), 0.3f, 0.35f);
            c.HitAll();
            SpGlyph(SpPos(t, 3.3f), c.glyph, c.color, 4.2f);
            yield return W(0.65f);
        }

        // ---- sweep 横扫：贴地的一圈回旋刀光，扫到谁打谁
        IEnumerator Sp_sweep(SpCtx c)
        {
            var cc = SpPos(c.u, 0.6f);
            yield return SpCharge(c, 0.2f);
            Sfx.Play("hit", 0.8f);
            var others = c.targets.Where(x => x != c.u).ToList();
            // 每个目标相对施展者的角度（弧面平躺，绕 y 轴扫过）
            Func<BUnit, float> angle = x => { var p = SpPos(x); return Mathf.Atan2(p.z - cc.z, p.x - cc.x); };
            float start = others.Count > 0 ? angle(others[0]) - 0.9f : 0;
            SpArc(cc, c.color, new AO { flat = true, radius = 1.7f, width = 0.9f, start = start, sweep = Mathf.PI * 2, dur = 0.6f, len = 0.5f, grow = 0.25f });
            SpArc(cc, Color.white, new AO { flat = true, radius = 1.75f, width = 0.25f, start = start, sweep = Mathf.PI * 2, dur = 0.5f, len = 0.3f, grow = 0.25f });
            SpDust(SpPos(c.u, 0.2f), 16, 3.4f);
            var done = new HashSet<BUnit>();
            yield return SpAnim(0.36f, k =>
            {
                float head = start + k * Mathf.PI * 2;
                foreach (var x in others)
                {
                    if (done.Contains(x)) continue;
                    float d = angle(x) - start; while (d < 0) d += Mathf.PI * 2;
                    if (head - start >= d) { done.Add(x); SpImpact(x, c.color, false); c.HitUnit(x); }
                }
            });
            foreach (var x in others) if (!done.Contains(x)) { SpImpact(x, c.color, false); c.HitUnit(x); }
            c.HitAll();
            SpRing(SpPos(c.u), c.color, 0.6f, 3.4f, 0.5f);
            SpShake(0.2f, 0.3f);
            SpGlyph(SpPos(c.u, 3.0f), c.glyph, c.color);
            yield return W(0.5f);
        }

        // ---- dash 突击：残影 + 冲击 + 击退
        IEnumerator Sp_dash(SpCtx c)
        {
            var v = c.v; var t = c.target; var res = c.res;
            yield return SpCharge(c, 0.22f);
            var from = v != null ? v.transform.position : SpPos(c.u);
            var to = res != null && res.moved != null ? Tile(res.moved.to.x, res.moved.to.y) : from;
            var tp = SpPos(t);
            if (v != null) v.Face(tp - from);
            Sfx.Play("march", 0.7f);
            var trail = new List<Vector3>();
            float lastGhost = -1;
            yield return SpAnim(0.3f, k =>
            {
                float e = k * k;
                if (v != null) v.transform.position = Vector3.Lerp(from, to, e);
                var cur = v != null ? v.transform.position : Vector3.Lerp(from, to, e);
                trail.Add(cur + Up * 0.5f);
                if (k - lastGhost > 0.12f && v != null) { lastGhost = k; SpGhost(v, c.color, 0.45f, 0.55f); }
                SpDust(cur + Up * 0.15f, 2, 1.2f);
                Emit(glow, cur + Up * 0.6f, 3, new EO { color = c.color, alpha = 0.9f, life = 0.35f, speed = 0.6f, size = 0.35f, sizeEnd = 0.1f, gravity = 0, radius = 0.35f });
            });
            if (trail.Count > 1)
            {
                var tm = FxMat(1, c.color, true);
                var m = SpRibbonObj(tm, 3024);
                SetRibbon(m.mesh, trail, 1.1f, null);
                SpRun(SpAnim(0.4f, k => tm.SetFloat("_Opacity", 0.9f * (1 - k)), () => SpFree(m)));
            }
            // 冲击
            var tv = VisOf(t);
            var hitP = Vector3.Lerp(Vector3.Lerp(from, tp, 0.5f), tp, 0.5f);
            if (v != null)
            {
                var back = v.transform.position;
                yield return SpAnim(0.08f, k => v.transform.position = Vector3.Lerp(back, tp, k * 0.35f));
                SpRun(SpAnim(0.18f, k => v.transform.position = Vector3.Lerp(tp, back, 0.65f + 0.35f * k), () => { if (v != null) v.transform.position = back; }));
            }
            Sfx.Play("hit", 1); Sfx.Play("rock", 0.4f);
            SpImpact(t, c.color, true);
            SpRing(tp, c.color, 0.4f, 3, 0.55f);
            hitP.y = tp.y;
            SpRing(hitP, Color.white, 0.3f, 1.6f, 0.3f);
            SpLight(SpPos(t, 1), c.color, 7, 0.3f);
            SpShake(0.35f, 0.4f);
            SpFlash(Color.white, 0.18f, 0.2f);
            c.HitAll();
            SpGlyph(SpPos(t, 3.1f), c.glyph, c.color);
            var push = res != null ? res.pushed.FirstOrDefault(p => p.unit == t) : null;
            if (push != null && tv != null)
            {
                var pa = tv.transform.position; var pb = Tile(push.to.x, push.to.y);
                yield return SpAnim(0.25f, k =>
                {
                    var p = Vector3.Lerp(pa, pb, Ease(k)); p.y += Mathf.Sin(k * Mathf.PI) * 0.35f;
                    tv.transform.position = p;
                    SpDust(p + Up * 0.1f, 1, 1);
                });
                tv.transform.position = pb;
            }
            else if (res != null && res.blocked != null && tv != null) SpSparks(SpPos(t, 0.5f), Art.Hex("#ffb04a"), 26, 4.5f);
            yield return W(0.45f);
        }

        // ---- whirl 往来连斩：在敌军之间瞬移连斩，最后回到原位
        IEnumerator Sp_whirl(SpCtx c)
        {
            var v = c.v;
            var home = v != null ? v.transform.position : SpPos(c.u);
            yield return SpCharge(c, 0.2f);
            int n = c.hits.Count > 0 ? c.hits.Count : 1;
            float step = Mathf.Clamp(0.95f / n, 0.1f, 0.22f);
            var cur = home;
            for (int i = 0; i < n; i++)
            {
                var x = i < c.hits.Count ? c.hits[i].unit : c.target;
                var tp = SpPos(x);
                var dir = tp - cur; dir.y = 0;
                var off = dir.sqrMagnitude > 0.01f ? dir.normalized * -0.75f : new Vector3(0.75f, 0, 0);
                var side = new Vector3(off.z, 0, -off.x) * (i % 2 == 1 ? 0.7f : -0.7f);
                var dest = tp + off + side;
                var a = cur;
                if (v != null) { SpGhost(v, c.color, 0.35f, 0.5f); v.Face(tp - dest); }
                yield return SpAnim(step * 0.45f, k =>
                {
                    var p = Vector3.Lerp(a, dest, Ease(k));
                    if (v != null) v.transform.position = p;
                    Emit(glow, p + Up * 0.6f, 2, new EO { color = c.color, alpha = 0.9f, life = 0.3f, speed = 0.3f, size = 0.3f, sizeEnd = 0.1f, gravity = 0, radius = 0.2f });
                });
                cur = dest;
                Sfx.Play("hit", 0.7f);
                float tl = (i % 2 == 1 ? 0.8f : -0.8f) + (Rnd() - 0.5f) * 0.4f;
                SpArc(SpPos(x, 0.9f), c.color, new AO { radius = 1.9f, width = 0.42f, start = Mathf.PI / 2 - 0.55f, sweep = 1.1f, tilt = tl, mid = 1.9f, dur = 0.42f, len = 1, stay = true });
                SpArc(SpPos(x, 0.9f), Color.white, new AO { radius = 1.97f, width = 0.12f, start = Mathf.PI / 2 - 0.55f, sweep = 1.1f, tilt = tl, mid = 1.9f, dur = 0.3f, len = 1, stay = true });
                SpImpact(x, c.color, false);
                SpShake(0.12f, 0.15f);
                c.Hit(i);
                yield return W(step * 0.55f);
            }
            if (v != null)
            {
                SpGhost(v, c.color, 0.35f, 0.5f);
                var a2 = v.transform.position;
                yield return SpAnim(0.18f, k => v.transform.position = Vector3.Lerp(a2, home, Ease(k)));
                v.transform.position = home;
                v.Face(SpPos(c.target) - home);
            }
            SpRing(SpPos(c.u), c.color, 0.5f, 2.6f, 0.45f);
            SpGlyph(SpPos(c.u, 3.0f), c.glyph, c.color);
            yield return W(0.4f);
        }

        // ---- arrows 箭雨：扇形齐射，抛物线落入范围
        sealed class SpBatch
        {
            public readonly Mesh mesh; readonly Vector3[] tv, verts; readonly int per;
            public SpBatch(MeshBuilder tpl, int count)
            {
                per = tpl.v.Count; tv = tpl.v.ToArray();
                verts = new Vector3[per * count];
                var uvs = new Vector2[per * count]; var cols = new Color[per * count]; var tris = new int[tpl.t.Count * count];
                for (int i = 0; i < count; i++)
                {
                    for (int j = 0; j < per; j++) { uvs[i * per + j] = tpl.uv[j]; cols[i * per + j] = Color.white; }
                    for (int k = 0; k < tpl.t.Count; k++) tris[i * tpl.t.Count + k] = tpl.t[k] + i * per;
                }
                mesh = new Mesh { name = "fxBatch" };
                mesh.MarkDynamic();
                mesh.vertices = verts; mesh.uv = uvs; mesh.colors = cols; mesh.triangles = tris;
            }
            public void Set(int i, Matrix4x4 m) { for (int j = 0; j < per; j++) verts[i * per + j] = m.MultiplyPoint3x4(tv[j]); }
            public void Apply() { mesh.vertices = verts; mesh.RecalculateBounds(); }
        }
        sealed class Arrow { public Vector3 s, d; public float t0, dur, h; public bool landed; }
        IEnumerator Sp_arrows(SpCtx c)
        {
            var src = SpPos(c.u, 1.0f);
            var col = c.color;
            var area = c.res != null && c.res.area != null && c.res.area.Count > 0 ? c.res.area : new List<Vector2Int> { new Vector2Int(c.target.x, c.target.y) };
            SpTiles(area, col, 1.2f, 0.45f);
            yield return SpCharge(c, 0.18f);
            Sfx.Play("march", 0.5f);
            const int N = 42;
            // 箭杆：深色实体（普通混合），箭头：发光的招式色（叠加）——两组共用同一套矩阵
            var shaftTpl = new MeshBuilder(); shaftTpl.Box(Vector3.zero, new Vector3(0.05f, 0.05f, 0.9f), Color.white);
            var headTpl = new MeshBuilder(); headTpl.Box(new Vector3(0, 0, 0.5f), new Vector3(0.12f, 0.12f, 0.3f), Color.white);
            var shafts = new SpBatch(shaftTpl, N); var heads = new SpBatch(headTpl, N);
            var shaftMat = AlphaMat(new Color(0x2a / 255f, 0x20 / 255f, 0x18 / 255f, 0.95f), null);
            var headMat = AddMat(Art.Shade(col, 0.35f), null);
            var so = SpMake("FxArrows", shafts.mesh, true, shaftMat, true, 3024);
            var ho = SpMake("FxArrowHeads", heads.mesh, true, headMat, true, 3025);
            var arrows = new List<Arrow>();
            for (int i = 0; i < N; i++)
            {
                var tl = area[i % area.Count];
                var dst = Tile(tl.x, tl.y) + new Vector3((Rnd() - 0.5f) * 1.5f, 0.15f, (Rnd() - 0.5f) * 1.5f);
                var s = src + new Vector3((Rnd() - 0.5f) * 1.2f, Rnd() * 0.4f, (Rnd() - 0.5f) * 1.2f);
                arrows.Add(new Arrow { s = s, d = dst, t0 = Rnd() * 0.3f, dur = 0.5f + Rnd() * 0.15f, h = 2.6f + Rnd() * 1.1f });
            }
            var firstHit = new HashSet<BUnit>();
            yield return SpLoop(1.15f, k =>
            {
                if (so.mesh == null) return;
                float elapsed = k * 1.15f;
                for (int i = 0; i < N; i++)
                {
                    var a = arrows[i];
                    float q = Mathf.Clamp01((elapsed - a.t0) / a.dur);
                    var p = Vector3.Lerp(a.s, a.d, q); p.y += Mathf.Sin(q * Mathf.PI) * a.h;
                    float q2 = Mathf.Min(1, q + 0.02f);
                    var p2 = Vector3.Lerp(a.s, a.d, q2); p2.y += Mathf.Sin(q2 * Mathf.PI) * a.h;
                    var rot = q > 0 && q < 1 && (p2 - p).sqrMagnitude > 1e-8f ? Quaternion.LookRotation(p2 - p) : Quaternion.identity;
                    float sc = q <= 0 || q >= 1 ? 0.0001f : 1;
                    var mtx = Matrix4x4.TRS(p, rot, new Vector3(sc, sc, sc));
                    shafts.Set(i, mtx); heads.Set(i, mtx);
                    if (q > 0 && q < 1 && Rnd() < 0.3f) Emit(glow, p, 1, new EO { color = col, alpha = 0.6f, life = 0.2f, speed = 0, size = 0.18f, sizeEnd = 0.2f, gravity = 0, radius = 0.02f });
                    if (q >= 1 && !a.landed)
                    {
                        a.landed = true;
                        Emit(glow, a.d, 3, new EO { color = col, color2 = Color.white, mix = 0.4f, alpha = 1, life = 0.3f, speed = 1.6f, size = 0.14f, gravity = 0.6f, radius = 0.1f });
                        if (Rnd() < 0.4f) SpDustPuff(a.d, 0.8f);
                        foreach (var h in c.hits)
                        {
                            if (firstHit.Contains(h.unit)) continue;
                            if (Mathf.Abs(h.unit.x * T - (a.d.x - Origin.x - T / 2)) < T && Mathf.Abs(h.unit.y * T - (a.d.z - Origin.z - T / 2)) < T)
                            {
                                firstHit.Add(h.unit); var vv = VisOf(h.unit); if (vv != null) vv.FlashHit(); c.HitUnit(h.unit); Sfx.Play("hit", 0.5f);
                            }
                        }
                    }
                }
                shafts.Apply(); heads.Apply();
            });
            SpFree(so); SpFree(ho);
            c.HitAll();
            SpShake(0.12f, 0.2f);
            SpGlyph(SpPos(c.target, 3.0f), c.glyph, c.color);
            yield return W(0.35f);
        }

        // ---- arrow 一箭穿杨：蓄力、一道金光直贯敌阵
        IEnumerator Sp_arrow(SpCtx c)
        {
            var v = c.v; var t = c.target;
            Vector3 a = SpPos(c.u, 1.0f), b = SpPos(t, 0.8f);
            if (v != null) v.Face(b - a);
            yield return SpCharge(c, 0.4f);
            Sfx.Play("duel", 0.6f);
            var col = c.color;
            var mid = Vector3.Lerp(a, b, 0.5f); mid.y += 0.8f;
            var pts = new List<Vector3>();
            for (int i = 0; i <= 20; i++) pts.Add(Bez(a, mid, b, i / 20f));
            var mat = FxMat(1, col, true);
            mat.SetFloat("_Opacity", 0);
            var m = SpRibbonObj(mat, 3024);
            SetRibbon(m.mesh, pts, 0.42f, null);
            var hb = new MeshBuilder(); hb.Box(Vector3.zero, new Vector3(0.08f, 0.08f, 1.3f), Color.white);
            var head = SpMake("FxArrowHead", hb.ToMesh("fxArrowHead"), true, AddMat(Art.Shade(col, 0.6f), null), true, 3025);
            yield return SpAnim(0.22f, k =>
            {
                if (head.go == null) return;
                Vector3 p = Bez(a, mid, b, k), p2 = Bez(a, mid, b, Mathf.Min(1, k + 0.05f));
                head.go.transform.position = p;
                if ((p2 - p).sqrMagnitude > 1e-8f) head.go.transform.rotation = Quaternion.LookRotation(p2 - p);
                mat.SetFloat("_Opacity", 1.2f * k);
                Emit(glow, p, 4, new EO { color = col, color2 = Color.white, mix = 0.5f, alpha = 1, life = 0.35f, speed = 0.5f, size = 0.3f, sizeEnd = 0.1f, gravity = 0, radius = 0.08f });
            });
            SpFree(head);
            SpRun(SpAnim(0.5f, k => mat.SetFloat("_Opacity", 1.2f * (1 - k)), () => SpFree(m)));
            Sfx.Play("hit", 1);
            var dir = (b - a).normalized;
            Emit(glow, b, 30, new EO { color = col, color2 = Color.white, mix = 0.4f, alpha = 1, life = 0.5f, speed = 5, size = 0.2f, gravity = 0.3f, radius = 0.15f, speedVar = 0.6f });
            Emit(glow, b + dir * 0.8f, 12, new EO { color = Color.white, alpha = 1, life = 0.3f, speed = 6, size = 0.16f, gravity = 0, radius = 0.05f });
            var tv = VisOf(t); if (tv != null) tv.FlashHit();
            SpRing(SpPos(t), col, 0.3f, 2.4f, 0.45f);
            SpLight(b, col, 7, 0.25f);
            SpShake(0.22f, 0.3f);
            SpFlash(Rgb(255, 240, 200), 0.16f, 0.18f);
            c.HitAll();
            SpGlyph(SpPos(t, 3.0f), c.glyph, col);
            yield return W(0.55f);
        }

        // 火海（blaze / wind 共用）：范围内各格同时起火
        void SpFireField(List<Vector2Int> tiles, Color col, bool big)
        {
            EnsurePools();
            foreach (var tl in tiles)
            {
                if (!M.InBounds(tl.x, tl.y)) continue;
                var p = Tile(tl.x, tl.y);
                var bs = p + Up * 0.2f;
                Emit(glow, p + Up * 0.6f, 1, new EO { color = col, alpha = 0.75f, life = 0.35f, speed = 0, size = 2.6f, sizeEnd = 0.7f, gravity = 0, radius = 0.1f });
                Emitter(flame, bs, big ? 70 : 50, new EO
                {
                    color = C(1, 0.66f, 0.16f), color2 = C(1, 0.84f, 0.36f), mix = 0.3f, colorEnd = C(0.72f, 0.12f, 0.03f), alpha = 1,
                    life = 0.8f, speed = 1.8f, size = 1.1f, sizeEnd = 0.3f, gravity = -0.25f, box = new Vector3(1.6f, 0.2f, 1.6f), jitter = 0.3f, lifeVar = 0.35f, sizeVar = 0.3f, alphaCurve = 2,
                }, 1.1f);
                Emitter(heat, bs, 20, new EO
                {
                    color = C(1, 0.56f, 0.16f), colorEnd = C(0.95f, 0.24f, 0.04f), alpha = 0.9f, life = 0.6f, speed = 0.9f, size = 1.4f, sizeEnd = 0.7f, gravity = -0.1f,
                    box = new Vector3(1.4f, 0.15f, 1.4f), jitter = 0.15f, lifeVar = 0.3f, alphaCurve = 2,
                }, 1.1f);
                Emitter(glow, bs, 10, new EO
                {
                    color = C(1, 0.8f, 0.4f), colorEnd = C(1, 0.35f, 0.1f), alpha = 1, life = 1.0f, speed = 2.8f, size = 0.14f, sizeEnd = 0.4f, gravity = -0.1f,
                    box = new Vector3(1.4f, 0.2f, 1.4f), jitter = 0.6f, lifeVar = 0.4f,
                }, 1.1f);
                Emitter(dust, p + Up * 1.4f, 7, new EO
                {
                    color = C(0.15f, 0.13f, 0.12f), colorEnd = C(0.4f, 0.38f, 0.36f), alpha = 0.5f, life = 2.0f, speed = 1.3f, size = 1.0f, sizeEnd = 3.0f, gravity = -0.03f,
                    box = new Vector3(1.0f, 0.3f, 1.0f), jitter = 0.25f, lifeVar = 0.3f, drag = 0.3f, fadeIn = 0.12f,
                }, 1.1f);
            }
        }

        // ---- fire 火海：火球抛射 → 爆燃 → 范围火海
        IEnumerator Sp_fire(SpCtx c)
        {
            EnsurePools();
            Vector3 a = SpPos(c.u, 1.2f), b = SpPos(c.target, 0.5f);
            var area = SpAreaTiles(c, 1);
            SpTiles(area, c.color, 1.4f, 0.55f);
            yield return SpCharge(c, 0.25f);
            Sfx.Play("fire", 0.8f);
            var mid = Vector3.Lerp(a, b, 0.5f); mid.y += 4;
            yield return SpTrail(k => Bez(a, mid, b, k), 0.42f, 6, new EO
            {
                pool = flame, color = C(1, 0.7f, 0.2f), color2 = C(1, 0.9f, 0.5f), mix = 0.4f, colorEnd = C(0.8f, 0.15f, 0.03f), alpha = 1, life = 0.45f, speed = 0.6f,
                size = 0.9f, sizeEnd = 0.2f, gravity = -0.2f, radius = 0.2f, lifeVar = 0.3f, alphaCurve = 2, head = 1.5f, headColor = C(1, 0.85f, 0.5f),
            });
            Sfx.Play("rock", 0.6f); Sfx.Play("fire", 0.8f);
            SpSparks(b, Art.Hex("#ffb347"), 40, 6, 0.24f);
            SpRing(SpPos(c.target), Art.Hex("#ff9a3c"), 0.5f, 4, 0.6f);
            SpLight(b, Art.Hex("#ff8030"), 10, 0.6f);
            SpShake(0.3f, 0.45f);
            SpFlash(Rgb(255, 140, 40), 0.25f, 0.35f);
            SpFireField(area, c.color, true);
            foreach (var x in c.targets) { var vv = VisOf(x); if (vv != null && x.side != c.u.side) { vv.Burn(); vv.FlashHit(); } }
            c.HitAll();
            SpGlyph(SpPos(c.target, 3.2f), c.glyph, c.color, 3.8f);
            yield return W(0.75f);
        }

        // ---- wind 风助火势（借东风）：东风卷过战场，敌阵燃起大火
        IEnumerator Sp_wind(SpCtx c)
        {
            EnsurePools();
            var cc = SpPos(c.target, 0.6f);
            var area = SpAreaTiles(c, 2);
            var sky = Art.Hex("#bfe8ff");
            SpTiles(area, sky, 1.6f, 0.4f);
            if (c.v != null) c.v.Tint(sky, 1);
            SpPillar(SpPos(c.u), sky, 0.8f, 5, 0.9f, 0.7f);
            Sfx.Play("magic", 0.6f);
            // 风：自东南向目标的流线
            var wcol = C(0.85f, 0.95f, 1);
            var from = cc + new Vector3(9, 1.5f, -5);
            yield return SpAnim(0.55f, k =>
            {
                for (int i = 0; i < 6; i++)
                {
                    var s = from + new Vector3((Rnd() - 0.5f) * 6, Rnd() * 2.5f, (Rnd() - 0.5f) * 8);
                    var vel = (cc - s).normalized * (14 + Rnd() * 6);
                    glow.Add(s, new Vector3(vel.x, vel.y * 0.3f, vel.z), 0.55f, wcol, C(1, 0.8f, 0.5f), 0.75f, 0.28f, 0.9f, 0, 0.3f, 0.1f, 1);
                }
                // 漩涡
                for (int i = 0; i < 3; i++)
                {
                    float ang = k * 18 + i * 2.1f, r = 2.6f - k * 1.4f;
                    Emit(glow, cc + new Vector3(Mathf.Cos(ang) * r, k * 2.2f, -Mathf.Sin(ang) * r), 1, new EO { color = wcol, alpha = 0.8f, life = 0.4f, speed = 0.4f, size = 0.4f, sizeEnd = 0.2f, gravity = 0, radius = 0.1f });
                }
            });
            Sfx.Play("fire", 0.9f); Sfx.Play("rock", 0.5f);
            SpRing(SpPos(c.target), Art.Hex("#ffae42"), 0.6f, 5.5f, 0.7f);
            SpRing(SpPos(c.target), Color.white, 0.4f, 3.0f, 0.4f);
            SpLight(cc, Art.Hex("#ff8030"), 12, 0.7f);
            SpShake(0.32f, 0.5f);
            SpFlash(Rgb(255, 150, 50), 0.28f, 0.4f);
            var fireTiles = c.res != null && c.res.burned != null && c.res.burned.Count > 0 ? c.res.burned
                : c.targets.Where(x => x.side != c.u.side).Select(x => new Vector2Int(x.x, x.y)).ToList();
            SpFireField(fireTiles, c.color, true);
            foreach (var x in c.targets) { var vv = VisOf(x); if (vv != null && x.side != c.u.side) { vv.Burn(); vv.FlashHit(); } }
            c.HitAll();
            SpGlyph(cc + Up * 2.8f, c.glyph, sky, 4.2f);
            yield return W(0.8f);
        }

        // ---- lightning 雷击：天色骤暗，乌云压顶，雷霆逐一劈落
        IEnumerator Sp_lightning(SpCtx c)
        {
            EnsurePools();
            var area = SpAreaTiles(c, 2);
            var cc = SpPos(c.target);
            SpDim(0.5f, 1.5f);
            SpTiles(area, c.color, 1.4f, 0.4f);
            if (c.v != null) c.v.Tint(c.color, 1);
            for (int i = 0; i < 26; i++)
                Emit(dust, cc + new Vector3((Rnd() - 0.5f) * 9, 5.2f + Rnd() * 1.2f, (Rnd() - 0.5f) * 7), 1, new EO
                {
                    color = C(0.16f, 0.17f, 0.24f), colorEnd = C(0.3f, 0.32f, 0.4f), alpha = 0.75f, life = 1.6f, speed = 0.4f, size = 3.2f, sizeEnd = 4.5f, gravity = 0, radius = 0.5f, fadeIn = 0.25f, drag = 1,
                });
            Sfx.Play("magic", 0.5f);
            yield return W(0.3f);
            var strike = c.targets.Where(x => x.side != c.u.side).ToList();
            if (strike.Count == 0) strike.Add(c.target);
            foreach (var x in strike)
            {
                var g = SpPos(x, 0.3f);
                var top = g + new Vector3((Rnd() - 0.5f) * 2, 6.5f, (Rnd() - 0.5f) * 2);
                Sfx.Play("rock", 0.7f);
                SpBolt(top, g, c.color, 0.36f, 0.7f);
                SpBolt(top + new Vector3(0.5f, 0, -0.3f), g, Color.white, 0.22f, 0.32f);
                SpSparks(g, c.color, 26, 5, 0.22f);
                SpRing(SpPos(x), c.color, 0.3f, 2.4f, 0.45f);
                SpLight(g + Up * 1.5f, Art.Hex("#cfe6ff"), 12, 0.18f);
                SpFlash(Rgb(225, 240, 255), 0.35f, 0.16f);
                SpShake(0.25f, 0.25f);
                var vv = VisOf(x); if (vv != null) vv.FlashHit();
                c.HitUnit(x);
                yield return W(0.16f);
            }
            c.HitAll();
            SpGlyph(cc + Up * 3.2f, c.glyph, c.color, 4);
            yield return W(0.55f);
        }

        // ---- water 水淹：洪水漫过范围（水面 + 涟漪），浪墙席卷，目标处水柱冲天
        static Mesh WallMesh(float radius, float height, float start, float sweep, int n)
        {
            var vs = new Vector3[(n + 1) * 2]; var uv = new Vector2[(n + 1) * 2]; var tri = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n, a = start + sweep * t;
                float x = Mathf.Cos(a) * radius, z = Mathf.Sin(a) * radius;
                vs[i * 2] = new Vector3(x, 0, z); vs[i * 2 + 1] = new Vector3(x, height, z);
                uv[i * 2] = new Vector2(t, 0); uv[i * 2 + 1] = new Vector2(t, 1);
                if (i < n) { int k = i * 2, j = i * 6; tri[j] = k; tri[j + 1] = k + 1; tri[j + 2] = k + 2; tri[j + 3] = k + 1; tri[j + 4] = k + 3; tri[j + 5] = k + 2; }
            }
            var m = new Mesh { name = "fxWall" };
            m.vertices = vs; m.uv = uv; m.triangles = tri; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
        IEnumerator Sp_water(SpCtx c)
        {
            EnsurePools();
            var area = SpAreaTiles(c, 1);
            var cc = SpPos(c.target);
            var col = c.color;
            var deep = Art.Shade(col, -0.35f);
            SpTiles(area, col, 1.7f, 0.5f);
            yield return SpCharge(c, 0.25f);
            Sfx.Play("fire", 0.4f);
            var mat = FxMat(3, col, false);
            var wall = SpMake("FxWave", WallMesh(1, 1, 0, Mathf.PI * 2, 72), true, mat, true, 3020);
            wall.go.transform.position = cc;
            wall.go.transform.localScale = new Vector3(0.3f, 0.25f, 0.3f);
            // 水面：深色半透明圆盘，随浪扩张、随后退去
            var pmat = AlphaMat(deep, Art.SoftDot);
            SetAlpha(pmat, deep, 0);
            var pool = SpMake("FxFlood", UnitDisc, false, pmat, true, 3019);
            pool.go.transform.position = cc + Up * 0.14f;
            float R = c.Radius(1) * T + 1.4f;
            var done = new HashSet<BUnit>();
            float lastRipple = 0;
            yield return SpAnim(0.95f, k =>
            {
                if (wall.go == null) return;
                float r = 0.3f + Ease(k) * R;
                float h = 2.1f * Mathf.Sin(Mathf.Min(1, k * 1.3f) * Mathf.PI) + 0.25f;
                wall.go.transform.localScale = new Vector3(r, h, r);
                mat.SetFloat("_FxTime", k * 2.4f);
                mat.SetFloat("_Opacity", k < 0.7f ? 1 : (1 - k) / 0.3f);
                pool.go.transform.localScale = new Vector3(r * 1.25f, 1, r * 1.25f);
                SetAlpha(pmat, deep, 0.75f * Mathf.Min(1, k * 3) * (k < 0.75f ? 1 : (1 - k) / 0.25f));
                for (int i = 0; i < 2; i++)
                {
                    float ang = Rnd() * Mathf.PI * 2;
                    var p = cc + new Vector3(Mathf.Cos(ang) * r, h * 0.85f, Mathf.Sin(ang) * r);
                    Emit(glow, p, 3, new EO { color = C(0.85f, 0.95f, 1), alpha = 0.9f, life = 0.55f, speed = 2.6f, size = 0.24f, gravity = 1.3f, radius = 0.2f });
                }
                if (k - lastRipple > 0.12f)
                {
                    lastRipple = k;
                    float ang = Rnd() * Mathf.PI * 2, rr = Rnd() * r * 0.8f;
                    SpRing(cc + new Vector3(Mathf.Cos(ang) * rr, 0.06f, Mathf.Sin(ang) * rr), Art.Hex("#e8f6ff"), 0.2f, 1.3f, 0.5f, new RO { alpha = 0.7f });
                }
                foreach (var x in c.targets)
                {
                    if (done.Contains(x) || x.side == c.u.side) continue;
                    if (Vector3.Distance(SpPos(x), cc) <= r)
                    {
                        done.Add(x);
                        var xp = SpPos(x);
                        Emit(glow, xp + Up * 0.4f, 30, new EO { color = C(0.82f, 0.93f, 1), color2 = col, mix = 0.3f, alpha = 1, life = 0.75f, speed = 4.6f, size = 0.3f, gravity = 1.6f, radius = 0.35f, speedVar = 0.4f });
                        SpPillar(xp, col, 0.55f, 2.6f, 0.55f, 0.8f);
                        var vv = VisOf(x); if (vv != null) { vv.FlashHit(); vv.Tint(col, 0.8f); }
                        c.HitUnit(x); Sfx.Play("hit", 0.5f);
                    }
                }
            });
            SpFree(wall); SpFree(pool);
            c.HitAll();
            SpShake(0.2f, 0.3f);
            SpGlyph(cc + Up * 3.0f, c.glyph, col);
            yield return W(0.4f);
        }

        // ---- shock 怒吼：吸气 → 三重冲击波 + 竖直声浪，扬尘四散
        IEnumerator Sp_shock(SpCtx c)
        {
            EnsurePools();
            var cc = SpPos(c.u);
            float R = c.Radius(2) * T + 1;
            yield return SpCharge(c, 0.28f);
            Sfx.Play("horn", 0.9f); Sfx.Play("rock", 0.6f);
            SpFlash(Rgb(255, 170, 90), 0.22f, 0.3f);
            SpShake(0.5f, 0.7f);
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                SpLater(ii * 0.11f, () =>
                {
                    SpRing(cc, ii == 1 ? Color.white : c.color, 0.6f, R + ii * 0.6f, 0.6f, new RO { alpha = 1 });
                    SpRing(cc + Up * 1.2f, c.color, 0.4f, R * 0.7f + ii * 0.4f, 0.5f, new RO { vertical = true, y = 0, alpha = 0.7f });
                });
            }
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * Mathf.PI * 2;
                dust.Add(new Vector3(cc.x + Mathf.Cos(a) * 0.8f, cc.y + 0.25f, cc.z + Mathf.Sin(a) * 0.8f), new Vector3(Mathf.Cos(a) * 7, 0.4f, Mathf.Sin(a) * 7), 0.9f,
                    C(0.66f, 0.58f, 0.46f), C(0.7f, 0.65f, 0.58f), 0.55f, 0.9f, 2.4f, 0, 2.6f, 0.05f, 1);
            }
            var done = new HashSet<BUnit>();
            yield return SpAnim(0.55f, k =>
            {
                float r = 0.6f + Ease(k) * R;
                foreach (var x in c.targets)
                {
                    if (done.Contains(x) || x == c.u) continue;
                    if (Vector3.Distance(SpPos(x), cc) <= r)
                    {
                        done.Add(x); var vv = VisOf(x); if (vv != null) vv.FlashHit();
                        SpSparks(SpPos(x, 0.7f), c.color, 12, 2.5f, 0.18f); c.HitUnit(x);
                    }
                }
            });
            c.HitAll();
            SpGlyph(SpPos(c.u, 3.1f), c.glyph, c.color, 4);
            yield return W(0.55f);
        }

        // ---- aura 金光：光柱冲天，脚下光阵展开，友军金光加身
        IEnumerator Sp_aura(SpCtx c)
        {
            EnsurePools();
            var cc = SpPos(c.u);
            var col = c.color;
            float rad = c.res != null && c.res.sp != null ? (c.res.sp.radius != 0 ? (float)c.res.sp.radius : 1) : 2;
            float R = rad * T + 0.6f;
            Sfx.Play("horn", 0.7f); Sfx.Play("magic", 0.5f);
            SpPillar(cc, col, 0.9f, 7, 1.2f, 1);
            SpRing(cc, col, 0.5f, R, 1.1f, new RO { tex = CircleTex, spin = 2.2f, hold = true, alpha = 0.85f });
            SpRing(cc, col, 0.3f, R + 0.8f, 0.7f);
            SpLight(cc + Up * 2, col, 7, 0.8f);
            SpFlash(Rgb(255, 230, 150), 0.15f, 0.4f);
            if (c.v != null) c.v.Tint(col, 1);
            yield return W(0.25f);
            var allies = c.targets.Where(x => x.side == c.u.side).ToList();
            foreach (var x in allies)
            {
                var p = SpPos(x);
                var vv = VisOf(x); if (vv != null) vv.Tint(col, 1);
                if (x != c.u) SpPillar(p, col, 0.55f, 3.2f, 0.8f, 0.7f);
                SpRing(p, col, 0.2f, 1.6f, 0.5f);
                Emitter(glow, p + Up * 0.2f, 30, new EO { color = col, color2 = C(1, 1, 0.9f), mix = 0.4f, alpha = 1, life = 0.9f, speed = 1.6f, size = 0.2f, sizeEnd = 0.4f, gravity = -0.15f, box = new Vector3(1.2f, 0.2f, 1.2f), jitter = 0.2f, lifeVar = 0.3f }, 0.7f);
            }
            c.HitAll();
            SpGlyph(cc + Up * 3.4f, c.glyph, col, 4);
            yield return W(0.85f);
        }

        // ---- blossom 桃花（桃园结义）：桃花纷飞，金光加身
        IEnumerator Sp_blossom(SpCtx c)
        {
            EnsurePools();
            var cc = SpPos(c.u);
            var petalPool = PetalPool();
            var pink = c.color;
            Sfx.Play("magic", 0.6f);
            SpPillar(cc, Art.Hex("#ffe7a8"), 0.9f, 6, 1.2f, 0.7f);
            SpRing(cc, c.color, 0.5f, c.Radius(2) * T + 0.6f, 1.1f, new RO { tex = CircleTex, spin = 1.6f, hold = true, alpha = 0.7f });
            SpLight(cc + Up * 2, Art.Hex("#ffc0d8"), 6, 0.9f);
            if (c.v != null) c.v.Tint(Art.Hex("#ffd0e0"), 1);
            yield return SpAnim(0.6f, k =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float a = Rnd() * Mathf.PI * 2, r = 0.5f + Rnd() * 3.5f;
                    const float sw = 2.2f;
                    float shade = Rnd();
                    var c0 = Color.Lerp(pink, Color.white, shade * 0.6f);
                    petalPool.Add(new Vector3(cc.x + Mathf.Cos(a) * r, cc.y + 0.3f + Rnd() * 3, cc.z - Mathf.Sin(a) * r),
                        new Vector3(-Mathf.Sin(a) * sw + (Rnd() - 0.5f), 0.6f + Rnd() * 0.8f, -(Mathf.Cos(a) * sw + (Rnd() - 0.5f))),
                        1.4f + Rnd() * 0.6f, c0, C(1, 0.75f, 0.85f), 1, 0.34f + Rnd() * 0.18f, 0.8f, 0.05f, 0.6f, 0.15f, 3);
                }
            });
            var allies = c.targets.Where(x => x.side == c.u.side).ToList();
            var gold = Art.Hex("#ffe0a0");
            foreach (var x in allies)
            {
                var p = SpPos(x);
                var vv = VisOf(x); if (vv != null) vv.Tint(gold, 1);
                SpRing(p, gold, 0.2f, 1.6f, 0.5f);
                Emit(glow, p + Up * 0.8f, 16, new EO { color = C(1, 0.88f, 0.6f), alpha = 1, life = 0.8f, speed = 1.4f, size = 0.22f, gravity = -0.3f, radius = 0.5f });
            }
            c.HitAll();
            SpGlyph(cc + Up * 3.4f, c.glyph, c.color, 4);
            yield return W(0.7f);
        }

        // ---- spirit 符咒（奇谋 / 混乱）：朱砂符纸从施展者袖中飞出，每支敌军三张绕身旋转，脚下暗紫漩涡，符燃中术
        sealed class Paper { public SpObj o; public BUnit x; public int j; public float ph, lift, side; }
        IEnumerator Sp_spirit(SpCtx c)
        {
            EnsurePools();
            var col = c.color;
            var deep = Art.Shade(col, -0.3f);
            var foes = c.targets.Where(x => x.side != c.u.side).ToList();
            var list = foes.Count > 0 ? foes : new List<BUnit> { c.target };
            var src = SpPos(c.u, 1.5f);
            if (c.v != null) c.v.Tint(col, 1);
            Sfx.Play("magic", 0.7f);
            SpRing(SpPos(c.u), col, 1.4f, 0.4f, 0.35f, new RO { alpha = 0.8f });
            foreach (var x in list) SpRing(SpPos(x), deep, 0.3f, 1.75f, 1.35f, new RO { tex = SwirlTex, spin = -8, hold = true, alpha = 0.85f, normal = true });
            var paperMat = AlphaMat(Color.white, TalismanTex);
            paperMat.renderQueue = 3022;
            ownMats.Add(paperMat);
            SetAlpha(paperMat, Color.white, 0);
            var papers = new List<Paper>();
            foreach (var x in list)
                for (int j = 0; j < 3; j++)
                {
                    var o = SpMake("FxPaper", QuadMesh, false, paperMat, false);
                    o.go.transform.position = src;
                    o.go.transform.localScale = new Vector3(0.34f, 0.82f, 1);
                    papers.Add(new Paper { o = o, x = x, j = j, ph = Rnd() * 6, lift = 0.8f + Rnd() * 1.2f, side = Rnd() - 0.5f });
                }
            // 1. 飞出：二次曲线弧线，纸片翻飞（scale.x 随正弦翻面），身后拖紫色光点
            yield return SpAnim(0.5f, t =>
            {
                var cam = FxCam();
                float op = 0;
                foreach (var p in papers)
                {
                    if (p.o.go == null) continue;
                    float k = Mathf.Clamp01(t * 1.3f - p.j * 0.12f), e = Ease(k);
                    var dst = SpPos(p.x, 1.1f);
                    var mid = Vector3.Lerp(src, dst, 0.5f);
                    mid.y += 1.4f + p.lift; mid.x += p.side * 1.6f; mid.z += p.side * 1.2f;
                    float a0 = (1 - e) * (1 - e), a1 = 2 * (1 - e) * e, a2 = e * e;
                    var tr = p.o.go.transform;
                    tr.position = src * a0 + mid * a1 + dst * a2;
                    tr.localScale = new Vector3(0.34f * Mathf.Cos(t * 16 + p.ph), 0.82f, 1);
                    if (cam != null) tr.rotation = cam.transform.rotation;
                    op = Mathf.Max(op, Mathf.Min(1, k * 5));
                    if (k > 0 && k < 1) Emit(glow, tr.position, 1, new EO { color = col, alpha = 0.75f, life = 0.3f, speed = 0.15f, size = 0.2f, sizeEnd = 0.04f, gravity = 0, radius = 0.03f });
                }
                SetAlpha(paperMat, Color.white, op);
            });
            // 2. 绕身：三张符纸绕敌军旋转、缓缓收紧上升
            Sfx.Play("magic", 0.4f);
            yield return SpAnim(0.55f, t =>
            {
                var cam = FxCam();
                foreach (var p in papers)
                {
                    if (p.o.go == null) continue;
                    var cc = SpPos(p.x);
                    float a = p.j * 2.094f + t * 10 + p.ph * 0.2f, r = 1.0f - t * 0.4f;
                    var tr = p.o.go.transform;
                    tr.position = new Vector3(cc.x + Mathf.Cos(a) * r, cc.y + 1.1f + t * 0.5f + Mathf.Sin(t * 7 + p.j * 2) * 0.12f, cc.z - Mathf.Sin(a) * r);
                    tr.localScale = new Vector3(0.34f * Mathf.Cos(t * 12 + p.ph), 0.82f, 1);
                    if (cam != null) tr.rotation = cam.transform.rotation;
                }
                foreach (var x in list) if (Rnd() < 0.6f) Emit(glow, SpPos(x, 0.3f + Rnd() * 1.2f), 1, new EO { color = col, color2 = Color.white, mix = 0.25f, alpha = 0.9f, life = 0.5f, speed = 0.5f, size = 0.24f, sizeEnd = 0.08f, gravity = -0.4f, radius = 0.5f });
            });
            // 3. 符燃：纸片化作紫焰与金屑，敌军中术
            foreach (var p in papers)
            {
                if (p.o.go != null) Emit(glow, p.o.go.transform.position, 10, new EO { color = col, color2 = C(1, 0.85f, 0.45f), mix = 0.45f, alpha = 1, life = 0.55f, speed = 1.8f, size = 0.26f, sizeEnd = 0.05f, gravity = -0.3f, radius = 0.12f, lifeVar = 0.3f });
                SpFree(p.o);
            }
            Destroy(paperMat);
            foreach (var x in list)
            {
                var vv = VisOf(x); if (vv != null) { vv.FlashHit(); vv.Tint(col, 1); }
                SpRing(SpPos(x), col, 0.4f, 1.5f, 0.45f, new RO { alpha = 0.9f });
            }
            SpFlash(Rgb(190, 140, 255), 0.16f, 0.3f);
            c.HitAll();
            SpGlyph(SpPos(c.target) + Up * 3.0f, c.glyph, col);
            yield return W(0.55f);
        }

        // ---- shield 护盾：每支受益部队升起六角光罩
        IEnumerator Sp_shield(SpCtx c)
        {
            var col = c.color;
            Sfx.Play("horn", 0.5f); Sfx.Play("duel", 0.5f);
            yield return SpCharge(c, 0.2f);
            var allies = c.targets.Where(x => x.side == c.u.side).ToList();
            if (allies.Count == 0) allies.Add(c.u);
            var domes = new List<SpObj>();
            foreach (var x in allies)
            {
                var mat = FxMat(4, col, true, false, true);
                var d = SpMake("FxDome", DomeMesh, false, mat, true, 3021);
                d.go.transform.position = SpPos(x, 0.05f);
                d.go.transform.localScale = Vector3.one * 0.0001f;
                domes.Add(d);
                SpRing(SpPos(x), col, 0.4f, 1.9f, 0.5f);
                var vv = VisOf(x); if (vv != null) vv.Tint(col, 0.9f);
            }
            yield return SpAnim(1.0f, k =>
            {
                float s = (k < 0.2f ? Ease(k / 0.2f) : 1) * 1.45f;
                foreach (var d in domes)
                {
                    if (d.go == null) continue;
                    d.go.transform.localScale = new Vector3(Mathf.Max(0.0001f, s), Mathf.Max(0.0001f, s * 0.95f), Mathf.Max(0.0001f, s));
                    d.mat.SetFloat("_Scan", (k * 1.6f) % 1.1f);
                    d.mat.SetFloat("_Opacity", k < 0.65f ? 1 : (1 - k) / 0.35f);
                }
                if (k > 0.15f && k < 0.2f) foreach (var x in allies) SpSparks(SpPos(x, 1.3f), Color.white, 10, 2, 0.15f);
            });
            foreach (var d in domes) SpFree(d);
            c.HitAll();
            SpGlyph(SpPos(c.u, 3.2f), c.glyph, col, 3.8f);
            yield return W(0.3f);
        }

        // ---- heal 治愈之光：柔和光柱与上升的十字光点
        IEnumerator Sp_heal(SpCtx c)
        {
            EnsurePools();
            var col = c.color;
            var area = SpAreaTiles(c, 1);
            SpTiles(area, col, 1.4f, 0.4f);
            Sfx.Play("magic", 0.6f);
            if (c.v != null) c.v.Tint(col, 0.8f);
            // 施展者撒出一道药光飞向目标
            Vector3 a = SpPos(c.u, 1), b = SpPos(c.target, 1);
            if (c.target != c.u)
            {
                var mid = Vector3.Lerp(a, b, 0.5f); mid.y += 2.2f;
                yield return SpTrail(k => Bez(a, mid, b, k), 0.35f, 4, new EO { color = col, color2 = Color.white, mix = 0.4f, alpha = 1, life = 0.45f, speed = 0.3f, size = 0.32f, sizeEnd = 0.1f, gravity = 0, radius = 0.08f, head = 0.9f });
            }
            var allies = c.targets.Where(x => x.side == c.u.side).ToList();
            if (allies.Count == 0) allies.Add(c.target);
            foreach (var x in allies)
            {
                var p = SpPos(x);
                SpPillar(p, col, 0.7f, 4, 1.0f, 0.6f);
                SpRing(p, col, 0.3f, 1.7f, 0.7f);
                var vv = VisOf(x); if (vv != null) vv.Tint(col, 1);
                Emitter(glow, p + Up * 0.2f, 18, new EO { color = col, color2 = Color.white, mix = 0.5f, alpha = 1, life = 1.0f, speed = 1.3f, size = 0.3f, sizeEnd = 0.5f, gravity = -0.15f, box = new Vector3(1.2f, 0.2f, 1.2f), jitter = 0.15f, lifeVar = 0.3f }, 0.8f);
            }
            SpLight(b, col, 5, 0.8f);
            c.HitAll();
            SpGlyph(b + Up * 2.4f, c.glyph, col);
            yield return W(0.95f);
        }

        // ---- poison 毒雾：毒瓶抛出，绿雾翻滚
        IEnumerator Sp_poison(SpCtx c)
        {
            EnsurePools();
            var area = SpAreaTiles(c, 1);
            var col = c.color;
            Vector3 a = SpPos(c.u, 1.1f), b = SpPos(c.target, 0.4f);
            var mid = Vector3.Lerp(a, b, 0.5f); mid.y += 3.2f;
            yield return SpCharge(c, 0.2f);
            yield return SpTrail(k => Bez(a, mid, b, k), 0.4f, 3, new EO { color = col, alpha = 1, life = 0.4f, speed = 0.2f, size = 0.3f, sizeEnd = 0.1f, gravity = 0, radius = 0.05f, head = 0.8f, headColor = C(0.75f, 1, 0.5f) });
            Sfx.Play("fire", 0.4f);
            SpTiles(area, col, 1.3f, 0.5f);
            SpRing(SpPos(c.target), col, 0.4f, 3.4f, 0.7f);
            foreach (var tl in area)
            {
                var p = Tile(tl.x, tl.y);
                Emitter(dust, p + Up * 0.4f, 14, new EO
                {
                    color = C(0.42f, 0.75f, 0.25f), color2 = C(0.5f, 0.32f, 0.6f), mix = 0.3f, colorEnd = C(0.25f, 0.4f, 0.18f), alpha = 0.55f, life = 1.3f, speed = 0.7f, size = 1.2f, sizeEnd = 2.6f, gravity = -0.02f,
                    box = new Vector3(1.6f, 0.3f, 1.6f), jitter = 0.5f, lifeVar = 0.3f, drag = 0.6f, fadeIn = 0.15f,
                }, 0.9f);
                Emitter(glow, p + Up * 0.2f, 10, new EO { color = C(0.7f, 1, 0.4f), alpha = 0.9f, life = 0.8f, speed = 1.0f, size = 0.16f, sizeEnd = 0.4f, gravity = -0.2f, box = new Vector3(1.4f, 0.2f, 1.4f), jitter = 0.1f }, 0.9f);
            }
            foreach (var x in c.targets) { if (x.side == c.u.side) continue; var vv = VisOf(x); if (vv != null) { vv.FlashHit(); vv.Tint(col, 1); } }
            c.HitAll();
            SpGlyph(SpPos(c.target, 3.0f), c.glyph, col);
            yield return W(0.85f);
        }

        // ---- shadow 暗影刺杀：施展者化作黑烟，一道暗光掠过，血色十字
        IEnumerator Sp_shadow(SpCtx c)
        {
            EnsurePools();
            var v = c.v; var t = c.target; var res = c.res;
            Vector3 a = SpPos(c.u, 0.6f), b = SpPos(t, 0.8f);
            SpDim(0.45f, 1.3f);
            Sfx.Play("magic", 0.5f);
            var dark = C(0.12f, 0.08f, 0.16f);
            Emit(dust, a, 26, new EO { color = dark, colorEnd = C(0.25f, 0.2f, 0.3f), alpha = 0.8f, life = 0.9f, speed = 1.4f, size = 0.9f, sizeEnd = 2.0f, gravity = -0.05f, radius = 0.6f, drag = 1.6f });
            if (v != null) v.SetHidden(true);
            bool killed = res != null && res.killed == t;
            var blood = Art.Hex("#ff2a4a");
            try
            {
                yield return W(0.18f);
                yield return SpTrail(k => Vector3.Lerp(a, b, k), 0.16f, 5, new EO { color = c.color, alpha = 1, life = 0.3f, speed = 0.2f, size = 0.45f, sizeEnd = 0.1f, gravity = 0, radius = 0.1f, head = 1.0f, headColor = C(1, 0.5f, 0.7f), linear = true });
                Sfx.Play("hit", 1);
                SpArc(b, blood, new AO { radius = 1.2f, width = 0.3f, tilt = -0.8f, sweep = 2.2f, start = -0.1f, dur = 0.35f, len = 0.5f });
                SpArc(b, blood, new AO { radius = 1.2f, width = 0.3f, tilt = 0.8f, sweep = 2.2f, start = 0.9f, dur = 0.35f, len = 0.5f });
                if (killed)
                {
                    Emit(dust, SpPos(t, 0.6f), 30, new EO { color = C(0.15f, 0.05f, 0.08f), colorEnd = C(0.3f, 0.2f, 0.25f), alpha = 0.8f, life = 1.2f, speed = 2, size = 1, sizeEnd = 2.4f, gravity = -0.05f, radius = 0.5f, drag = 1.4f });
                    SpFlash(Rgb(255, 20, 40), 0.35f, 0.4f);
                    SpShake(0.35f, 0.45f);
                }
                else
                {
                    SpSparks(b, Color.white, 18, 3, 0.16f);
                    SpShake(0.15f, 0.2f);
                }
                var tv = VisOf(t); if (tv != null) tv.FlashHit();
                SpRing(SpPos(t), c.color, 0.3f, 2.2f, 0.45f);
                c.HitAll();
                yield return W(0.22f);
            }
            finally
            {
                if (v != null && this != null)
                {
                    v.SetHidden(false);
                    SpGhost(v, c.color, 0.4f, 0.6f);
                    Emit(dust, a, 14, new EO { color = dark, alpha = 0.7f, life = 0.6f, speed = 1, size = 0.7f, sizeEnd = 1.6f, gravity = 0, radius = 0.5f, drag = 2 });
                }
            }
            SpGlyph(SpPos(t, 3.0f), c.glyph, killed ? Art.Hex("#ff3355") : c.color, killed ? 4 : 3.2f);
            yield return W(0.5f);
        }

        // ---- drain 吸魂：重击后，敌军的魂光汇入施展者
        IEnumerator Sp_drain(SpCtx c)
        {
            var t = c.target;
            var col = c.color;
            yield return SpCharge(c, 0.2f);
            yield return SpLunge(c, 0.45f, 0.13f);
            var b = SpPos(t, 0.9f);
            Sfx.Play("hit", 0.9f);
            SpArc(b, col, new AO { radius = 1.3f, width = 0.5f, tilt = -0.7f, sweep = 2.4f, start = -0.2f, dur = 0.4f });
            SpImpact(t, col, true);
            SpShake(0.2f, 0.25f);
            c.HitAll();
            yield return W(0.15f);
            var a = SpPos(c.u, 0.9f);
            Sfx.Play("magic", 0.6f);
            for (int s = 0; s < 3; s++)
            {
                var mid = Vector3.Lerp(a, b, 0.5f); mid.y += 1.5f + s * 0.6f; mid.x += (s - 1) * 1.2f;
                SpRun(SpTrail(k => Bez(b, mid, a, k), 0.45f, 3, new EO { color = col, color2 = C(1, 0.85f, 0.9f), mix = 0.4f, alpha = 1, life = 0.4f, speed = 0.2f, size = 0.32f, sizeEnd = 0.1f, gravity = 0, radius = 0.06f, head = 0.7f }));
            }
            yield return W(0.45f);
            if (c.v != null) c.v.Tint(col, 1);
            SpRing(SpPos(c.u), col, 1.8f, 0.4f, 0.35f);
            SpPillar(SpPos(c.u), col, 0.6f, 3, 0.6f, 0.6f);
            SpGlyph(SpPos(c.u, 3.0f), c.glyph, col);
            yield return W(0.4f);
        }

        // ---- haste 疾风：脚下青色旋风；受令友军身上风痕沿前进方向疾掠而过，身后拖出三重残影
        sealed class Streak { public SpObj o; public Material mat; public Color col; public BUnit x; public Vector3 d, side; public float t0, off, h; }
        IEnumerator Sp_haste(SpCtx c)
        {
            EnsurePools();
            var cc = SpPos(c.u);
            var col = c.color;
            Sfx.Play("march", 0.6f); Sfx.Play("magic", 0.4f);
            float R = c.Radius(2) * T + 0.6f;
            SpRing(cc, col, 0.5f, R, 0.7f);
            SpRing(cc, col, 0.4f, 2.2f, 1.0f, new RO { tex = SwirlTex, spin = 10, hold = true, alpha = 0.75f });
            if (c.v != null) c.v.Tint(col, 1);
            var allies = c.targets.Where(x => x.side == c.u.side && x != c.u).ToList();
            var list = allies.Count > 0 ? allies : new List<BUnit> { c.u };
            // 前进方向：朝最近的敌军（没有则朝对方阵地）
            Func<BUnit, Vector3> dirOf = x =>
            {
                BUnit best = null; int bd = int.MaxValue;
                foreach (var e in M.units) if (e.alive && e.side != x.side) { int d0 = Mathf.Abs(e.x - x.x) + Mathf.Abs(e.y - x.y); if (d0 < bd) { bd = d0; best = e; } }
                var p = SpPos(x);
                var d = best != null ? SpPos(best) - p : new Vector3(x.side == 0 ? 1 : -1, 0, 0);
                d.y = 0;
                if (d.sqrMagnitude < 1e-6f) d = Vector3.right;
                return d.normalized;
            };
            // 风痕：细长的发光带（平躺、长轴沿前进方向），从身后掠到身前
            var mb = new MeshBuilder();
            mb.FlatQuad(new Vector3(-0.75f, 0, -0.035f), new Vector3(-0.75f, 0, 0.035f), new Vector3(0.75f, 0, 0.035f), new Vector3(0.75f, 0, -0.035f), Color.white);
            var streakMesh = mb.ToMesh("fxStreak");
            ownMeshes.Add(streakMesh);
            var streaks = new List<Streak>();
            var dirs = new Dictionary<BUnit, Vector3>();
            foreach (var x in list)
            {
                var d = dirOf(x);
                var side = new Vector3(-d.z, 0, d.x);
                var q = Quaternion.LookRotation(d) * Quaternion.Euler(0, -90, 0);
                for (int i = 0; i < 9; i++)
                {
                    var sc = i % 3 == 0 ? Color.white : col;
                    var mat = AddMat(sc, StreakTex);
                    SetAlpha(mat, sc, 0);
                    var o = SpMake("FxStreak", streakMesh, false, mat, true, 3020);
                    o.go.transform.rotation = q;
                    streaks.Add(new Streak { o = o, mat = mat, col = sc, x = x, d = d, side = side, t0 = i * 0.07f + Rnd() * 0.04f, off = (Rnd() - 0.5f) * 1.5f, h = 0.25f + Rnd() * 1.3f });
                }
                dirs[x] = d;
            }
            bool ghosted = false;
            yield return SpAnim(0.95f, k =>
            {
                float t = k * 0.95f;
                foreach (var s in streaks)
                {
                    if (s.o.go == null) continue;
                    float u = Mathf.Clamp01((t - s.t0) / 0.3f);
                    var p = SpPos(s.x);
                    var pos = p + s.d * (-2.0f + u * 4.0f) + s.side * s.off;
                    pos.y = p.y + s.h;
                    s.o.go.transform.position = pos;
                    SetAlpha(s.mat, s.col, u <= 0 || u >= 1 ? 0 : Mathf.Sin(u * Mathf.PI) * 0.95f);
                }
                if (!ghosted && t > 0.25f)
                {
                    ghosted = true;
                    foreach (var x in list)
                    {
                        var vv = VisOf(x);
                        if (vv != null) { vv.Tint(col, 1); for (int j = 1; j <= 3; j++) SpGhostAt(vv, col, 0.6f, 0.5f - j * 0.12f, dirs[x] * (-0.45f * j)); }
                        SpRing(SpPos(x), col, 0.3f, 1.8f, 0.45f);
                        SpDust(SpPos(x, 0.15f), 6, 2.4f);
                    }
                }
            });
            foreach (var s in streaks) SpFree(s.o);
            Destroy(streakMesh);
            c.HitAll();
            SpGlyph(cc + Up * 3.1f, c.glyph, col);
            yield return W(0.45f);
        }

        // ---- claw 猛虎爪痕：三道平行爪痕撕裂敌阵；以自身为中心的招式（威吓）则兽影扑向周围每支敌军
        void SpClawMarks(Vector3 pos, Color col, float s = 1)
        {
            float R = 2.6f * s, sweep = 0.9f, tilt = -0.65f + (Rnd() - 0.5f) * 0.35f;
            float start = Mathf.PI / 2 - sweep / 2;
            for (int i = 0; i < 3; i++)
            {
                float r = R + (i - 1) * 0.4f * s;
                SpLater(i * 0.045f, () =>
                {
                    SpArc(pos, col, new AO { radius = r, width = 0.42f * s, start = start, sweep = sweep, tilt = tilt, mid = R, dur = 0.6f, len = 1.0f, stay = true });
                    SpArc(pos, Color.white, new AO { radius = r + 0.07f * s, width = 0.14f * s, start = start, sweep = sweep, tilt = tilt, mid = R, dur = 0.45f, len = 1.0f, stay = true });
                });
            }
        }
        IEnumerator Sp_claw(SpCtx c)
        {
            var col = c.color;
            bool selfCentered = c.target == null || c.target == c.u;
            if (selfCentered)
            {
                var cc = SpPos(c.u);
                float rad = c.res != null && c.res.sp != null ? (c.res.sp.radius != 0 ? (float)c.res.sp.radius : 1) : 1;
                float R = rad * T + 1;
                yield return SpCharge(c, 0.3f);
                Sfx.Play("horn", 0.8f); Sfx.Play("rock", 0.5f);
                SpRing(cc, col, 0.6f, R, 0.55f);
                SpRing(cc, Color.white, 0.4f, R * 0.7f, 0.4f);
                SpShake(0.35f, 0.5f);
                SpDust(SpPos(c.u, 0.2f), 18, 3.5f);
                var foes = c.targets.Where(x => x.side != c.u.side).ToList();
                foreach (var x in foes)
                {
                    Vector3 a = SpPos(c.u, 0.7f), b = SpPos(x, 0.8f);
                    var mid = Vector3.Lerp(a, b, 0.5f); mid.y += 1.2f;
                    yield return SpTrail(k => Bez(a, mid, b, k), 0.15f, 5, new EO { color = col, color2 = Color.white, mix = 0.3f, alpha = 1, life = 0.35f, speed = 0.4f, size = 0.42f, sizeEnd = 0.1f, gravity = 0, radius = 0.12f, head = 1.6f });
                    Sfx.Play("hit", 0.7f);
                    SpClawMarks(SpPos(x, 0.9f), col, 0.8f);
                    SpImpact(x, col, false);
                    c.HitUnit(x);
                }
                c.HitAll();
                SpGlyph(SpPos(c.u, 3.1f), c.glyph, col, 4);
                yield return W(0.6f);
                yield break;
            }
            var t = c.target;
            yield return SpCharge(c, 0.26f);
            Sfx.Play("horn", 0.5f);
            yield return SpLunge(c, 0.55f, 0.13f);
            var p = SpPos(t, 0.9f);
            Sfx.Play("hit", 1); Sfx.Play("rock", 0.4f);
            SpClawMarks(p, col, 1.15f);
            yield return W(0.1f);
            SpImpact(t, col, true);
            SpRing(SpPos(t), col, 0.4f, 3.0f, 0.55f);
            SpLight(p, col, 7, 0.3f);
            SpShake(0.3f, 0.4f);
            SpFlash(Rgb(255, 190, 120), 0.2f, 0.22f);
            c.HitAll();
            SpGlyph(SpPos(t, 3.2f), c.glyph, col, 4);
            yield return W(0.6f);
        }

        // ---- rock 落石：巨石自山头滚落，砸进范围内各格，尘土飞扬
        sealed class Rock { public SpObj o; public Vector3 from, land, spin; public Vector2Int tile; public float delay, dur; public bool landed; }
        IEnumerator Sp_rock(SpCtx c)
        {
            EnsurePools();
            var area = SpAreaTiles(c, 1);
            var col = c.color;
            SpTiles(area, col, 1.6f, 0.45f);
            yield return SpCharge(c, 0.22f);
            Sfx.Play("rock", 0.5f);
            var rocks = new List<Rock>();
            var main = c.target != null ? new Vector2Int(c.target.x, c.target.y) : area[0];
            var tiles = new List<Vector2Int> { main };
            tiles.AddRange(area.Where(q => q.x != main.x || q.y != main.y));
            int n = Mathf.Clamp(tiles.Count * 2 + 2, 6, 16);
            for (int i = 0; i < n; i++)
            {
                var tl = tiles[i < 3 ? 0 : (i - 2) % tiles.Count];
                var g = Tile(tl.x, tl.y);
                float r = (i < 3 ? 0.42f : 0.28f) + Rnd() * 0.22f;
                var mb = new MeshBuilder();
                float sh = 0.45f + Rnd() * 0.15f;
                mb.Blob(Vector3.zero, new Vector3(r, r * 0.85f, r), C(sh * 1.05f, sh, sh * 0.9f), i * 7 + 3);
                var o = SpMake("FxRock", mb.ToMesh("fxRock"), true, Art.LowPoly, false, -1, true);
                var land = g + new Vector3((Rnd() - 0.5f) * 1.3f, r * 0.6f, (Rnd() - 0.5f) * 1.3f);
                // 自镜头一侧的高处斜落
                var from = land + new Vector3(-1.5f + Rnd() * 3, 8 + Rnd() * 2, -2 - Rnd() * 1.5f);
                o.go.transform.position = from;
                o.go.SetActive(false);
                rocks.Add(new Rock { o = o, from = from, land = land, tile = tl, delay = i * 0.045f + (i < 3 ? 0 : 0.12f), dur = 0.42f + Rnd() * 0.08f, spin = new Vector3(Rnd() * 9, Rnd() * 6, Rnd() * 9) });
            }
            var hitTiles = new HashSet<int>();
            yield return SpAnim(1.15f, k =>
            {
                float el = k * 1.15f;
                foreach (var rk in rocks)
                {
                    if (rk.o.go == null) continue;
                    float q = (el - rk.delay) / rk.dur;
                    if (q <= 0) continue;
                    rk.o.go.SetActive(true);
                    var tr = rk.o.go.transform;
                    if (q < 1)
                    {
                        float e = q * q;   // 自由落体：加速
                        tr.position = Vector3.Lerp(rk.from, rk.land, e);
                        tr.rotation = Quaternion.Euler(rk.spin * q * Mathf.Rad2Deg);
                        if (Rnd() < 0.6f) Emit(dust, tr.position, 1, new EO { color = C(0.55f, 0.5f, 0.44f), alpha = 0.35f, life = 0.4f, speed = 0.1f, size = 0.5f, sizeEnd = 1.1f, gravity = 0, radius = 0.1f });
                    }
                    else if (!rk.landed)
                    {
                        rk.landed = true;
                        tr.position = rk.land;
                        SpRockImpact(rk.land);
                        SpDust(rk.land, 6, 2.4f);
                        Emit(glow, rk.land, 6, new EO { color = col, color2 = C(1, 0.9f, 0.7f), mix = 0.5f, alpha = 1, life = 0.35f, speed = 3, size = 0.16f, gravity = 0.8f, radius = 0.15f });
                        SpShake(0.16f, 0.18f);
                        int key = rk.tile.x * 1000 + rk.tile.y;
                        if (!hitTiles.Contains(key))
                        {
                            hitTiles.Add(key);
                            Sfx.Play("rock", 0.6f);
                            SpRing(Tile(rk.tile.x, rk.tile.y), col, 0.3f, 2.0f, 0.4f);
                            foreach (var h in c.hits) if (h.unit.x == rk.tile.x && h.unit.y == rk.tile.y) { var vv = VisOf(h.unit); if (vv != null) vv.FlashHit(); c.HitUnit(h.unit); }
                        }
                    }
                    else
                    {
                        // 落地后略微下沉、渐隐
                        float s = Mathf.Clamp01(1 - (q - 1) * 1.6f);
                        tr.localScale = Vector3.one * Mathf.Max(0.001f, s);
                    }
                }
            });
            foreach (var rk in rocks) SpFree(rk.o);
            var mp = c.target != null ? SpPos(c.target) : Tile(main.x, main.y);
            Smoke(mp);
            c.HitAll();
            SpGlyph(mp + Up * 3.0f, c.glyph, col);
            yield return W(0.4f);
        }

        // ======================================================== 网格与贴图 --
        Mesh quadMesh, domeMesh;
        Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null) return quadMesh;
                quadMesh = new Mesh { name = "fxQuad" };
                quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
                quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quadMesh.RecalculateNormals(); quadMesh.RecalculateBounds();
                ownMeshes.Add(quadMesh);
                return quadMesh;
            }
        }
        // 半球（网页版 SphereGeometry(1, 28, 14, 0, 2π, 0, π/2)），法线朝外，三角形正面朝外
        Mesh DomeMesh
        {
            get
            {
                if (domeMesh != null) return domeMesh;
                const int WS = 28, HS = 14;
                var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int iy = 0; iy <= HS; iy++)
                {
                    float th = (float)iy / HS * Mathf.PI / 2;
                    for (int ix = 0; ix <= WS; ix++)
                    {
                        float ph = (float)ix / WS * Mathf.PI * 2;
                        var p = new Vector3(-Mathf.Cos(ph) * Mathf.Sin(th), Mathf.Cos(th), Mathf.Sin(ph) * Mathf.Sin(th));
                        vs.Add(p); ns.Add(p.normalized); uv.Add(new Vector2((float)ix / WS, 1 - (float)iy / HS));
                    }
                }
                Action<int, int, int> add = (a, b, cc) =>
                {
                    var n = Vector3.Cross(vs[b] - vs[a], vs[cc] - vs[a]);
                    if (n.sqrMagnitude < 1e-12f) return;
                    if (Vector3.Dot(n, vs[a] + vs[b] + vs[cc]) < 0) { tri.Add(a); tri.Add(cc); tri.Add(b); }
                    else { tri.Add(a); tri.Add(b); tri.Add(cc); }
                };
                for (int iy = 0; iy < HS; iy++)
                    for (int ix = 0; ix < WS; ix++)
                    {
                        int a = iy * (WS + 1) + ix, b = a + 1, d = a + WS + 1, cc = d + 1;
                        add(a, d, b); add(b, d, cc);
                    }
                domeMesh = new Mesh { name = "fxDome" };
                domeMesh.SetVertices(vs); domeMesh.SetNormals(ns); domeMesh.SetUVs(0, uv); domeMesh.SetTriangles(tri, 0);
                domeMesh.RecalculateBounds();
                ownMeshes.Add(domeMesh);
                return domeMesh;
            }
        }

        static Texture2D tileTex, flameTex, dustTex, petalTex, swirlTex, circleTex, streakTex, talismanTex;
        static Sprite dimSprite;
        static float Sst(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        static Texture2D AlphaTex(int w, int h, Func<int, int, float> alpha, bool mip = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mip) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255));
            t.SetPixels32(px);
            t.Apply(mip, false);
            t.hideFlags = HideFlags.DontSave;
            return t;
        }
        // 移动 / 攻击范围的格子贴图：圆角方块，内部半透明、边缘发亮、外缘柔光
        static Texture2D TileTex
        {
            get
            {
                if (tileTex != null) return tileTex;
                const float half = 0.84f, rad = 0.24f;
                tileTex = AlphaTex(64, 64, (x, y) =>
                {
                    float u = (x + 0.5f) / 64 * 2 - 1, v = (y + 0.5f) / 64 * 2 - 1;
                    float qx = Mathf.Abs(u) - (half - rad), qy = Mathf.Abs(v) - (half - rad);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
                    float a = d < 0 ? 0.62f + 0.3f * Mathf.SmoothStep(0, 1, 1 + d / 0.35f) : 0.75f * Mathf.Exp(-d / 0.05f);
                    return Mathf.Max(a, Mathf.Exp(-(d / 0.04f) * (d / 0.04f)));
                });
                return tileTex;
            }
        }
        // 火舌贴图：上尖下圆的水滴形（网页版第 0 行 = 点精灵顶部；Unity 第 0 行在下，故翻转）
        static Texture2D FlameTex
        {
            get
            {
                if (flameTex != null) return flameTex;
                const float yc = 0.63f, r = 0.31f, yt = 0.02f;
                flameTex = AlphaTex(64, 64, (px, py) =>
                {
                    float x = (px + 0.5f) / 64 - 0.5f, y = (63 - py + 0.5f) / 64;
                    float d;
                    if (y >= yc) d = Mathf.Sqrt(x * x + (y - yc) * 1.08f * (y - yc) * 1.08f) / r;
                    else if (y > yt) d = Mathf.Abs(x) / Mathf.Max(1e-4f, r * Mathf.Pow((y - yt) / (yc - yt), 0.8f));
                    else d = 9;
                    float a = 1 - Sst(0.42f, 1, d);
                    return a * Sst(yt, yt + 0.32f, y);
                });
                return flameTex;
            }
        }
        // 烟尘：柔光点的 alpha^0.6（网页版 uSoft = 0.6）
        static Texture2D DustTex
        {
            get
            {
                if (dustTex != null) return dustTex;
                dustTex = AlphaTex(64, 64, (x, y) =>
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) / 32;
                    float a = Mathf.Clamp01(1 - d);
                    return Mathf.Pow(a * a, 0.6f);
                });
                return dustTex;
            }
        }
        // 风痕：两端透明、中段明亮的细长光带
        static Texture2D StreakTex
        {
            get
            {
                if (streakTex != null) return streakTex;
                streakTex = AlphaTex(128, 16, (x, y) =>
                {
                    if (y < 3 || y >= 13) return 0;
                    float u = (x + 0.5f) / 128;
                    return u < 0.7f ? 0.9f * u / 0.7f : 0.9f * (1 - u) / 0.3f;
                });
                return streakTex;
            }
        }
        static Texture2D FromRaster(Raster r) { var t = r.ToTexture(true); t.hideFlags = HideFlags.DontSave; return t; }
        // 花瓣
        static Texture2D PetalTex
        {
            get
            {
                if (petalTex != null) return petalTex;
                var r = new Raster(64, 64);
                r.FillColor = "#ffffff";
                r.BeginPath(); r.MoveTo(32, 6); r.BezierCurveTo(56, 18, 54, 46, 32, 58); r.BezierCurveTo(10, 46, 8, 18, 32, 6); r.Fill();
                // 顶端的小缺口（destination-out 三角形）：三角形内清零
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        if (py < 4 || py > 14) continue;
                        float half = (py - 4) / 10 * 5;
                        if (Mathf.Abs(px - 32) <= half) { int i = (y * 64 + x) * 4; r.Px[i] = r.Px[i + 1] = r.Px[i + 2] = r.Px[i + 3] = 0; }
                    }
                petalTex = FromRaster(r);
                return petalTex;
            }
        }
        // 漩涡：三条渐粗的螺旋臂（外层一圈淡光代替网页版的 shadowBlur）
        static Texture2D SwirlTex
        {
            get
            {
                if (swirlTex != null) return swirlTex;
                var r = new Raster(256, 256);
                const float c = 128;
                r.StrokeColor = "#ffffff"; r.LineCap = LineCap.Round;
                for (int pass = 0; pass < 2; pass++)
                    for (int arm = 0; arm < 3; arm++)
                        for (int i = 0; i < 48; i++)
                        {
                            float t0 = i / 48f, t1 = (i + 1) / 48f;
                            float a0 = arm * 2.094f + t0 * 5.2f, r0 = 10 + t0 * 112, a1 = arm * 2.094f + t1 * 5.2f, r1 = 10 + t1 * 112;
                            r.LineWidth = 2 + t0 * 9 + (pass == 0 ? 8 : 0);
                            r.GlobalAlpha = (0.35f + t0 * 0.65f) * (pass == 0 ? 0.22f : 1);
                            r.BeginPath(); r.MoveTo(c + Mathf.Cos(a0) * r0, c + Mathf.Sin(a0) * r0); r.LineTo(c + Mathf.Cos(a1) * r1, c + Mathf.Sin(a1) * r1); r.Stroke();
                        }
                swirlTex = FromRaster(r);
                return swirlTex;
            }
        }
        // 符阵：同心圆 + 八卦短划 + 星形
        static Texture2D CircleTex
        {
            get
            {
                if (circleTex != null) return circleTex;
                var r = new Raster(512, 512);
                const double c = 256;
                r.StrokeColor = "#ffffff";
                for (int pass = 0; pass < 2; pass++)
                {
                    float glowW = pass == 0 ? 8 : 0;
                    r.GlobalAlpha = pass == 0 ? 0.22f : 1;
                    Action<double, double> ring = (rr, lw) => { r.LineWidth = lw + glowW; r.BeginPath(); r.Arc(c, c, rr, 0, Math.PI * 2); r.Stroke(); };
                    ring(240, 6); ring(222, 2.5); ring(150, 4); ring(92, 2.5);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i / 8.0 * Math.PI * 2;
                        for (int k = 0; k < 3; k++)
                        {
                            double r0 = 168 + k * 16; bool broken = ((i >> k) & 1) == 1;
                            double half = broken ? 0.07 : 0.16;
                            r.LineWidth = 7 + glowW;
                            r.BeginPath(); r.Arc(c, c, r0, a - 0.16, a - 0.16 + (broken ? half : half * 2)); r.Stroke();
                            if (broken) { r.BeginPath(); r.Arc(c, c, r0, a + 0.16 - half, a + 0.16); r.Stroke(); }
                        }
                    }
                    r.LineWidth = 3 + glowW;
                    r.BeginPath();
                    for (int i = 0; i <= 5; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI * 4 / 5;
                        double x = c + Math.Cos(a) * 140, y = c + Math.Sin(a) * 140;
                        if (i == 0) r.MoveTo(x, y); else r.LineTo(x, y);
                    }
                    r.Stroke();
                }
                circleTex = FromRaster(r);
                return circleTex;
            }
        }
        // 符纸：黄纸朱框，竖写「敕令」（以朱砂笔画示意），下接曲折符脚
        static Texture2D TalismanTex
        {
            get
            {
                if (talismanTex != null) return talismanTex;
                const int w = 64, h = 160;
                var r = new Raster(w, h);
                r.FillColor = "#f2df9a"; r.FillRect(4, 2, w - 8, h - 4);
                r.StrokeColor = "#c3261c"; r.LineWidth = 3; r.BeginPath(); r.Rect(9, 7, w - 18, h - 14); r.Stroke();
                r.LineCap = LineCap.Round; r.LineJoin = LineJoin.Round;
                Action<double[]> stroke = p => { r.BeginPath(); r.MoveTo(p[0], p[1]); for (int i = 2; i + 1 < p.Length; i += 2) r.LineTo(p[i], p[i + 1]); r.Stroke(); };
                r.LineWidth = 3.2;
                // 敕（约 32, 32）：左「束」右「攵」
                stroke(new double[] { 18, 22, 32, 22 }); stroke(new double[] { 25, 16, 25, 46 }); stroke(new double[] { 19, 29, 31, 29, 31, 36, 19, 36, 19, 29 });
                stroke(new double[] { 25, 38, 17, 45 }); stroke(new double[] { 25, 38, 32, 44 });
                stroke(new double[] { 39, 16, 36, 27 }); stroke(new double[] { 37, 23, 47, 23 }); stroke(new double[] { 45, 25, 36, 45 }); stroke(new double[] { 38, 30, 47, 45 });
                // 令（约 32, 68）
                stroke(new double[] { 32, 53, 17, 66 }); stroke(new double[] { 32, 53, 47, 66 }); stroke(new double[] { 30, 62, 34, 64 });
                stroke(new double[] { 22, 70, 41, 70, 41, 78, 35, 82 }); stroke(new double[] { 29, 70, 29, 86 });
                // 符脚
                r.LineWidth = 3.5;
                r.BeginPath(); r.MoveTo(w / 2.0, 90);
                for (int i = 0; i < 6; i++) r.LineTo(w / 2.0 + (i % 2 == 1 ? 10 : -10), 96 + i * 9);
                r.Stroke();
                talismanTex = FromRaster(r);
                return talismanTex;
            }
        }
        // 压暗：椭圆径向渐变（中心 50% 45%）rgba(10,12,30,.55) → rgba(4,4,12,.92)
        static Sprite DimSprite
        {
            get
            {
                if (dimSprite != null) return dimSprite;
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                float dmax = Mathf.Sqrt(0.5f * 0.5f + 0.55f * 0.55f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                        float d = Mathf.Clamp01(Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.55f) * (v - 0.55f)) / dmax);
                        px[y * n + x] = Color.Lerp(new Color(10 / 255f, 12 / 255f, 30 / 255f, 0.55f), new Color(4 / 255f, 4 / 255f, 12 / 255f, 0.92f), d);
                    }
                t.SetPixels(px); t.Apply(false, false);
                t.hideFlags = HideFlags.DontSave;
                dimSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return dimSprite;
            }
        }
    }
}
