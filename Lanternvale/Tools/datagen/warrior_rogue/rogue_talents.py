from common import *

A = "rogue_assassination_"
C = "rogue_combat_"
S = "rogue_subtlety_"

def used(abilities, effects):
    return proc("OnAbilityUsed", effects, abilities=abilities)

def cp(target=None):
    e = E("GainResource", resource="ComboPoints", amount=1)
    if target:
        e["target"] = target
    return e

def wtt(tags, **kw):
    return P_special("WeaponTypeTalent", tags=tags, **kw)

ASSASSINATION = {
    "id": "tree_rogue_assassination", "classId": "Rogue", "name": "Assassination", "icon": "poison",
    "description": "A deadly master of poisons who dispatches victims with vicious dagger strikes and lethal finishers.",
    "talents": [
        T(A + "improved_eviscerate", "Improved Eviscerate", "daggers", 1, 0, 3,
          "Increases the damage done by your Eviscerate ability by {5/10/15}%.",
          [P_mod("Damage", 5, abilities=["rogue_eviscerate"])]),
        T(A + "remorseless_attacks", "Remorseless Attacks", "skull", 1, 1, 2,
          "After killing an opponent that yields experience or honor, gives you a {20/40}% increased critical strike "
          "chance on your next Sinister Strike, Hemorrhage, Backstab, Ambush, or Ghostly Strike. Lasts 20 sec.",
          [P_proc(proc("OnKill", [E("ApplyAura", target="Self", aura="rogue_remorseless_attacks")]))]),
        T(A + "malice", "Malice", "dagger", 1, 2, 5, "Increases your critical strike chance by {1/2/3/4/5}%.",
          [P_stat("MeleeCrit", 1)]),
        T(A + "ruthlessness", "Ruthlessness", "fist", 2, 0, 3,
          "Gives your finishing moves a {20/40/60}% chance to add a combo point to your target.",
          [P_proc(proc("OnFinisher", [cp()]), values=[20, 40, 60])]),
        T(A + "murder", "Murder", "skull", 2, 1, 2,
          "Increases all damage caused against Humanoid, Giant, Beast and Dragonkin targets by {1/2}%.",
          [P_special("RogueMurder", value=1)]),
        T(A + "improved_slice_and_dice", "Improved Slice and Dice", "swords", 2, 3, 3,
          "Increases the duration of your Slice and Dice ability by {15/30/45}%.",
          [P_mod("DurationPct", 15, abilities=["rogue_slice_and_dice"])]),
        T(A + "relentless_strikes", "Relentless Strikes", "star", 3, 0, 1,
          "Your finishing moves have a 20% chance per combo point to restore 25 energy.",
          [P_special("RogueRelentlessStrikes")]),
        T(A + "improved_expose_armor", "Improved Expose Armor", "armor", 3, 1, 2,
          "Increases the armor reduced by your Expose Armor ability by {25/50}%.",
          [P_mod("Effect", values=[25, 50], abilities=["rogue_expose_armor"])]),
        T(A + "lethality", "Lethality", "dagger", 3, 2, 5,
          "Increases the critical strike damage bonus of your Sinister Strike, Gouge, Backstab, Ghostly Strike, and "
          "Hemorrhage abilities by {6/12/18/24/30}%.",
          [P_mod("CritBonus", 6, abilities=["rogue_sinister_strike", "rogue_gouge", "rogue_backstab",
                                            "rogue_ghostly_strike", "rogue_hemorrhage"])],
          requires=A + "malice"),
        T(A + "vile_poisons", "Vile Poisons", "poison", 4, 1, 5,
          "Increases the damage dealt by your poisons by {4/8/12/16/20}% and gives your poisons an additional "
          "{8/16/24/32/40}% chance to resist dispel effects.",
          [P_mod("Damage", 4, abilities=["rogue_apply_instant_poison", "rogue_apply_deadly_poison"],
                 schools=["Nature"])]),
        T(A + "improved_poisons", "Improved Poisons", "vial", 4, 2, 5,
          "Increases the chance to apply poisons to your target by {2/4/6/8/10}%.",
          [P_special("RogueImprovedPoisons", value=2)]),
        T(A + "cold_blood", "Cold Blood", "skull", 5, 1, 1, "Grants the Cold Blood ability.",
          [P_grant("rogue_cold_blood")]),
        T(A + "improved_kidney_shot", "Improved Kidney Shot", "fist", 5, 2, 3,
          "While affected by your Kidney Shot ability, the target receives an additional {3/6/9}% damage from all "
          "sources.",
          [P_special("RogueKidneyShot", value=3)]),
        T(A + "seal_fate", "Seal Fate", "star", 6, 1, 5,
          "Your critical strikes from abilities that add combo points have a {20/40/60/80/100}% chance to add an "
          "additional combo point.",
          [P_proc(proc("OnMeleeCrit", [cp()], tags=["Builder"]), values=[20, 40, 60, 80, 100])],
          requires=A + "cold_blood"),
        T(A + "vigor", "Vigor", "heart", 7, 1, 1, "Increases your maximum Energy by 10.",
          [P_special("RogueVigor", value=10)]),
    ],
}

