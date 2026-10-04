// Shared enums for game data. JSON files use the enum NAMES (case-insensitive).
namespace Lanternvale.Data
{
    public enum ClassId { None, Warrior, Hunter, Paladin, Mage, Priest, Rogue, Warlock, Shaman }

    public enum ResourceType { None, Mana, Rage, Energy, Focus }

    public enum School { Physical, Holy, Fire, Nature, Frost, Shadow, Arcane }

    public enum ArmorType { None, Cloth, Leather, Mail, Plate }

    public enum WeaponType
    {
        None, Dagger, FistWeapon, OneHandAxe, OneHandMace, OneHandSword, Polearm, Staff,
        TwoHandAxe, TwoHandMace, TwoHandSword, Bow, Crossbow, Gun, Thrown, Wand, Shield, HeldInOffhand
    }

    /// <summary>Where an item can be equipped (item side). Shields/held items use OffHand.</summary>
    public enum EquipType
    {
        None, Head, Neck, Shoulder, Back, Chest, Wrist, Hands, Waist, Legs, Feet, Finger, Trinket,
        OneHand, MainHand, OffHand, TwoHand, Ranged
    }

    /// <summary>Concrete paper-doll slots (character side).</summary>
    public enum EquipSlot
    {
        Head, Neck, Shoulder, Back, Chest, Wrist, Hands, Waist, Legs, Feet,
        Finger1, Finger2, Trinket1, Trinket2, MainHand, OffHand, Ranged
    }

    public enum ItemKind { Armor, Weapon, Accessory, Consumable, Quest, Junk, Reagent, Ammo, Food, Drink }

    public enum Quality { Poor, Common, Uncommon, Rare, Epic, Legendary }

    /// <summary>
    /// Time cost category. Every turn is 6 seconds; abilities consume time:
    /// Gcd = max(castTime, class GCD), OffGcd = only castTime (usually 0).
    /// </summary>
    public enum TimeCost { Gcd, OffGcd }

    public enum TargetType
    {
        Self,       // no target selection
        Enemy,      // hostile unit
        Ally,       // friendly unit including self
        AllyOther,  // friendly unit other than self
        Point,      // ground point (AoE placement / teleport)
        Any,        // any living unit
        DeadAlly,   // dead friendly unit (resurrection)
        Pet,        // caster's own pet
    }

    public enum AreaShape { None, Circle, Cone, Line }

    public enum AreaAffects { Enemies, Allies, All }

    public enum DispelType { None, Magic, Curse, Poison, Disease }

    public enum AuraKind { Buff, Debuff }

    public enum CreatureType { Humanoid, Beast, Undead, Demon, Elemental, Spirit, Dragonkin, Giant, Mechanical, Critter, Totem }

    public enum CreatureRank { Normal, Elite, Rare, Boss, Minion, Pet, Totem, Critter }

    public enum AIProfile
    {
        Melee,      // walks to target, swings, uses abilities by priority
        Ranged,     // keeps distance, shoots
        Caster,     // keeps distance, casts
        Healer,     // heals hurt allies first
        Skirmisher, // hit and run
        Coward,     // flees at low health
        Boss,       // scripted by ability priorities/conditions
        Totem,      // never moves; uses abilities at end of owner's turn
        Pet,        // follows owner's target
        Passive,    // never attacks
    }

    public enum Team { Player, Enemy, Neutral }

    /// <summary>
    /// Effect primitives used by abilities, aura ticks and procs.
    /// See Docs/DataSchema.md for the fields each one reads.
    /// </summary>
    public enum EffectType
    {
        Damage,         // spell/flat damage: min..max (+perLevel, +perCombo) + coef*SpellDamage + apCoef*AP
        WeaponDamage,   // weaponPct% of weapon roll (+ AP/14*speed) + min..max flat bonus
        Heal,           // min..max + coef*HealingPower; pctOfMax heals % of target max health
        ApplyAura,      // applies aura (by id); stacks, duration override
        RemoveAura,     // removes aura (by id) or auras with tag (auraTag)
        Dispel,         // removes dispelCount auras of dispelType (hostile buffs or friendly debuffs)
        Interrupt,      // cancels a pending cast; locks school for lockout seconds
        Taunt,          // target must attack caster next turn; caster gets top threat
        Threat,         // flat threat (amount) or threatPct multiplier on existing threat
        GainResource,   // resource (Mana/Rage/Energy/Focus/Health/ComboPoints) += amount (or pctOfMax)
        DrainResource,  // removes resource from target; optional pctToCaster returned to caster
        Teleport,       // to target point or distance yards forward (Blink)
        Charge,         // move adjacent to target instantly
        Knockback,      // push target distance yards away from caster
        Summon,         // summon creature (pet, demon, minion) with lifetime seconds
        SummonTotem,    // summon totem creature at caster/point; one per totemElement
        Resurrect,      // revive dead ally with pctOfMax health and mana
        CreateItem,     // add item x count to party inventory
        ResetCooldowns, // resets cooldowns of abilities matching abilities/tags/schools
        TriggerAbility, // casts another ability (ability) for free on the same target
        Kill,           // instantly kills target (Shadowburn-like executes via special instead)
        Special,        // named handler implemented in code (special)
    }

    /// <summary>Who an effect lands on, relative to the ability's caster/target.</summary>
    public enum EffectTarget
    {
        Target,          // the ability target (or each unit in the area for area abilities)
        Self,            // the caster
        Area,            // explicit: every unit hit by the ability's area
        Pet,             // caster's pet
        Owner,           // the owner of a pet/totem caster
        AlliesInRadius,  // caster's allies within radius yards of the caster (party buffs, Prayer of Healing)
        EnemiesInRadius, // enemies within radius yards of the caster
        Party,           // all living allies in the encounter
        Attacker,        // in procs: the unit that triggered the proc (e.g. the melee attacker for OnStruck)
    }

