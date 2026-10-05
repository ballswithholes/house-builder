// Autopilot: plays a scripted tour of the real game and saves screenshots, for unattended runs (cloud
// builds, CI, quick visual checks). It drives the same public APIs the UI uses — GameFlow, the
// CombatController and GameSession — so what it captures is what a player would see.
//
// Enable with command-line arguments (player or editor):
//   -lv-autopilot              run the tour
//   -lv-shots <dir>            screenshot folder (default: <persistentDataPath>/autopilot)
//   -lv-class <ClassId>        hero class (default Paladin)
//   -lv-level <1-60>           start level (default 12, a "veteran start" so talents and gear show)
//   -lv-quit                   quit when the tour is done
// or set the environment variable LANTERNVALE_AUTOPILOT=1.
using System;
using System.Collections;
using System.IO;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class Autopilot : MonoBehaviour
    {
        string shotDir;
        int shotIndex;
        ClassId heroClass = ClassId.Paladin;
        int startLevel = 12;
        bool quitWhenDone;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            bool on = Arg("-lv-autopilot") != null || Environment.GetEnvironmentVariable("LANTERNVALE_AUTOPILOT") == "1";
            if (!on) return;
            var go = new GameObject("Lanternvale Autopilot");
            DontDestroyOnLoad(go);
            go.AddComponent<Autopilot>();
        }

        void Awake()
        {
            shotDir = Arg("-lv-shots");
            if (string.IsNullOrEmpty(shotDir)) shotDir = Path.Combine(Application.persistentDataPath, "autopilot");
            Directory.CreateDirectory(shotDir);
            var cls = Arg("-lv-class");
            if (!string.IsNullOrEmpty(cls) && Enum.TryParse(cls, true, out ClassId parsed) && parsed != ClassId.None) heroClass = parsed;
            var lvl = Arg("-lv-level");
            if (!string.IsNullOrEmpty(lvl) && int.TryParse(lvl, out var l)) startLevel = Mathf.Clamp(l, 1, 60);
            quitWhenDone = Arg("-lv-quit") != null;
            Debug.Log($"[Autopilot] class={heroClass} level={startLevel} shots={shotDir}");
        }

        IEnumerator Start()
        {
            yield return Until(() => GameFlow.Instance != null, 30f);
            var flow = GameFlow.Instance;
            if (flow == null) { Finish("GameFlow never started"); yield break; }

            // 1. title screen over the backdrop diorama
            yield return Wait(4f);
            Shot("main_menu");

            // 2. new game
            flow.StartNewGame(new NewGameOptions { Name = "Aria", Class = heroClass, StartLevel = startLevel });
            yield return Until(() => GameFlow.HasGame, 30f);
            if (!GameFlow.HasGame) { Finish("new game failed: " + flow.LastError); yield break; }
            var s = flow.Session;
            yield return Wait(3f);

            // 3. opening dialogue: capture it, then answer with the first choice until it ends
            int steps = 0;
            while (s.Mode == SessionMode.Dialogue && steps++ < 80)
            {
                if (steps == 2) Shot("opening_dialogue");
                var cur = s.Dialogue?.Current;
                if (cur == null) break;
                if (cur.CanContinue) s.ContinueDialogue(); else s.ChooseDialogue(0);
                yield return Wait(0.8f);
            }
            if (s.Mode == SessionMode.Dialogue) s.EndDialogue();

            // 4. the village by day, then the main windows
            s.SetTime(11f);
            yield return Wait(3f);
            Shot("lanternvale_day");
            foreach (var panel in new[] { UiPanels.Character, UiPanels.Inventory, UiPanels.Spellbook, UiPanels.Talents, UiPanels.Journal })
            {
                UiRoot.CloseAll();
                UiRoot.Open(panel);
                yield return Wait(1.2f);
                Shot("panel_" + panel);
            }
            UiRoot.CloseAll();

            // 5. a fight against the training dummy, using real abilities each turn
            s.StartEncounter("enc_training_dummy");
            yield return Until(() => flow.Combat != null, 10f);
            if (flow.Combat != null)
            {
                for (int turn = 0; turn < 6 && flow.Combat != null && !flow.Combat.Finished; turn++)
                {
                    yield return Until(() => flow.Combat == null || flow.Combat.IsPlayerTurn, 30f);
                    if (flow.Combat == null || !flow.Combat.IsPlayerTurn) break;
                    var unit = flow.Combat.ActiveUnit;
                    var target = FirstEnemy(flow.Combat.Battle, unit);
                    var ability = PickAttack(s, unit);
                    if (ability != null && target != null)
                    {
                        flow.Combat.BeginAbility(ability);
                        if (flow.Combat.IsTargeting) flow.Combat.TargetUnit(target);
                    }
                    yield return Wait(1.5f);
                    Shot($"combat_turn{turn + 1}");
                    yield return Until(() => flow.Combat == null || flow.Combat.IsPlayerTurn, 15f);
                    if (flow.Combat != null && flow.Combat.IsPlayerTurn) flow.Combat.EndTurn();
                }
                if (flow.Combat != null) flow.Combat.Disengage();
                yield return Until(() => flow.Combat == null, 20f);
            }

            // 6. night lighting, then the other two dioramas
            s.SetTime(22f);
            yield return Wait(4f);
            Shot("lanternvale_night");
            s.EnterMap("whisperwood", "default");
            s.SetTime(11f);
            yield return Wait(5f);
            Shot("whisperwood");
            s.EnterMap("shrine", "default");
            yield return Wait(5f);
            Shot("shrine");

            Finish("tour complete");
        }

        static Unit FirstEnemy(Battle b, Unit u)
        {
            if (b == null || u == null) return null;
            foreach (var e in b.EnemiesOf(u, false))
                if (e != null && !e.IsDeadOrDowned) return e;
            return null;
        }

        static string PickAttack(GameSession s, Unit u)
        {
            if (s == null || u == null) return null;
            foreach (var st in s.GetAbilityBar(u, false))
            {
                if (st == null || st.Ability == null || !st.Usable || !st.NeedsTarget) continue;
                var hint = st.Ability.aiHint ?? "";
                if (hint == "Damage" || hint == "Opener" || hint == "Finisher") return st.Ability.id;
            }
            return null;
        }

        void Shot(string name)
        {
            var file = Path.Combine(shotDir, $"{++shotIndex:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log("[Autopilot] screenshot " + file);
        }

        void Finish(string why)
        {
            Debug.Log("[Autopilot] " + why);
            if (quitWhenDone) StartCoroutine(QuitSoon());
        }

        IEnumerator QuitSoon()
        {
            yield return Wait(2f); // let the last screenshot flush
            Application.Quit();
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        static IEnumerator Until(Func<bool> cond, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok;
                try { ok = cond(); } catch (Exception) { ok = false; }
                if (ok) yield break;
                yield return null;
            }
        }
    }
}