COMBAT = {
    "id": "tree_rogue_combat", "classId": "Rogue", "name": "Combat", "icon": "swords",
    "description": "A swashbuckler who uses agility and guile to stand toe-to-toe with enemies.",
    "talents": [
        T(C + "improved_gouge", "Improved Gouge", "fist", 1, 0, 3,
          "Increases the effect duration of your Gouge ability by {0.5/1/1.5} sec.",
          [P_mod("Duration", 0.5, abilities=["rogue_gouge"])]),
        T(C + "improved_sinister_strike", "Improved Sinister Strike", "dagger", 1, 1, 2,
          "Reduces the Energy cost of your Sinister Strike ability by {3/5}.",
          [P_mod("CostFlat", values=[-3, -5], abilities=["rogue_sinister_strike"])]),
        T(C + "lightning_reflexes", "Lightning Reflexes", "feather", 1, 2, 5,
          "Increases your Dodge chance by {1/2/3/4/5}%.", [P_stat("Dodge", 1)]),
        T(C + "improved_backstab", "Improved Backstab", "dagger", 2, 0, 3,
          "Increases the critical strike chance of your Backstab ability by {10/20/30}%.",
          [P_mod("CritChance", 10, abilities=["rogue_backstab"])]),
        T(C + "deflection", "Deflection", "swords", 2, 1, 5, "Increases your Parry chance by {1/2/3/4/5}%.",
          [P_stat("Parry", 1)]),
        T(C + "precision", "Precision", "eye", 2, 2, 5,
          "Increases your chance to hit with melee weapons by {1/2/3/4/5}%.", [P_stat("MeleeHit", 1)]),
        T(C + "endurance", "Endurance", "boot", 3, 0, 2,
          "Reduces the cooldown of your Sprint and Evasion abilities by {45/90} sec.",
          [P_mod("Cooldown", -45, abilities=["rogue_sprint", "rogue_evasion"])]),
        T(C + "riposte", "Riposte", "swords", 3, 1, 1, "Grants the Riposte ability.", [P_grant("rogue_riposte")],
          requires=C + "deflection"),
        T(C + "improved_sprint", "Improved Sprint", "boot", 3, 3, 2,
          "Gives a {50/100}% chance to remove all movement impairing effects when you activate your Sprint ability.",
          [P_proc(used(["rogue_sprint"], [E("Special", target="Self", special="RogueRemoveSnares")]),
                  values=[50, 100])]),
        T(C + "improved_kick", "Improved Kick", "kick", 4, 0, 2,
          "Gives your Kick ability a {50/100}% chance to silence the target for 2 sec.",
          [P_proc(used(["rogue_kick"], [E("ApplyAura", aura="rogue_kick_silence")]), values=[50, 100])]),
        T(C + "dagger_specialization", "Dagger Specialization", "dagger", 4, 1, 5,
          "Increases your chance to get a critical strike with Daggers by {1/2/3/4/5}%.",
          [wtt(["Dagger"], stat="MeleeCrit", value=1)]),
        T(C + "dual_wield_specialization", "Dual Wield Specialization", "daggers", 4, 2, 5,
          "Increases the damage done by your offhand weapon by {10/20/30/40/50}%.",
          [P_special("OffHandDamage", value=10)], requires=C + "precision"),
        T(C + "mace_specialization", "Mace Specialization", "mace", 5, 0, 5,
          "Increases your skill with Maces by {1/2/3/4/5}, and gives you a {1/2/3/4/6}% chance to stun your target "
          "for 3 sec with a Mace.",
          [wtt(["OneHandMace"], values=[1, 2, 3, 4, 6],
               proc=proc("OnMeleeHit", [E("ApplyAura", aura="rogue_mace_stun")]))]),
        T(C + "blade_flurry", "Blade Flurry", "whirlwind", 5, 1, 1, "Grants the Blade Flurry ability.",
          [P_grant("rogue_blade_flurry")]),
        T(C + "sword_specialization", "Sword Specialization", "sword", 5, 2, 5,
          "Gives you a {1/2/3/4/5}% chance to get an extra attack on the same target after hitting your target with "
          "your Sword.",
          [wtt(["OneHandSword"], values=[1, 2, 3, 4, 5], proc=proc("OnMeleeHit", [E("WeaponDamage", weaponPct=100)]))]),
        T(C + "fist_weapon_specialization", "Fist Weapon Specialization", "fist", 5, 3, 5,
          "Increases your chance to get a critical strike with Fist Weapons by {1/2/3/4/5}%.",
          [wtt(["FistWeapon"], stat="MeleeCrit", value=1)]),
        T(C + "weapon_expertise", "Weapon Expertise", "swords", 6, 1, 2,
          "Increases your skill with Sword, Fist and Dagger weapons by {3/5} (here: +{0.6/1}% chance to hit with them).",
          [wtt(["OneHandSword", "FistWeapon", "Dagger"], stat="MeleeHit", values=[0.6, 1.0])],
          requires=C + "blade_flurry"),
        T(C + "aggression", "Aggression", "rage", 6, 2, 3,
          "Increases the damage of your Sinister Strike and Eviscerate abilities by {2/4/6}%.",
          [P_mod("Damage", 2, abilities=["rogue_sinister_strike", "rogue_eviscerate"])]),
        T(C + "adrenaline_rush", "Adrenaline Rush", "rage", 7, 1, 1, "Grants the Adrenaline Rush ability.",
          [P_grant("rogue_adrenaline_rush")]),
    ],
}

