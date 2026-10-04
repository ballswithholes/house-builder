// A combatant or party member: player characters, companions, enemies, pets, demons, totems and summons.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum UnitKind { Character, Companion, Creature, Pet, Totem, Summon }

    /// <summary>Combat role used by companion AI (Auto = inferred from class and talents).</summary>
    public enum UnitRole { Auto, Tank, Healer, MeleeDps, RangedDps }

    /// <summary>A cast or channel that did not fit in the turn and resolves at the start of the caster's next turn.</summary>
    public sealed class PendingCast
    {
        public AbilityDef Ability;
        public int Rank = 1;
        public Unit Target;
        public Vec2 Point;
        public bool HasPoint;
        /// <summary>Cast seconds still to elapse at the start of the next turn (may exceed 6 for very long casts).</summary>
        public float RemainingTime;
        public bool Channel;
        public int TicksLeft, TicksTotal;
        public int ComboPoints;
        public float ExtraResource;
        public bool Free;
        public int StartRound;
        /// <summary>Casting pushback applied so far (max 2) and channel time lost to pushback.</summary>
        public int Pushbacks;
        public float ChannelLoss;
        /// <summary>Full channel duration (seconds) for pushback tick loss.</summary>
        public float ChannelDuration;
        public override string ToString() => $"{Ability?.name} ({RemainingTime:0.#}s{(Channel ? $", {TicksLeft} ticks" : "")})";
    }

    /// <summary>A self-resurrection the downed/dead unit may accept at its next turn (Soulstone, Reincarnation).</summary>
    public sealed class SelfResOffer
    {
        public string Source = "";       // aura/ability id that grants it
        public string Name = "";         // display ("Soulstone", "Reincarnation")
        public float Health, Mana;
        /// <summary>Called when accepted (consume reagents, start cooldowns).</summary>
        public Action<Battle, Unit> OnAccept;
    }

    /// <summary>Hunter's active pet (persisted in saves): template creature, display name, remembered health, dead flag.</summary>
    public sealed class HunterPetState
    {
        public string TemplateId = "hunter_pet_wolf";
        public string Name = "";
        /// <summary>Remembered health fraction (0..1) when dismissed.</summary>
        public float HealthFraction = 1f;
        public bool Dead;
    }

    public sealed partial class Unit
    {
        static int nextId = 1;
        /// <summary>Unique id (also used as the navigation occupancy id).</summary>
        public int Id;
        public string Name = "";
        public UnitKind Kind;
        public Team Team = Team.Player;
        public int Level = 1;
        /// <summary>XP accumulated towards the next level (characters/companions).</summary>
        public int Xp;
        public readonly GameDatabase Db;
        public ClassDef Class;          // characters/companions
        public CreatureDef Creature;    // creatures/pets/totems/summons
        public CompanionDef Companion;  // companions
        public bool IsMainCharacter;
        /// <summary>Companion AI plays this party unit (enemies are always AI).</summary>
        public bool AutoPlay;
        public UnitRole RoleOverride = UnitRole.Auto;
        public string Sprite = "", Portrait = "";

        // ------------------------------------------------------------- position
        public Vec2 Position;
        public Vec2 Facing = Vec2.Right;
        public float Radius = RulesConstants.DefaultUnitRadius;

        // ------------------------------------------------------------ resources
        public float Health, Mana, Rage, Energy, Focus;

        // -------------------------------------------------------- abilities etc
        /// <summary>Known abilities: id → rank (1-based).</summary>
        public readonly Dictionary<string, int> Abilities = new Dictionary<string, int>();
        /// <summary>Learned talents: id → rank.</summary>
        public readonly Dictionary<string, int> Talents = new Dictionary<string, int>();
        /// <summary>Cooldowns in seconds: ability id, or "grp:&lt;cooldownGroup&gt;".</summary>
        public readonly Dictionary<string, float> Cooldowns = new Dictionary<string, float>();
        /// <summary>Interrupt lockouts in seconds per school.</summary>
        public readonly Dictionary<School, float> Lockouts = new Dictionary<School, float>();
        public readonly Equipment Equipment = new Equipment();
        public readonly List<AuraInstance> Auras = new List<AuraInstance>();
        /// <summary>Internal cooldowns of talent/item procs (key → seconds).</summary>
        public readonly Dictionary<string, float> ProcCooldowns = new Dictionary<string, float>();
        public int RespecCount;

        // --------------------------------------------------------- combat state
        public Unit AttackTarget;
        public bool AutoAttacking;
        /// <summary>Basic attack ability in use ("attack", "auto_shot" ...).</summary>
        public string AutoAttackAbility = "attack";
        public float SwingMain, SwingOff, SwingRanged;
        /// <summary>nextSwing ability (Heroic Strike/Cleave/Raptor Strike) replacing the next main-hand swing.</summary>
        public string QueuedSwing = "";
        public int ComboPoints;
        public Unit ComboTarget;
        /// <summary>Threat table (enemies controlled by AI): unit → threat.</summary>
        public readonly Dictionary<Unit, float> Threat = new Dictionary<Unit, float>();
        /// <summary>Unit this AI is currently attacking (threat target).</summary>
        public Unit AggroTarget;
        /// <summary>Taunt: forced target until the end of this unit's next turn.</summary>
        public Unit TauntedBy;
        public int TauntUntilTurn = -1;
        public bool Downed, Dead;
        public PendingCast Pending;
        public float TimeLeft = RulesConstants.TurnSeconds;
        public float TimeDebt;
        public float MoveLeft, MoveBudget;
        public int TurnsTaken;
        public bool InOwnTurn;
        public bool Surprised;
        public float Initiative;
        /// <summary>Reactive windows: name ("TargetDodged"...) → TurnsTaken value after which it expires (end of next turn).</summary>
        public readonly Dictionary<string, int> Reactive = new Dictionary<string, int>();
        /// <summary>TurnsTaken value when mana was last spent (five-second rule in combat).</summary>
        public int ManaSpentTurn = -100;
        /// <summary>Seconds since mana was last spent (five-second rule out of combat).</summary>
        public float SecondsSinceManaSpent = 999f;
        /// <summary>Seconds since the unit last took/dealt damage (out-of-combat regen pacing).</summary>
        public float SecondsSinceCombat = 999f;
        /// <summary>Per-turn windows: seconds of the turn clock during which a state that was active at turn start still applies.</summary>
        public readonly float[] StateWindow = new float[Enum.GetValues(typeof(UnitState)).Length];
        public readonly Dictionary<School, float> LockoutWindow = new Dictionary<School, float>();

        // ------------------------------------------------------ pets / summons
        public Unit Owner;
        public Unit Pet;
        /// <summary>Active totems by element.</summary>
        public readonly Dictionary<string, Unit> Totems = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        public readonly List<Unit> Summons = new List<Unit>();
        /// <summary>Remaining lifetime seconds for summons/totems (−1 = permanent).</summary>
        public float Lifetime = -1f;
        public string TotemElement = "";
        /// <summary>Demons/hunter pets persist with their owner across battles.</summary>
        public bool IsPersistentPet => Kind == UnitKind.Pet && Lifetime < 0;

        /// <summary>Pending self-resurrection offer while downed/dead.</summary>
        public SelfResOffer SelfRes;
        /// <summary>Has dealt/taken damage or used an ability on an enemy in the current battle.</summary>
        public bool Engaged;
        /// <summary>When set, the unit keeps facing this point (Distract).</summary>
        public Vec2? FacingLock;
        /// <summary>Hunters: the active pet (template, name, health), persisted in saves.</summary>
        public HunterPetState HunterPet;
        /// <summary>Team before a temporary side change (Enslave Demon, Mind Control).</summary>
        public Team? OriginalTeam;
        /// <summary>Stored extra attacks (Reckoning).</summary>
        public int ExtraAttacks;

        /// <summary>AI scratch state for the current turn.</summary>
        public readonly AITurnMemory AIMemory = new AITurnMemory();

        // ----------------------------------------------------------- stats cache
        UnitStats stats;
        bool statsDirty = true;

        public Unit(GameDatabase db)
        {
            Db = db;
            Id = nextId++;
        }

        /// <summary>Re-seats the id counter (after loading saves, keeps ids unique).</summary>
        public static void EnsureIdAbove(int id) { if (nextId <= id) nextId = id + 1; }

        // ================================================================ queries

        public bool IsCharacter => Kind == UnitKind.Character || Kind == UnitKind.Companion;
        public bool IsCreature => Creature != null && Class == null;
        public bool IsTotem => Kind == UnitKind.Totem;
        public bool IsPetLike => Kind == UnitKind.Pet || Kind == UnitKind.Summon || Kind == UnitKind.Totem;
        /// <summary>Alive and not downed.</summary>
        public bool IsAlive => !Dead && !Downed;
        public bool IsDeadOrDowned => Dead || Downed;
        public ClassId ClassId => Class != null ? Class.id : ClassId.None;
        public CreatureRank Rank => Creature != null ? Creature.rank : CreatureRank.Normal;
        public CreatureType CreatureType => Creature != null ? Creature.type : CreatureType.Humanoid;

        public ResourceType PowerType
        {
            get
            {
                if (Class != null) return Class.resource;
                if (Creature != null)
                {
                    if (Creature.resource != ResourceType.None) return Creature.resource;
                    return ResourceType.None;
                }
                return ResourceType.None;
            }
        }

        public UnitStats Stats
        {
            get
            {
                if (statsDirty || stats == null)
                {
                    stats = StatCalculator.Compute(this);
                    statsDirty = false;
                }
                return stats;
            }
        }

        /// <summary>Marks the stat cache dirty (auras, gear, talents, level changed). Also dirties the pet.</summary>
        public void InvalidateStats()
        {
            statsDirty = true;
            if (Pet != null && Pet != this) Pet.statsDirty = true;
            foreach (var s in Summons) if (s != null) s.statsDirty = true;
        }

        public float MaxHealth => Stats.MaxHealth;
        public float MaxMana => Stats.MaxMana;
        public float HealthPct => MaxHealth <= 0 ? 0 : Health / MaxHealth * 100f;
        public float ManaPct => MaxMana <= 0 ? 100f : Mana / MaxMana * 100f;
        public float MaxResource(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Mana: return MaxMana;
                case ResourceType.Rage: return RulesConstants.MaxRage;
                case ResourceType.Energy: return RulesConstants.MaxEnergy + Stats.ExtraMaxEnergy;
                case ResourceType.Focus: return RulesConstants.MaxFocus;
                default: return 0f;
            }
        }

        public float GetResource(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Mana: return Mana;
                case ResourceType.Rage: return Rage;
                case ResourceType.Energy: return Energy;
                case ResourceType.Focus: return Focus;
                default: return 0f;
            }
        }

        /// <summary>Sets a resource clamped to [0, max]. Returns the actual change.</summary>
        public float SetResource(ResourceType r, float value)
        {
            float max = MaxResource(r);
            value = MathUtil.Clamp(value, 0f, Math.Max(0f, max));
            float old = GetResource(r);
            switch (r)
            {
                case ResourceType.Mana: Mana = value; break;
                case ResourceType.Rage: Rage = value; break;
                case ResourceType.Energy: Energy = value; break;
                case ResourceType.Focus: Focus = value; break;
                default: return 0f;
            }
            return value - old;
        }

        /// <summary>Clamps health and resources to their maxima (after stat changes).</summary>
        public void ClampResources()
        {
            if (Health > MaxHealth) Health = MaxHealth;
            if (Mana > MaxMana) Mana = MaxMana;
            if (Rage > RulesConstants.MaxRage) Rage = RulesConstants.MaxRage;
            if (Energy > MaxResource(ResourceType.Energy)) Energy = MaxResource(ResourceType.Energy);
            if (Focus > RulesConstants.MaxFocus) Focus = RulesConstants.MaxFocus;
        }

        /// <summary>Fills health, mana, energy and focus (rage to 0).</summary>
        public void RestoreFull()
        {
            Health = MaxHealth;
            Mana = MaxMana;
            Energy = MaxResource(ResourceType.Energy);
            Focus = RulesConstants.MaxFocus;
            Rage = 0f;
        }

        // ------------------------------------------------------------- abilities

        public bool Knows(string abilityId) => abilityId != null && Abilities.ContainsKey(abilityId);
        public int RankOf(string abilityId) => abilityId != null && Abilities.TryGetValue(abilityId, out var r) ? r : 0;
        public int TalentRank(string talentId) => talentId != null && Talents.TryGetValue(talentId, out var r) ? r : 0;

        public float CooldownLeft(AbilityDef a)
        {
            float cd = 0f;
            if (Cooldowns.TryGetValue(a.id, out var c)) cd = c;
            if (!string.IsNullOrEmpty(a.cooldownGroup) && Cooldowns.TryGetValue("grp:" + a.cooldownGroup, out var g)) cd = Math.Max(cd, g);
            return cd;
        }

        // ----------------------------------------------------------------- auras

        public AuraInstance FindAura(string auraId, Unit caster = null)
        {
            foreach (var a in Auras)
                if (a.Def.id == auraId && (caster == null || a.Caster == caster)) return a;
            return null;
        }

        public bool HasAura(string auraId, Unit caster = null) => FindAura(auraId, caster) != null;

        public bool HasAuraWithTag(string tag, Unit caster = null)
        {
            foreach (var a in Auras)
                if ((caster == null || a.Caster == caster) && a.HasTag(tag)) return true;
            return false;
        }

        public AuraInstance FindAuraWithTag(string tag, Unit caster = null)
        {
            foreach (var a in Auras)
                if ((caster == null || a.Caster == caster) && a.HasTag(tag)) return a;
            return null;
        }

        /// <summary>True when an aura grants the state, or (during this unit's own turn) the state was active at turn start
        /// and the turn clock has not passed its remaining duration yet.</summary>
        public bool HasState(UnitState s)
        {
            foreach (var a in Auras) if (a.HasState(s)) return true;
            if (InOwnTurn && TurnClock < StateWindow[(int)s] - 1e-4f) return true;
            return false;
        }

        /// <summary>True when only an aura (not a turn-start window) grants the state.</summary>
        public bool HasStateAura(UnitState s)
        {
            foreach (var a in Auras) if (a.HasState(s)) return true;
            return false;
        }

        /// <summary>Seconds of this turn's Time already used (includes time debt and lost control time).</summary>
        public float TurnClock => RulesConstants.TurnSeconds - Math.Max(0f, TimeLeft);

        public bool IsSchoolLocked(School s)
        {
            if (Lockouts.TryGetValue(s, out var t) && t > 0f && !InOwnTurn) return true;
            if (InOwnTurn && LockoutWindow.TryGetValue(s, out var w) && TurnClock < w - 1e-4f) return true;
            return false;
        }

        public bool IsSchoolForbidden(School s)
        {
            foreach (var a in Auras) foreach (var f in a.Def.forbidSchools) if (f == s) return true;
            return false;
        }

        /// <summary>Loses control of its actions (stun, incapacitate, sleep, polymorph, banish, fear, confuse).</summary>
        public bool IsControlled =>
            HasState(UnitState.Stun) || HasState(UnitState.Incapacitate) || HasState(UnitState.Sleep) ||
            HasState(UnitState.Polymorph) || HasState(UnitState.Banish) || HasState(UnitState.Fear) || HasState(UnitState.Confuse);

        public bool IsStealthed => HasStateAura(UnitState.Stealth) || HasStateAura(UnitState.Invisible);
        public bool IsInvulnerable => HasStateAura(UnitState.Invulnerable) || HasStateAura(UnitState.Banish);
        public bool IsUntargetable => HasStateAura(UnitState.Untargetable);
        public bool IsFeigningDeath => HasStateAura(UnitState.FeignDeath);

        public bool CanMoveNow => IsAlive && !IsControlled && !IsRooted && Pending == null;
        /// <summary>Rooted (and not freed by Blessing of Freedom-like effects).</summary>
        public bool IsRooted => HasState(UnitState.Root) && !Specials.IgnoresState(this, UnitState.Root);

        // -------------------------------------------------------------- relations

        public bool IsHostileTo(Unit o) => o != null && o.Team != Team;
        public bool IsFriendlyTo(Unit o) => o != null && o.Team == Team;
        /// <summary>The controlling owner for pets/totems/summons, else this.</summary>
        public Unit Master => Owner ?? this;

        public float DistanceTo(Unit o) => Vec2.Distance(Position, o.Position);
        public float DistanceTo(Vec2 p) => Vec2.Distance(Position, p);

        /// <summary>True when this unit stands behind <paramref name="target"/> (outside its frontal 180°).</summary>
        public bool IsBehind(Unit target)
        {
            var to = Position - target.Position;
            if (to.SqrLength < 1e-6f) return false;
            return Vec2.Dot(target.Facing.Normalized, to.Normalized) < RulesConstants.BehindDot;
        }

        public void FaceTowards(Vec2 p)
        {
            if (FacingLock.HasValue) p = FacingLock.Value;
            var d = p - Position;
            if (d.SqrLength > 1e-6f) Facing = d.Normalized;
        }

        /// <summary>Inferred or overridden combat role.</summary>
        public UnitRole Role => RoleOverride != UnitRole.Auto ? RoleOverride : RoleInference.Infer(this);

        public override string ToString() => $"{Name}#{Id}(L{Level} {Health:0}/{MaxHealth:0})";
    }

    /// <summary>Per-turn AI scratch data (reset at turn start).</summary>
    public sealed class AITurnMemory
    {
        public int Steps;
        public int Moves;
        public readonly HashSet<string> Failed = new HashSet<string>();
        public readonly Dictionary<string, int> UsedCounts = new Dictionary<string, int>();
        /// <summary>Per-turn result of CreatureAbilityDef.chance rolls.</summary>
        public readonly Dictionary<string, bool> ChanceRolls = new Dictionary<string, bool>();
        public bool Positioned;
        public void Reset() { Steps = 0; Moves = 0; Failed.Clear(); UsedCounts.Clear(); ChanceRolls.Clear(); Positioned = false; }
    }
}
