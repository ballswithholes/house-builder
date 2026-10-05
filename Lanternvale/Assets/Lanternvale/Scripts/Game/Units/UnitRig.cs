// Skeletons and per-recipe model data of the 3D units (characters, creatures, pets, demons, totems).
//
// Every unit is ONE rigidly skinned mesh (MeshBuilder.Bone per part, ToMesh(name, bindposes)) drawn by a
// SkinnedMeshRenderer with { LowPoly, Outline } → 2 draw calls. Bones are child transforms of the unit's "Body"
// transform (which carries the yaw (World3D.Yaw) and the size scale). In the bind pose every bone has an IDENTITY
// rotation, so bindpose[i] = Translate(−Bind[i]) and an animation rotation is simply the bone's rotation away from
// the bind pose, expressed in model space (Y up, +Z forward, +X = the character's right).
//
// Rig families (bone index constants below):
//   Biped  — humanoids, mosslings, treant, imps, succubus, infernal; also floaters (wisp, hollow spirit, voidwalker,
//            forest spirit) and the owl, which simply leave the leg bones empty.
//   Quad   — wolves, boars, cat, bear, felhunter, the warden stag, the polymorph sheep.
//   Spider — eight legs.
//   Static — totems, training dummy, traps, lightwell (base + wobbling top).
using UnityEngine;

namespace Lanternvale.Game
{
    public enum UnitRigKind { Biped, Quad, Spider, Static }

    /// <summary>Animation flavour on top of the rig.</summary>
    public enum UnitGait { Humanoid, Heavy, Small, Floater, Flier, Quad, Spider, Static }

    /// <summary>How the hands carry their items when idle / walking.</summary>
    public enum UnitHold { Relaxed, OneHand, Daggers, Staff, Bow, Shoulder, Book, Cane, Carry, Spear, Claws }

    /// <summary>Melee motion (PlayAttack).</summary>
    public enum UnitStrike { Fist, Sword, Greatsword, Daggers, Mace, Staff, Spear, Claw, Slam, Whip, Bite, Charge, Swipe, Stomp, Fangs, Lunge, Bump }

    /// <summary>Ranged motion (PlayShoot).</summary>
    public enum UnitRanged { Throw, Bow, Point, Howl, Pulse }

    /// <summary>Bone indices of the biped rig.</summary>
    public static class BB
    {
        public const int Hips = 0, Spine = 1, Chest = 2, Neck = 3, Head = 4;
        public const int ArmUL = 5, ArmLL = 6, HandL = 7, ArmUR = 8, ArmLR = 9, HandR = 10;
        public const int LegUL = 11, LegLL = 12, FootL = 13, LegUR = 14, LegLR = 15, FootR = 16;
        public const int SkirtL = 17, SkirtR = 18, SkirtB = 19, SkirtF = 20, Cape = 21, HairB = 22, Tail = 23, WingL = 24, WingR = 25;
        public const int Count = 26;

        public static readonly int[] Parent =
        {
            -1, Hips, Spine, Chest, Neck,
            Chest, ArmUL, ArmLL, Chest, ArmUR, ArmLR,
            Hips, LegUL, LegLL, Hips, LegUR, LegLR,
            Hips, Hips, Hips, Hips, Chest, Head, Hips, Chest, Chest,
        };

        public static readonly string[] Names =
        {
            "Hips", "Spine", "Chest", "Neck", "Head", "ArmUL", "ArmLL", "HandL", "ArmUR", "ArmLR", "HandR",
            "LegUL", "LegLL", "FootL", "LegUR", "LegLR", "FootR", "SkirtL", "SkirtR", "SkirtB", "SkirtF", "Cape",
            "HairB", "Tail", "WingL", "WingR",
        };

        public static int ArmU(int side) => side < 0 ? ArmUL : ArmUR;
        public static int ArmL(int side) => side < 0 ? ArmLL : ArmLR;
        public static int Hand(int side) => side < 0 ? HandL : HandR;
        public static int LegU(int side) => side < 0 ? LegUL : LegUR;
        public static int LegL(int side) => side < 0 ? LegLL : LegLR;
        public static int Foot(int side) => side < 0 ? FootL : FootR;
    }

    /// <summary>Bone indices of the quadruped rig (Hips = rear body, Chest = front body).</summary>
    public static class QB
    {
        public const int Hips = 0, Chest = 1, Neck = 2, Head = 3, Jaw = 4;
        public const int FLU = 5, FLL = 6, FLF = 7, FRU = 8, FRL = 9, FRF = 10;
        public const int BLU = 11, BLL = 12, BLF = 13, BRU = 14, BRL = 15, BRF = 16;
        public const int Tail1 = 17, Tail2 = 18, Back = 19;
        public const int Count = 20;

        public static readonly int[] Parent =
        {
            -1, Hips, Chest, Neck, Head,
            Chest, FLU, FLL, Chest, FRU, FRL,
            Hips, BLU, BLL, Hips, BRU, BRL,
            Hips, Tail1, Chest,
        };

