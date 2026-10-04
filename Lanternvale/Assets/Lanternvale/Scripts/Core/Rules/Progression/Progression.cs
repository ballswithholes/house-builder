// Experience, levelling, talents (tiers, prerequisites, respec, auto-allocation) and class trainers.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed class LevelUpInfo
    {
        public Unit Unit;
        public int OldLevel, NewLevel;
        public float HealthGained, ManaGained;
        public int TalentPointsGained;
        /// <summary>Abilities/ranks that became trainable at the new level(s).</summary>
        public readonly List<TrainerOffer> NewTrainable = new List<TrainerOffer>();
        public override string ToString() => $"{Unit.Name} reached level {NewLevel}";
    }

    public sealed class TrainerOffer
    {
        public AbilityDef Ability;
        public int Rank;
        public int Level;
        public int Cost;
        public bool CanTrain;
        public string Reason = "";
        public bool IsNewAbility => Rank == 1;
        public override string ToString() => $"{Ability.name} (Rank {Rank}) L{Level} {Inventory.FormatMoney(Cost)}{(CanTrain ? "" : " - " + Reason)}";
    }

    public static class Progression
    {
        // ================================================================= XP

        public static int XpToNextLevel(GameDatabase db, int level)
        {
            var table = db.Config.xpToLevel != null && db.Config.xpToLevel.Length > 0 ? db.Config.xpToLevel : GameDatabase.DefaultXpTable;
            if (level < 1) return table[0];
            if (level - 1 >= table.Length) return int.MaxValue;
            return table[level - 1];
        }

        /// <summary>Rank multiplier for kill XP (elites ×2, bosses ×3, minions ×0.25; pets/totems/critters give none).</summary>
        public static float XpRankMult(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: return 2f;
                case CreatureRank.Rare: return 2f;
                case CreatureRank.Boss: return 3f;
                case CreatureRank.Minion: return 0.25f;
                case CreatureRank.Pet: case CreatureRank.Totem: case CreatureRank.Critter: return 0f;
                default: return 1f;
            }
        }

        /// <summary>XP a character of <paramref name="charLevel"/> earns for killing <paramref name="mob"/> (rate applied).
        /// Every party member earns the full amount (BG3 style, no splitting).</summary>
        public static int KillXp(GameDatabase db, int charLevel, Unit mob)
        {
            if (mob == null || mob.Creature == null || mob.Kind != UnitKind.Creature) return 0;
            if (charLevel >= db.Config.maxLevel) return 0;
            float xp = Formulas.MobXp(charLevel, mob.Level) * XpRankMult(mob.Creature.rank) * mob.Creature.xpMult * db.Config.xpRate;
            return Math.Max(0, (int)Math.Round(xp));
        }

        /// <summary>Quest XP (data amount × xpRate).</summary>
        public static int QuestXp(GameDatabase db, int amount) => Math.Max(0, (int)Math.Round(amount * db.Config.xpRate));

        /// <summary>Adds XP, levelling up as needed. Returns one entry per level gained.</summary>
        public static List<LevelUpInfo> GiveXp(Unit u, int amount)
        {
            var ups = new List<LevelUpInfo>();
            if (u == null || amount <= 0 || !u.IsCharacter) return ups;
            var db = u.Db;
            int max = Math.Max(1, db.Config.maxLevel);
            if (u.Level >= max) return ups;
            u.Xp += amount;
            while (u.Level < max)
            {
                int need = XpToNextLevel(db, u.Level);
                if (u.Xp < need) break;
                u.Xp -= need;
                ups.Add(SetLevel(u, u.Level + 1));
            }
            if (u.Level >= max) u.Xp = 0;
            return ups;
        }

        /// <summary>Sets the level (stats recomputed, health/mana raised by the max increase, pet follows).</summary>
        public static LevelUpInfo SetLevel(Unit u, int level)
        {
            var info = new LevelUpInfo { Unit = u, OldLevel = u.Level };
            float oldHp = u.MaxHealth, oldMana = u.MaxMana;
            int oldPoints = TalentPointsTotal(u.Level);
            u.Level = MathUtil.Clamp(level, 1, Math.Max(1, u.Db.Config.maxLevel));
            u.InvalidateStats();
            info.NewLevel = u.Level;
            info.HealthGained = u.MaxHealth - oldHp;
            info.ManaGained = u.MaxMana - oldMana;
            if (u.IsAlive)
            {
                u.Health = Math.Min(u.MaxHealth, u.Health + Math.Max(0f, info.HealthGained));
                u.Mana = Math.Min(u.MaxMana, u.Mana + Math.Max(0f, info.ManaGained));
            }
            info.TalentPointsGained = TalentPointsTotal(u.Level) - oldPoints;
            if (u.Pet != null)
            {
                u.Pet.Level = u.Level;
                u.Pet.InvalidateStats();
                if (u.Pet.IsPersistentPet) UnitFactory.LearnCreatureAbilities(u.Pet);   // abilities its new level unlocks
            }
            foreach (var o in TrainerOffers(u, null))
                if (o.Level > info.OldLevel && o.Level <= info.NewLevel) info.NewTrainable.Add(o);
            UnitFactory.AttachPassives(u);
            return info;
        }

        // ============================================================= talents

        public static int TalentPointsTotal(int level) => Math.Max(0, level - 9);

        public static int TalentPointsSpent(Unit u)
        {
            int n = 0;
            foreach (var kv in u.Talents) n += kv.Value;
            return n;
        }

        public static int TalentPointsAvailable(Unit u) => TalentPointsTotal(u.Level) - TalentPointsSpent(u);

        public static int PointsInTree(Unit u, string treeId)
        {
            int n = 0;
            foreach (var kv in u.Talents)
                if (u.Db.TreeOfTalent.TryGetValue(kv.Key, out var t) && t.id == treeId) n += kv.Value;
            return n;
        }

        /// <summary>Why one more point cannot go into the talent, or null.</summary>
        public static string CannotLearnTalent(Unit u, string talentId)
        {
            var db = u.Db;
            var tal = db.Talent(talentId);
            if (tal == null) return "Unknown talent.";
            if (!db.TreeOfTalent.TryGetValue(talentId, out var tree)) return "Unknown talent tree.";
            if (u.Class == null || tree.classId != u.Class.id) return "That talent belongs to another class.";
            if (TalentPointsAvailable(u) <= 0) return u.Level < 10 ? "Talents unlock at level 10." : "No talent points available.";
            int rank = u.TalentRank(talentId);
            if (rank >= tal.maxRank) return "Already at maximum rank.";
            int need = 5 * (tal.tier - 1);
            int have = PointsInTree(u, tree.id);
            if (have < need) return $"Requires {need} points in {tree.name} ({have} spent).";
            if (!string.IsNullOrEmpty(tal.requires))
            {
                var pre = db.Talent(tal.requires);
                if (pre != null && u.TalentRank(pre.id) < pre.maxRank) return $"Requires {pre.maxRank} points in {pre.name}.";
            }
            return null;
        }

        /// <summary>Spends one point in the talent. Grants talent abilities. Returns null on success, else the reason.</summary>
        public static string LearnTalent(Unit u, string talentId)
        {
            var why = CannotLearnTalent(u, talentId);
            if (why != null) return why;
            var tal = u.Db.Talent(talentId);
            u.Talents[talentId] = u.TalentRank(talentId) + 1;
            foreach (var p in tal.effects)
                if (p.type == "GrantAbility")
                {
                    var a = u.Db.Ability(p.ability);
                    if (a != null && !u.Knows(a.id)) u.Abilities[a.id] = 1;
                }
            u.InvalidateStats();
            UnitFactory.AttachPassives(u);
            return null;
        }

        /// <summary>Gold cost of the next respec: 1g, 5g, 10g, 15g … (max 50g).</summary>
        public static int RespecCost(Unit u)
        {
            int n = u.RespecCount;
            if (n <= 0) return 10000;
            if (n == 1) return 50000;
            return Math.Min(500000, 50000 * n);
        }

        /// <summary>Removes all talents (and talent-granted abilities). Does not charge gold (see GameSession.Respec).</summary>
        /// <summary>
        /// Unlearns every talent (and talent-granted abilities). Items the unit can no longer use (e.g. two-handers
        /// granted by a talent) are unequipped into <paramref name="bags"/> when given; they are returned either way.
        /// </summary>
        public static List<ItemInstance> ResetTalents(Unit u, Inventory bags = null)
        {
            var db = u.Db;
            foreach (var kv in u.Talents)
            {
                var tal = db.Talent(kv.Key);
                if (tal == null) continue;
                foreach (var p in tal.effects)
                    if (p.type == "GrantAbility") u.Abilities.Remove(p.ability);
            }
            u.Talents.Clear();
            u.RespecCount++;
            // passive auras of removed abilities
            for (int i = u.Auras.Count - 1; i >= 0; i--)
            {
                var a = u.Auras[i];
                if (a.IsPassive && a.SourceAbility != null && !u.Knows(a.SourceAbility.id)) u.Auras.RemoveAt(i);
            }
            u.InvalidateStats();
            var removed = EquipmentRules.RemoveIllegal(u);
            if (bags != null) foreach (var it in removed) bags.Add(it);
            u.InvalidateStats();
            u.ClampResources();
            return removed;
        }

        /// <summary>Spends all free points following CompanionDef.preferredTalents or ClassDef.defaultBuild (illegal entries are retried later, then skipped).</summary>
        public static int AutoAllocateTalents(Unit u)
        {
            if (u.Class == null) return 0;
            var order = u.Companion != null && u.Companion.preferredTalents.Length > 0 ? u.Companion.preferredTalents : u.Class.defaultBuild;
            if (order == null || order.Length == 0) return 0;
            int spent = 0;
            // count how many times each talent appears so far to respect "one entry per point"
            var wanted = new List<string>(order);
            bool progress = true;
            while (progress && TalentPointsAvailable(u) > 0)
            {
                progress = false;
                var taken = new Dictionary<string, int>();
                for (int i = 0; i < wanted.Count && TalentPointsAvailable(u) > 0; i++)
                {
                    var id = wanted[i];
                    taken.TryGetValue(id, out var k);
                    taken[id] = k + 1;
                    if (u.TalentRank(id) >= taken[id]) continue; // this point already spent
                    if (CannotLearnTalent(u, id) != null) continue;
                    LearnTalent(u, id);
                    spent++;
                    progress = true;
                }
            }
            return spent;
        }

        // ============================================================ trainers

        /// <summary>Copper cost of training a rank: rank 1 = trainCost, later ranks max(trainCost, 4 × rankLevel²).</summary>
        public static int TrainCost(AbilityDef a, int rank)
        {
            if (rank <= 1) return Math.Max(0, a.trainCost);
            int lvl = AbilityRules.RankLevel(a, rank);
            return Math.Max(a.trainCost, 4 * lvl * lvl);
        }

        static bool IsTrainable(Unit u, AbilityDef a)
        {
            if (u.Class == null || a.classId != u.Class.id || a.hidden) return false;
            if (a.fromTalent && !u.Knows(a.id)) return false;
            return true;
        }

        /// <summary>
        /// Ranks the unit can learn from a trainer: every next rank at or below its level (CanTrain) and the next
        /// upcoming ones (CanTrain false, reason "Requires level N"). Gold is checked when an inventory is given.
        /// </summary>
        public static List<TrainerOffer> TrainerOffers(Unit u, Inventory inv, bool includeUpcoming = true)
        {
            var list = new List<TrainerOffer>();
            if (u.Class == null) return list;
            foreach (var a in u.Db.Abilities.Values)
            {
                if (!IsTrainable(u, a)) continue;
                int have = u.RankOf(a.id);
                int ranks = AbilityRules.RankCount(a);
                for (int r = have + 1; r <= ranks; r++)
                {
                    int lvl = AbilityRules.RankLevel(a, r);
                    var o = new TrainerOffer { Ability = a, Rank = r, Level = lvl, Cost = TrainCost(a, r) };
                    if (lvl > u.Level)
                    {
                        o.Reason = $"Requires level {lvl}.";
                        if (includeUpcoming) list.Add(o);
                        break;
                    }
                    o.CanTrain = true;
                    if (inv != null && inv.Gold < o.Cost) { o.CanTrain = false; o.Reason = "Not enough money."; }
                    list.Add(o);
                }
            }
            list.Sort((x, y) => x.Level != y.Level ? x.Level.CompareTo(y.Level) : string.CompareOrdinal(x.Ability.name, y.Ability.name));
            return list;
        }

        /// <summary>Learns a rank from a trainer, paying gold. Lower missing ranks are learned too (and paid).</summary>
        public static string Train(Unit u, AbilityDef a, int rank, Inventory inv)
        {
            if (u == null || a == null) return "Nothing to train.";
            if (!IsTrainable(u, a)) return "You cannot learn that.";
            int have = u.RankOf(a.id);
            if (rank <= have) return "You already know that.";
            if (AbilityRules.RankLevel(a, rank) > u.Level) return $"Requires level {AbilityRules.RankLevel(a, rank)}.";
            int cost = 0;
            for (int r = have + 1; r <= rank; r++) cost += TrainCost(a, r);
            if (inv != null && !inv.SpendGold(cost)) return "Not enough money.";
            u.Abilities[a.id] = rank;
            UnitFactory.AttachPassives(u);
            u.InvalidateStats();
            return null;
        }

        /// <summary>Learns every class ability (all ranks up to the unit's level) and talent abilities for free.</summary>
        public static void LearnAllAvailable(Unit u)
        {
            if (u.Class == null) return;
            foreach (var a in u.Db.Abilities.Values)
            {
                if (a.classId != u.Class.id || a.hidden) continue;
                if (a.fromTalent && !u.Knows(a.id)) continue;
                int r = AbilityRules.MaxRankAtLevel(a, u.Level);
                if (r <= 0) continue;
                if (u.RankOf(a.id) < r) u.Abilities[a.id] = r;
            }
            UnitFactory.AttachPassives(u);
            u.InvalidateStats();
        }
    }
}
