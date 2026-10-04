// Engine services used by special handlers: self-resurrection turns, side changes (Enslave Demon,
// Mind Control), extra swings (Windfury, Reckoning), summoning helpers.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed partial class Battle
    {
        // ================================================================ self-resurrection

        /// <summary>The downed/dead active unit may accept a self-resurrection (Soulstone, Reincarnation).</summary>
        public SelfResOffer PendingSelfResurrection(Unit u) => u != null && u == ActiveUnit && u.IsDeadOrDowned ? u.SelfRes : null;

        bool StartSelfResTurn(Unit u)
        {
            ActiveUnit = u;
            u.InOwnTurn = true;
            Emit(new CombatEvent { Type = CombatEventType.TurnStart, Source = u, Target = u, Reason = "self-resurrection offer" });
            Emit(new CombatEvent { Type = CombatEventType.Log, Source = u, Target = u, Name = u.SelfRes.Name, Text = $"{u.Name} may use {u.SelfRes.Name} to rise." });
            if (IsAIControlled(u) || u.AutoPlay || u.Team != PlayerTeam) return AcceptSelfResurrectionInternal(u);
            return true; // waiting for AcceptSelfResurrection / DeclineSelfResurrection
        }

        /// <summary>Accepts the pending self-resurrection: the unit stands up and takes its turn normally.</summary>
        public ActionResult AcceptSelfResurrection(Unit u)
        {
            if (PendingSelfResurrection(u) == null) return ActionResult.Fail("No resurrection offer.");
            bool waiting = AcceptSelfResurrectionInternal(u);
            if (!waiting && ActiveUnit == null && !IsOver) AdvanceTurn();
            return ActionResult.Success;
        }

        bool AcceptSelfResurrectionInternal(Unit u)
        {
            var o = u.SelfRes;
            u.SelfRes = null;
            u.InOwnTurn = false;
            ActiveUnit = null;
            Revive(u, o.Health, o.Mana, u, o.Name);
            o.OnAccept?.Invoke(this, u);
            return StartTurn(u);
        }

        /// <summary>Declines the offer: the unit stays downed (allies can still help it up).</summary>
        public ActionResult DeclineSelfResurrection(Unit u)
        {
            if (PendingSelfResurrection(u) == null) return ActionResult.Fail("No resurrection offer.");
            u.SelfRes = null;
            u.InOwnTurn = false;
            Emit(new CombatEvent { Type = CombatEventType.TurnEnd, Source = u, Target = u });
            ActiveUnit = null;
            AdvanceTurn();
            return ActionResult.Success;
        }

        // ================================================================ side changes

        /// <summary>
        /// Moves a unit to another side (Enslave Demon, Mind Control). With <paramref name="newOwner"/> it becomes that
        /// unit's pet (one pet per owner) and acts right after it. Threat tables involving the unit are cleared.
        /// </summary>
        public void ChangeSide(Unit u, Team team, Unit newOwner, bool asPet)
        {
            if (u == null) return;
            if (!u.OriginalTeam.HasValue) { u.OriginalTeam = u.Team; u.OriginalKind = u.Kind; u.OriginalOwner = u.Owner; }
            CancelPending(u, "changed sides", null, 0f);
            u.Team = team;
            ClearThreatOf(u);
            if (newOwner != null)
            {
                if (asPet)
                {
                    if (newOwner.Pet != null && newOwner.Pet != u && Units.Contains(newOwner.Pet)) RemoveUnit(newOwner.Pet, "dismissed");
                    newOwner.Pet = u;
                    u.Kind = UnitKind.Pet;
                }
                u.Owner = newOwner;
                u.AutoPlay = newOwner.AutoPlay;
                int idx = TurnOrder.IndexOf(u);
                if (idx >= 0) { TurnOrder.RemoveAt(idx); if (idx <= TurnIndex) TurnIndex--; }
                if (Started && InCombat) InsertIntoTurnOrder(u);
            }
            if (u.Team != PlayerTeam) EnsureThreatEntries(u);
            foreach (var e in Units)
                if (e.IsAlive && e.Team != PlayerTeam && e.IsHostileTo(u) && !e.Threat.ContainsKey(u)) e.Threat[u] = 0f;
            Emit(new CombatEvent { Type = CombatEventType.Log, Source = newOwner ?? u, Target = u, Text = $"{u.Name} now fights for {(team == PlayerTeam ? "the party" : "the enemy")}." });
            if (newOwner != null && asPet) Specials.OnPetChanged(this, newOwner);
        }

        /// <summary>Returns a unit to its original side; <paramref name="angryAt"/> gets top threat on its table.</summary>
        public void RestoreSide(Unit u, Unit angryAt)
        {
            if (u == null || !u.OriginalTeam.HasValue) return;
            var owner = u.Owner;
            if (owner != null && owner.Pet == u) owner.Pet = null;
            u.Team = u.OriginalTeam.Value;
            u.Kind = u.OriginalKind;
            u.Owner = u.OriginalOwner;
            u.OriginalTeam = null;
            u.OriginalOwner = null;
            ClearThreatOf(u);
            int idx = TurnOrder.IndexOf(u);
            if (idx >= 0) { TurnOrder.RemoveAt(idx); if (idx <= TurnIndex) TurnIndex--; }
            if (Started && InCombat && u.IsAlive) InsertIntoTurnOrder(u);
            if (u.Team != PlayerTeam && u.IsAlive)
            {
                EnsureThreatEntries(u);
                if (angryAt != null && angryAt.IsAlive) { u.Threat[angryAt] = 1000000f; u.AggroTarget = angryAt; }
            }
            Emit(new CombatEvent { Type = CombatEventType.Log, Source = u, Target = u, Text = $"{u.Name} breaks free!" });
            if (owner != null) Specials.OnPetChanged(this, owner);
        }

        void ClearThreatOf(Unit u)
        {
            u.Threat.Clear();
            u.AggroTarget = null;
            u.AttackTarget = null;
            u.AutoAttacking = false;
            u.TauntedBy = null;
            foreach (var o in Units)
            {
                o.Threat.Remove(u);
                if (o.AttackTarget == u && !o.IsHostileTo(u)) { o.AttackTarget = null; o.AutoAttacking = false; }
                if (o.AggroTarget == u) o.AggroTarget = null;
            }
        }

        // ================================================================ extra swings

        /// <summary>
        /// Performs an extra melee swing outside the swing timer (Windfury, Reckoning) with optional bonus attack power.
        /// It uses the normal hit table and can crit and trigger on-hit procs (except <paramref name="fromProc"/>).
        /// </summary>
        public void ExtraSwing(Unit u, Unit target, float apBonus, object fromProc = null, WeaponSlot slot = WeaponSlot.MainHand)
        {
            if (u == null || target == null || !u.IsAlive || !target.IsAlive || !InMeleeReach(u, target)) return;
            bool added = fromProc != null && runningProcs.Add(fromProc);
            try { WhiteSwing(u, target, slot, apBonus); }
            finally { if (added) runningProcs.Remove(fromProc); }
        }

        /// <summary>Summons a creature as the owner's pet or guardian (used by Call Pet, Tame Beast...).</summary>
        public Unit SummonUnit(Unit owner, CreatureDef def, UnitKind kind, float lifetime, Vec2 at, string displayName = null)
        {
            if (kind == UnitKind.Pet && owner.Pet != null && Units.Contains(owner.Pet)) RemoveUnit(owner.Pet, "dismissed");
            var u = UnitFactory.CreateSummon(Db, def, owner, kind, lifetime);
            if (!string.IsNullOrEmpty(displayName)) u.Name = displayName;
            u.Position = Targeting.FreeSpotNear(this, at, u.Radius, u.Id, owner.Facing);
            u.Facing = owner.Facing;
            if (kind == UnitKind.Pet) owner.Pet = u; else owner.Summons.Add(u);
            AddUnit(u);
            Emit(new CombatEvent { Type = CombatEventType.Summon, Source = owner, Target = u, Name = u.Name, To = u.Position });
            ApplyPassives(u);
            if (kind == UnitKind.Pet) Specials.OnPetChanged(this, owner);
            return u;
        }

        /// <summary>Removes the owner's pet without killing it (Dismiss Pet).</summary>
        public void DismissPet(Unit owner)
        {
            var p = owner?.Pet;
            if (p == null) return;
            owner.Pet = null;
            if (Units.Contains(p)) RemoveUnit(p, "dismissed");
            Specials.OnPetChanged(this, owner);
        }
    }
}
