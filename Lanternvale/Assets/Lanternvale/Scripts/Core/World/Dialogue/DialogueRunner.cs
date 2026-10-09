// Dialogue state machine over DialogueDef data. Pure C#: the UI reads `Current` and calls Choose/Continue.
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    /// <summary>What the dialogue UI shows for the current node.</summary>
    public sealed class DialogueView
    {
        public string DialogueId = "";
        public string NodeId = "";
        /// <summary>Resolved speaker id: npc/companion id, "player" or "narrator".</summary>
        public string SpeakerId = "";
        public string SpeakerName = "";
        /// <summary>Portrait art key from the npc/companion definition ("" when unknown).</summary>
        public string Portrait = "";
        /// <summary>Node text with tokens substituted.</summary>
        public string Text = "";
        /// <summary>Visible choices (conditions met, used `once` choices hidden).</summary>
        public readonly List<ChoiceView> Choices = new List<ChoiceView>();
        /// <summary>No choices: the UI shows a "Continue" button.</summary>
        public bool CanContinue => Choices.Count == 0;
        /// <summary>Continue will end the dialogue.</summary>
        public bool IsLast;
    }

    public sealed class ChoiceView
    {
        /// <summary>Index in <see cref="DialogueView.Choices"/> (pass to Choose).</summary>
        public int Index;
        /// <summary>Index in the node's `choices` list.</summary>
        public int SourceIndex;
        /// <summary>Choice text with tokens substituted (no tag).</summary>
        public string Text = "";
        /// <summary>"[PALADIN] text", or "[PERSUASION] text" for an untagged check.</summary>
        public string DisplayText = "";
        /// <summary>Upper-case tag without brackets ("" when none).</summary>
        public string Tag = "";
        public bool Once;
        /// <summary>Picked before (BG3 greys these out).</summary>
        public bool PreviouslyChosen;
        /// <summary>Check preview (roller, modifier, chance) or null when the choice has no roll.</summary>
        public CheckPreview Check;
        /// <summary>Choice ends the conversation (no next node, no check).</summary>
        public bool Ends;
        public ChoiceDef Def;
    }

    public sealed class DialogueRunner
    {
        public GameDatabase Db { get; set; }
        public IDialogueContext Context { get; set; }
        public DialogueMemory Memory { get; }
        public Rng Rng { get; set; }

        /// <summary>Defer StartCombat/Teleport/OpenVendor/OpenTrainer/OpenRespec/Rest until the dialogue ends (default true).</summary>
        public bool DeferInterruptingOutcomes = true;
        /// <summary>Safety limit on fallback/auto-skip hops per step.</summary>
        public int MaxAutoSteps = 64;

        public bool IsActive { get; private set; }
        /// <summary>True when no dialogue is running (ended or never started).</summary>
        public bool IsFinished => !IsActive;
        public DialogueView Current { get; private set; }
        public DialogueDef Dialogue { get; private set; }
        public DialogueNodeDef CurrentNode { get; private set; }
        /// <summary>Npc/companion the party is talking to ("" when none).</summary>
        public string OwnerId { get; private set; } = "";
        /// <summary>Result of the check rolled by the last choice (null when it had none).</summary>
        public CheckResult LastCheck { get; private set; }
        public ChoiceView LastChoice { get; private set; }
        /// <summary>Interrupting outcomes waiting for the dialogue to end.</summary>
        public IReadOnlyList<OutcomeDef> PendingOutcomes => deferred;

        public event Action<DialogueView> NodeEntered;
        public event Action<CheckResult> CheckRolled;
        public event Action<ChoiceView> ChoiceMade;
        /// <summary>Raised after the dialogue ended and deferred outcomes ran; argument is the dialogue id.</summary>
        public event Action<string> Ended;

        readonly List<OutcomeDef> deferred = new List<OutcomeDef>();
        bool endAfterNode;

        public DialogueRunner(GameDatabase db, IDialogueContext ctx, DialogueMemory memory = null, Rng rng = null)
        {
            Db = db;
            Context = ctx;
            Memory = memory ?? new DialogueMemory();
            Rng = rng ?? new Rng();
        }

        /// <summary>Starts a dialogue from the database. ownerId = npc/companion talked to. False if unknown.</summary>
        public bool Start(string dialogueId, string ownerId = "")
        {
            DialogueDef def = null;
            if (Db != null && dialogueId != null) Db.Dialogues.TryGetValue(dialogueId, out def);
            if (def == null)
            {
                Log.Warn($"DialogueRunner: unknown dialogue '{dialogueId}'");
                return false;
            }
            return Start(def, ownerId);
        }

        /// <summary>Starts a dialogue definition directly (ad-hoc or test dialogues).</summary>
        public bool Start(DialogueDef def, string ownerId = "")
        {
            if (def == null) return false;
            if (IsActive) End();
            Dialogue = def;
            OwnerId = ownerId ?? "";
            IsActive = true;
            deferred.Clear();
            LastCheck = null;
            LastChoice = null;
            Current = null;
            CurrentNode = null;
            Memory.MarkStarted(def.id);
            EnterNode(def.start);
            return true;
        }

        /// <summary>Picks a visible choice. Returns false when the index is invalid.</summary>
        public bool Choose(int index)
        {
            if (!IsActive || Current == null || index < 0 || index >= Current.Choices.Count) return false;
            var cv = Current.Choices[index];
            var c = cv.Def;
            Memory.MarkChosen(Dialogue.id, CurrentNode.id, cv.SourceIndex);
            LastChoice = cv;
            LastCheck = null;
            ChoiceMade?.Invoke(cv);
            endAfterNode = false;
            RunOutcomes(c.outcomes);
            if (!IsActive) return true;
            if (endAfterNode)
            {
                Finish();
                return true;
            }
            if (c.check != null)
            {
                var res = SkillChecks.Roll(Context, c.check.skill, c.check.dc, Rng);
                LastCheck = res;
                CheckRolled?.Invoke(res);
                EnterNode(res.Success ? c.check.success : c.check.failure);
            }
            else EnterNode(c.next);
            return true;
        }

        /// <summary>Advances a node without choices to its `next` node (or ends the dialogue).</summary>
        public bool Continue()
        {
            if (!IsActive || Current == null || Current.Choices.Count > 0) return false;
            LastCheck = null;
            if (endAfterNode || string.IsNullOrEmpty(CurrentNode?.next))
            {
                Finish();
                return true;
            }
            EnterNode(CurrentNode.next);
            return true;
        }

        /// <summary>Ends the dialogue now (deferred outcomes still run).</summary>
        public void End() => Finish();

        // ------------------------------------------------------------------ internals

        DialogueNodeDef FindNode(string id)
        {
            if (Dialogue?.nodes == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Dialogue.nodes.Count; i++)
                if (Dialogue.nodes[i] != null && Dialogue.nodes[i].id == id) return Dialogue.nodes[i];
            return null;
        }

        void EnterNode(string nodeId)
        {
            string id = nodeId;
            for (int guard = 0; ; guard++)
            {
                if (!IsActive) return;
                if (guard > MaxAutoSteps)
                {
                    Log.Warn($"DialogueRunner: '{Dialogue.id}' exceeded {MaxAutoSteps} automatic steps (loop?)");
                    Finish();
                    return;
                }
                if (string.IsNullOrEmpty(id)) { Finish(); return; }
                var node = FindNode(id);
                if (node == null)
                {
                    Log.Warn($"DialogueRunner: '{Dialogue.id}' has no node '{id}'");
                    Finish();
                    return;
                }
                if (!WorldRules.CheckAll(node.conditions, Context))
                {
                    if (!string.IsNullOrEmpty(node.fallback)) { id = node.fallback; continue; }
                    Finish();
                    return;
                }
                CurrentNode = node;
                endAfterNode = false;
                RunOutcomes(node.outcomes);
                if (!IsActive) return;
                var view = BuildView(node);
                if (endAfterNode)
                {
                    view.Choices.Clear();
                    view.IsLast = true;
                }
                if (string.IsNullOrWhiteSpace(node.text) && view.Choices.Count == 0)
                {
                    // logic/router node: nothing to show
                    if (endAfterNode || string.IsNullOrEmpty(node.next)) { Finish(); return; }
                    id = node.next;
                    continue;
                }
                Current = view;
                NodeEntered?.Invoke(view);
                return;
            }
        }

        void RunOutcomes(List<OutcomeDef> outcomes)
        {
            if (outcomes == null) return;
            for (int i = 0; i < outcomes.Count; i++)
            {
                var o = outcomes[i];
                if (o == null) continue;
                if (o.type == OutcomeType.EndDialogue) { endAfterNode = true; continue; }
                if (DeferInterruptingOutcomes && WorldRules.IsInterrupting(o.type)) { deferred.Add(o); continue; }
                WorldRules.Execute(o, Context, OwnerId);
            }
        }

        DialogueView BuildView(DialogueNodeDef node)
        {
            var view = new DialogueView
            {
                DialogueId = Dialogue.id,
                NodeId = node.id,
                Text = Substitute(node.text),
            };
            ResolveSpeaker(node.speaker, view);
            if (node.choices != null)
            {
                for (int i = 0; i < node.choices.Count; i++)
                {
                    var c = node.choices[i];
                    if (c == null || !WorldRules.CheckAll(c.conditions, Context)) continue;
                    bool chosenBefore = Memory.HasChosen(Dialogue.id, node.id, i);
                    if (c.once && chosenBefore) continue;
                    var cv = new ChoiceView
                    {
                        Index = view.Choices.Count,
                        SourceIndex = i,
                        Text = Substitute(c.text),
                        Tag = NormalizeTag(c.tag),
                        Once = c.once,
                        PreviouslyChosen = chosenBefore,
                        Def = c,
                    };
                    if (c.check != null) cv.Check = SkillChecks.Preview(Context, c.check.skill, c.check.dc);
                    cv.Ends = c.check == null && string.IsNullOrEmpty(c.next);
                    string prefix = cv.Tag.Length > 0 ? cv.Tag : (c.check != null ? SkillChecks.DisplayName(c.check.skill) : "");
                    cv.DisplayText = prefix.Length > 0 ? $"[{prefix}] {cv.Text}" : cv.Text;
                    view.Choices.Add(cv);
                }
            }
            view.IsLast = view.Choices.Count == 0 && string.IsNullOrEmpty(node.next);
            return view;
        }

        static string NormalizeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            var t = tag.Trim();
            if (t.StartsWith("[") && t.EndsWith("]") && t.Length >= 2) t = t.Substring(1, t.Length - 2).Trim();
            return t.ToUpperInvariant();
        }

        void ResolveSpeaker(string speaker, DialogueView view)
        {
            string sid = string.IsNullOrEmpty(speaker) ? OwnerId : speaker;
            view.SpeakerId = sid ?? "";
            if (string.IsNullOrEmpty(sid)) return;
            if (sid == "player")
            {
                view.SpeakerName = Context?.PlayerName ?? "";
                return;
            }
            if (sid == "narrator") return;
            if (Db != null)
            {
                if (Db.Npcs.TryGetValue(sid, out var npc))
                {
                    view.SpeakerName = npc.name;
                    view.Portrait = npc.portrait;
                    return;
                }
                if (Db.Companions.TryGetValue(sid, out var comp))
                {
                    view.SpeakerName = comp.name;
                    view.Portrait = comp.portrait;
                    return;
                }
            }
            var member = WorldRules.FindMember(Context, sid);
            view.SpeakerName = member != null ? member.name : sid;
        }

        void Finish()
        {
            if (!IsActive) return;
            IsActive = false;
            Current = null;
            var id = Dialogue?.id ?? "";
            if (!string.IsNullOrEmpty(OwnerId)) Context?.Quests?.OnTalk(OwnerId);
            if (deferred.Count > 0)
            {
                var pending = deferred.ToArray();
                deferred.Clear();
                foreach (var o in pending) WorldRules.Execute(o, Context, OwnerId);
            }
            Ended?.Invoke(id);
        }

        // ------------------------------------------------------------------ text tokens

        /// <summary>Replaces {player}, {class} (main character) and {companion:&lt;id&gt;}. Unknown tokens are kept.</summary>
        public string Substitute(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        var token = text.Substring(i + 1, close - i - 1);
                        var rep = ResolveToken(token);
                        if (rep != null)
                        {
                            sb.Append(rep);
                            i = close + 1;
                            continue;
                        }
                    }
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        string ResolveToken(string token)
        {
            var t = token.Trim();
            if (string.Equals(t, "player", StringComparison.OrdinalIgnoreCase)) return Context?.PlayerName ?? "";
            if (string.Equals(t, "class", StringComparison.OrdinalIgnoreCase))
            {
                var main = WorldRules.MainMember(Context);
                return main != null ? ClassName(main.classId) : "";
            }
            if (t.StartsWith("companion:", StringComparison.OrdinalIgnoreCase))
            {
                var id = t.Substring("companion:".Length).Trim();
                if (Db != null && Db.Companions.TryGetValue(id, out var comp) && !string.IsNullOrEmpty(comp.name)) return comp.name;
                var m = WorldRules.FindMember(Context, id);
                return m != null ? m.name : id;
            }
            return null;
        }

        string ClassName(ClassId c)
        {
            var def = Db?.Class(c);
            return def != null && !string.IsNullOrEmpty(def.name) ? def.name : c.ToString();
        }
    }
}
