// Game data definitions. Every JSON file under Resources/Data is a DataBundle; the loader
// merges all bundles into the GameDatabase. Field names here ARE the JSON keys.
//
// Conventions
//   * Ranges/radii/distances are WoW yards (converted with MathUtil.Yd). Durations/cooldowns are seconds.
//   * One combat round = 6 seconds. Each turn a unit spends up to 6 seconds of "time" on abilities.
//   * Numbers are WoW Classic numbers. Levels are 1-60.
//   * Missing keys keep the defaults written below.
using System;
using System.Collections.Generic;
using Lanternvale.Util;

namespace Lanternvale.Data
{
    /// <summary>Top-level object of every data file. Any subset of these arrays may be present.</summary>
    [Serializable]
    public class DataBundle
    {
        public List<ClassDef> classes = new List<ClassDef>();
        public List<AbilityDef> abilities = new List<AbilityDef>();
        public List<AuraDef> auras = new List<AuraDef>();
        public List<TalentTreeDef> talentTrees = new List<TalentTreeDef>();
        public List<ItemDef> items = new List<ItemDef>();
        public List<ItemSuffixDef> itemSuffixes = new List<ItemSuffixDef>();
        public List<LootTableDef> lootTables = new List<LootTableDef>();
        public List<CreatureDef> creatures = new List<CreatureDef>();
        public List<NpcDef> npcs = new List<NpcDef>();
        public List<CompanionDef> companions = new List<CompanionDef>();
        public List<DialogueDef> dialogues = new List<DialogueDef>();
        public List<QuestDef> quests = new List<QuestDef>();
        public List<MapDef> maps = new List<MapDef>();
        public List<SpecialDoc> specials = new List<SpecialDoc>();
        /// <summary>Item sets (Docs/Expansion.md §2.5). A set's <c>items</c> list is the only source of membership.</summary>
        public List<ItemSetDef> itemSets = new List<ItemSetDef>();
        public GameConfigDef config;
    }

    // ------------------------------------------------------------------ classes

    [Serializable]
    public class PrimaryStats
    {
        public float strength, agility, stamina, intellect, spirit;
    }

    [Serializable]
    public class ArmorUpgrade
    {
        public int level = 40;
        public ArmorType type = ArmorType.None;
    }

    public enum ApFormula { StrengthTimesTwo, StrengthPlusAgility, Strength }

    [Serializable]
    public class ClassDef
    {
        public ClassId id;
        public string name = "";
        public string description = "";
        public string[] roles = new string[0];          // "Tank", "Healer", "Melee DPS", "Ranged DPS", "Support"
        public string color = "#ffffff";                  // UI accent colour (WoW class colour)
        public string icon = "";
        public ResourceType resource = ResourceType.Mana;
        public float gcd = 1.5f;                          // global cooldown seconds (rogues 1.0)

        public ArmorType[] armorTypes = new ArmorType[0];
        public ArmorUpgrade armorUpgrade = new ArmorUpgrade();
        public WeaponType[] weaponTypes = new WeaponType[0];
        public int dualWieldLevel = 0;                    // 0 = cannot dual wield
        public bool canParry, canBlock;

        public PrimaryStats baseStatsLevel1 = new PrimaryStats();
        public PrimaryStats baseStatsLevel60 = new PrimaryStats();
        public float baseHealthLevel1, baseHealthLevel60;
        public float baseManaLevel1, baseManaLevel60;     // 0 for non-mana classes

        public ApFormula meleeAp = ApFormula.StrengthTimesTwo;
        public ApFormula rangedAp = ApFormula.Strength;   // Hunter: AgilityTimesTwo handled via rangedApAgilityTimesTwo
        public bool rangedApAgilityTimesTwo;
        public float agilityPerMeleeCritAt60 = 20f;       // agility per 1% melee crit at level 60
        public float intellectPerSpellCritAt60 = 60f;     // intellect per 1% spell crit at level 60
        public float agilityPerDodgeAt60 = 20f;
        public float baseMeleeCrit = 5f, baseSpellCrit = 1f, baseDodge = 5f;
        public float manaRegenBase = 15f;                 // mana per 2s tick outside the five second rule
        public float manaRegenPerSpirit = 0.2f;

        public string basicAttack = "attack";             // ability id used as the default attack
        public string[] startingAbilities = new string[0];
        public string[] startingItems = new string[0];
        public string startingStance = "";                // aura applied at creation (Warrior: Battle Stance)
        public string[] talentTrees = new string[0];      // 3 tree ids
        public string[] defaultBuild = new string[0];     // ordered talent ids (one entry per point) for auto-allocation
        public string sprite = "", portrait = "";        // art keys
        public string designNotes = "";                   // how the class plays (shown in class select)
    }