SUBTLETY = {
    "id": "tree_rogue_subtlety", "classId": "Rogue", "name": "Subtlety", "icon": "stealth",
    "description": "A shadowy operative who strikes from stealth and bends every fight to their own timing.",
    "talents": [
        T(S + "master_of_deception", "Master of Deception", "mask", 1, 1, 5,
          "Reduces the chance enemies have to detect you while in Stealth mode.",
          [P_special("RogueMasterOfDeception", value=1)]),
        T(S + "opportunity", "Opportunity", "dagger", 1, 2, 5,
          "Increases the damage dealt when striking from behind with your Backstab, Garrote, or Ambush abilities by "
          "{4/8/12/16/20}%.",
          [P_mod("Damage", 4, abilities=["rogue_backstab", "rogue_garrote", "rogue_ambush"])]),
        T(S + "sleight_of_hand", "Sleight of Hand", "hand", 2, 0, 2,
          "Reduces the chance you are critically hit by melee and ranged attacks by {1/2}% and increases the threat "
          "reduction of your Feint ability by {10/20}%.",
          [P_special("RogueSleightOfHand", value=1), P_mod("Threat", 10, abilities=["rogue_feint"])]),
        T(S + "elusiveness", "Elusiveness", "stealth", 2, 1, 2,
          "Reduces the cooldown of your Vanish and Blind abilities by {45/90} sec.",
          [P_mod("Cooldown", -45, abilities=["rogue_vanish", "rogue_blind"])]),
        T(S + "camouflage", "Camouflage", "leaf", 2, 2, 5,
          "Increases your speed while stealthed by {3/6/9/12/15}% and reduces the cooldown of your Stealth ability by "
          "{1/2/3/4/5} sec.",
          [P_special("RogueCamouflage", value=3), P_mod("Cooldown", -1, abilities=["rogue_stealth"])]),
        T(S + "initiative", "Initiative", "star", 3, 0, 3,
          "Gives you a {25/50/75}% chance to add an additional combo point to your target when using your Ambush, "
          "Garrote, or Cheap Shot ability.",
          [P_proc(used(["rogue_ambush", "rogue_garrote", "rogue_cheap_shot"], [cp()]), values=[25, 50, 75])]),
        T(S + "ghostly_strike", "Ghostly Strike", "dagger", 3, 1, 1, "Grants the Ghostly Strike ability.",
          [P_grant("rogue_ghostly_strike")]),
        T(S + "improved_ambush", "Improved Ambush", "daggers", 3, 2, 3,
          "Increases the critical strike chance of your Ambush ability by {15/30/45}%.",
          [P_mod("CritChance", 15, abilities=["rogue_ambush"])]),
        T(S + "setup", "Setup", "feather", 4, 0, 3,
          "Gives you a {15/30/45}% chance to add a combo point to your target after dodging their attack.",
          [P_proc(proc("OnDodge", [cp("Attacker")]), values=[15, 30, 45])]),
        T(S + "improved_sap", "Improved Sap", "mace", 4, 1, 3,
          "Gives you a {30/60/90}% chance to return to stealth mode after using your Sap ability.",
          [P_proc(used(["rogue_sap"], [E("ApplyAura", target="Self", aura="rogue_stealth_aura")]),
                  values=[30, 60, 90])]),
        T(S + "serrated_blades", "Serrated Blades", "dagger", 4, 2, 3,
          "Causes your attacks to ignore armor (about {2.7/5.4/8} per level) and increases the damage dealt by your "
          "Rupture ability by {10/20/30}%. The amount of Armor reduced increases with your level.",
          [P_special("RogueSerratedBlades", values=[2.67, 5.43, 8.0]),
           P_mod("Damage", 10, abilities=["rogue_rupture"])]),
        T(S + "heightened_senses", "Heightened Senses", "eye", 5, 0, 2,
          "Increases your Stealth detection and reduces the chance you are hit by spells and ranged attacks by "
          "{2/4}%.",
          [P_stat("StealthDetection", 5), P_stat("ChanceToBeHit", -2)]),
        T(S + "preparation", "Preparation", "hourglass", 5, 1, 1, "Grants the Preparation ability.",
          [P_grant("rogue_preparation")]),
        T(S + "hemorrhage", "Hemorrhage", "blood", 5, 2, 1, "Grants the Hemorrhage ability.",
          [P_grant("rogue_hemorrhage")], requires=S + "serrated_blades"),
        T(S + "dirty_deeds", "Dirty Deeds", "coin", 5, 3, 2,
          "Reduces the Energy cost of your Cheap Shot and Garrote abilities by {10/20}.",
          [P_mod("CostFlat", -10, abilities=["rogue_cheap_shot", "rogue_garrote"])]),
        T(S + "deadliness", "Deadliness", "skull", 6, 2, 5, "Increases your attack power by {2/4/6/8/10}%.",
          [P_stat("AttackPower", 2, pct=True)]),
        T(S + "premeditation", "Premeditation", "eye", 7, 1, 1, "Grants the Premeditation ability.",
          [P_grant("rogue_premeditation")], requires=S + "preparation"),
    ],
}

TREES = [ASSASSINATION, COMBAT, SUBTLETY]

BUILD = (
    [C + "improved_sinister_strike"] * 2 + [C + "lightning_reflexes"] * 5 + [C + "precision"] * 5 +
    [C + "endurance"] * 2 + [C + "improved_gouge"] + [C + "dual_wield_specialization"] * 5 + [C + "blade_flurry"] +
    [C + "sword_specialization"] * 5 + [C + "weapon_expertise"] * 2 + [C + "aggression"] * 2 +
    [C + "adrenaline_rush"] +
    [A + "improved_eviscerate"] * 3 + [A + "malice"] * 5 + [A + "ruthlessness"] * 3 + [A + "relentless_strikes"] +
    [A + "lethality"] * 5 + [A + "improved_slice_and_dice"] * 3
)
