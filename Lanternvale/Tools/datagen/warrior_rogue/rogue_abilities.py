from common import *

STEALTH = ["Stealth"]

def energy(n, combo=False):
    c = {"type": "Energy", "amount": n}
    if combo:
        c["consumesComboPoints"] = True
    return c

def finisher_req(**kw):
    r = {"minComboPoints": 1}
    r.update(kw)
    return r

POISONS = [
    # id, name, learn level, vial price
    ("instant", "Instant Poison", 20, 60),
    ("crippling", "Crippling Poison", 20, 60),
    ("mind_numbing", "Mind-numbing Poison", 24, 80),
    ("deadly", "Deadly Poison", 30, 120),
    ("wound", "Wound Poison", 32, 140),
]
POISON_TEXT = {
    "instant": "Each strike has a 20% chance of poisoning the enemy for 19 to 25 Nature damage (112 to 148 at level 60).",
    "crippling": "Each strike has a 30% chance of poisoning the enemy, slowing its movement speed by 50% (70% at level 60) for 12 sec.",
    "mind_numbing": "Each strike has a 20% chance of poisoning the enemy, increasing its casting time by 40% (up to 60%) for 10 sec.",
    "deadly": "Each strike has a 30% chance of poisoning the enemy for 36 Nature damage over 12 sec (136 at level 60). Stacks up to 5 times.",
    "wound": "Each strike has a 30% chance of poisoning the enemy, reducing all healing it receives (2% per application, about 5% at level 56). Stacks up to 5 times. Lasts 15 sec.",
}
CHARGES = {"instant": 40, "crippling": 20, "mind_numbing": 50, "deadly": 60, "wound": 60}

def poison_abilities():
    out = []
    for key, name, lvl, _ in POISONS:
        out.append(ab(f"rogue_{key}_poison", name, "vial",
                      f"Prepares 5 vials of {name} from the rogue's kit (out of combat). Using a vial coats your weapons "
                      f"for 30 min ({CHARGES[key]} charges). {POISON_TEXT[key]} Potency grows with your level "
                      f"(WoW ranks I-VI).",
                      ranks=[lvl], castTime=3, target="Self", breaksStealth=False, tags=["Poison"],
                      requires={"notInCombat": True},
                      effects=[E("CreateItem", target="Self", item=f"rogue_{key}_poison_vial", count=5)],
                      aiHint="Utility", aiPriority=0))
        out.append(ab(f"rogue_apply_{key}_poison", f"Apply {name}", "vial",
                      f"Coats your weapons with {name} for 30 min ({CHARGES[key]} charges). {POISON_TEXT[key]} Only one "
                      f"damaging (Instant/Deadly/Wound) and one utility (Crippling/Mind-numbing) poison can be active.",
                      ranks=[lvl], hidden=True, scaleWithLevel=True, castTime=3, target="Self", breaksStealth=False,
                      tags=["Poison"], effects=[E("ApplyAura", target="Self", aura=f"rogue_{key}_poison_coating")],
                      aiHint="Buff", aiPriority=0))
    return out

PREP_RESET = ["rogue_evasion", "rogue_sprint", "rogue_vanish", "rogue_blind", "rogue_gouge", "rogue_kick", "rogue_feint",
              "rogue_kidney_shot", "rogue_distract", "rogue_stealth", "rogue_riposte", "rogue_ghostly_strike",
              "rogue_cold_blood", "rogue_blade_flurry", "rogue_adrenaline_rush", "rogue_premeditation"]

