"""Item budget helpers mirroring ItemGenerator (Docs/Expansion.md §8 gear budget)."""

QA = {"Uncommon": 1.1, "Rare": 1.6, "Epic": 2.1, "Legendary": 2.8}
QM = {"Poor": 0.3, "Common": 0.5, "Uncommon": 1.0, "Rare": 1.3, "Epic": 1.6, "Legendary": 2.0}
DPSF = {"Uncommon": 0.72, "Rare": 0.79, "Epic": 0.86, "Legendary": 0.95}
SLOT = {"Chest": 1, "Legs": 1, "Head": 1, "TwoHand": 1, "Shoulder": 0.75, "Hands": 0.75, "Feet": 0.75, "Waist": 0.75,
        "Wrist": 0.56, "Neck": 0.56, "Back": 0.56, "Finger": 0.56, "Trinket": 0.56, "OffHand": 0.56,
        "OneHand": 0.45, "MainHand": 0.45, "Ranged": 0.35}
COST = {"AttackPower": 0.5, "RangedAttackPower": 0.5, "SpellDamage": 0.86, "HealingPower": 0.45, "MeleeCrit": 14, "SpellCrit": 14,
        "RangedCrit": 14, "MeleeHit": 14, "SpellHit": 14, "Dodge": 14, "Parry": 14, "Armor": 0.1, "ManaRegen": 2.5, "HealthRegen": 2.5}
ARMT = {"Cloth": 1.4, "Leather": 2.8, "Mail": 5.9, "Plate": 10}
ARMS = {"Chest": 1, "Legs": 1, "Head": 0.81, "Shoulder": 0.75, "Feet": 0.69, "Hands": 0.62, "Waist": 0.56, "Wrist": 0.44, "Back": 0.5}
SPEED = {"Dagger": 1.7, "FistWeapon": 2.5, "OneHandAxe": 2.6, "OneHandMace": 2.6, "OneHandSword": 2.6, "Staff": 3.0, "Polearm": 3.5,
         "TwoHandAxe": 3.5, "TwoHandMace": 3.5, "TwoHandSword": 3.5, "Bow": 2.8, "Gun": 2.8, "Crossbow": 3.0, "Wand": 1.8, "Thrown": 2.0}
TWOHAND = {"Polearm", "Staff", "TwoHandAxe", "TwoHandMace", "TwoHandSword"}


def target(ilvl, q, slot):
    return 0.55 * ilvl * QA[q] * SLOT[slot]


def cost(stats):
    return sum(v * COST.get(s, 1.0) for s, v in stats)


def split(ilvl, q, slot, weights):
    """Integer stats whose cost is as close as possible to the budget, shared by weight."""
    t = target(ilvl, q, slot)
    tw = sum(w for _, w in weights)
    out = []
    for s, w in weights:
        out.append([s, max(1, round(t * w / tw / COST.get(s, 1.0)))])
    # nudge the biggest plain stat until within 6 %
    for _ in range(40):
        c = cost(out)
        if abs(c - t) <= 0.06 * t:
            break
        i = max(range(len(out)), key=lambda k: weights[k][1] / COST.get(out[k][0], 1.0))
        out[i][1] += 1 if c < t else -1
        out[i][1] = max(1, out[i][1])
    return [{"stat": s, "value": v} for s, v in out]


def armor_value(t, slot, ilvl, q):
    qm = 1.1 + 0.1 * (["Rare", "Epic", "Legendary"].index(q)) if q in ("Rare", "Epic", "Legendary") else (1.0 if q == "Uncommon" else 0.9)
    if slot == "Back":
        t = "Cloth"
    return round(ARMT[t] * ARMS[slot] * (ilvl + 5) * qm)


def weapon_dps(w, ilvl, q):
    qm = 1.1 + 0.12 * (["Rare", "Epic", "Legendary"].index(q)) if q in ("Rare", "Epic", "Legendary") else (1.0 if q == "Uncommon" else 0.9)
    dps = (0.62 * ilvl + 2) * qm
    if w in TWOHAND:
        dps *= 1.3
    elif w in ("Bow", "Gun", "Crossbow", "Thrown"):
        dps *= 0.95
    elif w == "Wand":
        dps *= 1.25
    return dps * DPSF[q]


def price(ilvl, q, slot):
    k = {"Uncommon": 5.2, "Rare": 11.0, "Epic": 26.0}[q]
    return int(round(ilvl * ilvl * k * (0.6 + SLOT[slot]) / 10.0) * 10)


def gear(id, name, icon, desc, q, ilvl, req, slot, weights, armor_type=None, weapon=None, kind=None, unique=False, classes=None):
    it = {"id": id, "name": name, "icon": icon, "description": desc}
    if weapon:
        it["kind"] = "Weapon"
    elif slot in ("Neck", "Finger", "Trinket"):
        it["kind"] = "Accessory"
    elif slot == "OffHand":
        it["kind"] = "Weapon"
    else:
        it["kind"] = "Armor"
    if kind:
        it["kind"] = kind
    it.update({"quality": q, "itemLevel": ilvl, "requiredLevel": req, "equip": slot})
    if armor_type:
        it["armorType"] = armor_type
        it["armor"] = armor_value(armor_type, slot, ilvl, q)
    if weapon in ("HeldInOffhand", "Shield"):
        it["weaponType"] = weapon
    elif weapon:
        it["weaponType"] = weapon
        sp = SPEED[weapon]
        avg = weapon_dps(weapon, ilvl, q) * sp
        it["minDamage"] = int(round(avg * 0.77))
        it["maxDamage"] = int(round(avg * 1.23))
        it["speed"] = sp
    if classes:
        it["classes"] = classes
    it["stats"] = split(ilvl, q, slot, weights)
    it["price"] = price(ilvl, q, slot)
    if unique:
        it["unique"] = True
    return it
