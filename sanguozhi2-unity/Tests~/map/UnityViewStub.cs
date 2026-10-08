// 地图视图无头测试用的 UnityEngine 补充替身（与 Tests~/sim/UnityStub.cs 一起编译；run.sh 把那份替身里的
// Vector2 / Vector3 / Color / Mathf / Debug 改成 partial 后再编译）。只实现 MapView / CultureArt / Art 用到的成员：
// 网格只保存数组，GameObject / Transform 维护父子层级与 TRS，渲染、物理、着色器均为空实现。
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
    }
    public static partial class Debug
    {
        public static void LogException(Exception e) => Console.WriteLine("EXCEPTION " + e);
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
        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
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
        Vector3 n; float d;
        public Plane(Vector3 normal, Vector3 point) { n = normal.normalized; d = -Vector3.Dot(n, point); }
        public bool Raycast(Ray r, out float enter)
        {
            float vd = Vector3.Dot(r.direction, n), num = -Vector3.Dot(r.origin, n) - d;
            if (Math.Abs(vd) < 1e-6f) { enter = 0; return false; }
            enter = num / vd; return enter > 0;
        }
    }
    public struct RaycastHit { public Collider collider; }
    public static class Physics { public static bool Raycast(Ray r, out RaycastHit h, float max) { h = default(RaycastHit); return false; } }

    public enum MeshTopology { Triangles = 0 }
    public enum TextureFormat { RGBA32 = 4 }
    public enum TextureWrapMode { Repeat, Clamp }
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
    }
    public class Coroutine { }

    public class GameObject : Object
    {
        public readonly Transform transform;
        readonly List<Component> comps = new List<Component>();
        bool active = true;
        public bool destroyed;
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
        }
    }

    public class Transform : Component
    {
        internal GameObject go;
        public Transform parent;
        internal readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition; public Quaternion localRotation = Quaternion.identity; public Vector3 localScale = Vector3.one;
        public void SetParent(Transform p, bool worldStays) { if (parent != null) parent.children.Remove(this); parent = p; if (p != null) p.children.Add(this); }
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        // 测试只用到“父节点无旋转缩放”的情形：位置 = 父位置 + 父旋转 × 本地位置
        public Vector3 position
        {
            get { return parent == null ? localPosition : parent.position + parent.rotation * localPosition; }
            set { localPosition = parent == null ? value : value - parent.position; }
        }
        public Quaternion rotation { get { return parent == null ? localRotation : parent.rotation * localRotation; } set { localRotation = value; } }
        public Vector3 forward => rotation * Vector3.forward;
    }

    public class Collider : Component { }
    public class SphereCollider : Collider { public float radius; public Vector3 center; }

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
        public int[] triangles = new int[0];
        public int vertexCount => vertices.Length;
        public void Clear() { vertices = new Vector3[0]; normals = new Vector3[0]; colors = new Color[0]; uv = new Vector2[0]; triangles = new int[0]; }
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetNormals(List<Vector3> v) { normals = v.ToArray(); }
        public void SetColors(List<Color> c) { colors = c.ToArray(); }
        public void SetColors(List<Color32> c) { colors32 = c.ToArray(); }
        public void SetUVs(int ch, List<Vector2> u) { uv = u.ToArray(); }
        public void SetTriangles(List<int> t, int sub) { SetTriangles(t.ToArray(), sub); }
        public void SetTriangles(int[] t, int sub)
        {
            foreach (var i in t) if (i < 0 || i >= vertices.Length) throw new Exception("triangle index out of range in " + name);
            if (vertices.Length > 65535 && indexFormat != Rendering.IndexFormat.UInt32) throw new Exception("needs UInt32 index: " + name);
            triangles = t;
        }
        public void SetIndices(int[] idx, int start, int len, MeshTopology topo, int sub)
        {
            var t = new int[len]; Array.Copy(idx, start, t, 0, len); SetTriangles(t, sub);
        }
        public void RecalculateBounds() { }
    }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Renderer : Component
    {
        public Material sharedMaterial;
        public Material material { get { return sharedMaterial; } set { sharedMaterial = value; } }
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public bool enabled = true;
        public void SetPropertyBlock(MaterialPropertyBlock b) { }
    }
    public class MeshRenderer : Renderer { }
    public class MaterialPropertyBlock { public void SetColor(string n, Color c) { } }

    public class Shader : Object
    {
        public bool isSupported => true;
        public static Shader Find(string n) => new Shader { name = n };
    }
    public class Texture : Object { public TextureWrapMode wrapMode; }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixel(int x, int y, Color c) { }
        public void Apply() { }
    }
    public class Material : Object
    {
        public Color color = Color.white;
        public Texture mainTexture;
        public int renderQueue;
        public bool enableInstancing;
        public static readonly List<Material> Created = new List<Material>();
        public Material(Shader s) { Created.Add(this); }
        public Material(Material m) { Created.Add(this); color = m.color; mainTexture = m.mainTexture; enableInstancing = m.enableInstancing; }
        public void SetFloat(string n, float v) { }
        public void SetColor(string n, Color c) { }
    }
    public static class Resources { public static T Load<T>(string path) where T : Object => null; }

    public class Camera : Behaviour
    {
        public static Camera main;
        public float fieldOfView = 34, aspect = 16f / 9f, farClipPlane = 600, nearClipPlane = 0.5f;
        public Ray ScreenPointToRay(Vector3 p) => new Ray(transform.position, transform.forward);
    }

    public static class RenderSettings
    {
        public static bool fog = true;
        public static float fogStartDistance = 90, fogEndDistance = 320;
    }
}
