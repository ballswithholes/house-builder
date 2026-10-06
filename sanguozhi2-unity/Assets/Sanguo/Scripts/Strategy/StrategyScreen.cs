using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sanguo
{
    // 战略画面：顶栏、城池情报、指令与月份循环
    public class StrategyScreen : MonoBehaviour
    {
        static GameState G { get { return GameState.Current; } }
        RectTransform hud, cityPanel, cmdGrid, genList;
        Text topLeft, topCenter, cityTitle, cityStats;
        UIKit.Btn endBtn;
        readonly Dictionary<int, Text> labels = new Dictionary<int, Text>();
        int selected = -1;
        bool busy, endMonth;

        // ---------------------------------------------------------- 主循环 --
        public IEnumerator Run()
        {
            BuildHud();
            Game.I.Rig.OnTap += OnMapTap;
            Sfx.Music("map");
            var cap = G.cities[G.Ruler(G.player).city];
            Game.I.Rig.Focus(new Vector3(cap.MapPos.x, 0, cap.MapPos.y), 48);
            SelectCity(cap.id);
            UIKit.Banner(G.year + "年 " + G.month + "月", G.PlayerFaction.name + "军");
            while (true)
            {
                endMonth = false;
                RefreshAll();
                while (!endMonth) yield return null;
                busy = true;
                RefreshAll();
                UIKit.Toast("诸侯行动中……", 1.2f);
                yield return new WaitForSeconds(0.4f);
                var news = new List<string>();
                var battles = StrategyAI.RunAI(news);
                Game.I.Map.Refresh(G); RefreshAll();
                foreach (var b in battles) { yield return Defend(b); if (!G.PlayerFaction.alive) break; }
                if (G.PlayerFaction.alive) news.AddRange(StrategyAI.EndMonth());
                Game.I.Map.Refresh(G); RefreshAll();
                if (!G.PlayerFaction.alive) { yield return GameOver(false); yield break; }
                if (G.cities.All(c => c.owner == G.player)) { yield return GameOver(true); yield break; }
                if (news.Count > 0) yield return UIKit.Say(string.Join("\n", news.Take(6).ToArray()));
                try { G.Save(); } catch (System.Exception e) { Debug.LogWarning(e); }
                UIKit.Banner(G.year + "年 " + G.month + "月", "令牌 " + G.tokens + " 枚", 1.8f);
                busy = false;
            }
        }

        IEnumerator GameOver(bool won)
        {
            busy = true;
            if (won)
            {
                Sfx.Play("win");
                UIKit.Banner("天下统一", G.Ruler(G.player).name + "终成霸业", 4f);
                yield return new WaitForSeconds(2.5f);
                yield return UIKit.Say(string.Format("{0}年{1}月，{2}平定四海，一统天下。\n麾下武将 {3} 员，历时 {4} 年。", G.year, G.month, G.Ruler(G.player).name, G.GeneralsOf(G.player).Count(), G.year - ScenarioData.StartYear));
            }
            else
            {
                Sfx.Play("lose");
                UIKit.Banner("势力灭亡", "霸业未成，身先陨落……", 4f);
                yield return new WaitForSeconds(3f);
            }
            try { System.IO.File.Delete(GameState.SavePath); } catch { }
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex < 0 ? 0 : UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void OnMapTap(Vector2 p)
        {
            if (UIKit.AnyModal) return;
            int id = Game.I.Map.Pick(Game.I.Rig.Cam, p);
            SelectCity(id);
        }

        // ---------------------------------------------------------- 界面 --
        void BuildHud()
        {
            hud = UIKit.NewRect("StrategyHUD", UIKit.Screens); UIKit.Stretch(hud);
            var top = UIKit.Panel(hud, "TopBar");
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(14, -92); top.offsetMax = new Vector2(-14, -12);
            topLeft = UIKit.Label(top, "", 30, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Stretch(topLeft.rectTransform, 28, 0, 0, 0);
            topCenter = UIKit.Label(top, "", 26, UIKit.Text, TextAnchor.MiddleCenter);
            UIKit.Stretch(topCenter.rectTransform, 300, 0, 300, 0);
            var row = UIKit.HList(top, 10);
            row.anchorMin = new Vector2(1, 0); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(1, 0.5f);
            row.offsetMin = new Vector2(-560, 12); row.offsetMax = new Vector2(-14, -12);
            UIKit.Button(row, "势力", () => Do(FactionInfo()), 24);
            UIKit.Button(row, "记录", () => { try { G.Save(); UIKit.Toast("✦ 进度已记录"); } catch { UIKit.Toast("记录失败"); } }, 24);
            UIKit.Button(row, "音乐", () => { Sfx.SetMusic(!Sfx.MusicOn); UIKit.Toast(Sfx.MusicOn ? "音乐 开" : "音乐 关"); }, 24);
            endBtn = UIKit.Button(row, "结束本月", () => { if (!busy) Do(ConfirmEnd()); }, 24, true);

            cityPanel = UIKit.Panel(hud, "CityPanel");
            cityPanel.anchorMin = new Vector2(1, 0); cityPanel.anchorMax = new Vector2(1, 1); cityPanel.pivot = new Vector2(1, 0.5f);
            cityPanel.offsetMin = new Vector2(-520, 14); cityPanel.offsetMax = new Vector2(-14, -104);
            cityTitle = UIKit.Label(cityPanel, "", 40, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(cityTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -14), new Vector2(460, 56));
            cityStats = UIKit.Label(cityPanel, "", 22, UIKit.Text, TextAnchor.UpperLeft);
            cityStats.lineSpacing = 1.15f;
            UIKit.Place(cityStats.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -74), new Vector2(460, 170));
            var close = UIKit.Button(cityPanel, "×", () => SelectCity(-1), 22);
            UIKit.Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), new Vector2(48, 44));
            ScrollRect sr;
            genList = UIKit.Scroll(cityPanel, out sr);
            var srt = (RectTransform)sr.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(20, 300); srt.offsetMax = new Vector2(-20, -250);
            cmdGrid = UIKit.NewRect("Commands", cityPanel);
            cmdGrid.anchorMin = new Vector2(0, 0); cmdGrid.anchorMax = new Vector2(1, 0); cmdGrid.pivot = new Vector2(0.5f, 0);
            cmdGrid.offsetMin = new Vector2(20, 18); cmdGrid.offsetMax = new Vector2(-20, 290);
            var grid = cmdGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150, 62); grid.spacing = new Vector2(9, 9); grid.childAlignment = TextAnchor.LowerCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            cityPanel.gameObject.SetActive(false);

            // 地图上的城名
            foreach (var cv in Game.I.Map.Cities.Values)
            {
                var bg = UIKit.Img(UIKit.LabelLayer, UIKit.RR, new Color(0.05f, 0.05f, 0.08f, 0.72f), "CityLabel");
                bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                bg.rectTransform.sizeDelta = new Vector2(120, 38);
                var t = UIKit.Label(bg.transform, cv.city.name, 22, UIKit.Text, TextAnchor.MiddleCenter, true);
                UIKit.Stretch(t.rectTransform, 4, 0, 4, 0);
                var f = bg.gameObject.AddComponent<WorldFollow>(); f.target = cv.labelAnchor; f.hideBeyond = 170;
                labels[cv.city.id] = t;
            }
        }

        void RefreshAll()
        {
            var f = G.PlayerFaction;
            topLeft.text = "<color=#" + ColorUtility.ToHtmlStringRGB(f.Col) + ">■</color> " + G.Ruler(G.player).name + "军";
            int gold = G.CitiesOf(G.player).Sum(c => c.gold), food = G.CitiesOf(G.player).Sum(c => c.food);
            string tokens = new string('●', G.tokens) + new string('○', Mathf.Max(0, G.TokensFor(G.player) - G.tokens));
            topCenter.text = string.Format("<b>{0}</b>年 <b>{1}</b>月　令牌 <color=#f3c969>{2}</color>　城 {3}　金 {4}　粮 {5}",
                G.year, G.month, tokens, G.CityCount(G.player), gold, food);
            endBtn.button.interactable = !busy;
            foreach (var kv in labels)
            {
                var c = G.cities[kv.Key];
                var col = c.owner >= 0 ? G.factions[c.owner].Col : new Color(0.7f, 0.7f, 0.7f);
                kv.Value.text = "<color=#" + ColorUtility.ToHtmlStringRGB(col) + ">●</color>" + c.name + (c.owner == G.player ? " <size=16>" + G.TroopsIn(c) + "</size>" : "");
                var bg = kv.Value.transform.parent.GetComponent<Image>();
                bg.rectTransform.sizeDelta = new Vector2(kv.Value.preferredWidth + 22, 38);
            }
            RefreshCityPanel();
        }

        void SelectCity(int id)
        {
            selected = id;
            Game.I.Map.Select(id);
            if (id >= 0) Sfx.Click();
            RefreshCityPanel();
        }

        void RefreshCityPanel()
        {
            if (selected < 0) { cityPanel.gameObject.SetActive(false); return; }
            cityPanel.gameObject.SetActive(true);
            var c = G.cities[selected];
            bool mine = c.owner == G.player;
            var owner = c.owner >= 0 ? G.factions[c.owner] : null;
            cityTitle.text = c.name + "  <size=24>" + (owner != null ? "<color=#" + ColorUtility.ToHtmlStringRGB(owner.Col) + ">" + owner.name + "</color>" : "<color=#a8a194>空城</color>") + "</size>";
            var gov = c.governor >= 0 ? G.generals[c.governor].name : "—";
            cityStats.text = string.Format("太守 <b>{0}</b>　　人口 {1:N0}\n土地 <b>{2}</b>　产业 <b>{3}</b>　町 <b>{4}</b>\n金 <b>{5}</b>　粮 <b>{6}</b>　防御 {7}\n兵力 <b>{8}</b>　武将 {9} 人{10}",
                gov, c.population, c.land, c.industry, c.town, c.gold, c.food, c.Defense, G.TroopsIn(c), G.OfficersIn(c).Count(),
                G.FreeFoundIn(c.id).Any() && mine ? "　<color=#73d98c>在野 " + G.FreeFoundIn(c.id).Count() + " 人</color>" : "");
            foreach (Transform t in genList) Destroy(t.gameObject);
            foreach (var g in G.OfficersIn(c).OrderByDescending(x => G.IsRuler(x)).ThenByDescending(x => x.troops))
            {
                var row = UIKit.Img(genList, UIKit.RR, new Color(1, 1, 1, 0.05f), "Gen");
                UIKit.Size(row, -1, 54);
                var tag = G.IsRuler(g) ? "<color=#ff9a7a>君</color> " : g.id == c.governor ? "<color=#f3c969>守</color> " : "";
                var l = UIKit.Label(row.transform, tag + "<b>" + g.name + "</b>" + (g.moved ? " <size=15><color=#a8a194>已行动</color></size>" : ""), 22, UIKit.Text, TextAnchor.MiddleLeft);
                UIKit.Stretch(l.rectTransform, 14, 0, 220, 0);
                var r = UIKit.Label(row.transform, string.Format("武{0} 智{1} 政{2}\n兵{3} 训{4} 忠{5}", g.war, g.intel, g.pol, g.troops, g.training, G.IsRuler(g) ? 100 : g.loyalty), 15, UIKit.Muted, TextAnchor.MiddleRight);
                UIKit.Stretch(r.rectTransform, 160, 0, 12, 0);
            }
            foreach (Transform t in cmdGrid) Destroy(t.gameObject);
            if (!mine) return;
            bool tok = G.tokens > 0 && !busy;
            AddCmd("开发", tok, () => Do(CmdDevelop(c)));
            AddCmd("征兵", tok, () => Do(CmdRecruit(c)));
            AddCmd("训练", tok, () => Do(CmdTrain(c)));
            AddCmd("搜索", tok, () => Do(CmdSearch(c)));
            AddCmd("登用", tok && G.FreeFoundIn(c.id).Any(), () => Do(CmdHire(c)));
            AddCmd("移动", tok && c.links.Any(i => G.cities[i].owner == G.player), () => Do(CmdMove(c)));
            AddCmd("输送", tok && c.links.Any(i => G.cities[i].owner == G.player), () => Do(CmdTransport(c)));
            AddCmd("外交", tok, () => Do(CmdDiplomacy(c)));
            AddCmd("出征", tok && c.links.Any(i => G.cities[i].owner != G.player && !G.Allied(G.cities[i].owner, G.player)), () => Do(CmdAttack(c)), true);
            AddCmd("赏赐", !busy, () => Do(CmdReward(c)));
            AddCmd("交易", !busy, () => Do(CmdTrade(c)));
            AddCmd("任命", !busy, () => Do(CmdAppoint(c)));
        }

        void AddCmd(string name, bool enabled, System.Action a, bool primary = false)
        {
            var b = UIKit.Button(cmdGrid, name, a, 26, primary);
            b.button.interactable = enabled;
        }

        void Do(IEnumerator routine) { if (busy || UIKit.AnyModal) return; StartCoroutine(Wrap(routine)); }
        IEnumerator Wrap(IEnumerator r)
        {
            busy = true; RefreshAll();
            yield return r;
            busy = false;
            Game.I.Map.Refresh(G);
            RefreshAll();
        }
        void UseToken() { G.tokens = Mathf.Max(0, G.tokens - 1); }

        IEnumerator ConfirmEnd()
        {
            if (G.tokens > 0)
            {
                var ok = new Ref<bool>();
                yield return UIKit.Confirm("还剩 " + G.tokens + " 枚令牌，确定结束本月？", ok, "结束", "继续");
                if (!ok.v) yield break;
            }
            endMonth = true;
        }

        // ---------------------------------------------------------- 选择武将 --
        IEnumerator PickGeneral(string title, IEnumerable<General> list, System.Func<General, string> info, Ref<General> result, System.Func<General, bool> enabled = null)
        {
            result.v = null;
            var gens = list.ToList();
            if (gens.Count == 0) { yield return UIKit.Say("没有可用的武将。"); yield break; }
            var items = gens.Select(g => new UIKit.Item(g.name, info(g), enabled == null || enabled(g))).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose(title, items, r, null, 700);
            if (r.v >= 0) result.v = gens[r.v];
        }
        IEnumerable<General> Available(City c) { return G.OfficersIn(c).Where(g => !g.moved); }

        // ---------------------------------------------------------- 内政 --
        IEnumerator CmdDevelop(City c)
        {
            var kinds = new[] { DevKind.Land, DevKind.Industry, DevKind.Town };
            var desc = new[] { "增加秋收的粮食", "增加每月的金收入", "增加人口与城防" };
            var vals = new[] { c.land, c.industry, c.town };
            var r = new Ref<int>();
            yield return UIKit.Choose("开发（花费 " + Balance.DevelopCost + " 金）", kinds.Select((k, i) => new UIKit.Item(Commands.DevName(k), vals[i] + " / " + Balance.StatMax, c.gold >= Balance.DevelopCost, desc[i])).ToList(), r, "金 " + c.gold);
            if (r.v < 0) yield break;
            var who = new Ref<General>();
            yield return PickGeneral("由谁主持？", G.OfficersIn(c), g => "政治 " + g.pol, who);
            if (who.v == null) yield break;
            UseToken();
            Sfx.Play("coin");
            yield return UIKit.Say(Commands.Develop(c, who.v, kinds[r.v]));
        }

        IEnumerator CmdRecruit(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("为谁征兵？", G.OfficersIn(c), g => string.Format("兵 {0} / {1}", g.troops, g.MaxTroops), who, g => g.troops < g.MaxTroops);
            if (who.v == null) yield break;
            int max = Commands.RecruitMax(c, who.v);
            var n = new Ref<int>();
            yield return UIKit.PickNumber("征兵人数", Mathf.Min(100, max), max, 100, max, v => "花费 " + Mathf.CeilToInt(v / (float)Balance.TroopsPerGold) + " 金（现有 " + c.gold + "）", n);
            if (n.v <= 0) yield break;
            UseToken();
            Sfx.Play("march", 0.5f);
            yield return UIKit.Say(Commands.Recruit(c, who.v, n.v));
        }

        IEnumerator CmdTrain(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("训练哪支部队？", G.OfficersIn(c), g => string.Format("兵 {0}　训练 {1}", g.troops, g.training), who, g => g.troops > 0 && g.training < 100);
            if (who.v == null) yield break;
            UseToken();
            yield return UIKit.Say(Commands.Train(who.v));
        }

        // ---------------------------------------------------------- 人事 --
        IEnumerator CmdSearch(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("派谁搜索人才？", Available(c), g => "智力 " + g.intel, who);
            if (who.v == null) yield break;
            UseToken();
            var msg = Commands.Search(c, who.v);
            var found = Commands.SearchFound;
            yield return UIKit.Say(msg);
            if (found != null)
            {
                var ok = new Ref<bool>();
                yield return UIKit.Confirm(string.Format("登用{0}？（武{1} 智{2} 政{3}，成功率约 {4:P0}）", found.name, found.war, found.intel, found.pol, Commands.HireChance(found, who.v, G.player)), ok, "登用", "暂且作罢");
                if (ok.v) yield return HireResult(found, who.v, c);
            }
        }

        IEnumerator HireResult(General target, General recruiter, City c)
        {
            if (Commands.Hire(target, recruiter, G.player, c.id)) { Sfx.Play("win", 0.5f); yield return UIKit.Say(target.name + "：久闻明公大名，愿效犬马之劳！\n（" + target.name + "加入了我军）", target.name, UIKit.Cinnabar); }
            else yield return UIKit.Say(target.name + "：在下另有志向，请回吧。", target.name, new Color(0.4f, 0.4f, 0.45f));
        }

        IEnumerator CmdHire(City c)
        {
            var target = new Ref<General>();
            yield return PickGeneral("登用哪位在野人才？", G.FreeFoundIn(c.id), g => string.Format("武{0} 智{1} 政{2}", g.war, g.intel, g.pol), target);
            if (target.v == null) yield break;
            var who = new Ref<General>();
            yield return PickGeneral("派谁前去？", G.OfficersIn(c), g => string.Format("智力 {0}　成功率约 {1:P0}", g.intel, Commands.HireChance(target.v, g, G.player)), who);
            if (who.v == null) yield break;
            UseToken();
            yield return HireResult(target.v, who.v, c);
        }

        IEnumerator CmdReward(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("赏赐谁？（" + Balance.RewardGold + " 金）", G.OfficersIn(c).Where(g => !G.IsRuler(g)), g => "忠诚 " + g.loyalty, who, g => g.loyalty < 100 && c.gold >= Balance.RewardGold);
            if (who.v == null) yield break;
            Sfx.Play("coin");
            UIKit.Toast(Commands.Reward(c, who.v));
        }

        IEnumerator CmdAppoint(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("任命太守", G.OfficersIn(c), g => string.Format("政{0} 智{1}", g.pol, g.intel), who);
            if (who.v == null) yield break;
            if (G.OfficersIn(c).Any(G.IsRuler) && !G.IsRuler(who.v)) { yield return UIKit.Say("君主所在之城，由君主亲自坐镇。"); yield break; }
            c.governor = who.v.id;
            UIKit.Toast(who.v.name + "出任" + c.name + "太守");
        }

        IEnumerator CmdMove(City c)
        {
            var dests = c.links.Select(i => G.cities[i]).Where(x => x.owner == G.player).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose("移往何处？", dests.Select(d => new UIKit.Item(d.name, "武将 " + G.OfficersIn(d).Count() + "　兵 " + G.TroopsIn(d))).ToList(), r);
            if (r.v < 0) yield break;
            var dest = dests[r.v];
            var sel = new Ref<List<int>>();
            var gens = Available(c).ToList();
            if (gens.Count == 0) { yield return UIKit.Say("本月已无可调动的武将。"); yield break; }
            yield return UIKit.ChooseMany("调动武将至" + dest.name, gens.Select(g => new UIKit.Item(g.name, "兵 " + g.troops)).ToList(), 10, sel, "可一次调动多名武将（消耗 1 枚令牌）");
            if (sel.v == null || sel.v.Count == 0) yield break;
            UseToken();
            yield return Game.I.Map.March(c, dest, G.PlayerFaction.Col, 1.2f);
            foreach (var i in sel.v) Commands.Move(gens[i], dest);
            UIKit.Toast(sel.v.Count + " 名武将移驻" + dest.name);
        }

        IEnumerator CmdTransport(City c)
        {
            var dests = c.links.Select(i => G.cities[i]).Where(x => x.owner == G.player).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose("输送至何处？", dests.Select(d => new UIKit.Item(d.name, "金 " + d.gold + "　粮 " + d.food)).ToList(), r);
            if (r.v < 0) yield break;
            var gold = new Ref<int>(); var food = new Ref<int>();
            yield return UIKit.PickNumber("输送金", 0, c.gold, 50, c.gold / 2, null, gold);
            if (gold.v < 0) yield break;
            yield return UIKit.PickNumber("输送粮", 0, c.food, 500, c.food / 2, null, food);
            if (food.v < 0) yield break;
            UseToken();
            UIKit.Toast(Commands.Transport(c, dests[r.v], gold.v, food.v));
        }

        IEnumerator CmdTrade(City c)
        {
            int price = Commands.FoodPrice();
            var r = new Ref<int>();
            yield return UIKit.Choose("交易　（每 100 粮 " + price + " 金）", new List<UIKit.Item> { new UIKit.Item("买粮", "金 " + c.gold, c.gold > 0), new UIKit.Item("卖粮", "粮 " + c.food, c.food > 0) }, r);
            if (r.v < 0) yield break;
            var n = new Ref<int>();
            if (r.v == 0)
            {
                yield return UIKit.PickNumber("花多少金买粮？", 0, c.gold, 10, Mathf.Min(c.gold, 200), v => "可得粮 " + v * 100 / price, n);
                if (n.v > 0) { Sfx.Play("coin"); UIKit.Toast(Commands.BuyFood(c, n.v)); }
            }
            else
            {
                yield return UIKit.PickNumber("卖出多少粮？", 0, c.food, 100, Mathf.Min(c.food, 1000), v => "可得金 " + v * price / 100 * 9 / 10, n);
                if (n.v > 0) { Sfx.Play("coin"); UIKit.Toast(Commands.SellFood(c, n.v)); }
            }
        }

        // ---------------------------------------------------------- 外交 --
        IEnumerator CmdDiplomacy(City c)
        {
            var r = new Ref<int>();
            yield return UIKit.Choose("外交策略", new List<UIKit.Item>
            {
                new UIKit.Item("同盟", null, true, "与他国缔结 " + Balance.AllianceMonths + " 个月的同盟"),
                new UIKit.Item("离间", null, true, "降低敌将的忠诚"),
                new UIKit.Item("拉拢", null, true, "劝诱敌将倒戈"),
            }, r);
            if (r.v < 0) yield break;
            if (r.v == 0)
            {
                var fs = G.factions.Where(f => f.alive && f.id != G.player).ToList();
                var fi = new Ref<int>();
                yield return UIKit.Choose("与谁结盟？", fs.Select(f => new UIKit.Item(G.Ruler(f.id).name, G.Allied(f.id, G.player) ? "已同盟" : "城 " + G.CityCount(f.id) + "　成功率约 " + Commands.AllyChance(G.player, f.id, Balance.AllianceGift).ToString("P0"), !G.Allied(f.id, G.player))).ToList(), fi);
                if (fi.v < 0) yield break;
                var gift = new Ref<int>();
                yield return UIKit.PickNumber("赠送礼金", 0, c.gold, 50, Mathf.Min(c.gold, Balance.AllianceGift), v => "成功率约 " + Commands.AllyChance(G.player, fs[fi.v].id, v).ToString("P0"), gift);
                if (gift.v < 0) yield break;
                UseToken();
                yield return UIKit.Say(Commands.Ally(c, G.player, fs[fi.v].id, gift.v));
                yield break;
            }
            var agent = new Ref<General>();
            yield return PickGeneral("派谁执行？", Available(c), g => "智力 " + g.intel, agent);
            if (agent.v == null) yield break;
            var targets = c.links.Select(i => G.cities[i]).Where(x => x.owner >= 0 && x.owner != G.player).SelectMany(x => G.OfficersIn(x)).Where(g => !G.IsRuler(g)).ToList();
            if (targets.Count == 0) { yield return UIKit.Say("邻近城池中没有可以下手的敌将。"); yield break; }
            var target = new Ref<General>();
            if (r.v == 1) yield return PickGeneral("离间哪位敌将？", targets, g => G.factions[g.faction].name + "·" + G.cities[g.city].name + "　智" + g.intel + "　忠诚 " + g.loyalty, target);
            else yield return PickGeneral("拉拢哪位敌将？", targets, g => G.factions[g.faction].name + "·" + G.cities[g.city].name + "　忠诚 " + g.loyalty + "　成功率约 " + Commands.PersuadeChance(agent.v, g, G.player).ToString("P0"), target);
            if (target.v == null) yield break;
            UseToken();
            agent.v.moved = true;
            yield return UIKit.Say(r.v == 1 ? Commands.Discord(agent.v, target.v) : Commands.Persuade(agent.v, target.v, G.player));
        }

        // ---------------------------------------------------------- 战争 --
        IEnumerator CmdAttack(City c)
        {
            var targets = c.links.Select(i => G.cities[i]).Where(x => x.owner != G.player && !G.Allied(x.owner, G.player)).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose("攻打何处？", targets.Select(t => new UIKit.Item(t.name, (t.owner >= 0 ? G.factions[t.owner].name : "空城") + "　武将 " + G.OfficersIn(t).Count() + "　兵 " + G.TroopsIn(t) + "　防 " + t.Defense)).ToList(), r);
            if (r.v < 0) yield break;
            var target = targets[r.v];
            var gens = Available(c).Where(g => g.troops > 0).ToList();
            if (gens.Count == 0) { yield return UIKit.Say("没有可以出征的部队（需有兵力且本月未行动）。"); yield break; }
            var sel = new Ref<List<int>>();
            yield return UIKit.ChooseMany("出征武将（至多 " + Balance.MaxSortieGenerals + " 名）", gens.Select(g => new UIKit.Item(g.name, string.Format("兵{0} 武{1} 智{2} 训{3}", g.troops, g.war, g.intel, g.training))).ToList(), Balance.MaxSortieGenerals, sel, "第一位选中的武将为主将；主将败走则全军撤退。");
            if (sel.v == null || sel.v.Count == 0) yield break;
            var squad = sel.v.Select(i => gens[i]).ToList();
            int troops = squad.Sum(g => g.troops);
            int suggest = Mathf.Min(c.food, Mathf.CeilToInt(troops * 0.14f / 100f) * 100 + 200);
            var food = new Ref<int>();
            yield return UIKit.PickNumber("携带军粮", 0, c.food, 100, suggest, v => string.Format("约可支撑 {0} 日（兵 {1}）", v / Mathf.Max(1, troops / Balance.BattleFoodPerTroops), troops), food);
            if (food.v < 0) yield break;
            UseToken();
            var setup = Conquest.Prepare(G.player, c, target, squad, food.v, 0);
            Sfx.Play("horn");
            yield return Game.I.Map.March(c, target, G.PlayerFaction.Col, 1.4f);
            yield return Fight(setup, 0);
        }

        IEnumerator Defend(BattleSetup s)
        {
            Game.I.Rig.Focus(new Vector3(s.target.MapPos.x, 0, s.target.MapPos.y), 45);
            SelectCity(s.target.id);
            yield return Game.I.Map.March(s.src, s.target, G.factions[s.attacker].Col, 1.4f);
            yield return UIKit.Say(string.Format("急报！{0}率 {1} 名武将、兵 {2} 来犯{3}！", G.Ruler(s.attacker).name == s.atk[0].name ? G.Ruler(s.attacker).name : G.factions[s.attacker].name + "军" + s.atk[0].name, s.atk.Count, s.atk.Sum(g => g.troops), s.target.name));
            yield return Fight(s, 1);
        }

        // 战斗与战后处理。playerSide：玩家是攻方 0 还是守方 1
        IEnumerator Fight(BattleSetup s, int playerSide)
        {
            if (s.def.Count == 0 || s.def.All(d => d.troops <= 0))
            {
                Conquest.AutoResolve(s);
                Conquest.Apply(s);
                if (s.captives.Count > 0) yield return HandleCaptives(s, playerSide == 0);
                yield return UIKit.Say(s.summary);
                yield break;
            }
            var mode = new Ref<int>();
            yield return UIKit.Choose(s.target.name + "之战", new List<UIKit.Item> { new UIKit.Item("亲自指挥", "在战场上调兵遣将"), new UIKit.Item("委任", "由部将自行作战，立即得出结果") }, mode, null, 560);
            if (mode.v == 0)
            {
                hud.gameObject.SetActive(false);
                foreach (var l in labels.Values) l.transform.parent.gameObject.SetActive(false);
                yield return BattleController.Run(s, playerSide);
                hud.gameObject.SetActive(true);
                foreach (var l in labels.Values) l.transform.parent.gameObject.SetActive(true);
                Sfx.Music("map");
            }
            else Conquest.AutoResolve(s);
            Conquest.Apply(s);
            bool playerWon = s.attackerWon == (playerSide == 0);
            if (s.captives.Count > 0)
            {
                if (playerWon) yield return HandleCaptives(s, true);
                else Conquest.AiDecideCaptives(s, playerSide == 0 ? s.defender : s.attacker);
            }
            foreach (var f in G.factions) if (f.alive && G.CityCount(f.id) == 0) G.CheckFactionDeath(f.id);
            Game.I.Map.Refresh(G);
            string res = playerSide == 0
                ? (s.attackerWon ? "我军攻陷了" + s.target.name + "！" : "攻城失利，全军撤回" + s.src.name + "。")
                : (s.attackerWon ? s.target.name + "失守了……" : "我军成功守住了" + s.target.name + "！");
            yield return UIKit.Say(res);
            if (s.attackerWon && s.defender >= 0 && !G.factions[s.defender].alive) yield return UIKit.Say(G.factions[s.defender].name + "势力就此灭亡。");
        }

        IEnumerator HandleCaptives(BattleSetup s, bool playerDecides)
        {
            if (!playerDecides) { Conquest.AiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); yield break; }
            // 玩家只在获胜后处置俘虏，胜方必然据有 s.target（攻方刚攻下 / 守方守住），降将应编入该城
            var holderCity = s.target;
            var recruiter = G.Ruler(G.player);
            foreach (var cap in s.captives.ToList())
            {
                bool isRuler = cap.faction >= 0 && G.factions[cap.faction].ruler == cap.id;
                var r = new Ref<int>();
                yield return UIKit.Choose("俘虏：" + cap.name + (isRuler ? "（君主）" : ""), new List<UIKit.Item>
                {
                    new UIKit.Item("登用", isRuler ? "君主不会投降" : "成功率约 " + Commands.HireChance(cap, recruiter, G.player).ToString("P0"), !isRuler),
                    new UIKit.Item("释放", "放其归去"),
                    new UIKit.Item("处斩", "以绝后患"),
                }, r, string.Format("武{0} 智{1} 政{2}", cap.war, cap.intel, cap.pol));
                if (r.v == 0)
                {
                    if (Commands.Hire(cap, recruiter, G.player, holderCity.id)) yield return UIKit.Say(cap.name + "：败军之将，承蒙不弃，愿降！", cap.name, UIKit.Cinnabar);
                    else { yield return UIKit.Say(cap.name + "：忠臣不事二主！", cap.name, new Color(0.4f, 0.4f, 0.45f)); Conquest.Release(cap); }
                }
                else if (r.v == 2) { Conquest.Execute(cap); yield return UIKit.Say(cap.name + "被处斩了。"); }
                else Conquest.Release(cap);
            }
        }

        // ---------------------------------------------------------- 情报 --
        IEnumerator FactionInfo()
        {
            var fs = G.factions.Where(f => f.alive).OrderByDescending(f => G.CityCount(f.id)).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose("天下势力", fs.Select(f => new UIKit.Item(
                "<color=#" + ColorUtility.ToHtmlStringRGB(f.Col) + ">■</color> " + G.Ruler(f.id).name + (f.id == G.player ? "（我方）" : G.Allied(f.id, G.player) ? "（同盟）" : ""),
                string.Format("城 {0}　将 {1}　兵 {2}", G.CityCount(f.id), G.GeneralsOf(f.id).Count(), G.CitiesOf(f.id).Sum(c => G.TroopsIn(c))))).ToList(), r, null, 760);
            if (r.v < 0) yield break;
            var cap = G.cities[G.Ruler(fs[r.v].id).city];
            Game.I.Rig.Focus(new Vector3(cap.MapPos.x, 0, cap.MapPos.y), 45);
            SelectCity(cap.id);
        }
    }
}
