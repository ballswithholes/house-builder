// Lanternvale offline 3D preview (no Unity needed): see README.md.
//
//   props <out.png> [keys|all] [seed] [--views gqtfb] [--tile px] [--lit 0|1] [--open] [--stats]
//   units <out.png> [keys|group|all] [--tile px] [--ss N] [--hour H] [--speed m/s]
//   map <mapId> <out.png> [--hour H] [--yaw D] [--zoom Z] [--at x,y] [--size WxH] [--ss N] [--spawn id]
//                          [--player key] [--flags f1,f2|*] [--no-units] [--no-halos] [--no-ink] [--gamma]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Game;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Lanternvale.Preview
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0) return Usage();
            try
            {
                var resources = FindResources(args);
                ArtLibrary.ResourcesDir = resources;
                switch (args[0])
                {
                    case "props": return PropSheet.Run(args.Skip(1).Where(a => !a.StartsWith("--root")).ToArray());
                    case "map": return Map(args.Skip(1).ToArray(), resources);
                    case "units": return UnitSheet.Run(args.Skip(1).ToArray());
                    default: return Usage();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }

        static int Usage()
        {
            Console.WriteLine(@"Lanternvale offline 3D preview
  props <out.png> [key[,key@seed...]|all] [seed] [--views gqtfb] [--tile 380] [--lit 0|1] [--open] [--stats]
      contact sheet of prop models: g = game camera, q = 3/4, t = top (collider ellipse), f = front, b = back
  units <out.png> [key,key...|group|all] [--tile 240] [--ss 2] [--hour 12.5] [--speed 3.4]
      unit models posed by the game's animator: game camera, 3/4 idle, walk, attack wind-up/strike, shoot, cast;
      groups: chars comps npcs foes beasts pets demons static (all = one page per group, <out>_<group>.png)
  map <mapId> <out.png> [--hour H] [--yaw D] [--zoom Z] [--at x,y] [--size 1600x900] [--ss 2] [--spawn id]
                        [--player char_warrior] [--flags f1,f2|*] [--no-units] [--no-halos] [--no-ink] [--gamma]
      the player's view of a map: look-at = the spawn (or --at), Zoom = CameraRig zoom (2.6 … 10.4, default 6.2),
      yaw ±45, hour 0..24 (default: the map's own time / the world clock 12.5)");
            return 2;
        }

        /// <summary>Lanternvale/Assets/Lanternvale/Resources, found upwards from the tool (or --root &lt;Lanternvale dir&gt;).</summary>
        static string FindResources(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--root") return Path.Combine(Path.GetFullPath(args[i + 1]), "Assets", "Lanternvale", "Resources");
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    var c = Path.Combine(d.FullName, "Assets", "Lanternvale", "Resources");
                    if (Directory.Exists(c)) return c;
                    c = Path.Combine(d.FullName, "Lanternvale", "Assets", "Lanternvale", "Resources");
                    if (Directory.Exists(c)) return c;
                    d = d.Parent;
                }
            }
            throw new DirectoryNotFoundException("Lanternvale/Assets/Lanternvale/Resources not found (run inside the repo or pass --root <Lanternvale dir>)");
        }

        static IEnumerable<KeyValuePair<string, string>> ReadDataFiles(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                yield return new KeyValuePair<string, string>(Path.GetRelativePath(dir, f), File.ReadAllText(f));
        }

        static int Map(string[] args, string resources)
        {
            if (args.Length < 2) return Usage();
            string mapId = args[0], outPath = args[1];
            var opt = new MapScene.Options();
            int w = 1600, h = 900, ss = 2;
            bool ink = true;
            for (int i = 2; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value after " + a);
                switch (a)
                {
                    case "--hour": opt.Hour = float.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--yaw": opt.Yaw = Mathf.Clamp(float.Parse(Next(), CultureInfo.InvariantCulture), -45f, 45f); break;
                    case "--zoom": opt.Zoom = float.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--at":
                    {
                        var p = Next().Split(',');
                        opt.At = new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture));
                        break;
                    }
                    case "--size":
                    {
                        var p = Next().ToLowerInvariant().Split('x');
                        w = int.Parse(p[0]); h = int.Parse(p[1]);
                        break;
                    }
                    case "--ss": ss = Math.Clamp(int.Parse(Next()), 1, 4); break;
                    case "--spawn": opt.Spawn = Next(); break;
                    case "--player": opt.Player = Next(); break;
                    case "--flags": foreach (var f in Next().Split(',', StringSplitOptions.RemoveEmptyEntries)) opt.Flags.Add(f.Trim()); break;
                    case "--no-units": opt.Units = false; break;
                    case "--no-halos": opt.Halos = false; break;
                    case "--no-ink": ink = false; break;
                    case "--gamma": Lighting.Linear = false; QualitySettings.activeColorSpace = ColorSpace.Gamma; break;
                    case "--root": i++; break;
                    default: throw new ArgumentException("unknown option " + a);
                }
            }

            var sw = Stopwatch.StartNew();
            var db = GameDatabase.Load(ReadDataFiles(Path.Combine(resources, "Data")));
            if (!db.Maps.TryGetValue(mapId, out var def))
            {
                Console.Error.WriteLine($"unknown map '{mapId}' (maps: {string.Join(", ", db.Maps.Keys)})");
                return 2;
            }
            Debug.Quiet = true;
            var scene = new MapScene(db, def, opt);
            scene.Build(w * ss, h * ss);
            long tBuild = sw.ElapsedMilliseconds;

            var frame = new Frame(scene.View, scene.Lighting);
            var stats = new Collector.Stats();
            var draws = Collector.Collect(new[] { scene.Root }, frame, scene.View.Pos, stats);
            Pipeline.Render(frame, draws, ink ? Math.Max(1, (int)Math.Round(2.2f * h / 1080f * ss)) : 0);
            var px = frame.Output(ss, out int ow, out int oh);
            Png.SaveRgb(outPath, ow, oh, px);

            var dn = scene.DayNight;
            Console.WriteLine($"{def.id} → {outPath} ({ow}x{oh}, ss {ss}) in {sw.ElapsedMilliseconds} ms (build {tBuild} ms)");
            Console.WriteLine($"  hour {dn.Hour:0.0} ({dn.Phase}, night {dn.NightFactor:0.00}), zoom {opt.Zoom:0.0} → distance {scene.Distance:0.0} m, pitch {scene.Pitch:0.0}°, yaw {opt.Yaw:0}, look-at {(Vector2)scene.LookAt}");
            Console.WriteLine($"  {scene.PropCount} prop models, {stats.Renderers} renderers ({stats.Outlined} outlined), {draws.Count} draws, {frame.Triangles} triangles drawn, {scene.LightCount} point lights in view");
            return 0;
        }
    }
}