ABILITIES = [
    ab("rogue_sinister_strike", "Sinister Strike", "dagger",
       "An instant strike that causes 3 damage (68 at rank 8) in addition to your normal weapon damage: {0}. Awards 1 "
       "combo point.",
       ranks=[1, 6, 14, 22, 30, 38, 46, 54], cost=energy(45), melee=True, generatesComboPoint=True, tags=["Builder"],
       effects=[E("WeaponDamage", weaponPct=100, min=3, perLevel=1.226)], aiHint="Damage", aiPriority=6),
    ab("rogue_eviscerate", "Eviscerate", "daggers",
       "Finishing move that causes damage per combo point: 1 point 6-10, 2 points 11-15, 3 points 16-20, 4 points "
       "21-25, 5 points 26-30 (rank 8: 199-295 ... 787-883).",
       ranks=[1, 8, 16, 24, 32, 40, 48, 56], cost=energy(35, combo=True), melee=True, requires=finisher_req(),
       tags=["Finisher"], special="RoguePerComboByRank",
       effects=[E("Damage", min=1, max=5, perLevel=1.76, perCombo=5, amount=2.582)], aiHint="Finisher", aiPriority=8),
    ab("rogue_stealth", "Stealth", "stealth",
       "Allows the rogue to sneak around, but reduces your speed by 30%. Lasts until cancelled. Attacking from Stealth "
       "with an opener starts the battle with a surprise round.",
       ranks=[1, 10, 20, 30], time="OffGcd", cooldown=10, target="Self", breaksStealth=False,
       requires={"notInCombat": True}, effects=[E("ApplyAura", target="Self", aura="rogue_stealth_aura")],
       aiHint="Utility", aiPriority=2),
    ab("rogue_backstab", "Backstab", "dagger",
       "Backstab the target, causing 150% weapon damage plus 15 (210 at rank 8): {0}. Must be behind the target. "
       "Requires a dagger in the main hand. Awards 1 combo point.",
       ranks=[4, 12, 20, 28, 36, 44, 52, 60], cost=energy(60), melee=True, generatesComboPoint=True, tags=["Builder"],
       requires={"behindTarget": True, "mainHand": ["Dagger"]},
       effects=[E("WeaponDamage", weaponPct=150, min=15, perLevel=3.48)], aiHint="Damage", aiPriority=7),
    ab("rogue_gouge", "Gouge", "fist",
       "Causes {0} damage, incapacitating the opponent for 4 sec, and turns off your attack. Target must be facing "
       "you. Any damage caused will revive the target. Awards 1 combo point.",
       ranks=[6, 18, 32, 46, 60], cost=energy(45), cooldown=10, melee=True, generatesComboPoint=True,
       tags=["Builder"], effects=[E("Damage", min=10, perLevel=1.2), E("ApplyAura", aura="rogue_gouge")],
       aiHint="CC", aiPriority=5),
    ab("rogue_evasion", "Evasion", "feather",
       "The rogue's dodge chance will increase by 50% for 15 sec.",
       ranks=[8], cooldown=300, target="Self", breaksStealth=False,
       effects=[E("ApplyAura", target="Self", aura="rogue_evasion")], aiHint="Defensive", aiPriority=5),
    ab("rogue_sap", "Sap", "mace",
       "Incapacitates the target for 25 sec (35/45 sec at ranks 2-3). Must be stealthed. Only works on Humanoids that "
       "are not in combat. Any damage caused will revive the target. Only 1 target may be sapped at a time.",
       ranks=[10, 28, 48], cost=energy(65), melee=True, tags=["Opener"],
       requires={"casterStates": STEALTH, "targetCreatureTypes": ["Humanoid"]},
       effects=[E("ApplyAura", aura="rogue_sap")], aiHint="CC", aiPriority=4),
    ab("rogue_slice_and_dice", "Slice and Dice", "swords",
       "Finishing move that increases melee attack speed by {0}% (30% at rank 2). Lasts longer per combo point: "
       "1 point 9 sec, 2 points 12 sec, 3 points 15 sec, 4 points 18 sec, 5 points 21 sec.",
       ranks=[10, 42], cost=energy(25, combo=True), range=30, requires=finisher_req(), tags=["Finisher"],
       effects=[E("ApplyAura", target="Self", aura="rogue_slice_and_dice", durationPerCombo=3)],
       aiHint="Buff", aiPriority=7),
    ab("rogue_sprint", "Sprint", "boot",
       "Increases the rogue's movement speed by {0}% for 15 sec (70%/100% at ranks 2-3). Does not break stealth.",
       ranks=[10, 34, 58], cooldown=300, target="Self", breaksStealth=False,
       effects=[E("ApplyAura", target="Self", aura="rogue_sprint")], aiHint="Utility", aiPriority=3),
    ab("rogue_kick", "Kick", "kick",
       "A quick kick that injures a single foe for {0} damage. It also interrupts spellcasting and prevents any spell "
       "in that school from being cast for 5 sec.",
       ranks=[12, 26, 42, 58], cost=energy(25), cooldown=10, melee=True, tags=["Interrupt"],
       effects=[E("Damage", min=15, perLevel=1.41), E("Interrupt", lockout=5)], aiHint="Interrupt", aiPriority=9),
    ab("rogue_garrote", "Garrote", "blood",
       "Garrote the enemy, causing {0} damage over 18 sec (rank 6: 552). Must be stealthed and behind the target. "
       "Awards 1 combo point.",
       ranks=[14, 22, 30, 38, 46, 54], cost=energy(50), melee=True, generatesComboPoint=True,
       tags=["Opener", "Builder", "Bleed"], requires={"casterStates": STEALTH, "behindTarget": True},
       effects=[E("ApplyAura", aura="rogue_garrote")], aiHint="Opener", aiPriority=7),
    ab("rogue_expose_armor", "Expose Armor", "armor",
       "Finishing move that exposes the target, reducing armor per combo point: 80 per point at rank 1 (340 per "
       "point, 1700 at 5 points, at rank 5). Lasts 30 sec.",
       ranks=[14, 26, 36, 46, 56], cost=energy(25, combo=True), melee=True, requires=finisher_req(),
       tags=["Finisher"], effects=[E("ApplyAura", aura="rogue_expose_armor")], aiHint="Debuff", aiPriority=4),
    ab("rogue_feint", "Feint", "mask",
       "Performs a feint, causing no damage but lowering your threat by a large amount (600 at rank 4), making the "
       "enemy less likely to attack you.",
       ranks=[16, 28, 40, 52], cost=energy(20), cooldown=10, melee=True,
       effects=[E("Threat", threat=-600)], aiHint="Utility", aiPriority=4),
    ab("rogue_pick_lock", "Pick Lock", "key",
       "Allows opening of locked chests and doors that require a Sleight of Hand check. 5 sec cast, out of combat.",
       ranks=[16], castTime=5, target="Self", breaksStealth=False, requires={"notInCombat": True},
       effects=[E("Special", target="Self", special="RoguePickLock")], aiHint="Utility", aiPriority=0),
    ab("rogue_ambush", "Ambush", "daggers",
       "Ambush the target, causing 250% weapon damage plus 70 (290 at rank 6): {0}. Must be stealthed and behind the "
       "target. Requires a dagger in the main hand. Awards 1 combo point.",
       ranks=[18, 26, 34, 42, 50, 58], cost=energy(60), melee=True, generatesComboPoint=True,
       tags=["Opener", "Builder"], requires={"casterStates": STEALTH, "behindTarget": True, "mainHand": ["Dagger"]},
       effects=[E("WeaponDamage", weaponPct=250, min=70, perLevel=5.5)], aiHint="Opener", aiPriority=9),
    ab("rogue_rupture", "Rupture", "blood",
       "Finishing move that causes damage over time, increased by combo points: 1 point 40 over 8 sec, 2 points 60 "
       "over 10 sec, 3 points 84 over 12 sec, 4 points 112 over 14 sec, 5 points 144 over 16 sec (much more at "
       "higher ranks).",
       ranks=[20, 28, 36, 44, 52, 60], cost=energy(25, combo=True), melee=True, requires=finisher_req(),
       tags=["Finisher", "Bleed"], special="RoguePerComboByRank",
       effects=[E("ApplyAura", aura="rogue_rupture", durationPerCombo=2)], aiHint="Finisher", aiPriority=6),
    ab("rogue_distract", "Distract", "coin",
       "Throws a distraction, attracting the attention of all nearby monsters within 10 yards for 10 sec. Does not "
       "break stealth.",
       ranks=[22], cost=energy(30), cooldown=30, target="Point", range=30, breaksStealth=False,
       area=area(10, caster=False), effects=[E("ApplyAura", aura="rogue_distract")], aiHint="Utility", aiPriority=2),
    ab("rogue_vanish", "Vanish", "stealth",
       "Allows the rogue to vanish from sight, entering an improved stealth mode for 10 sec. Also breaks movement "
       "impairing effects and drops you from enemy threat. Requires Flash Powder.",
       ranks=[22, 42], cooldown=300, target="Self", breaksStealth=False, special="RogueVanish",
       effects=[E("ApplyAura", target="Self", aura="rogue_stealth_aura"),
                E("ApplyAura", target="Self", aura="rogue_vanish_aura")], aiHint="Defensive", aiPriority=3),
    ab("rogue_cheap_shot", "Cheap Shot", "fist",
       "Stuns the target for 4 sec. Must be stealthed. Awards 2 combo points.",
       ranks=[26], cost=energy(60), melee=True, generatesComboPoint=True, tags=["Opener", "Builder"],
       requires={"casterStates": STEALTH},
       effects=[E("ApplyAura", aura="rogue_cheap_shot"), E("GainResource", resource="ComboPoints", amount=1)],
       aiHint="Opener", aiPriority=8),
    ab("rogue_kidney_shot", "Kidney Shot", "fist",
       "Finishing move that stuns the target. Lasts longer per combo point: 1 point 1 sec ... 5 points 5 sec "
       "(rank 2: 2 to 6 sec).",
       ranks=[30, 50], cost=energy(25, combo=True), cooldown=20, melee=True, requires=finisher_req(),
       tags=["Finisher"], effects=[E("ApplyAura", aura="rogue_kidney_shot", durationPerCombo=1)],
       aiHint="CC", aiPriority=7),
    ab("rogue_blind", "Blind", "eye",
       "Blinds the target, causing it to wander disoriented for up to 10 sec. Any damage caused will remove the "
       "effect. Requires Blinding Powder.",
       ranks=[34], cost=energy(30), cooldown=300, range=10, special="RogueBlind",
       effects=[E("ApplyAura", aura="rogue_blind")], aiHint="CC", aiPriority=4),
    ab("rogue_shoot", "Shoot", "bow",
       "Shoot your equipped bow, gun or crossbow at the target for ranged weapon damage. Takes as long as the "
       "weapon's speed.",
       ranks=[1], trainCost=0, range=30, minRange=8, special="PhysicalRangedShot", tags=["Ranged", "Shoot"],
       requires={"rangedWeapon": True}, effects=[E("WeaponDamage", weaponPct=100, ranged=True)],
       aiHint="Damage", aiPriority=1),
    ab("rogue_throw", "Throw", "dagger",
       "Throw your equipped thrown weapon at the target for ranged weapon damage. Takes as long as the weapon's speed.",
       ranks=[1], trainCost=0, range=30, minRange=8, special="PhysicalRangedShot", tags=["Ranged", "Thrown"],
       requires={"rangedWeapon": True}, effects=[E("WeaponDamage", weaponPct=100, ranged=True)],
       aiHint="Damage", aiPriority=1),
] + poison_abilities() + [
    # passives
    ab("rogue_shadowed_presence", "Shadowed Presence", "mask",
       "Rogues work unseen: all threat you generate is reduced by 29% (WoW Classic rogue threat modifier x0.71).",
       ranks=[1], trainCost=0, passive=True, passiveAura="rogue_threat_aura"),
    ab("rogue_dual_wield", "Dual Wield", "daggers",
       "Allows one-handed weapons to be equipped in the off hand. Off-hand attacks deal 50% damage.",
       ranks=[10], passive=True),
    ab("rogue_parry", "Parry", "swords", "Gives a 5% chance to parry enemy melee attacks made from the front.",
       ranks=[12], passive=True),

    # talent abilities
    ab("rogue_riposte", "Riposte", "swords",
       "A strike that becomes active after parrying an opponent's attack (until the end of your next turn). This "
       "attack deals 150% weapon damage ({0}) and disarms the target for 6 sec. Awards 1 combo point.",
       ranks=[20], fromTalent=True, cost=energy(10), cooldown=6, melee=True, generatesComboPoint=True,
       tags=["Builder"], requires={"reactive": "SelfParried", "meleeWeapon": True},
       effects=[E("WeaponDamage", weaponPct=150), E("ApplyAura", aura="rogue_riposte_disarm")],
       aiHint="Damage", aiPriority=8),
    ab("rogue_ghostly_strike", "Ghostly Strike", "dagger",
       "A strike that deals 125% weapon damage ({0}) and increases your chance to dodge by 15% for 7 sec. Awards 1 "
       "combo point.",
       ranks=[20], fromTalent=True, cost=energy(40), cooldown=20, melee=True, generatesComboPoint=True,
       tags=["Builder"],
       effects=[E("WeaponDamage", weaponPct=125), E("ApplyAura", target="Self", aura="rogue_ghostly_strike")],
       aiHint="Damage", aiPriority=6),
    ab("rogue_cold_blood", "Cold Blood", "skull",
       "When activated, increases the critical strike chance of your next offensive ability by 100%.",
       ranks=[30], fromTalent=True, time="OffGcd", cooldown=180, target="Self", breaksStealth=False,
       effects=[E("ApplyAura", target="Self", aura="rogue_cold_blood")], aiHint="Buff", aiPriority=5),
    ab("rogue_blade_flurry", "Blade Flurry", "whirlwind",
       "Increases your attack speed by 20%. In addition, attacks strike an additional nearby opponent. Lasts 15 sec.",
       ranks=[30], fromTalent=True, cost=energy(25), cooldown=120, target="Self",
       effects=[E("ApplyAura", target="Self", aura="rogue_blade_flurry")], aiHint="Buff", aiPriority=5),
    ab("rogue_hemorrhage", "Hemorrhage", "blood",
       "An instant strike that deals 110% weapon damage ({0}) and causes the target to hemorrhage, increasing Physical "
       "damage dealt to it (WoW: +3 per hit, +5/+7 at ranks 2-3; here +3% Physical damage taken). Lasts 30 charges "
       "or 15 sec. Awards 1 combo point.",
       ranks=[30, 46, 58], fromTalent=True, cost=energy(35), melee=True, generatesComboPoint=True, tags=["Builder"],
       effects=[E("WeaponDamage", weaponPct=110), E("ApplyAura", aura="rogue_hemorrhage")],
       aiHint="Damage", aiPriority=6),
    ab("rogue_preparation", "Preparation", "hourglass",
       "When activated, this ability immediately finishes the cooldown on your other Rogue abilities.",
       ranks=[30], fromTalent=True, cooldown=600, target="Self", breaksStealth=False,
       effects=[E("ResetCooldowns", target="Self", abilities=PREP_RESET)], aiHint="Utility", aiPriority=2),
    ab("rogue_adrenaline_rush", "Adrenaline Rush", "rage",
       "Increases your Energy regeneration rate by 100% for 15 sec.",
       ranks=[40], fromTalent=True, time="OffGcd", cooldown=300, target="Self",
       effects=[E("ApplyAura", target="Self", aura="rogue_adrenaline_rush")], aiHint="Buff", aiPriority=5),
    ab("rogue_premeditation", "Premeditation", "eye",
       "When used, adds 2 combo points to your target. You must add to or use those combo points within 10 sec or the "
       "combo points are lost. Must be stealthed; does not break stealth.",
       ranks=[40], fromTalent=True, time="OffGcd", cooldown=120, range=30, breaksStealth=False,
       requires={"casterStates": STEALTH},
       effects=[E("GainResource", resource="ComboPoints", amount=2)], aiHint="Opener", aiPriority=6),
]
