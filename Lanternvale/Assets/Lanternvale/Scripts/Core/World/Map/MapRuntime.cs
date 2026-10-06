// Engine-independent runtime bookkeeping for one map: encounters, npc visibility, chests, transitions, regions.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    [Serializable]
    public sealed class MapRuntimeState
    {
        public string mapId = "";
        public bool visited;
        public List<string> openedChests = new List<string>();
        public List<string> unlockedChests = new List<string>();
        public List<string> enteredRegions = new List<string>();
        /// <summary>Encounters whose combat started but has not been won (hidden ambushes stay revealed).</summary>
        public List<string> triggeredEncounters = new List<string>();
        /// <summary>Regions whose passive check (RegionDef.check) has been rolled (once per save).</summary>
        public List<string> checkedRegions = new List<string>();
    }

    public sealed class MapRuntime
    {
        /// <summary>Stealthed party members are only noticed within this distance (metres) of an encounter.</summary>
        public const float StealthDetectRadius = 3f;

        public readonly MapDef Def;
        public FlagStore Flags { get; set; }
        public QuestLog Quests { get; set; }
        public MapRuntimeState State { get; private set; }

        public string Id => Def?.id ?? "";

        readonly HashSet<string> inside = new HashSet<string>(StringComparer.Ordinal);
        readonly List<RegionDef> firstEntries = new List<RegionDef>();

        public MapRuntime(MapDef def, FlagStore flags, QuestLog quests = null, MapRuntimeState state = null)
        {
            Def = def ?? new MapDef();
            Flags = flags ?? new FlagStore();
            Quests = quests;
            SetState(state);
        }

        /// <summary>Replaces the persistent state (used by WorldState.Load).</summary>
        public void SetState(MapRuntimeState state)
        {
            State = state ?? new MapRuntimeState();
            State.mapId = Def.id;
            State.openedChests ??= new List<string>();
            State.unlockedChests ??= new List<string>();
            State.enteredRegions ??= new List<string>();
            State.triggeredEncounters ??= new List<string>();
            State.checkedRegions ??= new List<string>();
            inside.Clear();
        }

        /// <summary>Call when the party arrives on this map: marks it visited and notifies Reach objectives (map id).</summary>
        public void OnEnterMap()
        {
            State.visited = true;
            inside.Clear();
            Quests?.OnReach(Def.id);
        }

        /// <summary>Spawn position by id, falling back to "default", then the map centre.</summary>
        public Vec2 SpawnPosition(string spawnId)
        {
            SpawnPointDef fallback = null;
            if (Def.spawns != null)
            {
                foreach (var s in Def.spawns)
                {
                    if (s == null) continue;
                    if (s.id == spawnId) return s.pos;
                    if (s.id == "default") fallback = s;
                }
            }
            return fallback != null ? fallback.pos : new Vec2(Def.width * 0.5f, Def.depth * 0.5f);
        }

        // ------------------------------------------------------------------ encounters

        public static string DoneFlag(EncounterDef e) =>
            e == null ? "" : (!string.IsNullOrEmpty(e.doneFlag) ? e.doneFlag : WorldRules.EncounterDoneFlag(e.id));

        public EncounterDef FindEncounter(string id)
        {
            if (Def.encounters == null || id == null) return null;
            foreach (var e in Def.encounters) if (e != null && e.id == id) return e;
            return null;
        }

        public bool IsEncounterDone(EncounterDef e) => e != null && Flags.IsSet(DoneFlag(e));

        public bool IsEncounterDone(string id)
        {
            var e = FindEncounter(id);
            return e != null ? IsEncounterDone(e) : Flags.IsSet(WorldRules.EncounterDoneFlag(id));
        }

        /// <summary>Not defeated and requireFlag satisfied: its enemies exist on the map.</summary>
        public bool IsEncounterAvailable(EncounterDef e) => e != null && !IsEncounterDone(e) && Flags.Test(e.requireFlag);

        public bool IsEncounterTriggered(string id) => State.triggeredEncounters.Contains(id);

        /// <summary>Available and either not hidden or already triggered: draw its enemies.</summary>
        public bool IsEncounterVisible(EncounterDef e) => IsEncounterAvailable(e) && (!e.hidden || IsEncounterTriggered(e.id));

        public IEnumerable<EncounterDef> AvailableEncounters()
        {
            if (Def.encounters == null) yield break;
            foreach (var e in Def.encounters)
                if (IsEncounterAvailable(e)) yield return e;
        }

        /// <summary>
        /// First available encounter with a party member inside its radius. Stealthed members (stealthed[i] true)
        /// only trigger within min(radius, StealthDetectRadius). Returns null when nothing triggers.
        /// </summary>
        public EncounterDef FindTriggeredEncounter(IReadOnlyList<Vec2> partyPositions, IReadOnlyList<bool> stealthed = null)
        {
            if (Def.encounters == null || partyPositions == null) return null;
            foreach (var e in Def.encounters)
            {
                if (!IsEncounterAvailable(e)) continue;
                for (int i = 0; i < partyPositions.Count; i++)
                {
                    bool st = stealthed != null && i < stealthed.Count && stealthed[i];
                    float r = st ? Math.Min(e.radius, StealthDetectRadius) : e.radius;
                    if ((partyPositions[i] - e.pos).SqrLength <= r * r) return e;
                }
            }
            return null;
        }

        public EncounterDef FindTriggeredEncounter(Vec2 position, bool stealthed = false)
        {
            if (Def.encounters == null) return null;
            foreach (var e in Def.encounters)
            {
                if (!IsEncounterAvailable(e)) continue;
                float r = stealthed ? Math.Min(e.radius, StealthDetectRadius) : e.radius;
                if ((position - e.pos).SqrLength <= r * r) return e;
            }
            return null;
        }

        /// <summary>Combat with this encounter started (reveals hidden ambushes).</summary>
        public void MarkEncounterTriggered(string id)
        {
            if (!string.IsNullOrEmpty(id) && !State.triggeredEncounters.Contains(id)) State.triggeredEncounters.Add(id);
        }

        /// <summary>Encounter won: sets its done flag and notifies Defeat objectives.</summary>
        public void MarkEncounterDone(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var e = FindEncounter(id);
            Flags.Set(e != null ? DoneFlag(e) : WorldRules.EncounterDoneFlag(id), 1);
            State.triggeredEncounters.Remove(id);
            Quests?.OnDefeat(id);
        }

        /// <summary>Clears the done flag (respawn / scripted reset).</summary>
        public void ResetEncounter(string id)
        {
            var e = FindEncounter(id);
            Flags.Clear(e != null ? DoneFlag(e) : WorldRules.EncounterDoneFlag(id));
            State.triggeredEncounters.Remove(id);
        }

        // ------------------------------------------------------------------ npcs

        /// <summary>requireFlag satisfied and hideFlag (if any) not satisfied. Flag expressions support "!flag".</summary>
        public bool IsNpcVisible(MapNpcDef n) =>
            n != null && Flags.Test(n.requireFlag) && !(!string.IsNullOrEmpty(n.hideFlag) && Flags.Test(n.hideFlag));

        public IEnumerable<MapNpcDef> VisibleNpcs()
        {
            if (Def.npcs == null) yield break;
            foreach (var n in Def.npcs)
                if (IsNpcVisible(n)) yield return n;
        }

        // ------------------------------------------------------------------ chests

        public ChestDef FindChest(string id)
        {
            if (Def.chests == null || id == null) return null;
            foreach (var c in Def.chests) if (c != null && c.id == id) return c;
            return null;
        }

        /// <summary>Chest exists in the world (requireFlag satisfied).</summary>
        public bool IsChestAvailable(ChestDef c) => c != null && Flags.Test(c.requireFlag);

        public bool IsChestOpened(string id) => State.openedChests.Contains(id);

        public void MarkChestOpened(string id)
        {
            if (!string.IsNullOrEmpty(id) && !State.openedChests.Contains(id)) State.openedChests.Add(id);
        }

        /// <summary>Has a lock check that has not been passed yet.</summary>
        public bool IsChestLocked(ChestDef c) => c?.lockCheck != null && !State.unlockedChests.Contains(c.id) && !IsChestOpened(c.id);

        public void MarkChestUnlocked(string id)
        {
            if (!string.IsNullOrEmpty(id) && !State.unlockedChests.Contains(id)) State.unlockedChests.Add(id);
        }

        /// <summary>
        /// Rolls the chest's lock check (usually SleightOfHand) for the best party member. Success unlocks it.
        /// Returns null when the chest is not locked. Retries are allowed; gate them in the UI if desired.
        /// </summary>
        public CheckResult TryUnlockChest(ChestDef c, IDialogueContext ctx, Rng rng)
        {
            if (!IsChestLocked(c)) return null;
            var res = SkillChecks.Roll(ctx, c.lockCheck.skill, c.lockCheck.dc, rng);
            if (res.Success) MarkChestUnlocked(c.id);
            return res;
        }

        // ------------------------------------------------------------------ transitions & regions

        /// <summary>Axis-aligned rectangle test; pos is the centre, size the full extents.</summary>
        public static bool RectContains(Vec2 center, Vec2 size, Vec2 p) =>
            Math.Abs(p.x - center.x) <= size.x * 0.5f && Math.Abs(p.y - center.y) <= size.y * 0.5f;

        public bool IsTransitionUnlocked(TransitionDef t) => t != null && Flags.Test(t.requireFlag);

        /// <summary>A transition is visible (and usable) unless it is hidden and its revealFlag does not hold yet.</summary>
        public bool IsTransitionVisible(TransitionDef t) =>
            t != null && (!t.hidden || (!string.IsNullOrEmpty(t.revealFlag) && Flags.Test(t.revealFlag)));

        /// <summary>A prop is visible while its requireFlag holds and its hideFlag (if any) does not.</summary>
        public bool IsPropVisible(PropDef p) =>
            p != null && Flags.Test(p.requireFlag) && !(!string.IsNullOrEmpty(p.hideFlag) && Flags.Test(p.hideFlag));

        /// <summary>Transition rectangle size (axes ≤ 0 default to 2 m, as in the presentation layer).</summary>
        public static Vec2 TransitionSize(TransitionDef t) =>
            t == null ? default : new Vec2(t.size.x > 0f ? t.size.x : 2f, t.size.y > 0f ? t.size.y : 2f);

        /// <summary>First visible transition whose rectangle contains p (locked or not), or null. Hidden transitions
        /// are skipped until their revealFlag holds (<see cref="IsTransitionVisible"/>).</summary>
        public TransitionDef TransitionAt(Vec2 p)
        {
            if (Def.transitions == null) return null;
            foreach (var t in Def.transitions)
                if (t != null && RectContains(t.pos, TransitionSize(t), p) && IsTransitionVisible(t)) return t;
            return null;
        }

        public IEnumerable<RegionDef> RegionsAt(Vec2 p)
        {
            if (Def.regions == null) yield break;
            foreach (var r in Def.regions)
                if (r != null && RectContains(r.pos, r.size, p)) yield return r;
        }

        public bool HasEnteredRegion(string id) => State.enteredRegions.Contains(id);

        /// <summary>
        /// Call whenever the party leader moves. Notifies Reach objectives for the map and every region the leader is
        /// in, sets region enterFlags, and returns the regions entered for the FIRST time (show their `text` as a
        /// toast). The returned list is reused: it is only valid until the next call.
        /// </summary>
        public IReadOnlyList<RegionDef> UpdatePartyPosition(Vec2 leaderPos)
        {
            firstEntries.Clear();
            Quests?.OnReach(Def.id);
            if (Def.regions == null) return firstEntries;
            foreach (var r in Def.regions)
            {
                if (r == null || string.IsNullOrEmpty(r.id)) continue;
                if (!RectContains(r.pos, r.size, leaderPos))
                {
                    inside.Remove(r.id);
                    continue;
                }
                if (inside.Add(r.id) && !State.enteredRegions.Contains(r.id))
                {
                    State.enteredRegions.Add(r.id);
                    firstEntries.Add(r);
                }
                if (!string.IsNullOrEmpty(r.enterFlag) && !Flags.IsSet(r.enterFlag)) Flags.Set(r.enterFlag, 1);
                Quests?.OnReach(r.id);
            }
            return firstEntries;
        }
    }
}