    // ---------------------------------------------------------------- abilities

    [Serializable]
    public class CostDef
    {
        public ResourceType type = ResourceType.None;
        public float amount;                // flat cost at learnLevel
        public float perLevel;              // added per (rank level - learnLevel)
        public float pctBaseMana;           // alternative to amount: % of class base mana at the caster's level
        public float health;                // flat health cost (Life Tap uses an effect instead)
        public bool consumeAll;             // Execute: spends all remaining resource (handler reads leftover)
        public bool consumesComboPoints;    // finishers
    }

    [Serializable]
    public class AreaDef
    {
        public AreaShape shape = AreaShape.None;
        public float radius;                // yards
        public float angle = 90f;           // cone angle, degrees
        public float width = 4f;            // line width, yards
        public bool centeredOnCaster;       // e.g. Arcane Explosion, Thunder Clap, Frost Nova
        public int maxTargets;              // 0 = unlimited
        public AreaAffects affects = AreaAffects.Enemies;
    }

    [Serializable]
    public class RequirementDef
    {
        public string[] casterAuras = new string[0];     // caster must have ANY of these auras (e.g. stances)
        public string[] casterAuraTags = new string[0];  // caster must have an aura with ANY of these tags ("Seal")
        public string[] targetAuras = new string[0];     // target must have ANY of these auras
        public string[] targetAuraTags = new string[0];
        public bool targetAuraFromSelf;                  // the target aura must be the caster's own
        public string[] casterStates = new string[0];    // e.g. ["Stealth"]
        public bool notInCombat, inCombat;
        public bool behindTarget;
        public string[] mainHand = new string[0];        // allowed main hand WeaponType names
        public bool shield, rangedWeapon, meleeWeapon, dualWield;
        public float targetHealthBelowPct;               // Execute 20, Hammer of Wrath 20
        public CreatureType[] targetCreatureTypes = new CreatureType[0];
        public string reactive = "";                     // "TargetDodged", "TargetParried", "SelfDodged", "SelfParried", "SelfBlocked", "SelfDodgedParriedBlocked", "SelfCrit"
        public bool hasPet, noPet;
        public int minComboPoints;
        public bool targetNotSelf;
        public bool outOfMeleeRange;                     // Charge: target must be farther than 8 yards
        public float minHealthPct;
    }

    [Serializable]
    public class EffectDef
    {
        public EffectType type;
        public EffectTarget target = EffectTarget.Target;
        public School? school;                           // overrides ability/aura school
        public float min, max;                           // base magnitude at learnLevel (max defaults to min)
        public float perLevel;                           // added per level above learnLevel (rank level / caster level)
        public float perCombo;                           // added per combo point spent
        public float coef;                               // spell power coefficient (SpellDamage or HealingPower)
        public float apCoef;                             // attack power coefficient (ranged abilities use RAP)
        public float weaponPct = 100f;                   // WeaponDamage: percent of weapon damage
        public bool offHand, ranged;                     // WeaponDamage: which weapon
        public float pctOfMax;                           // Heal/GainResource/Resurrect: percent of max
        public float pctOfDamage;                        // Damage: heal caster for this % of damage dealt (Drain Life)
        public float pctToCaster;                        // DrainResource: % of drained amount given to caster
        public float threat;                             // Threat: flat amount; others: bonus threat added
        public float threatPct;                          // Threat: multiply existing threat (-100 = wipe, +50)
        public string resource = "";                     // GainResource/DrainResource: Mana/Rage/Energy/Focus/Health/ComboPoints
        public float amount;                             // generic amount (resources, threat, stacks...)
        public string aura = "";                         // ApplyAura/RemoveAura
        public string auraTag = "";                      // RemoveAura by tag
        public int stacks = 1;
        public float duration;                           // overrides aura duration if > 0
        public float durationPerCombo;                   // finishers: extra seconds per combo point
        public float chance = 100f;                      // percent chance the effect happens
        public float radius;                             // yards for *InRadius targets
        public DispelType dispelType = DispelType.None;
        public int dispelCount = 1;
        public float lockout;                            // Interrupt: school lockout seconds
        public float distance;                           // Teleport/Knockback yards
        public string summon = "";                       // creature id for Summon/SummonTotem
        public string totemElement = "";                 // Earth/Fire/Water/Air
        public float lifetime = -1f;                     // seconds, -1 = permanent
        public string item = "";                         // CreateItem
        public int count = 1;
        public string ability = "";                      // TriggerAbility
        public string[] abilities = new string[0];       // ResetCooldowns filter
        public string[] tags = new string[0];            // ResetCooldowns filter
        public School[] schools = new School[0];         // ResetCooldowns filter
        public int chainTargets;                         // Chain Lightning/Chain Heal: extra jumps
        public float chainFalloffPct = 0f;               // magnitude reduction per jump
        public float chainRange = 12f;                   // yards between jumps
        public string requireTargetAura = "";            // only applies if target has this aura
        public bool consumeTargetAura;                   // remove requireTargetAura afterwards
        public bool cannotCrit;
        public bool cannotMiss;
        public string special = "";                      // Special handler name
    }

