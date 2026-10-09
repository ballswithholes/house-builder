// The battle formation (GameSession.Raid.cs ArrangeBattleFormation, called by PrepareEncounter): at the start of a fight
// the party stands in rows facing the enemies — tanks in front, melee behind them, ranged, then healers; pets beside their
// owner — on free walkable ground; it is skipped when a stealthed attacker or an opener starts the fight and against
// training dummies.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsSessionBattleFormation
    {
        /// <summary>A mage leading Kael (tank), Seren (healer), Pip (melee) and Rook (ranged, with his pet) in Mirefen.</summary>
        static GameSession FiveParty(ulong seed)
        {
            var s = SessionTest.NewGame(ClassId.Mage, 22, seed);
            foreach (var id in new[] { "kael", "seren", "pip", "rook" }) s.Recruit(id);
            s.EnterMap("mirefen", "from_raid_hollow_heart");
            var rook = s.FindMember("rook");
            Assert(s.UseAbility(rook, "hunter_call_pet").Ok && rook.Pet != null, "Rook calls his pet");
            s.FindMember("kael").RoleOverride = UnitRole.Tank;
            s.FindMember("seren").RoleOverride = UnitRole.Healer;
            s.FindMember("pip").RoleOverride = UnitRole.MeleeDps;
            rook.RoleOverride = UnitRole.RangedDps;
            return s;
        }

        /// <summary>Every unit of the battle stands on walkable ground, clear of every other unit.</summary>
        static void AssertNoOverlap(GameSession s, Battle b)
        {
            var agent = NavAgent.Default.IgnoringAllUnits();
            foreach (var u in b.Units)
            {
                Assert(s.Nav.IsWalkable(u.Position, NavAgent.ForUnit(u.Radius, u.Id).IgnoringAllUnits()), $"{u.Name} on walkable ground at {u.Position}");
                foreach (var o in b.Units)
                    if (o != u && !u.IsTotem && !o.IsTotem)
                        Assert(Vec2.Distance(u.Position, o.Position) >= u.Radius + o.Radius - 0.05f, $"{u.Name} and {o.Name} do not overlap ({Vec2.Distance(u.Position, o.Position):0.00} m)");
            }
        }

        static Vec2 Centroid(IEnumerable<Unit> us)
        {
            var list = us.ToList();
            var c = Vec2.Zero;
            foreach (var u in list) c += u.Position;
            return c / list.Count;
        }

        [Test]
        public static void Formation_TanksFront_MeleeRangedHealersBehind_PetsByOwner()
        {
            List<Vec2> First = null;
            foreach (var pass in new[] { 0, 1 })
            {
                var s = FiveParty(71);
                var at = RaidTest.OpenArea(s, 20f, 14f, s.Leader.Position);
                using (var enc = new RaidTest.TempEncounter("mirefen", "enc_test_formation_" + pass, at + new Vec2(5f, 0f), RaidTest.Pack(4)))
                {
                    // the party arrives strung out behind the leader
                    var lead = at + new Vec2(1f, 0f);
                    Assert(s.SetPartyPositions(lead) == null, "place the party");
                    var before = Centroid(s.Party);
                    var enemyCentre = enc.Def.enemies.Aggregate(Vec2.Zero, (a, e) => a + e.pos) / enc.Def.enemies.Count;
                    var f = (enemyCentre - before).Normalized;
                    var b = s.StartEncounter(enc.Def.id);
                    Assert(b != null, "fight: " + s.LastError);
                    AssertNoOverlap(s, b);
                    float Depth(string id) => Vec2.Dot(s.FindMember(id).Position - before, f);
                    var kael = Depth("kael"); var pip = Depth("pip"); var rook = Depth("rook"); var mage = Vec2.Dot(s.Main.Position - before, f); var seren = Depth("seren");
                    Assert(kael > pip + 0.4f, $"the tank stands before the melee ({kael:0.0} vs {pip:0.0})");
                    Assert(pip > rook + 1.5f && pip > mage + 1.5f, $"the melee before the ranged ({pip:0.0} vs {rook:0.0}, {mage:0.0})");
                    Assert(rook > seren + 0.8f && mage > seren + 0.8f, $"the ranged before the healer ({rook:0.0}, {mage:0.0} vs {seren:0.0})");
                    float nearest = b.Units.Where(u => u.Team == Team.Enemy).Min(e => Vec2.Distance(e.Position, s.FindMember("kael").Position));
                    Assert(nearest > 1.5f && nearest < GameSession.FormationFrontGap + 2f, $"the tank stands ~2.5 m short of the nearest enemy ({nearest:0.0} m)");
                    Assert(Vec2.Distance(before, Centroid(s.Party)) <= GameSession.FormationMaxAdvance + 3f, "the party does not leap across the map");
                    var pet = s.FindMember("rook").Pet;
                    Assert(pet != null && b.Units.Contains(pet) && Vec2.Distance(pet.Position, s.FindMember("rook").Position) < 2.5f, "the pet stands by Rook");
                    foreach (var u in s.Party) Assert(Vec2.Dot(u.Facing, f) > 0.9f, $"{u.Name} faces the enemies");
                    foreach (var u in s.PartyUnits()) Assert(s.Nav.AreConnected(s.Leader.Position, u.Position, u.Radius), $"{u.Name} stands on the leader's ground");

                    var now = s.PartyUnits().Select(u => u.Position).ToList();
                    if (First == null) First = now;
                    else Assert(First.SequenceEqual(now), "the formation is deterministic");
                    b.Finish(BattleOutcome.Fled);
                    s.FinishBattle();
                }
            }
        }

        [Test]
        public static void Formation_IsSkipped_ForStealth_Openers_AndTrainingDummies()
        {
            // an opener (Charge): everyone else keeps their place
            var s = SessionTest.NewGame(ClassId.Warrior, 22, 72);
            foreach (var id in new[] { "seren", "lys", "pip" }) s.Recruit(id);
            s.EnterMap("mirefen", "from_raid_hollow_heart");
            var at = RaidTest.OpenArea(s, 26f, 12f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter("mirefen", "enc_test_formation_opener", at + new Vec2(9f, 0f), RaidTest.Pack(3)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                var before = s.PartyUnits().Where(u => u != s.Main).ToDictionary(u => u, u => u.Position);
                var b = s.EngageEncounter(enc.Def.id, s.Main, "warrior_charge", 0);
                Assert(b != null, "engaged: " + s.LastError);
                foreach (var kv in before) Assert(kv.Key.Position == kv.Value, $"{kv.Key.Name} keeps its place under an opener");
                b.Finish(BattleOutcome.Fled);
                s.FinishBattle();
            }

            // a stealthed rogue opens: nobody moves
            var r = SessionTest.NewGame(ClassId.Rogue, 22, 73);
            foreach (var id in new[] { "kael", "seren" }) r.Recruit(id);
            r.EnterMap("mirefen", "from_raid_hollow_heart");
            at = RaidTest.OpenArea(r, 20f, 12f, r.Leader.Position);
            using (var enc = new RaidTest.TempEncounter("mirefen", "enc_test_formation_stealth", at + new Vec2(5f, 0f), RaidTest.Pack(3)))
            {
                r.SetPartyPositions(at - new Vec2(3f, 0f));
                Assert(r.UseAbility(r.Main, "rogue_stealth").Ok && r.Main.IsStealthed, "stealth");
                var before = r.PartyUnits().ToDictionary(u => u, u => u.Position);
                var b = r.EngageEncounter(enc.Def.id);
                Assert(b != null, "engaged from stealth: " + r.LastError);
                foreach (var kv in before) Assert(kv.Key.Position == kv.Value, $"{kv.Key.Name} keeps its place (stealth)");
                b.Finish(BattleOutcome.Fled);
                r.FinishBattle();
            }

            // the training dummy: practice where you stand
            var d = SessionTest.NewGame(ClassId.Hunter, 10, 74);
            d.Recruit("kael");
            d.Recruit("seren");
            var dummy = d.MapDef.encounters.First(e => e.id == "enc_training_dummy");
            d.SetPartyPositions(d.Nav.ClampToWalkable(dummy.pos + new Vec2(-5f, 0f), NavAgent.Default.IgnoringAllUnits()));
            var was = d.PartyUnits().ToDictionary(u => u, u => u.Position);
            Assert(d.StartEncounter("enc_training_dummy") != null, "practice");
            foreach (var kv in was) Assert(kv.Key.Position == kv.Value, $"{kv.Key.Name} keeps its place at the dummy");
        }

        [Test]
        public static void Formation_RaidOfTen_EveryoneFits()
        {
            var s = RaidTest.Game(ClassId.Hunter, 22, 75);
            Assert(s.UseAbility(s.Main, "hunter_call_pet").Ok, "a pet for Main");
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null, "enter");
            var at = RaidTest.OpenArea(s, 20f, 14f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter(RaidTest.Raid, "enc_test_formation_raid", at + new Vec2(6f, 0f), RaidTest.Pack(12)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "fight: " + s.LastError);
                Assert(b.Units.Count(u => u.Team == b.PlayerTeam && u.IsCharacter) == 10, "ten characters");
                AssertNoOverlap(s, b);
                var enemies = b.Units.Where(u => u.Team == Team.Enemy).ToList();
                foreach (var u in s.PartyUnits())
                {
                    Assert(s.Nav.AreConnected(s.Leader.Position, u.Position, u.Radius), $"{u.Name} on the leader's ground");
                    float d = enemies.Min(e => Vec2.Distance(e.Position, u.Position));
                    Assert(d > 1f && d < 14f, $"{u.Name} stands at a fighting distance ({d:0.0} m)");
                }
                var tanks = s.Party.Where(u => u.Role == UnitRole.Tank).ToList();
                var healers = s.Party.Where(u => u.Role == UnitRole.Healer).ToList();
                if (tanks.Count > 0 && healers.Count > 0)
                {
                    float Near(Unit u) => enemies.Min(e => Vec2.Distance(e.Position, u.Position));
                    Assert(tanks.Max(Near) < healers.Min(Near), "every tank stands closer to the enemies than every healer");
                }
            }
        }
    }
}
