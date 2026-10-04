// Creates units from data: player characters, companions, creatures, pets/demons, totems and summons.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public static class UnitFactory
    {
        /// <summary>
        /// Creates a level-N character of a class. Knows the class starting abilities, basic attacks and help_up;
        /// <paramref name="learnAll"/> also learns every class ability (all ranks) available at the level ("veteran start").
        /// Starting items are equipped when possible; the rest are returned in <paramref name="leftovers"/>.
        /// </summary>
        public static Unit CreateCharacter(GameDatabase db, ClassId classId, string name, int level, bool learnAll = false, List<ItemInstance> leftovers = null)
        {
            var c = db.Class(classId) ?? throw new ArgumentException($"Unknown class {classId}");
            var u = new Unit(db)
            {
                Name = name, Kind = UnitKind.Character, Team = Team.Player, Class = c,
                Level = MathUtil.Clamp(level, 1, Math.Max(1, db.Config.maxLevel)), Sprite = c.sprite, Portrait = c.portrait,
            };
            InitCharacter(u, c, learnAll, c.startingItems, leftovers);
            return u;
        }

        public static Unit CreateCompanion(GameDatabase db, CompanionDef comp, int level, bool learnAll = true, List<ItemInstance> leftovers = null)
        {
            var c = db.Class(comp.classId) ?? throw new ArgumentException($"Companion {comp.id}: unknown class {comp.classId}");
            var u = new Unit(db)
            {
                Name = comp.name, Kind = UnitKind.Companion, Team = Team.Player, Class = c, Companion = comp,
                Level = MathUtil.Clamp(level, 1, Math.Max(1, db.Config.maxLevel)),
                Sprite = string.IsNullOrEmpty(comp.sprite) ? c.sprite : comp.sprite,
                Portrait = string.IsNullOrEmpty(comp.portrait) ? c.portrait : comp.portrait,
            };
            var items = new List<string>(comp.startingItems);
            if (items.Count == 0) items.AddRange(c.startingItems);
            InitCharacter(u, c, learnAll, items.ToArray(), leftovers);
            Progression.AutoAllocateTalents(u);
            if (learnAll) Progression.LearnAllAvailable(u); // talent-granted abilities at their best rank
            AttachPassives(u);
            u.InvalidateStats();
            u.RestoreFull();
            return u;
        }

        static void InitCharacter(Unit u, ClassDef c, bool learnAll, string[] startingItems, List<ItemInstance> leftovers)
        {
            var db = u.Db;
            GrantBasics(u);
            foreach (var id in c.startingAbilities) LearnAtBestRank(u, db.Ability(id));
            if (learnAll) Progression.LearnAllAvailable(u);
            foreach (var id in startingItems)
            {
                var def = db.Item(id);
                if (def == null) continue;
                var inst = new ItemInstance(def);
                var slot = def.equip != EquipType.None ? EquipmentRules.ChooseSlot(u, def) : null;
                if (slot.HasValue && u.Equipment[slot.Value] == null) EquipmentRules.Equip(u, inst, slot.Value);
                else leftovers?.Add(inst);
            }
            if (!string.IsNullOrEmpty(c.startingStance))
            {
                var st = db.Aura(c.startingStance);
                if (st != null) AttachAura(u, st, u, false, 1, u.Level, 1);
            }
            AttachPassives(u);
            u.InvalidateStats();
            u.RestoreFull();
        }

        /// <summary>Basic attacks every character knows (attack, class basic attack, help_up, wand shoot / auto shot by proficiency).</summary>
        public static void GrantBasics(Unit u)
        {
            var db = u.Db;
            void Give(string id) { if (db.Ability(id) != null && !u.Abilities.ContainsKey(id)) u.Abilities[id] = 1; }
            Give("attack");
            Give("help_up");
            if (u.Class != null)
            {
                Give(u.Class.basicAttack);
                foreach (var w in u.Class.weaponTypes)
                {
                    if (w == WeaponType.Wand) Give("shoot");
                    if (w == WeaponType.Bow || w == WeaponType.Gun || w == WeaponType.Crossbow) Give("auto_shot");
                }
            }
        }

        /// <summary>Learns an ability at the highest rank available at the unit's level (rank 1 if none yet).</summary>
        public static void LearnAtBestRank(Unit u, AbilityDef a)
        {
            if (a == null) return;
            int r = Math.Max(1, AbilityRules.MaxRankAtLevel(a, u.Level));
            if (!u.Abilities.TryGetValue(a.id, out var cur) || cur < r) u.Abilities[a.id] = r;
        }

        public static Unit CreateCreature(GameDatabase db, CreatureDef def, int level = 0, Team team = Team.Enemy)
        {
            if (level <= 0) level = def.levelMin;
            var u = new Unit(db)
            {
                Name = def.name, Kind = UnitKind.Creature, Team = team, Creature = def, Level = Math.Max(1, level),
                Sprite = def.sprite, Portrait = def.portrait, Radius = CreatureScaling.Radius(def),
            };
            InitCreature(u);
            return u;
        }

        /// <summary>Pets/demons (kind Pet), totems (Totem) and temporary guardians (Summon) at the owner's level.</summary>
        public static Unit CreateSummon(GameDatabase db, CreatureDef def, Unit owner, UnitKind kind, float lifetime)
        {
            var u = new Unit(db)
            {
                Name = def.name, Kind = kind, Team = owner.Team, Creature = def, Level = Math.Max(1, owner.Level),
                Sprite = def.sprite, Portrait = def.portrait, Radius = kind == UnitKind.Totem ? 0.3f : CreatureScaling.Radius(def),
                Owner = owner, Lifetime = lifetime > 0 ? lifetime : -1f, AutoPlay = owner.AutoPlay,
            };
            if (kind == UnitKind.Totem) u.TotemElement = def.totemElement;
            InitCreature(u);
            return u;
        }

        static void InitCreature(Unit u)
        {
            var def = u.Creature;
            u.Abilities["attack"] = 1;
            if (def.ranged && u.Db.Ability("auto_shot") != null) u.Abilities["auto_shot"] = 1;
            bool owned = u.Kind == UnitKind.Pet || u.Kind == UnitKind.Summon || u.Kind == UnitKind.Totem;
            foreach (var ca in def.abilities)
            {
                var a = u.Db.Ability(ca.ability);
                if (a == null) continue;
                if (owned && a.learnLevel > u.Level) continue; // pets/demons/totems learn abilities with their level
                u.Abilities[ca.ability] = 1;
            }
            AttachPassives(u);
            u.InvalidateStats();
            u.RestoreFull();
            if (def.resource == ResourceType.Rage) u.Rage = 0f;
        }

        /// <summary>Creature passives and passive abilities' auras, attached directly (no events).</summary>
        public static void AttachPassives(Unit u)
        {
            var db = u.Db;
            if (u.Creature != null && u.Class == null)
                foreach (var id in u.Creature.passives)
                {
                    var def = db.Aura(id);
                    if (def != null && !u.HasAura(id)) AttachAura(u, def, u, true, 1, u.Level, 1);
                }
            foreach (var kv in new List<KeyValuePair<string, int>>(u.Abilities))
            {
                var a = db.Ability(kv.Key);
                if (a == null || !a.passive || string.IsNullOrEmpty(a.passiveAura)) continue;
                var def = db.Aura(a.passiveAura);
                if (def == null) continue;
                var existing = u.FindAura(def.id);
                if (existing != null) { existing.Rank = kv.Value; existing.EffLevel = AbilityRules.EffLevel(u, a, kv.Value); RefreshModValues(existing); continue; }
                var inst = AttachAura(u, def, u, true, kv.Value, AbilityRules.EffLevel(u, a, kv.Value), a.learnLevel);
                inst.SourceAbility = a;
            }
        }

        /// <summary>Adds an aura instance without events or onApply effects (passives, starting stance, save loading).</summary>
        public static AuraInstance AttachAura(Unit u, AuraDef def, Unit caster, bool passive, int rank, int effLevel, int learnLevel, float remaining = -1f)
        {
            var inst = new AuraInstance
            {
                Def = def, Caster = caster, Bearer = u, IsPassive = passive, Rank = Math.Max(1, rank), EffLevel = effLevel, LearnLevel = learnLevel,
                Duration = passive ? 0f : (remaining > 0 ? Math.Max(def.duration, remaining) : def.duration),
                Remaining = remaining > 0 ? remaining : def.duration, Charges = def.charges, ProcCooldowns = new float[def.procs.Count],
            };
            RefreshModValues(inst);
            if (def.absorb != null) inst.AbsorbLeft = def.absorb.amount + def.absorb.perLevel * Math.Max(0, effLevel - learnLevel);
            u.Auras.Add(inst);
            u.InvalidateStats();
            return inst;
        }

        /// <summary>
        /// Recomputes an aura instance's stat mod values from its def, Rank, EffLevel, LearnLevel and EffectMult, then lets
        /// specials re-apply their scaling (Master Demonologist × talent rank, Expose Armor × combo points...). Use it when
        /// restoring saved auras; call <c>bearer.InvalidateStats()</c> afterwards.
        /// </summary>
        public static void RefreshModValues(AuraInstance a)
        {
            var mods = a.Def.mods;
            if (a.ModValues.Length != mods.Count) a.ModValues = new float[mods.Count];
            for (int i = 0; i < mods.Count; i++)
                a.ModValues[i] = StatCalculator.AuraModValue(mods[i], a.Rank, a.EffLevel, a.LearnLevel, a.EffectMult);
            Specials.OnModValuesRefreshed(a);
        }

        /// <summary>Level used for a creature in an encounter (explicit, scaled to party, or random in its range).</summary>
        public static int CreatureLevel(CreatureDef def, int explicitLevel, int partyLevel, Rng rng)
        {
            if (explicitLevel > 0) return explicitLevel;
            if (def.scaleToParty) return MathUtil.Clamp(partyLevel + def.levelOffset, 1, 63);
            return rng != null ? rng.Range(def.levelMin, Math.Max(def.levelMin, def.levelMax)) : def.levelMin;
        }
    }
}
