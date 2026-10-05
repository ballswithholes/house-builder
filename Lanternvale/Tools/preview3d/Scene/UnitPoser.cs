// A unit as UnitView builds and animates it, without UnitView's FX/UI/system plumbing: UnitModels.Get(key) →
// UnitBody.Create under a holder at the feet, the body transform (World3D.Yaw + size scale + the animator's body
// offset and pop), the per-renderer Look and the blob shadow (UnitView.UpdateGround). Time is stepped explicitly so
// a pose can be sampled at an exact moment of a walk cycle or an action (UnitView.Tick/Animate, UnitView.cs:611-756).
using System;
using System.Collections.Generic;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    public sealed class UnitPoser
    {
        public const float FacingBias = UnitFacing.Bias;   // UnitView.SetFacing

        public readonly string Key;
        public readonly UnitModel Model;
        public readonly Transform Holder;
        public readonly UnitBody Body;
        public readonly float Scale;
        public float Height => Model.Height * Scale;
        public float Yaw;
        public Vector2 Pos;

        readonly MeshRenderer shadow;
        readonly MaterialPropertyBlock shadowBlock = new MaterialPropertyBlock();
        float time, spawnT = 5f;
        UnitAction action;
        float actionT, actionDur;
        Vector2 actionDir;

        public UnitPoser(string key, float height, Transform parent, Vector2 pos, float yaw, int variant = 0)
        {
            Key = key;
            Model = UnitModels.Get(key, variant) ?? throw new InvalidOperationException("no unit model for " + key);
            Scale = height > 0f ? height / Mathf.Max(0.05f, Model.Height) : 1f;
            Holder = new GameObject("Unit " + key).transform;
            Holder.SetParent(parent, false);
            Pos = pos;
            Holder.localPosition = new Vector3(pos.x, pos.y, 0f);
            Yaw = yaw;
            Body = UnitBody.Create(Model, Holder);
            shadow = MeshCache.AddShadow(Holder, 0.4f, 0.4f);
            shadow.name = "Shadow";
            ApplyBody(Vector3.zero, 1f);
        }

        /// <summary>UnitView.SetFacing: +1 screen-right, −1 screen-left for a camera turned by cameraYaw, a little towards it.</summary>
        public static float FacingYaw(int dir, float cameraYaw = 0f) => UnitFacing.SideYaw(dir, cameraYaw);

        public void StartAction(UnitAction a, float dur, Vector2 dirWorld)
        {
            action = a; actionT = 0f; actionDur = dur;
            actionDir = dirWorld.sqrMagnitude > 1e-6f ? dirWorld.normalized : World3D.DirOf(Yaw);
        }

        public float ActionT => actionT;

        /// <summary>One frame: moves by velWorld·dt (facing the motion), advances the action and runs the animator.</summary>
        public void Step(float dt, Vector2 velWorld = default)
        {
            time += dt;
            spawnT += dt;
            float moved = 0f;
            if (velWorld.sqrMagnitude > 1e-8f)
            {
                var delta = velWorld * dt;
                Pos += delta;
                moved = delta.magnitude;
                Yaw = World3D.YawOf(delta);
                Holder.localPosition = new Vector3(Pos.x, Pos.y, 0f);
            }
            if (action != UnitAction.None)
            {
                actionT += dt;
                if (actionT >= actionDur) action = UnitAction.None;
            }
            var fwd = World3D.DirOf(Yaw);
            var right = new Vector2(fwd.y, -fwd.x);
            float sc = Scale;
            var inp = new UnitAnimInput
            {
                Dt = dt,
                Time = time,
                Velocity = new Vector3(Vector2.Dot(velWorld, right), 0f, Vector2.Dot(velWorld, fwd)) / sc,
                MoveDist = moved / sc,
                YawRate = 0f,
                Action = action,
                ActionT = actionT,
                ActionDur = actionDur,
                ActionDir = new Vector3(Vector2.Dot(actionDir, right), 0f, Vector2.Dot(actionDir, fwd)),
                DodgeSide = 1,
                HitT = -1f,
                SpawnT = spawnT,
            };
            Body.Anim.Tick(inp);
            ApplyBody(Body.Anim.BodyOffset, Body.Anim.ScalePop);
        }

        void ApplyBody(Vector3 offsetModel, float pop)
        {
            // UnitView.ApplyBodyTransform
            var rot = World3D.Yaw(Yaw);
            Body.Root.localPosition = rot * (offsetModel * Scale);
            Body.Root.localRotation = rot;
            float s = Scale * pop;
            Body.Root.localScale = new Vector3(s, s, s);
            // UnitView.UpdateLook (standing, untargeted, not hovered)
            Body.Look.Tint = Color.white;
            Body.Look.Flash = new Color(1f, 1f, 0.96f, 0f);
            Body.Look.Fade = Mathf.Clamp01(Model.BaseFade);
            Body.Look.OutlineColor = Materials3D.Ink;
            Body.Look.OutlineWidth = 1.9f;
            Body.Look.Rim = 0f;
            Body.ApplyLook();
            // UnitView.UpdateGround: the blob shadow follows the body offset, shrinks while hovering
            float r = Model.Radius * Scale;
            float lie = Body.Anim.LieAmount;
            var bo = Body.Root.localPosition;
            float hover = Mathf.Max(0f, -bo.z) + (Model.FloatHeight > 0f ? Model.FloatHeight * Scale : 0f);
            float len = (Model.HalfLength * Scale + r) * (1f + lie * 0.5f);
            float wid = r * (1f + (Model.Rig == UnitRigKind.Quad ? lie * 0.4f : 0f));
            float shrink = 1f / (1f + hover * 0.35f);
            var st = shadow.transform;
            var f = World3D.DirOf(Yaw);
            st.localPosition = new Vector3(bo.x, bo.y, -0.004f);
            st.localRotation = Quaternion.AngleAxis(Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg, Vector3.forward);
            st.localScale = new Vector3(len * 2f * shrink, wid * 2f * shrink, 1f);
            shadowBlock.SetColor(Materials3D.ColorId, new Color(0.12f, 0.09f, 0.16f, 0.4f * shrink * Mathf.Lerp(1f, 0.85f, lie)));
            shadow.SetPropertyBlock(shadowBlock);
        }

        /// <summary>UnitView.CenterPosition / HeadPosition (the occluder-fade ray targets).</summary>
        public Vector3 CenterPosition => Body.BonePoint(Model.CenterBone, Model.CenterOffset);
        public Vector3 HeadPosition => Body.BonePoint(Model.HeadBone, Model.HeadTop);

        /// <summary>World positions of the feet/paws (IK leg ends).</summary>
        public IEnumerable<Vector3> FeetWorld()
        {
            foreach (var leg in Model.Legs)
            {
                int b = leg.Foot >= 0 ? leg.Foot : leg.Lower;
                if (b >= 0 && b < Body.Bones.Length) yield return Body.Bones[b].position;
            }
        }

        // ---- pose snapshots (bones + body transform), to keep the best frame of a cycle
        public sealed class Snapshot
        {
            public Vector3[] P; public Quaternion[] R; public Vector3[] S;
            public Vector3 RootP, RootS; public Quaternion RootR; public Vector2 Pos; public float Yaw;
        }

        public Snapshot Save()
        {
            int n = Body.Bones.Length;
            var s = new Snapshot { P = new Vector3[n], R = new Quaternion[n], S = new Vector3[n] };
            for (int i = 0; i < n; i++) { var b = Body.Bones[i]; s.P[i] = b.localPosition; s.R[i] = b.localRotation; s.S[i] = b.localScale; }
            s.RootP = Body.Root.localPosition; s.RootR = Body.Root.localRotation; s.RootS = Body.Root.localScale;
            s.Pos = Pos; s.Yaw = Yaw;
            return s;
        }

        public void Restore(Snapshot s)
        {
            for (int i = 0; i < Body.Bones.Length; i++) { var b = Body.Bones[i]; b.localPosition = s.P[i]; b.localRotation = s.R[i]; b.localScale = s.S[i]; }
            Pos = s.Pos; Yaw = s.Yaw;
            Holder.localPosition = new Vector3(Pos.x, Pos.y, 0f);
            Body.Root.localPosition = s.RootP; Body.Root.localRotation = s.RootR; Body.Root.localScale = s.RootS;
        }

        // ---- canned poses (60 fps like a frame-locked game)
        public const float Dt = 1f / 60f;

        public void Idle(float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) Step(Dt);
        }

        /// <summary>
        /// Walks along the facing at `speed` m/s; after a run-up, keeps the frame of one full stride where the feet are
        /// spread widest along the motion (a mid-stride contact pose for bipeds, the extended phase of a trot).
        /// </summary>
        public void Walk(float speed, float runUp = 1.6f, float window = 1.2f)
        {
            var dir = World3D.DirOf(Yaw);
            var v = dir * speed;
            for (float t = 0f; t < runUp; t += Dt) Step(Dt, v);
            Snapshot best = null;
            float bestScore = -1f;
            for (float t = 0f; t < window; t += Dt)
            {
                Step(Dt, v);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in FeetWorld())
                {
                    float a = p.x * dir.x + p.y * dir.y;
                    lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a);
                }
                float score = hi > lo ? hi - lo : 0f;
                if (score > bestScore + 1e-4f) { bestScore = score; best = Save(); }
            }
            if (best != null) Restore(best);
        }

        /// <summary>Idles, then plays `a` (dur) and stops at actionT = at.</summary>
        public void Act(UnitAction a, float dur, float at, float idleFirst = 1.0f)
        {
            Idle(idleFirst);
            StartAction(a, dur, World3D.DirOf(Yaw));
            while (actionT + Dt <= at + 1e-4f) Step(Dt);
        }
    }
}
