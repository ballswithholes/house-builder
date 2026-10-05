// The frame order of the game's forward renderer, shared by the map and unit modes: sky (queue 1000), opaque and
// terrain (2000) into the G-buffer, lighting, the ink hulls, then the transparent queues (decals 2950, merged
// shadows 2955, additive 3000) back to front within a queue.
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    public static class Pipeline
    {
        public static void Render(Frame frame, List<DrawCall> draws, int inkRadius)
        {
            foreach (var d in draws.Where(d => d.Kind == DrawKind.Sky)) frame.Draw(d);
            foreach (var d in draws.Where(d => d.Kind == DrawKind.Opaque || d.Kind == DrawKind.Terrain)) frame.Draw(d);
            frame.Resolve();
            if (inkRadius > 0) frame.Ink(inkRadius, Materials3D.Ink);
            var transparent = draws.Where(d => d.Kind == DrawKind.LitTransparent || d.Kind == DrawKind.Shadow || d.Kind == DrawKind.Additive)
                                   .OrderBy(d => d.Order).ThenBy(d => d.Queue).ThenByDescending(d => d.SortDist).ToList();
            foreach (var d in transparent) frame.Draw(d);
        }
    }
}
