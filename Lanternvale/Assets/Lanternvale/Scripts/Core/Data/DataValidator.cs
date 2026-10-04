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
                        else if (db.Talents[tal.requires].tier >= tal.tier) Err(tw, "prerequisite must be on an earlier tier");
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
                    if (e.type == "Proc" && e.proc != null) CheckEffects(e.proc.effects, w + ".proc");
                    if (e.type == "GrantAbility" && !HasAbility(e.ability)) Err(w, $"grants unknown ability '{e.ability}'");
                    UseSpecial(e.special, w);
                }
            }
            foreach (var l in db.LootTables.Values)
                foreach (var e in l.entries)
                    if (!e.random && !HasItem(e.item)) Err($"loot {l.id}", $"unknown item '{e.item}'");

            // creatures
            foreach (var c in db.Creatures.Values)
            {
                var w = $"creature {c.id}";
                foreach (var a in c.abilities) if (!HasAbility(a.ability)) Err(w, $"unknown ability '{a.ability}'");
                foreach (var a in c.passives) if (!HasAura(a)) Err(w, $"unknown passive aura '{a}'");
                if (!string.IsNullOrEmpty(c.lootTable) && !db.LootTables.ContainsKey(c.lootTable)) Err(w, $"unknown loot table '{c.lootTable}'");
                if (c.levelMax < c.levelMin) Err(w, "levelMax < levelMin");
            }

            // npcs & companions
            foreach (var n in db.Npcs.Values)
            {
                var w = $"npc {n.id}";
                if (!string.IsNullOrEmpty(n.dialogue) && !db.Dialogues.ContainsKey(n.dialogue)) Err(w, $"unknown dialogue '{n.dialogue}'");
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

            // quests
            foreach (var q in db.Quests.Values)
            {
                var w = $"quest {q.id}";
                if (q.stages.Count == 0) Err(w, "no stages");
                var stageIds = new HashSet<string>(q.stages.Select(s => s.id));
                foreach (var s in q.stages)
                {
                    if (!string.IsNullOrEmpty(s.next) && !stageIds.Contains(s.next)) Err(w, $"stage {s.id} next '{s.next}' missing");
                    foreach (var o in s.objectives)
                    {
                        if (o.type == ObjectiveType.Kill && !HasCreature(o.target)) Err(w, $"kill objective unknown creature '{o.target}'");
                        if (o.type == ObjectiveType.Collect && !HasItem(o.target)) Err(w, $"collect objective unknown item '{o.target}'");
                        if (o.type == ObjectiveType.Talk && !db.Npcs.ContainsKey(o.target) && !db.Companions.ContainsKey(o.target)) Err(w, $"talk objective unknown npc '{o.target}'");
                    }
                    CheckOutcomes(s.onComplete, w);
                }
                foreach (var i in q.rewards.items.Concat(q.rewards.choiceItems)) if (!HasItem(i)) Err(w, $"reward unknown item '{i}'");
            }

            // maps
            foreach (var m in db.Maps.Values)
            {
                var w = $"map {m.id}";
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
