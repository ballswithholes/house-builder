// One manager animates every swaying transform (grass, ferns, trees, banners) with a gentle wind:
// a rotation about the sprite's pivot (its base) plus a tiny horizontal squash, per-object phase,
// a slow gust envelope and a travelling wave across x. Off-screen entries are skipped.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    [DefaultExecutionOrder(900)]
    public sealed class SwayManager : MonoBehaviour
    {
        public static SwayManager Instance { get; private set; }

        /// <summary>Global wind strength multiplier (0 = still air).</summary>
        public static float Wind = 1f;

        struct Entry
        {
            public Transform t;
            public object owner;
            public float phase, freq, amp, squash, x;
            public Vector3 baseScale;
        }

        readonly List<Entry> entries = new List<Entry>();

        public static SwayManager Get()
        {
            if (Instance == null) Instance = PresentationHost.Ensure<SwayManager>();
            return Instance;
        }

        void Awake() { Instance = this; }

        /// <summary>
        /// Registers a transform whose pivot is at its base. heightMetres tunes the motion: tall
        /// things sway slowly by a small angle, small tufts quickly by a larger one.
        /// </summary>
        public void Add(Transform t, float heightMetres, object owner, float strength = 1f)
        {
            if (t == null) return;
            heightMetres = Mathf.Max(0.3f, heightMetres);
            var e = new Entry
            {
                t = t,
                owner = owner,
                phase = Random.value * Mathf.PI * 2f,
                freq = Mathf.Lerp(1.9f, 0.55f, Mathf.InverseLerp(0.5f, 10f, heightMetres)) * Random.Range(0.85f, 1.15f),
                amp = Mathf.Lerp(4.5f, 0.9f, Mathf.InverseLerp(0.5f, 10f, heightMetres)) * strength,
                squash = heightMetres < 2f ? 0.035f : 0.012f,
                x = t.position.x,
                baseScale = t.localScale,
            };
            entries.Add(e);
        }

        /// <summary>Removes every entry registered by owner (call when a map is destroyed).</summary>
        public void RemoveOwner(object owner)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (ReferenceEquals(entries[i].owner, owner) || entries[i].t == null) entries.RemoveAt(i);
        }

        void LateUpdate()
        {
            if (entries.Count == 0) return;
            float time = Time.time;
            float gust = 0.55f + 0.45f * Mathf.PerlinNoise(time * 0.11f, 0.37f) + 0.25f * Mathf.Max(0f, Mathf.Sin(time * 0.31f) - 0.6f);
            gust *= Wind;
            var view = PresentationHost.ViewRect(12f);
            bool removed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.t == null) { removed = true; continue; }
                if (e.x < view.xMin || e.x > view.xMax) continue;
                float wave = Mathf.Sin(time * e.freq + e.phase + e.x * 0.18f);
                float wave2 = Mathf.Sin(time * e.freq * 2.3f + e.phase * 1.7f) * 0.25f;
                float s = (wave + wave2) * gust;
                e.t.localRotation = Quaternion.Euler(0f, 0f, s * e.amp);
                float sq = 1f + Mathf.Abs(s) * e.squash;
                e.t.localScale = new Vector3(e.baseScale.x * sq, e.baseScale.y * (2f - sq), e.baseScale.z);
            }
            if (removed)
                for (int i = entries.Count - 1; i >= 0; i--)
                    if (entries[i].t == null) entries.RemoveAt(i);
        }
    }
}
