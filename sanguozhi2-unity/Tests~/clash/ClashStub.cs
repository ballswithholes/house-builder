// 攻击画面（Battle/Clash.cs）无头测试用的 UnityEngine 补充替身：在 ../sim/UnityStub.cs 与 ../figure/FigureUnityStub.cs 之上
// 补齐攻击画面用到、人物模型库没用到的成员（灯光、天空盒、雾与环境光、镜头的层 / 清屏 / 投影换算、键盘输入、逐个绘制的记录等）。
// run.sh 把人物替身里的 Object / Camera / RenderSettings / Graphics 改成 partial 后与本文件一起编译，
// SystemInfo.supportsInstancing 改成可写字段（画图时关掉实例化，士兵群走「每名士兵一个 Figure」的回退路径，便于软件光栅器取姿势）。
// 渲染仍为空实现；Graphics.DrawMesh 只记录（Recorded 不为 null 时保存副本，供软件光栅器重放）。
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public enum LightType { Spot, Directional, Point, Area }
    public enum LightShadows { None, Hard, Soft }
    public enum LightShadowResolution { FromQualitySettings = -1, Low, Medium, High, VeryHigh }
    public enum FogMode { Linear = 1, Exponential, ExponentialSquared }
    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }
    namespace Rendering
    {
        public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    }

    public class Light : Behaviour
    {
        public static readonly List<Light> All = new List<Light>();
        public Light() { All.Add(this); }
        public LightType type = LightType.Directional;
        public Color color = Color.white;
        public float intensity = 1, shadowStrength = 1, shadowBias = 0.05f, shadowNormalBias = 0.4f, shadowNearPlane = 0.2f;
        public LightShadows shadows;
        public LightShadowResolution shadowResolution = LightShadowResolution.FromQualitySettings;
        public int cullingMask = -1;
    }
    public class Skybox : Behaviour { public Material material; }

    public partial struct Quaternion
    {
        // 把方向 a 转到 b 的最小旋转（与 Unity / three setFromUnitVectors 相同）
        public static Quaternion FromToRotation(Vector3 a, Vector3 b)
        {
            a = a.normalized; b = b.normalized;
            float r = Vector3.Dot(a, b) + 1;
            Quaternion q;
            if (r < 1e-6f)
            {
                r = 0;
                q = Math.Abs(a.x) > Math.Abs(a.z) ? new Quaternion(-a.y, a.x, 0, r) : new Quaternion(0, -a.z, a.y, r);
            }
            else
            {
                var c = Vector3.Cross(a, b);
                q = new Quaternion(c.x, c.y, c.z, r);
            }
            float l = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / l, q.y / l, q.z / l, q.w / l);
        }
    }
    public partial class Transform
    {
        public void LookAt(Vector3 p, Vector3 up) { rotation = Quaternion.LookRotation(p - position, up); }
    }
    public partial class Texture2D
    {
        static Texture2D white;
        public static Texture2D whiteTexture { get { if (white == null) { white = new Texture2D(4, 4); for (int i = 0; i < 16; i++) white.pixels[i] = new Color32(255, 255, 255, 255); } return white; } }
    }
    public class RenderTexture : Texture { public void Release() { } }
    public partial class MaterialPropertyBlock
    {
        // Shader.PropertyToID（替身：名字的哈希）→ 以 "#id" 为键另存一份
        public void SetColor(int id, Color c) { values["#" + id] = c; }
        public void SetFloat(int id, float v) { values["#" + id] = v; }
        public void SetVector(int id, Vector4 v) { values["#" + id] = v; }
    }

    public partial class Object
    {
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            if (typeof(T) == typeof(Light)) return Light.All.Where(l => l.gameObject != null && !l.gameObject.destroyed).Cast<T>().ToArray();
            if (typeof(T) == typeof(Camera)) return Camera.allCameras.Cast<T>().ToArray();
            return new T[0];
        }
    }

    public partial class Camera
    {
        public static readonly List<Camera> Created = new List<Camera>();
        public Camera() { Created.Add(this); }
        public static Camera[] allCameras { get { return Created.Where(c => c.enabled && c.gameObject != null && !c.gameObject.destroyed).ToArray(); } }
        public float depth;
        public int cullingMask = -1;
        public CameraClearFlags clearFlags = CameraClearFlags.Skybox;
        public Color backgroundColor = Color.black;
        public bool allowMSAA = true, allowHDR;
        // 透视投影（Unity 约定：视口 (0,0) 左下、(1,1) 右上，z = 沿镜头前向的距离）
        public Vector3 WorldToViewportPoint(Vector3 p)
        {
            var t = transform; var q = Quaternion.Inverse(t.rotation); var d = q * (p - t.position);
            float tv = (float)Math.Tan(fieldOfView * Math.PI / 360), th = tv * aspect;
            return new Vector3(d.x / (d.z * th) * 0.5f + 0.5f, d.y / (d.z * tv) * 0.5f + 0.5f, d.z);
        }
        public Vector3 WorldToScreenPoint(Vector3 p) { var v = WorldToViewportPoint(p); return new Vector3(v.x * Screen.width, v.y * Screen.height, v.z); }
    }

    public static partial class RenderSettings
    {
        public static FogMode fogMode = FogMode.Linear;
        public static Rendering.AmbientMode ambientMode = Rendering.AmbientMode.Skybox;
        public static Color ambientSkyColor = Color.white, ambientEquatorColor = Color.white, ambientGroundColor = Color.white;
        public static float ambientIntensity = 1;
        public static Material skybox;
    }
    public static class DynamicGI { public static int updates; public static void UpdateEnvironment() { updates++; } }

    public static partial class Graphics
    {
        public sealed class MeshDraw { public Mesh mesh; public Matrix4x4 m; public Material mat; public int layer; public Color tint = Color.white; public bool hasTint; public bool castShadows; public Dictionary<string, object> block; }
        public static int meshCalls;
        public static List<MeshDraw> RecordedMeshes;
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam, int sub, MaterialPropertyBlock b, bool castShadows, bool receiveShadows, bool useLightProbes)
        {
            if (mesh == null || mat == null) throw new Exception("DrawMesh: null mesh / material");
            meshCalls++;
            if (RecordedMeshes != null)
            {
                var d = new MeshDraw { mesh = mesh, m = m, mat = mat, layer = layer, castShadows = castShadows, block = b != null ? new Dictionary<string, object>(b.values) : null };
                object c; if (b != null && (b.values.TryGetValue("_Color", out c) || b.values.TryGetValue("#" + Shader.PropertyToID("_Color"), out c)) && c is Color) { d.tint = (Color)c; d.hasTint = true; }
                RecordedMeshes.Add(d);
            }
        }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam, int sub, MaterialPropertyBlock b, Rendering.ShadowCastingMode sh, bool receiveShadows, Transform probe, bool useLightProbes)
        { DrawMesh(mesh, m, mat, layer, cam, sub, b, sh != Rendering.ShadowCastingMode.Off, receiveShadows, useLightProbes); }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam, int sub, MaterialPropertyBlock b)
        { DrawMesh(mesh, m, mat, layer, cam, sub, b, true, true, true); }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer) { DrawMesh(mesh, m, mat, layer, null, 0, null, true, true, true); }
    }

    public enum KeyCode { None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32, KeypadEnter = 271 }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Down = new HashSet<KeyCode>();
        public static bool GetKeyDown(KeyCode k) { return Down.Contains(k); }
        public static bool GetKey(KeyCode k) { return Down.Contains(k); }
        public static bool touchSupported, mousePresent = true;
        public static int touchCount;
        public static bool GetMouseButtonDown(int b) { return false; }
    }
}
