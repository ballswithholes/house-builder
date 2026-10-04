// Save / load of the whole session as one JSON string (the Unity layer owns files).
using System;
using System.Collections.Generic;
using System.Globalization;
using Lanternvale.Data;
using Lanternvale.Json;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        // ================================================================= save

        /// <summary>Null when saving is allowed now, else the reason.</summary>
        public string CannotSaveReason()
        {
            if (!hasGame) return "There is no game to save.";
            if (gameOver) return "The party has fallen.";
            if (Battle != null) return "Cannot save during combat.";
            if (Dialogue.IsActive) return "Cannot save during a conversation.";
            return null;
        }

        /// <summary>Serializes the session. Throws InvalidOperationException when saving is not allowed (see CannotSaveReason).</summary>
        public string SaveGame()
        {
            var why = CannotSaveReason();
            if (why != null) throw new InvalidOperationException(why);
            return JsonWriter.Serialize(BuildSaveData(), true);
        }

        public bool TrySaveGame(out string json, out string reason)
        {
            json = null;
            reason = CannotSaveReason();
            if (reason != null) return false;
            try
            {
                json = JsonWriter.Serialize(BuildSaveData(), true);
                return true;
            }
            catch (Exception e)
            {
                reason = "Save failed: " + e.Message;
                Log.Error("GameSession.SaveGame: " + e);
                return false;
            }
        }

        /// <summary>The save DTO of the current state (no serialization).</summary>
        public SessionSaveData BuildSaveData()
        {
            var main = Main;
            var d = new SessionSaveData
            {
                version = SaveVersion,
                playerName = main?.Name ?? "",
                playerClass = main != null ? main.ClassId : ClassId.None,
                playerLevel = main?.Level ?? 1,
                mapId = MapId ?? "",
                mapName = MapDef?.name ?? "",
                day = day,
                gameHour = gameHour,
                playSeconds = playSeconds,
                leader = MemberId(Leader),
                rngS0 = Rng.s0.ToString("x16", CultureInfo.InvariantCulture),
                rngS1 = Rng.s1.ToString("x16", CultureInfo.InvariantCulture),
                gold = Inventory.Gold,
                settings = CopySettings(Settings),
                world = World.Save(),
            };
            foreach (var u in party) d.party.Add(MemberId(u));
            foreach (var it in Inventory.Items) d.inventory.Add(SaveItem(it));
            foreach (var u in roster) d.roster.Add(SaveUnit(u));
            d.approval = SortedInts(approval);
            var vids = new List<string>(vendors.Keys);
            vids.Sort(StringComparer.Ordinal);
            foreach (var id in vids)
            {
                var s = vendors[id];
                var v = new VendorSaveData { npc = id, stock = SortedInts(s.Stock) };
                foreach (var it in s.Buyback) v.buyback.Add(SaveItem(it));
                d.vendors.Add(v);
            }
            if (PendingLoot != null)
            {
                d.loot = new LootSaveData { source = PendingLoot.Source, title = PendingLoot.Title, gold = PendingLoot.Gold };
                foreach (var it in PendingLoot.Items) d.loot.items.Add(SaveItem(it));
            }
            d.suppressedEncounters = new List<string>(suppressedEncounters);
            d.suppressedEncounters.Sort(StringComparer.Ordinal);
            d.soothedEncounters = SortedFloats(soothedEncounters);
            return d;
        }

        static SessionSettings CopySettings(SessionSettings s) => new SessionSettings
        {
            CompanionAutoPlay = s.CompanionAutoPlay, CompanionAutoTrain = s.CompanionAutoTrain,
            AutoAllocateCompanionTalents = s.AutoAllocateCompanionTalents, GameHoursPerRealMinute = s.GameHoursPerRealMinute,
            StartHour = s.StartHour, VeteranGear = s.VeteranGear,
        };

        static Dictionary<string, int> SortedInts(Dictionary<string, int> src)
        {
            var keys = new List<string>(src.Keys);
            keys.Sort(StringComparer.Ordinal);
            var d = new Dictionary<string, int>();
            foreach (var k in keys) d[k] = src[k];
            return d;
        }

        static Dictionary<string, float> SortedFloats(Dictionary<string, float> src)
        {
            var keys = new List<string>(src.Keys);
            keys.Sort(StringComparer.Ordinal);
            var d = new Dictionary<string, float>();
            foreach (var k in keys) d[k] = src[k];
            return d;
        }

        static ItemSaveData SaveItem(ItemInstance it)
        {
            var s = new ItemSaveData { id = it.Def.id, count = it.Count, suffix = it.SuffixId ?? "", suffixName = it.SuffixName ?? "" };
            if (it.SuffixStats.Count > 0) s.suffixStats = new List<StatModDef>(it.SuffixStats);
            if (it.Generated) s.generated = it.Def;
            return s;
        }

        UnitSaveData SaveUnit(Unit u)
        {
            var s = new UnitSaveData
            {
                id = MemberId(u), name = u.Name, classId = u.ClassId, companion = u.Companion != null ? u.Companion.id : "",
                level = u.Level, xp = u.Xp, sprite = u.Sprite ?? "", portrait = u.Portrait ?? "",
                health = u.Health, mana = u.Mana, rage = u.Rage, energy = u.Energy, focus = u.Focus,
                position = u.Position, facing = u.Facing, autoPlay = u.AutoPlay, role = u.RoleOverride, respecCount = u.RespecCount,
                secondsSinceManaSpent = u.SecondsSinceManaSpent, secondsSinceCombat = u.SecondsSinceCombat,
                abilities = SortedInts(u.Abilities), talents = SortedInts(u.Talents),
                cooldowns = SortedFloats(u.Cooldowns), procCooldowns = SortedFloats(u.ProcCooldowns),
            };
            var lk = new Dictionary<string, float>();
            foreach (var kv in u.Lockouts) lk[kv.Key.ToString()] = kv.Value;
            s.lockouts = SortedFloats(lk);
            foreach (var kv in u.Equipment.Equipped) s.equipment.Add(new EquippedItemSaveData { slot = kv.Key, item = SaveItem(kv.Value) });
            SaveAuras(u, s.auras);
            if (u.HunterPet != null)
                s.hunterPet = new HunterPetSaveData { templateId = u.HunterPet.TemplateId ?? "", name = u.HunterPet.Name ?? "", healthFraction = u.HunterPet.HealthFraction, dead = u.HunterPet.Dead };
            var p = u.Pet;
            if (p != null && !p.Dead && p.IsPersistentPet && p.Creature != null)
            {
                var ps = new PetSaveData
                {
                    creature = p.Creature.id, name = p.Name, level = p.Level, health = p.Health, mana = p.Mana, rage = p.Rage,
                    energy = p.Energy, focus = p.Focus, position = p.Position, facing = p.Facing, autoPlay = p.AutoPlay,
                    abilities = SortedInts(p.Abilities), cooldowns = SortedFloats(p.Cooldowns), procCooldowns = SortedFloats(p.ProcCooldowns),
                };
                SaveAuras(p, ps.auras);
                s.pet = ps;
            }
            return s;
        }

        void SaveAuras(Unit u, List<AuraSaveData> into)
        {
            foreach (var a in u.Auras)
            {
                if (a.IsPassive || a.IsAreaChild || a.Def == null) continue;
                var s = new AuraSaveData
                {
                    id = a.Def.id, caster = CasterRef(u, a.Caster), duration = a.Duration, remaining = a.Remaining, stacks = a.Stacks,
                    charges = a.Charges, source = a.SourceAbility != null ? a.SourceAbility.id : "", rank = a.Rank, effLevel = a.EffLevel,
                    learnLevel = a.LearnLevel, comboPoints = a.ComboPoints, effectMult = a.EffectMult, damageMult = a.DamageMult,
                    healingMult = a.HealingMult, tickAccum = a.TickAccum, absorbLeft = a.AbsorbLeft, damageTaken = a.DamageTaken,
                };
                if (a.Vars != null && a.Vars.Count > 0) s.vars = SortedFloats(a.Vars);
                into.Add(s);
            }
        }

        string CasterRef(Unit bearer, Unit caster)
        {
            if (caster == null) return "";
            if (caster == bearer) return "self";
            if (roster.Contains(caster)) return MemberId(caster);
            if (caster.Owner != null && caster.Owner.Pet == caster && roster.Contains(caster.Owner)) return "pet:" + MemberId(caster.Owner);
            return "";
        }

        // ================================================================= load

        /// <summary>Reads only the header of a save (save-slot lists). Null when the text is not a save.</summary>
        public static SaveHeader ReadSaveHeader(string json)
        {
            try
            {
                return JsonMapper.FromJson<SaveHeader>(json, new JsonMapContext { ReportUnknownKeys = false }, "save");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Replaces this session's state with a save. On failure the session is unchanged and error says why.</summary>
        public bool LoadGame(string json, out string error)
        {
            error = null;
            SessionSaveData d;
            try
            {
                d = JsonMapper.FromJson<SessionSaveData>(json, new JsonMapContext { ReportUnknownKeys = false }, "save");
            }
            catch (Exception e)
            {
                error = "Not a valid save: " + e.Message;
                return false;
            }
            if (d == null) { error = "Not a valid save."; return false; }
            if (d.version > SaveVersion) { error = $"This save was made by a newer version of the game (v{d.version})."; return false; }
            if (d.version < 1) { error = "Unknown save version."; return false; }
            if (d.roster == null || d.roster.Count == 0 || Db.Class(d.roster[0].classId) == null) { error = "The save has no main character."; return false; }
            if (!Db.Maps.ContainsKey(d.mapId ?? "")) { error = $"Unknown map '{d.mapId}'."; return false; }
            foreach (var us in d.roster)
                if (Db.Class(us.classId) == null || (!string.IsNullOrEmpty(us.companion) && !Db.Companions.ContainsKey(us.companion)))
                { error = $"Unknown party member '{us.id}'."; return false; }

            SessionSaveData backup = null;
            if (CannotSaveReason() == null)
            {
                try { backup = JsonMapper.FromJson<SessionSaveData>(JsonWriter.Serialize(BuildSaveData(), false)); }
                catch (Exception) { backup = null; }
            }
            ResetState();
            try
            {
                resetting = true;
                ApplySave(d);
            }
            catch (Exception e)
            {
                resetting = false;
                Log.Error("GameSession.LoadGame: " + e);
                ResetState();
                error = "Load failed: " + e.Message;
                if (backup != null)
                {
                    try
                    {
                        resetting = true;
                        ApplySave(backup);
                        resetting = false;
                        hasGame = true;
                        RebuildField();
                        RestoreLoadedVitals();
                    }
                    catch (Exception) { resetting = false; ResetState(); }
                }
                return false;
            }
            finally { resetting = false; }

            hasGame = true;
            RebuildField();
            RestoreLoadedVitals();
            transitionArmed = Map.TransitionAt(Leader.Position) == null;
            lastPhase = TimeOfDay;
            Raise(new SessionEvent { Kind = SessionEventKind.GameLoaded, Unit = Main, Text = $"Loaded: {Main.Name}, level {Main.Level}." });
            Raise(new SessionEvent { Kind = SessionEventKind.MapEntered, Id = MapId, Id2 = "", Text = MapDef?.name ?? "" });
            if (LanternsRekindled) Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = RekindleLanternsSpecial, Amount = 0 });
            MarkFlagsChanged();   // FlagStore.Load replaces the flags without Changed events
            FlushFlagsChanged();
            return true;
        }

        void ApplySave(SessionSaveData d)
        {
            Settings = d.settings != null ? CopySettings(d.settings) : new SessionSettings();
            Rng.s0 = ParseHex(d.rngS0, Rng.s0);
            Rng.s1 = ParseHex(d.rngS1, Rng.s1);
            if (Rng.s0 == 0 && Rng.s1 == 0) Rng.s1 = 1;
            gameHour = d.gameHour;
            day = Math.Max(1, d.day);
            playSeconds = d.playSeconds;
            World.Load(d.world);

            Inventory.Gold = Math.Max(0, d.gold);
            if (d.inventory != null)
                foreach (var s in d.inventory)
                {
                    var it = LoadItem(s);
                    if (it != null) Inventory.Items.Add(it);
                }

            // units first (aura casters may reference other members), then auras
            var saves = new List<KeyValuePair<Unit, UnitSaveData>>();
            foreach (var s in d.roster)
            {
                var u = LoadCharacter(s, roster.Count == 0);
                roster.Add(u);
                saves.Add(new KeyValuePair<Unit, UnitSaveData>(u, s));
            }
            foreach (var kv in saves)
            {
                var u = kv.Key;
                var s = kv.Value;
                if (s.pet != null) LoadPet(u, s.pet);
            }
            foreach (var kv in saves)
            {
                LoadAuras(kv.Key, kv.Value.auras);
                if (kv.Value.pet != null && kv.Key.Pet != null) LoadAuras(kv.Key.Pet, kv.Value.pet.auras);
            }
            loadedVitals.Clear();
            foreach (var kv in saves)
            {
                var u = kv.Key;
                var s = kv.Value;
                u.InvalidateStats();
                // saves are made out of combat, where nobody stays down: a character saved at 0 health (downed by
                // field damage, older saves) loads standing at 1 health, never as a 0-health "alive" unit
                float health = Math.Max(1f, s.health);
                u.Health = MathUtil.Clamp(health, 0f, u.MaxHealth);
                u.Mana = MathUtil.Clamp(s.mana, 0f, u.MaxMana);
                u.Rage = s.rage; u.Energy = s.energy; u.Focus = s.focus;
                loadedVitals.Add(new LoadedVitals { Unit = u, Health = health, Mana = s.mana });
                if (u.Pet != null && s.pet != null)
                {
                    var p = u.Pet;
                    p.InvalidateStats();
                    p.Health = MathUtil.Clamp(s.pet.health, 0f, p.MaxHealth);
                    p.Mana = MathUtil.Clamp(s.pet.mana, 0f, p.MaxMana);
                    p.Rage = s.pet.rage; p.Energy = s.pet.energy; p.Focus = s.pet.focus;
                    loadedVitals.Add(new LoadedVitals { Unit = p, Health = s.pet.health, Mana = s.pet.mana });
                }
            }

            if (d.party != null)
                foreach (var id in d.party)
                {
                    var u = FindMember(id);
                    if (u != null && !party.Contains(u)) party.Add(u);
                }
            if (!party.Contains(Main)) party.Insert(0, Main);
            if (party[0] != Main) { party.Remove(Main); party.Insert(0, Main); }
            leader = FindMember(d.leader);
            if (leader == null || !party.Contains(leader)) leader = Main;

            if (d.approval != null) foreach (var kv in d.approval) approval[kv.Key] = kv.Value;

            if (d.vendors != null)
                foreach (var v in d.vendors)
                {
                    if (!Db.Npcs.TryGetValue(v.npc ?? "", out var npc)) continue;
                    var bb = new List<ItemInstance>();
                    if (v.buyback != null) foreach (var s in v.buyback) { var it = LoadItem(s); if (it != null) bb.Add(it); }
                    vendors[npc.id] = new VendorShop(Db, npc, v.stock ?? new Dictionary<string, int>(), bb);
                }

            if (d.loot != null)
            {
                var w = new LootWindow { Source = d.loot.source ?? "", Title = d.loot.title ?? "", Gold = d.loot.gold };
                if (d.loot.items != null) foreach (var s in d.loot.items) { var it = LoadItem(s); if (it != null) w.Items.Add(it); }
                PendingLoot = w;
            }

            SetMap(World.GetMap(d.mapId));
            if (d.suppressedEncounters != null)
            {
                foreach (var id in d.suppressedEncounters)
                    if (!string.IsNullOrEmpty(id) && Map.FindEncounter(id) != null) suppressedEncounters.Add(id);
            }
            else SuppressEncountersAroundParty();   // older saves: what stood next to the party could not have triggered
            if (d.soothedEncounters != null)
                foreach (var kv in d.soothedEncounters)
                    if (kv.Value > 0f && Map.FindEncounter(kv.Key) != null) soothedEncounters[kv.Key] = kv.Value;
        }

        /// <summary>Suppresses every available encounter with a living party member within its radius + 1 m (the release rule
        /// of UpdateSuppression): loading a save without the suppressed list never restarts the fight the party just left.</summary>
        void SuppressEncountersAroundParty()
        {
            if (Map?.Def?.encounters == null) return;
            foreach (var e in Map.Def.encounters)
            {
                if (e == null || !Map.IsEncounterAvailable(e)) continue;
                foreach (var u in party)
                    if (u.IsAlive && (u.Position - e.pos).Length <= e.radius + 1f) { suppressedEncounters.Add(e.id); break; }
            }
        }

        struct LoadedVitals
        {
            public Unit Unit;
            public float Health, Mana;
        }

        /// <summary>Health/mana read by ApplySave, re-applied once the exploration context has put area-aura children back.</summary>
        readonly List<LoadedVitals> loadedVitals = new List<LoadedVitals>();

        /// <summary>
        /// Area-aura children (Blood Pact…) are not saved: RebuildField re-applies them. Health and mana were clamped against
        /// the maxima without them, so they are set again from the save, clamped against the complete maxima.
        /// </summary>
        void RestoreLoadedVitals()
        {
            foreach (var v in loadedVitals)
            {
                var u = v.Unit;
                u.InvalidateStats();
                u.Health = MathUtil.Clamp(v.Health, 0f, u.MaxHealth);
                u.Mana = MathUtil.Clamp(v.Mana, 0f, u.MaxMana);
            }
            loadedVitals.Clear();
        }

        static ulong ParseHex(string s, ulong fallback) =>
            !string.IsNullOrEmpty(s) && ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        ItemInstance LoadItem(ItemSaveData s)
        {
            if (s == null) return null;
            ItemDef def;
            bool generated = s.generated != null;
            if (generated)
            {
                def = s.generated;
                def.stats ??= new List<StatModDef>();
                def.equipEffects ??= new List<PassiveDef>();
                def.classes ??= new ClassId[0];
            }
            else def = Db.Item(s.id);
            if (def == null)
            {
                Log.Warn($"GameSession.LoadGame: unknown item '{s.id}' dropped");
                return null;
            }
            var it = new ItemInstance(def, Math.Max(1, s.count)) { Generated = generated, SuffixId = s.suffix ?? "", SuffixName = s.suffixName ?? "" };
            if (s.suffixStats != null) it.SuffixStats.AddRange(s.suffixStats);
            // keep newly generated item ids from colliding with the ones we just loaded
            if (generated) ItemGenerator.EnsureCounterAbove(new[] { def.id });
            return it;
        }

        Unit LoadCharacter(UnitSaveData s, bool isMain)
        {
            var cls = Db.Class(s.classId);
            CompanionDef comp = null;
            if (!string.IsNullOrEmpty(s.companion)) Db.Companions.TryGetValue(s.companion, out comp);
            var u = new Unit(Db)
            {
                Name = s.name ?? "", Kind = comp != null ? UnitKind.Companion : UnitKind.Character, Team = Team.Player,
                Class = cls, Companion = comp, IsMainCharacter = isMain, Level = MathUtil.Clamp(s.level, 1, Math.Max(1, Db.Config.maxLevel)),
                Xp = s.xp, Sprite = s.sprite ?? "", Portrait = s.portrait ?? "", AutoPlay = s.autoPlay, RoleOverride = s.role,
                RespecCount = s.respecCount, Position = s.position, Facing = s.facing,
                SecondsSinceManaSpent = s.secondsSinceManaSpent, SecondsSinceCombat = s.secondsSinceCombat,
            };
            if (s.abilities != null) foreach (var kv in s.abilities) if (Db.Ability(kv.Key) != null) u.Abilities[kv.Key] = kv.Value;
            if (s.talents != null) foreach (var kv in s.talents) if (Db.Talent(kv.Key) != null) u.Talents[kv.Key] = kv.Value;
            if (s.cooldowns != null) foreach (var kv in s.cooldowns) u.Cooldowns[kv.Key] = kv.Value;
            if (s.procCooldowns != null) foreach (var kv in s.procCooldowns) u.ProcCooldowns[kv.Key] = kv.Value;
            if (s.lockouts != null)
                foreach (var kv in s.lockouts)
                    if (Enum.TryParse<School>(kv.Key, true, out var sc)) u.Lockouts[sc] = kv.Value;
            if (s.equipment != null)
                foreach (var e in s.equipment)
                {
                    var it = LoadItem(e.item);
                    if (it == null) continue;
                    if (u.Equipment[e.slot] != null) { Inventory.Items.Add(it); continue; }
                    foreach (var displaced in EquipmentRules.Equip(u, it, e.slot)) if (displaced != null) Inventory.Items.Add(displaced);
                }
            if (s.hunterPet != null)
                u.HunterPet = new HunterPetState { TemplateId = s.hunterPet.templateId ?? "", Name = s.hunterPet.name ?? "", HealthFraction = s.hunterPet.healthFraction, Dead = s.hunterPet.dead };
            UnitFactory.AttachPassives(u);
            u.InvalidateStats();
            return u;
        }

        void LoadPet(Unit owner, PetSaveData s)
        {
            var def = Db.Creature(s.creature);
            if (def == null) { Log.Warn($"GameSession.LoadGame: unknown pet creature '{s.creature}' dropped"); return; }
            var p = UnitFactory.CreateSummon(Db, def, owner, UnitKind.Pet, -1f);
            p.Name = s.name ?? def.name;
            p.Level = Math.Max(1, s.level);
            p.Position = s.position;
            p.Facing = s.facing;
            p.AutoPlay = s.autoPlay;
            if (s.abilities != null && s.abilities.Count > 0)
            {
                p.Abilities.Clear();
                foreach (var kv in s.abilities) if (Db.Ability(kv.Key) != null) p.Abilities[kv.Key] = kv.Value;
            }
            // plus what its level allows (saves from before pets learned abilities on level-up lack them)
            UnitFactory.LearnCreatureAbilities(p);
            if (s.cooldowns != null) foreach (var kv in s.cooldowns) p.Cooldowns[kv.Key] = kv.Value;
            if (s.procCooldowns != null) foreach (var kv in s.procCooldowns) p.ProcCooldowns[kv.Key] = kv.Value;
            p.InvalidateStats();
            owner.Pet = p;
        }

        void LoadAuras(Unit u, List<AuraSaveData> list)
        {
            if (list == null) return;
            // non-passive auras that a fresh unit already carries (none for loaded units) are replaced by the saved ones
            foreach (var s in list)
            {
                var def = Db.Aura(s.id);
                if (def == null) continue;
                var caster = ResolveCaster(u, s.caster);
                var inst = UnitFactory.AttachAura(u, def, caster, false, s.rank, s.effLevel, s.learnLevel, s.remaining > 0 ? s.remaining : -1f);
                inst.Duration = s.duration;
                inst.Remaining = s.remaining;
                inst.Stacks = Math.Max(1, s.stacks);
                inst.Charges = s.charges;
                inst.SourceAbility = string.IsNullOrEmpty(s.source) ? null : Db.Ability(s.source);
                inst.ComboPoints = s.comboPoints;
                inst.EffectMult = s.effectMult;
                inst.DamageMult = s.damageMult;
                inst.HealingMult = s.healingMult;
                inst.TickAccum = s.tickAccum;
                inst.AbsorbLeft = s.absorbLeft;
                inst.DamageTaken = s.damageTaken;
                if (s.vars != null && s.vars.Count > 0) foreach (var kv in s.vars) inst.SetVar(kv.Key, kv.Value);
                UnitFactory.RefreshModValues(inst);
            }
            u.InvalidateStats();
        }

        Unit ResolveCaster(Unit bearer, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            if (reference == "self") return bearer;
            if (reference.StartsWith("pet:", StringComparison.Ordinal)) return FindMember(reference.Substring(4))?.Pet;
            return FindMember(reference);
        }
    }
}
