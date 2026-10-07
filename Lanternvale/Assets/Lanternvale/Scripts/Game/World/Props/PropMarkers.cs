// Stand-in models for styled transition markers (Waymarker.BuildEntrance): used only while the prop library has no
// model for the marker's art key (prop_cave_mouth, prop_crypt_door, prop_stairs_down, prop_raid_portal: the
// props-dungeon builder's keys win as soon as they exist). They are not registered as art keys.
//
//   cave    a rocky mound with a dark arched mouth under a mossy lintel stone, roots hanging over it
//   door    a dressed-stone portal with iron-banded double doors set into a grassy barrow, two little lamps
//   stairs  a stair going down into the dark between low rubble walls, under a small stone archway with a lamp
//   portal  a trilithon on a round dais with a glowing oval between the stones, rune-lit, ringed by small menhirs
//
// Model space: Y up, front −Z (towards the camera), pivot on the ground. cave / door: the pivot is the threshold (the
// model reaches back, +Z); stairs / portal: the pivot is the centre of the walk-in area.
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        /// <summary>A stand-in model for a transition marker style (cave | door | stairs | portal); glow tints a portal.</summary>
        internal static PropModel MarkerStandIn(string marker, int seed, Color glow)
        {
            switch (marker)
            {
                case "cave": return MarkerCave(seed);
                case "door": return MarkerDoor(seed);
                case "stairs": return MarkerStairs(seed);
                default: return MarkerPortal(glow);
            }
        }

        static readonly Color MkRock = Paint.Hex("#9a948f"), MkRockDark = Paint.Hex("#77716e"), MkRockLight = Paint.Hex("#b3aca3");
        static readonly Color MkVoid = new Color(0.07f, 0.06f, 0.08f);
        static readonly Color MkMoss = Paint.Hex("#7f9a55"), MkRoot = Paint.Hex("#6a5040");

        // ------------------------------------------------------------------ cave mouth

        static PropModel MarkerCave(int seed)
        {
            int b = Bucket(seed);
            var rig = new Rig("marker_cave");
            rig.Body(Cached("lv_marker_cave_" + b, () =>
            {
                var mb = Builder(VariantSeed("lv_marker_cave", b), 0.07f, 0.4f, 1.2f);
                int s = 11 + b * 7;
                // the knoll the mouth opens into (low in front, so the mouth shows from the game camera), its two jambs
                mb.Color = MkRockDark;
                mb.Blob(new Vector3(0f, 0.6f, 1.55f), new Vector3(2.7f, 1.55f, 1.5f), 1, 0.14f, s, 0.6f);
                mb.Color = MkRock;
                mb.Blob(new Vector3(-1.65f, 0.85f, 0.2f), new Vector3(0.82f, 1.1f, 0.85f), 1, 0.16f, s + 1, 0.5f);
                mb.Color = Paint.Shade(MkRock, 0.95f);
                mb.Blob(new Vector3(1.7f, 0.75f, 0.25f), new Vector3(0.85f, 1.0f, 0.85f), 1, 0.16f, s + 2, 0.5f);
                // the dark mouth between the jambs (its lower half is below the floor) and the floor sloping into it
                mb.Color = MkVoid;
                mb.Blob(new Vector3(0f, 0.05f, 0.1f), new Vector3(1.05f, 1.85f, 0.3f), 1, 0.04f, s + 3);
                mb.Color = Color.Lerp(MkVoid, MkRockDark, 0.35f);
                mb.Blob(new Vector3(0f, 0.01f, -0.2f), new Vector3(0.95f, 0.04f, 0.45f), 1, 0.05f, s + 11);
                // the lintel stone over the mouth, moss on top
                mb.Color = MkRockLight;
                mb.Blob(new Vector3(0.05f, 2.25f, 0.4f), new Vector3(1.7f, 0.52f, 0.8f), 1, 0.14f, s + 4, 0.3f);
                mb.Color = MkMoss;
                mb.Blob(new Vector3(-0.2f, 2.68f, 0.6f), new Vector3(1.25f, 0.2f, 0.6f), 1, 0.2f, s + 5, 0.7f);
                mb.Blob(new Vector3(-1.6f, 1.88f, 0.5f), new Vector3(0.6f, 0.2f, 0.55f), 1, 0.2f, s + 6, 0.7f);
                mb.Blob(new Vector3(1.0f, 2.1f, 1.7f), new Vector3(1.4f, 0.32f, 0.85f), 1, 0.2f, s + 7, 0.7f);
                // roots hanging over the mouth
                mb.Color = MkRoot;
                mb.Segment(new Vector3(-0.7f, 1.8f, -0.2f), new Vector3(-0.78f, 1.3f, -0.26f), 0.035f, 0.015f, 4);
                mb.Segment(new Vector3(0.62f, 1.82f, -0.22f), new Vector3(0.7f, 1.2f, -0.28f), 0.035f, 0.012f, 4);
                // an old straw rope across the mouth with paper charms: someone once sealed this place
                mb.Color = Pal.RopeStraw;
                var r0 = new Vector3(-1.15f, 1.82f, -0.42f);
                var r1 = new Vector3(0f, 1.66f, -0.46f);
                var r2 = new Vector3(1.15f, 1.8f, -0.42f);
                mb.Segment(r0, r1, 0.045f, 0.045f, 5);
                mb.Segment(r1, r2, 0.045f, 0.045f, 5);
                mb.Color = Pal.Paper;
                for (int i = 0; i < 3; i++)
                {
                    var at = i == 1 ? r1 : Vector3.Lerp(i == 0 ? r0 : r2, r1, 0.5f);
                    mb.Box(at + new Vector3(0f, -0.06f, -0.03f), new Vector3(0.09f, 0.08f, 0.01f));
                    mb.Box(at + new Vector3(0.04f, -0.16f, -0.03f), new Vector3(0.09f, 0.1f, 0.01f));
                    mb.Box(at + new Vector3(0f, -0.27f, -0.03f), new Vector3(0.09f, 0.1f, 0.01f));
                }
                // rubble at the foot
                mb.Color = MkRock;
                mb.Blob(new Vector3(-2.25f, 0.14f, -0.55f), new Vector3(0.36f, 0.24f, 0.3f), 0, 0.2f, s + 8, 0.6f);
                mb.Blob(new Vector3(2.15f, 0.12f, -0.6f), new Vector3(0.3f, 0.2f, 0.28f), 0, 0.2f, s + 9, 0.6f);
                mb.Blob(new Vector3(-1.2f, 0.08f, -0.95f), new Vector3(0.2f, 0.12f, 0.18f), 0, 0.2f, s + 10, 0.6f);
                return mb;
            }));
            return rig.Done(1.8f);
        }

        // ------------------------------------------------------------------ barrow / crypt door

        static PropModel MarkerDoor(int seed)
        {
            var rig = new Rig("marker_door");
            rig.Body(Cached("lv_marker_door", () =>
            {
                var mb = Builder(VariantSeed("lv_marker_door", 0), 0.06f, 0.35f, 1f);
                // the grassy barrow the door is set into
                // a long low mound, lumpy, old stones showing through its turf
                mb.Color = Paint.Hex("#71824a");
                mb.Blob(new Vector3(0f, 0.1f, 2.0f), new Vector3(3.6f, 1.75f, 2.5f), 2, 0.07f, 21, 0.85f);
                mb.Color = Paint.Hex("#7c8c4e");
                mb.Blob(new Vector3(-1.3f, 1.1f, 1.7f), new Vector3(1.6f, 0.65f, 1.4f), 1, 0.14f, 26, 0.6f);
                mb.Blob(new Vector3(1.4f, 0.95f, 2.2f), new Vector3(1.7f, 0.7f, 1.5f), 1, 0.14f, 27, 0.6f);
                mb.Color = Paint.Hex("#66763f");
                mb.Blob(new Vector3(0.2f, 1.6f, 2.5f), new Vector3(1.3f, 0.38f, 1.1f), 1, 0.14f, 28, 0.6f);
                mb.Color = MkRockDark;
                mb.Blob(new Vector3(-1.75f, 0.25f, 0.35f), new Vector3(0.6f, 0.45f, 0.5f), 0, 0.2f, 22, 0.6f);
                mb.Blob(new Vector3(1.8f, 0.22f, 0.4f), new Vector3(0.55f, 0.4f, 0.5f), 0, 0.2f, 23, 0.6f);
                mb.Color = MkRock;
                mb.Blob(new Vector3(-2.3f, 0.7f, 1.6f), new Vector3(0.4f, 0.3f, 0.35f), 0, 0.2f, 29, 0.5f);
                mb.Blob(new Vector3(2.0f, 1.3f, 1.9f), new Vector3(0.35f, 0.25f, 0.3f), 0, 0.2f, 30, 0.5f);
                // tufts of long grass along the foot
                mb.Color = Paint.Hex("#9aa356");
                for (int i = 0; i < 9; i++)
                {
                    float x = -2.8f + i * 0.7f;
                    if (Mathf.Abs(x) < 1.4f) continue;
                    mb.Blade(new Vector3(x, 0f, 0.55f + (i % 3) * 0.12f), new Vector3(x + 0.12f, 0.5f + (i % 2) * 0.15f, 0.45f), 0.14f);
                    mb.Blade(new Vector3(x + 0.18f, 0f, 0.6f), new Vector3(x + 0.05f, 0.42f, 0.5f), 0.12f);
                }
                // the portal: jambs, lintel, a low pediment
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Color = Pal.StoneDark;
                    mb.BoxOn(new Vector3(s * 1.0f, 0f, 0f), new Vector3(0.62f, 0.2f, 0.66f));
                    mb.Color = Pal.StoneCool;
                    mb.TaperedBox(new Vector3(s * 1.0f, 0.2f, 0f), new Vector3(0.5f, 2.2f, 0.56f), 0.9f);
                }
                mb.Color = Pal.StoneCool;
                mb.Box(new Vector3(0f, 2.58f, 0f), new Vector3(2.7f, 0.38f, 0.66f));
                mb.Color = Pal.StoneDark;
                mb.TaperedBox(new Vector3(0f, 2.77f, 0.02f), new Vector3(2.4f, 0.42f, 0.56f), 0.35f, 1f);
                // a carved lantern sigil over the doors
                mb.Color = Pal.Brass;
                mb.Box(new Vector3(0f, 2.6f, -0.34f), new Vector3(0.22f, 0.24f, 0.03f));
                // the doors: dark oak leaves with iron bands and ring pulls, a dark seam between them
                mb.Color = MkVoid;
                mb.Box(new Vector3(0f, 1.2f, 0.12f), new Vector3(1.5f, 2.3f, 0.06f));
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Color = Paint.Hex("#5c4231");
                    mb.Box(new Vector3(s * 0.37f, 1.15f, 0.04f), new Vector3(0.7f, 2.18f, 0.1f));
                    mb.Color = Pal.Iron;
                    for (int k = 0; k < 3; k++) mb.Box(new Vector3(s * 0.37f, 0.45f + k * 0.72f, -0.02f), new Vector3(0.72f, 0.06f, 0.03f));
                    mb.Push().Translate(s * 0.13f, 1.12f, -0.04f).Rotate(90f, 0f, 0f);
                    mb.Torus(Vector3.zero, 0.07f, 0.016f, 8, 4);
                    mb.Pop();
                }
                // the threshold steps and ivy on the lintel
                mb.Color = Pal.Stone;
                mb.BoxOn(new Vector3(0f, 0f, -0.48f), new Vector3(2.2f, 0.12f, 0.6f));
                mb.Color = Pal.StoneDark;
                mb.BoxOn(new Vector3(0f, 0f, -0.92f), new Vector3(2.6f, 0.06f, 0.36f));
                mb.Color = Pal.Leaf;
                mb.Blob(new Vector3(-0.85f, 2.82f, -0.12f), new Vector3(0.45f, 0.16f, 0.22f), 1, 0.25f, 24);
                mb.Blob(new Vector3(-1.16f, 2.2f, -0.22f), new Vector3(0.14f, 0.5f, 0.12f), 1, 0.25f, 25);
                // the lamps' iron brackets
                mb.Color = Pal.Iron;
                for (int s = -1; s <= 1; s += 2)
                    mb.Segment(new Vector3(s * 1.0f, 1.82f, -0.28f), new Vector3(s * 1.0f, 1.82f, -0.46f), 0.02f, 0.02f, 4);
                return mb;
            }));
            var lamps = rig.Part("Lamps", LitMesh("lv_marker_door_lamps", false, MarkerDoorLamps), Vector3.zero, Quaternion.identity);
            rig.Model.SetLit = MeshSwitch(lamps, "lv_marker_door_lamps", MarkerDoorLamps);
            rig.Light(new Vector3(-1.0f, 1.86f, -0.5f));
            rig.Light(new Vector3(1.0f, 1.86f, -0.5f));
            return rig.Done(1.6f);
        }

        static MeshBuilder MarkerDoorLamps(bool lit)
        {
            var mb = Builder(41, 0.04f, 0f, 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                var c = new Vector3(s * 1.0f, 1.86f, -0.52f);
                mb.Color = Pal.Iron;
                mb.Box(c + new Vector3(0f, 0.14f, 0f), new Vector3(0.2f, 0.04f, 0.2f));
                mb.Box(c - new Vector3(0f, 0.13f, 0f), new Vector3(0.18f, 0.04f, 0.18f));
                if (lit) { mb.Color = Pal.Glow; mb.Emission = 1f; }
                else mb.Color = Pal.GlassDark;
                mb.Box(c, new Vector3(0.15f, 0.22f, 0.15f));
                mb.Emission = 0f;
            }
            return mb;
        }

        // ------------------------------------------------------------------ stair down

        static PropModel MarkerStairs(int seed)
        {
            var rig = new Rig("marker_stairs");
            rig.Body(Cached("lv_marker_stairs", () =>
            {
                var mb = Builder(VariantSeed("lv_marker_stairs", 0), 0.06f, 0.35f, 0.8f);
                // the dark well and the treads going down into it, darker the deeper they are
                mb.Color = MkVoid;
                mb.BoxOn(new Vector3(0f, 0f, 0.2f), new Vector3(1.9f, 0.02f, 2.5f));
                for (int i = 0; i < 6; i++)
                {
                    float k = i / 5f;
                    mb.Color = Color.Lerp(Pal.StoneCool, MkVoid, Mathf.Pow(k, 0.8f) * 0.92f);
                    mb.BoxOn(new Vector3(0f, 0.02f, -0.85f + i * 0.37f), new Vector3(1.66f, 0.025f, 0.31f));
                }
                // low rubble walls around three sides
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Color = Paint.Shade(Pal.Stone, s < 0 ? 1f : 0.94f);
                    mb.BoxOn(new Vector3(s * 1.12f, 0f, 0.2f), new Vector3(0.38f, 0.5f, 2.55f));
                    mb.Color = Pal.StoneDark;
                    mb.Blob(new Vector3(s * 1.12f, 0.55f, 0.75f), new Vector3(0.24f, 0.12f, 0.3f), 0, 0.2f, 31 + s, 0.5f);
                }
                mb.Color = Pal.Stone;
                mb.BoxOn(new Vector3(0f, 0f, 1.3f), new Vector3(2.62f, 0.62f, 0.4f));
                mb.Color = MkMoss;
                mb.Blob(new Vector3(0.6f, 0.64f, 1.3f), new Vector3(0.5f, 0.1f, 0.22f), 1, 0.2f, 33, 0.6f);
                mb.Blob(new Vector3(-1.12f, 0.52f, -0.3f), new Vector3(0.22f, 0.08f, 0.5f), 1, 0.2f, 34, 0.6f);
                // the little archway over the stair head
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Color = Pal.StoneDark;
                    mb.BoxOn(new Vector3(s * 1.12f, 0f, -1.05f), new Vector3(0.5f, 0.2f, 0.5f));
                    mb.Color = Pal.StoneCool;
                    mb.TaperedBox(new Vector3(s * 1.12f, 0.2f, -1.05f), new Vector3(0.38f, 1.85f, 0.38f), 0.85f);
                }
                mb.Color = Pal.StoneLight;
                mb.Box(new Vector3(0f, 2.18f, -1.05f), new Vector3(2.75f, 0.3f, 0.46f));
                mb.Color = Pal.Iron;
                mb.Segment(new Vector3(0f, 2.03f, -1.05f), new Vector3(0f, 1.9f, -1.05f), 0.018f, 0.018f, 4);
                return mb;
            }));
            var lamp = rig.Part("Lamp", LitMesh("lv_marker_stairs_lamp", false, MarkerStairsLamp), Vector3.zero, Quaternion.identity);
            rig.Model.SetLit = MeshSwitch(lamp, "lv_marker_stairs_lamp", MarkerStairsLamp);
            rig.Light(new Vector3(0f, 1.74f, -1.05f));
            return rig.Done(1.5f);
        }

        static MeshBuilder MarkerStairsLamp(bool lit)
        {
            var mb = Builder(43, 0.04f, 0f, 1f);
            var c = new Vector3(0f, 1.74f, -1.05f);
            mb.Color = Pal.Iron;
            mb.Box(c + new Vector3(0f, 0.15f, 0f), new Vector3(0.24f, 0.04f, 0.24f));
            mb.Box(c - new Vector3(0f, 0.15f, 0f), new Vector3(0.2f, 0.04f, 0.2f));
            if (lit) { mb.Color = Pal.Glow; mb.Emission = 1f; }
            else mb.Color = Pal.GlassDark;
            mb.Box(c, new Vector3(0.18f, 0.26f, 0.18f));
            return mb;
        }

        // ------------------------------------------------------------------ raid portal

        static PropModel MarkerPortal(Color glow)
        {
            string key = "lv_marker_portal_" + ColorUtility.ToHtmlStringRGB(glow);
            var rig = new Rig("marker_portal");
            rig.Body(Cached(key, () =>
            {
                var mb = Builder(51, 0.06f, 0.35f, 1.2f);
                var deep = Color.Lerp(glow, Color.black, 0.35f);
                // the round dais
                mb.Color = Pal.StoneDark;
                mb.Lathe(new[] { new Vector2(2.15f, 0f), new Vector2(2.15f, 0.12f), new Vector2(1.75f, 0.12f), new Vector2(1.75f, 0.24f), new Vector2(0f, 0.24f) }, 12, false, false, true);
                mb.Color = Pal.StoneCool;
                mb.Lathe(new[] { new Vector2(1.7f, 0.24f), new Vector2(0f, 0.25f) }, 12, false, false, true);
                // the trilithon: two leaning menhirs and the capstone across them
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Color = Paint.Shade(MkRock, s < 0 ? 1f : 0.93f);
                    mb.Push().Translate(s * 1.3f, 0.2f, 0f).Rotate(0f, s * 6f, s * 3f);
                    mb.TaperedBox(Vector3.zero, new Vector3(0.62f, 3.05f, 0.56f), 0.8f, 0.85f);
                    // runes down the inner face
                    mb.Color = glow;
                    mb.Emission = 1f;
                    for (int k = 0; k < 4; k++)
                        mb.Box(new Vector3(-s * 0.05f, 0.65f + k * 0.55f, -0.29f), new Vector3(k % 2 == 0 ? 0.16f : 0.08f, k % 2 == 0 ? 0.06f : 0.18f, 0.02f));
                    mb.Emission = 0f;
                    mb.Pop();
                }
                mb.Color = MkRockLight;
                mb.Push().Translate(0.05f, 3.38f, 0f).Rotate(0f, 0f, 2.5f);
                mb.Box(Vector3.zero, new Vector3(3.55f, 0.48f, 0.66f));
                mb.Pop();
                mb.Color = MkMoss;
                mb.Blob(new Vector3(-0.7f, 3.66f, 0.05f), new Vector3(0.7f, 0.12f, 0.3f), 1, 0.2f, 52, 0.7f);
                // the glowing oval between the stones: a deep rim, a bright heart
                mb.Emission = 1f;
                mb.Color = deep;
                mb.Blob(new Vector3(0f, 1.62f, 0.02f), new Vector3(0.98f, 1.4f, 0.07f), 1, 0.03f, 53);
                mb.Color = glow;
                mb.Blob(new Vector3(0f, 1.6f, -0.02f), new Vector3(0.76f, 1.14f, 0.07f), 1, 0.04f, 54);
                mb.Color = Color.Lerp(glow, Color.white, 0.6f);
                mb.Blob(new Vector3(0f, 1.52f, -0.06f), new Vector3(0.4f, 0.66f, 0.05f), 1, 0.05f, 55);
                // motes drifting up around it
                mb.Color = Color.Lerp(glow, Color.white, 0.4f);
                mb.Blob(new Vector3(-0.75f, 2.75f, -0.4f), new Vector3(0.07f, 0.07f, 0.07f), 0, 0f, 56);
                mb.Blob(new Vector3(0.8f, 2.4f, -0.5f), new Vector3(0.06f, 0.06f, 0.06f), 0, 0f, 57);
                mb.Blob(new Vector3(0.2f, 3.0f, -0.35f), new Vector3(0.05f, 0.05f, 0.05f), 0, 0f, 58);
                mb.Emission = 0f;
                // small standing stones around the dais (the front, towards the camera, stays open)
                float[] angles = { 12f, 62f, 118f, 168f, 214f, 326f };
                for (int i = 0; i < angles.Length; i++)
                {
                    float a = angles[i] * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Cos(a) * 2.45f, 0f, Mathf.Sin(a) * 2.45f);
                    mb.Color = Paint.Shade(MkRock, 0.9f + 0.04f * (i % 3));
                    mb.Push().Translate(p).Rotate(0f, angles[i] * 1.7f, (i % 2 == 0 ? 4f : -5f));
                    mb.TaperedBox(Vector3.zero, new Vector3(0.36f, 0.85f + 0.25f * ((i * 7) % 3), 0.32f), 0.7f);
                    mb.Pop();
                }
                return mb;
            }));
            rig.Model.SetLit = _ => { };
            rig.Light(new Vector3(0f, 1.6f, -0.35f));
            return rig.Done(2.2f);
        }
    }
}
