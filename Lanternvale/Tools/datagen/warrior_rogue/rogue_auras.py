from common import *
from rogue_abilities import CHARGES

def debuff(id_, name, icon, desc, duration, **kw):
    return aura(id_, name, icon, desc, kind="Debuff", duration=duration, **kw)

def buff(id_, name, icon, desc, duration, **kw):
    return aura(id_, name, icon, desc, kind="Buff", duration=duration, **kw)

def coating(key, name, group, chance, effects):
    return buff(f"rogue_{key}_poison_coating", name, "vial",
                f"Weapons coated with {name}: each melee hit has a {chance}% chance to poison the target.", 1800,
                school="Nature", charges=CHARGES[key], exclusiveGroup=group, tags=["Poison", "WeaponCoating"],
                procs=[proc("OnMeleeHit", effects, chance=chance, consumeCharge=True)])

REMORSELESS_ABILITIES = ["rogue_sinister_strike", "rogue_hemorrhage", "rogue_backstab", "rogue_ambush",
                         "rogue_ghostly_strike"]

AURAS = [
    buff("rogue_stealth_aura", "Stealth", "stealth", "Stealthed. Movement speed reduced by 30%.", 0,
         states=["Stealth"], breakOnAction=True, tags=["Stealth"], mods=[mod("MoveSpeed", -30, pct=True)]),
    buff("rogue_vanish_aura", "Vanish", "stealth", "Vanished: improved stealth for 10 sec.", 10,
         states=["Stealth"], breakOnAction=True, tags=["Stealth"]),
    buff("rogue_threat_aura", "Shadowed Presence", "mask", "Threat generated reduced by 29%.", 0, hidden=True,
         persistThroughDeath=True, mods=[mod("ThreatGenerated", -29, pct=True)]),
    debuff("rogue_gouge", "Gouge", "fist", "Incapacitated. Any damage will revive the target.", 4,
           states=["Incapacitate"], breakOnDamage=True),
    buff("rogue_evasion", "Evasion", "feather", "Dodge chance increased by 50%.", 15, mods=[mod("Dodge", 50)]),
    debuff("rogue_sap", "Sap", "mace", "Incapacitated. Any damage will revive the target.", 25,
           states=["Incapacitate"], breakOnDamage=True, special="RankDurations", tags=["Durations:25/35/45"]),
    buff("rogue_slice_and_dice", "Slice and Dice", "swords", "Melee attack speed increased.", 6,
         mods=[vmod("MeleeHaste", [20, 30], pct=True)]),
    buff("rogue_sprint", "Sprint", "boot", "Movement speed increased.", 15,
         mods=[vmod("MoveSpeed", [50, 70, 100], pct=True)]),
    debuff("rogue_garrote", "Garrote", "blood", "Bleeding for Physical damage every 3 sec.", 18, tags=["Bleed"],
           tickInterval=3, tickEffects=[E("Damage", min=24, perLevel=1.7, apCoef=0.03)]),
    debuff("rogue_expose_armor", "Expose Armor", "armor", "Armor reduced.", 30, special="RogueExposeArmor",
           mods=[vmod("Armor", [-80, -145, -210, -275, -340])]),
    debuff("rogue_rupture", "Rupture", "blood", "Bleeding for Physical damage every 2 sec.", 6, tags=["Bleed"],
           tickInterval=2, tickEffects=[E("Damage", min=8, perLevel=1.2, perCombo=2, amount=0.3)]),
    debuff("rogue_distract", "Distracted", "coin", "Distracted: looking away from the rogue.", 10,
           breakOnDamage=True, special="RogueDistracted"),
    debuff("rogue_cheap_shot", "Cheap Shot", "fist", "Stunned.", 4, states=["Stun"], tags=["Stun"]),
    debuff("rogue_kidney_shot", "Kidney Shot", "fist", "Stunned.", 1, states=["Stun"], tags=["Stun"],
           special="RogueKidneyShot", mods=[mod("DamageTaken", 0, pct=True)]),
    debuff("rogue_blind", "Blind", "eye", "Disoriented. Any damage will remove the effect.", 10,
           states=["Confuse"], breakOnDamage=True),
    debuff("rogue_kick_silence", "Kick - Silenced", "kick", "Silenced.", 2, states=["Silence"]),
    debuff("rogue_mace_stun", "Mace Stun Effect", "mace", "Stunned.", 3, states=["Stun"], tags=["Stun"]),
    debuff("rogue_riposte_disarm", "Riposte", "swords", "Disarmed.", 6, states=["Disarm"]),
    buff("rogue_blade_flurry", "Blade Flurry", "whirlwind",
         "Attack speed increased by 20%. Attacks strike an additional nearby opponent.", 15,
         special="SweepingHits", mods=[mod("MeleeHaste", 20, pct=True)]),
    buff("rogue_adrenaline_rush", "Adrenaline Rush", "rage", "Energy regeneration increased by 100%.", 15,
         mods=[mod("EnergyRegen", 100, pct=True)]),
    buff("rogue_ghostly_strike", "Ghostly Strike", "dagger", "Dodge chance increased by 15%.", 7,
         mods=[mod("Dodge", 15)]),
    debuff("rogue_hemorrhage", "Hemorrhage", "blood", "Physical damage taken increased (30 hits).", 15, charges=30,
           mods=[mod("DamageTaken", 3, pct=True, school="Physical")],
           procs=[proc("OnStruck", [], schools=["Physical"], consumeCharge=True)]),
    buff("rogue_cold_blood", "Cold Blood", "skull", "Your next offensive ability will be a critical strike.", 0,
         charges=1, mods=[mod("MeleeCrit", 100)],
         procs=[proc("OnAbilityUsed", [], tags=["Builder", "Finisher", "Opener"], consumeCharge=True)]),
    buff("rogue_remorseless_attacks", "Remorseless", "skull",
         "Critical strike chance of your next Sinister Strike, Hemorrhage, Backstab, Ambush or Ghostly Strike increased.",
         20, charges=1, mods=[vmod("MeleeCrit", [20, 40])],
         procs=[proc("OnAbilityUsed", [], abilities=REMORSELESS_ABILITIES, consumeCharge=True)]),

    # poisons
    coating("instant", "Instant Poison", "rogue_poison_lethal", 20,
            [E("Damage", school="Nature", min=19, max=25, perLevel=2.7)]),
    coating("deadly", "Deadly Poison", "rogue_poison_lethal", 30, [E("ApplyAura", aura="rogue_deadly_poison")]),
    coating("wound", "Wound Poison", "rogue_poison_lethal", 30, [E("ApplyAura", aura="rogue_wound_poison")]),
    coating("crippling", "Crippling Poison", "rogue_poison_utility", 30,
            [E("ApplyAura", aura="rogue_crippling_poison")]),
    coating("mind_numbing", "Mind-numbing Poison", "rogue_poison_utility", 20,
            [E("ApplyAura", aura="rogue_mind_numbing_poison")]),
    debuff("rogue_deadly_poison", "Deadly Poison", "poison", "Suffering Nature damage every 3 sec.", 12,
           school="Nature", dispel="Poison", maxStacks=5, tags=["Poison"], tickInterval=3,
           tickEffects=[E("Damage", school="Nature", min=9, perLevel=0.75)]),
    debuff("rogue_wound_poison", "Wound Poison", "poison", "Healing received reduced.", 15, school="Nature",
           dispel="Poison", maxStacks=5, tags=["Poison"],
           mods=[{"stat": "HealingTaken", "value": -2, "perLevel": -0.125, "pct": True}]),
    debuff("rogue_crippling_poison", "Crippling Poison", "poison", "Movement speed reduced.", 12, school="Nature",
           dispel="Poison", tags=["Poison", "Snare"],
           mods=[{"stat": "MoveSpeed", "value": -50, "perLevel": -0.5, "pct": True}]),
    debuff("rogue_mind_numbing_poison", "Mind-numbing Poison", "poison", "Casting time increased.", 10,
           school="Nature", dispel="Poison", tags=["Poison"],
           mods=[{"stat": "CastSpeed", "value": -40, "perLevel": -0.625, "pct": True}]),
]