    [Serializable]
    public class AbilityDef
    {
        public string id = "";
        public string name = "";
        public string icon = "";                         // glyph name (see Docs/DataSchema.md) or art key
        public string description = "";                  // tooltip; may use {0},{1}.. tokens = effect magnitudes
        public ClassId classId = ClassId.None;
        public School school = School.Physical;
        public int learnLevel = 1;
        public int[] rankLevels = new int[0];            // levels at which ranks are trainable; empty = single rank
        public int trainCost;                            // copper for rank 1 (later ranks scale)
        public bool fromTalent;                          // only granted by a talent
        public bool passive;                             // always-on; applies passiveAura
        public string passiveAura = "";
        public bool scaleWithLevel;                      // magnitudes scale with caster level instead of rank

        public TimeCost time = TimeCost.Gcd;
        public float castTime;                           // seconds; 0 = instant
        public float[] rankCastTimes = new float[0];     // optional per-rank cast times (WoW's faster low ranks); empty = castTime for every rank
        public bool channeled;                           // ticks over castTime
        public int channelTicks;                         // number of ticks for channeled abilities
        public float gcdOverride = -1f;                  // >= 0 overrides class GCD
        public float cooldown;                           // seconds
        public string cooldownGroup = "";                // shared cooldown group (Shocks, Judgements...)
        public CostDef cost = new CostDef();

        public TargetType target = TargetType.Enemy;
        public float range;                              // yards; 0 = melee (if melee) or self
        public float minRange;                           // yards (hunter dead zone 8)
        public bool melee;                               // uses melee reach instead of range
        public AreaDef area = new AreaDef();
        public RequirementDef requires = new RequirementDef();

        public List<EffectDef> effects = new List<EffectDef>();
        public string[] tags = new string[0];            // Interrupt, Finisher, Opener, Totem, Seal, Judgement, Aura, Blessing, Stance, Sting, Aspect, Curse, Shock, Armor, Poison, Heal, Shout, Trap, Pet...
        public string exclusiveGroup = "";               // using it removes caster auras from the same group
        public bool breaksStealth = true;
        public bool autoAttack;                          // this is a basic attack (Attack/Auto Shot/Shoot)
        public bool nextSwing;                           // Heroic Strike/Cleave/Raptor Strike: replaces next auto attack
        public bool generatesComboPoint;                 // rogue builders (+1 combo point on hit)
        public bool usableWhileCasting;
        public bool hidden;                              // not shown in spellbook (procs, item uses, enemy-only)
        public string special = "";                      // named handler for unique mechanics
        public int aiPriority;                           // companion/enemy AI hint (higher = preferred)
        public string aiHint = "";                       // "Heal", "Buff", "Interrupt", "Defensive", "Finisher", "Opener", "AoE", "Taunt"...

        // runtime cache (not data: non-public, never serialized)
        string grpKeySource, grpKey;

        /// <summary>"grp:&lt;cooldownGroup&gt;" (the Unit.Cooldowns key of the shared group), or null without a group.
        /// Built once per definition (rebuilt only if <see cref="cooldownGroup"/> is reassigned).</summary>
        internal string CooldownGroupKey
        {
            get
            {
                var g = cooldownGroup;
                if (string.IsNullOrEmpty(g)) return null;
                if (!ReferenceEquals(g, grpKeySource)) { grpKey = "grp:" + g; grpKeySource = g; }
                return grpKey;
            }
        }
    }

    // -------------------------------------------------------------------- auras

    [Serializable]
    public class StatModDef
    {
        public StatId stat;
        public float value;
        public float[] values = new float[0];            // per-rank explicit values (talents; auras: rank of the applying ability)
        public float perLevel;                           // auras: added per (rank level - learnLevel) of the applying ability
        public bool pct;
        public School? school;
        public string target = "Self";                   // "Self" or "Pet" (talents that buff pets)
    }

