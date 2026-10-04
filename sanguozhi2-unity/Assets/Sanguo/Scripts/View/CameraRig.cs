using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sanguo
{
    // 俯视相机：拖动平移、滚轮 / 双指缩放、点击选取
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Vector3 Target, Desired;
        public float Distance = 70, DesiredDistance = 70, MinDist = 16, MaxDist = 130;
        public float Pitch = 52, Yaw = 0, DesiredYaw = 0;
        public Rect Bounds = new Rect(0, 0, 112, 100);
        public bool InputEnabled = true;
        public event Action<Vector2> OnTap;

        Vector2 pressPos; bool pressing, dragging; Vector3 lastGround; float lastPinch = -1;
        int pressFinger = -1;

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
            rig.Cam.farClipPlane = 600;
            rig.Cam.clearFlags = CameraClearFlags.Skybox;
            rig.Cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            return rig;
        }

        public void Focus(Vector3 p, float dist = -1, bool instant = false)
        {
            Desired = p; if (dist > 0) DesiredDistance = dist;
            if (instant) { Target = Desired; Distance = DesiredDistance; }
        }

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

        void Update()
        {
            if (InputEnabled) HandleInput();
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            Desired.x = Mathf.Clamp(Desired.x, Bounds.xMin, Bounds.xMax);
            Desired.z = Mathf.Clamp(Desired.z, Bounds.yMin, Bounds.yMax);
            DesiredDistance = Mathf.Clamp(DesiredDistance, MinDist, MaxDist);
            Target = Vector3.Lerp(Target, Desired, k);
            Distance = Mathf.Lerp(Distance, DesiredDistance, k);
            Yaw = Mathf.LerpAngle(Yaw, DesiredYaw, k);
            float pitch = Mathf.Lerp(Pitch - 10, Pitch + 8, Mathf.InverseLerp(MinDist, MaxDist, Distance));
            var rot = Quaternion.Euler(pitch, Yaw, 0);
            Cam.transform.position = Target - rot * Vector3.forward * Distance;
            Cam.transform.rotation = rot;
        }

        void HandleInput()
        {
            // 触屏
            if (Input.touchCount > 0)
            {
                if (Input.touchCount >= 2)
                {
                    var t0 = Input.GetTouch(0); var t1 = Input.GetTouch(1);
                    float d = Vector2.Distance(t0.position, t1.position);
                    if (lastPinch > 0) DesiredDistance *= lastPinch / Mathf.Max(1f, d);
                    lastPinch = d;
                    dragging = true; pressing = false;
                    return;
                }
                lastPinch = -1;
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    if (OverUI(t.fingerId)) { pressing = false; return; }
                    pressing = true; dragging = false; pressPos = t.position; pressFinger = t.fingerId;
                    GroundPoint(t.position, out lastGround);
                }
                else if (pressing && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)) Drag(t.position);
                else if (pressing && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                {
                    if (!dragging && t.phase == TouchPhase.Ended && OnTap != null) OnTap(t.position);
                    pressing = false;
                }
                return;
            }
            lastPinch = -1;
            // 鼠标
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                if (OverUI()) { pressing = false; }
                else { pressing = true; dragging = false; pressPos = Input.mousePosition; GroundPoint(Input.mousePosition, out lastGround); }
            }
            if (pressing && (Input.GetMouseButton(0) || Input.GetMouseButton(1))) Drag(Input.mousePosition);
            if (pressing && (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)))
            {
                if (!dragging && Input.GetMouseButtonUp(0) && OnTap != null) OnTap(Input.mousePosition);
                pressing = false;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUI()) DesiredDistance *= Mathf.Pow(0.88f, wheel);
            // 键盘
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 0.01f) Desired += Quaternion.Euler(0, Yaw, 0) * move * Time.unscaledDeltaTime * Distance * 0.9f;
            if (Input.GetKey(KeyCode.Q)) DesiredYaw -= 60 * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.E)) DesiredYaw += 60 * Time.unscaledDeltaTime;
        }

        void Drag(Vector2 screen)
        {
            if (!dragging && Vector2.Distance(screen, pressPos) > Screen.dpi * 0.08f + 6) dragging = true;
            if (!dragging) return;
            Vector3 g;
            if (!GroundPoint(screen, out g)) return;
            var delta = lastGround - g;
            Desired += new Vector3(delta.x, 0, delta.z);
            Target += new Vector3(delta.x, 0, delta.z);
            // 更新参考点（相机移动后同一屏幕点对应的地面）
            GroundPointAfterMove(screen, delta);
        }
        void GroundPointAfterMove(Vector2 screen, Vector3 delta)
        {
            var rot = Cam.transform.rotation;
            Cam.transform.position = Target - rot * Vector3.forward * Distance;
            GroundPoint(screen, out lastGround);
        }
    }
}
