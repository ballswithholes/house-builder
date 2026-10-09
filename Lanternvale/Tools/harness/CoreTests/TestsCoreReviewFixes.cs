// Regression tests of the final core review: suppressed encounters survive save/load, area-aura children (Blood Pact)
// survive load and leave units that leave the party, proficiency passives (Plate Mail, Mail, Dual Wield, Parry) gate
// their proficiency, Enhancement Parry lets a shaman parry, Mind Soothe soothes an encounter without a fight, Pick Lock
// works from the action bar, and the exploration tick allocates (almost) nothing.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Json;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsCoreReviewFixes
    {
        // ------------------------------------------------------------------------------------------ suppressed encounters

        [Test]
        public static void LeftPracticeFight_StaysSuppressed_AfterLoad()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 1, seed: 901);
            var enc = s.Map.FindEncounter("enc_training_dummy");
            Harness.Assert(enc != null, "training dummy encounter");
            var near = enc.pos + new Vec2(0.75f, 0f);       // inside the 0.9 m trigger radius
            var step = enc.pos + new Vec2(0.7f, 0.05f);
            Harness.Assert(s.SetPartyPositions(near + new Vec2(3f, 0f)) == null, "placed outside the radius");
            var tr = s.UpdatePartyPositions(near);
            Harness.Assert(tr.Kind == TriggerKind.Combat && s.Battle != null, "walking into the dummy starts the practice fight");
            Harness.Assert(s.LeaveCombat() != null && s.Battle == null, "left the practice fight");
            Harness.Assert(s.UpdatePartyPositions(step).Kind == TriggerKind.None && s.Battle == null, "a small step does not restart it");
            string json = s.SaveGame();

            var s2 = new GameSession(Harness.Db, 902);
            Harness.Assert(s2.LoadGame(json, out var err), "load: " + err);
            Harness.Assert(s2.SaveGame() == json, "save -> load -> save identical");
            var t2 = s2.UpdatePartyPositions(step);
            Harness.Assert(t2.Kind == TriggerKind.None && s2.Battle == null, "after loading, the same small step does not restart the fight");
            // walking away releases it, coming back starts it again
            Harness.Assert(s2.UpdatePartyPositions(enc.pos + new Vec2(4f, 0f)).Kind == TriggerKind.None, "walked away");
            Harness.Assert(s2.UpdatePartyPositions(near).Kind == TriggerKind.Combat && s2.Battle != null, "released once the party left: it triggers again");
            s2.LeaveCombat();

            // a save written before the suppressed list existed: what stands next to the party is suppressed on load
            var d = JsonMapper.FromJson<SessionSaveData>(json);
            d.suppressedEncounters = null;
            d.soothedEncounters = null;
            var s3 = new GameSession(Harness.Db, 903);
            Harness.Assert(s3.LoadGame(JsonWriter.Serialize(d, true), out err), "legacy load: " + err);
            Harness.Assert(s3.UpdatePartyPositions(step).Kind == TriggerKind.None && s3.Battle == null, "legacy save: no fight on the first step");
        }

        // ------------------------------------------------------------------------------------------ area-aura children

        static GameSession WarlockWithImpAndKael(out Unit imp, out Unit kael)
        {
            var s = SessionTest.NewGame(ClassId.Warlock, 30, seed: 911);
            s.Recruit("kael");
            var r = s.UseAbility(s.Main, "warlock_summon_imp");
            Harness.Assert(r.Ok && s.Main.Pet != null, "imp summoned: " + r.Reason);
            imp = s.Main.Pet;
            if (!imp.HasAura("warlock_imp_blood_pact"))
            {
                var bp = s.UseAbility(imp, "warlock_imp_blood_pact");
                Harness.Assert(bp.Ok, "Blood Pact: " + bp.Reason);
            }
            s.Tick(0.1f);
            kael = s.FindMember("kael");
            Harness.Assert(kael != null && s.IsInParty(kael), "kael in the party");
            foreach (var u in s.PartyUnits()) u.RestoreFull();
            return s;
        }

        [Test]
        public static void BloodPact_HealthSurvivesSaveLoad()
        {
            var s = WarlockWithImpAndKael(out var imp, out var kael);
            Harness.Assert(kael.HasAura("warlock_imp_blood_pact_buff") && s.Main.HasAura("warlock_imp_blood_pact_buff"), "party members carry Blood Pact");
            float mainHp = s.Main.Health, kaelHp = kael.Health, impHp = imp.Health;
            Harness.Assert(mainHp >= s.Main.MaxHealth - 0.01f && kaelHp >= kael.MaxHealth - 0.01f, "full health with the buff");
            string json = s.SaveGame();

            var s2 = new GameSession(Harness.Db, 912);
            Harness.Assert(s2.LoadGame(json, out var err), "load: " + err);
            var kael2 = s2.FindMember("kael");
            Harness.Assert(kael2.HasAura("warlock_imp_blood_pact_buff"), "Blood Pact back on Kael");
            Harness.AssertNear(s2.Main.Health, mainHp, 0.01f, "main health kept through the load");
            Harness.AssertNear(kael2.Health, kaelHp, 0.01f, "Kael health kept through the load");
            Harness.Assert(s2.Main.Pet != null, "imp restored");
            Harness.AssertNear(s2.Main.Pet.Health, impHp, 0.01f, "imp health kept through the load");
            string json2 = s2.SaveGame();
            Harness.Assert(json2 == json, "save -> load -> save identical with Blood Pact active");
        }

        [Test]
        public static void BloodPact_LeavesCompanionSentToCamp()
        {
            var s = WarlockWithImpAndKael(out var imp, out var kael);
            float buffed = kael.MaxHealth;
            Harness.Assert(kael.HasAura("warlock_imp_blood_pact_buff"), "buffed in the party");
            Harness.Assert(s.SetPartyMemberActive("kael", false) == null, "kael to camp");
            Harness.Assert(!kael.HasAura("warlock_imp_blood_pact_buff"), "no Blood Pact at camp");
            Harness.Assert(kael.MaxHealth < buffed && kael.Health <= kael.MaxHealth + 0.01f, "camp stats without the buff");
            s.Tick(60f);
            s.EnterMap("whisperwood", "from_village");
            Harness.Assert(!kael.HasAura("warlock_imp_blood_pact_buff"), "still none after time and travel");
            Harness.Assert(s.SetPartyMemberActive("kael", true) == null, "kael back");
            s.Tick(0.1f);
            Harness.Assert(kael.HasAura("warlock_imp_blood_pact_buff"), "Blood Pact again once back with the imp");

            // a dismissed companion loses it too
            s.Dismiss("kael");
            Harness.Assert(!kael.HasAura("warlock_imp_blood_pact_buff"), "dismissed: no Blood Pact");
            foreach (var a in kael.Auras) Harness.Assert(!a.IsAreaChild, "no area child left on a dismissed companion");
        }

        // ------------------------------------------------------------------------------------------ proficiencies

        static ItemInstance FindItem(Unit u, Func<ItemDef, bool> pred)
        {
            var ids = new List<string>(Harness.Db.Items.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var d = Harness.Db.Items[id];
                if (d.equipEffects.Count > 0 || d.quality >= Quality.Epic) continue;   // plain items only (no expansion procs or epics)
                if (d.requiredLevel <= u.Level && (d.classes == null || d.classes.Length == 0) && pred(d)) return new ItemInstance(d);
            }
            throw new Exception("no matching item");
        }

        [Test]
        public static void ProficiencyPassives_GateArmourDualWieldAndParry()
        {
            var db = Harness.Db;
            var w = UnitFactory.CreateCharacter(db, ClassId.Warrior, "W", 40);   // knows only its starting abilities
            var sword = FindItem(w, d => d.weaponType == WeaponType.OneHandSword && d.equip == EquipType.OneHand);
            var offSword = FindItem(w, d => d.weaponType == WeaponType.OneHandSword && d.equip == EquipType.OneHand);
            var plate = FindItem(w, d => d.armorType == ArmorType.Plate && d.equip == EquipType.Chest);
            EquipmentRules.Unequip(w, EquipSlot.OffHand);
            EquipmentRules.Equip(w, sword, EquipSlot.MainHand);
            w.InvalidateStats();

            Harness.Assert(!w.Knows("warrior_plate_mail") && !w.Knows("warrior_dual_wield") && !w.Knows("warrior_parry"), "untrained");
            var why = EquipmentRules.CannotEquipReason(w, plate.Def, EquipSlot.Chest);
            Harness.Assert(why != null && why.Contains("Plate Mail"), "plate needs Plate Mail: " + why);
            why = EquipmentRules.CannotEquipReason(w, offSword.Def, EquipSlot.OffHand);
            Harness.Assert(why != null && why.Contains("Dual Wield"), "off hand needs Dual Wield: " + why);
            Harness.Assert(!w.Stats.CanParry && w.Stats.Parry == 0f, "no parry before Parry is trained");

            var inv = new Inventory { Gold = 1000000 };
            foreach (var id in new[] { "warrior_plate_mail", "warrior_dual_wield", "warrior_parry" })
                Harness.Assert(Progression.Train(w, db.Ability(id), 1, inv) == null, "train " + id);
            w.InvalidateStats();
            Harness.Assert(EquipmentRules.CannotEquipReason(w, plate.Def, EquipSlot.Chest) == null, "plate after Plate Mail");
            Harness.Assert(EquipmentRules.CannotEquipReason(w, offSword.Def, EquipSlot.OffHand) == null, "dual wield after Dual Wield");
            Harness.Assert(w.Stats.CanParry && w.Stats.Parry >= 5f, "parries after Parry: " + w.Stats.Parry);

            // below the level the trainer cannot teach them, and a level 1 warrior does not parry yet
            var w1 = UnitFactory.CreateCharacter(db, ClassId.Warrior, "W1", 1);
            Harness.Assert(w1.Equipment.HasMeleeWeapon && !w1.Stats.CanParry && w1.Stats.Parry == 0f, "level 1 warrior: no parry yet");
            Harness.Assert(Progression.Train(w1, db.Ability("warrior_parry"), 1, inv) != null, "Parry is trained at level 6");

            // veteran starts and companions learn them with everything else
            var s = SessionTest.NewGame(ClassId.Warrior, 40, seed: 921);
            Harness.Assert(EquipmentRules.MaxArmor(s.Main) == ArmorType.Plate && EquipmentRules.CanDualWield(s.Main), "veteran level 40 warrior: plate and dual wield");
            Harness.Assert(s.Main.Stats.CanParry, "veteran warrior parries");
            // classes without such passives keep their level rule (mage: cloth only, no dual wield, no parry)
            var m = UnitFactory.CreateCharacter(db, ClassId.Mage, "M", 60, learnAll: true);
            Harness.Assert(EquipmentRules.MaxArmor(m) == ArmorType.Cloth && !EquipmentRules.CanDualWield(m) && !m.Stats.CanParry, "mage unchanged");
            // rogues dual wield from level 1: Dual Wield is a starting passive, not a trainer step (Parry still is)
            var r = UnitFactory.CreateCharacter(db, ClassId.Rogue, "R", 1);   // knows only its starting abilities
            var offDagger = FindItem(r, d => d.weaponType == WeaponType.Dagger && d.equip == EquipType.OneHand);
            Harness.Assert(r.Knows("rogue_dual_wield") && EquipmentRules.CanDualWield(r), "level 1 rogue: Dual Wield known, dual wields");
            Harness.Assert(EquipmentRules.ProficiencyPassive(r, EquipmentRules.Proficiency.DualWield) == null, "no trainer passive gates the rogue's dual wield");
            Harness.Assert(EquipmentRules.CannotEquipReason(r, offDagger.Def, EquipSlot.OffHand) == null, "level 1 rogue: a One-Hand dagger in the off hand");
            Harness.Assert(Progression.Train(r, db.Ability("rogue_dual_wield"), 1, inv) != null, "nothing to train");
            Harness.Assert(!r.Knows("rogue_parry") && !r.Stats.CanParry, "rogue Parry is still trained (level 12)");
        }

        [Test]
        public static void EnhancementParry_LetsTheShamanParry()
        {
            var db = Harness.Db;
            var sh = UnitFactory.CreateCharacter(db, ClassId.Shaman, "Sh", 60, learnAll: true);
            var mace = FindItem(sh, d => d.weaponType == WeaponType.OneHandMace && d.equip == EquipType.OneHand);
            EquipmentRules.Equip(sh, mace, EquipSlot.MainHand);
            sh.InvalidateStats();
            Harness.Assert(!sh.Stats.CanParry && sh.Stats.Parry == 0f, "no parry without the talent");

            // the default build spends a point in Parry (Enhancement tier 5)
            Progression.AutoAllocateTalents(sh);
            Progression.LearnAllAvailable(sh);
            Harness.Assert(sh.TalentRank("shaman_enh_parry") == 1 && sh.Knows("shaman_parry"), "Parry talent learned by the default build");
            sh.InvalidateStats();
            Harness.Assert(sh.Stats.CanParry && sh.Stats.Parry >= 5f && sh.Stats.Parry < 10f, "1/1 Parry: a 5% parry chance, got " + sh.Stats.Parry);

            // the hit table rolls it: a frontal melee attack can be parried
            var b = RulesTestUtil.NewBattle(921);
            var mob = RulesTestUtil.Mob("cr_wolf", 60).At(11f, 10f);
            sh.At(10f, 10f);
            mob.FaceTowards(sh.Position);
            sh.FaceTowards(mob.Position);
            b.AddUnits(new[] { sh, mob });
            var hc = b.SwingHitChance(mob, sh);
            Harness.Assert(hc.Parry > 0f, "the wolf's swings can be parried: " + hc);
        }

        // ------------------------------------------------------------------------------------------ field specials

        [Test]
        public static void MindSoothe_ShrinksTheEncounterRadius_WithoutAFight()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 30, seed: 931);
            s.EnterMap("whisperwood", "from_village");
            const string id = "enc_bandit_lookouts";
            var enc = s.Map.FindEncounter(id);
            Harness.Assert(enc != null && s.Main.Knows("priest_mind_soothe"), "bandit lookouts and Mind Soothe");
            Harness.Assert(s.IsEncounterFieldAbility("priest_mind_soothe") && !s.IsEncounterFieldAbility("priest_smite"), "Mind Soothe acts on encounters, Smite does not");
            Harness.AssertNear(s.EncounterTriggerRadius(id), enc.radius, 1e-4f, "normal trigger radius");

            // a Beast encounter is refused (Humanoid only) and nothing starts
            var boars = s.UseAbilityOnEncounter(s.Main, "priest_mind_soothe", "enc_boars_edge");
            Harness.Assert(!boars.Ok && s.Battle == null, "boars are not Humanoid: " + boars.Reason);
            Harness.Assert(!s.UseAbilityOnEncounter(s.Main, "priest_smite", id).Ok, "Smite would start a fight: refused");

            var stand = enc.pos + new Vec2(-6f, 0f);
            Harness.Assert(s.SetPartyPositions(stand) == null, "placed in range of the bandits");
            bool soothed = false;
            for (int i = 0; i < 8 && !soothed; i++)
            {
                s.Main.Mana = s.Main.MaxMana;
                float mana = s.Main.Mana;
                // the UI path: Mind Soothe armed as an opener, then the bandit clicked
                var b = s.EngageEncounter(id, s.Main, "priest_mind_soothe", 1);
                Harness.Assert(b == null && s.Battle == null && s.Mode == SessionMode.Exploration, "no fight starts: " + s.LastError);
                Harness.Assert(string.IsNullOrEmpty(s.LastError), "cast: " + s.LastError);
                Harness.Assert(s.Main.Mana < mana, "mana spent");
                soothed = s.EncounterSoothedSeconds(id) > 0f;
            }
            Harness.Assert(soothed, "Mind Soothe landed");
            Harness.AssertNear(s.EncounterSoothedSeconds(id), 15f, 0.5f, "15 seconds");
            Harness.AssertNear(s.EncounterTriggerRadius(id), Math.Max(1f, enc.radius - 4f), 1e-3f, "radius reduced by 4 m (min 1 m)");
            var ev = s.TakeEvents();
            bool toast = false;
            foreach (var e in ev) if (e.Kind == SessionEventKind.Toast && e.Text.Contains("soothed")) toast = true;
            Harness.Assert(toast, "soothed toast");

            // inside the normal radius but outside the soothed one: nothing happens
            var inside = enc.pos + new Vec2(-(enc.radius - 0.3f), 0f);
            Harness.Assert(s.SetPartyPositions(inside) == null, "placed inside the normal radius");
            var tr = s.UpdatePartyPositions(inside + new Vec2(0.05f, 0f));
            Harness.Assert(tr.Kind == TriggerKind.None && s.Battle == null && !s.Dialogue.IsActive, "the soothed bandits do not notice the party: " + tr.Kind);

            // it survives a save/load, then wears off after 15 s of real time
            string json = s.SaveGame();
            var s2 = new GameSession(Harness.Db, 932);
            Harness.Assert(s2.LoadGame(json, out var err) && s2.SaveGame() == json, "save round trip: " + err);
            Harness.Assert(s2.EncounterSoothedSeconds(id) > 0f && s2.UpdatePartyPositions(inside).Kind == TriggerKind.None, "still soothed after loading");
            s.Tick(16f);
            Harness.Assert(s.EncounterSoothedSeconds(id) == 0f && Math.Abs(s.EncounterTriggerRadius(id) - enc.radius) < 1e-4f, "worn off");
            var tr2 = s.UpdatePartyPositions(inside);
            Harness.Assert(tr2.Kind == TriggerKind.Dialogue || tr2.Kind == TriggerKind.Combat, "noticed again once it wore off: " + tr2.Kind);
        }

        [Test]
        public static void PickLock_FromTheActionBar_OpensTheNearestLock()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 30, seed: 941);
            s.EnterMap("whisperwood", "from_village");
            Harness.Assert(s.Main.Knows(GameSession.PickLockAbility), "rogue knows Pick Lock");
            s.TakeEvents();
            var r0 = s.UseAbility(s.Main, GameSession.PickLockAbility);
            bool none = false;
            foreach (var e in s.TakeEvents()) if (e.Kind == SessionEventKind.Toast && e.Text.Contains("no lock")) none = true;
            Harness.Assert(!r0.Ok || none, "far from any lock: says so");

            var chest = s.Map.FindChest("chest_bandit_strongbox");
            Harness.Assert(chest != null && s.Map.IsChestLocked(chest), "locked strongbox");
            Harness.Assert(s.SetPartyPositions(chest.pos + new Vec2(-1.2f, 0f)) == null, "next to the strongbox");
            bool opened = false;
            int checks = 0;
            for (int i = 0; i < 20 && !opened; i++)
            {
                var r = s.UseAbility(s.Main, GameSession.PickLockAbility);
                Harness.Assert(r.Ok, "Pick Lock from the bar: " + r.Reason);
                foreach (var e in s.TakeEvents()) if (e.Kind == SessionEventKind.SkillCheck && e.Id == chest.id) checks++;
                opened = s.Map.IsChestOpened(chest.id);
                if (s.PendingLoot != null) s.CloseLoot(true);
            }
            Harness.Assert(checks > 0, "each use rolls the lock");
            Harness.Assert(opened, "the strongbox opens");
        }

        // ------------------------------------------------------------------------------------------ Engaged per battle

        [Test]
        public static void Charge_UsableAgain_InTheNextBattle()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 10, seed: 961);
            var b = s.StartEncounter("enc_training_dummy");
            Harness.Assert(b != null, "dummy fight: " + s.LastError);
            int guard = 0;
            while (!s.Main.Engaged && !b.IsOver && guard++ < 60)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                AI.RunTurn(b, u);
                if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
            }
            Harness.Assert(s.Main.Engaged, "the warrior fought the dummy");
            Harness.Assert(s.LeaveCombat() != null, "left");
            s.Flags.Set("shepherd_quest");
            var b2 = s.StartEncounter("enc_pasture_wolves");
            Harness.Assert(b2 != null, "wolves: " + s.LastError);
            Harness.Assert(!s.Main.Engaged, "a new battle starts with the warrior not engaged");
            Unit wolf = null;
            foreach (var u in b2.Units) if (u.Team != s.Main.Team && u.IsAlive) { wolf = u; break; }
            guard = 0;
            while (b2.ActiveUnit != s.Main && !b2.IsOver && guard++ < 60)
            {
                var u = b2.ActiveUnit;
                if (u == null) break;
                if (u.Team == s.Main.Team) b2.EndTurn(u);
                else { AI.RunTurn(b2, u); if (b2.ActiveUnit == u && !b2.IsOver) b2.EndTurn(u); }
            }
            Harness.Assert(b2.ActiveUnit == s.Main, "the warrior's turn");
            if (!s.Main.Engaged)
            {
                var chk = b2.CanUse(s.Main, "warrior_charge", wolf);
                Harness.Assert(chk.Ok || !chk.Reason.Contains("already in combat"), "Charge is not blocked by the previous fight: " + chk.Reason);
            }
            s.LeaveCombat();
        }

        // ------------------------------------------------------------------------------------------ allocations

        [Test]
        public static void ExplorationTick_AllocatesAlmostNothing()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 20, seed: 951);
            s.Recruit("kael");
            s.Recruit("seren");
            s.Recruit("rook");
            s.TakeEvents();
            for (int i = 0; i < 30; i++) s.Tick(1f / 60f);   // warm up (pools, caches, JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            const int frames = 120;
            for (int i = 0; i < frames; i++) s.Tick(1f / 60f);
            long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / frames;
            Console.WriteLine($"    exploration tick: {perTick} bytes per frame ({s.PartyUnits().Count} party units)");
            Harness.Assert(perTick < 256, $"exploration Tick allocates {perTick} bytes per frame");
        }
    }
}
