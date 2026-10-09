// An interrupt that meets an uninterruptible boss cast reads as such: the combat log line and the floating word
// (CombatLog.AvoidWord, used by the combat and field presenters) say "uninterruptible" instead of a generic "immune",
// and the interrupt spends nothing beyond its own cooldown (no school lockout, the cast goes on).
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsUninterruptibleText
    {
        [Test]
        public static void KickOnAnUninterruptibleCast_ReadsUninterruptible()
        {
            var slam = Db.Ability("cr_r2_wyrmfire_slam");
            Assert(Battle.IsUninterruptible(slam), "Wyrmfire Slam is uninterruptible");
            var b = NewBattle(43, new Inventory());
            var rogue = Hero(ClassId.Rogue, 32, talents: false).At(20f, 20f);
            var brute = Mob("cr_wolf", 32).Tough().At(21.5f, 20f);
            brute.Abilities[slam.id] = 1;
            rogue.FaceTowards(brute.Position);
            b.AddUnit(rogue); b.AddUnit(brute);
            b.Begin();
            SkipTo(b, brute);
            brute.TimeLeft = 6f; brute.TimeDebt = 0f;
            Assert(b.UseAbility(brute, slam.id).Ok, "the slam is cast");

            SkipTo(b, rogue);
            rogue.Cooldowns.Clear(); rogue.Energy = rogue.MaxResource(ResourceType.Energy);
            var kick = Db.Ability("rogue_kick");
            int from = b.Events.Count;
            Assert(b.UseAbility(rogue, kick.id, brute).Ok, "the kick is used");
            var ev = b.Events.Skip(from).FirstOrDefault(e => e.Type == CombatEventType.Immune && e.Target == brute);
            Assert(ev != null && ev.Reason == CombatLog.UninterruptibleReason, "an Immune event with the uninterruptible reason");
            Assert(CombatLog.IsUninterruptible(ev), "IsUninterruptible");
            Assert(CombatLog.AvoidWord(ev) == "Uninterruptible", "floating word: " + CombatLog.AvoidWord(ev));
            Assert(ev.Text.Contains(slam.name + " is uninterruptible") && ev.Text.Contains("Kick has no effect") && !ev.Text.Contains("immune"),
                "log line: " + ev.Text);

            // nothing else is spent: the slam goes on, no lockout, only the kick's own cooldown
            Assert(brute.Pending != null && brute.Pending.Ability == slam, "the slam goes on");
            Assert(!brute.Lockouts.Any(kv => kv.Value > 0f), "no school lockout");
            Assert(!b.Events.Skip(from).Any(e => e.Type == CombatEventType.CastInterrupted), "no interruption");

            // other Immune events keep their word and line
            var plain = new CombatEvent { Type = CombatEventType.Immune, Source = rogue, Target = brute, Name = "Kick" };
            Assert(CombatLog.AvoidWord(plain) == "Immune" && CombatLog.Format(plain).Contains("is immune to"), "a plain immune: " + CombatLog.Format(plain));
            Assert(CombatLog.AvoidWord(new CombatEvent { Type = CombatEventType.Dodge }) == "Dodge", "other avoid words unchanged");
        }
    }
}
