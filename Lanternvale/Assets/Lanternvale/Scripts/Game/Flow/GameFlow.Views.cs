// GameFlow views: the UnitView registry for rules units (party, pets, summons, battle units) plus the
// views the flow owns without a rules Unit — map NPCs (and not-yet-recruited companions) and the enemies
// of visible encounters. Sync keeps them in step with the session (recruit/dismiss hand-offs, pets,
// encounters resolved peacefully), and a tiny idle behaviour makes the village feel alive.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        // ------------------------------------------------------------ registries

        readonly Dictionary<Unit, UnitView> views = new Dictionary<Unit, UnitView>();
        readonly Dictionary<UnitView, Unit> viewUnits = new Dictionary<UnitView, Unit>();

        sealed class NpcEntry
        {
            public MapNpcDef Def;
            public string Id = "";
            public NpcDef Npc;
            public CompanionDef Companion;
            public UnitView View;
            public Vector2 Home;
            public int HomeFacing = 1;
            public bool Wanders;
            public float NextIdle;
            public float BarkReady;
        }

        sealed class EnemyEntry
        {
            public EncounterDef Encounter;
            public EncounterEnemyDef Def;
            public CreatureDef Creature;
            /// <summary>Session.PreviewEncounter entry (name, level as the battle will scale it, rank) for nameplates.</summary>
            public EncounterEnemyPreview Preview;
            public int Index;                 // index among the encounter's (valid) enemies, as PreviewEncounter lists them
            public UnitView View;
            public Vector2 Home;
            public bool Wanders;
            public float NextIdle;
        }

        sealed class DelayedRemoval
        {
            public UnitView View;
            public float Left;
        }

        readonly List<NpcEntry> npcEntries = new List<NpcEntry>();
        readonly Dictionary<UnitView, NpcEntry> npcByView = new Dictionary<UnitView, NpcEntry>();
        readonly List<EnemyEntry> enemyEntries = new List<EnemyEntry>();
        readonly Dictionary<UnitView, EnemyEntry> enemyByView = new Dictionary<UnitView, EnemyEntry>();
        readonly List<DelayedRemoval> delayedRemovals = new List<DelayedRemoval>();

        readonly Lanternvale.Util.Rng presentationRng = new Lanternvale.Util.Rng(0x1A57E2B5UL);
        readonly NavPath wanderPath = new NavPath();
        readonly List<Unit> tmpUnits = new List<Unit>();
        readonly HashSet<Unit> tmpUnitSet = new HashSet<Unit>();
        float idleClock;

        static readonly Color NpcRing = new Color(1f, 0.86f, 0.52f);
        static readonly Color EnemyRing = new Color(1f, 0.42f, 0.36f);
        static readonly Color NeutralRing = new Color(0.95f, 0.85f, 0.4f);

        // ------------------------------------------------------------ unit views

        UnitView CreateUnitView(Unit u)
        {
            var key = SpriteKeyOf(u);
            float height = u.Class == null && u.Creature != null && u.Creature.size > 0f ? u.Creature.size : 0f;
            var v = UnitView.Create(key, height, RingColorOf(u));
            v.DisplayName = NameOf(u);
            v.Tag = u;
            v.UnitId = u.Id;
            v.Teleport(ToUnity(u.Position));
            if (Mathf.Abs(u.Facing.x) > 0.05f) v.SetFacing(u.Facing.x < 0f ? -1 : 1);
            if (u.Downed) v.PlayDowned();
            if (u.IsStealthed) v.SetStealthed(true);
            BindView(u, v);
            if (u == Selected) v.SetSelected(true);
            return v;
        }

        void BindView(Unit u, UnitView v)
        {
            if (u == null || v == null) return;
            if (views.TryGetValue(u, out var old) && old != v && old != null)
            {
                viewUnits.Remove(old);
                DisposeView(old);
            }
            views[u] = v;
            viewUnits[v] = u;
            v.Tag = u;
            v.UnitId = u.Id;
            if (u == Selected) v.SetSelected(true);
        }

        /// <summary>Removes the unit ↔ view binding (the view object survives).</summary>
        void DetachUnitView(Unit u)
        {
            if (u == null || !views.TryGetValue(u, out var v)) return;
            views.Remove(u);
            if (v != null)
            {
                viewUnits.Remove(v);
                if (u == Selected) v.SetSelected(false);
            }
            else
            {
                // destroyed elsewhere: purge stale reverse entries
                tmpViews.Clear();
                foreach (var kv in viewUnits) if (kv.Value == u) tmpViews.Add(kv.Key);
                foreach (var k in tmpViews) viewUnits.Remove(k);
            }
            if (HoveredUnit == u) ClearHoverState();
        }

        readonly List<UnitView> tmpViews = new List<UnitView>();

        /// <summary>Destroys a view object and forgets every reference the flow holds to it.</summary>
        void DisposeView(UnitView v)
        {
            if (ReferenceEquals(v, null)) return;
            if (ReferenceEquals(v, hoveredView)) ClearHoverState();
            if (npcByView.TryGetValue(v, out var ne)) { npcByView.Remove(v); npcEntries.Remove(ne); }
            if (enemyByView.TryGetValue(v, out var ee)) { enemyByView.Remove(v); enemyEntries.Remove(ee); }
            if (viewUnits.TryGetValue(v, out var u)) { viewUnits.Remove(v); if (views.TryGetValue(u, out var cur) && ReferenceEquals(cur, v)) views.Remove(u); }
            for (int i = delayedRemovals.Count - 1; i >= 0; i--) if (ReferenceEquals(delayedRemovals[i].View, v)) delayedRemovals.RemoveAt(i);
            ForgetBarksOf(v);
            try { v.Dispose(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void DisposeAllViews()
        {
            tmpViews.Clear();
            foreach (var v in views.Values) tmpViews.Add(v);
            foreach (var e in npcEntries) tmpViews.Add(e.View);
            foreach (var e in enemyEntries) tmpViews.Add(e.View);
            foreach (var d in delayedRemovals) tmpViews.Add(d.View);
            views.Clear();
            viewUnits.Clear();
            npcEntries.Clear();
            npcByView.Clear();
            enemyEntries.Clear();
            enemyByView.Clear();
            delayedRemovals.Clear();
            hoveredView = null;
            foreach (var v in tmpViews)
            {
                if (ReferenceEquals(v, null)) continue;
                try { v.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            }
            tmpViews.Clear();
        }

        /// <summary>Lets a dying view finish its animation, then destroys it.</summary>
        void ScheduleRemoval(UnitView v, float seconds)
        {
            if (ReferenceEquals(v, null)) return;
            foreach (var d in delayedRemovals) if (ReferenceEquals(d.View, v)) { d.Left = Mathf.Min(d.Left, seconds); return; }
            delayedRemovals.Add(new DelayedRemoval { View = v, Left = seconds });
        }

        void UpdateDelayedRemovals(float dt)
        {
            for (int i = delayedRemovals.Count - 1; i >= 0; i--)
            {
                var d = delayedRemovals[i];
                d.Left -= dt;
                if (d.Left > 0f) continue;
                delayedRemovals.RemoveAt(i);
                DisposeView(d.View);
            }
        }

        string SpriteKeyOf(Unit u)
        {
            if (!string.IsNullOrEmpty(u.Sprite)) return u.Sprite;
            if (u.Companion != null && !string.IsNullOrEmpty(u.Companion.sprite)) return u.Companion.sprite;
            if (u.Class != null && !string.IsNullOrEmpty(u.Class.sprite)) return u.Class.sprite;
            if (u.Creature != null && !string.IsNullOrEmpty(u.Creature.sprite)) return u.Creature.sprite;
            if (u.Class != null) return "char_" + u.Class.id.ToString().ToLowerInvariant();
            return "cr_wolf";
        }

        Color RingColorOf(Unit u)
        {
            if (u == null) return Color.white;
            if (u.Team == Team.Enemy) return EnemyRing;
            if (u.Team == Team.Neutral) return NeutralRing;
            var owner = u.Owner ?? u;
            if (owner.Class != null) return Ui.ClassColor(owner.Class.id);
            return Ui.Good;
        }

        // ------------------------------------------------------------ NPC views

        NpcEntry CreateNpcView(MapNpcDef n, Vector2? from)
        {
            var db = Db;
            string id = n.npc ?? "";
            NpcDef npc = null;
            CompanionDef comp = null;
            if (db != null)
            {
                db.Npcs.TryGetValue(id, out npc);
                if (npc == null) db.Companions.TryGetValue(id, out comp);
            }
            string sprite = "";
            if (npc != null) sprite = npc.sprite;
            else if (comp != null)
            {
                sprite = comp.sprite;
                if (string.IsNullOrEmpty(sprite) && db != null) sprite = db.Class(comp.classId)?.sprite ?? "";
            }
            if (string.IsNullOrEmpty(sprite)) sprite = "npc_villager_a";

            var v = UnitView.Create(sprite, 0f, NpcRing);
            v.FadesOccluders = false;
            v.DisplayName = NpcDisplayName(id);
            var home = ToUnity(n.pos);
            int facing = n.flip ? -1 : 1;
            var e = new NpcEntry
            {
                Def = n, Id = id, Npc = npc, Companion = comp, View = v, Home = home, HomeFacing = facing,
                Wanders = npc != null && npc.wanders,
                NextIdle = idleClock + 2f + presentationRng.Range(0f, 5f),
            };
            npcEntries.Add(e);
            npcByView[v] = e;
            if (from.HasValue && (from.Value - home).sqrMagnitude > 0.04f)
            {
                v.Teleport(from.Value);
                WalkView(v, home, 2.6f, () => { if (v != null) v.SetFacing(facing); });
            }
            else
            {
                v.Teleport(home);
                v.SetFacing(facing);
            }
            return e;
        }

        string NpcDisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (Session != null) return Session.NpcName(id);
            var db = Db;
            if (db != null)
            {
                if (db.Npcs.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n.name)) return n.name;
                if (db.Companions.TryGetValue(id, out var c) && !string.IsNullOrEmpty(c.name)) return c.name;
            }
            return id;
        }

        NpcEntry FindNpcEntry(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < npcEntries.Count; i++) if (npcEntries[i].Id == id) return npcEntries[i];
            return null;
        }

        // ------------------------------------------------------------ encounter views

        void CreateEncounterViews(EncounterDef enc, List<KeyValuePair<Unit, UnitView>> reusable, bool appearFx = false)
        {
            if (enc == null || enc.enemies == null) return;
            var db = Db;
            Vector2 lookAt = LeaderFeet();
            var previews = EncounterPreview(enc.id, true);
            int index = -1;
            foreach (var ed in enc.enemies)
            {
                if (ed == null) continue;
                var cdef = db != null ? db.Creature(ed.creature) : null;
                if (cdef == null) continue;
                index++;   // PreviewEncounter skips the same entries (null / unknown creature)
                var home = ToUnity(ed.pos);
                UnitView v = null;
                if (reusable != null)
                {
                    int best = -1;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < reusable.Count; i++)
                    {
                        var ru = reusable[i].Key;
                        if (ru.Creature == null || ru.Creature.id != cdef.id || reusable[i].Value == null) continue;
                        float d = (reusable[i].Value.FeetPosition - home).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best >= 0)
                    {
                        v = reusable[best].Value;
                        reusable.RemoveAt(best);
                        v.SetHovered(false);
                        v.SetTargetable(null);
                        v.SetActiveTurn(false);
                        v.SetSelected(false);
                        v.StopCasting();
                        v.SetTint(Color.white);
                        if (v.IsPolymorphed) v.SetPolymorphed(false);
                        if ((v.FeetPosition - home).sqrMagnitude > 0.01f) WalkView(v, home, 2.2f, null);
                    }
                }
                if (v == null)
                {
                    v = UnitView.Create(cdef.sprite, Mathf.Max(0f, cdef.size), EnemyRing);
                    v.Teleport(home);
                    if (Mathf.Abs(lookAt.x - home.x) > 0.05f) v.SetFacing(lookAt.x < home.x ? -1 : 1);
                    if (appearFx) FxSystem.Puff(v.CenterPosition, new Color(0.85f, 0.85f, 0.8f), 1f);
                }
                v.DisplayName = cdef.name;
                v.Tag = null;
                v.UnitId = int.MinValue;
                var e = new EnemyEntry
                {
                    Encounter = enc, Def = ed, Creature = cdef, View = v, Home = home, Index = index,
                    Preview = previews != null && index < previews.Count && previews[index].CreatureId == cdef.id ? previews[index] : null,
                    Wanders = cdef.type == CreatureType.Beast || cdef.type == CreatureType.Critter,
                    NextIdle = idleClock + 1f + presentationRng.Range(0f, 6f),
                };
                enemyEntries.Add(e);
                enemyByView[v] = e;
            }
        }

        bool HasEncounterViews(EncounterDef enc)
        {
            for (int i = 0; i < enemyEntries.Count; i++) if (enemyEntries[i].Encounter == enc) return true;
            return false;
        }

        // ------------------------------------------------------------ spawning & sync

        /// <summary>Spawns every view of the current map (after MapView.Build).</summary>
        void SpawnWorldViews()
        {
            var s = Session;
            if (s == null) return;
            foreach (var u in s.PartyUnits()) EnsureView(u);
            foreach (var n in s.VisibleNpcs()) if (n != null && !string.IsNullOrEmpty(n.npc)) CreateNpcView(n, null);
            foreach (var e in s.VisibleEncounters()) CreateEncounterViews(e, null);
        }

        /// <summary>
        /// Brings party, NPC and encounter views in line with the session (out of combat): recruited companions
        /// step out of their NPC spot, dismissed ones walk back to it, pets appear/vanish, encounters that were
        /// resolved disappear. reusableEnemies: views of living battle enemies (after leaving a fight) to reuse.
        /// </summary>
        void SyncWorldViews(List<KeyValuePair<Unit, UnitView>> reusableEnemies = null)
        {
            var s = Session;
            if (s == null || MapView.Current == null || backdropActive || battlePresenting || s.Battle != null) return;

            // ---- party (+ pets, and the totems/temporary guardians placed out of combat: Session.OwnedSummons)
            var units = s.PartyUnits();
            foreach (var u in s.OwnedSummons())
                if (u != null && !units.Contains(u)) units.Add(u);
            tmpUnitSet.Clear();
            foreach (var u in units) tmpUnitSet.Add(u);
            tmpUnits.Clear();
            foreach (var kv in views) if (!tmpUnitSet.Contains(kv.Key)) tmpUnits.Add(kv.Key);
            Dictionary<string, Vector2> leftFrom = null;
            foreach (var u in tmpUnits)
            {
                var v = ViewOf(u);
                if (v != null)
                {
                    if (u.Companion != null && u.Class != null) (leftFrom ??= new Dictionary<string, Vector2>())[u.Companion.id] = v.FeetPosition;
                    else if (u.IsPetLike && !v.IsDead) FxSystem.Puff(v.CenterPosition, new Color(0.85f, 0.82f, 0.95f), 0.8f);
                }
                RemoveView(u);
            }
            foreach (var u in units)
            {
                if (ViewOf(u) != null) continue;
                Vector2? from = null;
                if (u.Companion != null)
                {
                    var ne = FindNpcEntry(u.Companion.id);
                    if (ne != null && ne.View != null) from = ne.View.FeetPosition;
                }
                var v = EnsureView(u);
                if (v == null) continue;
                var dest = ToUnity(u.Position);
                if (from.HasValue && (from.Value - dest).sqrMagnitude > 0.04f)
                {
                    v.Teleport(from.Value);
                    WalkView(v, dest, 3.0f, null);
                }
                else if (u.IsPetLike && !InFieldAction)   // summoned right now: the Summon event plays its own effect
                {
                    FxSystem.Sparkles(v.CenterPosition, Ui.SchoolColor(u.Creature != null ? u.Creature.meleeSchool : School.Arcane), 10);
                }
            }
            if (Selected != null && !tmpUnitSet.Contains(Selected) && s.Leader != null) SetSelectedInternal(s.Leader);
            var leaderView = ViewOf(s.Leader);
            var rig = CameraRig.Instance;
            if (rig != null && leaderView != null && rig.Follow != leaderView.transform && s.Mode == SessionMode.Exploration)
                rig.Follow = leaderView.transform;

            // ---- NPCs
            var visibleNpcs = s.VisibleNpcs();
            for (int i = npcEntries.Count - 1; i >= 0; i--)
            {
                var e = npcEntries[i];
                if (visibleNpcs.Contains(e.Def)) continue;
                bool becameMember = e.Companion != null && s.FindMember(e.Id) != null && s.IsInParty(s.FindMember(e.Id));
                if (!becameMember && e.View != null) FxSystem.Puff(e.View.CenterPosition, new Color(0.92f, 0.9f, 0.86f), 0.9f);
                DisposeView(e.View);
            }
            foreach (var n in visibleNpcs)
            {
                if (n == null || string.IsNullOrEmpty(n.npc)) continue;
                bool have = false;
                for (int i = 0; i < npcEntries.Count; i++) if (npcEntries[i].Def == n) { have = true; break; }
                if (have) continue;
                Vector2? from = null;
                if (leftFrom != null && leftFrom.TryGetValue(n.npc, out var p)) from = p;
                var e = CreateNpcView(n, from);
                if (!from.HasValue && e.View != null) FxSystem.Sparkles(e.View.CenterPosition, new Color(1f, 0.92f, 0.7f), 8);
            }

            // ---- encounters
            var visibleEnc = s.VisibleEncounters();
            for (int i = enemyEntries.Count - 1; i >= 0; i--)
            {
                var e = enemyEntries[i];
                if (visibleEnc.Contains(e.Encounter)) continue;
                if (e.View != null) FxSystem.Puff(e.View.CenterPosition, new Color(0.85f, 0.85f, 0.9f), 1f);
                DisposeView(e.View);
            }
            foreach (var enc in visibleEnc)
                if (!HasEncounterViews(enc)) CreateEncounterViews(enc, reusableEnemies, true);
        }

        /// <summary>Re-evaluates flag-driven world state: chests/transitions (requireFlag), NPCs, encounters. Remembers the
        /// session's FlagsVersion it was built for (see RefreshWorldIfFlagsChanged).</summary>
        void RefreshWorldFromFlags()
        {
            var s = Session;
            var map = MapView.Current;
            if (s == null || map == null || backdropActive) return;
            worldFlagsVersion = s.FlagsVersion;
            worldFlagsSession = s;
            map.RefreshFlags(s.Flags.Test);
            SyncWorldViews();
        }

        int worldFlagsVersion = -1;
        GameSession worldFlagsSession;

        /// <summary>
        /// Rebuilds the flag-dependent world (chests, transitions, NPCs, encounters) when the session's story flags changed
        /// since the last rebuild (GameSession.FlagsVersion; FlagsChanged events land here too). Only in exploration and
        /// out of a presented battle: changes made during a conversation or a fight are picked up when it ends.
        /// </summary>
        void RefreshWorldIfFlagsChanged()
        {
            var s = Session;
            if (s == null || s.Mode != SessionMode.Exploration || battlePresenting || s.Battle != null || MapView.Current == null || backdropActive) return;
            if (worldFlagsSession == s && worldFlagsVersion == s.FlagsVersion) return;
            RefreshWorldFromFlags();
        }

        /// <summary>
        /// Out of combat (5x/s): re-applies every aura-driven look of the unit views — stealth, polymorph and the
        /// control/invulnerability tint — from the units' real auras. Self-cast state auras survive a won fight
        /// (Divine Shield, Ice Block…) and expire in the field without a presented event, so the tint the combat
        /// presenter left must follow them here. The combat presenter owns these visuals during a battle.
        /// </summary>
        void SyncStealthVisuals()
        {
            var s = Session;
            if (s == null || s.Battle != null || battlePresenting) return;
            foreach (var kv in views)
            {
                var v = kv.Value;
                if (v == null || kv.Key == null) continue;
                ApplyAuraStateVisuals(kv.Key, v);
            }
        }

        static readonly Color BanishTint = new Color(0.78f, 0.72f, 1f, 0.8f);
        static readonly Color InvulnerableTint = new Color(1f, 0.95f, 0.72f);
        static readonly Color FrostTint = new Color(0.62f, 0.85f, 1f);
        static readonly Color StunTint = new Color(0.96f, 0.9f, 0.62f);
        static readonly Color FearTint = new Color(0.8f, 0.68f, 0.96f);
        static readonly Color ConfuseTint = new Color(0.92f, 0.74f, 0.95f);
        static readonly Color SleepTint = new Color(0.78f, 0.8f, 0.95f);
        static readonly Color RootTint = new Color(0.78f, 0.93f, 0.66f);

        /// <summary>Same mapping as the combat presenter's state visuals (CombatController.ApplyStateMask), from the
        /// unit's current auras. Dead views are left alone (their fade-out owns the look).</summary>
        static void ApplyAuraStateVisuals(Unit u, UnitView v)
        {
            if (v.IsDead) return;
            int mask = 0;
            bool frost = false;
            if (u.IsAlive)
            {
                var auras = u.Auras;
                for (int i = 0; i < auras.Count; i++)
                {
                    var a = auras[i];
                    var def = a != null ? a.Def : null;
                    if (def == null || def.states == null || def.states.Length == 0) continue;
                    bool rootOrStun = false;
                    for (int k = 0; k < def.states.Length; k++)
                    {
                        var st = def.states[k];
                        mask |= 1 << (int)st;
                        if (st == UnitState.Root || st == UnitState.Stun) rootOrStun = true;
                    }
                    if (rootOrStun && def.school == School.Frost) frost = true;
                }
            }
            v.SetStealthed(HasStateBit(mask, UnitState.Stealth) || HasStateBit(mask, UnitState.Invisible));
            v.SetPolymorphed(HasStateBit(mask, UnitState.Polymorph));
            Color tint = Color.white;
            if (HasStateBit(mask, UnitState.Banish)) tint = BanishTint;
            else if (HasStateBit(mask, UnitState.Invulnerable)) tint = InvulnerableTint;
            else if (frost) tint = FrostTint;
            else if (HasStateBit(mask, UnitState.Stun)) tint = StunTint;
            else if (HasStateBit(mask, UnitState.Fear)) tint = FearTint;
            else if (HasStateBit(mask, UnitState.Confuse)) tint = ConfuseTint;
            else if (HasStateBit(mask, UnitState.Sleep) || HasStateBit(mask, UnitState.Incapacitate)) tint = SleepTint;
            else if (HasStateBit(mask, UnitState.Root)) tint = RootTint;
            v.SetTint(tint);
        }

        static bool HasStateBit(int mask, UnitState s) => (mask & (1 << (int)s)) != 0;

        // ------------------------------------------------------------ movement helpers

        /// <summary>Walks a view to a point along an exploration path (straight line when no nav grid).</summary>
        void WalkView(UnitView v, Vector2 dest, float speed, Action arrive)
        {
            if (v == null) return;
            var nav = Session != null ? Session.Nav : backdropNav;
            if (nav != null)
            {
                var p = nav.FindPath(ToVec2(v.FeetPosition), ToVec2(dest), NavAgent.Default.IgnoringAllUnits(), float.PositiveInfinity, wanderPath);
                if (p.Count >= 2)
                {
                    v.MoveAlong(p.Points, speed, arrive);
                    return;
                }
            }
            v.MoveAlong(new List<Vector2>(1) { dest }, speed, arrive);
        }

        Vector2 LeaderFeet()
        {
            var lv = Session != null ? ViewOf(Session.Leader) : null;
            return lv != null ? lv.FeetPosition : Vector2.zero;
        }

        // ------------------------------------------------------------ idle life

        void UpdateWanderers(float dt)
        {
            idleClock += dt;
            if (npcEntries.Count == 0 && enemyEntries.Count == 0) return;
            var s = Session;
            bool exploring = s != null && s.Mode == SessionMode.Exploration;
            bool frozen = s != null && (s.Mode == SessionMode.Combat || s.Mode == SessionMode.GameOver);
            UnitView leader = s != null ? ViewOf(s.Leader) : null;
            string talkingTo = s != null && s.Mode == SessionMode.Dialogue ? s.Dialogue.OwnerId : null;
            var nav = s != null ? s.Nav : backdropNav;

            for (int i = 0; i < npcEntries.Count; i++)
            {
                var e = npcEntries[i];
                var v = e.View;
                if (v == null || v.IsMoving) continue;
                if (talkingTo != null && talkingTo == e.Id) continue;   // the dialogue camera handles facing
                // NPCs turn towards the leader when the party comes close
                if (leader != null && (exploring || talkingTo != null))
                {
                    float d2 = (leader.FeetPosition - v.FeetPosition).sqrMagnitude;
                    if (d2 < 3.6f * 3.6f) { v.FaceTowards(leader.FeetPosition); e.NextIdle = Mathf.Max(e.NextIdle, idleClock + 3f); continue; }
                }
                if (idleClock < e.NextIdle || frozen) continue;
                e.NextIdle = idleClock + presentationRng.Range(4f, 11f);
                if (e.Wanders && nav != null && (v.FeetPosition - e.Home).sqrMagnitude < 9f
                    && nav.RandomWalkablePointNear(ToVec2(e.Home), 2.2f, presentationRng, NavAgent.Default.IgnoringAllUnits(), out var p))
                {
                    WalkView(v, ToUnity(p), 1.1f, null);
                }
                else if (!e.Wanders && (v.FeetPosition - e.Home).sqrMagnitude > 0.04f)
                {
                    int f = e.HomeFacing;
                    WalkView(v, e.Home, 1.4f, () => { if (v != null) v.SetFacing(f); });
                }
                else if (v.Facing != e.HomeFacing) v.SetFacing(e.HomeFacing);
                else if (presentationRng.Chance(35f)) v.SetFacing(-e.HomeFacing);   // a glance around
            }

            if (frozen || talkingTo != null) return;
            for (int i = 0; i < enemyEntries.Count; i++)
            {
                var e = enemyEntries[i];
                var v = e.View;
                if (v == null || v.IsMoving || idleClock < e.NextIdle) continue;
                e.NextIdle = idleClock + presentationRng.Range(5f, 12f);
                if (e.Wanders && nav != null
                    && nav.RandomWalkablePointNear(ToVec2(e.Home), 1.4f, presentationRng, NavAgent.Default.IgnoringAllUnits(), out var p))
                    WalkView(v, ToUnity(p), 0.9f, null);
                else if (presentationRng.Chance(40f)) v.SetFacing(-v.Facing);
            }
        }
    }
}
