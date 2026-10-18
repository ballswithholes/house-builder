// 地图视图无头测试用的 UnityEngine 补充替身（与 Tests~/sim/UnityStub.cs 一起编译；run.sh 把那份替身里的
// Vector2 / Vector3 / Color / Debug / Application / Time 改成 partial 后再编译）。只实现 MapView / CultureArt / Art / Raster 用到的成员：
// 网格只保存数组，GameObject / Transform 维护父子层级与 TRS，纹理保存像素（测试读回比对），
// 镜头视锥（GeometryUtility）按 Unity 的定义计算（地形分块的选择依赖它）；渲染、物理、着色器均为空实现。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public partial struct Vector2
    {
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 right => new Vector2(1, 0);
    }
    public partial struct Vector3
    {
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public static Vector3 Normalize(Vector3 v) => v.normalized;
        public float this[int i] { get { return i == 0 ? x : i == 1 ? y : z; } set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public void Normalize() { this = normalized; }
        public void Set(float a, float b, float c) { x = a; y = b; z = c; }
    }
    public partial struct Color
    {
        public static Color grey => new Color(0.5f, 0.5f, 0.5f);
        public static Color cyan => new Color(0, 1, 1);
        public static Color magenta => new Color(1, 0, 1);
        public float maxColorComponent => Math.Max(r, Math.Max(g, b));
        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        public static Color operator /(Color c, float k) => new Color(c.r / k, c.g / k, c.b / k, c.a / k);
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static Color LerpUnclamped(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        public static void RGBToHSV(Color c, out float h, out float s, out float v)
        {
            float mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b)), d = mx - mn;
            v = mx; s = mx > 0 ? d / mx : 0;
            if (d <= 0) { h = 0; return; }
            if (mx == c.r) h = ((c.g - c.b) / d) / 6f; else if (mx == c.g) h = (2 + (c.b - c.r) / d) / 6f; else h = (4 + (c.r - c.g) / d) / 6f;
            if (h < 0) h += 1;
        }
        public static Color HSVToRGB(float h, float s, float v)
        {
            h = (h - (float)Math.Floor(h)) * 6; int i = (int)Math.Floor(h); float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            switch (i % 6) { case 0: return new Color(v, t, p); case 1: return new Color(q, v, p); case 2: return new Color(p, v, t); case 3: return new Color(p, q, v); case 4: return new Color(t, p, v); default: return new Color(v, p, q); }
        }
    }
    public static partial class Debug
    {
        public static void LogException(Exception e) => Console.WriteLine("EXCEPTION " + e);
    }
    public static partial class Application
    {
        public static bool isMobilePlatform => false;
        public static RuntimePlatform platform => RuntimePlatform.LinuxPlayer;
        public static int targetFrameRate = -1;
    }
    public enum RuntimePlatform { LinuxPlayer, WebGLPlayer, Android, IPhonePlayer, WindowsPlayer, OSXPlayer }
    public static partial class Time
    {
        public static float timeScale = 1;
        public static float smoothDeltaTime => deltaTime;
    }
    public static class SystemInfo
    {
        public static int processorCount => Environment.ProcessorCount;
        public static bool supportsInstancing => true;
        public static int maxTextureSize => 8192;
        public static string graphicsDeviceName => "headless";
    }
    public static class Screen
    {
        public static int width = 1600, height = 900;
        public static float dpi = 96;
        public static bool fullScreen;
        public static int resW, resH;
        public static void SetResolution(int w, int h, bool full) { resW = w; resH = h; }
    }
    public enum ShadowResolution { Low, Medium, High, VeryHigh }
    public enum ShadowQuality { Disable, HardOnly, All }
    public static class QualitySettings
    {
        public static ShadowResolution shadowResolution = ShadowResolution.High;
        public static ShadowQuality shadows = ShadowQuality.All;
        public static float shadowDistance = 150;
        public static int antiAliasing = 4;
        public static float lodBias = 1;
        public static int vSyncCount = 1;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static Vector4 one => new Vector4(1, 1, 1, 1);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
        public static Vector4 operator *(Vector4 a, float k) => new Vector4(a.x * k, a.y * k, a.z * k, a.w * k);
        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        // Unity：先绕 z、再绕 x、最后绕 y（ZXY），左手系
        public static Quaternion Euler(float ex, float ey, float ez)
        {
            float rx = ex * Mathf.Deg2Rad * 0.5f, ry = ey * Mathf.Deg2Rad * 0.5f, rz = ez * Mathf.Deg2Rad * 0.5f;
            var qx = new Quaternion((float)Math.Sin(rx), 0, 0, (float)Math.Cos(rx));
            var qy = new Quaternion(0, (float)Math.Sin(ry), 0, (float)Math.Cos(ry));
            var qz = new Quaternion(0, 0, (float)Math.Sin(rz), (float)Math.Cos(rz));
            return qy * qx * qz;
        }
        public static Quaternion Euler(Vector3 e) => Euler(e.x, e.y, e.z);
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            var a = axis.normalized; float h = deg * Mathf.Deg2Rad * 0.5f, s = (float)Math.Sin(h);
            return new Quaternion(a.x * s, a.y * s, a.z * s, (float)Math.Cos(h));
        }
        // 前向 f、上方 up 的朝向（与 Unity 相同的正交化）
        public static Quaternion LookRotation(Vector3 f, Vector3 up)
        {
            f = f.normalized; var r = Vector3.Cross(up, f).normalized; var u = Vector3.Cross(f, r);
            float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
            float tr = m00 + m11 + m22;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; return new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s); }
            if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; return new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; return new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; return new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
        }
        public static Quaternion LookRotation(Vector3 f) => LookRotation(f, Vector3.up);
        public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => t < 0.5f ? a : b;
        public Vector3 eulerAngles => Vector3.zero;
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float x2 = q.x * 2, y2 = q.y * 2, z2 = q.z * 2;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2, xy = q.x * y2, xz = q.x * z2, yz = q.y * z2, wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
            return new Vector3((1 - (yy + zz)) * v.x + (xy - wz) * v.y + (xz + wy) * v.z,
                               (xy + wz) * v.x + (1 - (xx + zz)) * v.y + (yz - wx) * v.z,
                               (xz - wy) * v.x + (yz + wx) * v.y + (1 - (xx + yy)) * v.z);
        }
    }

    public struct Matrix4x4
    {
        // 行主序 m[r, c]
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 identity => new Matrix4x4 { m00 = 1, m11 = 1, m22 = 1, m33 = 1 };
        public static Matrix4x4 TRS(Vector3 t, Quaternion q, Vector3 s)
        {
            var ex = q * new Vector3(s.x, 0, 0); var ey = q * new Vector3(0, s.y, 0); var ez = q * new Vector3(0, 0, s.z);
            return new Matrix4x4 { m00 = ex.x, m10 = ex.y, m20 = ex.z, m01 = ey.x, m11 = ey.y, m21 = ey.z, m02 = ez.x, m12 = ez.y, m22 = ez.z, m03 = t.x, m13 = t.y, m23 = t.z, m33 = 1 };
        }
        public static Matrix4x4 Translate(Vector3 t) => TRS(t, Quaternion.identity, Vector3.one);
        public static Matrix4x4 Rotate(Quaternion q) => TRS(Vector3.zero, q, Vector3.one);
        public static Matrix4x4 Scale(Vector3 s) => TRS(Vector3.zero, Quaternion.identity, s);
        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        public Vector3 MultiplyPoint(Vector3 p) => MultiplyPoint3x4(p);
        public Vector3 MultiplyVector(Vector3 v) => new Vector3(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z);
        public float this[int r, int c]
        {
            get
            {
                switch (r * 4 + c)
                {
                    case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03;
                    case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13;
                    case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23;
                    case 12: return m30; case 13: return m31; case 14: return m32; default: return m33;
                }
            }
        }
        public Vector4 GetColumn(int c) => new Vector4(this[0, c], this[1, c], this[2, c], this[3, c]);
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var r = new Matrix4x4();
            var A = new float[16]; var B = new float[16]; var C = new float[16];
            for (int i = 0; i < 16; i++) { A[i] = a[i / 4, i % 4]; B[i] = b[i / 4, i % 4]; }
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { float s = 0; for (int k = 0; k < 4; k++) s += A[i * 4 + k] * B[k * 4 + j]; C[i * 4 + j] = s; }
            r.m00 = C[0]; r.m01 = C[1]; r.m02 = C[2]; r.m03 = C[3]; r.m10 = C[4]; r.m11 = C[5]; r.m12 = C[6]; r.m13 = C[7];
            r.m20 = C[8]; r.m21 = C[9]; r.m22 = C[10]; r.m23 = C[11]; r.m30 = C[12]; r.m31 = C[13]; r.m32 = C[14]; r.m33 = C[15];
            return r;
        }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public bool Contains(Vector2 p) => p.x >= x && p.x < x + width && p.y >= y && p.y < y + height;
        public static Rect MinMaxRect(float x0, float y0, float x1, float y1) => new Rect(x0, y0, x1 - x0, y1 - y0);
    }
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 o, Vector3 d) { origin = o; direction = d.normalized; }
        public Vector3 GetPoint(float d) => origin + direction * d;
    }
    public struct Plane
    {
        public Vector3 normal; public float distance;
        public Plane(Vector3 inNormal, Vector3 point) { normal = inNormal.normalized; distance = -Vector3.Dot(normal, point); }
        public Plane(Vector3 inNormal, float d) { normal = inNormal.normalized; distance = d; }
        public float GetDistanceToPoint(Vector3 p) => Vector3.Dot(normal, p) + distance;
        public bool Raycast(Ray r, out float enter)
        {
            float vd = Vector3.Dot(r.direction, normal), num = -Vector3.Dot(r.origin, normal) - distance;
            if (Math.Abs(vd) < 1e-6f) { enter = 0; return false; }
            enter = num / vd; return enter > 0;
        }
    }
    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 c, Vector3 size) { center = c; extents = size * 0.5f; }
        public Vector3 size { get { return extents * 2; } set { extents = value * 0.5f; } }
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;
        public void SetMinMax(Vector3 mn, Vector3 mx) { extents = (mx - mn) * 0.5f; center = mn + extents; }
        public void Expand(float a) { extents += new Vector3(a, a, a) * 0.5f; }
        public void Encapsulate(Vector3 p) { SetMinMax(Vector3.Min(min, p), Vector3.Max(max, p)); }
        public bool Contains(Vector3 p) { var a = min; var b = max; return p.x >= a.x && p.x <= b.x && p.y >= a.y && p.y <= b.y && p.z >= a.z && p.z <= b.z; }
        // 点到包围盒的距离平方（盒内为 0）
        public float SqrDistance(Vector3 p)
        {
            var a = min; var b = max; double s = 0;
            for (int i = 0; i < 3; i++) { double v = p[i], lo = a[i], hi = b[i]; if (v < lo) s += (lo - v) * (lo - v); else if (v > hi) s += (v - hi) * (v - hi); }
            return (float)s;
        }
    }
    public struct RaycastHit { public Collider collider; public Vector3 point; public float distance; }
    public static class Physics { public static bool Raycast(Ray r, out RaycastHit h, float max) { h = default(RaycastHit); return false; } }

    // 视锥平面（Unity 的约定：法线朝内，顺序 左 右 下 上 近 远）与包围盒测试（p 顶点在任一平面外侧即判为不可见）
    public static class GeometryUtility
    {
        public static Plane[] CalculateFrustumPlanes(Camera cam) { var p = new Plane[6]; CalculateFrustumPlanes(cam, p); return p; }
        public static void CalculateFrustumPlanes(Camera cam, Plane[] planes)
        {
            var t = cam.transform; var pos = t.position; var rot = t.rotation;
            Vector3 f = rot * Vector3.forward, u = rot * Vector3.up, r = rot * Vector3.right;
            float tv = (float)Math.Tan(cam.fieldOfView * Math.PI / 360), th = tv * cam.aspect;
            planes[0] = new Plane((r + f * th).normalized, pos);
            planes[1] = new Plane((-r + f * th).normalized, pos);
            planes[2] = new Plane((u + f * tv).normalized, pos);
            planes[3] = new Plane((-u + f * tv).normalized, pos);
            planes[4] = new Plane(f, pos + f * cam.nearClipPlane);
            planes[5] = new Plane(-f, pos + f * cam.farClipPlane);
        }
        public static bool TestPlanesAABB(Plane[] planes, Bounds b)
        {
            var mn = b.min; var mx = b.max;
            foreach (var p in planes)
            {
                var n = p.normal;
                var v = new Vector3(n.x >= 0 ? mx.x : mn.x, n.y >= 0 ? mx.y : mn.y, n.z >= 0 ? mx.z : mn.z);
                if (Vector3.Dot(n, v) + p.distance < 0) return false;
            }
            return true;
        }
    }

    public enum MeshTopology { Triangles = 0 }
    public enum TextureFormat { RGBA32 = 4, ARGB32 = 5, RGB24 = 3, Alpha8 = 1 }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum FilterMode { Point, Bilinear, Trilinear }
    namespace Rendering
    {
        public enum IndexFormat { UInt16, UInt32 }
        public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    }

    public class Object
    {
        public string name;
        public static readonly HashSet<Object> Destroyed = new HashSet<Object>();
        public static void Destroy(Object o)
        {
            if (o == null) return;
            Destroyed.Add(o);
            var go = o as GameObject;
            if (go != null) go.DestroyTree();
        }
        public static void DestroyImmediate(Object o) { Destroy(o); }
        public static void DontDestroyOnLoad(Object o) { }
        public static bool operator true(Object o) => !ReferenceEquals(o, null);
        public static bool operator false(Object o) => ReferenceEquals(o, null);
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator e) { return new Coroutine(); }
        public void StopCoroutine(Coroutine c) { }
    }
    public class Coroutine { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public readonly int order; public DefaultExecutionOrder(int order) { this.order = order; } }
    public class WaitForSeconds { public WaitForSeconds(float s) { } }

    public class GameObject : Object
    {
        public readonly Transform transform;
        readonly List<Component> comps = new List<Component>();
        bool active = true;
        public bool destroyed;
        public int layer;
        public static readonly List<GameObject> All = new List<GameObject>();
        public GameObject(string n) { name = n; transform = new Transform { go = this }; transform.gameObject = this; All.Add(this); }
        public GameObject() : this("GameObject") { }
        public T AddComponent<T>() where T : Component, new() { var c = new T(); c.gameObject = this; comps.Add(c); return c; }
        public T GetComponent<T>() where T : Component { foreach (var c in comps) if (c is T) return (T)c; return null; }
        public IEnumerable<Component> Components => comps;
        public void SetActive(bool v) { active = v; }
        public bool activeSelf => active;
        public bool activeInHierarchy { get { for (var t = transform; t != null; t = t.parent) if (!t.go.active) return false; return true; } }
        internal void DestroyTree()
        {
            destroyed = true;
            foreach (var ch in transform.children.ToArray()) ch.go.DestroyTree();
            if (transform.parent != null) transform.parent.children.Remove(transform);
        }
    }

    public class Transform : Component
    {
        internal GameObject go;
        public Transform parent;
        internal readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition; public Quaternion localRotation = Quaternion.identity; public Vector3 localScale = Vector3.one;
        public void SetParent(Transform p, bool worldStays) { if (parent != null) parent.children.Remove(this); parent = p; if (p != null) p.children.Add(this); }
        public void SetParent(Transform p) { SetParent(p, true); }
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        // 测试只用到“父节点无缩放”的情形：位置 = 父位置 + 父旋转 × 本地位置
        public Vector3 position
        {
            get { return parent == null ? localPosition : parent.position + parent.rotation * localPosition; }
            set { localPosition = parent == null ? value : Quaternion.Inverse(parent.rotation) * (value - parent.position); }
        }
        public Quaternion rotation { get { return parent == null ? localRotation : parent.rotation * localRotation; } set { localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; } }
        public Vector3 forward => rotation * Vector3.forward;
        public Vector3 up => rotation * Vector3.up;
        public Vector3 right => rotation * Vector3.right;
        public Matrix4x4 localToWorldMatrix => Matrix4x4.TRS(position, rotation, localScale);
        public void LookAt(Vector3 p) { rotation = Quaternion.LookRotation(p - position); }
    }

    public class Collider : Component { }
    public class SphereCollider : Collider { public float radius; public Vector3 center; }
    public class BoxCollider : Collider { public Vector3 size, center; }

    public class Mesh : Object
    {
        public static readonly List<Mesh> Created = new List<Mesh>();
        public Mesh() { Created.Add(this); }
        public Rendering.IndexFormat indexFormat;
        public Vector3[] vertices = new Vector3[0];
        public Vector3[] normals = new Vector3[0];
        public Color[] colors = new Color[0];
        public Color32[] colors32 = new Color32[0];
        public Vector2[] uv = new Vector2[0];
        public Vector2[] uv2 = new Vector2[0];
        public Vector4[] uv4 = new Vector4[0];
        public int[] triangles = new int[0];
        public Bounds bounds;
        public int vertexCount => vertices.Length;
        public int subMeshCount = 1;
        public void Clear() { vertices = new Vector3[0]; normals = new Vector3[0]; colors = new Color[0]; uv = new Vector2[0]; uv2 = new Vector2[0]; triangles = new int[0]; }
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetVertices(Vector3[] v) { vertices = (Vector3[])v.Clone(); }
        public void SetNormals(List<Vector3> v) { normals = v.ToArray(); }
        public void SetColors(List<Color> c) { colors = c.ToArray(); }
        public void SetColors(List<Color32> c) { colors32 = c.ToArray(); }
        public void SetUVs(int ch, List<Vector2> u) { if (ch == 0) uv = u.ToArray(); else uv2 = u.ToArray(); }
        public void SetUVs(int ch, Vector2[] u) { if (ch == 0) uv = (Vector2[])u.Clone(); else uv2 = (Vector2[])u.Clone(); }
        public void SetUVs(int ch, List<Vector4> u) { uv4 = u.ToArray(); }
        public void SetUVs(int ch, List<Vector3> u) { uv4 = u.Select(v => (Vector4)v).ToArray(); }
        public void SetTriangles(List<int> t, int sub) { SetTriangles(t.ToArray(), sub); }
        public void SetTriangles(int[] t, int sub) { SetTriangles(t, sub, true); }
        public void SetTriangles(int[] t, int sub, bool calcBounds)
        {
            foreach (var i in t) if (i < 0 || i >= vertices.Length) throw new Exception("triangle index out of range in " + name);
            if (vertices.Length > 65535 && indexFormat != Rendering.IndexFormat.UInt32) throw new Exception("needs UInt32 index: " + name);
            triangles = t;
            if (calcBounds) RecalculateBounds();
        }
        public void SetIndices(int[] idx, int start, int len, MeshTopology topo, int sub)
        {
            var t = new int[len]; Array.Copy(idx, start, t, 0, len); SetTriangles(t, sub);
        }
        public void RecalculateBounds()
        {
            if (vertices.Length == 0) { bounds = new Bounds(); return; }
            var mn = vertices[0]; var mx = vertices[0];
            foreach (var v in vertices) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            var b = new Bounds(); b.SetMinMax(mn, mx); bounds = b;
        }
        public void RecalculateNormals() { }
        public void UploadMeshData(bool noLonger) { }
        public void MarkDynamic() { }
    }
    public class MeshFilter : Component { public Mesh sharedMesh; public Mesh mesh { get { return sharedMesh; } set { sharedMesh = value; } } }
    public class Renderer : Component
    {
        public Material sharedMaterial;
        public Material material { get { return sharedMaterial; } set { sharedMaterial = value; } }
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public bool enabled = true;
        public int sortingOrder;
        public MaterialPropertyBlock lastBlock;
        // Unity 复制属性块的值（之后改动传入的块不影响此渲染器）
        public void SetPropertyBlock(MaterialPropertyBlock b) { lastBlock = new MaterialPropertyBlock(); foreach (var kv in b.values) lastBlock.values[kv.Key] = kv.Value; }
        public Bounds bounds => new Bounds();
    }
    public class MeshRenderer : Renderer { }
    public class MaterialPropertyBlock
    {
        public readonly Dictionary<string, object> values = new Dictionary<string, object>();
        public void SetColor(string n, Color c) { values[n] = c; }
        public void SetFloat(string n, float v) { values[n] = v; }
        public void SetVector(string n, Vector4 v) { values[n] = v; }
        // Unity：属性块里数组的长度第一次设定后固定，之后再设更长的数组会被截短——替身直接报错，便于发现
        void SetArr(string n, Array v)
        {
            object o;
            if (values.TryGetValue(n, out o) && o is Array && v.Length > ((Array)o).Length) throw new Exception("MaterialPropertyBlock: 数组 " + n + " 长度 " + v.Length + " 超过首次设定的 " + ((Array)o).Length);
            values[n] = v;
        }
        public void SetVectorArray(string n, Vector4[] v) { SetArr(n, v); }
        public void SetVectorArray(string n, List<Vector4> v) { SetArr(n, v.ToArray()); }
        public void SetFloatArray(string n, float[] v) { SetArr(n, v); }
        public void SetFloatArray(string n, List<float> v) { SetArr(n, v.ToArray()); }
        public void Clear() { values.Clear(); }
    }

    public class Shader : Object
    {
        public bool isSupported => true;
        public static Shader Find(string n) => new Shader { name = n };
        public static int PropertyToID(string n) => n.GetHashCode();
        public static void SetGlobalFloat(string n, float v) { }
        public static void SetGlobalVector(string n, Vector4 v) { }
        public static void SetGlobalColor(string n, Color c) { }
    }
    public class Texture : Object
    {
        public TextureWrapMode wrapMode, wrapModeU, wrapModeV;
        public FilterMode filterMode = FilterMode.Bilinear;
        public int anisoLevel = 1;
        public float mipMapBias;
        public int width, height;
    }
    public class Texture2D : Texture
    {
        public readonly bool mipmap, linear;
        public Color32[] pixels;
        public int applied;
        public Texture2D(int w, int h) : this(w, h, TextureFormat.RGBA32, true, false) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip) : this(w, h, f, mip, false) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool lin) { width = w; height = h; mipmap = mip; linear = lin; pixels = new Color32[w * h]; }
        public void SetPixel(int x, int y, Color c) { pixels[y * width + x] = c; }
        public void SetPixels32(Color32[] p) { if (p.Length != width * height) throw new Exception("SetPixels32 size"); pixels = (Color32[])p.Clone(); }
        public void SetPixels(Color[] p) { if (p.Length != width * height) throw new Exception("SetPixels size"); pixels = p.Select(c => (Color32)c).ToArray(); }
        public Color32[] GetPixels32() => pixels;
        public void LoadRawTextureData(byte[] d) { for (int i = 0; i < width * height; i++) pixels[i] = new Color32(d[i * 4], d[i * 4 + 1], d[i * 4 + 2], d[i * 4 + 3]); }
        public void Apply() { applied++; }
        public void Apply(bool mip) { applied++; }
        public void Apply(bool mip, bool noLonger) { applied++; }
    }
    public class Material : Object
    {
        public Color color = Color.white;
        public Texture mainTexture;
        public int renderQueue;
        public bool enableInstancing;
        public Shader shader;
        public readonly Dictionary<string, object> props = new Dictionary<string, object>();
        public readonly HashSet<string> keywords = new HashSet<string>();
        public static readonly List<Material> Created = new List<Material>();
        public Material(Shader s) { Created.Add(this); shader = s; }
        public Material(Material m) { Created.Add(this); shader = m.shader; color = m.color; mainTexture = m.mainTexture; enableInstancing = m.enableInstancing; renderQueue = m.renderQueue; foreach (var kv in m.props) props[kv.Key] = kv.Value; foreach (var k in m.keywords) keywords.Add(k); }
        public void SetFloat(string n, float v) { props[n] = v; }
        public void SetInt(string n, int v) { props[n] = (float)v; }
        public void SetColor(string n, Color c) { props[n] = c; if (n == "_Color") color = c; }
        public void SetVector(string n, Vector4 v) { props[n] = v; }
        public void SetTexture(string n, Texture t) { props[n] = t; if (n == "_MainTex") mainTexture = t; }
        public Texture GetTexture(string n) { object o; return props.TryGetValue(n, out o) ? o as Texture : null; }
        public float GetFloat(string n) { object o; return props.TryGetValue(n, out o) && o is float ? (float)o : 0; }
        public bool HasProperty(string n) => true;
        public void EnableKeyword(string k) { keywords.Add(k); }
        public void DisableKeyword(string k) { keywords.Remove(k); }
        public bool IsKeywordEnabled(string k) => keywords.Contains(k);
    }
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static void UnloadUnusedAssets() { }
    }
    // 实例化绘制：只记录调用（测试统计每帧提交的实例数；Recorded 不为 null 时保存每次调用的副本，供软件光栅器重放）
    public static class Graphics
    {
        public static int calls, instances;
        public sealed class InstancedDraw { public Mesh mesh; public Material mat; public Matrix4x4[] m; public int count; public Vector4[] cols; public float[] cells; }
        public static List<InstancedDraw> Recorded;
        public static void DrawMeshInstanced(Mesh mesh, int sub, Material mat, Matrix4x4[] m, int count, MaterialPropertyBlock b, Rendering.ShadowCastingMode sh, bool recv, int layer, Camera cam)
        {
            if (count > 1023) throw new Exception("DrawMeshInstanced: count > 1023");
            if (mesh == null || mat == null) throw new Exception("DrawMeshInstanced: null mesh / material");
            if (count > m.Length) throw new Exception("DrawMeshInstanced: count > matrices.Length");
            if (b != null) foreach (var kv in b.values) if (kv.Value is Array && ((Array)kv.Value).Length < count) throw new Exception("DrawMeshInstanced: 属性数组 " + kv.Key + " 短于实例数");
            calls++; instances += count;
            if (Recorded != null)
            {
                object c = null, cl = null;
                if (b != null) { b.values.TryGetValue("_InstColor", out c); b.values.TryGetValue("_Cell", out cl); }
                Recorded.Add(new InstancedDraw { mesh = mesh, mat = mat, m = (Matrix4x4[])m.Clone(), count = count, cols = c != null ? (Vector4[])((Vector4[])c).Clone() : null, cells = cl != null ? (float[])((float[])cl).Clone() : null });
            }
        }
        public static void DrawMeshInstanced(Mesh mesh, int sub, Material mat, Matrix4x4[] m, int count, MaterialPropertyBlock b) { DrawMeshInstanced(mesh, sub, mat, m, count, b, Rendering.ShadowCastingMode.On, true, 0, null); }
        public static void DrawMeshInstanced(Mesh mesh, int sub, Material mat, Matrix4x4[] m, int count) { DrawMeshInstanced(mesh, sub, mat, m, count, null); }
    }

    public class Camera : Behaviour
    {
        public static Camera main;
        public float fieldOfView = 34, aspect = 16f / 9f, farClipPlane = 600, nearClipPlane = 0.5f;
        public Ray ScreenPointToRay(Vector3 p) => new Ray(transform.position, transform.forward);
        public int pixelWidth => Screen.width;
        public int pixelHeight => Screen.height;
    }

    public static class RenderSettings
    {
        public static bool fog = true;
        public static float fogStartDistance = 90, fogEndDistance = 320;
        public static Color fogColor = Color.white, ambientLight = Color.white;
    }
}
