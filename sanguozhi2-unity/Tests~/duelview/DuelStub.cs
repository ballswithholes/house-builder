// 单挑画面无头测试的替身（在 ../sim/UnityStub.cs、../figure/FigureUnityStub.cs 之上补）：
//   UnityEngine：灯光、天空盒、雾与 Trilight 环境光、镜头（层、清屏、投影）、键盘 / 触摸 / 鼠标（全部为「未按」）、音源与音频片段、
//                折线（LineRenderer）、CanvasGroup / RectTransform、Transform 的逆变换与自转、FindObjectsOfType；
//   Sanguo：UIKit（只有层的空节点）、Sfx（记录播放）、Game（无镜头控制器）、DuelHud / DuelInput（记录调用的同名替身——
//           画面代码 DuelGame.cs 原样编译，界面换成这里的空实现）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEngine
{
    public enum LightType { Spot, Directional, Point, Area }
    public enum LightShadows { None, Hard, Soft }
    public enum LightRenderMode { Auto, ForcePixel, ForceVertex }
    public enum FogMode { Linear = 1, Exponential, ExponentialSquared }
    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    public enum Space { World, Self }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
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
        public float intensity = 1, shadowStrength = 1, shadowBias = 0.05f, shadowNormalBias = 0.4f;
        public LightShadows shadows;
        public LightRenderMode renderMode;
        public int cullingMask = -1;
    }
    public class Skybox : Behaviour { public Material material; }
    public class CanvasGroup : Behaviour { public float alpha = 1; public bool blocksRaycasts = true, interactable = true; }
    public class RectTransform : Transform { }
    public class AudioClip : Object
    {
        public float[] data; public int samples, channels, frequency;
        public static AudioClip Create(string name, int samples, int channels, int freq, bool stream) { return new AudioClip { name = name, samples = samples, channels = channels, frequency = freq }; }
        public bool SetData(float[] d, int offset) { data = (float[])d.Clone(); return true; }
    }
    public class AudioSource : Behaviour
    {
        public bool playOnAwake; public float spatialBlend, volume = 1;
        public static readonly List<string> Played = new List<string>();
        public void PlayOneShot(AudioClip c, float vol) { Played.Add(c.name + "@" + vol.ToString("0.00")); }
    }
    public class LineRenderer : Renderer
    {
        public bool useWorldSpace = true; public int positionCount; public float startWidth, endWidth;
        public Vector3[] pts = new Vector3[8];
        public void SetPosition(int i, Vector3 p) { if (i >= pts.Length) Array.Resize(ref pts, i + 1); pts[i] = p; }
    }
    public struct Touch { public int fingerId; public Vector2 position; public TouchPhase phase; }
    public enum KeyCode { None = 0, Space = 32, A = 97, D = 100, I = 105, J = 106, K = 107, L = 108, S = 115, U = 117, W = 119, UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276, LeftControl = 306, RightControl = 305, LeftAlt = 308, LeftCommand = 310 }
    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) { return false; }
        public static bool GetKey(KeyCode k) { return false; }
        public static bool anyKeyDown => false;
        public static bool touchSupported, mousePresent = true;
        public static int touchCount => 0;
        public static Touch GetTouch(int i) { return new Touch(); }
        public static Vector3 mousePosition => Vector3.zero;
        public static bool GetMouseButtonDown(int b) { return false; }
        public static bool GetMouseButton(int b) { return false; }
    }
    public static partial class Screen
    {
        public static Rect safeArea => new Rect(0, 0, width, height);
    }
    public partial struct Quaternion
    {
        // 把方向 a 转到 b 的最小旋转（与 Unity / three setFromUnitVectors 相同）
        public static Quaternion FromToRotation(Vector3 a, Vector3 b)
        {
            a = a.normalized; b = b.normalized;
            float r = Vector3.Dot(a, b) + 1;
            Quaternion q;
            if (r < 1e-6f) { r = 0; q = Math.Abs(a.x) > Math.Abs(a.z) ? new Quaternion(-a.y, a.x, 0, r) : new Quaternion(0, -a.z, a.y, r); }
            else { var c = Vector3.Cross(a, b); q = new Quaternion(c.x, c.y, c.z, r); }
            float l = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / l, q.y / l, q.z / l, q.w / l);
        }
    }
    public partial class Transform
    {
        public void LookAt(Vector3 p, Vector3 up) { rotation = Quaternion.LookRotation(p - position, up); }
        // 逆变换：逐级 (p − 位置) → 逆旋转 → 除以缩放（与 Unity 相同，适用于各级缩放沿本地轴）
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            if (parent != null) p = parent.InverseTransformPoint(p);
            var d = Quaternion.Inverse(localRotation) * (p - localPosition);
            return new Vector3(d.x / localScale.x, d.y / localScale.y, d.z / localScale.z);
        }
        public IEnumerator GetEnumerator() { return children.ToArray().GetEnumerator(); }
        public void Rotate(float x, float y, float z, Space s)
        {
            var q = Quaternion.Euler(x, y, z);
            if (s == Space.Self) localRotation = localRotation * q; else rotation = q * rotation;
        }
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
    }
    public static partial class RenderSettings
    {
        public static FogMode fogMode = FogMode.Linear;
        public static Rendering.AmbientMode ambientMode = Rendering.AmbientMode.Skybox;
        public static Color ambientSkyColor = Color.white, ambientEquatorColor = Color.white, ambientGroundColor = Color.white;
    }
    public static class DynamicGI { public static int updates; public static void UpdateEnvironment() { updates++; } }
}

