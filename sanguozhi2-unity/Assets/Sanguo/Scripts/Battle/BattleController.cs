using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sanguo
{
    // 战斗流程：玩家操作、电脑行动、动画与界面
    public class BattleController
    {
        BattleModel M; BattleView V; int playerSide; bool autoPlayer, endTurn;
        Vector2? tap;
        RectTransform hud; Text topText, cardText; RectTransform card;
        static GameState G { get { return GameState.Current; } }
        static Color SideColor(int s) { return s == 0 ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.5f, 0.42f); }

        public static IEnumerator Run(BattleSetup s, int playerSide)
        {
            var bc = new BattleController { playerSide = playerSide };
            yield return bc.Main(s);
        }

        IEnumerator Main(BattleSetup s)
        {
            M = new BattleModel(s);
            V = BattleView.Create(M);
            var rig = Game.I.Rig;
            var oldBounds = rig.Bounds; var oldTarget = rig.Desired; var oldDist = rig.DesiredDistance; float oldMin = rig.MinDist, oldMax = rig.MaxDist;
            Game.I.Map.Root.gameObject.SetActive(false);
            rig.Bounds = new Rect(BattleView.Origin.x, BattleView.Origin.z, M.W * BattleView.T, M.H * BattleView.T);
            rig.MinDist = 12; rig.MaxDist = 62;
            rig.Focus(V.BoardCenter - new Vector3(0, 0, 2), 40, true);
            rig.OnTap += OnTap;
            Sfx.Music("battle");
            BuildHud();
            string an = G.factions[s.attacker].name, dn = s.defender >= 0 ? G.factions[s.defender].name : "守军";
            UIKit.Banner(s.target.name + "之战", an + "军 进攻 " + dn + "军", 2.2f);
            Sfx.Play("horn");
            yield return new WaitForSeconds(1.6f);

            while (M.result == 0)
            {
                UpdateHud();
                bool human = !autoPlayer && M.side == playerSide;
                if (human) yield return PlayerPhase(); else yield return AiPhase();
                if (M.result != 0) break;
                V.ClearHighlights(); V.SetCursor(null);
                bool newDay = M.EndSide();
                foreach (var u in M.units) V.Refresh(u);
                foreach (var u in M.units.Where(x => !x.alive && V.Vis(x).gameObject.activeSelf)) yield return V.Rout(u);
                if (newDay)
                {
                    foreach (var l in M.dayLog) UIKit.Toast(l, 2.5f);
                    if (M.result == 0) UIKit.Toast("第 " + M.day + " 日", 1.2f);
                }
            }
            V.ClearHighlights(); V.SetCursor(null);
            UpdateHud();
            s.attackerWon = M.result == 1;
            bool playerWon = playerSide >= 0 && (M.result == 1) == (playerSide == 0);
            Sfx.Play(playerSide < 0 ? "horn" : playerWon ? "win" : "lose");
            UIKit.Banner(M.result == 1 ? "攻方胜利" : "守方胜利", M.resultReason, 3f);
            yield return new WaitForSeconds(2.6f);

            rig.OnTap -= OnTap;
            Object.Destroy(hud.gameObject);
            Object.Destroy(V.gameObject);
            Game.I.Map.Root.gameObject.SetActive(true);
            rig.Bounds = oldBounds; rig.MinDist = oldMin; rig.MaxDist = oldMax; rig.Focus(oldTarget, oldDist, true);
            Sfx.Music("map");
        }

        void OnTap(Vector2 p) { tap = p; }

        // ---------------------------------------------------------- 界面 --
        void BuildHud()
        {
            hud = UIKit.NewRect("BattleHUD", UIKit.Screens); UIKit.Stretch(hud);
            var top = UIKit.Panel(hud, "Top");
            UIKit.Place(top, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(980, 64));
            topText = UIKit.Label(top, "", 24, UIKit.Text, TextAnchor.MiddleCenter);
            UIKit.Stretch(topText.rectTransform, 16, 0, 16, 0);
            var end = UIKit.Button(hud, "结束回合", () => endTurn = true, 28, true);
            UIKit.Place(end.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(220, 70));
            UIKit.Btn auto = null;
            auto = UIKit.Button(hud, "委任作战", () => { autoPlayer = !autoPlayer; endTurn = true; auto.label.text = autoPlayer ? "亲自指挥" : "委任作战"; }, 26);
            UIKit.Place(auto.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-260, 24), new Vector2(200, 70));
            if (playerSide < 0) { end.rt.gameObject.SetActive(false); auto.rt.gameObject.SetActive(false); }
            card = UIKit.Panel(hud, "Card");
            UIKit.Place(card, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(430, 210));
            cardText = UIKit.Label(card, "", 22, UIKit.Text, TextAnchor.UpperLeft);
            cardText.lineSpacing = 1.15f;
            UIKit.Stretch(cardText.rectTransform, 24, 16, 20, 18);
            card.gameObject.SetActive(false);
        }

        void UpdateHud()
        {
            string side = M.side == 0 ? "<color=#73bfff>攻方</color>" : "<color=#ff806b>守方</color>";
            string dots = new string('●', Mathf.Max(0, M.ap)) + new string('○', Mathf.Max(0, M.ActionPoints(M.side) - M.ap));
            int t0 = M.Alive(0).Sum(u => u.Troops), t1 = M.Alive(1).Sum(u => u.Troops);
            topText.text = string.Format("第 <b>{0}</b>/{1} 日　{2}行动　行动力 <color=#f3c969>{3}</color>　｜　攻 {4}  粮{5}　守 {6}  粮{7}",
                Mathf.Min(M.day, Balance.BattleDays), Balance.BattleDays, side, dots, t0, M.food[0], t1, M.food[1]);
        }

        void ShowCard(BUnit u)
        {
            if (u == null) { card.gameObject.SetActive(false); return; }
            card.gameObject.SetActive(true);
            var f = u.Form;
            cardText.text = string.Format("<size=30><b>{0}</b></size>  <color=#a8a194>{1}{2}</color>\n兵力 <b>{3}</b>　士气 {4}　训练 {5}\n武力 {6}　智力 {7}\n阵型 <color=#f3c969>{8}</color>（攻×{9:0.00} 防×{10:0.00}）\n地形 {11}{12}",
                u.gen.name, u.side == 0 ? "攻方" : "守方", u.commander ? "·主将" : "", u.Troops, u.morale, u.gen.training, u.gen.war, u.gen.intel,
                f.Name, f.Atk, f.Def, Defs.TerrainName(M.map[u.x, u.y]), u.confused > 0 ? "　<color=#c79bff>混乱中</color>" : "");
        }

        // ---------------------------------------------------------- 玩家 --
        IEnumerator PlayerPhase()
        {
            endTurn = false; tap = null;
            BUnit sel = null;
            UIKit.Toast("我军行动：点选部队", 1.4f);
            while (!endTurn && M.result == 0 && M.ap > 0 && M.Alive(M.side).Any(M.CanAct))
            {
                UpdateHud();
                if (sel != null) { V.SetCursor(sel); ShowCard(sel); }
                tap = null;
                while (tap == null && !endTurn) yield return null;
                if (endTurn) break;
                Vector2Int t;
                if (!V.TileFromScreen(Game.I.Rig.Cam, tap.Value, out t)) continue;
                var at = M.UnitAt(t.x, t.y);
                if (sel == null)
                {
                    if (at != null) ShowCard(at);
                    if (at != null && at.side == M.side && M.CanAct(at)) { sel = at; ShowRange(sel); Sfx.Click(); }
                    continue;
                }
                if (at == sel) { V.ClearHighlights(); yield return ActionMenu(sel); sel = null; V.SetCursor(null); continue; }
                var moves = M.MoveTargets(sel);
                if (at == null && moves.Contains(t))
                {
                    V.ClearHighlights();
                    var path = M.PathTo(sel, t);
                    yield return V.MoveUnit(sel, path);
                    M.Move(sel, t);
                    if (M.result != 0) break;
                    V.SetCursor(sel);
                    yield return ActionMenu(sel, true);
                    sel = null; V.SetCursor(null);
                    continue;
                }
                if (at != null && at.side != M.side && BattleModel.Dist(at, sel) == 1)
                {
                    V.ClearHighlights();
                    yield return DoAttack(sel, at);
                    M.Spend(sel); sel = null; V.SetCursor(null);
                    continue;
                }
                if (at != null && at.side == M.side && M.CanAct(at)) { sel = at; ShowRange(sel); continue; }
                if (at != null) ShowCard(at);
                sel = null; V.ClearHighlights(); V.SetCursor(null);
            }
            V.ClearHighlights(); V.SetCursor(null); ShowCard(null);
        }

        void ShowRange(BUnit u)
        {
            V.ClearHighlights();
            V.Highlight(M.MoveTargets(u).Where(p => p.x != u.x || p.y != u.y), new Color(0.35f, 0.7f, 1f, 0.55f));
            V.Highlight(M.AdjacentEnemies(u).Select(e => new Vector2Int(e.x, e.y)), new Color(1f, 0.3f, 0.25f, 0.7f));
        }

        IEnumerator ActionMenu(BUnit u, bool moved = false)
        {
            var adj = M.AdjacentEnemies(u);
            var items = new List<UIKit.Item>
            {
                new UIKit.Item("攻击", adj.Count > 0 ? adj.Count + " 支敌军相邻" : "无相邻敌军", adj.Count > 0),
                new UIKit.Item("策略", "智力 " + u.gen.intel, u.gen.intel >= 40),
                new UIKit.Item("单挑", "武力 " + u.gen.war, adj.Count > 0),
                new UIKit.Item("阵型", u.Form.Name),
                new UIKit.Item("待机"),
            };
            var r = new Ref<int>();
            yield return UIKit.Choose(u.gen.name + " 的行动", items, r, null, 520);
            switch (r.v)
            {
                case 0: { var tgt = new Ref<BUnit>(); yield return PickEnemy(adj, "攻击目标", tgt); if (tgt.v != null) { yield return DoAttack(u, tgt.v); M.Spend(u); } else if (moved) M.Spend(u); break; }
                case 1: yield return TacticMenu(u, moved); break;
                case 2: { var tgt = new Ref<BUnit>(); yield return PickEnemy(adj, "单挑对手", tgt); if (tgt.v != null) { yield return DoDuel(u, tgt.v); M.Spend(u); } else if (moved) M.Spend(u); break; }
                case 3: yield return FormationMenu(u); break;
                case 4: M.Spend(u); break;
                default: if (moved) M.Spend(u); break;
            }
            foreach (var x in M.units) V.Refresh(x);
        }

        IEnumerator PickEnemy(List<BUnit> list, string title, Ref<BUnit> result)
        {
            result.v = null;
            if (list.Count == 1) { result.v = list[0]; yield break; }
            var r = new Ref<int>();
            yield return UIKit.Choose(title, list.Select(e => new UIKit.Item(e.gen.name, "兵 " + e.Troops + "　武 " + e.gen.war + "　智 " + e.gen.intel)).ToList(), r, null, 560);
            if (r.v >= 0) result.v = list[r.v];
        }

        IEnumerator TacticMenu(BUnit u, bool moved)
        {
            var items = new List<UIKit.Item>();
            foreach (var t in Defs.Tactics)
            {
                string why; bool ok = M.TacticUsable(u, t, out why) && M.TacticTargets(u, t).Count > 0;
                if (why == "" && !ok) why = "范围内无目标";
                items.Add(new UIKit.Item(t.Name, ok ? t.Desc : why, ok));
            }
            var r = new Ref<int>();
            yield return UIKit.Choose("策略", items, r, null, 640);
            if (r.v < 0) { if (moved) M.Spend(u); yield break; }
            var tac = Defs.Tactics[r.v];
            var targets = M.TacticTargets(u, tac);
            var tgt = new Ref<BUnit>();
            if (tac.Kind == TacticKind.Inspire) tgt.v = u;
            else yield return PickEnemy(targets, tac.Name + "的目标", tgt);
            if (tgt.v == null) { if (moved) M.Spend(u); yield break; }
            yield return DoTactic(u, tac, tgt.v);
            M.Spend(u);
        }

        IEnumerator FormationMenu(BUnit u)
        {
            var items = Defs.Formations.Select((f, i) => new UIKit.Item(f.Name + (i == u.formation ? "（当前）" : ""),
                M.FormationAllowed(u, i) ? string.Format("攻×{0:0.00} 防×{1:0.00} 机动{2:+0;-0;0}", f.Atk, f.Def, f.Move) : (f.ReqInt > 0 ? "需智力 " + f.ReqInt : "需武力 " + f.ReqWar),
                M.FormationAllowed(u, i), f.Desc)).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose("变换阵型（消耗本回合行动）", items, r, null, 760);
            if (r.v < 0 || r.v == u.formation) yield break;
            M.ChangeFormation(u, r.v);
            V.FloatText(V.Tile(u.x, u.y) + Vector3.up * 1.8f, Defs.Formations[r.v].Name + "之阵", UIKit.Gold, 34);
            Sfx.Play("horn", 0.4f);
            M.Spend(u);
        }

        // ---------------------------------------------------------- 动作 --
        IEnumerator DoAttack(BUnit a, BUnit t)
        {
            ShowCard(a);
            yield return V.Lunge(a, t);
            var r = M.Attack(a, t);
            Sfx.Play("hit");
            V.Hit(t, r.dmg);
            if (r.counter > 0) { yield return new WaitForSeconds(0.15f); V.Hit(a, r.counter); }
            V.Refresh(a); V.Refresh(t);
            yield return new WaitForSeconds(0.35f);
            if (!t.alive) { UIKit.Toast(t.gen.name + "部溃散！"); yield return V.Rout(t); }
            if (!a.alive) { UIKit.Toast(a.gen.name + "部溃散！"); yield return V.Rout(a); }
        }

        IEnumerator DoTactic(BUnit u, TacticDef tac, BUnit t)
        {
            UIKit.Toast(u.gen.name + "施展「" + tac.Name + "」！", 1.5f);
            var res = M.UseTactic(u, tac, t);
            switch (tac.Kind)
            {
                case TacticKind.Fire: yield return V.FireFx(t); break;
                case TacticKind.Rockfall: yield return V.RockFx(t); break;
                case TacticKind.Confuse: yield return V.MagicFx(t, new Color(0.75f, 0.5f, 1f)); break;
                case TacticKind.Inspire: yield return V.MagicFx(u, new Color(1f, 0.85f, 0.4f)); break;
            }
            if (!res.Key) V.FloatText(V.Tile(t.x, t.y) + Vector3.up * 1.6f, "识破", UIKit.Muted, 34);
            else if (res.Value > 0) V.Hit(t, res.Value, true);
            else if (tac.Kind == TacticKind.Confuse) V.FloatText(V.Tile(t.x, t.y) + Vector3.up * 1.6f, "混乱", new Color(0.8f, 0.6f, 1f), 38);
            else if (tac.Kind == TacticKind.Inspire) V.FloatText(V.Tile(u.x, u.y) + Vector3.up * 1.6f, "士气高涨", UIKit.Good, 34);
            foreach (var x in M.units) V.Refresh(x);
            yield return new WaitForSeconds(0.4f);
            if (!t.alive) { UIKit.Toast(t.gen.name + "部溃散！"); yield return V.Rout(t); }
        }

        IEnumerator DoDuel(BUnit a, BUnit b)
        {
            var res = M.Duel(a, b);
            if (!res.accepted)
            {
                yield return UIKit.Say(b.gen.name + "：哼，匹夫之勇，不足与战！\n（" + b.gen.name + "拒绝单挑，其部士气下降）", b.gen.name, SideColor(b.side));
                foreach (var x in M.units) V.Refresh(x);
                yield break;
            }
            yield return UIKit.Say(a.gen.name + "：" + b.gen.name + "，可敢与我一战？", a.gen.name, SideColor(a.side));
            // 单挑画面
            var m = UIKit.OpenModal(980, 420);
            var title = UIKit.Label(m.panel, "单　挑", 52, UIKit.Gold, TextAnchor.MiddleCenter, true);
            UIKit.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(400, 70));
            System.Func<BUnit, float, RectTransform> side = (u, x) =>
            {
                var medal = UIKit.Medal(m.panel, u.gen.name.Substring(0, 1), SideColor(u.side), 130);
                UIKit.Place(medal.rectTransform, new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(130, 130));
                var n = UIKit.Label(m.panel, u.gen.name + "　武力 " + u.gen.war, 26, UIKit.Text, TextAnchor.MiddleCenter, true);
                UIKit.Place(n.rectTransform, new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(380, 40));
                var bar = UIKit.Bar(m.panel, 1, SideColor(u.side));
                UIKit.Place(bar, new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -122), new Vector2(340, 18));
                return (RectTransform)bar.Find("Fill");
            };
            var fa = side(a, 0.25f); var fb = side(b, 0.75f);
            var vs = UIKit.Label(m.panel, "VS", 60, UIKit.Cinnabar, TextAnchor.MiddleCenter, true);
            UIKit.Place(vs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(160, 90));
            var log = UIKit.Label(m.panel, "", 24, UIKit.Muted, TextAnchor.MiddleCenter);
            UIKit.Place(log.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(900, 40));
            yield return new WaitForSeconds(0.6f);
            int round = 0;
            foreach (var rd in res.rounds)
            {
                round++;
                Sfx.Play("duel");
                fa.anchorMax = new Vector2(rd.hpA / 100f, 1); fb.anchorMax = new Vector2(rd.hpB / 100f, 1);
                var striker = rd.who == 0 ? a : b;
                log.text = string.Format("第 {0} 合　{1}一击，造成 {2} 伤害", round, striker.gen.name, rd.dmg);
                vs.transform.localScale = Vector3.one * 1.3f;
                for (float t = 0; t < 0.32f; t += Time.deltaTime) { vs.transform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, t / 0.32f); yield return null; }
            }
            var winner = res.winner == 0 ? a : b; var loser = res.winner == 0 ? b : a;
            log.text = string.Format("<color=#f3c969>{0}</color>击败了{1}！", winner.gen.name, loser.gen.name);
            Sfx.Play("win", 0.5f);
            yield return new WaitForSeconds(1.4f);
            m.Close();
            foreach (var x in M.units) V.Refresh(x);
            yield return V.Rout(loser);
        }

        // ---------------------------------------------------------- 电脑 --
        IEnumerator AiPhase()
        {
            yield return new WaitForSeconds(0.3f);
            var order = M.Alive(M.side).Where(M.CanAct).OrderBy(u => u.commander ? 1 : 0)
                .ThenBy(u => M.Alive(1 - M.side).Select(e => BattleModel.Dist(e, u)).DefaultIfEmpty(99).Min()).ToList();
            foreach (var u in order)
            {
                if (M.result != 0 || M.ap <= 0) break;
                if (!M.CanAct(u)) continue;
                if (endTurn && playerSide >= 0 && M.side == playerSide && !autoPlayer) break;
                var plan = M.PlanFor(u);
                Game.I.Rig.Focus(V.Tile(u.x, u.y));
                ShowCard(u); V.SetCursor(u);
                if (plan.move.HasValue)
                {
                    var path = M.PathTo(u, plan.move.Value);
                    yield return V.MoveUnit(u, path);
                    M.Move(u, plan.move.Value);
                    V.SetCursor(u);
                    if (M.result != 0) break;
                }
                var act = M.PlanAction(u);
                if (act.kind == "attack") yield return DoAttack(u, act.target);
                else if (act.kind == "tactic") yield return DoTactic(u, act.tactic, act.target);
                else if (act.kind == "duel") yield return DoDuel(u, act.target);
                M.Spend(u);
                UpdateHud();
                yield return new WaitForSeconds(0.2f);
            }
            V.SetCursor(null); ShowCard(null);
        }
    }
}