    [Serializable]
    public class AbsorbDef
    {
        public float amount;                             // at learnLevel
        public float perLevel;
        public float coef;                               // spell power coefficient
        public School[] schools = new School[0];         // empty = all schools
        public float manaPerDamage;                      // Mana Shield: mana spent per point absorbed
    }

    [Serializable]
    public class ProcDef
    {
        public ProcTrigger trigger;
        public float chance = 100f;                      // percent
        public float ppm;                                // procs per minute (weapon-speed normalised), overrides chance
        public string[] abilities = new string[0];       // only when the triggering ability is one of these
        public string[] tags = new string[0];            // ...or has one of these tags
        public School[] schools = new School[0];
        public List<EffectDef> effects = new List<EffectDef>();
        public bool consumeCharge;                       // spends one aura charge (Clearcasting, Lightning Shield)
        public float internalCooldown;                   // seconds
    }

    [Serializable]
    public class AuraDef
    {
        public string id = "";
        public string name = "";
        public string icon = "";
        public string description = "";
        public AuraKind kind = AuraKind.Buff;
        public School school = School.Physical;
        public DispelType dispel = DispelType.None;
        public float duration;                           // seconds; <= 0 = until removed
        public int maxStacks = 1;
        public int charges;                              // 0 = unlimited
        public string exclusiveGroup = "";               // one aura per group per unit (stances, aspects, seals, armors, paladin auras)
        public bool exclusivePerCaster;                  // group exclusivity only among auras from the same caster (curses, blessings)
        public string[] tags = new string[0];
        public bool hidden;
        public bool persistThroughDeath;

        public List<StatModDef> mods = new List<StatModDef>();
        public UnitState[] states = new UnitState[0];
        public School[] forbidSchools = new School[0];   // Shadowform blocks Holy
        public AbsorbDef absorb;                         // null = none
        public bool breakOnDamage;                       // Polymorph, Sap, Gouge, Fear (threshold)
        public float breakDamageThreshold;               // 0 = any damage
        public bool breakOnAction;                       // Stealth, Feign Death: removed when the unit acts/attacks
        public bool breakOnMove;

        public float tickInterval;                       // seconds between ticks (DoTs/HoTs: 3; Consecration 1)
        public List<EffectDef> tickEffects = new List<EffectDef>();   // magnitudes are PER TICK
        public List<EffectDef> onApply = new List<EffectDef>();
        public List<EffectDef> onExpire = new List<EffectDef>();      // natural expiry only (Living Bomb style)
        public List<EffectDef> onRemove = new List<EffectDef>();      // any removal
        public List<ProcDef> procs = new List<ProcDef>();

        public float radius;                             // yards; > 0 makes this an area aura
        public string radiusAura = "";                   // aura applied to units in radius while active
        public AreaAffects radiusAffects = AreaAffects.Allies;
        public string special = "";
    }

    // ------------------------------------------------------------------ talents

    [Serializable]
    public class PassiveDef
    {
        public string type = "Stat";                     // "Stat", "AbilityMod", "Proc", "GrantAbility", "Special"
        // Stat
        public StatId stat;
        public float value;
        public float[] values = new float[0];            // per-rank values; otherwise value * rank
        public bool pct;
        public School? school;
        public string target = "Self";                   // "Self" or "Pet"
        // AbilityMod
        public AbilityProperty property;
        public string[] abilities = new string[0];       // filters: ability ids
        public string[] tags = new string[0];            //          or ability tags
        public School[] schools = new School[0];         //          or schools (any match)
        // Proc (chance scales with rank via values or value*rank when proc.chance == 0)
        public ProcDef proc;
        // GrantAbility
        public string ability = "";
        // Special
        public string special = "";
        /// <summary>Optional tooltip text; used first when set (items, set bonuses).</summary>
        public string description = "";
    }

    [Serializable]
    public class TalentDef
    {
        public string id = "";
        public string name = "";
        public string icon = "";
        public int tier = 1;                             // 1..7 (requires 5*(tier-1) points in tree)
        public int column;                               // 0..3
        public int maxRank = 1;
        public string description = "";                 // use {a/b/c/d/e} for per-rank values
        public string requires = "";                     // prerequisite talent id (arrow)
        public List<PassiveDef> effects = new List<PassiveDef>();
    }

    [Serializable]
    public class TalentTreeDef
    {
        public string id = "";
        public ClassId classId;
        public string name = "";
        public string icon = "";
        public string description = "";
        public string background = "";                   // art key
        public List<TalentDef> talents = new List<TalentDef>();
    }

    // -------------------------------------------------------------------- items

