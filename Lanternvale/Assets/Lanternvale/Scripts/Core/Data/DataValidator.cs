// Cross-reference validation of the loaded database. Run by the test harness
// (Tools/check.sh) and by the Unity editor menu "Lanternvale/Validate Data".
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lanternvale.Data
{
    public static class DataValidator
    {
        /// <summary>Special handler names implemented by the rules engine. The rules layer registers these at startup.</summary>
        public static Func<string, bool> IsSpecialImplemented = _ => true;

        /// <summary>Flags set by code rather than data (specials). Quest Flag objectives on these need no data setter.</summary>
        public static readonly HashSet<string> CodeSetFlags = new HashSet<string>(StringComparer.Ordinal) { "lanterns_rekindled" };

        // Value sets of string-typed fields (Docs/Expansion.md §2).
        public static readonly string[] TransitionMarkers = { "", "none", "cave", "door", "stairs", "portal" };
        /// <summary>NpcDef.scale range (model size multiplier).</summary>
        public const float NpcScaleMin = 0.3f, NpcScaleMax = 3f;
        public static readonly string[] Biomes = { "", "meadow", "village", "forest", "shrine", "highlands", "fen", "peaks", "cave", "ice_cave", "crypt", "hollow_heart", "roost" };
        public static readonly string[] Environments = { "", "outdoor", "cave", "crypt" };
        public static readonly string[] CreatureMaterials = { "", "plate", "mail", "leather", "cloth", "flesh", "fur", "chitin", "bone", "wood", "stone", "ether", "ice", "scale", "wet" };
        public static readonly string[] CreatureVoices = { "", "beast", "humanoid", "spirit", "wood", "stone", "dragon", "frog", "gnoll", "none" };
        public static readonly string[] PassiveTypes = { "Stat", "AbilityMod", "Proc", "GrantAbility", "Special" };
        public static readonly string[] SetBonusTypes = { "Stat", "AbilityMod", "Proc", "Special" };

        public static List<string> Validate(GameDatabase db)
        {
            var p = new List<string>(db.Problems);
            void Err(string where, string msg) => p.Add($"{where}: {msg}");

            bool HasAbility(string id) => !string.IsNullOrEmpty(id) && db.Abilities.ContainsKey(id);
            bool HasAura(string id) => !string.IsNullOrEmpty(id) && db.Auras.ContainsKey(id);
            bool HasItem(string id) => !string.IsNullOrEmpty(id) && db.Items.ContainsKey(id);
            bool HasCreature(string id) => !string.IsNullOrEmpty(id) && db.Creatures.ContainsKey(id);

            var usedSpecials = new Dictionary<string, string>();
            void UseSpecial(string special, string where)
            {
                if (string.IsNullOrEmpty(special)) return;
                if (!usedSpecials.ContainsKey(special)) usedSpecials[special] = where;
            }

            void CheckEffects(IEnumerable<EffectDef> effects, string where)
            {
                int i = 0;
                foreach (var e in effects)
                {
                    var w = $"{where}.effects[{i++}]({e.type})";
                    switch (e.type)
                    {
                        case EffectType.ApplyAura:
                            if (!HasAura(e.aura)) Err(w, $"unknown aura '{e.aura}'");
                            break;
                        case EffectType.RemoveAura:
                            if (string.IsNullOrEmpty(e.auraTag) && !HasAura(e.aura)) Err(w, $"unknown aura '{e.aura}' (or set auraTag)");
                            break;
                        case EffectType.Summon:
                        case EffectType.SummonTotem:
                            if (!HasCreature(e.summon)) Err(w, $"unknown creature '{e.summon}'");
                            break;
                        case EffectType.CreateItem:
                            if (!HasItem(e.item)) Err(w, $"unknown item '{e.item}'");
                            break;
                        case EffectType.TriggerAbility:
                            if (!HasAbility(e.ability)) Err(w, $"unknown ability '{e.ability}'");
                            break;
                        case EffectType.GainResource:
                        case EffectType.DrainResource:
                            if (!new[] { "Mana", "Rage", "Energy", "Focus", "Health", "ComboPoints" }.Contains(e.resource))
                                Err(w, $"resource must be Mana/Rage/Energy/Focus/Health/ComboPoints, got '{e.resource}'");
                            break;
                        case EffectType.Damage:
                        case EffectType.Heal:
                            if (e.min <= 0 && e.coef <= 0 && e.apCoef <= 0 && e.pctOfMax <= 0 && e.perCombo <= 0 && string.IsNullOrEmpty(e.special))
                                Err(w, "has no magnitude (min/coef/apCoef/pctOfMax/perCombo)");
                            break;
                        case EffectType.Special:
                            if (string.IsNullOrEmpty(e.special)) Err(w, "Special effect without 'special' name");
                            break;
                    }
                    if (!string.IsNullOrEmpty(e.requireTargetAura) && !HasAura(e.requireTargetAura)) Err(w, $"unknown requireTargetAura '{e.requireTargetAura}'");
                    foreach (var a in e.abilities) if (!HasAbility(a)) Err(w, $"unknown ability filter '{a}'");
                    UseSpecial(e.special, w);
                }
            }

            // classes
            foreach (ClassId cid in Enum.GetValues(typeof(ClassId)))
            {
                if (cid == ClassId.None) continue;
                if (!db.Classes.ContainsKey(cid)) { Err("classes", $"missing class {cid}"); continue; }
                var c = db.Classes[cid];
                var w = $"class {cid}";
                if (!HasAbility(c.basicAttack)) Err(w, $"unknown basicAttack '{c.basicAttack}'");
                foreach (var a in c.startingAbilities) if (!HasAbility(a)) Err(w, $"unknown starting ability '{a}'");
                foreach (var i in c.startingItems) if (!HasItem(i)) Err(w, $"unknown starting item '{i}'");
                if (!string.IsNullOrEmpty(c.startingStance) && !HasAura(c.startingStance)) Err(w, $"unknown startingStance '{c.startingStance}'");
                if (c.talentTrees.Length != 3) Err(w, "needs exactly 3 talent trees");
                foreach (var t in c.talentTrees)
                    if (!db.TalentTrees.TryGetValue(t, out var tree)) Err(w, $"unknown talent tree '{t}'");
                    else if (tree.classId != cid) Err(w, $"talent tree '{t}' belongs to {tree.classId}");
                foreach (var t in c.defaultBuild)
                    if (!db.TreeOfTalent.TryGetValue(t, out var bt)) Err(w, $"defaultBuild unknown talent '{t}'");
                    else if (bt.classId != cid) Err(w, $"defaultBuild talent '{t}' belongs to {bt.classId}");
                if (c.defaultBuild.Length > 51) Err(w, "defaultBuild has more than 51 points");
                foreach (var g in c.defaultBuild.GroupBy(x => x))
                    if (db.Talents.TryGetValue(g.Key, out var gt) && g.Count() > gt.maxRank) Err(w, $"defaultBuild puts {g.Count()} points in '{g.Key}' (maxRank {gt.maxRank})");
                if (c.baseHealthLevel60 <= c.baseHealthLevel1) Err(w, "baseHealthLevel60 must exceed baseHealthLevel1");
                if (c.resource == ResourceType.Mana && c.baseManaLevel60 <= 0) Err(w, "mana class needs baseMana values");
                var classAbilities = db.Abilities.Values.Count(a => a.classId == cid && !a.hidden && !a.fromTalent);
                if (classAbilities < 15) Err(w, $"only {classAbilities} trainable abilities (expected a full WoW Classic kit)");
            }

            // abilities
            foreach (var a in db.Abilities.Values)
            {
                var w = $"ability {a.id}";
                if (string.IsNullOrEmpty(a.name)) Err(w, "missing name");
                if (string.IsNullOrEmpty(a.icon)) Err(w, "missing icon");
                if (a.learnLevel < 1 || a.learnLevel > 60) Err(w, "learnLevel must be 1..60");
                for (int i = 0; i < a.rankLevels.Length; i++)
                {
                    if (a.rankLevels[i] < 1 || a.rankLevels[i] > 60) Err(w, "rankLevels must be 1..60");
                    if (i > 0 && a.rankLevels[i] <= a.rankLevels[i - 1]) Err(w, "rankLevels must be ascending");
                }
                if (a.rankLevels.Length > 0 && a.rankLevels[0] != a.learnLevel) Err(w, "rankLevels[0] must equal learnLevel");
                if (a.passive && !string.IsNullOrEmpty(a.passiveAura) && !HasAura(a.passiveAura)) Err(w, $"unknown passiveAura '{a.passiveAura}'");
                if (a.channeled && a.channelTicks <= 0) Err(w, "channeled ability needs channelTicks");
                if (a.tags != null && Array.IndexOf(a.tags, "Uninterruptible") >= 0)
                {
                    if (a.castTime <= 0f && !a.channeled) Err(w, "tag Uninterruptible needs a cast time or a channel");
                    if (a.passive) Err(w, "tag Uninterruptible on a passive ability");
                }
                if (a.rankCastTimes != null && a.rankCastTimes.Length > 0)
                {
                    if (a.rankCastTimes.Length != Math.Max(1, a.rankLevels.Length)) Err(w, "rankCastTimes needs one value per rank");
                    foreach (var t in a.rankCastTimes) if (t < 0f) Err(w, "rankCastTimes must be >= 0");
                }
                if (a.target == TargetType.Point && a.area.shape == AreaShape.None && !a.effects.Any(e => e.type == EffectType.Teleport || e.type == EffectType.SummonTotem || e.type == EffectType.Special))
                    Err(w, "Point target without area");
                if (!a.passive && a.effects.Count == 0 && string.IsNullOrEmpty(a.special)) Err(w, "no effects");
                foreach (var x in a.requires.casterAuras) if (!HasAura(x)) Err(w, $"requires unknown caster aura '{x}'");
                foreach (var x in a.requires.targetAuras) if (!HasAura(x)) Err(w, $"requires unknown target aura '{x}'");
                if (a.cost.type == ResourceType.None && (a.cost.amount > 0 || a.cost.pctBaseMana > 0)) Err(w, "cost amount without cost type");
                CheckEffects(a.effects, w);
                UseSpecial(a.special, w);
            }

            // auras
            foreach (var a in db.Auras.Values)
            {
                var w = $"aura {a.id}";
                if (string.IsNullOrEmpty(a.name)) Err(w, "missing name");
                if (a.tickEffects.Count > 0 && a.tickInterval <= 0) Err(w, "tickEffects need tickInterval > 0");
                if (a.radius > 0 && !HasAura(a.radiusAura)) Err(w, $"area aura needs a valid radiusAura (got '{a.radiusAura}')");
                CheckEffects(a.tickEffects, w + ".tick");
                CheckEffects(a.onApply, w + ".onApply");
                CheckEffects(a.onExpire, w + ".onExpire");
                CheckEffects(a.onRemove, w + ".onRemove");
                for (int i = 0; i < a.procs.Count; i++)
                {
                    CheckEffects(a.procs[i].effects, $"{w}.procs[{i}]");
                    foreach (var x in a.procs[i].abilities) if (!HasAbility(x)) Err(w, $"proc filter unknown ability '{x}'");
                }
                UseSpecial(a.special, w);
            }

            // talents
            foreach (var t in db.TalentTrees.Values)
            {
                var w = $"tree {t.id}";
                var positions = new HashSet<(int, int)>();
                var ids = new HashSet<string>(t.talents.Select(x => x.id));
                if (t.talents.Count < 12) Err(w, $"only {t.talents.Count} talents (WoW Classic trees have ~16-23)");
                foreach (var tal in t.talents)
                {
                    var tw = $"talent {tal.id}";
                    if (tal.tier < 1 || tal.tier > 7) Err(tw, "tier must be 1..7");
                    if (tal.column < 0 || tal.column > 3) Err(tw, "column must be 0..3");
                    if (!positions.Add((tal.tier, tal.column))) Err(tw, $"position tier {tal.tier} column {tal.column} already used in {t.id}");
                    if (tal.maxRank < 1 || tal.maxRank > 5) Err(tw, "maxRank must be 1..5");
                    if (!string.IsNullOrEmpty(tal.requires))
                    {
                        if (!ids.Contains(tal.requires)) Err(tw, $"requires '{tal.requires}' not in the same tree");
                        else if (db.Talents[tal.requires].tier > tal.tier) Err(tw, "prerequisite must be on the same or an earlier tier");
                    }
                    if (tal.effects.Count == 0) Err(tw, "no effects");
                    foreach (var e in tal.effects)
                    {
                        switch (e.type)
                        {
                            case "Stat":
                                break;
                            case "AbilityMod":
                                foreach (var x in e.abilities) if (!HasAbility(x)) Err(tw, $"AbilityMod unknown ability '{x}'");
                                if (e.abilities.Length == 0 && e.tags.Length == 0 && e.schools.Length == 0) Err(tw, "AbilityMod needs abilities, tags or schools filter");
                                break;
                            case "Proc":
                                if (e.proc == null) Err(tw, "Proc passive without proc");
                                else CheckEffects(e.proc.effects, tw + ".proc");
                                break;
                            case "GrantAbility":
                                if (!HasAbility(e.ability)) Err(tw, $"grants unknown ability '{e.ability}'");
                                else if (!db.Abilities[e.ability].fromTalent) Err(tw, $"granted ability '{e.ability}' should have fromTalent: true");
                                break;
                            case "Special":
                                UseSpecial(e.special, tw);
                                break;
                            default:
                                Err(tw, $"unknown passive type '{e.type}' (Stat, AbilityMod, Proc, GrantAbility, Special)");
                                break;
                        }
                        if (e.values.Length > 0 && e.values.Length != tal.maxRank) Err(tw, $"values has {e.values.Length} entries but maxRank is {tal.maxRank}");
                    }
                }
            }

            // items
            foreach (var i in db.Items.Values)
            {
                var w = $"item {i.id}";
                if (!string.IsNullOrEmpty(i.use) && !HasAbility(i.use)) Err(w, $"unknown use ability '{i.use}'");
                if (i.kind == ItemKind.Weapon && (i.maxDamage <= 0 || i.speed <= 0) && i.weaponType != WeaponType.Shield && i.weaponType != WeaponType.HeldInOffhand)
                    Err(w, "weapon needs damage and speed");
                if ((i.kind == ItemKind.Weapon || i.kind == ItemKind.Armor || i.kind == ItemKind.Accessory) && i.equip == EquipType.None)
                    Err(w, "equipable item needs equip slot");
                foreach (var e in i.equipEffects)
                {
                    if (!PassiveTypes.Contains(e.type)) Err(w, $"unknown equipEffects type '{e.type}' ({string.Join(", ", PassiveTypes)})");
                    if (e.type == "Proc" && e.proc != null) CheckEffects(e.proc.effects, w + ".proc");
                    if (e.type == "GrantAbility" && !HasAbility(e.ability)) Err(w, $"grants unknown ability '{e.ability}'");
                    UseSpecial(e.special, w);
                }
                if (i.quality == Quality.Legendary && !i.unique) Err(w, "a Legendary item must be unique");
            }

            // item sets
            bool IsEquipable(ItemDef it) =>
                it != null && (it.kind == ItemKind.Weapon || it.kind == ItemKind.Armor || it.kind == ItemKind.Accessory) && it.equip != EquipType.None;
            var setOfItem = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var set in db.ItemSets.Values)
            {
                var w = $"item set {set.id}";
                if (string.IsNullOrEmpty(set.name)) Err(w, "missing name");
                var items = set.items ?? new string[0];
                int equipable = 0;
                foreach (var id in items.Distinct())
                {
                    if (!HasItem(id)) { Err(w, $"unknown item '{id}'"); continue; }
                    if (!IsEquipable(db.Items[id])) Err(w, $"item '{id}' is not equipable");
                    else equipable++;
                    if (setOfItem.TryGetValue(id, out var other)) Err(w, $"item '{id}' already belongs to set '{other}'");
                    else setOfItem[id] = set.id;
                }
                if (items.Length != items.Distinct().Count()) Err(w, "lists an item twice");
                if (equipable < 2) Err(w, "needs at least 2 equipable items");
                int prev = 0;
                for (int b = 0; b < set.bonuses.Count; b++)
                {
                    var bonus = set.bonuses[b];
                    var bw = $"{w}.bonuses[{b}]";
                    if (bonus.pieces < 1 || bonus.pieces > items.Length) Err(bw, $"pieces {bonus.pieces} must be in [1, {items.Length}]");
                    if (bonus.pieces <= prev) Err(bw, "pieces must be strictly increasing (and unique)");
                    prev = Math.Max(prev, bonus.pieces);
                    foreach (var e in bonus.equipEffects)
                    {
                        if (!SetBonusTypes.Contains(e.type)) Err(bw, $"effect type '{e.type}' is not allowed in a set bonus ({string.Join(", ", SetBonusTypes)})");
                        if (e.type == "Proc")
                        {
                            if (e.proc == null) Err(bw, "Proc effect without proc");
                            else CheckEffects(e.proc.effects, bw + ".proc");
                        }
                        if (e.type == "AbilityMod") foreach (var x in e.abilities) if (!HasAbility(x)) Err(bw, $"AbilityMod unknown ability '{x}'");
                        UseSpecial(e.special, bw);
                    }
                }
            }

            // loot tables
            var collected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in db.Quests.Values)
                if (q?.stages != null)
                    foreach (var st in q.stages)
                        if (st?.objectives != null)
                            foreach (var o in st.objectives)
                                if (o != null && o.type == ObjectiveType.Collect && !string.IsNullOrEmpty(o.target)) collected.Add(o.target);
            foreach (var l in db.LootTables.Values)
            {
                var lw = $"loot {l.id}";
                foreach (var e in l.entries)
                {
                    var pool = e.pool ?? new string[0];
                    var weights = e.weights ?? new float[0];
                    if (pool.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(e.item)) Err(lw, $"entry has both item '{e.item}' and a pool (they are exclusive)");
                        if (e.random) Err(lw, "entry is both random and pooled (they are exclusive)");
                        foreach (var id in pool) if (!HasItem(id)) Err(lw, $"pool: unknown item '{id}'");
                        if (weights.Length > 0 && weights.Length != pool.Length) Err(lw, $"weights has {weights.Length} entries but the pool has {pool.Length}");
                    }
                    else
                    {
                        if (!e.random && !HasItem(e.item)) Err(lw, $"unknown item '{e.item}'");
                        if (weights.Length > 0) Err(lw, "weights without a pool");
                    }
                    if (e.perMembers < 0) Err(lw, "perMembers must be >= 0");
                    if (e.whileQuestNeeds)
                    {
                        if (e.random || pool.Length > 0) Err(lw, "whileQuestNeeds needs a plain item entry (not random or pooled)");
                        else if (!collected.Contains(e.item ?? "")) Err(lw, $"whileQuestNeeds: no quest collects '{e.item}' (it would never drop)");
                    }
                    if (e.random && e.quality >= Quality.Epic) Err(lw, $"random {e.quality} drops are not allowed (author Epic and Legendary items, use a pool)");
                }
            }

            // creatures
            foreach (var c in db.Creatures.Values)
            {
                var w = $"creature {c.id}";
                foreach (var a in c.abilities) if (!HasAbility(a.ability)) Err(w, $"unknown ability '{a.ability}'");
                foreach (var a in c.passives) if (!HasAura(a)) Err(w, $"unknown passive aura '{a}'");
                if (!string.IsNullOrEmpty(c.lootTable) && !db.LootTables.ContainsKey(c.lootTable)) Err(w, $"unknown loot table '{c.lootTable}'");
                if (c.levelMax < c.levelMin) Err(w, "levelMax < levelMin");
                if (c.levelFloor < 0 || c.levelCap < 0) Err(w, "levelFloor/levelCap must be >= 0");
                if (c.levelFloor > 0 && c.levelCap > 0 && c.levelFloor > c.levelCap) Err(w, $"levelFloor {c.levelFloor} > levelCap {c.levelCap}");
                if (!CreatureMaterials.Contains(c.material ?? "")) Err(w, $"unknown material '{c.material}' ({string.Join("|", CreatureMaterials.Skip(1))})");
                if (!CreatureVoices.Contains(c.voice ?? "")) Err(w, $"unknown voice '{c.voice}' ({string.Join("|", CreatureVoices.Skip(1))})");
            }

            // npcs & companions
            foreach (var n in db.Npcs.Values)
            {
                var w = $"npc {n.id}";
                if (!string.IsNullOrEmpty(n.dialogue) && !db.Dialogues.ContainsKey(n.dialogue)) Err(w, $"unknown dialogue '{n.dialogue}'");
                if (!(n.scale >= NpcScaleMin && n.scale <= NpcScaleMax)) Err(w, $"scale {n.scale} outside [{NpcScaleMin}, {NpcScaleMax}]");
                foreach (var v in n.vendor) if (!HasItem(v.item)) Err(w, $"vendor sells unknown item '{v.item}'");
            }
            foreach (var c in db.Companions.Values)
            {
                var w = $"companion {c.id}";
                if (c.classId == ClassId.None) Err(w, "needs classId");
                foreach (var i in c.startingItems) if (!HasItem(i)) Err(w, $"unknown item '{i}'");
                foreach (var t in c.preferredTalents) if (!db.Talents.ContainsKey(t)) Err(w, $"unknown talent '{t}'");
                if (!string.IsNullOrEmpty(c.recruitDialogue) && !db.Dialogues.ContainsKey(c.recruitDialogue)) Err(w, $"unknown dialogue '{c.recruitDialogue}'");
            }

            // dialogue
            foreach (var d in db.Dialogues.Values)
            {
                var w = $"dialogue {d.id}";
                var nodes = new HashSet<string>(d.nodes.Select(n => n.id));
                if (!nodes.Contains(d.start)) Err(w, $"start node '{d.start}' missing");
                foreach (var n in d.nodes)
                {
                    var nw = $"{w}.{n.id}";
                    if (!string.IsNullOrEmpty(n.next) && !nodes.Contains(n.next)) Err(nw, $"next '{n.next}' missing");
                    if (!string.IsNullOrEmpty(n.fallback) && !nodes.Contains(n.fallback)) Err(nw, $"fallback '{n.fallback}' missing");
                    CheckOutcomes(n.outcomes, nw);
                    foreach (var c in n.choices)
                    {
                        if (!string.IsNullOrEmpty(c.next) && !nodes.Contains(c.next)) Err(nw, $"choice '{c.text}' -> missing node '{c.next}'");
                        if (c.check != null)
                        {
                            if (!nodes.Contains(c.check.success)) Err(nw, $"check success node '{c.check.success}' missing");
                            if (!nodes.Contains(c.check.failure)) Err(nw, $"check failure node '{c.check.failure}' missing");
                        }
                        CheckOutcomes(c.outcomes, nw);
                        CheckConditions(c.conditions, nw);
                    }
                    CheckConditions(n.conditions, nw);
                }
            }

            void CheckOutcomes(IEnumerable<OutcomeDef> outs, string where)
            {
                foreach (var o in outs)
                {
                    switch (o.type)
                    {
                        case OutcomeType.StartQuest:
                        case OutcomeType.SetQuestStage:
                        case OutcomeType.CompleteQuest:
                        case OutcomeType.FailQuest:
                            if (!db.Quests.ContainsKey(o.key)) Err(where, $"{o.type}: unknown quest '{o.key}'");
                            else if (o.type == OutcomeType.SetQuestStage && !db.Quests[o.key].stages.Any(s => s.id == o.value))
                                Err(where, $"SetQuestStage: quest '{o.key}' has no stage '{o.value}'");
                            break;
                        case OutcomeType.GiveItem:
                        case OutcomeType.TakeItem:
                            if (!HasItem(o.key)) Err(where, $"{o.type}: unknown item '{o.key}'");
                            break;
                        case OutcomeType.Recruit:
                        case OutcomeType.Dismiss:
                            if (!db.Companions.ContainsKey(o.key)) Err(where, $"{o.type}: unknown companion '{o.key}'");
                            break;
                        case OutcomeType.Teleport:
                            if (!db.Maps.ContainsKey(o.key)) Err(where, $"Teleport: unknown map '{o.key}'");
                            break;
                        case OutcomeType.OpenVendor:
                        case OutcomeType.OpenTrainer:
                            if (!string.IsNullOrEmpty(o.key) && !db.Npcs.ContainsKey(o.key)) Err(where, $"{o.type}: unknown npc '{o.key}'");
                            break;
                        case OutcomeType.Special:
                            UseSpecial(o.key, where);
                            break;
                    }
                }
            }

            void CheckConditions(IEnumerable<ConditionDef> conds, string where)
            {
                foreach (var c in conds)
                {
                    switch (c.type)
                    {
                        case ConditionType.QuestState:
                        case ConditionType.QuestNotStarted:
                        case ConditionType.QuestActive:
                        case ConditionType.QuestComplete:
                            if (!db.Quests.ContainsKey(c.key)) Err(where, $"condition: unknown quest '{c.key}'");
                            break;
                        case ConditionType.HasItem:
                        case ConditionType.NotHasItem:
                            if (!HasItem(c.key)) Err(where, $"condition: unknown item '{c.key}'");
                            break;
                        case ConditionType.Class:
                        case ConditionType.NotClass:
                            if (!Enum.TryParse<ClassId>(c.key, true, out _)) Err(where, $"condition: unknown class '{c.key}'");
                            break;
                        case ConditionType.InParty:
                        case ConditionType.NotInParty:
                        case ConditionType.Companion:
                            if (!db.Companions.ContainsKey(c.key)) Err(where, $"condition: unknown companion '{c.key}'");
                            break;
                    }
                }
            }

            // quest sources: who starts quests and who sets flags (dialogue nodes and choices, quest onComplete, map data)
            var questStarters = new HashSet<string>(StringComparer.Ordinal);
            var settableFlags = new HashSet<string>(CodeSetFlags, StringComparer.Ordinal);
            void IndexOutcomes(IEnumerable<OutcomeDef> outs)
            {
                foreach (var o in outs)
                {
                    if (o.type == OutcomeType.StartQuest || o.type == OutcomeType.SetQuestStage) questStarters.Add(o.key ?? "");
                    if (o.type == OutcomeType.SetFlag && !string.IsNullOrEmpty(o.key)) settableFlags.Add(o.key);
                }
            }
            foreach (var d in db.Dialogues.Values)
                foreach (var n in d.nodes)
                {
                    IndexOutcomes(n.outcomes);
                    foreach (var c in n.choices) IndexOutcomes(c.outcomes);
                }
            foreach (var q in db.Quests.Values)
                foreach (var s in q.stages) IndexOutcomes(s.onComplete);
            foreach (var c in db.Companions.Keys) settableFlags.Add("recruited_" + c);
            foreach (var m in db.Maps.Values)
            {
                foreach (var r in m.regions)
                {
                    if (!string.IsNullOrEmpty(r.enterFlag)) settableFlags.Add(r.enterFlag);
                    if (!string.IsNullOrEmpty(r.checkFlag)) settableFlags.Add(r.checkFlag);
                }
                foreach (var e in m.encounters) settableFlags.Add(!string.IsNullOrEmpty(e.doneFlag) ? e.doneFlag : "enc_" + e.id);
            }
            bool IsPerson(string id) => !string.IsNullOrEmpty(id) && (db.Npcs.ContainsKey(id) || db.Companions.ContainsKey(id));
            bool HasTalkDialogue(string id) =>
                db.Npcs.TryGetValue(id ?? "", out var npc) ? !string.IsNullOrEmpty(npc.dialogue)
                : db.Companions.TryGetValue(id ?? "", out var comp) && !string.IsNullOrEmpty(comp.recruitDialogue);

            // quests
            foreach (var q in db.Quests.Values)
            {
                var w = $"quest {q.id}";
                if (q.stages.Count == 0) Err(w, "no stages");
                if (!IsPerson(q.giver)) Err(w, $"giver '{q.giver}' is not a known npc or companion");
                if (!questStarters.Contains(q.id)) Err(w, "nothing starts it (no StartQuest/SetQuestStage outcome in any dialogue or quest)");
                if (q.minLevel < 0) Err(w, "minLevel must be >= 0");
                if (!string.IsNullOrEmpty(q.zone) && !db.Maps.ContainsKey(q.zone)) Err(w, $"zone '{q.zone}' is not a known map");
                var stageIds = new HashSet<string>(q.stages.Select(s => s.id));
                foreach (var s in q.stages)
                {
                    if (!string.IsNullOrEmpty(s.next) && !stageIds.Contains(s.next)) Err(w, $"stage {s.id} next '{s.next}' missing");
                    foreach (var o in s.objectives)
                    {
                        if (o.type == ObjectiveType.Kill && !HasCreature(o.target)) Err(w, $"kill objective unknown creature '{o.target}'");
                        if (o.type == ObjectiveType.Collect && !HasItem(o.target)) Err(w, $"collect objective unknown item '{o.target}'");
                        if (o.type == ObjectiveType.Talk && !db.Npcs.ContainsKey(o.target) && !db.Companions.ContainsKey(o.target)) Err(w, $"talk objective unknown npc '{o.target}'");
                        else if (o.type == ObjectiveType.Talk && !HasTalkDialogue(o.target)) Err(w, $"stage {s.id}: talk target '{o.target}' has no dialogue (a Talk objective completes when its dialogue ends)");
                        if (o.type == ObjectiveType.Flag && string.IsNullOrEmpty(s.turnIn) && !settableFlags.Contains(o.target ?? ""))
                            Err(w, $"stage {s.id}: nothing sets flag '{o.target}' (no SetFlag, region enterFlag/checkFlag, encounter doneFlag or Recruit) and the stage has no turnIn");
                    }
                    if (!string.IsNullOrEmpty(s.turnIn) && !IsPerson(s.turnIn)) Err(w, $"stage {s.id}: turnIn '{s.turnIn}' is not a known npc or companion");
                    CheckOutcomes(s.onComplete, w);
                }
                foreach (var i in q.rewards.items.Concat(q.rewards.choiceItems)) if (!HasItem(i)) Err(w, $"reward unknown item '{i}'");
            }

            // minLevel > 1: every dialogue StartQuest of the quest is gated by Level >= minLevel (on the choice or its node)
            int LevelGate(IEnumerable<ConditionDef> conds) =>
                conds.Where(c => c.type == ConditionType.Level).Select(c => c.amount).DefaultIfEmpty(0).Max();
            void CheckStartLevel(IEnumerable<OutcomeDef> outs, int gate, string where)
            {
                foreach (var o in outs)
                    if (o.type == OutcomeType.StartQuest && db.Quests.TryGetValue(o.key ?? "", out var sq) && sq.minLevel > 1 && gate < sq.minLevel)
                        Err(where, $"StartQuest {sq.id} needs a Level >= {sq.minLevel} condition (quest minLevel)");
            }
            foreach (var d in db.Dialogues.Values)
                foreach (var n in d.nodes)
                {
                    int nodeGate = LevelGate(n.conditions);
                    CheckStartLevel(n.outcomes, nodeGate, $"dialogue {d.id}.{n.id}");
                    foreach (var c in n.choices)
                        CheckStartLevel(c.outcomes, Math.Max(nodeGate, LevelGate(c.conditions)), $"dialogue {d.id}.{n.id} choice '{c.text}'");
                }

            // maps
            var encounterMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in db.Maps.Values)
            {
                var w = $"map {m.id}";
                if (!Biomes.Contains(m.biome ?? "")) Err(w, $"unknown biome '{m.biome}' ({string.Join("|", Biomes.Skip(1))})");
                if (!Environments.Contains(m.environment ?? "")) Err(w, $"unknown environment '{m.environment}' ({string.Join("|", Environments.Skip(1))})");
                if (m.raidSize < 0 || m.raidSize > db.Config.maxRaidSize) Err(w, $"raidSize {m.raidSize} must be in [0, {db.Config.maxRaidSize}]");
                if (m.raidSize > 0)
                {
                    if (!db.Maps.TryGetValue(m.raidReturnMap ?? "", out var rm)) Err(w, $"raid needs a valid raidReturnMap (got '{m.raidReturnMap}')");
                    else if (!rm.spawns.Any(sp => sp.id == m.raidReturnSpawn)) Err(w, $"raidReturnMap '{m.raidReturnMap}' has no spawn '{m.raidReturnSpawn}'");
                }
                if (m.levelMin < 0 || m.levelMax < 0 || (m.levelMax > 0 && m.levelMax < m.levelMin)) Err(w, $"bad level band {m.levelMin}-{m.levelMax}");
                if (m.fill < 0f || m.fill > 1f) Err(w, "fill must be in [0, 1]");
                for (int i = 0; i < m.paths.Count; i++)
                    if (m.paths[i].points == null || m.paths[i].points.Count < 2) Err(w, $"paths[{i}] needs at least 2 points");
                for (int i = 0; i < m.water.Count; i++)
                {
                    var wd = m.water[i];
                    int need = wd.closed ? 3 : 2;
                    if (wd.points == null || wd.points.Count < need) Err(w, $"water[{i}] needs at least {need} points{(wd.closed ? " (closed)" : "")}");
                }
                foreach (var t in m.transitions)
                {
                    if (t.hidden && string.IsNullOrEmpty(t.revealFlag)) Err(w, $"transition {t.id}: hidden needs a revealFlag");
                    if (!TransitionMarkers.Contains(t.marker ?? "")) Err(w, $"transition {t.id}: unknown marker '{t.marker}' ({string.Join("|", TransitionMarkers.Skip(1))})");
                }
                foreach (var r in m.regions)
                    if (r.check != null && string.IsNullOrEmpty(r.checkFlag)) Err(w, $"region {r.id}: a check needs a checkFlag");
                foreach (var list in new[] { m.props, m.foreground })
                    foreach (var pr in list)
                        if (!string.IsNullOrEmpty(pr.dialogue) && !db.Dialogues.ContainsKey(pr.dialogue)) Err(w, $"prop {pr.art} '{pr.interact}': unknown dialogue '{pr.dialogue}'");
                var ids = new Dictionary<string, string>(StringComparer.Ordinal);
                void UniqueId(string id, string kind)
                {
                    if (string.IsNullOrEmpty(id)) return;
                    if (ids.TryGetValue(id, out var prevKind)) Err(w, $"id '{id}' is used by a {prevKind} and a {kind} (ids are unique per map across interact, chest, transition and region ids)");
                    else ids[id] = kind;
                }
                foreach (var list in new[] { m.props, m.foreground })
                    foreach (var pr in list) UniqueId(pr.interact, "prop");
                foreach (var c in m.chests) UniqueId(c.id, "chest");
                foreach (var t in m.transitions) UniqueId(t.id, "transition");
                foreach (var r in m.regions) UniqueId(r.id, "region");
                foreach (var e in m.encounters)
                {
                    if (string.IsNullOrEmpty(e.id)) continue;
                    if (encounterMap.TryGetValue(e.id, out var other)) Err(w, $"encounter id '{e.id}' is also used on map '{other}' (encounter ids are global)");
                    else encounterMap[e.id] = m.id;
                }
                foreach (var n in m.npcs) if (!db.Npcs.ContainsKey(n.npc) && !db.Companions.ContainsKey(n.npc)) Err(w, $"unknown npc '{n.npc}'");
                foreach (var e in m.encounters)
                {
                    foreach (var en in e.enemies) if (!HasCreature(en.creature)) Err(w, $"encounter {e.id}: unknown creature '{en.creature}'");
                    if (!string.IsNullOrEmpty(e.dialogue) && !db.Dialogues.ContainsKey(e.dialogue)) Err(w, $"encounter {e.id}: unknown dialogue '{e.dialogue}'");
                }
                foreach (var c in m.chests)
                {
                    if (!string.IsNullOrEmpty(c.lootTable) && !db.LootTables.ContainsKey(c.lootTable)) Err(w, $"chest {c.id}: unknown loot table '{c.lootTable}'");
                    foreach (var i in c.items) if (!HasItem(i)) Err(w, $"chest {c.id}: unknown item '{i}'");
                }
                foreach (var t in m.transitions)
                {
                    if (!db.Maps.TryGetValue(t.targetMap, out var tm)) Err(w, $"transition {t.id}: unknown map '{t.targetMap}'");
                    else if (!tm.spawns.Any(s => s.id == t.targetSpawn)) Err(w, $"transition {t.id}: map '{t.targetMap}' has no spawn '{t.targetSpawn}'");
                }
                if (!m.spawns.Any(s => s.id == "default")) Err(w, "needs a spawn with id 'default'");
            }
            if (!db.Maps.ContainsKey(db.Config.startMap)) Err("config", $"startMap '{db.Config.startMap}' missing");

            // specials
            foreach (var kv in usedSpecials)
            {
                if (!db.Specials.ContainsKey(kv.Key)) Err(kv.Value, $"special '{kv.Key}' is not documented in a 'specials' list");
                if (!IsSpecialImplemented(kv.Key)) Err(kv.Value, $"special '{kv.Key}' is not implemented by the rules engine");
            }

            return p;
        }
    }
}
