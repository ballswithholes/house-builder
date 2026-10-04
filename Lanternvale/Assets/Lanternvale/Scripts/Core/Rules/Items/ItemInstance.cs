// A concrete item: a database ItemDef (or a generated def) plus instance data (stack count, random suffix).
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public sealed class ItemInstance
    {
        static long nextUid = 1;

        /// <summary>Session-unique id (for UI selection; not stable across saves).</summary>
        public readonly long Uid;
        public ItemDef Def;
        /// <summary>True when <see cref="Def"/> was generated at runtime (veteran gear, random loot) and is not in the database.</summary>
        public bool Generated;
        /// <summary>Random suffix id ("of the Bear") or "".</summary>
        public string SuffixId = "";
        public string SuffixName = "";
        /// <summary>Stats rolled for the suffix (added to Def.stats).</summary>
        public readonly List<StatModDef> SuffixStats = new List<StatModDef>();
        public int Count = 1;

        public ItemInstance(ItemDef def, int count = 1)
        {
            Uid = nextUid++;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Count = Math.Max(1, count);
        }

        public string Id => Def.id;
        public string Name => string.IsNullOrEmpty(SuffixName) ? Def.name : Def.name + " " + SuffixName;
        public int MaxStack => Math.Max(1, Def.stack);
        public bool IsEquipable => Def.equip != EquipType.None;
        public bool IsWeapon => Def.kind == ItemKind.Weapon && Def.weaponType != WeaponType.Shield && Def.weaponType != WeaponType.HeldInOffhand;
        public Quality Quality => Def.quality;

        /// <summary>Item stats including the random suffix.</summary>
        public IEnumerable<StatModDef> Stats
        {
            get
            {
                foreach (var s in Def.stats) yield return s;
                foreach (var s in SuffixStats) yield return s;
            }
        }

        /// <summary>True when this instance can merge into the same stack as <paramref name="o"/>.</summary>
        public bool CanStackWith(ItemInstance o) =>
            o != null && o.Def.id == Def.id && !Generated && !o.Generated && SuffixId == o.SuffixId && MaxStack > 1;

        public ItemInstance CloneOne()
        {
            var c = new ItemInstance(Def, 1) { Generated = Generated, SuffixId = SuffixId, SuffixName = SuffixName };
            c.SuffixStats.AddRange(SuffixStats);
            return c;
        }

        /// <summary>Copper value when sold to a vendor (1/4 of the buy price).</summary>
        public int SellPrice => Math.Max(0, Def.price / 4);

        public override string ToString() => Count > 1 ? $"{Name} x{Count}" : Name;
    }
}