    public enum ProcTrigger
    {
        OnMeleeHit, OnAutoAttackHit, OnRangedHit, OnSpellHit, OnSpellCast, OnHealCast,
        OnCrit, OnMeleeCrit, OnSpellCrit, OnStruck, OnDamaged, OnCritTaken,
        OnDodge, OnParry, OnBlock, OnTargetDodged, OnTargetParried, OnKill,
        OnFinisher, OnAbilityUsed, OnTurnStart, OnTurnEnd, OnBattleStart, OnPeriodicDamage,
    }

    /// <summary>Unit states an aura can impose.</summary>
    public enum UnitState
    {
        Stun,           // loses turn
        Root,           // cannot move
        Silence,        // cannot use spells (non-Physical school abilities)
        Pacify,         // cannot use Physical abilities or auto attack
        Disarm,         // no weapon damage (auto attacks/weapon abilities deal reduced damage)
        Fear,           // turn is spent fleeing (AI controlled), breaks on enough damage
        Polymorph,      // incapacitated, breaks on damage, regenerates
        Incapacitate,   // loses turn, breaks on damage (Sap, Gouge, Seduction)
        Sleep,          // like Incapacitate
        Confuse,        // moves randomly
        Stealth,        // cannot be targeted by enemies (detection checks), breaks on action
        Invisible,
        Invulnerable,   // immune to all damage and harmful effects (Divine Shield, Ice Block)
        ImmunePhysical, // Blessing of Protection
        ImmuneMagic,
        Banish,         // cannot act, cannot be harmed
        Shapeshift,     // Ghost Wolf: only movement and shapeshift abilities
        FeignDeath,     // enemies drop threat; breaks on action
        Daze,
        Untargetable,
    }

    /// <summary>
    /// Character statistics. StatMod.pct = true multiplies (+10 = +10%), otherwise flat.
    /// Some stats accept an optional school (SpellDamage, SpellCrit, SpellHit, Resistance, DamageDone, DamageTaken, ThreatGenerated, CritDamage, ManaCost).
    /// </summary>
    public enum StatId
    {
        Strength, Agility, Stamina, Intellect, Spirit, AllStats,
        Health, Mana,                 // max health / max mana
        Armor,
        AttackPower, RangedAttackPower,
        SpellDamage, HealingPower,
        MeleeCrit, RangedCrit, SpellCrit,   // percent points
        MeleeHit, RangedHit, SpellHit,      // percent points
        Dodge, Parry, Block, BlockValue, Defense,
        Resistance,                   // school resistance (no school = all)
        MoveSpeed,                    // pct
        MeleeHaste, RangedHaste, CastSpeed, // pct
        ManaRegen, HealthRegen,       // per 5 seconds
        SpiritRegenWhileCasting,      // pct of spirit regen kept inside the five-second rule
        DamageDone, DamageTaken,      // pct
        HealingDone, HealingTaken,    // pct
        ThreatGenerated,              // pct
        CritDamage,                   // pct bonus to crit multiplier
        ArmorPenetration, SpellPenetration,
        EnergyRegen, RageGenerated,   // pct
        ManaCost,                     // pct
        DodgeChanceAgainstMe,         // pct points (reduces enemy chance to dodge you)
        ChanceToBeHit,                // pct points (Improved Sprint-like / Nature's Grasp... misc)
        StealthDetection,
    }

    /// <summary>Ability properties that talents/auras/items can modify via an AbilityMod passive.</summary>
    public enum AbilityProperty
    {
        Damage,      // pct
        Healing,     // pct
        Effect,      // pct, generic magnitude (absorbs, DoT/HoT ticks, stat amounts of applied auras)
        CritChance,  // pct points
        CritBonus,   // pct increase of crit bonus (Ice Shards: +20 per rank)
        Cost,        // pct
        CostFlat,    // flat resource (negative reduces)
        Cooldown,    // seconds (negative reduces)
        CastTime,    // seconds (negative reduces)
        Range,       // pct
        Radius,      // pct
        Duration,    // seconds added to auras applied by the ability
        DurationPct, // pct
        Threat,      // pct
        HitChance,   // pct points
        Charges,     // flat
        ComboPoints, // extra combo points generated (flat)
        Gcd,         // seconds (negative reduces)
    }

    public enum ConditionType
    {
        Flag, NotFlag, QuestState, QuestNotStarted, QuestActive, QuestComplete,
        HasItem, NotHasItem, Gold, Class, NotClass, Level, InParty, NotInParty, Companion, TimeOfDay,
    }

    public enum OutcomeType
    {
        SetFlag, ClearFlag, StartQuest, SetQuestStage, CompleteQuest, FailQuest,
        GiveItem, TakeItem, GiveGold, TakeGold, GiveXP, Recruit, Dismiss, StartCombat,
        OpenVendor, OpenTrainer, OpenRespec, Rest, HealParty, Teleport, Approval, EndDialogue, Special,
    }

    public enum SkillCheck
    {
        Athletics,     // Strength
        Intimidation,  // Strength
        Acrobatics,    // Agility
        Stealth,       // Agility
        SleightOfHand, // Agility
        Endurance,     // Stamina
        Arcana,        // Intellect
        History,       // Intellect
        Investigation, // Intellect
        Insight,       // Spirit
        Persuasion,    // Spirit
        Religion,      // Spirit
        Nature,        // Spirit
        Survival,      // Spirit
    }

    public enum ObjectiveType { Kill, Collect, Talk, Reach, Flag, Defeat }
}
