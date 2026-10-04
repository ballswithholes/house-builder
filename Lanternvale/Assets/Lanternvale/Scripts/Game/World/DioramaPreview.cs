// Developer tool for visual QA of maps, units, FX, lighting and audio before the full game flow
// exists. Add it to an empty GameObject (set mapId, or leave empty for the start map) or call
// DioramaPreview.Run("whisperwood") from code. Builds the map from GameRoot's database (falls back
// to a built-in demo map when no map data is loaded) and spawns a line of sample units.
//
// Controls (H toggles the help panel):
//   WASD pan · arrows pan (camera rig) · wheel zoom · T time of day · V fast day/night cycle
//   click select unit / move it · right-click attack towards the cursor · Tab next unit
//   1 bolt  2 arrows  3 frost burst  4 shadow beam  5 heal  6 melee crit  7 target previews
//   8 move range  9 next animation  0 next state visual · F aura + ground ring
//   G open chests / toggle lanterns · E encounters · L labels · M music mood · N next map
using System.Collections;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class DioramaPreview : MonoBehaviour
    {
        public string mapId = "";

        static DioramaPreview instance;
        /// <summary>True while a preview is running (game flow can skip its own boot).</summary>
        public static bool Active => instance != null;

        static readonly string[] Characters = { "char_warrior", "char_hunter", "char_paladin", "char_mage", "char_priest", "char_rogue", "char_warlock", "char_shaman" };
        static readonly string[] Creatures = { "cr_wolf", "cr_mossling", "cr_bandit", "cr_hollow_wisp", "cr_hollow_spirit", "cr_hollow_warden" };
        static readonly string[] Animations = { "attack", "shoot", "cast", "hit", "dodge", "downed", "revive", "death", "revive" };
        static readonly string[] States = { "selected", "active turn", "targetable", "stealthed", "frozen", "poisoned", "polymorphed", "casting", "normal" };

        MapView map;
        readonly List<UnitView> units = new List<UnitView>();
        readonly List<UnitView> extras = new List<UnitView>();   // npcs + encounter enemies
        Transform camTarget;
        UnitView selected, hoveredUnit;
        MapObject hoveredObject;
        List<string> mapIds = new List<string>();
        int mapIndex, timeIndex = 1, animIndex, stateIndex, moodIndex;
        bool showHelp = true, showLabels = true, showPreviews, showRange, showEncounters, fastCycle;
        float fpsSmoothed = 60f;
        float footTimer;
        GameDatabase db;
        string status = "";

        /// <summary>Starts a preview of mapId ("" = config start map / first map).</summary>
        public static DioramaPreview Run(string mapId = "")
        {
            if (instance != null) { instance.Load(mapId); return instance; }
            var go = new GameObject("Diorama Preview");
            var p = go.AddComponent<DioramaPreview>();
            p.mapId = mapId ?? "";
            return p;
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
        }

        IEnumerator Start()
        {
            yield return null; // let GameRoot boot (it auto-creates after the scene loads)
            if (GameRoot.Instance == null) yield return null;
            db = GameRoot.Instance != null ? GameRoot.Instance.Db : null;
            if (db == null)
            {
                try { db = GameRoot.LoadDatabase(); } catch (System.Exception e) { Debug.LogWarning("[Lanternvale] Preview: no database (" + e.Message + ")"); }
            }
            if (CameraRig.Instance == null) CameraRig.Create();
            GameInput.EnsureDriver();
            GameAudio.Init();
            if (db != null) foreach (var id in db.Maps.Keys) mapIds.Add(id);
            mapIds.Sort(System.StringComparer.Ordinal);
            Load(mapId);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Clear();
        }

        // ------------------------------------------------------------------ building

        void Clear()
        {
            FxSystem.HideAll();
            FxSystem.ClearAll();
            FloatingText.Clear();
            foreach (var u in units) if (u != null) u.Dispose();
            foreach (var u in extras) if (u != null) u.Dispose();
            units.Clear();
            extras.Clear();
            selected = hoveredUnit = null;
            hoveredObject = null;
            if (map != null) { map.Dispose(); map = null; }
        }

        void Load(string id)
        {
            Clear();
            MapDef def = null;
            if (db != null)
            {
                if (string.IsNullOrEmpty(id)) id = db.Config != null ? db.Config.startMap : "";
                if (!string.IsNullOrEmpty(id)) db.Maps.TryGetValue(id, out def);
                if (def == null && mapIds.Count > 0) db.Maps.TryGetValue(mapIds[0], out def);
            }
            if (def == null) def = DemoMap();
            mapId = def.id;
            mapIndex = Mathf.Max(0, mapIds.IndexOf(def.id));
            map = MapView.Build(def);

            // sample line of units
            Vector2 spawn = new Vector2(def.width * 0.5f, def.depth * 0.3f);
            foreach (var s in def.spawns) if (s != null && (s.id == "default" || spawn == new Vector2(def.width * 0.5f, def.depth * 0.3f))) spawn = new Vector2(s.pos.x, s.pos.y);
            var keys = new List<string>(Characters);
            keys.AddRange(Creatures);
            float x = spawn.x - (keys.Count - 1) * 1.1f;
            for (int i = 0; i < keys.Count; i++)
            {
                float h = 0f;
                var cr = FindCreatureBySprite(keys[i]);
                if (cr != null) h = cr.size;
                var u = UnitView.Create(keys[i], h, i < Characters.Length ? Ui.Hex("#9fe3a0") : Ui.Hex("#ff8a7a"));
                float w = Mathf.Max(1.9f, u.Height * 0.6f);
                x += i == 0 ? 0f : w * 0.5f;
                u.Teleport(new Vector2(Mathf.Clamp(x, 1f, def.width - 1f), Mathf.Clamp(spawn.y + (i % 2) * 0.6f, 0.5f, def.depth - 0.5f)));
                x += w * 0.5f;
                u.DisplayName = Pretty(keys[i]);
                if (i >= Characters.Length) u.SetFacing(-1);
                units.Add(u);
            }
            Select(units.Count > 0 ? units[0] : null);

            // npcs placed by the map
            if (db != null)
                foreach (var n in def.npcs)
                {
                    if (n == null || !db.Npcs.TryGetValue(n.npc, out var nd)) continue;
                    var u = UnitView.Create(string.IsNullOrEmpty(nd.sprite) ? "npc_villager_a" : nd.sprite, 0f, Ui.Gold);
                    u.Teleport(new Vector2(n.pos.x, n.pos.y));
                    u.SetFacing(n.flip ? -1 : 1);
                    u.DisplayName = nd.name;
                    extras.Add(u);
                }
            if (showEncounters) SpawnEncounters(true);

            if (camTarget == null) camTarget = new GameObject("Preview Camera Target").transform;
            camTarget.SetParent(transform, false);
            camTarget.position = new Vector3(spawn.x, spawn.y, 0f);
            var rig = CameraRig.Instance;
            if (rig != null)
            {
                rig.Follow = camTarget;
                rig.Bounds = map.Bounds;
                rig.ResetPan();
                rig.SnapToTarget();
            }
            Music.Play(Music.MoodForMap(def), 2f);
            status = $"Map '{def.id}' ({def.width}×{def.depth} m, {def.props.Count} props, {def.layers.Count} layers)";
        }

        CreatureDef FindCreatureBySprite(string key)
        {
            if (db == null) return null;
            foreach (var c in db.Creatures.Values) if (c.sprite == key) return c;
            return null;
        }

        void SpawnEncounters(bool on)
        {
            for (int i = extras.Count - 1; i >= 0; i--)
                if (extras[i] != null && extras[i].Tag is string t && t == "enc") { extras[i].Dispose(); extras.RemoveAt(i); }
            if (!on || map == null || db == null) return;
            foreach (var e in map.Def.encounters)
            {
                if (e == null) continue;
                FxSystem.GroundRing(new Vector2(e.pos.x, e.pos.y), e.radius, new Color(1f, 0.45f, 0.4f, 0.8f), 6f);
                foreach (var en in e.enemies)
                {
                    var cd = db.Creature(en.creature);
                    if (cd == null) continue;
                    var u = UnitView.Create(cd.sprite, cd.size, Ui.Hex("#ff8a7a"));
                    u.Teleport(new Vector2(en.pos.x, en.pos.y));
                    u.SetFacing(-1);
                    u.DisplayName = cd.name;
                    u.Tag = "enc";
                    extras.Add(u);
                }
            }
        }

        static string Pretty(string key)
        {
            int i = key.IndexOf('_');
            var s = i >= 0 ? key.Substring(i + 1) : key;
            s = s.Replace('_', ' ');
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        void Select(UnitView u)
        {
            if (selected != null) selected.SetSelected(false);
            selected = u;
            if (selected != null) selected.SetSelected(true);
        }

        UnitView TargetFor(UnitView from)
        {
            if (hoveredUnit != null && hoveredUnit != from) return hoveredUnit;
            UnitView best = null;
            float bd = float.MaxValue;
            foreach (var u in units)
            {
                if (u == from || u.IsDead) continue;
                float d = Vector2.Distance(u.FeetPosition, from.FeetPosition);
                bool enemy = units.IndexOf(u) >= Characters.Length;
                if (enemy) d -= 100f;
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (map == null) return;
            float dt = Time.unscaledDeltaTime;
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(1e-4f, dt), 0.05f);
            var rig = CameraRig.Instance;

            // WASD pans the follow target
            var pan = Vector2.zero;
            if (GameInput.Key(KeyCode.A)) pan.x -= 1f;
            if (GameInput.Key(KeyCode.D)) pan.x += 1f;
            if (GameInput.Key(KeyCode.W)) pan.y += 1f;
            if (GameInput.Key(KeyCode.S)) pan.y -= 1f;
            if (pan != Vector2.zero && camTarget != null)
            {
                var p = (Vector2)camTarget.position + pan.normalized * (10f * dt);
                p.x = Mathf.Clamp(p.x, 0f, map.Def.width);
                p.y = Mathf.Clamp(p.y, -2f, map.Bounds.yMax);
                camTarget.position = new Vector3(p.x, p.y, 0f);
            }

            // hover
            var mouse = rig != null ? rig.MouseWorld : Vector2.zero;
            var hu = GameInput.PointerOverUi ? null : UnitView.Pick(mouse);
            if (hu != hoveredUnit)
            {
                if (hoveredUnit != null) hoveredUnit.SetHovered(false);
                hoveredUnit = hu;
                if (hoveredUnit != null) hoveredUnit.SetHovered(true);
            }
            var ho = hu == null && !GameInput.PointerOverUi ? map.Pick(mouse) : null;
            if (ho != hoveredObject)
            {
                if (hoveredObject != null) map.SetHighlighted(hoveredObject.Id, false);
                hoveredObject = ho;
                if (hoveredObject != null) map.SetHighlighted(hoveredObject.Id, true);
            }

            // clicks
            if (GameInput.WorldClick(0))
            {
                if (hu != null) { Select(hu); Sfx.Play("ui_click"); }
                else if (ho != null && ho.Kind == MapObjectKind.Chest) { map.SetChestOpen(ho.Id, !ho.Opened); Sfx.Play("chest_open", ho.Position); }
                else if (ho != null && ho.IsLantern) { map.SetLanternLit(ho.Id, !ho.LanternLit); Sfx.Play(ho.LanternLit ? "impact_holy" : "debuff", ho.Position); }
                else if (selected != null && !selected.IsDead)
                {
                    var target = new Vector2(Mathf.Clamp(mouse.x, 0.3f, map.Def.width - 0.3f), Mathf.Clamp(mouse.y, 0.2f, map.Def.depth - 0.2f));
                    selected.MoveAlong(new List<Vector2> { selected.FeetPosition, target }, 3.4f, () => FloatingText.Status(target + new Vector2(0f, 2f), "Arrived", Ui.TextMuted));
                    FxSystem.GroundRing(target, 0.35f, Ui.Gold, 0.6f);
                }
            }
            if (GameInput.WorldClick(1) && selected != null)
            {
                float d = selected.PlayAttack(mouse);
                Sfx.Play("swing", selected.FeetPosition);
                StartCoroutine(After(UnitView.AttackHitTime, () => FxSystem.Slash(selected.CenterPosition + (mouse - selected.CenterPosition).normalized * 0.7f, mouse - selected.CenterPosition)));
            }

            // footsteps for walking units (demo of positional SFX)
            footTimer -= dt;
            if (footTimer <= 0f && selected != null && selected.IsMoving) { footTimer = 0.32f; Sfx.Play("footstep_grass", selected.FeetPosition); }

            HandleKeys(mouse);
            UpdatePreviews(mouse);
        }

        void HandleKeys(Vector2 mouse)
        {
            if (GameInput.KeyDown(KeyCode.H)) showHelp = !showHelp;
            if (GameInput.KeyDown(KeyCode.L)) showLabels = !showLabels;
            if (GameInput.KeyDown(KeyCode.T))
            {
                timeIndex = (timeIndex + 1) % 4;
                string[] names = { "dawn", "day", "dusk", "night" };
                map.DayNight.SetHour(DayNight.HourOf(names[timeIndex]));
                status = "Time of day: " + names[timeIndex];
            }
            if (GameInput.KeyDown(KeyCode.V))
            {
                fastCycle = !fastCycle;
                DayNight.HoursPerSecond = fastCycle ? 0.5f : 1f / 60f;
                if (fastCycle && !map.DayNight.Cycle) status = "Fast cycle only animates maps with ambient.dayNightCycle (use T here)";
                else status = fastCycle ? "Fast day/night cycle" : "Normal cycle speed";
                if (fastCycle && !map.DayNight.Cycle) StartCoroutine(SpinClock());
            }
            if (GameInput.KeyDown(KeyCode.M))
            {
                moodIndex = (moodIndex + 1) % Music.Moods.Length;
                Music.Play(Music.Moods[moodIndex]);
                status = "Music: " + Music.Moods[moodIndex];
            }
            if (GameInput.KeyDown(KeyCode.N) && mapIds.Count > 0) Load(mapIds[(mapIndex + 1) % mapIds.Count]);
            if (GameInput.KeyDown(KeyCode.E)) { showEncounters = !showEncounters; SpawnEncounters(showEncounters); }
            if (GameInput.KeyDown(KeyCode.Tab) && units.Count > 0) Select(units[(units.IndexOf(selected) + 1 + units.Count) % units.Count]);
            if (GameInput.KeyDown(KeyCode.G))
            {
                foreach (var c in map.Chests) map.SetChestOpen(c.Id, !c.Opened);
                foreach (var o in map.Interactables) if (o.IsLantern) map.SetLanternLit(o.Id, !o.LanternLit);
                Sfx.Play("chest_open");
            }
            if (selected == null) return;
            var from = selected;
            var target = TargetFor(from);
            if (GameInput.KeyDown(KeyCode.F))
            {
                FxSystem.AuraPulse(from.FeetPosition, 4f, Ui.SchoolColor(School.Holy));
                FxSystem.GroundRing(from.FeetPosition, 2.5f, Ui.SchoolColor(School.Nature), 3f);
                Sfx.Play("buff", from.FeetPosition);
            }
            if (target == null) return;
            if (GameInput.KeyDown(KeyCode.Alpha1))
            {
                from.FaceTowards(target.FeetPosition);
                from.PlayCast(Ui.SchoolColor(School.Fire));
                Sfx.Play("cast_start", from.FeetPosition);
                StartCoroutine(After(UnitView.CastReleaseTime, () =>
                    FxSystem.Projectile(from.CenterPosition, target.CenterPosition, School.Fire, "", 12f, () => Hit(target, School.Fire, Random.Range(40, 90), false))));
            }
            if (GameInput.KeyDown(KeyCode.Alpha2))
            {
                from.PlayShoot(target.FeetPosition);
                Sfx.Play("bow", from.FeetPosition);
                for (int i = 0; i < 3; i++)
                {
                    float delay = UnitView.ShootReleaseTime + i * 0.12f;
                    var off = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.3f));
                    StartCoroutine(After(delay, () => FxSystem.Projectile(from.CenterPosition, target.CenterPosition + off, School.Physical, "fx_arrow", 16f, () => Hit(target, School.Physical, Random.Range(10, 30), false))));
                }
            }
            if (GameInput.KeyDown(KeyCode.Alpha3))
            {
                var c = target.FeetPosition;
                FxSystem.ShowCircle("demo_burst", c, 3f, Ui.SchoolColor(School.Frost));
                from.PlayCast(Ui.SchoolColor(School.Frost));
                StartCoroutine(After(UnitView.CastReleaseTime, () =>
                {
                    FxSystem.Hide("demo_burst");
                    FxSystem.Burst(c, 3f, School.Frost);
                    Sfx.Play("impact_frost", c);
                    foreach (var u in units)
                        if (!u.IsDead && Vector2.Distance(u.FeetPosition, c) <= 3f) { u.PlayHit(); u.SetTint(new Color(0.72f, 0.86f, 1f)); FloatingText.Damage(u.HeadPosition, Random.Range(20, 45), false, School.Frost); }
                }));
            }
            if (GameInput.KeyDown(KeyCode.Alpha4))
            {
                from.SetCasting(Ui.SchoolColor(School.Shadow), 0.6f);
                int h = FxSystem.Beam(from.CenterPosition, target.CenterPosition, School.Shadow, 2.2f);
                Sfx.Play("impact_shadow", target.FeetPosition);
                StartCoroutine(After(2.2f, () => from.StopCasting()));
                for (int i = 1; i <= 3; i++) StartCoroutine(After(i * 0.6f, () => { target.PlayHit(); FloatingText.Damage(target.HeadPosition, Random.Range(8, 16), false, School.Shadow); }));
            }
            if (GameInput.KeyDown(KeyCode.Alpha5))
            {
                FxSystem.HealSparkles(from.FeetPosition, from.Height);
                FloatingText.Heal(from.HeadPosition, Random.Range(60, 140), Random.value < 0.3f);
                FloatingText.Resource(from.HeadPosition, 20, ResourceType.Mana);
                Sfx.Play("heal", from.FeetPosition);
            }
            if (GameInput.KeyDown(KeyCode.Alpha6))
            {
                float dur = from.PlayAttack(target.FeetPosition);
                Sfx.Play("swing", from.FeetPosition);
                StartCoroutine(After(UnitView.AttackHitTime, () =>
                {
                    var dir = target.CenterPosition - from.CenterPosition;
                    FxSystem.Slash(target.CenterPosition - dir.normalized * 0.3f, dir);
                    bool crit = Random.value < 0.5f;
                    if (Random.value < 0.2f) { target.PlayDodge(); FloatingText.Miss(target.HeadPosition, "Dodge"); }
                    else Hit(target, School.Physical, crit ? Random.Range(120, 200) : Random.Range(40, 80), crit);
                }));
            }
            if (GameInput.KeyDown(KeyCode.Alpha7)) { showPreviews = !showPreviews; if (!showPreviews) { FxSystem.Hide("pv_circle"); FxSystem.Hide("pv_cone"); FxSystem.Hide("pv_line"); FxSystem.Hide("pv_path"); } }
            if (GameInput.KeyDown(KeyCode.Alpha8)) { showRange = !showRange; if (showRange) BakeRange(); else FxSystem.Hide("pv_range"); }
            if (GameInput.KeyDown(KeyCode.Alpha9)) PlayNextAnimation(from, target);
            if (GameInput.KeyDown(KeyCode.Alpha0)) NextState(from);
        }

        IEnumerator SpinClock()
        {
            while (fastCycle && map != null)
            {
                map.DayNight.SetHour(map.DayNight.Hour + Time.deltaTime * DayNight.HoursPerSecond);
                yield return null;
            }
        }

        void Hit(UnitView target, School school, int amount, bool crit)
        {
            if (target == null) return;
            target.PlayHit();
            FxSystem.Impact(target.CenterPosition, school);
            FloatingText.Damage(target.HeadPosition, amount, crit, school);
            Sfx.Impact(school, target.FeetPosition, crit);
            if (crit && CameraRig.Instance != null) CameraRig.Instance.Shake(0.08f, 0.2f);
        }

        void PlayNextAnimation(UnitView u, UnitView target)
        {
            string a = Animations[animIndex % Animations.Length];
            animIndex++;
            status = "Animation: " + a;
            switch (a)
            {
                case "attack": u.PlayAttack(target.FeetPosition); Sfx.Play("swing", u.FeetPosition); break;
                case "shoot": u.PlayShoot(target.FeetPosition); Sfx.Play("bow", u.FeetPosition); break;
                case "cast": u.PlayCast(Ui.SchoolColor(School.Arcane)); Sfx.Play("cast_start", u.FeetPosition); break;
                case "hit": u.PlayHit(); Sfx.Play("hit_physical", u.FeetPosition); break;
                case "dodge": u.PlayDodge(); FloatingText.Miss(u.HeadPosition, "Dodge"); break;
                case "downed": u.PlayDowned(); Sfx.Play("death", u.FeetPosition); break;
                case "revive": u.PlayRevive(); FxSystem.HealSparkles(u.FeetPosition, u.Height); Sfx.Play("heal", u.FeetPosition); break;
                case "death": u.PlayDeath(); Sfx.Play("death", u.FeetPosition); break;
            }
        }

        void NextState(UnitView u)
        {
            string s = States[stateIndex % States.Length];
            stateIndex++;
            status = "State: " + s;
            u.SetActiveTurn(false); u.SetTargetable(null); u.SetStealthed(false); u.SetTint(Color.white); u.SetPolymorphed(false); u.StopCasting();
            switch (s)
            {
                case "active turn": u.SetActiveTurn(true); break;
                case "targetable": u.SetTargetable(new Color(1f, 0.45f, 0.4f)); break;
                case "stealthed": u.SetStealthed(true); break;
                case "frozen": u.SetTint(new Color(0.7f, 0.85f, 1f)); break;
                case "poisoned": u.SetTint(new Color(0.72f, 0.95f, 0.6f)); break;
                case "polymorphed": u.SetPolymorphed(true); FloatingText.Status(u.HeadPosition, "Polymorphed", Ui.SchoolColor(School.Arcane)); break;
                case "casting": u.SetCasting(Ui.SchoolColor(School.Holy), 0.7f); break;
            }
        }

        void BakeRange()
        {
            if (selected == null || map == null) return;
            var start = selected.FeetPosition;
            const float max = 9f;
            var def = map.Def;
            System.Func<Vector2, bool> canReach = p =>
            {
                if (p.x < 0.25f || p.y < 0.25f || p.x > def.width - 0.25f || p.y > def.depth - 0.25f) return false;
                if (Vector2.Distance(p, start) > max) return false;
                foreach (var prop in def.props)
                {
                    if (prop?.collider == null || prop.collider.w <= 0f) continue;
                    float s = prop.scale > 0f ? prop.scale : 1f;
                    var c = new Vector2(prop.pos.x + prop.collider.offset.x * s * (prop.flip ? -1f : 1f), prop.pos.y + prop.collider.offset.y * s);
                    float rx = prop.collider.w * s * 0.5f, ry = Mathf.Max(0.05f, prop.collider.h * s * 0.5f);
                    float dx = (p.x - c.x) / rx, dy = (p.y - c.y) / ry;
                    if (dx * dx + dy * dy < 1f) return false;
                }
                return true;
            };
            FxSystem.ShowMoveRange("pv_range", canReach, new Rect(start.x - max - 1f, start.y - max - 1f, max * 2f + 2f, max * 2f + 2f), 0.5f, new Color(0.62f, 0.95f, 0.9f, 0.95f));
        }

        readonly List<Vector2> pathTmp = new List<Vector2>();

        void UpdatePreviews(Vector2 mouse)
        {
            if (!showPreviews || selected == null) return;
            var o = selected.FeetPosition;
            FxSystem.ShowCircle("pv_circle", mouse, 2.5f, Ui.SchoolColor(School.Fire));
            FxSystem.ShowCone("pv_cone", o, mouse - o, 5f, 60f, Ui.SchoolColor(School.Frost));
            FxSystem.ShowLine("pv_line", o, o + (mouse - o).normalized * 8f, 1.2f, Ui.SchoolColor(School.Arcane));
            pathTmp.Clear();
            pathTmp.Add(o);
            pathTmp.Add(Vector2.Lerp(o, mouse, 0.5f) + new Vector2(0f, 1.5f));
            pathTmp.Add(mouse);
            FxSystem.ShowPath("pv_path", pathTmp, Ui.Gold, true);
        }

        static IEnumerator After(float seconds, System.Action a)
        {
            yield return new WaitForSeconds(seconds);
            a?.Invoke();
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            if (map == null) return;
            Ui.BeginFrame();
            var rig = CameraRig.Instance;
            if (showLabels && rig != null && Event.current.type == EventType.Repaint)
            {
                var style = Ui.CenterSmall;
                foreach (var u in units) Label(rig, u.NameplatePosition, u.DisplayName, style);
                foreach (var u in extras) Label(rig, u.NameplatePosition, u.DisplayName, style);
                if (hoveredObject != null)
                    Label(rig, hoveredObject.LabelPosition, string.IsNullOrEmpty(hoveredObject.Label) ? hoveredObject.Id : hoveredObject.Label, Ui.Center);
                foreach (var t in map.Transitions) Label(rig, t.LabelPosition, (t.Locked ? "🔒 " : "→ ") + t.Label, style);
            }

            float w = 520f;
            var r = new Rect(16f, 16f, w, showHelp ? 420f : 64f);
            Ui.Panel(r, Ui.InkPanelSoft);
            var dn = map.DayNight;
            GUI.Label(new Rect(r.x + 16f, r.y + 10f, w - 32f, 26f),
                $"<b>{map.Def.name}</b>  ·  {dn.Hour:00.0}h {dn.Phase}  ·  {fpsSmoothed:0} fps  ·  fx {FxSystem.ActiveSprites}  ·  lit {(Lighting2D.IsLit ? "URP 2D" : "overlay")}", Ui.LabelSmall);
            GUI.Label(new Rect(r.x + 16f, r.y + 34f, w - 32f, 24f), status, Ui.LabelSmall);
            if (!showHelp) return;
            string help =
                "<b>WASD</b>/arrows pan · wheel zoom · <b>T</b> time · <b>V</b> fast cycle · <b>H</b> help\n" +
                "<b>Click</b> select unit / walk / open chest / toggle lantern\n" +
                "<b>Right-click</b> attack towards cursor · <b>Tab</b> next unit\n" +
                "<b>1</b> fire bolt  <b>2</b> arrows  <b>3</b> frost burst  <b>4</b> shadow beam\n" +
                "<b>5</b> heal  <b>6</b> melee  <b>7</b> target previews  <b>8</b> move range\n" +
                "<b>9</b> next animation  <b>0</b> next state visual  <b>F</b> aura pulse\n" +
                "<b>G</b> chests/lanterns  <b>E</b> encounters  <b>L</b> labels\n" +
                "<b>M</b> music mood (" + (string.IsNullOrEmpty(Music.Mood) ? "off" : Music.Mood) + ")  <b>N</b> next map (" + Mathf.Max(1, mapIds.Count) + ")\n" +
                "Selected: " + (selected != null ? selected.DisplayName : "-");
            GUI.Label(new Rect(r.x + 16f, r.y + 64f, w - 32f, r.height - 70f), help, Ui.LabelSmall);
        }

        static void Label(CameraRig rig, Vector2 world, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var g = rig.WorldToGui(world) / Ui.Scale;
            Ui.Shadowed(new Rect(g.x - 120f, g.y - 14f, 240f, 28f), text, style);
        }

        // ------------------------------------------------------------------ demo map

        /// <summary>A small meadow village used when no map data is loaded yet.</summary>
        public static MapDef DemoMap()
        {
            var m = new MapDef
            {
                id = "preview_meadow", name = "Preview Meadow", subtitle = "presentation test",
                width = 44f, depth = 16f, skyTop = "#8fc4e8", skyBottom = "#fdf0d4", ground = "ground_meadow", groundTile = 8f,
                ambient = new AmbientDef { fireflies = true, leaves = true, pollen = true, mist = true, embers = true, timeOfDay = "day" },
                music = "village",
            };
            m.layers.Add(new ParallaxLayerDef { art = "bg_clouds", parallax = 0.04f, y = 22f, height = 6f, scrollSpeed = 0.15f });
            m.layers.Add(new ParallaxLayerDef { art = "bg_mountains_far", parallax = 0.12f, y = 15.2f, height = 9f });
            m.layers.Add(new ParallaxLayerDef { art = "bg_hills_far", parallax = 0.3f, y = 14.8f, height = 7f });
            m.layers.Add(new ParallaxLayerDef { art = "bg_hills_near", parallax = 0.55f, y = 14.6f, height = 6f });
            void P(string art, float x, float y, float scale = 1f, bool sway = false, bool flip = false, LightDef light = null, string interact = "", float cw = 0f, float ch = 0f)
            {
                var p = new PropDef { art = art, pos = new Lanternvale.Util.Vec2(x, y), scale = scale, sway = sway, flip = flip, light = light, interact = interact, text = interact };
                p.collider = new ColliderDef { w = cw, h = ch };
                m.props.Add(p);
            }
            P("decal_path_dirt", 12f, 6f, 1.2f); P("decal_path_dirt", 22f, 6.5f, 1.2f, false, true); P("decal_path_dirt", 32f, 6f, 1.2f);
            P("decal_flowers", 8f, 3f); P("decal_flowers", 29f, 10f);
            P("prop_cottage_a", 9f, 11f, 1f, false, false, new LightDef { color = "#ffcf8a", radius = 3.5f, intensity = 0.8f, offset = new Lanternvale.Util.Vec2(0.8f, 2f), nightOnly = true }, "", 4.5f, 2f);
            P("prop_cottage_b", 19f, 12.5f, 1f, false, true, null, "", 4f, 2f);
            P("prop_inn", 31f, 11.5f, 1f, false, false, new LightDef { color = "#ffd28a", radius = 4.5f, intensity = 0.9f, offset = new Lanternvale.Util.Vec2(-1.2f, 2.2f), flicker = true, nightOnly = true }, "sign_inn", 6f, 2.5f);
            P("prop_tree_oak", 3f, 13f, 1f, true, false, null, "", 1.2f, 0.8f);
            P("prop_tree_birch", 25f, 13.8f, 0.9f, true, false, null, "", 0.8f, 0.6f);
            P("prop_tree_pine", 40f, 12.5f, 1f, true, false, null, "", 1f, 0.8f);
            P("prop_tree_great", 38f, 4.5f, 0.8f, true, true, null, "", 2f, 1.2f);
            P("prop_well", 22f, 8f, 1f, false, false, null, "well", 1.8f, 1.2f);
            P("prop_lamp_post", 15f, 6.2f, 1f, false, false, new LightDef { color = "#ffcf7a", radius = 4f, intensity = 1f, offset = new Lanternvale.Util.Vec2(0.3f, 2.4f), flicker = true, nightOnly = true }, "", 0.3f, 0.3f);
            P("prop_lamp_post", 28f, 5.8f, 1f, false, true, new LightDef { color = "#ffcf7a", radius = 4f, intensity = 1f, offset = new Lanternvale.Util.Vec2(0.3f, 2.4f), flicker = true, nightOnly = true }, "", 0.3f, 0.3f);
            P("prop_spirit_lantern", 6f, 7f, 1f, false, false, null, "lantern_meadow", 0.8f, 0.6f);
            P("prop_spirit_lantern_dark", 35f, 8f, 1f, false, false, null, "lantern_hill", 0.8f, 0.6f);
            P("prop_campfire", 17f, 3f, 1f, false, false, new LightDef { color = "#ffb060", radius = 4.5f, intensity = 1.1f, offset = new Lanternvale.Util.Vec2(0f, 0.4f), flicker = true }, "", 1f, 0.6f);
            P("prop_fence", 11f, 9f); P("prop_fence", 14f, 9f);
            P("prop_bush_a", 1.5f, 8f, 1f, true); P("prop_bush_b", 26f, 9.5f, 1f, true); P("prop_bush_a", 42f, 7f, 1f, true, true);
            P("prop_rock_large", 36f, 2.2f, 1f, false, false, null, "", 1.8f, 1f); P("prop_rock_small", 13f, 1.6f); P("prop_stump", 4f, 4f);
            P("prop_barrel", 34.5f, 9.8f); P("prop_crate", 35.6f, 9.6f); P("prop_noticeboard", 26f, 7.5f, 1f, false, false, null, "noticeboard", 1.2f, 0.4f);
            P("prop_mushrooms", 39f, 6.5f, 1f, false, false, new LightDef { color = "#a9f0d0", radius = 1.6f, intensity = 0.6f, offset = new Lanternvale.Util.Vec2(0f, 0.3f), nightOnly = true });
            void F(string art, float x, float y, bool flip = false) => m.foreground.Add(new PropDef { art = art, pos = new Lanternvale.Util.Vec2(x, y), sway = true, flip = flip });
            F("fg_grass_a", 2f, -0.4f); F("fg_flowers_a", 7.5f, -0.6f); F("fg_ferns", 14f, -0.5f, true); F("fg_grass_b", 20f, -0.3f);
            F("fg_stones_a", 26f, -0.6f); F("fg_flowers_b", 31f, -0.4f, true); F("fg_grass_a", 37f, -0.5f); F("fg_ferns", 43f, -0.6f);
            m.chests.Add(new ChestDef { id = "chest_demo", pos = new Lanternvale.Util.Vec2(41f, 9.5f) });
            m.chests.Add(new ChestDef { id = "chest_demo2", pos = new Lanternvale.Util.Vec2(5f, 2.2f), lockCheck = new CheckDef() });
            m.transitions.Add(new TransitionDef { id = "to_forest", pos = new Lanternvale.Util.Vec2(43f, 6f), size = new Lanternvale.Util.Vec2(2f, 4f), targetMap = "whisperwood", label = "Whisperwood" });
            m.transitions.Add(new TransitionDef { id = "to_shrine", pos = new Lanternvale.Util.Vec2(1f, 6f), size = new Lanternvale.Util.Vec2(2f, 4f), targetMap = "shrine", label = "Old Lantern Shrine", requireFlag = "never" });
            m.transitions.Add(new TransitionDef { id = "to_square", pos = new Lanternvale.Util.Vec2(22f, 4f), size = new Lanternvale.Util.Vec2(2.5f, 2.5f), targetMap = "lanternvale", label = "Village square" });
            m.regions.Add(new RegionDef { id = "region_well", pos = new Lanternvale.Util.Vec2(22f, 8f), size = new Lanternvale.Util.Vec2(6f, 4f), text = "The old well" });
            m.spawns.Add(new SpawnPointDef { id = "default", pos = new Lanternvale.Util.Vec2(20f, 4.5f) });
            return m;
        }
    }
}
