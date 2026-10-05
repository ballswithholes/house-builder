// World-anchored combat text (damage, heals, misses, resources, status words) drawn with IMGUI and
// the Ui toolkit fonts. Texts rise (world up is −Z) and fade; crits pop; texts spawned close together stack upwards.
// The anchor is a 3D world point projected through the perspective camera every repaint: texts behind the camera are
// skipped, and texts scale a little with distance (bigger up close). Drawn at GUI.depth 10, i.e. underneath the
// regular UI (depth 0).
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class FloatingText : MonoBehaviour
    {
        public static FloatingText Instance { get; private set; }

        /// <summary>GUI depth of the layer (higher = further back; main UI should use 0).</summary>
        public static int GuiDepth = 10;
        /// <summary>Global size multiplier (accessibility option).</summary>
        public static float SizeScale = 1f;
        /// <summary>Camera distance at which texts have their nominal size (≈ the default zoom); nearer is a bit bigger.</summary>
        public static float ReferenceDistance = 18f;

        sealed class Entry
        {
            public Vector3 world;
            public string text;
            public Color color;
            public float scale, age, life, stack, drift;
            public bool crit;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly Stack<Entry> pool = new Stack<Entry>();
        GUIStyle style;

        public static FloatingText Get()
        {
            if (Instance == null) Instance = PresentationHost.Ensure<FloatingText>();
            return Instance;
        }

        void Awake()
        {
            Instance = this;
            useGUILayout = false;
        }

        // ------------------------------------------------------------------ API

        /// <summary>Spawns a floating text at a world position (typically UnitView.HeadPosition).</summary>
        public static void Spawn(Vector3 worldPos, string text, Color color, float scale = 1f, bool crit = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            Get().Add(worldPos, text, color, scale, crit);
        }

        /// <summary>Damage number: white (school-tinted for spells), crits yellow, bigger and popping.</summary>
        public static void Damage(Vector3 worldPos, int amount, bool crit = false, School school = School.Physical)
        {
            Color c = crit ? new Color(1f, 0.86f, 0.32f) : school == School.Physical ? Color.white : Color.Lerp(Color.white, Ui.SchoolColor(school), 0.55f);
            Spawn(worldPos, crit ? amount + "!" : amount.ToString(), c, crit ? 1.25f : 1f, crit);
        }

        /// <summary>Heal number in green with a plus.</summary>
        public static void Heal(Vector3 worldPos, int amount, bool crit = false)
        {
            Spawn(worldPos, "+" + amount, crit ? new Color(0.7f, 1f, 0.55f) : Ui.Good, crit ? 1.2f : 1f, crit);
        }

        /// <summary>"Miss", "Dodge", "Parry", "Block", "Resist", "Immune", "Absorb", "Evade"…</summary>
        public static void Miss(Vector3 worldPos, string word)
        {
            Spawn(worldPos, word, new Color(0.88f, 0.86f, 0.95f), 0.85f);
        }

        /// <summary>Resource gain such as "+20 Rage" coloured by resource.</summary>
        public static void Resource(Vector3 worldPos, int amount, ResourceType type)
        {
            string name = type == ResourceType.Mana ? "Mana" : type.ToString();
            Spawn(worldPos, (amount >= 0 ? "+" : "") + amount + " " + name, Color.Lerp(Ui.ResourceColor(type), Color.white, 0.25f), 0.8f);
        }

        /// <summary>Status word ("Stunned", "Polymorphed", "Level Up!").</summary>
        public static void Status(Vector3 worldPos, string word, Color color)
        {
            Spawn(worldPos, word, color, 0.9f);
        }

        public static void Clear()
        {
            if (Instance == null) return;
            foreach (var e in Instance.entries) Instance.pool.Push(e);
            Instance.entries.Clear();
        }

        void Add(Vector3 world, string text, Color color, float scale, bool crit)
        {
            // stack above texts recently spawned near the same spot
            float stack = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                var o = entries[i];
                if (o.age > 0.7f) continue;
                if ((o.world - world).sqrMagnitude > 1.2f * 1.2f) continue;
                stack = Mathf.Max(stack, o.stack + 0.42f * Mathf.Max(1f, o.scale) * (1f - o.age / 0.7f) + 0.1f);
            }
            var e = pool.Count > 0 ? pool.Pop() : new Entry();
            e.world = world;
            e.text = text;
            e.color = color;
            e.scale = Mathf.Max(0.3f, scale);
            e.crit = crit;
            e.age = 0f;
            e.life = crit ? 1.55f : 1.2f;
            e.stack = Mathf.Min(stack, 2.5f);
            e.drift = Random.Range(-0.18f, 0.18f);
            entries.Add(e);
        }

        // ------------------------------------------------------------------ update / draw

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                e.age += dt;
                if (e.age >= e.life)
                {
                    entries[i] = entries[entries.Count - 1];
                    entries.RemoveAt(entries.Count - 1);
                    pool.Push(e);
                }
            }
        }

        static readonly Color Outline = new Color(0.12f, 0.08f, 0.16f, 1f);

        void OnGUI()
        {
            GUI.depth = GuiDepth;
            if (entries.Count == 0) return;
            var ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            var cam = PresentationHost.Cam;
            if (cam == null) return;
            Ui.BeginFrame();
            if (style == null || style.font != Ui.BoldFont)
            {
                style = new GUIStyle(Ui.Number) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false, clipping = TextClipping.Overflow };
                style.font = Ui.BoldFont;
            }
            float inv = 1f / Mathf.Max(0.01f, Ui.Scale);
            var ct = cam.transform;
            var camPos = ct.position;
            var camRight = ct.right;
            var oldColor = GUI.color;
            GUI.color = Color.white;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                float t = e.age / e.life;
                float rise = 1f - (1f - t) * (1f - t);
                // rises (world up is −Z) and drifts sideways as seen on screen
                var w = e.world + camRight * (e.drift * rise) + World3D.Up * (0.15f + e.stack + rise * 0.95f);
                var sp = cam.WorldToScreenPoint(w);
                if (sp.z <= 0.05f) continue;   // behind the camera: its projection is meaningless
                var gui = new Vector2(sp.x, Screen.height - sp.y) * inv;
                // a little bigger up close, a little smaller far away (reference: the default view, ≈ 18 m)
                float dist = Vector3.Distance(camPos, w);
                float near = Mathf.Clamp(Mathf.Pow(ReferenceDistance / Mathf.Max(1f, dist), 0.35f), 0.82f, 1.22f);
                float pop = 1f;
                if (e.crit && e.age < 0.2f) { float k = 1f - e.age / 0.2f; pop = 1f + 0.75f * k * k; }
                else if (e.age < 0.1f) pop = 1f + 0.25f * (1f - e.age / 0.1f);
                float alpha = 1f - Mathf.Clamp01((t - 0.62f) / 0.38f);
                int size = Mathf.Clamp(Mathf.RoundToInt((e.crit ? 36f : 27f) * e.scale * pop * near * SizeScale), 8, 120);
                style.fontSize = size;
                var r = new Rect(gui.x - 200f, gui.y - size, 400f, size * 2f);

                var oc = Outline;
                oc.a = 0.85f * alpha;
                style.normal.textColor = oc;
                float o = Mathf.Max(1.5f, size * 0.07f);
                GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), e.text, style);
                GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), e.text, style);
                GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), e.text, style);
                GUI.Label(new Rect(r.x, r.y + o * 1.4f, r.width, r.height), e.text, style);
                var c = e.color;
                c.a *= alpha;
                style.normal.textColor = c;
                GUI.Label(r, e.text, style);
            }
            GUI.color = oldColor;
        }
    }
}
