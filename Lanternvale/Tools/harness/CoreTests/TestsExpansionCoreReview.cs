// Core fixes from the expansion review: uninterruptible boss casts (ability tag Uninterruptible: interrupts, silences and
// crowd control do not cancel them, a full-turn stun only delays them, the companion AI does not waste its kicks on them),
// ponds blocked along the shore the terrain draws (Spline.PondShore), and companions recruited without veteran gear
// wearing their class's starter pieces in the slots their signature items leave empty.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsExpansionCoreReview
    {
        // ===================================================================================================== uninterruptible

        [Test]
        public static void Uninterruptible_KickIsShrugged_AStunOnlyDelaysIt()
        {
            var slam = Db.Ability("cr_r2_wyrmfire_slam");
            Assert(Battle.IsTelegraphed(slam) && Battle.IsUninterruptible(slam), "Wyrmfire Slam: a telegraphed, uninterruptible cast");
            var b = NewBattle(41, new Inventory());
            var rogue = Hero(ClassId.Rogue, 32, talents: false).At(20f, 20f);
            var brute = Mob("cr_wolf", 32).Tough().At(21.5f, 20f);   // not immune to stuns, unlike the bosses
            brute.Abilities[slam.id] = 1;
            rogue.FaceTowards(brute.Position);
            b.AddUnit(rogue); b.AddUnit(brute);
            b.Begin();
            SkipTo(b, brute);
            brute.TimeLeft = 6f; brute.TimeDebt = 0f;
            Assert(b.UseAbility(brute, slam.id).Ok, "the slam is cast");
            Assert(brute.Pending != null && brute.Pending.Ability == slam, "pending until the brute's next turn");

            // the rogue's turn: the companion AI does not pick Kick (or any stop) for it, and a Kick does nothing
            SkipTo(b, rogue);
            rogue.Cooldowns.Clear(); rogue.Energy = rogue.MaxResource(ResourceType.Energy); rogue.AutoPlay = true;
            var kick = Db.Ability("rogue_kick");
            Assert(!AI.CancelsCast(b, kick, brute), "AI: a kick cannot stop it");
            Assert(!AI.CancelsCast(b, Db.Ability("rogue_gouge"), brute), "AI: nor can a gouge");
            var step = AI.NextStep(b, rogue);
            Assert(step.AbilityId != kick.id && !(step.Reason ?? "").StartsWith("interrupt"), "AI: no kick wasted on it: " + step);
            var r = b.UseAbility(rogue, kick.id, brute);
            Assert(r.Ok, "the kick lands: " + r.Reason);
            Assert(brute.Pending != null && brute.Pending.Ability == slam, "the slam goes on");
            Assert(b.Events.Any(e => e.Type == CombatEventType.Immune && e.Target == brute && e.AbilityId == kick.id && e.Reason == "uninterruptible"),
                "the log says it cannot be interrupted");
            Assert(!b.Events.Any(e => e.Type == CombatEventType.CastInterrupted && e.AbilityId == slam.id), "no interruption");
            Assert(!brute.Lockouts.Any(kv => kv.Value > 0f), "no school lockout");

            // a stun lasting into the brute's next turn: the turn is lost, the slam waits; it lands the turn after
            var stun = Db.Aura("paladin_hammer_of_justice");
            Assert(stun != null && stun.states.Contains(UnitState.Stun), "a stun aura");
            Assert(b.ApplyAura(rogue, brute, stun, new AuraApplyInfo { Duration = 7f, EffLevel = 32, LearnLevel = 8 }) != null, "stunned");
            Assert(brute.Pending != null, "the stun does not cancel it");
            SkipTo(b, brute);   // the stunned turn is skipped on its own; this stops at the brute's next, free turn
            int skipped = b.Events.FindIndex(e => e.Type == CombatEventType.TurnSkipped && e.Source == brute);
            int landed = b.Events.FindIndex(e => e.Type == CombatEventType.CastComplete && e.AbilityId == slam.id);
            Assert(skipped >= 0, "the stun took the brute's turn");
            Assert(landed > skipped, "the slam lands after the lost turn, once the stun wears off");
            Assert(!b.Events.Any(e => e.Type == CombatEventType.CastInterrupted && e.AbilityId == slam.id), "never interrupted");
            Assert(b.Events.Any(e => e.Type == CombatEventType.Damage && e.AbilityId == slam.id && e.Target == rogue), "and hits the rogue");
        }

        [Test]
        public static void Uninterruptible_OtherCastsStillStopped()
        {
            // an untagged telegraph (the Hollow Warden's requiem) is still kicked, with the lockout; the companion AI kicks it
            var req = Db.Ability("cr_lantern_requiem");
            Assert(Battle.IsTelegraphed(req) && !Battle.IsUninterruptible(req), "the requiem can be interrupted");
            var b = NewBattle(43, new Inventory());
            var rogue = Hero(ClassId.Rogue, 20, talents: false).At(20f, 20f);
            var warden = Mob("cr_hollow_warden", 20).Tough().At(21.5f, 20f);
            rogue.FaceTowards(warden.Position);
            b.AddUnit(rogue); b.AddUnit(warden);
            b.Begin();
            SkipTo(b, warden);
            warden.TimeLeft = 6f; warden.TimeDebt = 0f;
            Assert(b.UseAbility(warden, req.id).Ok && warden.Pending != null, "the requiem is cast");
            SkipTo(b, rogue);
            rogue.Cooldowns.Clear(); rogue.Energy = rogue.MaxResource(ResourceType.Energy); rogue.AutoPlay = true;
            Assert(AI.CancelsCast(b, Db.Ability("rogue_kick"), warden), "AI: a kick stops it");
            var step = AI.NextStep(b, rogue);
            Assert(step.Target == warden && (step.AbilityId == "rogue_kick" || AI.CancelsCast(b, Db.Ability(step.AbilityId), warden)), "AI: stop the requiem: " + step);
            Assert(AI.Execute(b, step).Ok, "stop it");
            Assert(warden.Pending == null && b.Events.Any(e => e.Type == CombatEventType.CastInterrupted && e.AbilityId == req.id), "the requiem is stopped");
        }

        [Test]
        public static void Uninterruptible_TheRaidAoEsAndSomeDungeonSignatures_HealsStayInterruptible()
        {
            var tagged = new[]
            {
                "cr_r1_bramble_burst", "cr_r1_veil_of_tears", "cr_r1_bog_eruption", "cr_r2_bitter_cold", "cr_r2_avalanche",
                "cr_r2_ash_breath", "cr_r2_choking_cinders", "cr_r2_brand_of_the_wyrm", "cr_r2_wyrmfire_slam", "cr_r2_fire_breath",
                "cr_r2_rain_of_cinders", "cr_r2_hollowfire", "cr_dg5_tidal_wave", "cr_dg6_glacial_pulse",
            };
            foreach (var id in tagged)
            {
                var a = Db.Ability(id);
                Assert(Battle.IsUninterruptible(a) && Battle.IsTelegraphed(a), id + ": uninterruptible telegraph");
                Assert(a.description.Contains("cannot be interrupted"), id + ": the tooltip says so");
            }
            // the bosses' heals and the casts whose text asks for an interrupt stay interruptible
            foreach (var id in new[] { "cr_r1_drink_the_light", "cr_r1_coven_mend", "cr_r1_solace_mending", "cr_r1_witchs_brew", "cr_r2_rekindle",
                                       "cr_r2_cinder_mend", "cr_dg2_glow_nova", "cr_dg4_kingsfall", "cr_dg5_pearl_mend", "cr_lantern_requiem" })
                Assert(Battle.IsTelegraphed(Db.Ability(id)) && !Battle.IsUninterruptible(Db.Ability(id)), id + ": interruptible");
            foreach (var a in Db.Abilities.Values.Where(Battle.IsUninterruptible))
                Assert(a.id.StartsWith("cr_"), a.id + ": only creature casts are uninterruptible");
            Assert(!Battle.IsUninterruptible(null) && !Battle.HasInterruptibleCast(null), "null");

            // validator: the tag needs a cast time or a channel
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_uninterruptible_bad.json",
                @"{""abilities"": [{""id"": ""xz_instant_unint"", ""name"": ""Instant"", ""icon"": ""fire"", ""learnLevel"": 1, ""target"": ""Self"",
                    ""effects"": [{""type"": ""Damage"", ""min"": 5}], ""tags"": [""Uninterruptible""]}]}"));
            var db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "parses: " + string.Join("; ", db.Problems));
            var problems = DataValidator.Validate(db);
            Assert(problems.Any(p => p.Contains("xz_instant_unint") && p.Contains("Uninterruptible")), "validator: " + string.Join("; ", problems.Where(p => p.Contains("xz_"))));
        }

        // ===================================================================================================== ponds

        [Test]
        public static void Ponds_TheNavGridBlocksTheShoreTheTerrainDraws()
        {
            int ponds = 0;
            foreach (var m in Db.Maps.Values)
            {
                if (m.water == null || !m.water.Any(w => w != null && w.closed && w.blocksMovement)) continue;
                var nav = new NavGrid(m, null, TestsMapsReachable.AllFlagsSet);
                foreach (var w in m.water.Where(w => w != null && w.closed && w.blocksMovement && w.points.Count >= 3))
                {
                    ponds++;
                    var shore = Spline.PondShore(w.points);
                    Assert(shore.Count >= w.points.Count, "a shore loop");
                    float worst = 0f;
                    Vec2 at = default;
                    foreach (var p in shore)
                    {
                        if (p.x < 0.6f || p.y < 0.6f || p.x > m.width - 0.6f || p.y > m.depth - 0.6f) continue;
                        if (w.crossings != null && w.crossings.Any(c => c != null && Math.Abs(p.x - c.pos.x) <= c.size.x * 0.5f + 0.75f && Math.Abs(p.y - c.pos.y) <= c.size.y * 0.5f + 0.75f)) continue;
                        // the nearest water cell: within half a metre of the drawn shore (cells are 0.5 m)
                        float near = float.MaxValue;
                        for (int k = 0; k <= 4 && near == float.MaxValue; k++)
                        {
                            float rad = 0.15f * k;
                            for (int d = 0; d < 16; d++)
                            {
                                double ang = d * Math.PI / 8;
                                var q = new Vec2(p.x + rad * (float)Math.Cos(ang), p.y + rad * (float)Math.Sin(ang));
                                if (nav.IsWater(q)) { near = rad; break; }
                            }
                        }
                        float gap = near == float.MaxValue ? 1f : near;
                        if (gap > worst) { worst = gap; at = p; }
                    }
                    Assert(worst <= 0.45f + 1e-3f, $"{m.id}: the pond's drawn shore at {at} is {worst:0.00} m from blocked water");
                    // the old rule blocked only the raw polygon: the shore's bulge is blocked now too
                    foreach (var p in shore)
                    {
                        float cx = (float)Math.Floor(p.x / nav.CellSize) * nav.CellSize + nav.CellSize * 0.5f, cy = (float)Math.Floor(p.y / nav.CellSize) * nav.CellSize + nav.CellSize * 0.5f;
                        if (!PointIn(shore, cx, cy) || (w.crossings != null && w.crossings.Any(c => c != null && Math.Abs(cx - c.pos.x) <= c.size.x * 0.5f && Math.Abs(cy - c.pos.y) <= c.size.y * 0.5f))) continue;
                        Assert(nav.IsWater(new Vec2(cx, cy)), $"{m.id}: a cell centre inside the drawn shore at ({cx:0.00}, {cy:0.00}) is water");
                    }
                }
            }
            Assert(ponds >= 5, "the expansion's maps have ponds: " + ponds);
        }

        static bool PointIn(List<Vec2> poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > y) != (poly[j].y > y) && x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        // ===================================================================================================== recruits

        [Test]
        public static void Recruits_WithoutVeteranGear_WearClassStarterPiecesInEmptySlots()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 14, 5, veteranGear: false);
            int bagBefore = s.Inventory.Items.Count;
            foreach (var id in new[] { "bruna", "ysolde", "liora", "nanami", "kael", "pip" }) s.Recruit(id);
            foreach (var u in s.Roster.Where(x => x.Companion != null))
            {
                var comp = u.Companion;
                foreach (var sig in comp.startingItems.Distinct())
                    Assert(u.Equipment.Equipped.Any(kv => kv.Value.Def.id == sig) || s.Inventory.Items.Any(i => i.Def.id == sig), $"{comp.id}: keeps {sig}");
                var starter = u.Class.startingItems.Select(Db.Item).Where(d => d != null && d.equip != EquipType.None).ToList();
                foreach (var d in starter)
                {
                    var slots = EquipmentRules.SlotsFor(d);
                    if (slots.Contains(EquipSlot.MainHand) || slots.Contains(EquipSlot.OffHand)) continue;
                    Assert(slots.Any(sl => u.Equipment[sl] != null), $"{comp.id}: the {slots[0]} slot is not left bare ({d.id} fits it)");
                }
                Assert(u.Equipment.MainHand != null, comp.id + ": armed");
                Assert(u.Equipment[EquipSlot.Legs] != null, comp.id + ": legs covered");
                Assert(u.Equipment.Equipped.All(kv => EquipmentRules.CannotUseReason(u, kv.Value.Def) == null), comp.id + ": everything worn is usable");
            }
            Assert(!s.Inventory.Items.Skip(bagBefore).Any(i => i.Def.id.Contains("_starter_")), "no spare starter pieces in the bags");
        }
    }
}