        public static readonly string[] Names =
        {
            "Hips", "Chest", "Neck", "Head", "Jaw", "FLU", "FLL", "FLF", "FRU", "FRL", "FRF",
            "BLU", "BLL", "BLF", "BRU", "BRL", "BRF", "Tail1", "Tail2", "Back",
        };
    }

    /// <summary>Bone indices of the spider rig: body, abdomen, fangs and 8 two-bone legs.</summary>
    public static class SB
    {
        public const int Body = 0, Abdomen = 1, Fangs = 2;
        public const int Count = 3 + 16;
        public static int Upper(int leg) => 3 + leg * 2;
        public static int Lower(int leg) => 4 + leg * 2;

        public static readonly int[] Parent = MakeParents();
        public static readonly string[] Names = MakeNames();

        static int[] MakeParents()
        {
            var p = new int[Count];
            p[0] = -1; p[1] = Body; p[2] = Body;
            for (int i = 0; i < 8; i++) { p[Upper(i)] = Body; p[Lower(i)] = Upper(i); }
            return p;
        }

        static string[] MakeNames()
        {
            var n = new string[Count];
            n[0] = "Body"; n[1] = "Abdomen"; n[2] = "Fangs";
            for (int i = 0; i < 8; i++) { n[Upper(i)] = "LegU" + i; n[Lower(i)] = "LegL" + i; }
            return n;
        }
    }

    /// <summary>Bone indices of static models (totems, dummy): a base and a top that wobbles.</summary>
    public static class TB
    {
        public const int Base = 0, Top = 1, Count = 2;
        public static readonly int[] Parent = { -1, Base };
        public static readonly string[] Names = { "Base", "Top" };
    }

    /// <summary>One IK leg: upper joint → lower joint → ankle/paw (model space, metres at natural size).</summary>
    public sealed class UnitLeg
    {
        public int Upper, Lower, Foot = -1;
        /// <summary>Parent body bone of the upper joint (hips, chest, spider body).</summary>
        public int Root;
        /// <summary>Segment lengths (upper, lower).</summary>
        public float A, B;
        /// <summary>Rest ankle/paw position in model space (y = ankle height above the ground).</summary>
        public Vector3 Rest;
        /// <summary>Direction the middle joint points to (model space).</summary>
        public Vector3 Pole = Vector3.forward;
        /// <summary>Gait phase offset 0..1.</summary>
        public float Phase;
        public int Side;
        /// <summary>Foot geometry (bipeds): heel behind / ball in front of the ankle, for the heel-toe roll.</summary>
        public float HeelBack, BallFwd;
    }

    /// <summary>Everything a recipe produces: shared mesh + skeleton + animation hints. Built once per key.</summary>
    public sealed class UnitModel
    {
        public string Key;
        public Mesh Mesh;
        public UnitRigKind Rig;
        public UnitGait Gait;
        public Vector3[] Bind;
        public int[] Parent;
        public string[] Names;
        public UnitLeg[] Legs = new UnitLeg[0];

        /// <summary>Natural height (top of the head / ears / crown) in metres.</summary>
        public float Height = 1.75f;
        /// <summary>Footprint radius (shadow, rings).</summary>
        public float Radius = 0.36f;
        /// <summary>Extra half-length along the facing (quadrupeds): the shadow is stretched along the body.</summary>
        public float HalfLength;

        // biped / quad body
        public float HipY = 0.9f;        // rest height of the Hips bone
        public float LegLength = 0.85f;  // hip joint → ground (gait scaling)
        public float LieHeight = 0.14f;  // Hips height when lying on the ground

        // look & motion hints
        public UnitHold HoldR = UnitHold.Relaxed, HoldL = UnitHold.Relaxed;
        public bool Shield;
        public UnitStrike Strike = UnitStrike.Fist;
        public UnitRanged Ranged = UnitRanged.Throw;
        public bool TwoHanded;
        public float FloatHeight;
        public float BaseFade = 1f;
        public float Heavy;              // 0 light … 1 lumbering
        public float StrideK = 1f;
        public float MaxCadence = 3.2f;  // full strides per second
        public float TurnRate = 720f;
        public bool Dust = true;
        public Color DustColor = new Color(0.86f, 0.78f, 0.64f, 0.32f);
        public float Breath = 1f;
        public bool Static;
        /// <summary>Totems/traps pop in with a little bounce when created.</summary>
        public bool SpawnPop;
        /// <summary>Wings flap (owl, imp, succubus).</summary>
        public bool Wings;
        public bool WingsFlap;

        // anchors (bone + local offset in bone space, model units)
        public int CastBone = -1;
        public Vector3 CastOffset;
        public int HeadBone;
        public Vector3 HeadTop;
        public int CenterBone;
        public Vector3 CenterOffset;
        /// <summary>Bones whose projected positions bound the body for screen picking.</summary>
        public int[] PickBones;
        public float PickPad = 0.16f;

        /// <summary>Squash of the whole model when it dies/lies (static models topple instead).</summary>
        public int BoneCount => Bind != null ? Bind.Length : 0;

        public Vector3 LocalOffset(int bone) => Bind[bone] - (Parent[bone] >= 0 ? Bind[Parent[bone]] : Vector3.zero);
    }
}