    [Serializable]
    public class ItemDef
    {
        public string id = "";
        public string name = "";
        public string icon = "";
        public string description = "";                  // flavour text
        public ItemKind kind = ItemKind.Junk;
        public Quality quality = Quality.Common;
        public int itemLevel = 1;
        public int requiredLevel;
        public EquipType equip = EquipType.None;
        public ArmorType armorType = ArmorType.None;
        public WeaponType weaponType = WeaponType.None;
        public float armor;
        public float block;                              // shield block value
        public float minDamage, maxDamage, speed;        // weapons
        public School damageSchool = School.Physical;    // wands
        public List<StatModDef> stats = new List<StatModDef>();
        public List<PassiveDef> equipEffects = new List<PassiveDef>();  // "Equip:" and "Chance on hit:" lines
        public string use = "";                          // ability id used when the item is used (potions, food)
        public bool consumable;                          // removes one on use
        public int stack = 1;
        public int price;                                // buy price in copper (sells for 1/4)
        public ClassId[] classes = new ClassId[0];       // class restriction
        public bool unique;
        public string quest = "";                        // quest item for quest id
        public string art = "";                          // optional world/paper-doll art key
    }

    [Serializable]
    public class ItemSuffixDef
    {
        public string id = "";
        public string name = "";                         // "of the Bear"
        public StatId[] stats = new StatId[0];           // budget split across these
        public float[] weights = new float[0];
    }

    /// <summary>One bonus of an item set: active while at least <see cref="pieces"/> distinct set items are equipped.</summary>
    [Serializable]
    public class SetBonusDef
    {
        public int pieces = 2;
        public List<StatModDef> stats = new List<StatModDef>();
        public List<PassiveDef> equipEffects = new List<PassiveDef>();  // Stat / AbilityMod / Proc / Special only
        public string description = "";                  // optional tooltip text; else generated
    }

    [Serializable]
    public class ItemSetDef
    {
        public string id = "";
        public string name = "";
        public string[] items = new string[0];           // item ids (the only source of set membership)
        public List<SetBonusDef> bonuses = new List<SetBonusDef>();
    }

    [Serializable]
    public class LootEntryDef
    {
        public string item = "";                         // item id (empty when random or pooled)
        public float chance = 100f;
        public int min = 1, max = 1;
        public bool random;                              // generate a random equipable item
        public Quality quality = Quality.Uncommon;       // random item quality
        public int itemLevelOffset;                      // random item level relative to creature level
        public string[] pool = new string[0];            // pick one item id from this pool (exclusive with item)
        public float[] weights = new float[0];           // optional pool weights (empty = equal; else one per pool id)
        public bool skipOwned;                           // pool: skip ids the party already owns (bags or equipped)
        public int perMembers;                           // > 0: roll once more per this many party members (ceil(n / perMembers) rolls)
        public bool partyUsable;                         // pool: only ids some party member can equip
    }

    [Serializable]
    public class LootTableDef
    {
        public string id = "";
        public int goldMin, goldMax;                     // copper
        public List<LootEntryDef> entries = new List<LootEntryDef>();
        public int rolls = 1;                            // how many times the entry list is rolled
    }

    // ---------------------------------------------------------------- creatures

    [Serializable]
    public class CreatureAbilityDef
    {
        public string ability = "";
        public int priority = 1;                         // higher first
        public float chance = 100f;                      // percent chance to consider each turn
        public string condition = "";                    // "", "selfHpBelow:30", "allyHpBelow:50", "targetCasting", "targetNoAura:id", "enemiesInRange:3", "notOnCooldown"
    }

    [Serializable]
    public class CreatureDef
    {
        public string id = "";
        public string name = "";
        public string description = "";
        public string sprite = "";                       // art key
        public string portrait = "";
        public CreatureType type = CreatureType.Beast;
        public string family = "";                       // Wolf, Boar, Cat, Bear, Spider, Raptor, Bat, Owl, Imp, Voidwalker...
        public CreatureRank rank = CreatureRank.Normal;
        public int levelMin = 1, levelMax = 1;
        public bool scaleToParty;                        // level = clamp(party level + levelOffset)
        public int levelOffset;
        public float healthMult = 1f, damageMult = 1f, armorMult = 1f, manaMult = 1f;
        public ResourceType resource = ResourceType.None;
        public float attackSpeed = 2f;
        public School meleeSchool = School.Physical;
        public bool ranged;                              // basic attack is ranged
        public float rangedRange = 30f;                  // yards
        public string projectile = "";                   // art key for ranged attacks
        public float moveSpeed = 9f;                     // metres per turn
        public float size = 1.8f;                        // sprite world height
        public AIProfile ai = AIProfile.Melee;
        public List<CreatureAbilityDef> abilities = new List<CreatureAbilityDef>();
        public string[] passives = new string[0];        // aura ids applied permanently
        public UnitState[] immune = new UnitState[0];
        public List<StatModDef> stats = new List<StatModDef>();   // resistances etc.
        public string lootTable = "";
        public float xpMult = 1f;
        public string faction = "";
        public bool tameable;                            // hunters can tame it
        public string totemElement = "";                 // totems: Earth/Fire/Water/Air
        public string bark = "";                         // one-liner when combat starts
        public int levelFloor, levelCap;                 // scaleToParty clamp: [max(1, floor), cap > 0 ? cap : 63]; 0 = none
        public string material = "";                     // impact sound: plate|mail|leather|cloth|flesh|fur|chitin|bone|wood|stone|ether|ice|scale|wet ("" = infer)
        public string voice = "";                        // death/vocal sound: beast|humanoid|spirit|wood|stone|dragon|frog|gnoll|none ("" = infer)
    }

