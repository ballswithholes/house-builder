// 3D prop library: one procedural low-poly model per prop / foreground / chest art key (see Docs/ThreeD.md).
// Interface used by MapView; the models themselves live in the other files of World/Props.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>A built prop. Root sits at the prop's ground pivot, stood up with World3D.Upright (front faces the camera).</summary>
    public sealed class PropModel
    {
        /// <summary>World object (MapView parents, positions, scales and mirrors it).</summary>
        public GameObject Root;
        /// <summary>Top of the model in metres (unscaled).</summary>
        public float Height = 1f;
        /// <summary>Footprint radius in metres (unscaled): shadows, picking fallback.</summary>
        public float Radius = 0.5f;
        /// <summary>Model-space (Y-up, unscaled) bounds: picking, label placement, occlusion fades.</summary>
        public Bounds LocalBounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
        /// <summary>Every LowPoly renderer of the model (highlight rim, dither fades).</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();
        /// <summary>Model-space points where a light belongs (lantern flame, window, fire); MapView adds the lights.</summary>
        public readonly List<Vector3> LightAnchors = new List<Vector3>();
        /// <summary>Chests: the lid hinge (MapView rotates it about local X to open).</summary>
        public Transform Lid;
        /// <summary>Lanterns, lamps, campfires, windows: switches the glowing parts (null when the prop never glows).</summary>
        public Action<bool> SetLit;
        /// <summary>True when the model sways in the wind (its vertices carry wind weights).</summary>
        public bool Sways;
    }

    public static partial class PropModels
    {
        /// <summary>True when a dedicated model exists for the art key (else Create builds a generic stand-in).</summary>
        public static bool Has(string artKey)
        {
            bool has = false;
            if (!string.IsNullOrEmpty(artKey)) TryHas(artKey, ref has);
            return has;
        }

        /// <summary>
        /// Builds the model for a prop art key (prop_*, fg_*, chest art, *_open / *_dark variants). seed varies shape and
        /// colour between instances. Never returns null.
        /// </summary>
        public static PropModel Create(string artKey, int seed = 0) => Build(artKey ?? "", seed);

        // implemented by the model library (other partial files of PropModels)
        static partial void TryBuild(string artKey, int seed, ref PropModel model);
        static partial void TryHas(string artKey, ref bool has);

        static PropModel Build(string artKey, int seed)
        {
            PropModel model = null;
            TryBuild(artKey, seed, ref model);
            return model ?? Placeholder(artKey, seed);
        }

        /// <summary>Generic stand-in: a soft rounded block of roughly the right size.</summary>
        static PropModel Placeholder(string artKey, int seed)
        {
            var mb = new MeshBuilder(seed) { Color = new Color(0.72f, 0.66f, 0.58f), Jitter = 0.06f, AOStrength = 0.3f };
            mb.Blob(new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.6f), 1, 0.12f, seed, 0.6f);
            var go = new GameObject(artKey);
            go.transform.rotation = World3D.Upright;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = MeshCache.Get("prop_placeholder", () => mb.ToMesh("prop_placeholder"));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials3D.WithOutline();
            var m = new PropModel { Root = go, Height = 1f, Radius = 0.6f, LocalBounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 1.2f)) };
            m.Renderers.Add(r);
            return m;
        }
    }
}
