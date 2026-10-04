// "WeaponTypeTalent" talents (weapon specializations): stat bonuses and procs restricted to weapon types.
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public static class WeaponTalents
    {
        public const string SpecialName = "WeaponTypeTalent";

        static bool IsOneHandMelee(WeaponType w) =>
            w == WeaponType.Dagger || w == WeaponType.FistWeapon || w == WeaponType.OneHandAxe || w == WeaponType.OneHandMace || w == WeaponType.OneHandSword;
        static bool IsTwoHandMelee(WeaponType w) =>
            w == WeaponType.TwoHandAxe || w == WeaponType.TwoHandMace || w == WeaponType.TwoHandSword || w == WeaponType.Polearm || w == WeaponType.Staff;

        /// <summary>True when weapon type <paramref name="w"/> is one of the talent's tags (WeaponType names, "OneHanded", "TwoHanded").</summary>
        public static bool TypeMatches(PassiveDef p, WeaponType w)
        {
            if (w == WeaponType.None || p.tags == null) return false;
            foreach (var t in p.tags)
            {
                if (string.Equals(t, "OneHanded", StringComparison.OrdinalIgnoreCase)) { if (IsOneHandMelee(w)) return true; continue; }
                if (string.Equals(t, "TwoHanded", StringComparison.OrdinalIgnoreCase)) { if (IsTwoHandMelee(w)) return true; continue; }
                if (string.Equals(t, w.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Weapon type used by an attack: the hand for weapon attacks, else the main hand.</summary>
        public static WeaponType AttackWeapon(Unit u, bool offHand, bool ranged)
        {
            var it = ranged ? u.Equipment.Ranged : offHand ? u.Equipment.OffHand : u.Equipment.MainHand;
            if (it == null || !it.IsWeapon) it = u.Equipment.MainHand;
            return it != null && it.IsWeapon ? it.Def.weaponType : WeaponType.None;
        }

        public static bool AttackMatches(Unit owner, PassiveDef p, ProcInfo info) => TypeMatches(p, AttackWeapon(owner, info.OffHand, info.Ranged));

        /// <summary>Sum of WeaponTypeTalent stat bonuses of the unit for an attack with weapon type <paramref name="w"/>.</summary>
        public static float StatBonus(Unit u, WeaponType w, StatId stat)
        {
            if (u?.Db == null || u.Talents.Count == 0 || w == WeaponType.None) return 0f;
            float v = 0f;
            foreach (var kv in u.Talents)
            {
                if (kv.Value <= 0) continue;
                var t = u.Db.Talent(kv.Key);
                if (t == null) continue;
                foreach (var p in t.effects)
                {
                    if (p.type != "Special" || p.special != SpecialName || p.proc != null || p.stat != stat) continue;
                    if (!TypeMatches(p, w)) continue;
                    v += StatCalculator.RankValue(p.value, p.values, kv.Value);
                }
            }
            return v;
        }
    }

    /// <summary>Registered so the validator sees it; the logic lives in <see cref="WeaponTalents"/> and the proc system.</summary>
    sealed class WeaponTypeTalentSpecial : SpecialHandler
    {
        public WeaponTypeTalentSpecial() : base(WeaponTalents.SpecialName) { }
    }
}
