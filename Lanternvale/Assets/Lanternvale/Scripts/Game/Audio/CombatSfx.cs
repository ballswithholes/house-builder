// Combat and world sound semantics on top of Sfx (Docs/PresentationAPI.md §6): which ids a swing, a release, a hit, an
// avoided blow, a cast, a death, a footstep or a loot drop plays, how loud, and when. The id choices come from
// Core/Rules/Combat/CombatSounds.cs (pure, tested by TestsCombatSounds); this file adds positions, volumes, the
// per-moment layer budget and the scaled-time scheduling of body falls.
//
// A physical hit is two layers — the weapon ("hit_blade", "hit_bite"…) and the material it lands on ("mat_plate",
// "mat_fur"…) — plus a sweetener on crits and killing blows, all scaled by the share of health the blow took. On an AoE
// moment only the first three targets get a material layer (softened by 1/√n); the 30 ms per-id rate limit in Sfx folds
// identical weapon layers into one.
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class CombatSfx
    {
        static GameDatabase Db => GameRoot.Instance != null ? GameRoot.Instance.Db : null;

        static AbilityDef AbilityOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = Db;
            return db != null ? db.Ability(id) : null;
        }

        // targets hit within one rendered frame (one AoE beat shows all its hits in the same frame)
        static int hitFrame = -1;
        static int hitsThisFrame;

        static int NextHitIndex()
        {
            int f = Time.frameCount;
            if (f != hitFrame) { hitFrame = f; hitsThisFrame = 0; }
            return ++hitsThisFrame;
        }

        // ------------------------------------------------------------------ attacks

        /// <summary>
        /// The pitch factor that keeps a timed clip in step with a faster presentation. AudioSource playback ignores
        /// Time.timeScale, but the blow lands at UnitView.AttackHitTime and a cast releases at UnitView.CastReleaseTime in
        /// scaled time: at 2× a clip whose peak should precede the blow must play twice as fast. Capped at 3× (beyond that
        /// the shift would turn a whoosh into a squeak); slow-motion and pause keep pitch 1.
        /// </summary>
        static float SpeedPitch => Mathf.Clamp(Time.timeScale, 1f, 3f);

        /// <summary>
        /// The whoosh of a melee swing, weighted by the weapon (daggers light, two-handers heavy) or the creature. It sits
        /// about 5 dB under the blow (×0.45): the contact, not the air, carries the hit.
        /// </summary>
        public static void Swing(Unit actor, bool offHand, Vector2 pos, float volume = 1f)
        {
            Sfx.Play(CombatSounds.SwingOf(actor, offHand), pos, volume * 0.45f, CombatSounds.SizePitchOf(actor) * SpeedPitch);
        }

        /// <summary>
        /// The release of a ranged attack (a = null for a white ranged swing): bow twang, crossbow click-thump, gun pop,
        /// throw, wand zap; magic bolts of creatures zap instead of twanging. Call it when the projectile leaves the hand
        /// (UnitView.ShootReleaseTime), or pass that delay and it is scheduled on scaled time.
        /// </summary>
        public static void Release(Unit actor, AbilityDef a, School school, Vector2 pos, float scaledDelay = 0f)
        {
            var id = CombatSounds.ReleaseOf(actor, school, a);
            float vol = id == "gun_fire" ? 0.75f : id == "wand_zap" ? 0.7f : 0.85f;
            Sfx.PlayAfter(id, scaledDelay, pos, vol, 1f);
        }

        /// <summary>The whistle of an arrow, bolt or thrown weapon in flight (scaledSeconds = flight time; nothing for magic or guns).</summary>
        public static void Flight(Unit actor, AbilityDef a, School school, Vector2 from, float scaledSeconds)
        {
            if (scaledSeconds < 0.12f || !CombatSounds.FlightWhooshOf(actor, school, a)) return;
            // the clip is ~0.5 s long at pitch 1: squeeze or stretch it towards the real flight time
            float real = scaledSeconds / Mathf.Max(0.05f, Time.timeScale);
            Sfx.Play("arrow_flight", from, 0.45f, Mathf.Clamp(0.5f / Mathf.Max(0.05f, real), 0.8f, 1.6f));
        }

        /// <summary>A direct damage event: weapon layer + material layer (physical) or the school impact, plus crit/kill sweeteners.</summary>
        public static void Hit(CombatEvent e, Vector2 pos)
        {
            if (e == null || e.Target == null) return;
            var t = e.Target;
            // (the unit's live state is ahead of the presentation: only the event itself says whether this blow killed)
            bool kill = e.Overkill > 0f;
            float vol = CombatSounds.HitVolume(e.Amount, t.MaxHealth, e.Crit, kill);
            int index = NextHitIndex();
            var a = AbilityOf(e.AbilityId);
            if (e.School == School.Physical)
            {
                Sfx.Play(CombatSounds.AttackLayerOf(e.Source, e.OffHand, e.Ranged), pos, vol * 0.9f, CombatSounds.SizePitchOf(e.Source));
                float g = CombatSounds.AreaLayerGain(index);
                if (g > 0f)
                    Sfx.Play(CombatSounds.MaterialLayer(CombatSounds.MaterialOf(t)), pos, vol * 0.7f * g, CombatSounds.SizePitchOf(t));
            }
            else
            {
                var id = CombatSounds.SpellImpactOf(a, e.School) ?? Sfx.ImpactId(e.School);
                Sfx.Play(id, pos, vol * 0.85f, 1f);
                // a magic shot from a bow (Arcane Shot) still thunks into the target
                if (e.Ranged && a != null && AbilityRules.IsRangedWeaponAbility(a))
                    Sfx.Play(CombatSounds.AttackLayerOf(e.Source, false, true), pos, vol * 0.5f, 1f);
            }
            if (e.Blocked > 0f) Sfx.Play(CombatSounds.BlockOf(t), pos, 0.55f, 1.05f);
            if (e.Crit) Sfx.Play("hit_crit", pos, 0.5f, 1.15f);
            else if (kill) Sfx.Play("hit_crit", pos, 0.32f, 0.85f);
        }

        /// <summary>A damage-over-time tick: a soft squelch for bleeds, a quiet school impact otherwise.</summary>
        public static void Tick(CombatEvent e, Vector2 pos)
        {
            if (e == null) return;
            var id = CombatSounds.TickOf(AbilityOf(e.AbilityId), e.School) ?? Sfx.ImpactId(e.School);
            Sfx.Play(id, pos, e.School == School.Physical ? 0.35f : 0.4f, 1.1f);
        }

        /// <summary>Miss / Dodge / Evade (whoosh), Parry (clang), Block (wood or metal), Resist (fizzle), Immune (tink), Absorb (shimmer).</summary>
        public static void Avoid(CombatEvent e, Vector2 pos)
        {
            if (e == null) return;
            var id = CombatSounds.AvoidOf(e.Type, e.Target);
            if (id == null) return;
            float vol;
            switch (id)
            {
                case "parry": vol = 0.85f; break;
                case "block_wood":
                case "block_metal": vol = 0.8f; break;
                case "miss": vol = 0.6f; break;
                default: vol = 0.7f; break;
            }
            Sfx.Play(id, pos, vol, 1f);
        }

        // ------------------------------------------------------------------ spells

        /// <summary>
        /// The wind-up of a cast: cast_fire, cast_frost… (cast_lightning for Lightning-tagged spells, cast_start for
        /// physical). The swell peaks just before the release (≈ 0.4 s at 1×) and is sped up with the presentation.
        /// </summary>
        public static void CastWindup(AbilityDef a, School school, Vector2 pos, float volume = 1f)
        {
            Sfx.Play(CombatSounds.CastWindupOf(a, school), pos, volume * 0.85f, SpeedPitch);
        }

        /// <summary>
        /// The burst of an area or channelled spell as it goes off: the school impact, impact_lightning, a stomp for
        /// Thunder Clap-like slams, the horn for shouts; nothing for weapon areas such as Whirlwind (each hit sounds).
        /// </summary>
        public static void SpellImpact(AbilityDef a, School school, Vector2 pos, bool crit = false, int targets = 1)
        {
            var id = CombatSounds.AreaBurstOf(a, school);
            if (id == null) return;
            if (id == "shout_horn") { Shout(a, pos); return; }
            float vol = targets > 1 ? 0.95f : 0.85f;
            Sfx.Play(id, pos, vol, 1f);
            if (crit) Sfx.Play("hit_crit", pos, 0.5f, 1.2f);
        }

        /// <summary>A war cry: a brassy horn blast (Battle Shout, Demoralizing Shout, Piercing Howl…).</summary>
        public static void Shout(AbilityDef a, Vector2 pos)
        {
            Sfx.Play("shout_horn", pos, 0.8f, 1f);
        }

        // ------------------------------------------------------------------ deaths

        /// <summary>
        /// A death (or a party member going down): the vocal now (sized to the body: whelps squeal where Vyrmathra
        /// booms, big beasts roar, female bodies grunt higher), the body landing when the death animation lays it down
        /// (scaled time, so it keeps in step at any presentation speed), plus armour clatter for mail and plate and a
        /// rattle of bones for skeletons. Spirits fade instead of falling.
        /// </summary>
        public static void Death(Unit u, Vector2 pos, bool downed)
        {
            if (u == null) return;
            float pitch = CombatSounds.SizePitchOf(u);
            var vocal = CombatSounds.VocalOf(u);
            if (vocal != null) Sfx.Play(vocal, pos, CombatSounds.VocalVolumeOf(u, downed), CombatSounds.VocalPitchOf(u));
            float delay = downed ? CombatSounds.DownedFallDelay : CombatSounds.DeathFallDelay;
            var fall = CombatSounds.BodyFallOf(u);
            if (fall != null) Sfx.PlayAfter(fall, delay, pos, 0.85f, pitch);
            if (fall != null && CombatSounds.ClattersOf(u)) Sfx.PlayAfter("armor_clatter", delay + 0.04f, pos, 0.65f, 1f);
            if (CombatSounds.RattlesOf(u))
            {
                // two quick bone knocks (90 ms apart: clear of the 30 ms per-id rate limit)
                Sfx.PlayAfter("mat_bone", delay, pos, 0.8f, 0.95f);
                Sfx.PlayAfter("mat_bone", delay + 0.09f, pos, 0.55f, 1.12f);
            }
        }

        // ------------------------------------------------------------------ world

        static int steps;

        /// <summary>A footstep on the map's ground (dirt, leaves, stone, snow, mud…); mail and plate wearers jingle every second step.</summary>
        public static void Footstep(Unit u, Vector2 pos, MapDef map, float volume = 0.32f)
        {
            Sfx.Play(CombatSounds.FootstepOf(map), pos, volume, 1f);
            steps++;
            if ((steps & 1) == 0 && u != null && CombatSounds.IsMetal(CombatSounds.MaterialOf(u)))
                Sfx.Play("armor_jingle", pos, volume * 0.7f, 1f);
        }

        /// <summary>Receiving an item: a fanfare for Rare, Epic and Legendary, the plain pickup otherwise.</summary>
        public static void Loot(Quality q)
        {
            var id = CombatSounds.LootOf(q);
            if (id != null) Sfx.Play(id, null, 0.85f, 1f);
            else Sfx.Play("ui_open", null, 0.7f, 1.08f);
        }

        public static void Loot(ItemInstance item) => Loot(item != null && item.Def != null ? item.Def.quality : Quality.Common);

        /// <summary>A quest accepted (true = turned in): quest_accept / quest_turnin.</summary>
        public static void QuestCue(bool turnedIn) => Sfx.Play(turnedIn ? "quest_turnin" : "quest_accept");

        /// <summary>A hidden passage or secret was discovered.</summary>
        public static void SecretFound() => Sfx.Play("secret_found", null, 0.9f, 1f);

        /// <summary>The raid party stepped through the portal: a whoosh, then the raid warning horn.</summary>
        public static void RaidStarted()
        {
            Sfx.Play("portal_whoosh", null, 0.85f, 1f);
            Sfx.PlayAfter("raid_warning", 0.7f, null, 0.85f, 1f);
        }

        /// <summary>A battle begins: the boss pull drum when a Boss-rank enemy is in it.</summary>
        public static void BattleStart(Battle b)
        {
            if (b == null) return;
            foreach (var u in b.Units)
                if (u != null && u.Creature != null && u.Rank == CreatureRank.Boss && u.Team != b.PlayerTeam)
                {
                    Sfx.Play("boss_pull", null, 0.9f, 1f);
                    return;
                }
        }

        /// <summary>
        /// The sounds of session events whose GameFlow.React case plays none itself: RaidStarted (portal and warning; React
        /// only shows its toast) and the items builder's set-bonus Toast (Id "set_complete").
        /// </summary>
        public static void SessionCue(SessionEvent e)
        {
            if (e == null) return;
            if (e.Kind == SessionEventKind.RaidStarted) RaidStarted();
            else if (e.Kind == SessionEventKind.Toast && e.Id == "set_complete") Sfx.Play("set_complete", null, 0.85f, 1f);
        }
    }
}
