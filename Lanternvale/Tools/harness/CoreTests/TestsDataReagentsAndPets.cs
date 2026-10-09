// Content reachability tests: the reagents the rules consume are sold (and nothing sells a look-alike reagent no
// rule uses), and every tameable beast placed in the world tames into a pet template of its own family.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsDataReagentsAndPets
    {
        // Reagents consumed by class specials (Specials.Shaman/Rogue/Warlock). soul_shard is made by Drain Soul, not sold.
        static readonly string[] SoldReagents = { "shaman_ankh", "rogue_flash_powder", "rogue_blinding_powder" };
        static readonly HashSet<string> ConsumedReagents = new HashSet<string>(SoldReagents) { "soul_shard" };

        static VendorShop ShopSelling(string itemId)
        {
            foreach (var n in Db.Npcs.Values.OrderBy(x => x.id, StringComparer.Ordinal))
                if (n.vendor.Any(v => v.item == itemId)) return new VendorShop(Db, n, null, null);
            return null;
        }

        [Test]
        public static void ConsumedReagentsAreSoldAndSoldReagentsAreConsumed()
        {
            foreach (var id in SoldReagents)
            {
                Assert(Db.Item(id) != null, $"reagent {id} is defined");
                Assert(ShopSelling(id) != null, $"some vendor sells {id}");
            }
            foreach (var n in Db.Npcs.Values)
                foreach (var v in n.vendor)
                {
                    var def = Db.Item(v.item);
                    if (def == null || def.kind != ItemKind.Reagent) continue;
                    Assert(ConsumedReagents.Contains(def.id), $"{n.id} sells reagent '{def.id}' ({def.name}) that no ability consumes");
                }
            foreach (var g in Db.Items.Values.Where(i => i.kind == ItemKind.Reagent).GroupBy(i => i.name))
                Assert(g.Count() == 1, $"reagents share the name '{g.Key}': {string.Join(", ", g.Select(i => i.id))}");
        }

        [Test]
        public static void BoughtAnkhPowersReincarnation()
        {
            var shop = ShopSelling("shaman_ankh");
            Assert(shop != null, "a vendor sells the Ankh");
            var inv = new Inventory();
            inv.AddGold(1000000);
            var offer = shop.Offers().FirstOrDefault(o => o.Item.name == "Ankh");
            Assert(offer != null && offer.Item.id == "shaman_ankh", "the 'Ankh' on sale is the one Reincarnation uses: " + offer?.Item.id);
            Assert(shop.Buy(inv, offer.Item.id) == null, "bought an Ankh");

            var b = NewBattle(7, inv);
            var sham = Hero(ClassId.Shaman, 40, name: "Sham").At(10, 10);
            var other = Hero(ClassId.Warrior, 40, name: "War").At(12, 10);
            var foe = Mob("cr_bandit_chief", 40).At(14, 10).Tough();
            b.AddUnit(sham); b.AddUnit(other); b.AddUnit(foe);
            b.Begin();
            b.DealDamage(foe, sham, sham.Health + 10f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(sham.Downed && sham.SelfRes != null && sham.SelfRes.Source == "shaman_reincarnation", "reincarnation offered with a bought Ankh");
        }

        [Test]
        public static void BoughtFlashPowderPowersVanish()
        {
            var shop = ShopSelling("rogue_flash_powder");
            Assert(shop != null, "a vendor sells Flash Powder");
            int offers = 0;
            // every "Flash Powder" a shop sells must be the one Vanish accepts
            foreach (var n in Db.Npcs.Values)
                foreach (var o in new VendorShop(Db, n, null, null).Offers().Where(o => o.Item.name == "Flash Powder"))
                {
                    offers++;
                    var inv = new Inventory();
                    inv.AddGold(1000000);
                    Assert(new VendorShop(Db, n, null, null).Buy(inv, o.Item.id) == null, $"bought {o.Item.id} from {n.id}");

                    var b = NewBattle(5, inv);
                    var r = Hero(ClassId.Rogue, 30).At(20f, 20f);
                    r.AutoPlay = false;
                    var foe = Mob("cr_bandit_cutthroat", 27).At(22f, 20f).Tough();
                    b.AddUnit(r); b.AddUnit(foe);
                    b.Begin();
                    SkipTo(b, r);
                    r.TimeLeft = 6f; r.TimeDebt = 0f; r.Cooldowns.Clear();
                    r.Energy = r.MaxResource(ResourceType.Energy);
                    var chk = b.CanUse(r, "rogue_vanish", r);
                    Assert(chk.Ok, $"Vanish usable with the Flash Powder ({o.Item.id}) sold by {n.id}: {chk.Reason}");
                }
            Assert(offers > 0, "Flash Powder is on sale");
        }

        [Test]
        public static void PlacedTameableBeastsTameIntoTheirOwnFamily()
        {
            var placed = new HashSet<string>();
            foreach (var m in Db.Maps.Values) foreach (var e in m.encounters) foreach (var en in e.enemies) placed.Add(en.creature);
            var families = new HashSet<string>();
            foreach (var cr in Db.Creatures.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (!cr.tameable || cr.type != CreatureType.Beast || !placed.Contains(cr.id)) continue;
                var template = Db.Creature("hunter_pet_" + (cr.family ?? "").ToLowerInvariant());
                Assert(template != null && template.rank == CreatureRank.Pet && template.family == cr.family,
                    $"tameable {cr.id} (family '{cr.family}') has a matching hunter_pet_ template");
                families.Add(cr.family);

                var b = NewBattle(3, new Inventory());
                var h = Hero(ClassId.Hunter, 20).At(20f, 20f);
                h.AutoPlay = false;
                var beast = Mob(cr.id, 18).At(26f, 22f);
                b.AddUnit(h); b.AddUnit(beast);
                b.Begin();
                if (h.Pet != null) b.DismissPet(h);
                var a = Db.Ability("hunter_tame_beast");
                var c = b.NewCast(h, a, 1, beast, null, null);
                Specials.Get("HunterTameBeast").Execute(c, a.effects[0], beast);
                Assert(h.Pet != null && h.HunterPet.TemplateId == template.id, $"taming {cr.id} gives {template.id} (got {h.HunterPet?.TemplateId})");
                Assert(h.Pet.Sprite == template.sprite && h.Pet.Name == beast.Name, $"{cr.id}: pet looks like a {cr.family} ({h.Pet.Sprite})");
            }
            Assert(families.Count >= 3, "at least three pet families can be tamed in the world: " + string.Join(", ", families));
        }
    }
}