    // ---------------------------------------------------------- npcs & companions

    [Serializable]
    public class VendorItemDef
    {
        public string item = "";
        public int stock = -1;                           // -1 unlimited
        public int priceOverride;                        // copper; 0 = item price
    }

    [Serializable]
    public class NpcDef
    {
        public string id = "";
        public string name = "";
        public string title = "";                        // "<Mage Trainer>"
        public string sprite = "", portrait = "";
        public string dialogue = "";                     // dialogue id
        public List<VendorItemDef> vendor = new List<VendorItemDef>();
        public ClassId[] trains = new ClassId[0];        // class trainer for these classes
        public bool innkeeper;                           // can long rest here
        public string bark = "";                         // hover/ambient line
        public bool wanders;
        public string shortName = "";                    // Map panel label ("" = name)
    }

    [Serializable]
    public class CompanionDef
    {
        public string id = "";
        public string name = "";
        public ClassId classId;
        public string title = "";
        public string sprite = "", portrait = "";
        public string bio = "";
        public string personality = "";
        public string inspiration = "";                  // design note (FFX-inspired look)
        public PrimaryStats statBonus = new PrimaryStats();
        public string[] startingItems = new string[0];
        public string[] preferredTalents = new string[0];  // talent ids, in order, for auto-allocation
        public string recruitDialogue = "";
        public string[] likes = new string[0];           // approval hints
        public string[] dislikes = new string[0];
    }

    // ------------------------------------------------------- dialogue & quests

    [Serializable]
    public class ConditionDef
    {
        public ConditionType type;
        public string key = "";
        public string value = "";
        public int amount;
    }

    [Serializable]
    public class OutcomeDef
    {
        public OutcomeType type;
        public string key = "";
        public string value = "";
        public int amount;
    }

    [Serializable]
    public class CheckDef
    {
        public SkillCheck skill;
        public int dc = 10;
        public string success = "";                      // node id
        public string failure = "";                      // node id
    }

    [Serializable]
    public class ChoiceDef
    {
        public string text = "";
        public string next = "";                         // node id ("" ends the dialogue)
        public List<ConditionDef> conditions = new List<ConditionDef>();
        public List<OutcomeDef> outcomes = new List<OutcomeDef>();
        public CheckDef check;                           // null = no roll
        public bool once;
        public string tag = "";                          // shown as [TAG] prefix, e.g. "PALADIN", "PERSUASION"
    }

    [Serializable]
    public class DialogueNodeDef
    {
        public string id = "";
        public string speaker = "";                      // npc/companion id, "player", or "narrator"
        public string text = "";
        public List<ConditionDef> conditions = new List<ConditionDef>();   // node skipped to `fallback` if unmet
        public string fallback = "";
        public List<OutcomeDef> outcomes = new List<OutcomeDef>();         // on enter
        public List<ChoiceDef> choices = new List<ChoiceDef>();
        public string next = "";                         // auto-continue when no choices
    }

    [Serializable]
    public class DialogueDef
    {
        public string id = "";
        public string start = "";
        public List<DialogueNodeDef> nodes = new List<DialogueNodeDef>();
    }

    [Serializable]
    public class ObjectiveDef
    {
        public ObjectiveType type;
        public string target = "";                       // creature id, item id, npc id, area id or flag
        public int count = 1;
        public string text = "";
    }

    [Serializable]
    public class QuestStageDef
    {
        public string id = "";
        public string description = "";
        public List<ObjectiveDef> objectives = new List<ObjectiveDef>();
        public string next = "";                         // stage id when objectives complete ("" = quest complete)
        public List<OutcomeDef> onComplete = new List<OutcomeDef>();
        public string turnIn = "";                       // npc or companion id the quest marker points at ("" = infer)
    }