namespace UnityEngine
{
    public static class DuelStubHooks
    {
        public static readonly List<IEnumerator> Coroutines = new List<IEnumerator>();
    }
    namespace Rendering { public enum CullMode { Off = 0, Front = 1, Back = 2 } }
    public enum RenderTextureFormat { ARGB32 = 0 }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum TextAlignment { Left, Center, Right }
    public class RenderTexture : Texture
    {
        public int anisoLevel;
        public RenderTexture(int w, int h, int d, RenderTextureFormat f) { }
        public bool Create() { return true; }
        public void Release() { }
    }
    public class Font : Object { public Material material; }
    public class TextMesh : Component
    {
        public Font font; public string text; public int fontSize; public float characterSize = 1; public FontStyle fontStyle;
        public TextAnchor anchor; public TextAlignment alignment; public Color color;
    }
    public partial class Camera
    {
        public bool orthographic; public float orthographicSize; public RenderTexture targetTexture;
        public void Render() { }
    }
    public partial struct Color
    {
        public static bool operator ==(Color a, Color b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }
        public static bool operator !=(Color a, Color b) { return !(a == b); }
        public override bool Equals(object o) { return o is Color && (Color)o == this; }
        public override int GetHashCode() { return r.GetHashCode() ^ g.GetHashCode() * 7 ^ b.GetHashCode() * 13 ^ a.GetHashCode() * 31; }
    }
    public partial struct Matrix4x4
    {
        public static bool operator ==(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if (a[i, j] != b[i, j]) return false;
            return true;
        }
        public static bool operator !=(Matrix4x4 a, Matrix4x4 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Matrix4x4 && (Matrix4x4)o == this; }
        public override int GetHashCode() { return m00.GetHashCode() ^ m11.GetHashCode() * 7 ^ m23.GetHashCode() * 13; }
    }
    public static partial class Graphics
    {
        public sealed class MeshDraw { public Mesh mesh; public Matrix4x4 m; public Material mat; public int layer; public Dictionary<string, object> block; }
        public static readonly List<MeshDraw> Meshes = new List<MeshDraw>();
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam, int sub, MaterialPropertyBlock b, Rendering.ShadowCastingMode sh, bool receiveShadows, Transform probe, bool useLightProbes)
        {
            if (mesh == null || mat == null) throw new Exception("DrawMesh: null mesh / material");
            Meshes.Add(new MeshDraw { mesh = mesh, m = m, mat = mat, layer = layer, block = b != null ? new Dictionary<string, object>(b.values) : null });
        }
    }
}

namespace Sanguo
{
    public static class UIKit
    {
        public static RectTransform Root = new GameObject("UIRoot").AddComponent<RectTransform>(), LabelLayer;
        public static RectTransform Screens => null;
        public static Font Title;
    }
    public static class Sfx
    {
        public static bool MusicOn = true, SoundOn = true;
        public static readonly List<string> Log = new List<string>();
        public static void Play(string name, float vol = 0.8f) { Log.Add("sfx:" + name); }
        public static void Music(string kind) { Log.Add("music:" + kind); }
    }
    public class CameraRig { public bool InputEnabled = true; }
    public class Game { public static Game I; public CameraRig Rig; }

    // 界面替身：记录调用（流程测试据此检查飘字、公告、格开提示等）
    public sealed class DuelHud
    {
        public static readonly List<string> Log = new List<string>();
        readonly DuelScreen S;
        public bool hasTouch, compact;
        public DuelHud(DuelScreen s) { S = s; }
        void L(string s) { Log.Add(s); }
        public void Build() { L("Build"); }
        public void SetFade(float a) { L("SetFade " + a); }
        public void Preload() { }
        public void BuildHud() { L("BuildHud"); }
        public Action IntroVS() { L("IntroVS"); return () => L("IntroVS.close"); }
        public void ShowHud(bool on) { L("ShowHud " + on); }
        public Action ControlsCard(DuelSpecialInfo sp, General me, General foe) { L("ControlsCard"); return () => L("ControlsCard.close"); }
        public void Announce(string text, string cls, string sub, float sec) { L("Announce " + text); }
        public void ShowControls(bool on) { L("ShowControls " + on); }
        public void WinPanel(bool lose, General winner, string sub, uint seed) { L("WinPanel " + winner.name + " " + sub); }
        public void DisposeKeepFade() { L("DisposeKeepFade"); }
        public void DisposeFade() { L("DisposeFade"); }
        public void Dispose() { L("Dispose"); }
        public void Update(float dt, float realT, DuelSim sim, Camera cam) { }
        public void PollTouch(DuelInput input, float time, bool specialReady) { }
        public bool PointInSkip(Vector2 p) { return false; }
        public void ShowPauseHint(bool on) { }
        public void FloatDmg(Vector3 threePos, string text, string cls, float vx) { L("FloatDmg " + text + " [" + cls + "]"); }
        public void ScreenFlash(float a) { L("ScreenFlash"); }
        public void HitShake(int idx) { }
        public void ShowCombo(int idx, int n, float realT) { L("Combo " + idx + " " + n); }
        public void CutIn(int idx, DuelSpecialInfo sp, float speed) { L("CutIn " + sp.name); }
        public void EdgeFlash() { L("EdgeFlash"); }
        public void ParryButtonFlash() { L("ParryButtonFlash"); }
        public void CounterChip(int idx) { L("CounterChip " + idx); }
    }
    public sealed class DuelInput : IDuelCtrl
    {
        readonly DuelHeld held = new DuelHeld();
        readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        public void Update(double dt, DuelSim sim) { }
        public bool enabled = true;
        public void Poll() { }
        public void SetEnabled(bool on) { enabled = on; }
    }
}
