// The battle as the player has SEEN it. Battle resolves every action synchronously when the command is issued; the
// CombatController animates the resulting events afterwards (wind-up, bolt flight, impact, end-of-turn swings). Reading
// the live rules state would make bars drop, plates vanish and the turn strip move on before the blow lands, so the HUD
// reads health, death/downed state and the acting unit through here instead.
//
// Fed by GameFlow.CombatEventPresented (raised in event order when the controller starts presenting an event): every
// battle event after the last presented one is still "in flight", and its effect on health/death is undone for display.
// When the controller waits for player input (or has finished) everything has been shown and the live state is used.
using System;
using System.Collections.Generic;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class HudPresented
    {
        static GameFlow subscribed;
        static readonly Action<CombatEvent> onPresented = OnPresented;

        static Battle battle;
        static int cursor;      // index in battle.Events just after the last presented event
        static Unit active;     // Source of the last presented TurnStart (null = not seen yet)

        struct Tail
        {
            public float Health;   // health the in-flight events took away (+) or gave (−)
            public bool Death, Downed, Revive;
        }

        static readonly Dictionary<Unit, Tail> tail = new Dictionary<Unit, Tail>();
        static int tailFrame = -1, tailCount = -1, tailCursor = -1;
        static Battle tailBattle;

        /// <summary>Subscribes to the flow (called every frame from the HUD's Tick).</summary>
        public static void Update()
        {
            var f = GameFlow.Instance;
            if (f != subscribed)
            {
                if (subscribed != null) subscribed.CombatEventPresented -= onPresented;
                subscribed = f;
                if (f != null) f.CombatEventPresented += onPresented;
            }
            Sync();
        }

        /// <summary>Tracks the current battle; snaps to the live state while the controller is idle for player input.</summary>
        static Battle Sync()
        {
            var b = Hud.Battle;
            if (b != battle)
            {
                battle = b;
                cursor = 0;
                active = null;
                tail.Clear();
                tailBattle = null;
            }
            if (b == null) return null;
            int n = b.Events.Count;
            if (cursor > n) cursor = n;   // trimmed list: never "un-present"
            bool settled = false;
            try
            {
                var c = Hud.Combat;
                settled = c == null || c.Finished || c.IsPlayerTurn;
            }
            catch (Exception) { settled = true; }
            if (settled)
            {
                cursor = n;
                active = b.ActiveUnit;
            }
            return b;
        }

        static void OnPresented(CombatEvent e)
        {
            if (e == null) return;
            var b = Sync();
            if (b == null) return;
            var ev = b.Events;
            int n = ev.Count;
            for (int i = Mathf.Max(0, cursor); i < n; i++)
            {
                if (!ReferenceEquals(ev[i], e)) continue;
                cursor = i + 1;
                break;
            }
            if (e.Type == CombatEventType.TurnStart && e.Source != null) active = e.Source;
        }

        static void BuildTail(Battle b)
        {
            int n = b != null ? b.Events.Count : 0;
            int f = Time.frameCount;
            if (f == tailFrame && n == tailCount && cursor == tailCursor && b == tailBattle) return;
            tailFrame = f;
            tailCount = n;
            tailCursor = cursor;
            tailBattle = b;
            tail.Clear();
            if (b == null || cursor >= n) return;
            var ev = b.Events;
            for (int i = Mathf.Max(0, cursor); i < n; i++)
            {
                var e = ev[i];
                if (e == null || e.Target == null) continue;
                switch (e.Type)
                {
                    case CombatEventType.Damage:
                    {
                        float lost = Mathf.Max(0f, e.Amount - e.Overkill);
                        if (lost <= 0f) break;
                        tail.TryGetValue(e.Target, out var t);
                        t.Health += lost;
                        tail[e.Target] = t;
                        break;
                    }
                    case CombatEventType.Heal:
                    {
                        if (e.Amount <= 0f) break;
                        tail.TryGetValue(e.Target, out var t);
                        t.Health -= e.Amount;
                        tail[e.Target] = t;
                        break;
                    }
                    case CombatEventType.Death:
                    {
                        tail.TryGetValue(e.Target, out var t);
                        t.Death = true;
                        tail[e.Target] = t;
                        break;
                    }
                    case CombatEventType.Downed:
                    {
                        tail.TryGetValue(e.Target, out var t);
                        t.Downed = true;
                        tail[e.Target] = t;
                        break;
                    }
                    case CombatEventType.Revive:
                    {
                        tail.TryGetValue(e.Target, out var t);
                        t.Revive = true;
                        t.Health -= Mathf.Max(0f, e.Amount);   // revived from 0 to Amount
                        tail[e.Target] = t;
                        break;
                    }
                }
            }
        }

        static bool Lookup(Unit u, out Tail t)
        {
            t = default;
            if (u == null) return false;
            var b = Sync();
            if (b == null) return false;
            BuildTail(b);
            return tail.Count > 0 && tail.TryGetValue(u, out t);
        }

        static bool FallsAsDowned(Unit u) => battle != null && u.Team == battle.PlayerTeam && u.IsCharacter;

        // ================================================================ queries

        /// <summary>Health as presented (live health with the in-flight damage/heals undone), clamped to 0..max.</summary>
        public static float Health(Unit u)
        {
            if (u == null) return 0f;
            float h = u.Health;
            if (Lookup(u, out var t)) h += t.Health;
            return Mathf.Clamp(h, 0f, Mathf.Max(0f, u.MaxHealth));
        }

        /// <summary>Dead as presented: a unit stays alive on screen until its Death event is shown.</summary>
        public static bool Dead(Unit u)
        {
            if (u == null) return true;
            if (!Lookup(u, out var t)) return u.Dead;
            if (u.Dead) return !t.Death;                                     // the killing blow is still in flight
            // revived by an in-flight event (and not felled in flight first): still down on screen
            return !u.Downed && t.Revive && !t.Death && !t.Downed && !FallsAsDowned(u);
        }

        /// <summary>Downed as presented (party characters at 0 health waiting for help).</summary>
        public static bool Downed(Unit u)
        {
            if (u == null) return false;
            if (!Lookup(u, out var t)) return u.Downed && !u.Dead;
            if (u.Dead) return false;
            if (u.Downed) return !t.Downed;
            return t.Revive && !t.Death && !t.Downed && FallsAsDowned(u);
        }

        /// <summary>Alive as presented.</summary>
        public static bool Alive(Unit u) => u != null && !Dead(u) && !Downed(u);

        /// <summary>
        /// The unit whose turn is on screen: the last presented TurnStart while the controller is still animating, the live
        /// active unit otherwise (or before any turn has been shown).
        /// </summary>
        public static Unit ActiveUnit(Battle b)
        {
            if (b == null) return null;
            var cur = Sync();
            if (cur != b) return b.ActiveUnit;
            var u = active;
            if (u == null || !b.Units.Contains(u)) return b.ActiveUnit;
            return u;
        }
    }
}