    [Serializable]
    public class QuestRewardDef
    {
        public int xp;
        public int gold;                                 // copper
        public string[] items = new string[0];
        public string[] choiceItems = new string[0];     // pick one
    }

    [Serializable]
    public class QuestDef
    {
        public string id = "";
        public string name = "";
        public string giver = "";
        public int level = 1;
        public string summary = "";
        public bool main;
        public List<QuestStageDef> stages = new List<QuestStageDef>();
        public QuestRewardDef rewards = new QuestRewardDef();
        public int minLevel;                             // main character level needed to be offered (0 = none; grey "!" marker)
        public string zone = "";                         // map id that groups the quest in the journal ("" = none)
    }

    // --------------------------------------------------------------------- maps

    [Serializable]
    public class LightDef
    {
        public string color = "#ffd9a0";
        public float radius = 4f;                        // metres
        public float intensity = 1f;
        public Vec2 offset;                              // metres from the prop pivot
        public bool flicker;
        public bool nightOnly;
    }

    [Serializable]
    public class ColliderDef
    {
        public float w, h;                               // ellipse footprint in metres (0 = no collider)
        public Vec2 offset;
    }

    [Serializable]
    public class PropDef
    {
        public string art = "";
        public Vec2 pos;
        public float scale = 1f;
        public bool flip;
        public ColliderDef collider = new ColliderDef();
        public LightDef light;                           // null = none
        public string tint = "";
        public bool sway;                                // gentle wind sway (grass, trees)
        public string interact = "";                     // optional interaction id (sign text, shrine, etc.)
        public string text = "";                         // sign/inspect text
        public string requireFlag = "";                  // shown only while this flag expression holds ("" = always)
        public string hideFlag = "";                     // hidden while this flag expression holds ("" = never)
        public string dialogue = "";                     // dialogue started when the prop is clicked (owner = interact id)
    }

    [Serializable]
    public class ParallaxLayerDef
    {
        public string art = "";
        public float parallax = 0.5f;                    // 0 = fixed to camera, 1 = world
        public float y;                                  // world y of the layer's bottom edge
        public float height = 10f;                       // world height
        public bool loop = true;                         // tile horizontally
        public string tint = "";
        public float scrollSpeed;                        // auto-scroll (clouds), metres/second
    }

    [Serializable]
    public class SpawnPointDef
    {
        public string id = "";
        public Vec2 pos;
    }

    [Serializable]
    public class MapNpcDef
    {
        public string npc = "";
        public Vec2 pos;
        public bool flip;
        public string requireFlag = "";
        public string hideFlag = "";
    }

    [Serializable]
    public class EncounterEnemyDef
    {
        public string creature = "";
        public Vec2 pos;
        public int level;                                // 0 = creature default
    }

    [Serializable]
    public class EncounterDef
    {
        public string id = "";
        public Vec2 pos;                                 // trigger centre
        public float radius = 6f;                        // metres
        public List<EncounterEnemyDef> enemies = new List<EncounterEnemyDef>();
        public string requireFlag = "";
        public string doneFlag = "";                     // set when defeated (defaults to "enc_<id>")
        public string dialogue = "";                     // optional dialogue before combat
        public bool hidden;                              // enemies invisible until triggered (ambush)
    }

    [Serializable]
    public class ChestDef
    {
        public string id = "";
        public Vec2 pos;
        public string art = "prop_chest";
        public string lootTable = "";
        public string[] items = new string[0];
        public int gold;
        public CheckDef lockCheck;                       // optional SleightOfHand check to open
        public string requireFlag = "";
    }

    [Serializable]
    public class TransitionDef
    {
        public string id = "";
        public Vec2 pos;
        public Vec2 size = new Vec2(2, 2);
        public string targetMap = "";
        public string targetSpawn = "";
        public string label = "";
        public string requireFlag = "";
        public string lockedText = "";
        public bool hidden;                              // invisible and unusable until Flags.Test(revealFlag)
        public string revealFlag = "";
        public string marker = "";                       // "" auto | none | cave | door | stairs | portal
    }

    [Serializable]
    public class AmbientDef
    {
        public bool fireflies, leaves, pollen, mist, rain, embers;
        public bool snow, ash, dust, drips;
        public string timeOfDay = "day";                 // dawn, day, dusk, night
        public bool dayNightCycle;
        public string ambientColor = "#ffffff";
        public float ambientIntensity = 1f;
    }

