// `units` mode: contact sheets of the unit models (Scripts/Game/Units) posed by the game's own animator.
// One row per unit; columns: the game camera (CameraRig at the default zoom, unit facing right like SetFacing(+1)),
// a 3/4 idle, a mid-stride walk/trot frame (the widest foot spread of one cycle at walking speed), the attack
// wind-up and strike, a shot (release) and a cast (release), each beside a 1.75 m reference figure (slate) on a 1 m
// checker. Shading is the map renderer's (noon light, ink outlines at the game's 1.9 px relative to on-screen size).
//   units <out.png> [keys|group|all] [--tile 240] [--ss 2] [--hour 12.5] [--speed 3.4]
//         [--view sheet|game|spin] [--pose idle|walk|wind|strike|shoot|cast] [--facing 1|-1] [--pitch deg]
// `all` writes one page per group (<out>_<group>.png); a group name or a key list writes one page. A key may name a
// look variation: `npc_child#2`, or `npc_child@child_nell` for the one an NPC id gets in the game.
// `--view game` puts every column at the game camera: idle and walk facing right and left (SetFacing ±1), then the
// four action frames facing right (`--facing -1`: left). `--view spin` shows one pose (`--pose`) at nine facings
// relative to the camera, from the game camera's pitch (or `--pitch`): in combat a unit faces its target, any way.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    static class UnitSheet
    {
        public static readonly (string name, string[] keys)[] Groups =
        {
            ("chars", new[] { "char_warrior", "char_hunter", "char_paladin", "char_mage", "char_priest", "char_rogue", "char_warlock", "char_shaman" }),
            ("comps", new[] { "comp_kael", "comp_lys", "comp_seren", "comp_rook", "comp_pip", "comp_torvan", "comp_aldric", "comp_morwen" }),
            ("npcs", new[] { "npc_elder", "npc_innkeeper", "npc_merchant", "npc_smith", "npc_villager_a", "npc_villager_b", "npc_child", "npc_guard", "npc_spirit" }),
            ("trainers", new[] { "npc_trainer", "npc_trainer_warrior", "npc_trainer_hunter", "npc_trainer_mage", "npc_trainer_priest" }),
            ("foes", new[] { "cr_bandit", "cr_bandit_archer", "cr_bandit_hexer", "cr_bandit_chief", "cr_mossling", "cr_mossling_shaman", "cr_hollow_spirit", "cr_hollow_wisp", "cr_hollow_treant" }),
            ("beasts", new[] { "cr_wolf", "cr_wolf_blighted", "cr_wolf_greymane", "cr_boar", "cr_spider", "cr_hollow_warden", "sheep" }),
            ("pets", new[] { "pet_wolf", "pet_boar", "pet_cat", "pet_bear", "pet_owl" }),
            ("demons", new[] { "demon_imp", "demon_voidwalker", "demon_succubus", "demon_infernal", "demon_felhunter" }),
            ("static", new[] { "totem_earth", "totem_fire", "totem_water", "totem_air", "cr_training_dummy" }),
        };

        enum View { Game, Quarter }
        enum PoseKind { Idle, Walk, Act }

        sealed class Column
        {
            public string Label; public View View; public PoseKind Pose; public float Yaw;
            public UnitAction Action; public float Dur, At;
        }

        static readonly Column[] SheetColumns =
        {
            new Column { Label = "game", View = View.Game, Pose = PoseKind.Idle, Yaw = UnitPoser.FacingYaw(1) },
            new Column { Label = "3/4 idle", View = View.Quarter, Pose = PoseKind.Idle, Yaw = 150f },
            new Column { Label = "walk", View = View.Quarter, Pose = PoseKind.Walk, Yaw = 115f },
            new Column { Label = "wind-up", View = View.Quarter, Pose = PoseKind.Act, Yaw = 115f, Action = UnitAction.Attack, Dur = 0.5f, At = 0.13f },
            new Column { Label = "strike", View = View.Quarter, Pose = PoseKind.Act, Yaw = 115f, Action = UnitAction.Attack, Dur = 0.5f, At = UnitView.AttackHitTime },
            new Column { Label = "shoot", View = View.Quarter, Pose = PoseKind.Act, Yaw = 115f, Action = UnitAction.Shoot, Dur = 0.45f, At = UnitView.ShootReleaseTime },
            new Column { Label = "cast", View = View.Quarter, Pose = PoseKind.Act, Yaw = 115f, Action = UnitAction.Cast, Dur = 0.7f, At = UnitView.CastReleaseTime },
        };

        static Column[] Columns = SheetColumns;

        /// <summary>A column showing a pose by name (--pose), with the sheet columns' action timings.</summary>
        static Column PoseColumn(string pose, string label, View view, float yaw)
        {
            var c = new Column { Label = label, View = view, Yaw = yaw, Pose = PoseKind.Act, Action = UnitAction.Attack, Dur = 0.5f };
            switch (pose)
            {
                case "idle": c.Pose = PoseKind.Idle; break;
                case "walk": c.Pose = PoseKind.Walk; break;
                case "wind": c.At = 0.13f; break;
                case "strike": c.At = UnitView.AttackHitTime; break;
                case "shoot": c.Action = UnitAction.Shoot; c.Dur = 0.45f; c.At = UnitView.ShootReleaseTime; break;
                case "cast": c.Action = UnitAction.Cast; c.Dur = 0.7f; c.At = UnitView.CastReleaseTime; break;
                default: throw new ArgumentException("unknown pose " + pose + " (idle walk wind strike shoot cast)");
            }
            return c;
        }

        /// <summary>--view game: idle and walk facing right and left, then the four action frames facing `facing`.</summary>
        static Column[] GameColumns(int facing)
        {
            float r = UnitPoser.FacingYaw(1), l = UnitPoser.FacingYaw(-1), a = UnitPoser.FacingYaw(facing);
            string side = facing >= 0 ? "" : " L";
            return new[]
            {
                PoseColumn("idle", "game R", View.Game, r),
                PoseColumn("idle", "game L", View.Game, l),
                PoseColumn("walk", "walk R", View.Game, r),
                PoseColumn("walk", "walk L", View.Game, l),
                PoseColumn("wind", "wind-up" + side, View.Game, a),
                PoseColumn("strike", "strike" + side, View.Game, a),
                PoseColumn("shoot", "shoot" + side, View.Game, a),
                PoseColumn("cast", "cast" + side, View.Game, a),
            };
        }

        /// <summary>--view spin: one pose at nine facings (World3D yaw: 90 = screen-right, 180 = towards the camera).</summary>
        static Column[] SpinColumns(string pose) =>
            new[] { 30f, 70f, 90f, 125f, 160f, 200f, 235f, 270f, 310f }
                .Select(y => PoseColumn(pose, pose + " " + y.ToString("0", CultureInfo.InvariantCulture), View.Game, y)).ToArray();

        // the game camera at the default zoom (CameraRig): distance and pitch; the 3/4 camera: low and closer
        static readonly float GameDist = CameraMath.DefaultSize / Mathf.Tan(CameraMath.FieldOfView * 0.5f * Mathf.Deg2Rad);
        static readonly float DefaultGamePitch = CameraMath.PitchFor(GameDist);
        static float GamePitch = DefaultGamePitch;
        const float QuarterDist = 9f, QuarterPitch = 14f;
        // pixels per metre at 1080p for a unit at the look-at point of the default view (outline width reference)
        static readonly float GamePxPerM = 1080f / (2f * CameraMath.DefaultSize);

        public static int Run(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "units.png";
            string sel = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "all";
            int tile = 240, ss = 2;
            float hour = 12.5f, speed = 3.4f;
            string view = "sheet", pose = "wind";
            int facing = 1;
            float pitch = DefaultGamePitch;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--tile": tile = Math.Clamp(int.Parse(args[++i]), 120, 600); break;
                    case "--ss": ss = Math.Clamp(int.Parse(args[++i]), 1, 4); break;
                    case "--hour": hour = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--speed": speed = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--view": view = args[++i]; break;
                    case "--pose": pose = args[++i]; break;
                    case "--facing": facing = int.Parse(args[++i], CultureInfo.InvariantCulture) >= 0 ? 1 : -1; break;
                    case "--pitch": pitch = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--gamma": Lighting.Linear = false; QualitySettings.activeColorSpace = ColorSpace.Gamma; break;
                    case "--root": i++; break;
                    default: if (i == 1) break; throw new ArgumentException("unknown option " + args[i]);
                }
            }
            switch (view)
            {
                case "sheet": Columns = SheetColumns; break;
                case "game": Columns = GameColumns(facing); break;
                case "spin": Columns = SpinColumns(pose); break;
                default: throw new ArgumentException("unknown view " + view + " (sheet game spin)");
            }
            GamePitch = pitch;
            Debug.Quiet = true;
            UnitPoser.CameraYaw = 0f;   // every tile's camera looks along yaw 0

            // noon lighting from the real DayNight; no fog at sheet distances
            var dn = new DayNight();
            dn.SetHour(hour);
            dn.ApplyTo();
            SceneLighting.FogStart = 1000f;
            SceneLighting.FogEnd = 2000f;
            SceneLighting.FogMax = 0f;

            var pages = new List<(string file, string title, string[] keys)>();
            string stem = outPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? outPath.Substring(0, outPath.Length - 4) : outPath;
            if (sel == "all")
                foreach (var g in Groups) pages.Add(($"{stem}_{g.name}.png", g.name, g.keys));
            else
            {
                var g = Groups.FirstOrDefault(x => x.name == sel);
                pages.Add((outPath, g.keys != null ? g.name : "units", g.keys ?? sel.Split(',', StringSplitOptions.RemoveEmptyEntries)));
            }

            foreach (var page in pages)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int header = 22;
                // pages of static models (totems, dummy) only have the two idle columns
                bool allStatic = page.keys.All(k => { var m = ModelOf(k); return m != null && (m.Static || m.Rig == UnitRigKind.Static); });
                int ncol = allStatic && view == "sheet" ? 2 : Columns.Length;
                var img = new SheetImage(Math.Max(tile * ncol, 640), header + tile * page.keys.Length);
                img.Fill(0, 0, Math.Max(tile * ncol, 640), header + tile * page.keys.Length, new Vector3(0.93f, 0.92f, 0.88f));
                string viewNote = view == "sheet" ? "" : $"  view {view}{(view == "spin" ? " " + pose : "")} pitch {GamePitch:0}";
                img.Text(6, 6, $"{page.title}  hour {hour:0.0}  walk {speed:0.0} m/s  checker 1 m  slate figure 1.75 m{viewNote}");
                for (int r = 0; r < page.keys.Length; r++)
                {
                    string key = page.keys[r];
                    try { RenderRow(img, 0, header + r * tile, tile, ss, key, speed, ncol); }
                    catch (Exception e) { img.Text(6, header + r * tile + 6, key + " failed"); Console.WriteLine($"{key}: {e}"); }
                }
                img.Save(page.file);
                Console.WriteLine($"{page.title}: {page.keys.Length} units → {page.file} in {sw.ElapsedMilliseconds} ms");
            }
            return 0;
        }

        sealed class Tile
        {
            public Column Col; public Transform Root; public UnitPoser Unit; public Transform Reference;
            public Vector3 Target; public float Extent;   // look-at point and half view height (m) needed
            public bool Skip;
        }

        /// <summary>
        /// A sheet key: a model key, `key#N` (look variation N) or `key@npcId` (the variation that NPC gets in the game,
        /// UnitModels.StableVariant), e.g. npc_child@child_nell.
        /// </summary>
        static string ParseKey(string k, out int variant)
        {
            variant = 0;
            int at = k.IndexOf('@');
            if (at < 0) return k;
            variant = UnitModels.StableVariant(k.Substring(at + 1));
            return k.Substring(0, at);
        }

        static UnitModel ModelOf(string k) => UnitModels.Get(ParseKey(k, out int v), v);

        static void RenderRow(SheetImage img, int ox, int oy, int tile, int ss, string label, float speed, int ncol)
        {
            string key = ParseKey(label, out int variant);
            var model = UnitModels.Get(key, variant);
            var tiles = new List<Tile>();
            foreach (var col in Columns.Take(ncol))
            {
                var t = new Tile { Col = col, Root = new GameObject("Tile " + col.Label).transform };
                // the game column is placed like SetFacing(+1) (static models face the camera); the 3/4 columns are fixed views
                t.Unit = new UnitPoser(key, 0f, t.Root, Vector2.zero, col.Yaw, variant, col.View != View.Game);
                var u = t.Unit;
                bool still = model.Static || model.Rig == UnitRigKind.Static;
                switch (col.Pose)
                {
                    case PoseKind.Idle: u.Idle(1.4f); break;
                    case PoseKind.Walk:
                        if (still) t.Skip = true; else u.Walk(speed);
                        break;
                    case PoseKind.Act:
                        if (still) t.Skip = true; else u.Act(col.Action, col.Dur, col.At);
                        break;
                }
                if (t.Skip) { tiles.Add(t); continue; }

                // the reference figure beside the unit (to the left on screen: units face right, actions lunge right)
                float yawCam = 0f;
                var f = CameraMath.ViewForward(yawCam, col.View == View.Game ? GamePitch : QuarterPitch);
                var right = new Vector2(f.y, -f.x).normalized;   // screen right on the ground
                // framing: the unit (skinned) and the figure, which stands 0.45 m clear of the unit's leftmost point
                var pts = new List<Vector3>(Collector.Skin(u.Body.Renderer, model.Mesh).V);
                float left = 0f;
                foreach (var p in pts) left = Mathf.Min(left, Vector2.Dot(new Vector2(p.x, p.y) - u.Pos, right));
                var refPos = u.Pos + right * (left - 0.45f - 0.2f);
                t.Reference = Reference(t.Root, refPos);
                foreach (float sx in new[] { -0.25f, 0.25f }) { pts.Add(World3D.At(refPos + right * sx, 0f)); pts.Add(World3D.At(refPos + right * sx, 1.8f)); }
                var b = new Bounds(pts[0], Vector3.zero);
                foreach (var p in pts) b.Encapsulate(p);
                t.Target = b.center;
                float dist = col.View == View.Game ? GameDist : QuarterDist;
                var cam = ViewCam.Create(t.Target - f * dist, Quaternion.LookRotation(f, World3D.Up), 30f, 16, 16);
                float need = 0f;
                foreach (var p in pts)
                {
                    var d = p - cam.Pos;
                    float z = Vector3.Dot(d, cam.F);
                    if (z < 0.1f) continue;
                    need = Mathf.Max(need, Mathf.Abs(Vector3.Dot(d, cam.R)) / z, Mathf.Abs(Vector3.Dot(d, cam.U)) / z);
                }
                t.Extent = need * dist * 1.1f;
                tiles.Add(t);
            }

            float extent = tiles.Where(t => !t.Skip).Select(t => t.Extent).DefaultIfEmpty(1.2f).Max();
            for (int c = 0; c < tiles.Count; c++)
            {
                var t = tiles[c];
                int x0 = ox + c * tile;
                if (t.Skip)
                {
                    img.Fill(x0, oy, tile, tile, new Vector3(0.86f, 0.86f, 0.84f));
                    img.Text(x0 + 6, oy + 6, t.Col.Label + " -");
                    continue;
                }
                var col = t.Col;
                float dist = col.View == View.Game ? GameDist : QuarterDist;
                var f = CameraMath.ViewForward(0f, col.View == View.Game ? GamePitch : QuarterPitch);
                float fov = 2f * Mathf.Atan(extent / dist) * Mathf.Rad2Deg;
                var cam = ViewCam.Create(t.Target - f * dist, Quaternion.LookRotation(f, World3D.Up), fov, tile * ss, tile * ss);
                var light = Lighting.FromScene(cam.Pos, new List<(Vector3, Color, float, float)>(), t.Target);
                var frame = new Frame(cam, light);
                FillBackground(frame);
                var ground = Ground(t.Root, t.Unit.Pos);
                var draws = Collector.Collect(new[] { t.Root }, frame, cam.Pos);
                float pxPerM = tile / (2f * extent);
                int ink = Math.Max(1, (int)Math.Round(1.9f * pxPerM / GamePxPerM * ss));
                Pipeline.Render(frame, draws, ink);
                var px = frame.Output(ss, out int w, out int h);
                img.Blit(x0, oy, w, h, px);
                img.Text(x0 + 4, oy + 4, col.Label);
                if (c == 0)
                {
                    img.Text(x0 + 4, oy + tile - 26, label);
                    img.Text(x0 + 4, oy + tile - 14, $"{model.Height:0.00}m {model.Rig.ToString().ToLowerInvariant()} {model.Mesh.T.Count / 3}t");
                }
            }
        }

        static void FillBackground(Frame f)
        {
            var top = Lighting.Lin(new Color(0.80f, 0.86f, 0.93f));
            var bottom = Lighting.Lin(new Color(0.90f, 0.91f, 0.90f));
            for (int y = 0; y < f.H; y++)
            {
                var c = V3.Lerp(top, bottom, (float)y / Math.Max(1, f.H - 1));
                for (int x = 0; x < f.W; x++) { int i = (y * f.W + x) * 3; f.C[i] = c.x; f.C[i + 1] = c.y; f.C[i + 2] = c.z; }
            }
        }

        static Mesh groundMesh, refMesh;

        /// <summary>A 1 m checker (40 m square) around the unit, aligned to the world grid.</summary>
        static Transform Ground(Transform parent, Vector2 around)
        {
            if (groundMesh == null)
            {
                var m = new Mesh { name = "sheet_ground" };
                var a = new Color(0.66f, 0.72f, 0.55f, 0f);
                var b = new Color(0.61f, 0.67f, 0.51f, 0f);
                const int N = 20;
                for (int y = -N; y < N; y++)
                    for (int x = -N; x < N; x++)
                    {
                        int i = m.V.Count;
                        var c = ((x + y) & 1) == 0 ? a : b;
                        m.V.Add(new Vector3(x, y, 0f)); m.V.Add(new Vector3(x + 1, y, 0f)); m.V.Add(new Vector3(x + 1, y + 1, 0f)); m.V.Add(new Vector3(x, y + 1, 0f));
                        for (int k = 0; k < 4; k++) { m.N.Add(new Vector3(0f, 0f, -1f)); m.C.Add(c); m.UV0.Add(Vector2.zero); m.UV1.Add(Vector2.zero); }
                        m.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                    }
                m.RecalculateBounds();
                groundMesh = m;
            }
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(Mathf.Floor(around.x), Mathf.Floor(around.y), 0f);
            go.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials3D.WithOutline(true)[0];
            return go.transform;
        }

        /// <summary>The 1.75 m reference figure (slate capsule + head), outlined like a unit.</summary>
        static Transform Reference(Transform parent, Vector2 at)
        {
            if (refMesh == null)
            {
                var mb = new MeshBuilder(7) { Color = new Color(0.47f, 0.54f, 0.66f) };
                mb.Capsule(Vector3.zero, 0.19f, 1.47f, 10, false);
                mb.Sphere(new Vector3(0f, 1.63f, 0f), 0.12f, 10, 6, false);
                refMesh = mb.ToMesh("sheet_reference");
            }
            var holder = new GameObject("Reference").transform;
            holder.SetParent(parent, false);
            holder.localPosition = new Vector3(at.x, at.y, 0f);
            var go = new GameObject("Body");
            go.transform.SetParent(holder, false);
            go.transform.localRotation = World3D.Yaw(180f);
            go.AddComponent<MeshFilter>().sharedMesh = refMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = Materials3D.WithOutline();
            MeshCache.AddShadow(holder, 0.3f, 0.3f, 0.3f);
            return holder;
        }
    }
}
