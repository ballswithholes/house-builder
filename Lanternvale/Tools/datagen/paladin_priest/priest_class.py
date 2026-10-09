# Priest: ClassDef, starter items, Lightwell creature, specials documentation.
from priest_talents import DEFAULT_BUILD

CLASS = {
    "id": "Priest", "name": "Priest",
    "description": "A devoted healer in layered robes and rings of light, equally able to mend the party and to "
                   "unmake foes with shadow. Priests command the strongest healing and the deepest mana pool.",
    "roles": ["Healer", "Ranged DPS", "Support"],
    "color": "#FFFFFF", "icon": "crest_priest",
    "resource": "Mana", "gcd": 1.5,
    "armorTypes": ["Cloth"], "armorUpgrade": {"level": 40, "type": "None"},
    "weaponTypes": ["OneHandMace", "Staff", "Dagger", "Wand"],
    "dualWieldLevel": 0, "canParry": False, "canBlock": False,
    "_statsNote": "Human priest, naked. Level 1: 20/20/20/22/23 -> 52 health, 160 mana (WoW Classic).",
    "baseStatsLevel1": {"strength": 20, "agility": 20, "stamina": 20, "intellect": 22, "spirit": 23},
    "baseStatsLevel60": {"strength": 37, "agility": 40, "stamina": 52, "intellect": 120, "spirit": 131},
    "baseHealthLevel1": 32, "baseHealthLevel60": 1397,
    "baseManaLevel1": 110, "baseManaLevel60": 1436,
    "meleeAp": "Strength", "rangedAp": "Strength",
    "agilityPerMeleeCritAt60": 20, "intellectPerSpellCritAt60": 59.5, "agilityPerDodgeAt60": 20,
    "baseMeleeCrit": 3, "baseSpellCrit": 0.8, "baseDodge": 3.2,
    "manaRegenBase": 12.5, "manaRegenPerSpirit": 0.25,
    "basicAttack": "attack",
    "startingAbilities": ["attack", "shoot", "priest_smite", "priest_lesser_heal"],
    "startingItems": ["priest_starter_mace", "priest_starter_robe", "priest_starter_pants", "priest_starter_boots"],
    "talentTrees": ["tree_priest_discipline", "tree_priest_holy", "tree_priest_shadow"],
    "defaultBuild": DEFAULT_BUILD,
    "sprite": "char_priest", "portrait": "portrait_priest",
    "designNotes": "The party's best healer: fast Flash Heal, efficient Heal/Greater Heal (3 sec casts that can run "
                   "into your next turn), Renew and Power Word: Shield (then Weakened Soul for 15 sec). Spirit drives "
                   "strong mana regeneration, so pace your casting around the five-second rule. Shadow priests trade "
                   "healing for Shadow Word: Pain, Mind Blast, Mind Flay and Shadowform, and everyone wields a wand "
                   "with Shoot when mana runs low.",
}


def item(id_, name, icon, desc, **kw):
    d = {"id": id_, "name": name, "icon": icon, "description": desc}
    d.update(kw)
    return d


ITEMS = [
    item("priest_starter_mace", "Worn Mace", "mace", "A plain acolyte's mace with a lantern-shaped head.",
         kind="Weapon", quality="Common", itemLevel=2, requiredLevel=1, equip="OneHand", weaponType="OneHandMace",
         minDamage=2, maxDamage=5, speed=2.0, price=30, classes=["Priest"]),
    item("priest_starter_robe", "Neophyte's Robe", "armor", "Layered white robes with a sky-blue sash.",
         kind="Armor", quality="Common", itemLevel=2, requiredLevel=1, equip="Chest", armorType="Cloth", armor=4,
         price=20, classes=["Priest"]),
    item("priest_starter_pants", "Neophyte's Hakama", "armor", "Pleated trousers, easy to kneel in.",
         kind="Armor", quality="Common", itemLevel=2, requiredLevel=1, equip="Legs", armorType="Cloth", armor=3,
         price=15, classes=["Priest"]),
    item("priest_starter_boots", "Neophyte's Sandals", "boot", "Woven sandals for temple steps.",
         kind="Armor", quality="Common", itemLevel=1, requiredLevel=1, equip="Feet", armorType="Cloth", armor=1,
         price=8, classes=["Priest"]),
]

