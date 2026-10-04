// Companion auto-play: role-aware scoring of every known ability on candidate targets.
// Tanks taunt and hold threat; healers heal by deficit; DPS respect the tank's threat, keep buffs/debuffs up,
// rogues build then finish, hunters stay out of the dead zone, shamans drop totems, paladins keep seals.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public static partial class AI
    {
        sealed class Candidate
        {
            public AbilityDef Ability;
            public Unit Target;
            public Vec2? Point;
            public float Score;
            /// <summary>Rank to use (0 = highest known; low-mana healers downrank).</summary>
            public int Rank;
            public bool NeedsMove;
            public string Why = "";
        }

        static AIStep CompanionStep(Battle b, Unit u)
        {
            var enemies = VisibleEnemies(b, u);
            var allies = b.AlliesOf(u, true, false);
            var role = u.Role;
            var mem = u.AIMemory;

            // help up a downed ally next to us (or walk to one if we are the healer / nobody is fighting us)
            foreach (var d in b.Units)
            {
                if (d.Team != u.Team || !d.Downed) continue;
                var help = b.Db.Ability("help_up");
                if (help == null) break;
                var chk = b.CanUse(u, help, d);
                if (chk.Ok && u.TimeLeft >= 1.0f) return Use(u, help, d, null, "help up");
                if (chk.Code == UseFailure.Range && (role == UnitRole.Healer || u.AggroTargetedBy(b) == 0) && u.DistanceTo(d) <= u.MoveLeft + b.MeleeReachOf(u, d))
                {
                    var mv = MoveToRange(b, u, d, b.MeleeReachOf(u, d), "to downed ally");
                    if (mv != null) return mv;
                }
            }

            if (enemies.Count == 0) return AIStep.End(u, "no enemies");
            var focus = FocusTarget(b, u, enemies, role);

            // consumables from the party bags: a healing potion when about to fall, a mana potion when dry
            var potion = PotionStep(b, u);
            if (potion != null) return potion;

            // contextual heals (Lightwell): take one when below 70% health
            if (u.HealthPct < 70f)
                foreach (var id in Specials.ContextualAbilities(b, u))
                {
                    var ca = b.Db.Ability(id);
                    if (ca == null || ca.aiHint != "Heal" || Failed(u, ca, u)) continue;
                    if (b.CanUse(u, ca, u).Ok) return Use(u, ca, u, null, "contextual heal");
                }

            Candidate best = null;
            foreach (var kv in u.Abilities)
            {
                var a = b.Db.Ability(kv.Key);
                if (a == null || a.passive || a.autoAttack || a.id == "help_up") continue;
                if (a.nextSwing && u.QueuedSwing == a.id) continue;
                if (u.AIMemory.UsedCount(a.id) > 0 && AbilityRules.TimeCost(u, a, AbilityModSet.Empty) <= 0f && a.cooldown <= 0) continue;
                foreach (var c in Candidates(b, u, a, enemies, allies, focus, role))
                {
                    if (c.Score <= 0 || Failed(u, a, c.Target)) continue;
                    var chk = b.CanUse(u, a, c.Target, c.Point, false, c.Rank);
                    if (!chk.Ok)
                    {
                        if (!IsMovableFailure(chk.Code) || c.Target == null || mem.Moves >= 2 || b.CannotMoveReason(u) != null) continue;
                        if (!b.CanUseIgnoringTarget(u, a, false, c.Rank).Ok) continue;
                        c.NeedsMove = true;
                        c.Score *= 0.85f;
                    }
                    if (best == null || c.Score > best.Score) best = c;
                }
            }
            if (best != null)
            {
                if (!best.NeedsMove) { var step = Use(u, best.Ability, best.Target, best.Point, best.Why); step.Rank = best.Rank; return step; }
                var mods = AbilityMods.For(u, best.Ability);
                float range = AbilityRules.RangeMetres(u, best.Ability, best.Target, mods, b.Config) - best.Target.Radius;
                bool behind = best.Ability.requires != null && best.Ability.requires.behindTarget;
                var mv = MoveToRange(b, u, best.Target, Math.Max(0.5f, range), "moving for " + best.Ability.name, behind || (role == UnitRole.MeleeDps && u.ClassId == ClassId.Rogue));
                if (mv != null) return mv;
            }

            // keep auto attack going
            var auto = AutoAttackChoice(b, u, focus, role);
            if (auto != null) return auto;

            // positioning
            var pos = Position(b, u, focus, enemies, role);
            if (pos != null) return pos;
            return AIStep.End(u, "done");
        }

        /// <summary>Sapped, gouged, polymorphed or asleep: damage would wake it, so it is left alone while others fight.</summary>
        static bool SoftControlled(Unit t) =>
            t.HasStateAura(UnitState.Polymorph) || t.HasStateAura(UnitState.Incapacitate) || t.HasStateAura(UnitState.Sleep);

        /// <summary>Main enemy for the unit: tanks pick enemies hitting others, DPS assist the tank.</summary>
        static Unit FocusTarget(Battle b, Unit u, List<Unit> enemies, UnitRole role)
        {
            // never focus a crowd-controlled enemy while another one is free
            var free = enemies.FindAll(e => !SoftControlled(e));
            if (free.Count > 0 && free.Count < enemies.Count) enemies = free;
            Unit tank = null;
            foreach (var a in b.AlliesOf(u, false)) if (a.IsCharacter && a.Role == UnitRole.Tank && a.IsAlive) { tank = a; break; }
            if (role == UnitRole.Tank)
            {
                // an enemy beating on a non-tank, closest first
                Unit loose = null; float ld = float.MaxValue;
                foreach (var e in enemies)
                {
                    if (e.IsTotem) continue;
                    var t = e.AggroTarget;
                    if (t != null && t != u && t.Team == u.Team && t.Role != UnitRole.Tank)
                    {
                        float d = u.DistanceTo(e);
                        if (d < ld) { ld = d; loose = e; }
                    }
                }
                if (loose != null) return loose;
                if (u.AttackTarget != null && u.AttackTarget.IsAlive && enemies.Contains(u.AttackTarget)) return u.AttackTarget;
                return Nearest(u, enemies);
            }
            // assist: the tank's target, else the enemy attacking us, else lowest health
            if (tank != null && tank.AttackTarget != null && tank.AttackTarget.IsAlive && enemies.Contains(tank.AttackTarget)) return tank.AttackTarget;
            if (u.AttackTarget != null && u.AttackTarget.IsAlive && enemies.Contains(u.AttackTarget)) return u.AttackTarget;
            Unit low = null; float lh = float.MaxValue;
            foreach (var e in enemies) if (!e.IsTotem && e.HealthPct < lh) { lh = e.HealthPct; low = e; }
            return low ?? Nearest(u, enemies);
        }

        static Unit Nearest(Unit u, List<Unit> list)
        {
            Unit best = null; float bd = float.MaxValue;
            foreach (var e in list) { float d = u.DistanceTo(e); if (d < bd) { bd = d; best = e; } }
            return best;
        }

        static float ExpectedHeal(Unit u, AbilityDef a) => ExpectedHeal(u, a, 0);

        /// <summary>Average healing of a rank (0 = highest known) of a heal, including HoT ticks and absorbs.</summary>
        internal static float ExpectedHeal(Unit u, AbilityDef a, int rank)
        {
            float v = 0f;
            int eff = AbilityRules.EffLevel(u, a, AbilityRules.UsedRank(u, a, rank));
            foreach (var e in a.effects)
                if (e.type == EffectType.Heal)
                    v += e.pctOfMax > 0 ? 0f : (e.min + Math.Max(e.min, e.max)) * 0.5f + e.perLevel * Math.Max(0, eff - a.learnLevel) + e.coef * u.Stats.HealingPower;
            foreach (var id in AppliedAuras(a))
            {
                var def = u.Db.Aura(id);
                if (def == null) continue;
                foreach (var te in def.tickEffects)
                    if (te.type == EffectType.Heal && def.tickInterval > 0)
                        v += ((te.min + Math.Max(te.min, te.max)) * 0.5f + te.perLevel * Math.Max(0, eff - a.learnLevel) + te.coef * u.Stats.HealingPower) * Math.Max(1f, def.duration / def.tickInterval);
                if (def.absorb != null) v += def.absorb.amount + def.absorb.perLevel * Math.Max(0, eff - a.learnLevel);
            }
            return Math.Max(1f, v);
        }

        /// <summary>Health (percent) below which an AI-played party member drinks a healing potion from the party bags.</summary>
        public const float PotionHealthPct = 30f;
        /// <summary>Mana (percent) below which an AI-played caster drinks a mana potion.</summary>
        public const float PotionManaPct = 15f;

        /// <summary>
        /// A self-targeted consumable from the party inventory: the biggest usable healing potion below
        /// <see cref="PotionHealthPct"/>% health, else a mana potion below <see cref="PotionManaPct"/>% mana (shared potion
        /// cooldown, level/class restrictions and combat-only rules are checked by Battle.CanUseItem).
        /// </summary>
        internal static AIStep PotionStep(Battle b, Unit u)
        {
            if (b.Inventory == null || !b.InCombat || u.Team != b.PlayerTeam || !u.IsCharacter) return null;
            bool low = u.HealthPct < PotionHealthPct;
            bool dry = !low && u.PowerType == ResourceType.Mana && u.MaxMana > 0 && u.ManaPct < PotionManaPct;
            if (!low && !dry) return null;
            ItemInstance best = null;
            float bestV = 0f;
            foreach (var it in b.Inventory.Items)
            {
                var a = it?.Def != null ? b.Db.Ability(it.Def.use) : null;
                if (a == null || a.target != TargetType.Self) continue;
                float v = 0f;
                foreach (var e in a.effects)
                {
                    if (low && e.type == EffectType.Heal)
                        v += e.pctOfMax > 0 ? u.MaxHealth * e.pctOfMax / 100f : (e.min + Math.Max(e.min, e.max)) * 0.5f;
                    if (dry && e.type == EffectType.GainResource && e.resource == "Mana")
                        v += e.pctOfMax > 0 ? u.MaxMana * e.pctOfMax / 100f : (e.amount != 0 ? e.amount : e.min);
                }
                if (v <= bestV || u.AIMemory.Failed.Contains("item:" + it.Id)) continue;
                if (!b.CanUseItem(u, it, u).Ok) continue;
                best = it;
                bestV = v;
            }
            return best == null ? null : new AIStep { Kind = AIStepKind.UseItem, Unit = u, Item = best, Target = u, Reason = low ? "healing potion" : "mana potion" };
        }

        /// <summary>Mana below this percentage: companion healers downrank direct heals to the smallest rank that covers the deficit.</summary>
        public const float DownrankManaPct = 50f;

        /// <summary>
        /// WoW downranking for companion healers: with mana below <see cref="DownrankManaPct"/>%, the lowest known rank of a
        /// direct heal whose average healing covers the deficit (0 = the highest rank: plenty of mana, or no lower rank
        /// is enough).
        /// </summary>
        internal static int DownrankFor(Unit u, AbilityDef a, float deficit)
        {
            int known = u.RankOf(a.id);
            if (known <= 1 || u.PowerType != ResourceType.Mana || u.MaxMana <= 0 || u.ManaPct >= DownrankManaPct) return 0;
            for (int r = 1; r < known; r++)
                if (ExpectedHeal(u, a, r) >= deficit) return r;
            return 0;
        }

        static bool IsTotemAbility(AbilityDef a)
        {
            foreach (var e in a.effects) if (e.type == EffectType.SummonTotem) return true;
            return false;
        }

        static string TotemElementOf(Battle b, AbilityDef a)
        {
            foreach (var e in a.effects)
                if (e.type == EffectType.SummonTotem)
                    return !string.IsNullOrEmpty(e.totemElement) ? e.totemElement : b.Db.Creature(e.summon)?.totemElement ?? "";
            return "";
        }

        static bool IsPartyBuff(AbilityDef a)
        {
            foreach (var e in a.effects)
                if (e.type == EffectType.ApplyAura && (e.target == EffectTarget.AlliesInRadius || e.target == EffectTarget.Party)) return true;
            return false;
        }

        static string PreferredGroupAura(Battle b, Unit u, string group)
        {
            // which aura of an exclusive group (stance, aspect, seal, armor, paladin aura) suits the role
            var role = u.Role;
            string g = group.ToLowerInvariant();
            if (g.Contains("stance"))
                return role == UnitRole.Tank ? "defensive" : (u.Knows("warrior_berserker_stance") && Progression.PointsInTree(u, "tree_warrior_fury") > 10 ? "berserker" : "battle");
            if (g.Contains("aspect")) return "hawk";
            if (g.Contains("seal")) return role == UnitRole.Tank || role == UnitRole.Healer ? "righteousness" : "command|righteousness";
            if (g.Contains("aura")) return role == UnitRole.Tank ? "devotion" : role == UnitRole.Healer ? "concentration|devotion" : "retribution|devotion";
            if (g.Contains("armor")) return u.ClassId == ClassId.Warlock ? "demon" : "mage_armor|frost|ice";
            return null;
        }

        static bool MatchesPreference(string id, string pref)
        {
            if (pref == null) return true;
            foreach (var p in pref.Split('|')) if (id.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static IEnumerable<Candidate> Candidates(Battle b, Unit u, AbilityDef a, List<Unit> enemies, List<Unit> allies, Unit focus, UnitRole role)
        {
            string hint = HintOf(a);
            float basePri = 1f + Math.Max(0, a.aiPriority);
            var mods = AbilityMods.For(u, a);
            float cast = AbilityRules.CastTime(u, a, mods);
            float castPenalty = cast > u.TimeLeft + 0.01f ? 0.8f : 1f;

            // stop a big (telegraphed) enemy cast with anything that cancels it: interrupts, stuns, silences...
            if (hint != "Interrupt" && (a.target == TargetType.Enemy || a.target == TargetType.Any) && cast <= 0.01f)
                foreach (var t in enemies)
                    if (t.Pending != null && !t.IsTotem && IsBigCast(t.Pending) && CancelsCast(b, a, t))
                        yield return new Candidate { Ability = a, Target = t, Score = 24f, Why = "stop " + t.Pending.Ability.name };
            bool manaLow = u.MaxMana > 0 && u.ManaPct < 25f;
            Unit tank = null;
            foreach (var x in allies) if (x != u && x.IsCharacter && x.Role == UnitRole.Tank) { tank = x; break; }

            // ---- totems
            if (IsTotemAbility(a))
            {
                string el = TotemElementOf(b, a);
                if (u.Totems.TryGetValue(el, out var have) && have != null && have.IsAlive && have.DistanceTo(u) < MathUtil.Yd(20f)) yield break;
                yield return new Candidate { Ability = a, Target = u, Point = u.Position, Score = basePri + 3f, Why = "totem " + el };
                yield break;
            }

            switch (hint)
            {
                case "Heal":
                {
                    float expected = ExpectedHeal(u, a);
                    bool hot = AppliedAuras(a).Count > 0;
                    float threshold = role == UnitRole.Healer ? 90f : 45f;
                    bool emergency = a.cooldown >= 60f;   // Lay on Hands and friends: only for a character about to fall
                    foreach (var t in TargetsFor(a, u, allies))
                    {
                        if (t.HealthPct >= threshold) continue;
                        if (emergency && (!t.IsCharacter || t.HealthPct >= 25f)) continue;
                        float deficit = t.MaxHealth - t.Health;
                        if (deficit < expected * 0.5f && t.HealthPct > 50f) continue;
                        if (hot && AlreadyHasAny(a, t, u)) continue;
                        float urgency = 1f - t.HealthPct / 100f;
                        float s = basePri * (1f + 5f * urgency) * (role == UnitRole.Healer ? 1.5f : 1f) * (t.Role == UnitRole.Tank ? 1.2f : 1f) * castPenalty;
                        if (deficit < expected) s *= 0.7f;
                        int rank = hot ? 0 : DownrankFor(u, a, deficit);
                        yield return new Candidate { Ability = a, Target = t, Score = s + 4f, Rank = rank, Why = $"heal {t.Name} {t.HealthPct:0}%" + (rank > 0 ? $" (rank {rank})" : "") };
                    }
                    break;
                }
                case "Buff":
                {
                    var auras = AppliedAuras(a);
                    if (auras.Count == 0) break;
                    var def = b.Db.Aura(auras[0]);
                    if (def == null) break;
                    if (!string.IsNullOrEmpty(def.exclusiveGroup) && (a.target == TargetType.Self || KeptOnCaster(def)))
                    {
                        // stances/aspects/seals/armors: keep one, prefer the role's choice
                        AuraInstance current = null;
                        foreach (var x in u.Auras) if (x.Def.exclusiveGroup == def.exclusiveGroup && (x.Caster == u || x.Caster == null)) { current = x; break; }
                        var pref = PreferredGroupAura(b, u, def.exclusiveGroup);
                        if (current != null && (current.Def.id == def.id || MatchesPreference(current.Def.id, pref) || !MatchesPreference(def.id, pref))) break;
                        if (current == null && pref != null && !MatchesPreference(def.id, pref) && KnowsPreferred(b, u, def.exclusiveGroup, pref)) break;
                        if (u.PowerType == ResourceType.Rage && current != null && u.Rage > 25f) break;
                        // the party-wide versions (Prayer of Fortitude, Arcane Brilliance) wait for the end of the fight too
                        if (IsPartyBuff(a) && TooCostlyForCombat(b, u, a, def, mods)) break;
                        yield return new Candidate { Ability = a, Target = u, Score = basePri + 8f, Why = "group " + def.exclusiveGroup };
                        break;
                    }
                    if (IsPartyBuff(a) || a.target == TargetType.Self)
                    {
                        bool missing = false;
                        var check = IsPartyBuff(a) ? allies : new List<Unit> { u };
                        foreach (var t in check) if (t.IsCharacter || t == u) if (!t.HasAura(def.id)) { missing = true; break; }
                        if (!missing || TooCostlyForCombat(b, u, a, def, mods)) break;
                        yield return new Candidate { Ability = a, Target = u, Score = basePri + (b.Round <= 1 ? 6f : 3f), Why = "party buff" };
                        break;
                    }
                    if (TooCostlyForCombat(b, u, a, def, mods)) break;
                    foreach (var t in TargetsFor(a, u, allies))
                    {
                        if (!t.IsCharacter && t != u.Pet) continue;
                        if (t.HasAura(def.id) || !BuffSuits(def, t)) continue;
                        if (!string.IsNullOrEmpty(def.exclusiveGroup))
                        {
                            bool groupTaken = false;
                            foreach (var x in t.Auras) if (x.Def.exclusiveGroup == def.exclusiveGroup && (!def.exclusivePerCaster || x.Caster == u)) { groupTaken = true; break; }
                            if (groupTaken) continue;
                        }
                        // buffs that outlast the fight (Fortitude, Arcane Intellect, Blessings) go up at its start, like party buffs
                        bool lasting = def.duration >= 60f;
                        float s = basePri + (lasting && b.Round <= 1 ? 6f : 3f) + (t.Role == UnitRole.Tank ? 1f : 0f);
                        if (def.absorb != null) s = t.HealthPct < 80f || t.AggroTargetedBy(b) > 0 ? basePri + 6f : 0f;
                        if (s > 0) yield return new Candidate { Ability = a, Target = t, Score = s, Why = "buff " + t.Name };
                    }
                    break;
                }
                case "Debuff":
                {
                    // a healer keeps the same mana floor for debuffs (Shadow Word: Pain) as for nukes: mana goes to heals first
                    if (role == UnitRole.Healer && a.cost.type == ResourceType.Mana && u.ManaPct < 70f) break;
                    foreach (var t in EnemyTargets(a, u, enemies, focus))
                    {
                        var check = t == u && focus != null ? focus : t;
                        if (t.IsTotem || AlreadyHasAny(a, check, u) || Pointless(b, u, a, check)) continue;
                        if (t.HealthPct < 20f && t.Rank != CreatureRank.Boss) continue;
                        float s = (basePri + 2.5f) * (t == focus ? 1.3f : 1f) * castPenalty;
                        if (role == UnitRole.Healer) s *= 0.4f;
                        s *= ThreatFactor(b, u, t, tank, role);
                        yield return new Candidate { Ability = a, Target = t, Score = s, Why = "debuff" };
                    }
                    break;
                }
                case "Finisher":
                {
                    if (focus == null) break;
                    int cp = b.ComboPointsOn(u, focus);
                    float s = 0f;
                    if (cp >= 5) s = basePri + 7f;
                    else if (cp >= 3 && focus.HealthPct < 30f) s = basePri + 4f;
                    else if (cp >= 1 && focus.HealthPct < 12f) s = basePri + 3f;
                    var auras = AppliedAuras(a);
                    if (auras.Count > 0 && a.target == TargetType.Self && u.HasAura(auras[0])) s = 0f; // Slice and Dice up
                    if (auras.Count > 0 && a.target == TargetType.Self && cp >= 2 && !u.HasAura(auras[0])) s = Math.Max(s, basePri + 5f);
                    if (s > 0) yield return new Candidate { Ability = a, Target = a.target == TargetType.Self ? u : focus, Score = s * ThreatFactor(b, u, focus, tank, role), Why = $"finisher {cp}cp" };
                    break;
                }
                case "Opener":
                    if (focus != null && u.IsStealthed) yield return new Candidate { Ability = a, Target = focus, Score = basePri + 9f, Why = "opener" };
                    break;
                case "Interrupt":
                    foreach (var t in enemies)
                        if (t.Pending != null && !t.IsTotem) yield return new Candidate { Ability = a, Target = t, Score = 25f, Why = "interrupt " + t.Pending.Ability.name };
                    break;
                case "Taunt":
                    if (role != UnitRole.Tank && !(u.Kind == UnitKind.Pet && u.Role == UnitRole.Tank)) break;
                    foreach (var t in enemies)
                        if (!t.IsTotem && t.AggroTarget != null && t.AggroTarget != u && t.AggroTarget.Team == u.Team && t.AggroTarget.Role != UnitRole.Tank)
                            yield return new Candidate { Ability = a, Target = a.target == TargetType.Self ? u : t, Score = 16f, Why = "taunt " + t.Name };
                    break;
                case "Defensive":
                {
                    float hp = u.HealthPct;
                    bool danger = role == UnitRole.Tank ? hp < 30f : hp < 40f && u.AggroTargetedBy(b) > 0;
                    if (danger) yield return new Candidate { Ability = a, Target = a.target == TargetType.Self ? u : focus, Score = 18f, Why = "defensive" };
                    break;
                }
                case "Summon":
                {
                    bool pet = false;
                    foreach (var e in a.effects) if (e.type == EffectType.Summon) { var cr = b.Db.Creature(e.summon); pet = cr != null && cr.rank == CreatureRank.Pet && e.lifetime < 0; }
                    if (pet && u.Pet != null && !u.Pet.Dead) break;
                    yield return new Candidate { Ability = a, Target = a.target == TargetType.Enemy ? focus : u, Point = focus?.Position, Score = pet ? 17f : basePri + 2f, Why = "summon" };
                    break;
                }
                case "CC":
                {
                    if (role == UnitRole.Tank || enemies.Count < 3) break;
                    foreach (var t in enemies)
                    {
                        if (t == focus || t.IsTotem || t.IsControlled || t.Rank == CreatureRank.Boss || t.HealthPct < 50f) continue;
                        if (tank != null && tank.AttackTarget == t) continue;
                        yield return new Candidate { Ability = a, Target = t, Score = basePri * 0.8f, Why = "cc " + t.Name };
                        break;
                    }
                    break;
                }
                case "AoE":
                {
                    if (manaLow && role == UnitRole.Healer) break;
                    Unit aim = focus;
                    int bestCount = 0; Unit bestT = focus;
                    foreach (var t in enemies)
                    {
                        var hit = Targeting.AreaUnits(b, u, a, a.target == TargetType.Self ? u : t, t.Position, mods);
                        int count = 0; bool hitsCc = false;
                        foreach (var h in hit) if (h.IsHostileTo(u)) { count++; if (h.HasStateAura(UnitState.Polymorph) || h.HasStateAura(UnitState.Incapacitate) || h.HasStateAura(UnitState.Sleep)) hitsCc = true; }
                        if (hitsCc) continue;
                        if (count > bestCount) { bestCount = count; bestT = t; }
                        if (a.target == TargetType.Self) break;
                    }
                    if (bestCount < 2) break;
                    float s = (basePri + 1.5f * bestCount) * castPenalty * (bestCount >= 3 ? 1.3f : 0.8f);
                    if (role == UnitRole.Healer) s *= 0.3f;
                    s *= ThreatFactor(b, u, bestT, tank, role);
                    yield return new Candidate { Ability = a, Target = a.target == TargetType.Self ? u : bestT, Point = bestT?.Position, Score = s, Why = $"aoe x{bestCount}" };
                    break;
                }
                case "Utility":
                {
                    // mana from health (Life Tap style) or resource tools
                    bool gainsMana = false;
                    foreach (var e in a.effects)
                        if ((e.type == EffectType.GainResource && e.resource == "Mana") || e.special == "WarlockLifeTap") gainsMana = true;
                    if (gainsMana && u.MaxMana > 0 && u.ManaPct < 40f && u.HealthPct > 60f)
                        yield return new Candidate { Ability = a, Target = a.target == TargetType.Self ? u : focus, Score = basePri + 4f, Why = "mana" };
                    break;
                }
                default: // Damage
                {
                    if (a.nextSwing)
                    {
                        if (u.PowerType == ResourceType.Rage && u.Rage >= 50f && focus != null && b.InMeleeRange(u, focus))
                            yield return new Candidate { Ability = a, Target = focus, Score = basePri + 1f, Why = "rage dump" };
                        break;
                    }
                    // saving the last quarter of mana only pays with a free attack to fall back on (wand, melee swings,
                    // Auto Shot): a ranged caster without one keeps casting down to the mana-potion step instead of idling
                    if (manaLow && a.cost.type == ResourceType.Mana && a.special != "Shoot" && HasFreeAttack(u, role)) break;
                    if (role == UnitRole.Healer && a.cost.type == ResourceType.Mana && u.ManaPct < 70f) break;
                    foreach (var t in EnemyTargets(a, u, enemies, focus))
                    {
                        if (t.IsTotem && enemies.Count > 1) continue;
                        if (t.HasStateAura(UnitState.Polymorph) || t.HasStateAura(UnitState.Incapacitate) || t.HasStateAura(UnitState.Sleep)) continue;
                        if (t != u && Pointless(b, u, a, t)) continue;
                        if (t != u && !HasDirectEffect(a) && AlreadyHasAny(a, t, u)) continue;
                        float s = basePri * (t == focus ? 1.3f : 1f) * castPenalty;
                        // a nuke with a DoT rider (Immolate) is not recast while its own DoT is still fresh: other nukes first
                        if (t != u && HasDirectEffect(a) && OwnDotFresh(b, a, t, u)) s *= 0.4f;
                        if (a.generatesComboPoint) s = b.ComboPointsOn(u, t) >= 5 ? s * 0.2f : s + 2f;
                        if (a.special == "Shoot") s = 1.2f;
                        if (role == UnitRole.Tank && a.effects.Exists(e => e.threat > 0)) s += 2f;
                        s *= ThreatFactor(b, u, t, tank, role);
                        yield return new Candidate { Ability = a, Target = a.target == TargetType.Self ? u : t, Point = a.target == TargetType.Point ? t.Position : (Vec2?)null, Score = s, Why = "damage" };
                    }
                    break;
                }
            }
        }

        /// <summary>The unit has an attack that costs no mana to fall back on when it saves its mana: melee swings (tanks,
        /// melee, healers who stand back anyway), a wand it can Shoot, or a ranged weapon for Auto Shot.</summary>
        static bool HasFreeAttack(Unit u, UnitRole role)
        {
            if (role != UnitRole.RangedDps) return true;
            if (u.Equipment.HasWand && u.Knows("shoot")) return true;
            return u.Equipment.HasRangedWeapon && !u.Equipment.HasWand && u.Knows("auto_shot");
        }

        /// <summary>The ability would do nothing useful on the target (drain mana of a manaless unit, nothing to dispel,
        /// own curse/sting of the same family already running, aura-only ability already applied).</summary>
        static bool Pointless(Battle b, Unit u, AbilityDef a, Unit t)
        {
            if (t == null) return false;
            bool hostile = t.IsHostileTo(u);
            bool onlyDispel = a.effects.Count > 0;
            bool anyDispellable = false;
            foreach (var e in a.effects)
            {
                if (e.type == EffectType.Dispel)
                {
                    foreach (var x in t.Auras)
                        if (!x.IsPassive && x.Def.dispel != DispelType.None && (hostile ? x.Def.kind == AuraKind.Buff : x.Def.kind == AuraKind.Debuff)
                            && (e.dispelType == DispelType.None || x.Def.dispel == e.dispelType)) { anyDispellable = true; break; }
                }
                else if (e.type != EffectType.Threat) onlyDispel = false;
                bool drainsMana = (e.type == EffectType.DrainResource && e.resource == "Mana") || e.special == "PriestManaBurn";
                if (drainsMana && hostile && t.MaxMana <= 0f) return true;
            }
            if (onlyDispel && !anyDispellable) return true;
            foreach (var id in AppliedAuras(a))
            {
                var def = b.Db.Aura(id);
                if (def == null) continue;
                foreach (var e in def.tickEffects)
                    if (e.type == EffectType.DrainResource && e.resource == "Mana" && hostile && t.MaxMana <= 0f) return true;
                if (hostile && !string.IsNullOrEmpty(def.exclusiveGroup) && def.exclusivePerCaster && !HasDirectEffect(a))
                    foreach (var x in t.Auras)
                        if (x.Caster == u && x.Def.exclusiveGroup == def.exclusiveGroup && x.Def.id != def.id && x.Remaining > 3f) return true;
                if (hostile && SpecialUtilIsSnare(def) && (t.Creature != null && t.Class == null && t.Creature.moveSpeed <= 0f)) return true;
            }
            return false;
        }

        static bool SpecialUtilIsSnare(AuraDef d)
        {
            if (Array.IndexOf(d.states, UnitState.Root) >= 0) return true;
            foreach (var m in d.mods) if (m.stat == StatId.MoveSpeed && m.value < 0) return true;
            return false;
        }

        /// <summary>A pending enemy cast worth stopping: telegraphed, long (≥ 2.5 s) or a heal.</summary>
        internal static bool IsBigCast(PendingCast p) =>
            p != null && p.Ability != null && (Battle.IsTelegraphed(p.Ability) || p.TotalTime >= 2.5f || p.Ability.castTime >= 2.5f || p.Ability.aiHint == "Heal");

        /// <summary>Using the ability on the caster would cancel its pending cast: an Interrupt effect, or an aura whose state
        /// cancels casts (stun, fear, polymorph, incapacitate, sleep, banish, confuse; silence for spells) the target is not immune to.</summary>
        internal static bool CancelsCast(Battle b, AbilityDef a, Unit t)
        {
            if (t == null || t.Pending == null || t.IsInvulnerable) return false;
            foreach (var e in a.effects)
            {
                if (e.chance < 100f) continue;
                if (e.type == EffectType.Interrupt && (e.target == EffectTarget.Target || e.target == EffectTarget.Area)) return true;
                if (e.type != EffectType.ApplyAura || e.target != EffectTarget.Target) continue;
                var def = b.Db.Aura(e.aura);
                if (def == null) continue;
                foreach (var s in def.states)
                {
                    bool cancels = s == UnitState.Stun || s == UnitState.Fear || s == UnitState.Polymorph || s == UnitState.Incapacitate
                                   || s == UnitState.Sleep || s == UnitState.Banish || s == UnitState.Confuse
                                   || (s == UnitState.Silence && AbilityRules.IsSpell(t.Pending.Ability));
                    if (!cancels) continue;
                    bool immune = t.Creature != null && t.Class == null && Array.IndexOf(t.Creature.immune, s) >= 0;
                    if (!immune) return true;
                }
            }
            return false;
        }

        static bool HasDirectEffect(AbilityDef a)
        {
            foreach (var e in a.effects)
                if (e.type == EffectType.Damage || e.type == EffectType.WeaponDamage || e.type == EffectType.Special || e.type == EffectType.DrainResource) return true;
            return false;
        }

        /// <summary>The caster's periodic aura from this ability is on the target with more than half its duration left.</summary>
        static bool OwnDotFresh(Battle b, AbilityDef a, Unit t, Unit u)
        {
            foreach (var id in AppliedAuras(a))
            {
                var def = b.Db.Aura(id);
                if (def == null || def.tickInterval <= 0 || def.kind != AuraKind.Debuff) continue;
                var x = t.FindAura(id, u);
                if (x != null && !x.IsPermanent && x.Duration > 0 && x.Remaining > x.Duration * 0.5f) return true;
            }
            return false;
        }

        /// <summary>Most of its mana pool a buff that outlasts the fight may cost for the AI to cast it in combat.</summary>
        public const float CombatBuffManaShare = 0.10f;

        /// <summary>
        /// A buff that outlasts the fight (a minute or more, no charges to spend: Fortitude, Arcane Intellect, their party-wide
        /// versions) whose mana cost is over <see cref="CombatBuffManaShare"/> of the caster's pool: put up between fights, not
        /// in one — ranks grow faster than the pool (Fortitude rank 3 is ~23% of a level-30 priest's mana), so casting them at
        /// the start of a fight left the healer dry by round 3. Cheaper ones (Blessings, Shadow Protection, low-rank
        /// Arcane Intellect) and charge-based shields (Inner Fire, Lightning Shield) still go up in combat.
        /// </summary>
        static bool TooCostlyForCombat(Battle b, Unit u, AbilityDef a, AuraDef def, AbilityModSet mods)
        {
            if (!b.InCombat || def.duration < 60f || def.charges > 0) return false;
            if (a.cost == null || a.cost.type != ResourceType.Mana || u.MaxMana <= 0f) return false;
            return AbilityRules.ResourceCost(u, a, AbilityRules.UsedRank(u, a), mods) > CombatBuffManaShare * u.MaxMana;
        }

        /// <summary>An ally-targeted buff of an exclusive group the AI keeps on the caster, like a stance: Dampen/Amplify
        /// Magic change the healing taken (and Amplify the spell damage taken) of whoever wears them. Every other
        /// ally-targeted grouped buff (Fortitude, Arcane Intellect, Blessings) goes to the party member that lacks it.</summary>
        static bool KeptOnCaster(AuraDef def) => def.special == "MageFlatMagicModifier";

        /// <summary>A single-target buff is worth giving the ally: mana buffs (Intellect, Wisdom's mana regeneration,
        /// Spirit) only to units with mana, attack power (Might) only to units that fight with it, threat reduction
        /// (Salvation) never to a tank.</summary>
        static bool BuffSuits(AuraDef def, Unit t)
        {
            foreach (var m in def.mods)
            {
                float v = m.values != null && m.values.Length > 0 ? m.values[0] : m.value;
                switch (m.stat)
                {
                    case StatId.Intellect: case StatId.ManaRegen: case StatId.Spirit:
                        if (v > 0 && t.MaxMana <= 0f) return false;
                        break;
                    case StatId.AttackPower: case StatId.RangedAttackPower:
                        if (v > 0 && !UsesAttackPower(t)) return false;
                        break;
                    case StatId.ThreatGenerated:
                        if (v < 0 && t.Role == UnitRole.Tank) return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>The unit's damage scales with attack power: tanks and melee (characters and pets) and hunters.</summary>
        static bool UsesAttackPower(Unit t) =>
            t.Role == UnitRole.Tank || t.Role == UnitRole.MeleeDps || t.ClassId == ClassId.Hunter;

        static bool KnowsPreferred(Battle b, Unit u, string group, string pref)
        {
            foreach (var kv in u.Abilities)
            {
                var a = b.Db.Ability(kv.Key);
                if (a == null) continue;
                foreach (var id in AppliedAuras(a))
                {
                    var d = b.Db.Aura(id);
                    if (d != null && d.exclusiveGroup == group && MatchesPreference(d.id, pref)) return true;
                }
            }
            return false;
        }

        static IEnumerable<Unit> TargetsFor(AbilityDef a, Unit u, List<Unit> allies)
        {
            switch (a.target)
            {
                case TargetType.Self: yield return u; break;
                case TargetType.Pet: if (u.Pet != null && u.Pet.IsAlive) yield return u.Pet; break;
                case TargetType.AllyOther: foreach (var x in allies) if (x != u) yield return x; break;
                default: foreach (var x in allies) yield return x; break;
            }
        }

        static IEnumerable<Unit> EnemyTargets(AbilityDef a, Unit u, List<Unit> enemies, Unit focus)
        {
            if (a.target == TargetType.Self || a.target == TargetType.Ally || a.target == TargetType.AllyOther) { yield return u; yield break; }
            if (focus != null) yield return focus;
            foreach (var e in enemies) if (e != focus) yield return e;
        }

        /// <summary>DPS avoid pulling threat off the tank: abilities on a target where we are close to the tank's threat score low.</summary>
        static float ThreatFactor(Battle b, Unit u, Unit t, Unit tank, UnitRole role)
        {
            if (t == null || role == UnitRole.Tank || tank == null || !tank.IsAlive || u.Kind == UnitKind.Pet) return 1f;
            float tankThreat = b.ThreatOf(t, tank);
            float mine = b.ThreatOf(t, u);
            if (tankThreat <= 0f) return b.Round <= 1 && u.ClassId != ClassId.Hunter ? 0.6f : 1f;
            if (mine > tankThreat * 0.9f) return 0.05f;
            if (mine > tankThreat * 0.75f) return 0.5f;
            return 1f;
        }

        static AIStep AutoAttackChoice(Battle b, Unit u, Unit focus, UnitRole role)
        {
            if (focus == null || SoftControlled(focus)) return null; // a swing would break the Sap/Gouge/Polymorph
            AbilityDef auto = null;
            if (u.ClassId == ClassId.Hunter && u.Knows("auto_shot") && u.Equipment.HasRangedWeapon && !b.InMeleeRange(u, focus)) auto = b.Db.Ability("auto_shot");
            else if (role == UnitRole.Tank || role == UnitRole.MeleeDps || (u.MaxMana <= 0)) auto = b.Db.Ability(u.Class?.basicAttack == "auto_shot" ? "attack" : u.Class?.basicAttack ?? "attack");
            if (auto == null) return null;
            if (u.AutoAttacking && u.AttackTarget == focus && u.AutoAttackAbility == auto.id) return null;
            if (u.AIMemory.UsedCount(auto.id) > 1) return null;
            var chk = b.CanUse(u, auto, focus);
            if (chk.Ok) return Use(u, auto, focus, null, "auto attack");
            return null;
        }

        static AIStep Position(Battle b, Unit u, Unit focus, List<Unit> enemies, UnitRole role)
        {
            if (focus == null || b.CannotMoveReason(u) != null || u.AIMemory.Moves >= 2) return null;
            var near = b.NearestHostile(u);
            if (u.ClassId == ClassId.Hunter)
            {
                float dead = MathUtil.Yd(8f) + 0.4f;
                if (near != null && u.DistanceTo(near) < dead + near.Radius)
                    return MoveAway(b, u, near, dead + 2.5f, focus, MathUtil.Yd(35f), "out of the dead zone");
                if (u.DistanceTo(focus) > MathUtil.Yd(35f)) return MoveToRange(b, u, focus, MathUtil.Yd(33f), "into shooting range");
                return null;
            }
            if (role == UnitRole.Tank || role == UnitRole.MeleeDps)
            {
                if (!b.InMeleeRange(u, focus)) return MoveToRange(b, u, focus, b.MeleeReachOf(u, focus), "closing in", u.ClassId == ClassId.Rogue);
                return null;
            }
            // casters / healers: keep out of melee, stay within 30 yd of the fight
            if (near != null && b.InMeleeRange(u, near) && near.AggroTarget == u && u.AIMemory.Moves == 0)
                return MoveAway(b, u, near, 6f, focus, MathUtil.Yd(28f), "backing off");
            if (u.DistanceTo(focus) > MathUtil.Yd(30f)) return MoveToRange(b, u, focus, MathUtil.Yd(27f), "into casting range");
            return null;
        }
    }

    public sealed partial class Unit
    {
        /// <summary>How many living enemies currently target this unit (AI aggro).</summary>
        public int AggroTargetedBy(Battle b)
        {
            int n = 0;
            foreach (var e in b.Units) if (e.IsAlive && e.AggroTarget == this && e.IsHostileTo(this)) n++;
            return n;
        }
    }
}
