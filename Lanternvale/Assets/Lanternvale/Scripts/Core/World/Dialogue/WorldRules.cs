// Shared evaluation of data-driven conditions and outcomes (dialogue nodes/choices, quest stage onComplete).
using System;
using System.Collections.Generic;
using System.Globalization;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    public static class WorldRules
    {
        /// <summary>Flag set by the Recruit outcome: recruited_&lt;companionId&gt;.</summary>
        public const string RecruitedFlagPrefix = "recruited_";
        /// <summary>Default encounter done flag: enc_&lt;encounterId&gt;.</summary>
        public const string EncounterFlagPrefix = "enc_";

        public static string RecruitedFlag(string companionId) => RecruitedFlagPrefix + companionId;
        public static string EncounterDoneFlag(string encounterId) => EncounterFlagPrefix + encounterId;

        // ------------------------------------------------------------------ conditions

        /// <summary>True when every condition holds (null/empty list → true).</summary>
        public static bool CheckAll(IList<ConditionDef> conditions, IDialogueContext ctx)
        {
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Count; i++)
                if (!Check(conditions[i], ctx)) return false;
            return true;
        }

        /// <summary>Evaluates one condition. See Docs/WorldAPI.md §4 for the exact semantics of each type.</summary>
        public static bool Check(ConditionDef c, IDialogueContext ctx)
        {
            if (c == null) return true;
            string key = c.key ?? "", value = c.value ?? "";
            switch (c.type)
            {
                case ConditionType.Flag: return FlagHolds(c, ctx);
                case ConditionType.NotFlag: return !FlagHolds(c, ctx);
                case ConditionType.QuestState:
                {
                    var status = QuestStatusOf(ctx, key);
                    if (value.Length == 0) return status == QuestStatus.Active;
                    if (TryParseQuestStatus(value, out var wanted)) return status == wanted;
                    return status == QuestStatus.Active && string.Equals(ctx?.Quests?.GetStage(key), value, StringComparison.Ordinal);
                }
                case ConditionType.QuestNotStarted: return QuestStatusOf(ctx, key) == QuestStatus.NotStarted;
                case ConditionType.QuestActive:
                    return QuestStatusOf(ctx, key) == QuestStatus.Active &&
                           (value.Length == 0 || string.Equals(ctx?.Quests?.GetStage(key), value, StringComparison.Ordinal));
                case ConditionType.QuestComplete: return QuestStatusOf(ctx, key) == QuestStatus.Completed;
                case ConditionType.HasItem: return ctx != null && ctx.CountItem(key) >= Math.Max(1, c.amount);
                case ConditionType.NotHasItem: return ctx == null || ctx.CountItem(key) < Math.Max(1, c.amount);
                case ConditionType.Gold: return ctx != null && ctx.Gold >= c.amount;
                case ConditionType.Class: return ClassHolds(key, value, ctx);
                case ConditionType.NotClass: return !ClassHolds(key, value, ctx);
                case ConditionType.Level:
                {
                    var main = MainMember(ctx);
                    return main != null && main.level >= c.amount;
                }
                case ConditionType.InParty: return FindMember(ctx, key) != null;
                case ConditionType.NotInParty: return FindMember(ctx, key) == null;
                case ConditionType.Companion: return ctx != null && ctx.GetApproval(key) >= c.amount;
                case ConditionType.TimeOfDay:
                {
                    var tod = ctx?.TimeOfDay ?? "";
                    var wanted = key.Length > 0 ? key : value;
                    foreach (var part in wanted.Split(',', '|'))
                        if (string.Equals(part.Trim(), tod, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                }
            }
            return true;
        }

        static bool FlagHolds(ConditionDef c, IDialogueContext ctx)
        {
            var flags = ctx?.Flags;
            if (flags == null) return false;
            int v = flags.Get(c.key);
            if (!string.IsNullOrEmpty(c.value) && int.TryParse(c.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int exact))
                return v == exact;
            if (c.amount > 0) return v >= c.amount;
            return v != 0;
        }

        static QuestStatus QuestStatusOf(IDialogueContext ctx, string questId) =>
            ctx?.Quests != null ? ctx.Quests.GetStatus(questId) : QuestStatus.NotStarted;

        public static bool TryParseQuestStatus(string s, out QuestStatus status)
        {
            status = QuestStatus.NotStarted;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "notstarted": case "not_started": case "none": status = QuestStatus.NotStarted; return true;
                case "active": case "started": case "inprogress": case "in_progress": status = QuestStatus.Active; return true;
                case "completed": case "complete": case "done": status = QuestStatus.Completed; return true;
                case "failed": case "fail": status = QuestStatus.Failed; return true;
            }
            return false;
        }

        static bool ClassHolds(string key, string value, IDialogueContext ctx)
        {
            if (!Enum.TryParse<ClassId>(key, true, out var cls)) return false;
            if (string.Equals(value, "party", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "any", StringComparison.OrdinalIgnoreCase))
            {
                var party = ctx?.Party;
                if (party == null) return false;
                for (int i = 0; i < party.Count; i++)
                    if (party[i] != null && party[i].classId == cls) return true;
                return false;
            }
            var main = MainMember(ctx);
            return main != null && main.classId == cls;
        }

        /// <summary>The member flagged isMain, else Party[0]; null for an empty party.</summary>
        public static PartyMemberInfo MainMember(IDialogueContext ctx)
        {
            var party = ctx?.Party;
            if (party == null || party.Count == 0) return null;
            for (int i = 0; i < party.Count; i++)
                if (party[i] != null && party[i].isMain) return party[i];
            return party[0];
        }

        public static PartyMemberInfo FindMember(IDialogueContext ctx, string id)
        {
            var party = ctx?.Party;
            if (party == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < party.Count; i++)
                if (party[i] != null && string.Equals(party[i].id, id, StringComparison.Ordinal)) return party[i];
            return null;
        }

        // ------------------------------------------------------------------ outcomes

        /// <summary>
        /// Outcomes that take the player out of the conversation (combat, travel, shop/trainer screens, resting).
        /// DialogueRunner defers them until the dialogue ends.
        /// </summary>
        public static bool IsInterrupting(OutcomeType t) =>
            t == OutcomeType.StartCombat || t == OutcomeType.Teleport || t == OutcomeType.OpenVendor ||
            t == OutcomeType.OpenTrainer || t == OutcomeType.OpenRespec || t == OutcomeType.Rest;

        public static void ExecuteAll(IList<OutcomeDef> outcomes, IDialogueContext ctx, string ownerId = "")
        {
            if (outcomes == null) return;
            for (int i = 0; i < outcomes.Count; i++) Execute(outcomes[i], ctx, ownerId);
        }

        /// <summary>Applies one outcome. EndDialogue is a no-op here (the DialogueRunner handles it).</summary>
        public static void Execute(OutcomeDef o, IDialogueContext ctx, string ownerId = "")
        {
            if (o == null || ctx == null) return;
            string key = o.key ?? "", value = o.value ?? "";
            switch (o.type)
            {
                case OutcomeType.SetFlag:
                {
                    int v = o.amount;
                    if (v == 0 && !(value.Length > 0 && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))) v = 1;
                    ctx.Flags?.Set(key, v);
                    break;
                }
                case OutcomeType.ClearFlag: ctx.Flags?.Clear(key); break;
                case OutcomeType.StartQuest: ctx.Quests?.Start(key); break;
                case OutcomeType.SetQuestStage: ctx.Quests?.SetStage(key, value); break;
                case OutcomeType.CompleteQuest: ctx.Quests?.Complete(key); break;
                case OutcomeType.FailQuest: ctx.Quests?.Fail(key); break;
                case OutcomeType.GiveItem: ctx.GiveItem(key, Math.Max(1, o.amount)); break;
                case OutcomeType.TakeItem: ctx.TakeItem(key, Math.Max(1, o.amount)); break;
                case OutcomeType.GiveGold: if (o.amount != 0) ctx.GiveGold(o.amount); break;
                case OutcomeType.TakeGold: if (o.amount != 0) ctx.TakeGold(o.amount); break;
                case OutcomeType.GiveXP: if (o.amount != 0) ctx.GiveXP(o.amount); break;
                case OutcomeType.Recruit:
                    ctx.Flags?.Set(RecruitedFlag(key), 1);
                    ctx.Recruit(key);
                    break;
                case OutcomeType.Dismiss: ctx.Dismiss(key); break;
                case OutcomeType.StartCombat: ctx.StartCombat(key); break;
                case OutcomeType.OpenVendor: ctx.OpenVendor(key.Length > 0 ? key : ownerId ?? ""); break;
                case OutcomeType.OpenTrainer: ctx.OpenTrainer(key.Length > 0 ? key : ownerId ?? ""); break;
                case OutcomeType.OpenRespec: ctx.OpenRespec(key.Length > 0 ? key : ownerId ?? ""); break;
                case OutcomeType.Rest: ctx.Rest(); break;
                case OutcomeType.HealParty: ctx.HealParty(); break;
                case OutcomeType.Teleport: ctx.Teleport(key, value.Length > 0 ? value : "default"); break;
                case OutcomeType.Approval: if (o.amount != 0) ctx.ChangeApproval(key, o.amount); break;
                case OutcomeType.EndDialogue: break;
                case OutcomeType.Special: ctx.RunSpecial(key, o); break;
                default: Log.Warn($"WorldRules: unhandled outcome {o.type}"); break;
            }
        }
    }
}