CREATURES = [
    {"id": "priest_lightwell", "name": "Lightwell",
     "description": "A pillar of holy light summoned by a priest. Party members within 5 yards can touch it to be "
                   "renewed (5 charges).",
     "sprite": "prop_spirit_lantern", "type": "Spirit", "rank": "Totem", "levelMin": 1, "levelMax": 60,
     "scaleToParty": True, "healthMult": 0.5, "damageMult": 0, "ai": "Totem", "moveSpeed": 0, "size": 2.4,
     "xpMult": 0, "immune": ["Fear", "Stun", "Polymorph", "Sleep", "Confuse", "Incapacitate"]},
]

SPECIALS = [
    {"id": "PriestWeakenedSoulCheck", "usedBy": "priest_power_word_shield",
     "behaviour": "Cast-validation hook only (the listed effects then resolve normally): Power Word: Shield cannot be "
                  "cast on a target that has the aura priest_weakened_soul (from ANY priest). The UI greys out such "
                  "targets and the AI never selects them. The shield then applies priest_weakened_soul (15 sec) "
                  "itself. While priest_power_word_shield holds, the bearer's pending casts ignore pushback (see "
                  "PriestPushbackResist)."},
    {"id": "PriestFade", "usedBy": "priest_fade",
     "behaviour": "Effect magnitude F = min + perLevel x (effLevel - learnLevel) (55 at rank 1 ... 820 at rank 7). "
                  "For every enemy that has the caster on its threat table, reduce the caster's threat by F (not "
                  "below 0) and remember the amount actually removed on the caster's priest_fade aura. When "
                  "priest_fade is removed (expiry after 10 sec, death, or combat end), add the remembered amounts "
                  "back to those enemies that are still alive. Taunt-like target forcing is not affected."},
    {"id": "PriestManaBurn", "usedBy": "priest_mana_burn, priest_feedback",
     "behaviour": "Burn B = rand(min, max) + perLevel x (effLevel - learnLevel) mana from the effect target (capped at "
                  "its current mana; 0 if it has no mana). Then deal Shadow damage = B x amount / 100 to it (Mana "
                  "Burn: amount 50; Feedback: amount 100). Damage cannot crit and has no spell power coefficient; "
                  "Mana Burn's spell can miss (Shadow spell hit roll), the Feedback proc cannot. Threat = damage."},
    {"id": "PriestDispelRank", "usedBy": "priest_dispel_magic",
     "behaviour": "Modifier on the Dispel effect: dispelCount = number of the highest rank the caster knows (rank 1 "
                  "at level 18 removes 1 magic effect, rank 2 at level 36 removes 2)."},
    {"id": "PriestMindSoothe", "usedBy": "priest_mind_soothe",
     "behaviour": "Only meaningful out of combat: if the target belongs to an encounter that has not started, that "
                  "encounter's trigger radius is reduced by 4 m (10 yards, minimum 1 m) while priest_mind_soothe is "
                  "on any of its enemies (15 sec, real time). In combat the spell has no effect beyond the debuff."},
    {"id": "PriestMindControl", "usedBy": "priest_mind_control",
     "behaviour": "Target must be a Humanoid creature of level <= 33 / 45 / 57 (rank 1/2/3); otherwise the cast "
                  "fails before cost. On success the target joins the caster's team for the duration of "
                  "priest_mind_control (up to 60 sec): it takes its turns right after the priest, controlled by the "
                  "player (AI fallback), with 25% slower attacks; its threat tables are cleared. The priest is "
                  "channelling the whole time: on each of the priest's turns the priest may do nothing but end the "
                  "turn (moving or acting breaks the control), and the control ends immediately if the priest is "
                  "stunned, silenced, feared, incapacitated, interrupted or killed, or when the aura is dispelled or "
                  "expires. When it ends the creature returns to its team with full threat on the priest."},
    {"id": "PriestLightwell", "usedBy": "priest_lightwell, priest_lightwell_renew",
     "behaviour": "After the Summon effect creates priest_lightwell next to the caster (lifetime 180 sec), give it 5 "
                  "charges and remember the caster's Lightwell rank. While it exists, every living party member "
                  "within 5 yards of it may, on its own turn, use the contextual action priest_lightwell_renew "
                  "(OffGcd, no cost, self-target) if it is not already affected by priest_lightwell_renew; this "
                  "consumes one charge and applies the HoT using the CASTER'S Lightwell rank and HealingPower "
                  "(800/1100/1400 over 10 sec). Companion AI uses it below 70% health. At 0 charges or when the "
                  "lifetime ends the Lightwell despawns. Only one Lightwell per priest."},
    {"id": "PriestSpiritOfRedemption", "usedBy": "priest_spirit_of_redemption_passive",
     "behaviour": "When the priest's health would reach 0 (instead of being Downed): set health to 1, remove all "
                  "debuffs and apply priest_spirit_of_redemption (10 sec: Untargetable, Invulnerable, Root, Pacify, "
                  "healing spells cost no mana). In that form the priest keeps taking turns but may only use "
                  "abilities tagged Heal (and end turn). When the aura ends, the priest dies (Downed, can be helped "
                  "up/resurrected normally). Does not trigger if the priest is already in that form."},
    {"id": "PriestVampiricEmbrace", "usedBy": "priest_vampiric_embrace",
     "behaviour": "Whenever the CASTER of this debuff deals Shadow damage (direct or periodic, after mitigation) to "
                  "the bearer, every living ally of the caster within 30 yards (including the caster) is healed for "
                  "20% of that damage (x (1 + AbilityMod Effect % on priest_vampiric_embrace): Improved Vampiric "
                  "Embrace makes it 25%/30%). These heals do not crit and generate no threat."},
    {"id": "PriestFearWard", "usedBy": "priest_fear_ward",
     "behaviour": "When an aura with the Fear state would be applied to the bearer, it is not applied (shown as "
                  "'Immune') and priest_fear_ward is removed (1 charge)."},
    {"id": "PriestFocusedCasting", "usedBy": "priest_focused_casting",
     "behaviour": "While active (6 sec) the bearer's pending casts and channels ignore casting pushback (see "
                  "PriestPushbackResist) and the bearer has +10% (Martyrdom rank 1) / +20% (rank 2) chance to resist "
                  "Interrupt effects (the interrupt does nothing)."},
    {"id": "PriestPushbackResist", "usedBy": "priest_holy_healing_focus",
     "behaviour": "Casting pushback rule (shared with PaladinConcentrationAura): when a unit with a pending cast takes "
                  "damage, 0.5 sec is added to the remaining cast time (channels lose 0.5 sec instead), at most twice "
                  "per cast. Healing Focus: values[rank-1] = % chance that a pending cast of an ability tagged Heal "
                  "ignores a pushback. If the engine does not implement pushback this talent has no effect."},
    {"id": "PriestUnbreakableWill", "usedBy": "priest_discipline_unbreakable_will",
     "behaviour": "Talent passive: values[rank-1] = additional % chance to resist auras with the Stun, Fear or Silence "
                  "state when they would be applied to the priest."},
    {"id": "PriestSpiritualGuidance", "usedBy": "priest_holy_spiritual_guidance",
     "behaviour": "Talent passive: the priest gains SpellDamage (all schools) and HealingPower equal to "
                  "values[rank-1] % of current total Spirit (5/10/15/20/25%), recomputed whenever Spirit changes."},
    {"id": "PriestBlessedRecovery", "usedBy": "priest_holy_blessed_recovery, priest_blessed_recovery",
     "behaviour": "Proc effect when the priest takes a melee/ranged critical hit of D damage: apply (refresh) "
                  "priest_blessed_recovery storing a pool P = D x (8/16/25% for talent rank 1/2/3), added to any "
                  "remaining pool. The aura's 3 ticks (every 2 sec over 6 sec) each heal the priest for P/3 (the "
                  "tick Heal effect carries this special and reads the pool; no coefficient, cannot crit)."},
]
