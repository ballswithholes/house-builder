// GameSession: new games for every class at levels 1/10/40/60 (abilities, talents, legal gear, gold, map).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsSessionNewGame
    {
        static readonly ClassId[] Classes =
            { ClassId.Warrior, ClassId.Hunter, ClassId.Paladin, ClassId.Mage, ClassId.Priest, ClassId.Rogue, ClassId.Warlock, ClassId.Shaman };

        static readonly EquipSlot[] ArmorSlots =
            { EquipSlot.Head, EquipSlot.Shoulder, EquipSlot.Chest, EquipSlot.Wrist, EquipSlot.Hands, EquipSlot.Waist, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Back };

        [Test]
        public static void NewGame_EveryClass_Levels_1_10_40_60()
        {
            var db = Harness.Db;
            foreach (var c in Classes)
            {
                foreach (int level in new[] { 1, 10, 40, 60 })
                {
                    var s = SessionTest.NewGame(c, level, seed: (ulong)(100 + level * 10 + (int)c));
                    string tag = $"{c} L{level}";
                    var u = s.Main;
                    Harness.Assert(u != null && u.Level == level, $"{tag}: level");
                    Harness.Assert(s.Mode == SessionMode.Exploration, $"{tag}: exploring (mode {s.Mode})");
                    Harness.Assert(s.MapId == db.Config.startMap, $"{tag}: start map");
                    Harness.Assert(s.Party.Count == 1 && s.Party[0] == u && s.Leader == u, $"{tag}: party");
                    Harness.Assert(s.Nav.IsWalkable(u.Position, 0.3f), $"{tag}: standing on walkable ground {u.Position}");
                    Harness.Assert(s.Field != null && s.Field.Units.Contains(u), $"{tag}: field context");
                    Harness.Assert(Math.Abs(u.Health - u.MaxHealth) < 0.01f && u.MaxHealth > 0, $"{tag}: full health");
                    var cls = db.Class(c);
                    foreach (var a in cls.startingAbilities)
                        Harness.Assert(db.Ability(a) == null || u.Knows(a), $"{tag}: starting ability {a}");
                    Harness.Assert(u.Knows(cls.basicAttack) || cls.basicAttack == "attack", $"{tag}: basic attack");
                    if (!string.IsNullOrEmpty(cls.startingStance) && db.Aura(cls.startingStance) != null)
                        Harness.Assert(u.HasAura(cls.startingStance), $"{tag}: starting stance {cls.startingStance}");

                    // every trainable rank learned (veteran start)
                    if (level > 1)
                    {
                        foreach (var a in db.Abilities.Values)
                        {
                            if (a.classId != c || a.hidden || a.fromTalent) continue;
                            int r = AbilityRules.MaxRankAtLevel(a, level);
                            if (r <= 0) continue;
                            Harness.Assert(u.RankOf(a.id) >= r, $"{tag}: {a.id} rank {u.RankOf(a.id)} < {r}");
                        }
                        Harness.Assert(s.Gold >= db.Config.startingGold + StartingGear.VeteranGold(level), $"{tag}: veteran gold");
                    }
                    else Harness.Assert(s.Gold == db.Config.startingGold, $"{tag}: starting gold");

                    // talents
                    int spent = Progression.TalentPointsSpent(u);
                    int total = Progression.TalentPointsTotal(level);
                    Harness.Assert(spent <= total, $"{tag}: talents within budget");
                    if (level >= 10 && cls.defaultBuild.Length > 0)
                        Harness.Assert(spent == Math.Min(total, cls.defaultBuild.Length) || spent >= Math.Min(total, cls.defaultBuild.Length) - 2,
                            $"{tag}: talents auto-allocated ({spent}/{total}, build {cls.defaultBuild.Length})");
                    foreach (var kv in u.Talents)
                    {
                        var tal = db.Talent(kv.Key);
                        Harness.Assert(tal != null && kv.Value <= tal.maxRank, $"{tag}: talent {kv.Key} legal");
                        foreach (var p in tal.effects)
                            if (p.type == "GrantAbility") Harness.Assert(u.Knows(p.ability), $"{tag}: talent ability {p.ability}");
                    }

                    // gear equipped and legal
                    foreach (var kv in u.Equipment.Equipped)
                        Harness.Assert(EquipmentRules.CannotEquipReason(u, kv.Value.Def, kv.Key) == null,
                            $"{tag}: illegal {kv.Value.Name} in {kv.Key}: {EquipmentRules.CannotEquipReason(u, kv.Value.Def, kv.Key)}");
                    Harness.Assert(u.Equipment.MainHand != null || c == ClassId.None, $"{tag}: has a main-hand weapon");
                    if (level >= 10)
                    {
                        foreach (var slot in ArmorSlots)
                            Harness.Assert(u.Equipment[slot] != null, $"{tag}: {slot} filled");
                        Harness.Assert(u.Equipment[EquipSlot.Finger1] != null && u.Equipment[EquipSlot.Neck] != null, $"{tag}: jewellery");
                        foreach (var kv in u.Equipment.Equipped)
                            if (kv.Key != EquipSlot.Ranged && kv.Key != EquipSlot.OffHand && kv.Key != EquipSlot.Trinket1 && kv.Key != EquipSlot.Trinket2)
                                Harness.Assert(kv.Value.Def.itemLevel >= Math.Min(level, 10) - 6, $"{tag}: {kv.Value.Name} (ilvl {kv.Value.Def.itemLevel}) is level-appropriate");
                        var mh = u.Equipment.MainHand;
                        Harness.Assert(mh != null && mh.Def.itemLevel >= level - 6, $"{tag}: weapon ilvl {mh?.Def.itemLevel}");
                    }
                    // starting items in the bags
                    foreach (var id in db.Config.startingItems)
                        Harness.Assert(db.Item(id) == null || s.CountItem(id) > 0, $"{tag}: starting item {id}");
                    Harness.Assert(s.CannotSaveReason() == null, $"{tag}: can save");
                }
            }
        }

        [Test]
        public static void NewGame_OpeningDialogue_StartsMainQuest()
        {
            var s = SessionTest.NewGame(ClassId.Paladin, 1, seed: 3, opening: true);
            var events = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(events, SessionEventKind.GameStarted) != null, "GameStarted event");
            Harness.Assert(SessionTest.FindEvent(events, SessionEventKind.MapEntered, "lanternvale") != null, "MapEntered event");
            Harness.Assert(s.Mode == SessionMode.Dialogue && s.Dialogue.Dialogue.id == Harness.Db.Config.startDialogue, "opening dialogue running");
            Harness.Assert(s.CannotSaveReason() != null, "no save during dialogue");
            float hour = s.GameHour;
            s.Tick(30f);
            Harness.Assert(s.GameHour == hour, "clock paused in dialogue");
            SessionTest.SkipText(s);
            Harness.Assert(SessionTest.ChoiceIndex(s.Dialogue.Current, "Whatever is doing this") >= 0, "paladin class choice visible");
            Harness.Assert(SessionTest.ChoiceIndex(s.Dialogue.Current, "Point me at whatever") < 0, "warrior class choice hidden");
            SessionTest.Pick(s, "Whatever is doing this");
            SessionTest.Pick(s, "I'll help");
            SessionTest.Finish(s);
            Harness.Assert(s.Mode == SessionMode.Exploration, "back to exploring");
            Harness.Assert(s.Quests.IsActive("mq_lanterns"), "main quest started");
            Harness.Assert(s.Flags.IsSet("opening_seen"), "opening flag");
            var ev = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.QuestStarted, "mq_lanterns") != null, "QuestStarted toast");
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.DialogueEnded) != null, "DialogueEnded");
        }

        [Test]
        public static void NewGame_AppearanceAndNoVeteranGear()
        {
            var s = new GameSession(Harness.Db, 5);
            s.NewGame(new NewGameOptions { Name = "Lulu", Class = ClassId.Mage, StartLevel = 20, Sprite = "char_custom", Portrait = "portrait_custom", VeteranGear = false, PlayOpening = false });
            Harness.Assert(s.Main.Sprite == "char_custom" && s.Main.Portrait == "portrait_custom", "appearance override");
            foreach (var kv in s.Main.Equipment.Equipped) Harness.Assert(!kv.Value.Generated, "no generated gear without the option");
            Harness.Assert(Progression.TalentPointsSpent(s.Main) > 0, "talents still auto-allocated");
            Harness.Assert(!s.Settings.VeteranGear, "veteran gear setting off");
        }
    }
}
