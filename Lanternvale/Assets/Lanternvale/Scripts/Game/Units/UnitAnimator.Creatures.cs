// Quadruped (diagonal-pair trot), spider (alternating tetrapod) and static (totem, dummy, trap) animation.
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class UnitAnimator
    {
        // quad working pose
        Vector3 qHipsPos, qHipsE, qChestE, qNeckE, qHeadE;
        float qJaw;
        readonly float[] qLift = new float[8];
        readonly Vector3[] qFootOff = new Vector3[8];

        // ================================================================== quadruped

        void TickQuad(in UnitAnimInput inp)
        {
            float dt = inp.Dt;
            float time = inp.Time + seed;
            float L = m.LegLength;
            float heavy = m.Heavy;
            int legs = m.Legs.Length;

            var v = inp.Velocity; v.y = 0f;
            float speed = v.magnitude;
            vs = Mathf.Lerp(vs, speed, 1f - Mathf.Exp(-dt / 0.1f));
            if (speed > 0.05f) moveDir = Vector3.Slerp(moveDir, v / speed, 1f - Mathf.Exp(-dt / 0.06f));
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude < 1e-6f) moveDir = Vector3.forward;
            moveDir.Normalize();
            float turn = Mathf.Abs(inp.YawRate) * Mathf.Deg2Rad;

            float chain = legs > 0 ? m.Legs[0].A + m.Legs[0].B : L;
            var g = GaitParams(Mathf.Max(vs, speed), L, chain, 0.85f, 0.9f * m.StrideK, m.MaxCadence);
            g.Walk = WalkWeight(vs, L);
            g.Run = Win(g.Vn, 1.3f, 2.0f) * (1f - heavy);
            float turnW = Mathf.Clamp01(turn / 2f) * (1f - g.Walk);
            float w = Mathf.Max(g.Walk, turnW);
            walkW = Mathf.Lerp(walkW, g.Walk, 1f - Mathf.Exp(-dt / 0.12f));
            phase = Frac(phase + inp.MoveDist / g.S + Mathf.Min(1.8f, turn * 0.4f) * dt * (1f - g.Walk));
            float liftK = Mathf.Max(g.Walk, turnW * 0.6f) * (1f + heavy * 0.3f);
            float wi = Mathf.Clamp01(w * 1.5f);

            for (int i = 0; i < legs; i++)
            {
                var leg = m.Legs[i];
                float p = Frac(phase + leg.Phase);
                FootPath(p, g, g.Lift * liftK, leg.Rest.y, 0f, 0f, false, out float along, out float fix, out float y, out float pitch, out bool stance);
                var gaitPos = new Vector3(leg.Rest.x, y, leg.Rest.z) + moveDir * (along * w);
                footT[i] = Vector3.Lerp(leg.Rest, gaitPos, wi);
                footPitch[i] = pitch * wi;
                if (stance && !wasStance[i] && vs > 0.6f && (i & 1) == 0)
                {
                    FootDown = true;
                    FootDownPos = footT[i];
                }
                wasStance[i] = stance;
                qLift[i] = 0f;
                qFootOff[i] = Vector3.zero;
            }

            // ---- body
            float bobPh = (phase - g.Beta * 0.5f) * Mathf.PI * 4f;
            float crouch = L * (0.01f + 0.04f * g.Run * w);
            float bob = -L * (0.022f + 0.02f * g.Run + 0.02f * heavy) * w * Mathf.Cos(bobPh);
            float breathe = Mathf.Sin(time * 1.9f) * m.Breath;
            qHipsPos = new Vector3(0f, m.HipY - crouch + bob + breathe * 0.004f * L, Rest(QB.Hips).z);
            float swPh = (phase - g.Beta * 0.5f) * Mathf.PI * 2f;
            float hipRoll = (2.5f + heavy * 5f) * w * Mathf.Sin(swPh);
            float hipYaw = (3f + heavy * 3f) * w * Mathf.Cos(swPh);
            float bend = Mathf.Clamp(inp.YawRate * 0.06f, -18f, 18f);
            qHipsE = new Vector3((2f + 2f * g.Run) * w, hipYaw - bend * 0.3f, hipRoll);
            qChestE = new Vector3(Mathf.Sin(bobPh) * 1.5f * w + breathe * 0.8f, -hipYaw * 1.3f + bend, -hipRoll * 0.8f);

            // idle glance / sniff
            glanceTimer -= dt;
            if (glanceTimer <= 0f)
            {
                glanceTimer = 2f + Random.value * 4.5f;
                bool look = Random.value < 0.65f && w < 0.3f;
                glanceTarget = look ? Random.Range(-35f, 35f) : 0f;
                glancePitchTarget = look ? Random.Range(-12f, 14f) : 0f;
            }
            if (w > 0.3f) { glanceTarget = 0f; glancePitchTarget = 0f; }
            glanceYaw = Mathf.Lerp(glanceYaw, glanceTarget, 1f - Mathf.Exp(-dt * 3f));
            glancePitch = Mathf.Lerp(glancePitch, glancePitchTarget, 1f - Mathf.Exp(-dt * 3f));
            float nod = Mathf.Sin(bobPh + 0.8f) * (3f + 3f * heavy) * w;
            qNeckE = new Vector3(-qHipsE.x - qChestE.x + nod + glancePitch * 0.5f, glanceYaw * 0.5f + bend * 0.4f, 0f);
            qHeadE = new Vector3(glancePitch * 0.5f - nod * 0.5f, glanceYaw * 0.5f, 0f);
            qJaw = 0f;

            QuadActions(inp);

            float lie, kneel;
            LieWeights(inp, out lie, out kneel);
            LieAmount = lie;
            if (lie > 0f || kneel > 0f) QuadLie(lie, kneel, inp.Dead || inp.Action == UnitAction.Death);

            SolveQuad(time, dt, w, g.Run);
        }

        void QuadActions(in UnitAnimInput inp)
        {
            float t = inp.ActionT, dur = Mathf.Max(0.05f, inp.ActionDur);
            float L = m.LegLength;
            float bodyLen = Mathf.Abs(Rest(QB.Chest).z) + L * 0.4f;
            var dir = inp.ActionDir; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            switch (inp.Action)
            {
                case UnitAction.Attack:
                {
                    var st = m.Strike;
                    if (st == UnitStrike.Swipe || st == UnitStrike.Stomp)
                    {
                        float rear = Bump(t, 0.0f, 0.14f, 0.2f, 0.26f);
                        float slam = Win(t, 0.17f, 0.23f) * (1f - Win(t, 0.3f, dur));
                        float pitch = st == UnitStrike.Stomp ? 34f : 26f;
                        qHipsE.x -= pitch * rear;
                        qHipsPos.y += L * 0.04f * rear - L * 0.05f * slam;
                        qNeckE.x += -10f * rear + 22f * slam;
                        qJaw = 30f * Bump(t, 0.05f, 0.15f, 0.22f, 0.32f);
                        BodyOffset += dir * (bodyLen * (0.12f * slam - 0.04f * rear));
                        // front paws rise and come down (swipe: mostly the right paw)
                        for (int i = 0; i < m.Legs.Length; i++)
                        {
                            if (m.Legs[i].Root != QB.Chest) continue;
                            bool main = st == UnitStrike.Stomp || m.Legs[i].Side > 0;
                            float k = main ? 1f : 0.45f;
                            qLift[i] += L * 0.55f * rear * k;
                            qFootOff[i] += Vector3.forward * (L * (0.35f * rear * k + 0.25f * slam * k));
                        }
                    }
                    else
                    {
                        float back = Bump(t, 0f, 0.09f, 0.11f, 0.19f);
                        float lunge = Win(t, 0.1f, 0.22f) * (1f - Win(t, 0.3f, dur));
                        float reach = st == UnitStrike.Charge ? 0.75f : (st == UnitStrike.Bump ? 0.3f : 0.45f);
                        BodyOffset += dir * (bodyLen * (reach * lunge - 0.1f * back));
                        qHipsPos.y -= L * (0.07f * back + 0.03f * lunge);
                        qHipsE.x += 7f * lunge - 4f * back;
                        bool headDown = st == UnitStrike.Charge;
                        qNeckE.x += -14f * back + (headDown ? 32f : 20f) * lunge;
                        qHeadE.x += (headDown ? 10f : 8f) * lunge;
                        if (st != UnitStrike.Charge && st != UnitStrike.Bump)
                            qJaw = 38f * Bump(t, 0.1f, 0.16f, 0.19f, 0.23f) + 6f * lunge;
                        for (int i = 0; i < m.Legs.Length; i++)
                        {
                            bool front = m.Legs[i].Root == QB.Chest;
                            qLift[i] += L * (front ? 0.16f : 0.06f) * Mathf.Sin(Mathf.PI * Win(t, 0.1f, 0.26f));
                        }
                    }
                    break;
                }
                case UnitAction.Shoot:
                case UnitAction.Cast:
                {
                    // howl / roar / spit: head up, jaw open, then snap towards the target
                    bool cast = inp.Action == UnitAction.Cast;
                    float rel = cast ? UnitView.CastReleaseTime : UnitView.ShootReleaseTime;
                    float up = Win(t, 0f, rel * 0.8f) * (1f - Win(t, rel, rel + 0.12f));
                    float snap = Win(t, rel - 0.02f, rel + 0.06f) * (1f - Win(t, dur - 0.18f, dur));
                    float env = 1f - Win(t, dur - 0.15f, dur);
                    qNeckE.x += (-38f * up + 12f * snap) * env;
                    qHeadE.x += -12f * up * env;
                    qChestE.x += -6f * up * env;
                    qHipsPos.y -= L * 0.03f * up * env;
                    qJaw = Mathf.Max(qJaw, (34f * up + 22f * snap) * env);
                    BodyOffset += dir * (bodyLen * 0.08f * snap);
                    break;
                }
                case UnitAction.Dodge:
                {
                    float u = Mathf.Clamp01(t / dur);
                    float k = Mathf.Sin(Mathf.PI * u);
                    float side = inp.DodgeSide >= 0 ? 1f : -1f;
                    BodyOffset += new Vector3(side * m.Radius * 1.2f * S01(u * 1.6f) * (1f - Win(u, 0.55f, 1f)), 0.12f * L * k, 0f);
                    qHipsE.z -= side * 14f * k;
                    for (int i = 0; i < m.Legs.Length; i++) qLift[i] += 0.12f * L * k;
                    break;
                }
                case UnitAction.Knockback:
                {
                    float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                    BodyOffset.y += 0.18f * L * k;
                    qHipsE.x -= 10f * k;
                    for (int i = 0; i < m.Legs.Length; i++) qLift[i] += 0.1f * L * k;
                    break;
                }
            }
            if (inp.HitT >= 0f && inp.HitT < 0.35f)
            {
                float k = Win(inp.HitT, 0f, 0.04f) * (1f - Win(inp.HitT, 0.06f, 0.33f));
                BodyOffset.z -= 0.08f * L * k;
                qHipsPos.y -= 0.04f * L * k;
                qNeckE.x += 14f * k;
                qChestE.z += 6f * k;
            }
            if (inp.Casting && inp.Action == UnitAction.None)
            {
                qNeckE.x -= 18f;
                qJaw = Mathf.Max(qJaw, 10f + 6f * Mathf.Sin(inp.Time * 5f));
            }
        }

        void QuadLie(float lie, float kneel, bool dead)
        {
            float L = m.LegLength;
            float k = Mathf.Max(lie, kneel);
            float le = S01(lie);
            // lower the body: legs fold under (feet stay planted), then (dead) roll onto the side
            qHipsPos.y = Mathf.Lerp(qHipsPos.y, dead ? m.LieHeight : m.LieHeight * 1.15f, S01(k));
            qHipsE.x = Mathf.Lerp(qHipsE.x, dead ? 4f : -4f, k);
            qNeckE.x = Mathf.Lerp(qNeckE.x, dead ? 30f : 8f, k);
            qHeadE.x = Mathf.Lerp(qHeadE.x, dead ? 10f : -6f, k);
            if (dead)
            {
                qHipsE.z = Mathf.Lerp(qHipsE.z, 82f, le);
                qJaw = Mathf.Lerp(qJaw, 12f, le);
            }
            for (int i = 0; i < m.Legs.Length; i++)
            {
                var leg = m.Legs[i];
                bool front = leg.Root == QB.Chest;
                Vector3 fold = front
                    ? new Vector3(leg.Rest.x * 0.9f, leg.Rest.y, leg.Rest.z + (leg.A + leg.B) * 0.55f)
                    : new Vector3(leg.Rest.x * 1.05f, leg.Rest.y, leg.Rest.z + (leg.A + leg.B) * 0.2f);
                footT[i] = Vector3.Lerp(footT[i], fold, S01(k));
                if (dead)
                {
                    // legs stretched out sideways (towards the body's down after the roll)
                    var down = E(0f, 0f, 82f) * Vector3.down;
                    var joint = new Vector3(leg.Rest.x, m.LieHeight, leg.Rest.z);
                    var stretched = joint + down * ((leg.A + leg.B) * 0.8f) + Vector3.forward * ((front ? 0.2f : -0.15f) * L);
                    stretched.y = Mathf.Max(stretched.y, leg.Rest.y);
                    footT[i] = Vector3.Lerp(footT[i], stretched, le);
                }
                qLift[i] *= 1f - k;
            }
        }

        void SolveQuad(float time, float dt, float w, float run)
        {
            var hipsQ = E(qHipsE);
            var chestQ = E(qChestE);
            var chestPos = qHipsPos + hipsQ * Rest(QB.Chest);
            var chestModel = hipsQ * chestQ;

            // keep the front legs reachable: pitch the body down at the front if needed
            float L = m.LegLength;
            if (LieAmount < 0.5f)
            {
                float worst = 0f;
                for (int i = 0; i < m.Legs.Length; i++)
                {
                    var leg = m.Legs[i];
                    if (leg.Root != QB.Chest) continue;
                    var j = chestPos + chestModel * Rest(leg.Upper);
                    var f = footT[i] + Vector3.up * qLift[i] + qFootOff[i];
                    float R = (leg.A + leg.B) * 0.98f;
                    float dx = f.x - j.x, dz = f.z - j.z;
                    float rem = R * R - dx * dx - dz * dz;
                    float allowed = rem > 0f ? f.y + Mathf.Sqrt(rem) : f.y + R * 0.3f;
                    worst = Mathf.Max(worst, j.y - allowed);
                }
                float bodyLen = Mathf.Max(0.05f, Mathf.Abs(Rest(QB.Chest).z));
                if (worst > 0f)
                {
                    qHipsE.x += Mathf.Min(14f, Mathf.Asin(Mathf.Clamp(worst / bodyLen, 0f, 0.5f)) * Mathf.Rad2Deg);
                    hipsQ = E(qHipsE);
                    chestPos = qHipsPos + hipsQ * Rest(QB.Chest);
                    chestModel = hipsQ * chestQ;
                }
                // and the back legs: lower the hips
                float ymax = float.MaxValue;
                for (int i = 0; i < m.Legs.Length; i++)
                {
                    var leg = m.Legs[i];
                    if (leg.Root != QB.Hips) continue;
                    var j = qHipsPos + hipsQ * Rest(leg.Upper);
                    var f = footT[i] + Vector3.up * qLift[i];
                    float R = (leg.A + leg.B) * 0.98f;
                    float dx = f.x - j.x, dz = f.z - j.z;
                    float rem = R * R - dx * dx - dz * dz;
                    float allowed = (rem > 0f ? f.y + Mathf.Sqrt(rem) : f.y + R * 0.3f) - (j.y - qHipsPos.y);
                    ymax = Mathf.Min(ymax, allowed);
                }
                if (ymax < float.MaxValue)
                {
                    float ny = SoftMin(qHipsPos.y, ymax, 0.03f * L);
                    float dy = ny - qHipsPos.y;
                    qHipsPos.y = ny;
                    chestPos.y += dy;
                }
            }

            SetPos(QB.Hips, qHipsPos);
            SetRot(QB.Hips, hipsQ);
            SetRot(QB.Chest, chestQ);
            SetRot(QB.Neck, E(qNeckE));
            SetRot(QB.Head, E(qHeadE));
            SetRot(QB.Jaw, E(qJaw, 0f, 0f));

            for (int i = 0; i < m.Legs.Length; i++)
            {
                var leg = m.Legs[i];
                bool front = leg.Root == QB.Chest;
                var rootRot = front ? chestModel : hipsQ;
                var rootPos = front ? chestPos : qHipsPos;
                var j = rootPos + rootRot * Rest(leg.Upper);
                var target = footT[i] + Vector3.up * qLift[i] + qFootOff[i];
                var pole = rootRot * leg.Pole;
                SolveIK(j, target, leg.A, leg.B, pole, out var wu, out var wl);
                SetRot(leg.Upper, Quaternion.Inverse(rootRot) * wu);
                SetRot(leg.Lower, Quaternion.Inverse(wu) * wl);
                if (leg.Foot >= 0)
                {
                    var wf = Quaternion.Slerp(E(footPitch[i], 0f, 0f), hipsQ, LieAmount * 0.8f);
                    SetRot(leg.Foot, Quaternion.Inverse(wl) * wf);
                }
            }

            // tail: wag + drag; second segment lags
            float speedK = Mathf.Clamp01(vs / Mathf.Max(0.3f, 2f * m.LegLength));
            float wagSpeed = 3f + 6f * speedK;
            float wag = Mathf.Sin(time * wagSpeed) * (14f + 8f * (1f - speedK));
            Spring(ref tailA, ref tailV, 10f + 25f * speedK - 30f * LieAmount, 50f, 7f, dt);
            Spring(ref tailYaw, ref tailYawV, wag, 80f, 6f, dt);
            SetRot(QB.Tail1, E(Mathf.Clamp(tailA, -40f, 60f), tailYaw, 0f));
            SetRot(QB.Tail2, E(8f + speedK * 8f, tailYaw * 0.8f + Mathf.Sin(time * wagSpeed - 1f) * 10f, 0f));
            Spring(ref capeA, ref capeV, 6f * speedK + Mathf.Sin(time * 2.4f) * 2f, 40f, 6f, dt);
            SetRot(QB.Back, E(capeA, 0f, Mathf.Sin(time * 1.8f) * 2f));
        }

        // ================================================================== spider

        void TickSpider(in UnitAnimInput inp)
        {
            float dt = inp.Dt;
            float time = inp.Time + seed;
            float L = m.LegLength;
            var v = inp.Velocity; v.y = 0f;
            float speed = v.magnitude;
            vs = Mathf.Lerp(vs, speed, 1f - Mathf.Exp(-dt / 0.08f));
            if (speed > 0.05f) moveDir = Vector3.Slerp(moveDir, v / speed, 1f - Mathf.Exp(-dt / 0.06f));
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude < 1e-6f) moveDir = Vector3.forward;
            moveDir.Normalize();
            float turn = Mathf.Abs(inp.YawRate) * Mathf.Deg2Rad;

            float chain = m.Legs.Length > 0 ? m.Legs[0].A + m.Legs[0].B : L;
            var g = GaitParams(Mathf.Max(vs, speed), L, chain, 0.7f, 0.8f * m.StrideK, m.MaxCadence);
            g.Walk = WalkWeight(vs, L);
            g.Beta = Mathf.Max(g.Beta, 0.5f);
            g.Sweep = Mathf.Min(g.Beta * g.S, 0.7f * chain);
            float turnW = Mathf.Clamp01(turn / 2f) * (1f - g.Walk);
            float w = Mathf.Max(g.Walk, turnW);
            phase = Frac(phase + inp.MoveDist / Mathf.Max(0.05f, g.Sweep / g.Beta) + Mathf.Min(2.2f, turn * 0.5f) * dt * (1f - g.Walk));
            float wi = Mathf.Clamp01(w * 1.5f);
            g.Lift = L * 0.5f;

            for (int i = 0; i < m.Legs.Length; i++)
            {
                var leg = m.Legs[i];
                float p = Frac(phase + leg.Phase);
                FootPath(p, g, g.Lift * Mathf.Max(g.Walk, turnW * 0.6f), leg.Rest.y, 0f, 0f, false, out float along, out float fix, out float y, out float pitch, out bool stance);
                var gaitPos = new Vector3(leg.Rest.x, y, leg.Rest.z) + moveDir * (along * w);
                // idle: a leg now and then lifts and taps
                float tap = Mathf.Max(0f, Mathf.Sin(time * 1.3f + i * 2.17f) - 0.94f) * 6f * (1f - wi);
                footT[i] = Vector3.Lerp(leg.Rest, gaitPos, wi) + Vector3.up * (tap * L * 0.2f);
                qLift[i] = 0f;
                qFootOff[i] = Vector3.zero;
            }

            float bob = -L * 0.06f * w * Mathf.Cos(phase * Mathf.PI * 4f);
            float breathe = Mathf.Sin(time * 2.2f);
            var bodyPos = new Vector3(0f, m.HipY + bob + breathe * 0.004f, 0f);
            var bodyE = new Vector3(Mathf.Sin(phase * Mathf.PI * 4f) * 2f * w, Mathf.Sin(phase * Mathf.PI * 2f) * 4f * w, Mathf.Sin(phase * Mathf.PI * 2f + 1f) * 3f * w);
            var abdE = new Vector3(breathe * 3f - 4f, Mathf.Sin(time * 0.8f) * 5f - bodyE.y * 1.5f, 0f);
            float fang = 0f;

            float t = inp.ActionT, dur = Mathf.Max(0.05f, inp.ActionDur);
            var dir = inp.ActionDir; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            switch (inp.Action)
            {
                case UnitAction.Attack:
                {
                    float rear = Bump(t, 0f, 0.13f, 0.18f, 0.24f);
                    float strike = Win(t, 0.16f, 0.22f) * (1f - Win(t, 0.3f, dur));
                    bodyE.x -= 26f * rear - 10f * strike;
                    bodyPos.y += L * 0.25f * rear;
                    BodyOffset += dir * (m.Radius * (0.5f * strike - 0.1f * rear));
                    fang = 30f * Bump(t, 0.08f, 0.15f, 0.2f, 0.25f);
                    for (int i = 0; i < 2 && i < m.Legs.Length; i++)
                    {
                        qLift[i] += L * 1.1f * rear;
                        qFootOff[i] += Vector3.forward * (m.Radius * (0.4f * rear + 0.3f * strike));
                    }
                    break;
                }
                case UnitAction.Shoot:
                case UnitAction.Cast:
                {
                    float k = Win(t, 0f, 0.18f) * (1f - Win(t, dur - 0.15f, dur));
                    abdE.x -= 50f * k;
                    bodyE.x += 8f * k;
                    bodyPos.y += L * 0.15f * k;
                    break;
                }
                case UnitAction.Dodge:
                {
                    float u = Mathf.Clamp01(t / dur);
                    float side = inp.DodgeSide >= 0 ? 1f : -1f;
                    BodyOffset += new Vector3(side * m.Radius * S01(u * 1.6f) * (1f - Win(u, 0.55f, 1f)), 0.1f * L * Mathf.Sin(Mathf.PI * u), 0f);
                    break;
                }
                case UnitAction.Knockback:
                    BodyOffset.y += 0.3f * L * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                    break;
            }
            if (inp.HitT >= 0f && inp.HitT < 0.35f)
            {
                float k = Win(inp.HitT, 0f, 0.04f) * (1f - Win(inp.HitT, 0.06f, 0.33f));
                bodyPos.y -= L * 0.12f * k;
                bodyE.x -= 8f * k;
                BodyOffset.z -= m.Radius * 0.1f * k;
            }

            float lie, kneel;
            LieWeights(inp, out lie, out kneel);
            LieAmount = lie;
            float curl = Mathf.Max(lie, kneel * 0.7f);
            if (curl > 0f)
            {
                bodyPos.y = Mathf.Lerp(bodyPos.y, m.LieHeight, S01(curl));
                for (int i = 0; i < m.Legs.Length; i++)
                {
                    var leg = m.Legs[i];
                    var j = Rest(leg.Upper);
                    var curled = new Vector3(j.x * 1.3f, m.LieHeight + L * 0.5f, j.z * 1.3f);
                    footT[i] = Vector3.Lerp(footT[i], curled, S01(curl));
                    qLift[i] *= 1f - curl;
                }
            }

            var bodyQ = E(bodyE);
            SetPos(SB.Body, bodyPos);
            SetRot(SB.Body, bodyQ);
            SetRot(SB.Abdomen, E(abdE));
            SetRot(SB.Fangs, E(fang, 0f, 0f));
            for (int i = 0; i < m.Legs.Length; i++)
            {
                var leg = m.Legs[i];
                var j = bodyPos + bodyQ * Rest(leg.Upper);
                var target = footT[i] + Vector3.up * qLift[i] + qFootOff[i];
                var pole = bodyQ * leg.Pole;
                SolveIK(j, target, leg.A, leg.B, pole, out var wu, out var wl);
                SetRot(leg.Upper, Quaternion.Inverse(bodyQ) * wu);
                SetRot(leg.Lower, Quaternion.Inverse(wu) * wl);
            }
        }

        // ================================================================== static (totems, dummy, traps)

        void TickStatic(in UnitAnimInput inp)
        {
            float dt = inp.Dt;
            float time = inp.Time + seed;
            // springy wobble, kicked by hits
            if (inp.HitT >= 0f && inp.HitT < dt + 1e-4f)
            {
                wobXV -= 260f;
                wobZV += (seed > 50f ? 1f : -1f) * 140f;
            }
            Spring(ref wobX, ref wobXV, Mathf.Sin(time * 0.9f) * 1.2f, 90f, 5f, dt);
            Spring(ref wobZ, ref wobZV, Mathf.Sin(time * 0.7f + 1f) * 1f, 90f, 5f, dt);
            float topBob = 0f, pulse = 0f;
            float at = inp.ActionT, dur = Mathf.Max(0.05f, inp.ActionDur);
            if (inp.Action == UnitAction.Cast || inp.Action == UnitAction.Shoot || inp.Action == UnitAction.Attack)
                pulse = Mathf.Sin(Mathf.PI * Mathf.Clamp01(at / dur));
            if (inp.Casting) pulse = Mathf.Max(pulse, 0.3f + 0.2f * Mathf.Sin(inp.Time * 4f));
            topBob = Mathf.Sin(time * 1.6f) * 0.015f * m.Height;

            float lie, kneel;
            LieWeights(inp, out lie, out kneel);
            LieAmount = lie;
            float topple = S01(Mathf.Max(lie, kneel * 0.3f));
            SetPos(TB.Base, Vector3.zero);
            SetRot(TB.Base, E(-80f * topple, 0f, 10f * topple));
            if (n > TB.Top)
            {
                SetPos(TB.Top, Rest(TB.Top) + Vector3.up * topBob);
                SetRot(TB.Top, E(wobX, Mathf.Sin(time * 0.5f) * 6f, wobZ));
                float sc = 1f + 0.12f * pulse;
                t[TB.Top].localScale = new Vector3(sc, sc, sc);
            }
        }
    }
}
