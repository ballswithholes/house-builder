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

        /// <summary>Out-of-combat health regeneration: % of max health per second (+ Spirit × OocHealthPerSpirit, + HP5).</summary>
        public static float OocHealthPctPerSecond = 0.5f;
        public static float OocHealthPerSpirit = 0.1f;
        /// <summary>Out-of-combat mana regeneration on top of Spirit regen: % of max mana per second.</summary>
        public static float OocManaPctPerSecond = 0.5f;
        /// <summary>Out-of-combat pet health/mana regeneration: % of max per second.</summary>
        public static float OocPetPctPerSecond = 1.5f;

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

        void ElapseOutOfCombat(float dt)
        {
            var units = PartyUnits();
            foreach (var u in units)
            {
                ElapseTimers(u, dt);
                RegenOutOfCombat(u, dt);
                u.SecondsSinceCombat += dt;
                u.SecondsSinceManaSpent += dt;
            }
            var f = Field;
            if (f == null) return;
            foreach (var u in new List<Unit>(f.Units))
            {
                if (!f.Units.Contains(u)) continue;
                if (u.IsAlive) f.ElapseAuras(u, dt);
                if (u.Lifetime > 0f && (u.Kind == UnitKind.Summon || u.Kind == UnitKind.Totem || u.Kind == UnitKind.Pet))
                {
                    u.Lifetime -= dt;
                    if (u.Lifetime <= 1e-3f) f.RemoveUnit(u, "expired");
                }
            }
            f.RefreshAreaAuras();
        }

        /// <summary>Cooldowns, school lockouts and proc internal cooldowns (seconds).</summary>
        static void ElapseTimers(Unit u, float dt)
        {
            ElapseDict(u.Cooldowns, dt);
            ElapseDict(u.ProcCooldowns, dt);
            if (u.Lockouts.Count > 0)
            {
                var keys = new List<School>(u.Lockouts.Keys);
                foreach (var k in keys)
                {
                    float v = u.Lockouts[k] - dt;
                    if (v <= 1e-3f) u.Lockouts.Remove(k); else u.Lockouts[k] = v;
                }
            }
        }

        static void ElapseDict(Dictionary<string, float> d, float dt)
        {
            if (d.Count == 0) return;
            var keys = new List<string>(d.Keys);
            foreach (var k in keys)
            {
                float v = d[k] - dt;
                if (v <= 1e-3f) d.Remove(k); else d[k] = v;
            }
        }

        /// <summary>Generous out-of-combat regeneration (Design §2/§3): health, mana (five-second rule), energy, focus, rage decay.</summary>
        static void RegenOutOfCombat(Unit u, float dt)
        {
            if (!u.IsAlive) return;
            var st = u.Stats;
            bool pet = u.Class == null;
            if (u.Health < u.MaxHealth)
            {
                float hps = pet ? u.MaxHealth * OocPetPctPerSecond / 100f
                    : u.MaxHealth * OocHealthPctPerSecond / 100f + st.Spirit * OocHealthPerSpirit;
                hps += st.HealthRegen / 5f;
                u.Health = Math.Min(u.MaxHealth, u.Health + Math.Max(0f, hps) * dt);
            }
            if (u.MaxMana > 0f && u.Mana < u.MaxMana)
            {
                float mps;
                if (pet) mps = u.MaxMana * OocPetPctPerSecond / 100f;
                else
                {
                    float spirit = st.SpiritRegenPerTick / 2f;
                    if (u.SecondsSinceManaSpent < RulesConstants.FiveSecondRule) spirit *= st.SpiritRegenWhileCasting / 100f;
                    mps = spirit + u.MaxMana * OocManaPctPerSecond / 100f;
                }
                mps += st.ManaRegen / 5f;
                u.Mana = Math.Min(u.MaxMana, u.Mana + Math.Max(0f, mps) * dt);
            }
            switch (u.PowerType)
            {
                case ResourceType.Energy:
                    u.SetResource(ResourceType.Energy, u.Energy + RulesConstants.EnergyPerSecondOoc * st.EnergyRegen * dt);
                    break;
                case ResourceType.Focus:
                    u.SetResource(ResourceType.Focus, u.Focus + RulesConstants.FocusPerSecondOoc * dt);
                    break;
                case ResourceType.Rage:
                    if (u.Rage > 0f) u.SetResource(ResourceType.Rage, u.Rage - RulesConstants.RageDecayPerSecondOoc * dt);
                    break;
            }
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
            Raise(new SessionEvent { Kind = SessionEventKind.Rested, Text = "You wake rested as the morning lanterns are snuffed." });
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
