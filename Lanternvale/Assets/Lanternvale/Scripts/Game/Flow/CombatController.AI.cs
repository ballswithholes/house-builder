// CombatController — AI pacing. When the presentation queue is idle and the active unit is AI-controlled
// (enemies, pets, totems, summons, auto-played companions, controlled party members), the controller asks the
// rules AI for ONE step, previews movement paths briefly, executes the step, lets the queue present the produced
// events and pauses a little before the next step. Guards: a step that changes nothing twice in a row, or too many
// steps in one turn, ends the turn.
using System;
using Lanternvale.Data;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class CombatController
    {
        const float AiFirstThink = 0.2f;      // before the first step of a turn (after the TurnStart beat)
        const float AiStepPause = 0.12f;      // between steps (after the previous step finished animating)
        const float AiPathPreview = 0.3f;     // a Move step's path is shown this long before walking
        const int AiStepGuardExtra = 8;       // AI.MaxStepsPerTurn + this → end turn

        static readonly Color AiEnemyPathColor = new Color(1f, 0.55f, 0.45f, 0.85f);
        static readonly Color AiAllyPathColor = new Color(0.62f, 0.9f, 1f, 0.85f);

        Unit aiUnit;
        int aiTurnNumber = -1;
        float aiWait;
        int aiNoChange, aiSteps;
        AIStep aiPreview;

        void DriveAI(float dt)
        {
            var u = Battle.ActiveUnit;
            if (u == null) return;
            if (u != aiUnit || u.TurnsTaken != aiTurnNumber)
            {
                aiUnit = u;
                aiTurnNumber = u.TurnsTaken;
                aiNoChange = 0;
                aiSteps = 0;
                aiPreview = null;
                aiWait = AiFirstThink;
                FxSystem.Hide(AiPathId);
            }
            if (aiWait > 0f) { aiWait -= dt; return; }

            if (aiPreview != null)
            {
                var s = aiPreview;
                aiPreview = null;
                FxSystem.Hide(AiPathId);
                ExecuteAI(u, s);
                aiWait = AiStepPause;
                return;
            }

            AIStep step;
            try { step = AI.NextStep(Battle, u); }
            catch (Exception e) { LogOnce("ai-next:" + e.GetType().Name, "AI.NextStep failed for " + u.Name + ": " + e); step = null; }
            if (step == null || step.Unit == null) step = AIStep.End(u, "no step");
            aiSteps++;
            if (aiSteps > AI.MaxStepsPerTurn + AiStepGuardExtra) step = AIStep.End(u, "step guard");

            if (step.Kind == AIStepKind.Move && step.Path != null && step.Path.Count >= 2 && V(u) != null)
            {
                FxSystem.ShowPath(AiPathId, step.Path, u.Team == Battle.PlayerTeam ? AiAllyPathColor : AiEnemyPathColor, true);
                aiPreview = step;
                aiWait = AiPathPreview;
                return;
            }
            ExecuteAI(u, step);
            aiWait = AiStepPause;
        }

        void ExecuteAI(Unit u, AIStep step)
        {
            int before = Battle.Events.Count;
            var pos = u.Position;
            float time = u.TimeLeft, move = u.MoveLeft;
            var intent = IntentFor(step);
            ActionResult r;
            try { r = AI.Execute(Battle, step); }
            catch (Exception e)
            {
                LogOnce("ai-exec:" + e.GetType().Name, "AI.Execute failed for " + u.Name + " (" + step + "): " + e);
                r = ActionResult.Fail("AI error");
            }
            // same fallback as GameSession.RunAIStep: an EndTurn that failed still has to end the turn
            if (!r.Ok && step.Kind == AIStepKind.EndTurn && Battle.ActiveUnit == u && !Battle.IsOver) ForceEndTurn(u);

            bool changed = Battle.Events.Count != before || Battle.ActiveUnit != u || Battle.IsOver ||
                           u.Position != pos || Math.Abs(u.TimeLeft - time) > 1e-4f || Math.Abs(u.MoveLeft - move) > 1e-4f;
            PullEvents(intent);
            if (changed) { aiNoChange = 0; return; }
            if (++aiNoChange >= 2 && Battle.ActiveUnit == u && !Battle.IsOver)
            {
                LogOnce("ai-stuck:" + u.Name, "AI for " + u.Name + " made no progress twice; ending its turn.");
                ForceEndTurn(u);
                PullEvents(null);
                aiNoChange = 0;
            }
        }

        void ForceEndTurn(Unit u)
        {
            try { Battle.EndTurn(u); }
            catch (Exception e) { LogOnce("ai-endturn", "EndTurn failed for " + u.Name + ": " + e); }
        }

        Intent IntentFor(AIStep step)
        {
            if (step == null) return null;
            switch (step.Kind)
            {
                case AIStepKind.UseAbility:
                {
                    var a = Db?.Ability(step.AbilityId);
                    if (a == null) return null;
                    return new Intent { Kind = IntentKind.Ability, Actor = step.Unit, Ability = a, Target = step.Target, Point = step.Point };
                }
                case AIStepKind.UseItem:
                {
                    var a = step.Item != null && step.Item.Def != null ? Db?.Ability(step.Item.Def.use) : null;
                    if (a == null) return null;
                    return new Intent { Kind = IntentKind.Ability, Actor = step.Unit, Ability = a, Item = step.Item, Target = step.Target, Point = step.Point };
                }
                case AIStepKind.Move:
                    return new Intent { Kind = IntentKind.Move, Actor = step.Unit };
                default:
                    return null;
            }
        }
    }
}
