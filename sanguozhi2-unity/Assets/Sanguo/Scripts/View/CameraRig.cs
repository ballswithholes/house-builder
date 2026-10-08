using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sanguo
{
    // 俯视相机：拖动平移、滚轮 / 双指缩放（双指中点平移）、点击选取、WASD / 方向键平移、Q/E 旋转
    // 第二版（§4G 世界地图，← 网页版 js/camera.js）新增：
    //   SetBounds(rect)                 限制镜头目标点的范围（地图坐标）
    //   SetRegion(key, pad)             按 WorldGeo.Regions[key] 设定范围（world china europe med arabia iran india tarim korea seasia）
    //   UseMap(map)                     按地图的构建范围配置：'world' → 世界模式 + 全图范围；'china' → 与旧版完全相同
    //   WorldMode                       世界模式：可拉远到 340 看到整个地区（130 以内俯角与旧版相同），远裁剪面 1500，远距离 Focus 自动改为飞行
    //   FlyTo(mapPos, dist)             平滑飞行（远距离时先拉高再降落），用户操作即中止；返回的 IEnumerator 在到达或中止时结束
    //   FitDistance(rect) / FitRect(rect, instant)   看到整个矩形所需的距离 / 对准矩形
    //   Flying                          是否正在飞行
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Vector3 Target, Desired;
        public float Distance = 70, DesiredDistance = 70, MinDist = 16, MaxDist = 130;
        public float Pitch = 52, Yaw = 0, DesiredYaw = 0;
        public Rect Bounds = new Rect(0, 0, 112, 100);
        public bool InputEnabled = true;
        public event Action<Vector2> OnTap;

        // 世界模式：俯角按 PitchRefDist 以内的距离计算；AutoFly 时远距离对焦改为飞行
        public const float WorldMaxDist = 340, ClassicMaxDist = 130, ClassicFar = 600, WorldFar = 1500;
        public float PitchRefDist = 0;
        public bool AutoFly = false;
        public float AutoFlyMin = 60;
        bool worldMode;

        Vector2 pressPos; bool pressing, dragging; Vector3 lastGround; float lastPinch = -1;
        int pressFinger = -1;
        bool hasMid; Vector3 lastMid;

        // 飞行状态（Update 推进）
        sealed class Fly { public Vector3 from, to; public float d0, d1, arc, t, dur; }
        Fly fly;

        public static CameraRig Create()
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            rig.Cam = camGo.AddComponent<Camera>();
            rig.Cam.fieldOfView = 34;
            rig.Cam.nearClipPlane = 0.5f;
            rig.Cam.farClipPlane = ClassicFar;
            rig.Cam.clearFlags = CameraClearFlags.Skybox;
            rig.Cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            return rig;
        }

        // ------------------------------------------------------------ 公共接口 --
        public void Focus(Vector3 p, float dist = -1, bool instant = false)
        {
            // 世界模式：远距离对焦改为平滑飞行
            if (!instant && AutoFly)
            {
                float dx = p.x - Target.x, dz = p.z - Target.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) > AutoFlyMin) { StartFly(p.x, p.z, dist, -1, true); return; }
            }
            EndFly();
            Desired = p; if (dist > 0) DesiredDistance = dist;
            if (instant)
            {
                ClampDesired();
                Target = Desired;
                Distance = DesiredDistance = Mathf.Clamp(DesiredDistance, MinDist, MaxDist);
                ApplyPose();
            }
        }
        public void FocusMap(float mapX, float mapY, float dist = -1, bool instant = false) { Focus(new Vector3(mapX, 0, mapY), dist, instant); }

        // 镜头目标点的范围（地图坐标 x / z）
        public void SetBounds(Rect r)
        {
            Bounds = Rect.MinMaxRect(Mathf.Min(r.xMin, r.xMax), Mathf.Min(r.yMin, r.yMax), Mathf.Max(r.xMin, r.xMax), Mathf.Max(r.yMin, r.yMax));
            ClampDesired();
        }
        public void SetBounds(MapRect r) { SetBounds(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMax)); }
        // 地区（WorldGeo.Regions 的键）→ 镜头范围；pad 为四周外扩。未知地区返回 false（范围不变）
        public bool SetRegion(string key, float pad = 0)
        {
            MapRect rc;
            if (!WorldGeo.TryRegionRect(key, out rc)) return false;
            SetBounds(rc.Pad(pad));
            return true;
        }
        // 经纬度方框 → 镜头范围
        public void SetRegion(float lon0, float lon1, float lat0, float lat1, float pad = 0)
        {
            SetBounds(WorldGeo.RegionRect(WorldGeo.D(lon0), WorldGeo.D(lon1), WorldGeo.D(lat0), WorldGeo.D(lat1)).Pad(pad));
        }

        // 世界模式：可拉远看到整个地区；俯角在 130 以内与旧版相同，更远时再略抬高；远距离对焦自动飞行
        public bool WorldMode
        {
            get { return worldMode; }
            set
            {
                worldMode = value;
                if (value) { MaxDist = WorldMaxDist; PitchRefDist = ClassicMaxDist; AutoFly = true; }
                else { MaxDist = ClassicMaxDist; PitchRefDist = 0; AutoFly = false; }
                // 远裁剪面：世界模式拉远时远处的地区不被裁掉（旧版 600）
                if (Cam != null) Cam.farClipPlane = value ? WorldFar : ClassicFar;
                DesiredDistance = Mathf.Clamp(DesiredDistance, MinDist, MaxDist);
            }
        }
        // 按 MapView 的构建范围配置：世界地图 → 世界模式 + 全图范围；中国地图 → 旧版设定
        public void UseMap(MapView m)
        {
            WorldMode = m != null && m.Region == "world";
            if (m != null) SetBounds(m.Bounds);
        }

        // 平滑飞行到地图坐标（远距离时先拉高再降落）。立即开始；返回的 IEnumerator 在到达或被中止时结束
        public IEnumerator FlyTo(Vector2 mapPos, float dist) { return WaitFly(StartFly(mapPos.x, mapPos.y, dist, -1, true)); }
        // duration ≤ 0 时按路程自动；arc = false 时不中途抬高
        public IEnumerator FlyTo(Vector2 mapPos, float dist, float duration, bool arc = true) { return WaitFly(StartFly(mapPos.x, mapPos.y, dist, duration, arc)); }
        public bool Flying { get { return fly != null; } }

        Fly StartFly(float mapX, float mapY, float dist, float duration, bool arcOn)
        {
            EndFly();
            var b = Bounds;
            var f = new Fly();
            f.to = new Vector3(Mathf.Clamp(mapX, b.xMin, b.xMax), 0, Mathf.Clamp(mapY, b.yMin, b.yMax));
            f.from = Target;
            f.d0 = Distance; f.d1 = Mathf.Clamp(dist > 0 ? dist : DesiredDistance, MinDist, MaxDist);
            float travel = Mathf.Sqrt((f.to.x - f.from.x) * (f.to.x - f.from.x) + (f.to.z - f.from.z) * (f.to.z - f.from.z));
            f.dur = duration > 0 ? duration : Mathf.Clamp(0.45f + travel / 240f, 0.5f, 2.4f);
            float hi = Mathf.Max(f.d0, f.d1);
            f.arc = arcOn ? Mathf.Max(0, Mathf.Min(MaxDist, hi + travel * 0.3f) - hi) : 0;
            fly = f;
            return f;
        }
        IEnumerator WaitFly(Fly f) { while (fly == f) yield return null; }
        void EndFly()
        {
            if (fly == null) return;
            fly = null;
            Desired = Target;
            DesiredDistance = Distance;
        }

        // 能看到整个矩形（地图坐标）所需的镜头距离（按当前视口宽高比与俯角估算，未按 MinDist / MaxDist 限制）
        public float FitDistance(Rect r)
        {
            float aspect = Cam != null && Cam.aspect > 0 ? Cam.aspect : 16f / 9f;
            float fov = (Cam != null && Cam.fieldOfView > 0 ? Cam.fieldOfView : 34) * Mathf.Deg2Rad;
            float w = Mathf.Abs(r.width), h = Mathf.Abs(r.height);
            float pitch = (Pitch + 4) * Mathf.Deg2Rad;
            float dH = h * Mathf.Sin(pitch) / (2 * Mathf.Tan(fov / 2)) * 1.05f;
            float dW = w / (2 * Mathf.Tan(fov / 2) * aspect) * 1.05f;
            return Mathf.Max(dH, dW);
        }
        // 对准矩形中心，并选择能看到整个矩形的距离；instant 时立即到位（返回空的 IEnumerator）
        public IEnumerator FitRect(Rect r, bool instant = false)
        {
            float d = Mathf.Clamp(FitDistance(r), MinDist, MaxDist);
            if (instant) { FocusMap(r.center.x, r.center.y, d, true); return Done(); }
            return FlyTo(r.center, d);
        }
        static IEnumerator Done() { yield break; }

        // ------------------------------------------------------------ 内部 --
        bool OverUI(int finger = -1)
        {
            if (EventSystem.current == null) return false;
            return finger >= 0 ? EventSystem.current.IsPointerOverGameObject(finger) : EventSystem.current.IsPointerOverGameObject();
        }

        bool GroundPoint(Vector2 screen, out Vector3 p)
        {
            var ray = Cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, 0.5f, 0));
            float d;
            if (plane.Raycast(ray, out d)) { p = ray.GetPoint(d); return true; }
            p = Vector3.zero; return false;
        }

        void ClampDesired()
        {
            Desired.x = Mathf.Clamp(Desired.x, Bounds.xMin, Bounds.xMax);
            Desired.z = Mathf.Clamp(Desired.z, Bounds.yMin, Bounds.yMax);
        }

        // 俯角：世界模式在 PitchRefDist（130）以内与旧版（MaxDist 130）完全相同，更远时每 10 单位再抬高约 0.8°（最多 6°）
        float CurrentPitch()
        {
            float rf = PitchRefDist > 0 ? Mathf.Min(MaxDist, PitchRefDist) : MaxDist;
            float p = Mathf.Lerp(Pitch - 10, Pitch + 8, Mathf.InverseLerp(MinDist, rf, Distance));
            if (PitchRefDist > 0 && Distance > rf) p += Mathf.Min(6, (Distance - rf) * 0.08f);
            return p;
        }
        void ApplyPose()
        {
            if (Cam == null) return;
            var rot = Quaternion.Euler(CurrentPitch(), Yaw, 0);
            Cam.transform.position = Target - rot * Vector3.forward * Distance;
            Cam.transform.rotation = rot;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (InputEnabled) HandleInput();
            var f = fly;
            if (f != null)
            {
                // 飞行：目标点按缓动曲线移动，距离 = 起止距离插值 + 中途抬高
                f.t = Mathf.Min(1, f.t + dt / f.dur);
                float t = f.t, k = t * t * t * (t * (t * 6 - 15) + 10);
                Target = Vector3.LerpUnclamped(f.from, f.to, k);
                Distance = Mathf.LerpUnclamped(f.d0, f.d1, k) + f.arc * Mathf.Sin(Mathf.PI * k);
                Desired = Target;
                DesiredDistance = Mathf.Clamp(Distance, MinDist, MaxDist);
                Yaw = Mathf.LerpAngle(Yaw, DesiredYaw, 1 - Mathf.Exp(-dt * 10f));
                if (t >= 1)
                {
                    fly = null;
                    Distance = DesiredDistance = f.d1;
                }
                ApplyPose();
                return;
            }
            float kk = 1 - Mathf.Exp(-dt * 10f);
            ClampDesired();
            DesiredDistance = Mathf.Clamp(DesiredDistance, MinDist, MaxDist);
            Target = Vector3.Lerp(Target, Desired, kk);
            Distance = Mathf.Lerp(Distance, DesiredDistance, kk);
            Yaw = Mathf.LerpAngle(Yaw, DesiredYaw, kk);
            ApplyPose();
        }

        void HandleInput()
        {
            // 触屏
            if (Input.touchCount > 0)
            {
                if (Input.touchCount >= 2)
                {
                    // 双指：缩放，并以中点平移；取消点击
                    EndFly();
                    var t0 = Input.GetTouch(0); var t1 = Input.GetTouch(1);
                    float d = Vector2.Distance(t0.position, t1.position);
                    if (lastPinch > 0) DesiredDistance *= lastPinch / Mathf.Max(1f, d);
                    lastPinch = d;
                    var mid = (t0.position + t1.position) * 0.5f;
                    Vector3 g;
                    if (GroundPoint(mid, out g))
                    {
                        if (hasMid) { Pan(lastMid - g); GroundPoint(mid, out lastMid); }
                        else { lastMid = g; hasMid = true; }
                    }
                    dragging = true; pressing = false;
                    return;
                }
                lastPinch = -1; hasMid = false;
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began || (!pressing && dragging && t.phase == TouchPhase.Moved))
                {
                    if (t.phase == TouchPhase.Began && OverUI(t.fingerId)) { pressing = false; dragging = false; return; }
                    // 双指变单指：剩下的手指继续拖动（不触发点击）
                    bool cont = t.phase != TouchPhase.Began;
                    EndFly();
                    pressing = true; dragging = cont; pressPos = t.position; pressFinger = t.fingerId;
                    GroundPoint(t.position, out lastGround);
                }
                else if (pressing && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)) Drag(t.position);
                else if (pressing && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                {
                    if (!dragging && t.phase == TouchPhase.Ended && OnTap != null) OnTap(t.position);
                    pressing = false; dragging = false;
                }
                return;
            }
            lastPinch = -1; hasMid = false;
            // 鼠标
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                if (OverUI()) { pressing = false; }
                else { EndFly(); pressing = true; dragging = false; pressPos = Input.mousePosition; GroundPoint(Input.mousePosition, out lastGround); }
            }
            if (pressing && (Input.GetMouseButton(0) || Input.GetMouseButton(1))) Drag(Input.mousePosition);
            if (pressing && (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)))
            {
                if (!dragging && Input.GetMouseButtonUp(0) && OnTap != null) OnTap(Input.mousePosition);
                pressing = false;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUI()) { EndFly(); DesiredDistance *= Mathf.Pow(0.88f, Mathf.Clamp(wheel, -4, 4)); }
            // 键盘
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 0.01f) { EndFly(); Desired += Quaternion.Euler(0, Yaw, 0) * move * Time.unscaledDeltaTime * Distance * 0.9f; }
            if (Input.GetKey(KeyCode.Q)) DesiredYaw -= 60 * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.E)) DesiredYaw += 60 * Time.unscaledDeltaTime;
        }

        void Drag(Vector2 screen)
        {
            if (!dragging && Vector2.Distance(screen, pressPos) > Screen.dpi * 0.08f + 6) dragging = true;
            if (!dragging) return;
            Vector3 g;
            if (!GroundPoint(screen, out g)) return;
            Pan(lastGround - g);
            // 更新参考点（相机移动后同一屏幕点对应的地面）
            GroundPoint(screen, out lastGround);
        }
        void Pan(Vector3 delta)
        {
            Desired += new Vector3(delta.x, 0, delta.z);
            Target += new Vector3(delta.x, 0, delta.z);
            ApplyPose();
        }
    }
}
