// Game clock (time of day), out-of-combat time (regen, cooldowns, auras, summon lifetimes) and resting.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        float gameHour = 12f;
        int day = 1;
        float playSeconds;
        string lastPhase = "";

        /// <summary>Hour of day, 0..24.</summary>
        public float GameHour => gameHour;
        public int Day => day;
        /// <summary>Real seconds played (all modes).</summary>
        public float PlaySeconds => playSeconds;

        /// <summary>"dawn" | "day" | "dusk" | "night" (maps without a day/night cycle report their fixed time).</summary>
        public string TimeOfDay
        {
            get
            {
                var amb = Map?.Def?.ambient;
                if (amb != null && !amb.dayNightCycle && !string.IsNullOrEmpty(amb.timeOfDay)) return amb.timeOfDay;
                return PhaseOf(gameHour);
            }
        }

        /// <summary>DayNight phases: dawn [5,8), day [8,17.5), dusk [17.5,20.5), night otherwise.</summary>
        public static string PhaseOf(float hour)
        {
            if (hour >= 5f && hour < 8f) return "dawn";
            if (hour >= 8f && hour < 17.5f) return "day";
            if (hour >= 17.5f && hour < 20.5f) return "dusk";
            return "night";
        }

        /// <summary>
        /// Advances real time. Out of combat and outside dialogue: the clock (Settings.GameHoursPerRealMinute),
        /// regeneration, cooldowns, aura ticks/expiry (food/drink) and summon lifetimes. Combat time is turn based.
        /// Companions waiting at camp are not ticked.
        /// </summary>
        public void Tick(float realSeconds)
        {
            if (!hasGame || !(realSeconds > 0f) || float.IsInfinity(realSeconds)) return;
            playSeconds += realSeconds;
            if (Mode != SessionMode.Exploration) return;
            AdvanceClock(realSeconds * Math.Max(0f, Settings.GameHoursPerRealMinute) / 60f);
            ElapseOutOfCombat(realSeconds);
        }

        /// <summary>Moves the clock forward by game hours (raises TimeOfDayChanged on phase changes).</summary>
        public void AdvanceClock(float hours)
        {
            if (!(hours > 0f)) return;
            gameHour += hours;
            while (gameHour >= 24f) { gameHour -= 24f; day++; }
            CheckTimeOfDay();
        }

        /// <summary>Sets the clock (debug, scripted scenes).</summary>
        public void SetTime(float hour, int newDay = 0)
        {
            gameHour = ((hour % 24f) + 24f) % 24f;
            if (newDay > 0) day = newDay;
            CheckTimeOfDay();
        }

        void CheckTimeOfDay()
        {
            var phase = TimeOfDay;
            if (phase == lastPhase) return;
            bool first = lastPhase.Length == 0;
            lastPhase = phase;
            if (!first) Raise(new SessionEvent { Kind = SessionEventKind.TimeOfDayChanged, Id = phase, Text = phase });
        }

        /// <summary>Real time out of combat for the party, its pets and field summons (Rules: Battle.TickOutOfCombat —
        /// cooldowns, aura ticks/expiry incl. food and drink, summon lifetimes, regeneration).</summary>
        void ElapseOutOfCombat(float dt)
        {
            if (Field == null) RebuildField();
            Field?.TickOutOfCombat(dt);
            Field?.RefreshAreaAuras();
        }

        // ================================================================= resting

        /// <summary>Null when the party may take a long rest here (restArea maps), else the reason.</summary>
        public string CannotRestReason()
        {
            if (!hasGame) return "No game.";
            if (Mode != SessionMode.Exploration) return "You cannot rest now.";
            if (Map?.Def == null || !Map.Def.restArea) return "You can only rest at an inn or a safe camp.";
            return null;
        }

        /// <summary>Long rest at a camp (restArea maps). False (LastError) when not allowed.</summary>
        public bool LongRest()
        {
            var why = CannotRestReason();
            if (why != null) { LastError = why; return false; }
            DoLongRest();
            return true;
        }

        /// <summary>Rest outcome (innkeeper): a long rest anywhere. Deferred while talking.</summary>
        public void Rest()
        {
            if (resetting) return;
            if (Dialogue.IsActive) { afterDialogue.Add(Rest); return; }
            if (Battle != null || !hasGame) return;
            DoLongRest();
        }

        void DoLongRest()
        {
            foreach (var u in roster) RestUnit(u);
            foreach (var u in roster)
            {
                if (u.HunterPet != null && u.HunterPet.Dead) { u.HunterPet.Dead = false; u.HunterPet.HealthFraction = 1f; }
                if (u.Pet != null && u.Pet.IsAlive) RestUnit(u.Pet);
            }
            float target = gameHour < 4f ? 8f : 32f;
            AdvanceClock(target - gameHour);
            Raise(new SessionEvent { Kind = SessionEventKind.Rested, Text = "You wake rested. Morning light spills over the valley." });
        }

        void RestUnit(Unit u)
        {
            u.Dead = false;
            u.Downed = false;
            u.Cooldowns.Clear();
            u.Lockouts.Clear();
            u.ProcCooldowns.Clear();
            for (int i = u.Auras.Count - 1; i >= 0; i--)
            {
                if (i >= u.Auras.Count) continue;
                var a = u.Auras[i];
                if (a.IsPassive || !a.IsDebuff) continue;
                if (Field != null && Field.Units.Contains(u)) Field.RemoveAura(a, AuraRemoveReason.Dispelled);
                else { u.Auras.RemoveAt(i); u.InvalidateStats(); }
            }
            u.InvalidateStats();
            u.RestoreFull();
            u.SecondsSinceCombat = 999f;
            u.SecondsSinceManaSpent = 999f;
        }

        /// <summary>HealParty outcome: full health/mana for the party and pets (no time passes).</summary>
        public void HealParty()
        {
            if (resetting || !hasGame) return;
            foreach (var u in PartyUnits())
            {
                u.Dead = false;
                u.Downed = false;
                u.Health = u.MaxHealth;
                if (u.MaxMana > 0) u.Mana = u.MaxMana;
            }
            Raise(new SessionEvent { Kind = SessionEventKind.PartyHealed, Text = "Your wounds close." });
        }
    }
}