    [Serializable]
    public class RegionDef
    {
        public string id = "";
        public Vec2 pos;
        public Vec2 size;
        public string enterFlag = "";                    // flag set on entering (quest Reach objectives)
        public string text = "";                         // toast shown on first entry
        public string name = "";                         // Map panel cluster label
        public CheckDef check;                           // passive check on first entry (skill + dc only; null = none)
        public string checkFlag = "";                    // set when the check succeeds
        public string successText = "", failText = "";
        public string requireFlag = "";                  // the check rolls only while this holds (once per save)
    }

    /// <summary>A painted path polyline (any direction).</summary>
    [Serializable]
    public class PathDef
    {
        public string art = "decal_path_dirt";
        public List<Vec2> points = new List<Vec2>();
        public float width = 2.2f;
    }

    /// <summary>An axis-aligned rectangle (pos = centre, size = full extents).</summary>
    [Serializable]
    public class RectDef
    {
        public Vec2 pos;
        public Vec2 size;
    }

    /// <summary>A stream polyline, or a pond when closed. Crossings are walkable rects (fords, bridges).</summary>
    [Serializable]
    public class WaterDef
    {
        public List<Vec2> points = new List<Vec2>();
        public float halfWidth = 1.5f;
        public bool closed;
        public List<RectDef> crossings = new List<RectDef>();
        public bool blocksMovement = true;
    }

    [Serializable]
    public class MapDef
    {
        public string id = "";
        public string name = "";
        public string subtitle = "";
        public float width = 60f, depth = 20f;           // walkable ground in metres: x in [0,width], y in [0,depth]
        public string skyTop = "#9fd3f0", skyBottom = "#fdf1d6";
        public List<ParallaxLayerDef> layers = new List<ParallaxLayerDef>();   // back to front, behind the ground
        public string ground = "ground_meadow";          // tiling ground art
        public float groundTile = 8f;                    // metres per tile
        public string groundTint = "";
        public List<Vec2> walkable = new List<Vec2>();   // optional polygon; empty = whole ground rect
        public List<PropDef> props = new List<PropDef>();
        public List<PropDef> foreground = new List<PropDef>();     // drawn in front with parallax ~1.15
        public List<MapNpcDef> npcs = new List<MapNpcDef>();
        public List<EncounterDef> encounters = new List<EncounterDef>();
        public List<ChestDef> chests = new List<ChestDef>();
        public List<TransitionDef> transitions = new List<TransitionDef>();
        public List<SpawnPointDef> spawns = new List<SpawnPointDef>();
        public List<RegionDef> regions = new List<RegionDef>();
        public AmbientDef ambient = new AmbientDef();
        public bool restArea;                            // camp/long rest allowed
        public string music = "";
        public string biome = "";                        // meadow|village|forest|shrine|highlands|fen|peaks|cave|ice_cave|crypt|hollow_heart|roost ("" = keyword fallback)
        public string environment = "";                  // "" / outdoor | cave | crypt (indoor: no sky, sun or hills)
        public List<PathDef> paths = new List<PathDef>();
        public List<WaterDef> water = new List<WaterDef>();
        public float fill;                               // 0..1 procedural interior ground cover (0 = none)
        public int levelMin, levelMax;                   // zone level band (0 = none)
        public int raidSize;                             // > 0: a raid map (party up to raidSize)
        public string raidReturnMap = "", raidReturnSpawn = "";
        public bool dungeon;                             // a hidden dungeon
    }

    // ------------------------------------------------------------------- config

    [Serializable]
    public class GameConfigDef
    {
        public string startMap = "lanternvale";
        public string startSpawn = "default";
        public string startDialogue = "";
        public float xpRate = 4f;                        // multiplier over WoW Classic XP
        public int maxLevel = 60;
        public int partySize = 4;
        public int maxRaidSize = 10;
        /// <summary>XP rate by the receiving character's level, linearly interpolated (empty = xpRate).</summary>
        public List<XpRatePoint> xpRateByLevel = new List<XpRatePoint>();
        public float baseMoveMetres = 9f;                // movement per turn
        public float meleeReachMetres = 2.2f;            // centre-to-centre melee reach
        public int startingGold = 500;                   // copper
        public string[] startingItems = new string[0];
        public int[] xpToLevel = new int[0];             // xp needed to go from level i+1 to i+2 (WoW table)
    }

    [Serializable]
    public class XpRatePoint
    {
        public int level;
        public float rate;
    }

    /// <summary>Documentation of a named special handler requested by data authors.</summary>
    [Serializable]
    public class SpecialDoc
    {
        public string id = "";
        public string usedBy = "";                       // ability/aura/talent ids
        public string behaviour = "";                    // exact rules the handler must implement
    }
}
