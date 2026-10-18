using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sanguo
{
    // 战略画面：顶栏、城池情报、指令与月份循环（← 网页版 js/strategy-screen.js）
    // 第二版 §4F 调动武将：
    //   「移动」「输送」的目的地 = 经由己方城池相连可达的所有己方城（Commands.MoveTargets），
    //   列表注明路程（相邻 / 经 N 城）与途经城名，行军动画沿路线逐城前进；
    //   灰色指令按钮被点按时以提示说明原因（AddCmd(name, why, …)、ExplainCmd）；
    //   第一次占领新城后弹一次「移动」提示并让「移动」按钮闪烁（MoveTip / HintCmd，本机只弹一次）；
    //   只在「移动」此刻真能用时弹出（有令牌、某座己方城有未行动的武将可调），否则留到下个月月初。
    // 第二版 §4G 世界剧本：
    //   地图音乐按玩家势力的文化（Sfx.SetCulture）；点按城池面板的武将行 → 武将详情（大头像、能力、
    //   必杀技名称与说明；世界武将另有全名、原名、身份、生卒与史实介绍）；
    //   「势力」列表与外交的同盟对象在世界剧本中按地域分组（GroupedFactions）。
    //   世界地图的城名在远景（拉远到 340）也显示，距离超过约 175 时改为紧凑样式，由避让逻辑按优先级取舍；
    //   月末消息优先显示与玩家相关的（我方、本地域与邻国），见 PickNews；
    //   同月排队的电脑进攻轮到时重新核对（StrategyAI.RecheckBattle）；战败后告知我方被俘武将的下落（ReportCaptives）。
    // 第三版 §5 头像无处不在（非战斗阶段）：
    //   所有列出武将的列表（指令选人、出征、移动、登用、俘虏、外交、势力一览……）每行带头像（UIKit.Item 的 avatar）；
    //   指令结果由执行的武将以头像汇报（Report）；月末消息由军师汇报，文中提到的武将 / 势力（→ 君主）带小头像（UIText.Faces）；
    //   急报以来犯主将的头像示警（Alarm）；俘虏、登用确认显示头像名片（UIKit.GenCardSpec）；「大事」查看记录（带小头像）。
    // 第三版 §4 成长界面（规则见 GameState.cs 的 Progress）：
    //   玩家武将不再自动习得（Progress.playerAutoLearn = false，Game.Awake 与本画面都会设）。每场战斗（亲自指挥 / 委任 / 空城）之后：
    //   Growth(s) → 升级通知（头像、「关羽 升至 5 级」、经验条、新解锁的格）→ 对每位有空格的我方武将弹出习得对话框
    //   （LearnDialog：3 个候选必杀 / 计策，显示等阶、说明、数值摘要与武将头像；「稍后再选」保留空格；× 不再询问其余武将）。
    //   武将详情：等级、经验条、战绩、老练加成、全部必杀（专属在前）与计策（含说明）及格数；有空格时「习得新招」按钮。
    //   城池面板的武将行显示等级（可习得新招时另有「新」标记）；月初若有保留的空格则提示一次。
    //   「任命」在君主坐镇的城灰显；攻下最后一城时被俘的君主仍按原君主显示（Conquest.CaptiveInfoOf）。
    public class StrategyScreen : MonoBehaviour
    {
        static GameState G { get { return GameState.Current; } }
        public static float RowPortrait { get { return UIKit.FaceItem; } }   // 城池面板武将行的头像尺寸（= 列表行 2.5rem = 50）
        const float LabelHideBeyond = 170;             // 经典地图：镜头距离超过即隐藏城名
        const float LabelHideBeyondWorld = 420;        // 世界地图可拉远到 340：远景也显示（避让后剩下的）城名
        const float LabelFarCompact = 175;             // 世界地图镜头距离超过约 175 时城名改为紧凑样式
        const float LabelFade = 0.18f;                 // 城名标签避让时的淡入淡出时长（秒）
        static readonly Color GreySpeaker = new Color(0.4f, 0.4f, 0.45f);

        RectTransform hud, topBar, cityPanel, cmdGrid, genList, genScroll;
        Text topLeft, topCenter, cityTitle, cityStats;
        // 城池情报：常规屏幕分四行（框高至少 170）；矮屏（视口高 ≤ 430 点，横屏手机）各项流排、字略小，框高随内容，
        // 武将列表紧接其下多露出一行（style.css @media (max-height: 430px)：.sg-cs-br 隐去、.94rem、行高 1.38、间隔 .3rem）
        const float CityStatsFlowPt = 430;
        const float CityStatsTop = 74, CityStatsMinH = 170, CityStatsW = 460;
        float cityStatsH = CityStatsMinH;
        bool cityStatsFlow;
        static bool CityStatsFlowWanted { get { return UIKit.ViewHeightPt <= CityStatsFlowPt; } }
        UIKit.Btn endBtn, saveBtn;
        readonly Dictionary<string, UIKit.Btn> cmdButtons = new Dictionary<string, UIKit.Btn>();
        readonly Dictionary<string, string> cmdWhy = new Dictionary<string, string>();
        int selected = -1;
        bool busy, endMonth;
        string hintName; float hintUntil;              // 正在闪烁的指令按钮（面板重建时继续闪烁，直到时间到）
        bool tipPending;                               // 已取得新城、「移动」提示待弹（等到「移动」可用时）
        List<int> tipCities = new List<int>();         // 新取得的城（提示时优先选中）
        RectTransform denyToast;
        bool genInfoOpen;

        // 地图上的城名（位置、避让与淡入淡出由 LateUpdate 处理）
        sealed class CityLabel
        {
            public RectTransform rt; public Text text; public CanvasGroup cg; public MapView.CityVisual cv;
            public Image bg, border, ring;     // 底色、金边、选中时的外圈（网页版 .is-mine / .is-selected）
            public int style = -1;
            public float fade = -1; public bool on, seen;
        }
        readonly Dictionary<int, CityLabel> labels = new Dictionary<int, CityLabel>();
        bool labelsOn = true, compact;
        float hideBeyond = LabelHideBeyond;

        // ---------------------------------------------------------- 本机记录：「移动」提示只弹一次 --
        const string TipKey = "sanguozhi2_tip_move";
        static bool tipShownMem;
        static bool TipShown
        {
            get
            {
                if (tipShownMem) return true;
                try { return PlayerPrefs.GetInt(TipKey, 0) != 0; } catch { return false; }
            }
        }
        static void MarkTipShown()
        {
            tipShownMem = true;
            try { PlayerPrefs.SetInt(TipKey, 1); PlayerPrefs.Save(); } catch { }
        }

        // ---------------------------------------------------------- 小工具 --
        static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }
        // 与区域设置无关的百分比 / 千分位（网页版 pct = Math.round(p*100)+'%'，n0 = toLocaleString('en-US')）
        static string Pct(double p) { return ((long)System.Math.Floor(p * 100 + 0.5)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"; }
        static string N0(long v) { return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture); }
        // 深色势力色作为文字时提亮，保证在漆黑面板上可读（标记 ■● 仍用原色）
        static string Readable(Color c)
        {
            float L = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (L >= 0.5f) return Hex(c);
            float k = Mathf.Clamp((0.5f - L) * 1.3f, 0, 0.6f);
            return Hex(new Color(c.r + (1 - c.r) * k, c.g + (1 - c.g) * k, c.b + (1 - c.b) * k));
        }
        // 小字用的势力色：比 Readable 提得更亮（说话人身份、名片副标题）
        static string Lifted(Color c)
        {
            float L = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (L >= 0.62f) return Hex(c);
            float k = Mathf.Clamp((0.62f - L) * 1.5f, 0, 0.75f);
            return Hex(new Color(c.r + (1 - c.r) * k, c.g + (1 - c.g) * k, c.b + (1 - c.b) * k));
        }
        static string Swatch(Faction f) { return "<color=" + Hex(f.Col) + ">■</color>"; }
        static string Face(General g) { return UIText.Face(g); }
        static string Faces(string s, FacesOpts o = null) { try { return UIText.Faces(s, o); } catch (System.Exception e) { Debug.LogWarning(e); return s; } }
        static string RulerTag(General g) { return G.IsRuler(g) ? UIText.Tag(TagStyle.Ruler, "君") : ""; }
        // 武将的身份小字：势力名（提亮的势力色）·君主 / 太守；在野
        static string GenSub(General gen)
        {
            var g = G;
            if (gen == null) return "";
            var f = gen.faction >= 0 ? g.factions[gen.faction] : null;
            string fn = f != null ? "<color=" + Lifted(f.Col) + ">" + f.name + "</color>" : "<color=#a8a194>在野</color>";
            string role = f != null && g.IsRuler(gen) ? "君主" : gen.city >= 0 && gen.city < g.cities.Count && g.cities[gen.city].governor == gen.id && f != null ? "太守" : "";
            return fn + (role.Length > 0 ? "·" + role : "");
        }
        // 矮屏（横屏手机，视口高 < 560 点）
        static bool ShortScreen() { float h = UIKit.ViewHeightPt; return h > 0 && h < 560; }
        static string Small(string s, int size = 18) { return UIKit.Sz(size) + "<color=#a8a194>" + s + "</color></size>"; }
        // 路程文字：相邻 / 经 N 城（N = 途经的己方城数）
        static string RouteText(Route rt) { return rt.hops <= 1 ? "相邻" : "经 " + (rt.hops - 1) + " 城"; }
        // 途经城名（最多列 4 座）
        static string ViaText(Route rt)
        {
            if (rt.hops <= 1) return null;
            var mid = rt.path.Skip(1).Take(rt.path.Count - 2).Select(i => G.cities[i].name).ToList();
            return "途经 " + (mid.Count > 4 ? string.Join("、", mid.Take(4).ToArray()) + " 等 " + mid.Count + " 城" : string.Join("、", mid.ToArray()));
        }
        // 地图音乐的地域风格 = 玩家势力的文化（世界剧本；经典剧本为 han）
        static void MapMusic()
        {
            try { if (G != null && G.player >= 0) Sfx.SetCulture(string.IsNullOrEmpty(G.PlayerFaction.culture) ? "han" : G.PlayerFaction.culture); } catch (System.Exception e) { Debug.LogWarning(e); }
            Sfx.Music("map");
        }
        // 世界剧本的地域表（经典剧本为 null）
        static ScenarioRegion[] RegionsOf()
        {
            if (G == null || G.scenario != "world") return null;
            var R = G.Scen.Regions;
            return R != null && R.Length > 0 ? R : null;
        }

        // ---------------------------------------------------------- 主循环 --
        public IEnumerator Run()
        {
            BuildHud();
            Game.I.Rig.OnTap += OnMapTap;
            MapMusic();
            Progress.playerAutoLearn = false;     // 升级对话框由本画面负责（电脑武将仍自动习得）
            var cap = G.cities[G.Ruler(G.player).city];
            // 从标题 / 读档进入时直接切到都城（世界地图上从中原飞到罗马要好几秒，城池面板却已打开）
            Game.I.Rig.Focus(new Vector3(cap.MapPos.x, 0, cap.MapPos.y), 48, true);
            SelectCity(cap.id);
            UIKit.Banner(G.year + "年 " + G.month + "月", G.PlayerFaction.name + "军");
            GrowthReminder();
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
                foreach (var b in battles)
                {
                    // 同月先到的进攻可能已使该城易主：不再针对玩家的就按电脑之间结算（或撤兵），不再向玩家告急
                    if (!StrategyAI.RecheckBattle(b, news)) { Game.I.Map.Refresh(G); RefreshAll(); continue; }
                    yield return Defend(b);
                    if (!G.PlayerFaction.alive) break;
                }
                if (G.PlayerFaction.alive) news.AddRange(StrategyAI.EndMonth());
                Game.I.Map.Refresh(G); RefreshAll();
                if (!G.PlayerFaction.alive) { yield return GameOver(false); yield break; }
                if (G.cities.All(c => c.owner == G.player)) { yield return GameOver(true); yield break; }
                if (news.Count > 0) yield return SayNews(news);
                try { G.Save(); } catch (System.Exception e) { Debug.LogWarning(e); }
                UIKit.Banner(G.year + "年 " + G.month + "月", "令牌 " + G.tokens + " 枚", 1.8f);
                // 上月取得新城时「移动」还不能用（武将都已行动 / 令牌用完）：月初横幅之后补弹提示
                if (tipPending && MoveUsableCity() != null) yield return new WaitForSeconds(1.2f);
                busy = false;
                GrowthReminder();
                if (tipPending) yield return MoveTip(false);
            }
        }

        // 月末消息：世界剧本中先列与玩家相关的（提到我方、本地域势力或与我方接壤的势力），至多 6 条
        string PickNews(List<string> news)
        {
            var g = G;
            if (g.scenario != "world" || news.Count <= 6) return string.Join("\n", news.Take(6).ToArray());
            var me = g.PlayerFaction;
            var names = new HashSet<string> { me.name };
            foreach (var f in g.factions) if (f.alive && f.region == me.region) names.Add(f.name);
            foreach (var c in g.CitiesOf(g.player)) foreach (var j in c.links) { int o = g.cities[j].owner; if (o >= 0) names.Add(g.factions[o].name); }
            System.Func<string, bool> near = n => { foreach (var k in names) if (n.IndexOf(k, System.StringComparison.Ordinal) >= 0) return true; return false; };
            // 先挑与我方相关的，再按原先的先后顺序排列（同一座城的两次易手不致前后颠倒）
            var idx = Enumerable.Range(0, news.Count).ToList();
            var pick = idx.Where(i => near(news[i])).Concat(idx.Where(i => !near(news[i]))).Take(6).OrderBy(i => i).ToList();
            var list = pick.Select(i => news[i]).ToArray();
            return string.Join("\n", list) + "\n<color=#a8a194>（各地另有 " + (news.Count - list.Length) + " 条消息）</color>";
        }

        // 月末消息由我方军师（智力最高的部将；没有部将时为君主）汇报，文中武将 / 势力带小头像
        General Advisor()
        {
            var g = G;
            var gs = g.GeneralsOf(g.player).Where(x => !x.dead).ToList();
            var ru = g.Ruler(g.player);
            var pool = gs.Where(x => x != ru).ToList();
            if (pool.Count == 0) return ru;
            var best = pool[0];
            foreach (var b in pool) if (b.intel > best.intel) best = b;   // 同 reduce：并列时取先出现的
            return best;
        }
        IEnumerator SayNews(List<string> news)
        {
            var adv = Advisor();
            var g = G;
            string role = adv != null && adv == g.Ruler(g.player) ? "君主" : "军师";
            string text = Faces(PickNews(news), new FacesOpts { skip = adv != null ? new[] { adv.name } : new string[0], max = 10 });
            yield return UIKit.Say(text, adv, new UIKit.SayOpts { sub = UIText.Tag(TagStyle.Cmd, role) + "本月消息" });
        }

        // 大事记：GameState.log（新的在前），提到的武将 / 势力带小头像（← chronicle，.sg-chronicle 宽 41rem）
        static readonly System.Text.RegularExpressions.Regex LogLine = new System.Text.RegularExpressions.Regex("^(\\d+)年(\\d+)月 ([\\s\\S]*)$");
        IEnumerator Chronicle()
        {
            var g = G;
            var log = (g.log ?? new List<string>()).ToList();
            log.Reverse();
            const float W = 820, pad = 30;
            var m = UIKit.OpenModal(W, Mathf.Min(680, UIKit.MaxModalHeight));
            float w = m.panel.sizeDelta.x;
            bool done = false;
            var bb = m.blocker.gameObject.AddComponent<Button>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(() => done = true);
            m.panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // 点在对话框内不关闭
            var head = UIKit.Label(m.panel, "天下大事  " + UIKit.Sz(20) + "<color=#a8a194>" + log.Count + " 条</color></size>", 32, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(pad, -18), new Vector2(w - 120, 48));
            var close = UIKit.Button(m.panel, "×", () => done = true, 24);
            UIKit.Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            ScrollRect sr;
            var list = UIKit.Scroll(m.panel, out sr);
            list.GetComponent<VerticalLayoutGroup>().spacing = 6;
            UIKit.Stretch((RectTransform)sr.transform, 22, 22, 22, 80);
            float rowW = w - 44, listH = 0;
            if (log.Count == 0)
            {
                var e = UIKit.Label(list, "尚无大事发生。", 22, UIKit.Muted, TextAnchor.MiddleCenter);
                UIKit.Size(e, -1, 60);
                listH = 66;
            }
            foreach (var line in log)
            {
                // 每行：日期（金色，「年」「月」小字灰）+ 正文（提到的武将带小头像，至多 4 个）
                var mt = LogLine.Match(line ?? "");
                var row = UIKit.Img(list, UIKit.RR, new Color(0.24f, 0.23f, 0.3f, 0.42f), "Row");
                row.raycastTarget = false;
                var ol = UIKit.Img(row.transform, UIKit.RROutline, new Color(0.953f, 0.788f, 0.412f, 0.1f), "Outline"); UIKit.Stretch(ol.rectTransform); ol.raycastTarget = false;
                var date = UIKit.Label(row.transform, mt.Success ? mt.Groups[1].Value + UIKit.Sz(16) + "<color=#a8a194>年</color></size>" + mt.Groups[2].Value + UIKit.Sz(16) + "<color=#a8a194>月</color></size>" : "", 20, UIKit.Gold, TextAnchor.UpperLeft, false, "Date");
                UIKit.Place(date.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -10), new Vector2(110, 30));
                var tx = UIKit.Label(row.transform, "", 23, UIKit.Text, TextAnchor.UpperLeft, false, "Text");
                float tw = rowW - 18 - 110 - 18 - 18;
                UIKit.Place(tx.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18 + 110 + 18, -8), new Vector2(tw, 30));
                tx.lineSpacing = 1.15f;
                UIKit.SetText(tx, Faces(mt.Success ? mt.Groups[3].Value : (line ?? ""), new FacesOpts { max = 4 }));
                float th = Mathf.Ceil(tx.preferredHeight) + 2;
                tx.rectTransform.sizeDelta = new Vector2(tw, th);
                float rh = Mathf.Max(46, th + 16);
                UIKit.Size(row, -1, rh);
                listH += rh + 6;
            }
            // 高度随内容，至多 34rem（680）
            m.panel.sizeDelta = new Vector2(w, Mathf.Min(Mathf.Min(680, UIKit.MaxModalHeight), 80 + listH + 22 + 6));
            yield return null;
            while (!done)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) done = true;
                yield return null;
            }
            Sfx.Click();
            m.Close();
        }

        IEnumerator GameOver(bool won)
        {
            busy = true;
            RefreshAll();
            if (won)
            {
                Sfx.Play("win");
                UIKit.Banner("天下统一", G.Ruler(G.player).name + "终成霸业", 4f);
                yield return new WaitForSeconds(2.5f);
                var ru = G.Ruler(G.player);
                yield return UIKit.Say(string.Format("{0}年{1}月，{2}平定四海，一统天下。\n麾下武将 {3} 员，历时 {4} 年。", G.year, G.month, ru.name, G.GeneralsOf(G.player).Count(), G.year - G.Scen.StartYear),
                    ru, new UIKit.SayOpts { mood = PortraitMood.Win, sub = GenSub(ru) });
            }
            else
            {
                Sfx.Play("lose");
                UIKit.Banner("势力灭亡", "霸业未成，身先陨落……", 4f);
                yield return new WaitForSeconds(3f);
            }
            GameState.DeleteSave();
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex < 0 ? 0 : UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void OnMapTap(Vector2 p)
        {
            if (UIKit.AnyModal) return;
            if (hud == null || !hud.gameObject.activeSelf) return;   // 战斗中（地图隐藏）
            int id = Game.I.Map.Pick(Game.I.Rig.Cam, p);
            SelectCity(id);
        }

        void OnDestroy()
        {
            UIKit.ScaleChanged -= OnScaleChanged;
            if (Game.I != null && Game.I.Rig != null) Game.I.Rig.OnTap -= OnMapTap;
        }

        // ---------------------------------------------------------- 界面 --
        void BuildHud()
        {
            hud = UIKit.NewRect("StrategyHUD", UIKit.Screens); UIKit.Stretch(hud);
            var top = topBar = UIKit.Panel(hud, "TopBar");
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(14, -92); top.offsetMax = new Vector2(-14, -12);
            topLeft = UIKit.Label(top, "", 30, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Stretch(topLeft.rectTransform, 28, 0, 0, 0);
            topCenter = UIKit.Label(top, "", 26, UIKit.Text, TextAnchor.MiddleCenter);
            UIKit.Stretch(topCenter.rectTransform, 300, 0, 670, 0);
            var row = UIKit.HList(top, 10);
            row.anchorMin = new Vector2(1, 0); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(1, 0.5f);
            row.offsetMin = new Vector2(-660, 12); row.offsetMax = new Vector2(-14, -12);
            UIKit.Button(row, "势力", () => Do(FactionInfo()), 24);
            UIKit.Button(row, "大事", () => Do(Chronicle()), 24);      // 天下大事（记录）
            // 指令或月末处理进行中（令牌已用、军粮已扣、尚未交战等）不可记录，避免存下半途的局面
            saveBtn = UIKit.Button(row, "记录", () =>
            {
                if (busy || endMonth) return;
                bool ok;
                try { ok = G.Save(); } catch { ok = false; }
                UIKit.Toast(ok ? "✦ 进度已记录" : "记录失败");
            }, 24);
            UIKit.Button(row, "音乐", () => { Sfx.SetMusic(!Sfx.MusicOn); UIKit.Toast(Sfx.MusicOn ? "音乐 开" : "音乐 关"); }, 24);
            endBtn = UIKit.Button(row, "结束本月", () => { if (!busy) Do(ConfirmEnd()); }, 24, true);

            cityPanel = UIKit.Panel(hud, "CityPanel");
            cityPanel.anchorMin = new Vector2(1, 0); cityPanel.anchorMax = new Vector2(1, 1); cityPanel.pivot = new Vector2(1, 0.5f);
            cityPanel.offsetMin = new Vector2(-520, 14); cityPanel.offsetMax = new Vector2(-14, -104);
            cityTitle = UIKit.Label(cityPanel, "", 40, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(cityTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -14), new Vector2(460, 56));
            cityStats = UIKit.Label(cityPanel, "", 22, UIKit.Text, TextAnchor.UpperLeft);
            cityStats.lineSpacing = 1.15f;
            UIKit.Place(cityStats.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -CityStatsTop), new Vector2(CityStatsW, CityStatsMinH));
            var close = UIKit.Button(cityPanel, "×", () => SelectCity(-1), 22);
            UIKit.Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), new Vector2(48, 44));
            ScrollRect sr;
            genList = UIKit.Scroll(cityPanel, out sr);
            genScroll = (RectTransform)sr.transform;
            cmdGrid = UIKit.NewRect("Commands", cityPanel);
            var grid = cmdGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(9, 9); grid.childAlignment = TextAnchor.LowerCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            LayoutCityPanel();
            UIKit.ScaleChanged += OnScaleChanged;
            cityPanel.gameObject.SetActive(false);

            // 地图上的城名
            hideBeyond = G.scenario == "world" ? LabelHideBeyondWorld : LabelHideBeyond;
            foreach (var cv in Game.I.Map.Cities.Values)
            {
                var bg = UIKit.Img(UIKit.LabelLayer, UIKit.RR, new Color(0.05f, 0.05f, 0.08f, 0.72f), "CityLabel");
                bg.raycastTarget = false;
                bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                bg.rectTransform.sizeDelta = new Vector2(120, 38);
                var t = UIKit.Label(bg.transform, cv.city.name, 22, UIKit.Text, TextAnchor.MiddleCenter, true);
                UIKit.Stretch(t.rectTransform, 4, 0, 4, 0);
                var border = UIKit.Img(bg.transform, UIKit.RROutline, new Color(0.95f, 0.79f, 0.41f, 0.26f), "Border");
                UIKit.Stretch(border.rectTransform, 1, 1, 1, 1); border.raycastTarget = false;
                var ring = UIKit.Img(bg.transform, UIKit.RROutline, new Color(0.95f, 0.79f, 0.41f, 0.45f), "Ring");
                UIKit.Stretch(ring.rectTransform, -3, -3, -3, -3); ring.raycastTarget = false; ring.enabled = false;
                var cg = bg.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
                cg.alpha = 0;                // 首次避让计算后才显示，避免第一帧城名叠在一起
                labels[cv.city.id] = new CityLabel { rt = bg.rectTransform, text = t, cg = cg, cv = cv, bg = bg, border = border, ring = ring };
            }
        }

        // 城池面板的纵向排布：指令格（3 列，格高不低于触控目标）在下，武将列表在中
        void LayoutCityPanel()
        {
            if (cmdGrid == null) return;
            float cellH = Mathf.Max(62, UIKit.MinTouch);
            var grid = cmdGrid.GetComponent<GridLayoutGroup>();
            // 触控目标高于原格高（手机）：改为 4 列 3 排，给武将列表留出空间（网页版矮屏为 6 列 2 排，但那样格宽不足 44 点）
            int cols = cellH > 62 ? 4 : 3, rows = 12 / cols;
            float cellW = cols == 3 ? 150 : (466 - (cols - 1) * 9) / (float)cols;
            grid.constraintCount = cols;
            grid.cellSize = new Vector2(cellW, cellH);
            float gridH = rows * cellH + (rows - 1) * 9;
            cmdGrid.anchorMin = new Vector2(0, 0); cmdGrid.anchorMax = new Vector2(1, 0); cmdGrid.pivot = new Vector2(0.5f, 0);
            cmdGrid.offsetMin = new Vector2(20, 18); cmdGrid.offsetMax = new Vector2(-20, 18 + gridH + 2);
            genScroll.anchorMin = new Vector2(0, 0); genScroll.anchorMax = new Vector2(1, 1);
            // 武将列表紧接城池情报之下（间隔 6）；情报的高度由 RefreshCityPanel 按内容定出
            genScroll.offsetMin = new Vector2(20, 18 + gridH + 12); genScroll.offsetMax = new Vector2(-20, -(CityStatsTop + cityStatsH + 6));
        }
        // 界面缩放变化：重排面板；视口高度跨过 430 点时城池情报改用另一种排法（内容随之重建）
        void OnScaleChanged()
        {
            LayoutCityPanel();
            if (cityStatsFlow != CityStatsFlowWanted && cityPanel != null && cityPanel.gameObject.activeSelf && G != null) RefreshCityPanel();
        }

        // 城池情报（← refreshCityPanel 的 .sg-cs 各项）：常规屏幕按 .sg-cs-br 分四行；矮屏各项流排——项内不折行，
        // 一行放不下时整项移到下一行（UIText.FlowItems 按 cityStats 的字体实际量宽）。随后按内容定出框高、重排武将列表
        void SetCityStats(List<string> items)
        {
            bool flow = CityStatsFlowWanted;
            cityStatsFlow = flow;
            var fb = cityStats.GetComponent<FontBase>();
            if (fb != null) fb.Set(flow ? 19 : 22);                  // .94rem / 1.1rem
            else cityStats.fontSize = UIKit.Fs(flow ? 19 : 22);
            cityStats.lineSpacing = flow ? 1.02f : 1.15f;           // 行高 1.38 / 1.55（与常规的 1.15 同比）
            string text;
            if (!flow)
                text = items[0] + "　　" + items[1] + "\n" + items[2] + "　" + items[3] + "　" + items[4] + "\n" + items[5] + "　" + items[6] + "　" + items[7] + "\n" +
                       string.Join("　", items.Skip(8).ToArray());
            else
            {
                var set = cityStats.GetGenerationSettings(Vector2.zero);
                var tg = cityStats.cachedTextGeneratorForLayout;
                float ppu = cityStats.pixelsPerUnit > 0 ? cityStats.pixelsPerUnit : 1;
                int fs = cityStats.fontSize;
                // 项间：一个空格加 .35em（两个半角空格约 .55em）；留 2 个单位的余量，免得量出的宽度恰好压线时又被自动折行
                text = UIText.FlowItems(items, "  ", CityStatsW - 2, s => tg.GetPreferredWidth(UIKit.ToSlots(s, fs, UIKit.FaceEmSmall), set) / ppu);
            }
            UIKit.SetText(cityStats, text, UIKit.FaceEmSmall);     // 太守的行内头像 1.45em（.sg-citystats .sg-face）
            var rt = cityStats.rectTransform;
            rt.sizeDelta = new Vector2(CityStatsW, 10);
            float need = Mathf.Ceil(cityStats.preferredHeight) + 2;
            float h = flow ? need : Mathf.Max(CityStatsMinH, need);
            rt.sizeDelta = new Vector2(CityStatsW, h);
            if (Mathf.Abs(h - cityStatsH) > 0.5f) { cityStatsH = h; LayoutCityPanel(); }
        }

        void SetMapLabelsVisible(bool on)
        {
            labelsOn = on;
            foreach (var l in labels.Values)
            {
                if (!on) { l.cg.alpha = 0; l.seen = false; l.on = false; }   // 重新显示时由避让逻辑直接定出透明度
                l.rt.gameObject.SetActive(on);
            }
        }

        // ---------------------------------------------------------- 城名避让 --
        // 每帧按优先级摆放城名：选中的城 → 我方城池 → 其余（人口多者优先），
        // 与已摆放的标签重叠、或被顶栏 / 城池面板遮住的标签淡出，避免城名互相压住或被界面切掉一半。
        struct Cand { public CityLabel l; public int id, rank, pop; public Vector2 sp; }
        readonly List<Cand> cand = new List<Cand>();
        readonly List<Rect> placed = new List<Rect>(), occ = new List<Rect>();
        readonly Vector3[] corners = new Vector3[4];

        void LateUpdate()
        {
            if (hud == null || labels.Count == 0 || G == null) return;
            // 窗口改变大小时视口高度可能跨过 430 点而字号下限不变（不触发 ScaleChanged）：城池情报换排法
            if (selected >= 0 && cityPanel.gameObject.activeSelf && cityStatsFlow != CityStatsFlowWanted) RefreshCityPanel();
            if (!labelsOn || !hud.gameObject.activeSelf) return;
            var rig = Game.I != null ? Game.I.Rig : null;
            var cam = rig != null ? rig.Cam : null;
            if (cam == null) return;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0, 0.1f);
            // 紧凑模式：视口高度不足 540 点且镜头距离超过约 90，或距离超过约 175（带滞后，缩放时不来回切换）
            float dist = rig.Distance;
            bool c = (UIKit.ViewHeightPt < 540 && dist > (compact ? 86 : 92)) || dist > LabelFarCompact - (compact ? 8 : 0);
            if (c != compact) { compact = c; foreach (var kv in labels) RenderLabel(kv.Key, kv.Value); }
            // 遮挡标签的界面矩形（屏幕像素）：顶栏（连同其上方的窄缝）、城池面板
            occ.Clear();
            AddOcc(topBar, true);
            if (cityPanel.gameObject.activeSelf) AddOcc(cityPanel, false);
            float scale = UIKit.Canvas != null ? UIKit.Canvas.scaleFactor : 1;
            float W = Screen.width, H = Screen.height;
            var camPos = cam.transform.position;
            cand.Clear();
            foreach (var kv in labels)
            {
                var l = kv.Value;
                var wp = l.cv.LabelPos;
                var sp = cam.WorldToScreenPoint(wp);
                bool vis = sp.z > 0 && Vector3.Distance(camPos, wp) < hideBeyond && sp.x > -W * 0.1f && sp.x < W * 1.1f && sp.y > -H * 0.1f && sp.y < H * 1.1f;
                if (!vis)
                {
                    // 不在画面内；再次进入画面时直接取目标透明度，不闪现
                    l.cg.alpha = 0; l.seen = false; l.on = false;
                    continue;
                }
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(UIKit.LabelLayer, sp, null, out local);
                l.rt.anchoredPosition = local;
                var city = G.cities[kv.Key];
                int rank = kv.Key == selected ? 0 : city.owner == G.player ? 1 : 2;
                cand.Add(new Cand { l = l, id = kv.Key, rank = rank, pop = city.population, sp = new Vector2(sp.x, sp.y) });
            }
            cand.Sort((a, b) => a.rank != b.rank ? a.rank - b.rank : a.pop != b.pop ? b.pop - a.pop : a.id - b.id);
            placed.Clear();
            float k = dt / LabelFade;
            foreach (var o in cand)
            {
                var l = o.l;
                // 滞后：已显示的标签真正压住才隐藏；已隐藏的要留出几像素空隙才重新出现，镜头移动时不闪烁
                float pad = (l.on ? -1 : 4) * scale;
                float hw = l.rt.sizeDelta.x * scale / 2, hh = l.rt.sizeDelta.y * scale / 2;
                var test = Rect.MinMaxRect(o.sp.x - hw - pad, o.sp.y - hh - pad, o.sp.x + hw + pad, o.sp.y + hh + pad);
                bool ok = true;
                foreach (var q in occ) if (test.Overlaps(q)) { ok = false; break; }
                if (ok) foreach (var q in placed) if (test.Overlaps(q)) { ok = false; break; }
                if (ok) placed.Add(Rect.MinMaxRect(o.sp.x - hw, o.sp.y - hh, o.sp.x + hw, o.sp.y + hh));
                l.on = ok;
                float target = ok ? 1 : 0;
                if (!l.seen || l.fade < 0) l.fade = target;
                else l.fade = target > l.fade ? Mathf.Min(target, l.fade + k) : Mathf.Max(target, l.fade - k);
                l.seen = true;
                l.cg.alpha = l.fade;
            }
        }
        void AddOcc(RectTransform rt, bool toTop)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return;
            rt.GetWorldCorners(corners);   // 屏幕空间覆盖画布：世界坐标即屏幕像素
            float xMin = corners[0].x, yMin = corners[0].y, xMax = corners[2].x, yMax = corners[2].y;
            if (!(xMax > xMin && yMax > yMin)) return;
            if (toTop) yMax = 1e5f;        // 顶栏上方的窄缝也算遮挡：标签伸进去只会露出半截
            occ.Add(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
        }

        void RefreshAll()
        {
            if (hud == null) return;
            var f = G.PlayerFaction;
            topLeft.text = "<color=" + Hex(f.Col) + ">■</color> " + G.Ruler(G.player).name + "军";
            int gold = G.CitiesOf(G.player).Sum(c => c.gold), food = G.CitiesOf(G.player).Sum(c => c.food);
            string tokens = new string('●', Mathf.Max(0, G.tokens)) + new string('○', Mathf.Max(0, G.TokensFor(G.player) - G.tokens));
            topCenter.text = string.Format("<b>{0}</b>年 <b>{1}</b>月　令牌 <color=#f3c969>{2}</color>　城 {3}　金 {4}　粮 {5}",
                G.year, G.month, tokens, G.CityCount(G.player), gold, food);
            endBtn.button.interactable = !busy;
            saveBtn.button.interactable = !busy && !endMonth;
            foreach (var kv in labels) RenderLabel(kv.Key, kv.Value);
            RefreshCityPanel();
        }

        // 城名标签内容；紧凑模式（矮屏且镜头拉远，或世界地图远景）字号略小、不显示兵力，减少拥挤
        void RenderLabel(int id, CityLabel l)
        {
            var c = G.cities[id];
            var col = c.owner >= 0 ? G.factions[c.owner].Col : new Color(0.7f, 0.7f, 0.7f);
            l.text.GetComponent<FontBase>().Set(compact ? 20 : 22);
            l.text.text = "<color=" + Hex(col) + ">●</color>" + c.name + (c.owner == G.player && !compact ? " " + UIKit.Sz(16) + G.TroopsIn(c) + "</size>" : "");
            l.rt.sizeDelta = new Vector2(l.text.preferredWidth + (compact ? 16 : 22), compact ? 32 : 38);
            StyleLabel(id, l);
        }

        // 城名牌样式：选中城金边 + 金褐底 + 外圈光晕，己方城金边加重，其余淡金边（网页版 .is-selected / .is-mine）
        void StyleLabel(int id, CityLabel l)
        {
            int st = id == selected ? 2 : G.cities[id].owner == G.player ? 1 : 0;
            if (st == l.style || l.bg == null) return;
            l.style = st;
            l.bg.color = st == 2 ? new Color(0.2f, 0.15f, 0.07f, 0.88f) : new Color(0.05f, 0.05f, 0.08f, 0.72f);
            l.border.color = new Color(0.95f, 0.79f, 0.41f, st == 2 ? 1f : st == 1 ? 0.5f : 0.26f);
            l.ring.enabled = st == 2;
        }

        void SelectCity(int id)
        {
            selected = id;
            foreach (var kv in labels) StyleLabel(kv.Key, kv.Value);
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
            cityTitle.text = c.name + "  " + UIKit.Sz(24) + (owner != null ? "<color=" + Readable(owner.Col) + ">" + owner.name + "</color>" : "<color=#a8a194>空城</color>") + "</size>";
            var govGen = c.governor >= 0 ? G.generals[c.governor] : null;
            var free = G.FreeFoundIn(c.id).ToList();
            // 每项一个 .sg-cs（第三版：太守名前带行内小头像）；分行方式见 SetCityStats
            var csItems = new List<string> {
                "太守 " + (govGen != null ? Face(govGen) + "<b>" + govGen.name + "</b>" : "<b>—</b>"), "人口 " + N0(c.population),
                "土地 <b>" + c.land + "</b>", "产业 <b>" + c.industry + "</b>", "町 <b>" + c.town + "</b>",
                "金 <b>" + c.gold + "</b>", "粮 <b>" + c.food + "</b>", "防御 " + c.Defense,
                "兵力 <b>" + G.TroopsIn(c) + "</b>", "武将 " + G.OfficersIn(c).Count() + " 人" };
            if (free.Count > 0 && mine) csItems.Add("<color=#73d98c>在野 " + free.Count + " 人</color>");
            SetCityStats(csItems);
            foreach (Transform t in genList) Destroy(t.gameObject);
            // 武将列表（君主优先，其次兵力）；点按武将行 → 武将详情
            foreach (var g in G.OfficersIn(c).OrderByDescending(x => G.IsRuler(x)).ThenByDescending(x => x.troops))
            {
                var gen = g;
                // 底色 rgba(255,255,255,.05)（由按钮的 normalColor 着色）；按下时淡金 rgba(243,201,105,.14)
                var row = UIKit.Img(genList, UIKit.RR, Color.white, "Gen");
                UIKit.Size(row, -1, Mathf.Max(RowPortrait + 12, UIKit.MinTouch));
                var btn = row.gameObject.AddComponent<Button>();
                var cb = btn.colors;
                cb.normalColor = new Color(1, 1, 1, 0.05f); cb.highlightedColor = new Color(1, 1, 1, 0.09f); cb.pressedColor = new Color(0.953f, 0.788f, 0.412f, 0.14f);
                cb.selectedColor = cb.normalColor; cb.disabledColor = cb.normalColor; cb.colorMultiplier = 1; cb.fadeDuration = 0.08f;
                btn.colors = cb;
                btn.targetGraphic = row;
                btn.onClick.AddListener(() => ShowGeneral(gen));
                var pim = UIKit.PortraitImg(row.transform, gen, RowPortrait);
                UIKit.Place(pim.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(RowPortrait, RowPortrait));
                var tag = G.IsRuler(gen) ? UIText.Tag(TagStyle.Ruler, "君") : gen.id == c.governor ? UIText.Tag(TagStyle.Gov, "守") : "";
                // 第三版：等级小标签；我方武将可习得新招时另有「新」
                string lv = UIKit.LvTag(gen, TagStyle.LvRow) + (mine && GrowthText.CanLearn(gen) ? UIText.Tag(TagStyle.New, "新") : "");
                var l = UIKit.Label(row.transform, tag + "<b>" + gen.name + "</b>" + lv + (gen.moved ? " " + Small("已行动", 15) : ""), 22, UIKit.Text, TextAnchor.MiddleLeft);
                UIKit.Stretch(l.rectTransform, 6 + RowPortrait + 10, 0, 170, 0);
                var r = UIKit.Label(row.transform, string.Format("武{0} 智{1} 政{2}\n兵{3} 训{4} 忠{5}", gen.war, gen.intel, gen.pol, gen.troops, gen.training, G.IsRuler(gen) ? 100 : gen.loyalty), 15, UIKit.Muted, TextAnchor.MiddleRight);
                UIKit.Stretch(r.rectTransform, 200, 0, 12, 0);
            }
            foreach (Transform t in cmdGrid) Destroy(t.gameObject);
            cmdButtons.Clear(); cmdWhy.Clear();
            if (!mine) return;
            // 每个指令给出“不能用的原因”（null = 可用）：先看是否忙碌，再看规则（城池、武将），最后看令牌
            string busyWhy = busy ? (endMonth ? "诸侯行动中，请稍候。" : "指令执行中，请稍候。") : null;
            string noTok = G.tokens > 0 ? null : "本月令牌已用完——点「结束本月」进入下个月。";
            bool canAttack = c.links.Any(i => G.cities[i].owner != G.player && !G.Allied(G.cities[i].owner, G.player));
            AddCmd("开发", First(busyWhy, noTok), () => Do(CmdDevelop(c)));
            AddCmd("征兵", First(busyWhy, noTok), () => Do(CmdRecruit(c)));
            AddCmd("训练", First(busyWhy, noTok), () => Do(CmdTrain(c)));
            AddCmd("搜索", First(busyWhy, noTok), () => Do(CmdSearch(c)));
            AddCmd("登用", First(busyWhy, free.Count > 0 ? null : "城中没有已发现的在野人才——先用「搜索」寻访。", noTok), () => Do(CmdHire(c)));
            AddCmd("移动", First(busyWhy, Commands.MoveBlocked(c), noTok), () => Do(CmdMove(c)));
            AddCmd("输送", First(busyWhy, Commands.TransportBlocked(c), noTok), () => Do(CmdTransport(c)));
            AddCmd("外交", First(busyWhy, noTok), () => Do(CmdDiplomacy(c)));
            AddCmd("出征", First(busyWhy, canAttack ? null : c.name + "周围没有可攻打的城池（只能攻打相邻的敌城或空城，同盟势力除外）。", noTok), () => Do(CmdAttack(c)), true);
            AddCmd("赏赐", busyWhy, () => Do(CmdReward(c)));
            AddCmd("交易", busyWhy, () => Do(CmdTrade(c)));
            bool rulerHere = G.OfficersIn(c).Any(G.IsRuler);
            AddCmd("任命", First(busyWhy, rulerHere ? "君主坐镇此城，无需另设太守。" : null), () => Do(CmdAppoint(c)));
        }
        static string First(params string[] whys) { foreach (var w in whys) if (w != null) return w; return null; }

        // why：不可用的原因——按钮显示为灰色，点按时说明原因而不执行；null 为可用
        void AddCmd(string name, string why, System.Action action, bool primary = false)
        {
            UIKit.Btn b = null;
            b = UIKit.Button(cmdGrid, name, () => { string w; if (cmdWhy.TryGetValue(name, out w) && w != null) ExplainCmd(b, name, w); else action(); }, 26, primary);
            cmdButtons[name] = b;
            cmdWhy[name] = why;
            if (why != null)
            {
                // 外观同禁用的按钮（ColorBlock.disabledColor），但仍接住点按
                b.button.transition = Selectable.Transition.None;
                var dc = new Color(0.55f, 0.55f, 0.55f, 0.6f);
                b.bg.color = b.bg.color * dc;
                b.label.color = new Color(UIKit.Text.r, UIKit.Text.g, UIKit.Text.b, 0.7f);
                var ol = b.rt.Find("Outline");
                if (ol != null) { var oi = ol.GetComponent<Image>(); oi.color = new Color(oi.color.r, oi.color.g, oi.color.b, oi.color.a * 0.6f); }
            }
            // 提示闪烁只给可用的按钮（灰色按钮闪金光会让人误以为能用）
            else if (hintName == name && Time.unscaledTime < hintUntil) UIKit.Glow(b.rt, hintUntil - Time.unscaledTime);
        }

        void ExplainCmd(UIKit.Btn b, string name, string why)
        {
            if (b == null) return;
            UIKit.StopGlow(b.rt);
            if (hintName == name) hintName = null;
            UIKit.Shake(b.rt);
            // 同一时间只留一条说明，连点不会堆叠
            if (denyToast != null) Destroy(denyToast.gameObject);
            denyToast = UIKit.Toast("<color=#f3c969><b>" + name + "</b></color>　" + why, 3.4f);
        }
        // 让某个指令按钮闪烁数次以引起注意（提示用）
        void HintCmd(string name)
        {
            hintName = name; hintUntil = Time.unscaledTime + 4.4f;
            UIKit.Btn b; string w;
            if (cmdButtons.TryGetValue(name, out b) && b != null && b.rt != null && cmdWhy.TryGetValue(name, out w) && w == null) UIKit.Glow(b.rt, 4.4f);
        }

        // 此刻能用「移动」的己方城（有令牌，且城中有未行动的武将可调往相连的己方城）；没有则 null。
        // 依次优先：当前选中的城、新取得的城、其余己方城。
        City MoveUsableCity()
        {
            if (G == null || G.tokens <= 0 || !G.PlayerFaction.alive) return null;
            var order = new List<int> { selected };
            order.AddRange(tipCities);
            order.AddRange(G.CitiesOf(G.player).Select(c => c.id));
            foreach (var id in order)
            {
                var c = id >= 0 && id < G.cities.Count ? G.cities[id] : null;
                if (c != null && c.owner == G.player && Commands.MoveBlocked(c) == null) return c;
            }
            return null;
        }
        // 第一次取得新城后弹一次提示：可用「移动」把武将调往其他城池，并让「移动」按钮闪烁。
        // 只在「移动」此刻真能用时弹出（必要时改选一座能用的城）；否则保持待弹，下个月月初再看。
        // fresh：刚取得新城（true）还是延到之后补弹（false），只影响第一句。
        IEnumerator MoveTip(bool fresh)
        {
            if (TipShown) { tipPending = false; yield break; }
            var c = MoveUsableCity();
            if (c == null) yield break;
            tipPending = false;
            tipCities.Clear();
            MarkTipShown();
            if (selected != c.id)
            {
                Game.I.Rig.FocusMap(c.MapPos.x, c.MapPos.y);
                SelectCity(c.id);
            }
            RefreshAll();              // 面板显示此刻的真实状态（「移动」可用）
            yield return UIKit.Say("【提示】" + (fresh ? "取得了新的城池！" : "领有两座以上相连的城池，可以调动武将了。") + "\n" +
                "用「移动」可把武将调往其他己方城池——只要经由己方城池相连即可，不必相邻（每次消耗 1 枚令牌，可一次调动多人）。\n" +
                "「输送」同样可以在己方城池之间调拨金粮。");
            HintCmd("移动");
        }

        void Do(IEnumerator routine) { if (busy || endMonth || UIKit.AnyModal) return; StartCoroutine(Wrap(routine)); }
        IEnumerator Wrap(IEnumerator r)
        {
            var before = new HashSet<int>(G.CitiesOf(G.player).Select(c => c.id));
            hintName = null;              // 下达新指令时停止提示闪烁
            bool gainedNow = false;
            busy = true; RefreshAll();
            yield return UIKit.Guard(r, e => Debug.LogException(e));
            // 出征攻下 / 敌将献城等使城池增加：第一次时提示「移动」（见 MoveTip）
            if (G.PlayerFaction.alive && !TipShown)
            {
                var gained = G.CitiesOf(G.player).Where(c => !before.Contains(c.id)).Select(c => c.id).ToList();
                if (gained.Count > 0) { gainedNow = true; tipPending = true; gained.AddRange(tipCities); tipCities = gained; }
            }
            busy = false;
            Game.I.Map.Refresh(G);
            RefreshAll();
            if (tipPending && !endMonth)   // 刚点了「结束本月」时不弹，留到月初
                yield return UIKit.Guard(MoveTip(gainedNow), e => Debug.LogException(e));
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
            // 第三版：每行带头像；君主另有「君」标签
            var items = gens.Select(g => new UIKit.Item(g.name + RulerTag(g), info(g), enabled == null || enabled(g), null, g)).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose(title, items, r, null, 700);
            if (r.v >= 0) result.v = gens[r.v];
        }
        IEnumerable<General> Available(City c) { return G.OfficersIn(c).Where(g => !g.moved); }
        // 武将汇报：以该武将的头像说话，文中提到的其他武将带小头像。cmd：指令名（姓名下的小字标签）；extraSub 接在其后
        IEnumerator Report(General who, string text, string cmd, PortraitMood mood = PortraitMood.Neutral, string extraSub = null)
        {
            string sub = (cmd != null ? UIText.Tag(TagStyle.Cmd, cmd) : "") + (extraSub ?? "");
            // 指令结果是第三人称（「刘备主持土地开发……」）：由本人汇报时省去句首的自己的名字
            text = GrowthText.ReportText(who, text);
            string body = Faces(text, new FacesOpts { skip = who != null ? new[] { who.name } : new string[0] });
            yield return UIKit.Say(body, who, new UIKit.SayOpts { mood = mood, sub = sub.Length > 0 ? sub : null });
        }

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
            yield return Report(who.v, Commands.Develop(c, who.v, kinds[r.v]), "开发");
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
            yield return Report(who.v, Commands.Recruit(c, who.v, n.v), "征兵");
        }

        IEnumerator CmdTrain(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("训练哪支部队？", G.OfficersIn(c), g => string.Format("兵 {0}　训练 {1}", g.troops, g.training), who, g => g.troops > 0 && g.training < 100);
            if (who.v == null) yield break;
            UseToken();
            yield return Report(who.v, Commands.Train(who.v), "训练");
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
            yield return Report(who.v, msg, "搜索", found != null ? PortraitMood.Win : PortraitMood.Neutral);
            if (found != null)
            {
                var ok = new Ref<bool>();
                yield return UIKit.Confirm("登用" + found.name + "？", ok, "登用", "暂且作罢",
                    new UIKit.GenCardSpec { gen = found, sub = "在野人才　成功率约 <b><color=#ffe08a>" + Pct(Commands.HireChance(found, who.v, G.player)) + "</color></b>" });
                if (ok.v) yield return HireResult(found, who.v, c);
            }
        }

        IEnumerator HireResult(General target, General recruiter, City c)
        {
            if (Commands.Hire(target, recruiter, G.player, c.id))
            {
                Sfx.Play("win", 0.5f);
                yield return UIKit.Say(target.name + "：久闻明公大名，愿效犬马之劳！\n（" + target.name + "加入了我军）", target, new UIKit.SayOpts { mood = PortraitMood.Win, sub = GenSub(target) }, UIKit.Cinnabar);
            }
            else yield return UIKit.Say(target.name + "：在下另有志向，请回吧。\n" + Faces("（" + recruiter.name + "登用未成）"), target, new UIKit.SayOpts { mood = PortraitMood.Angry, sub = GenSub(target) }, GreySpeaker);
        }

        IEnumerator CmdHire(City c)
        {
            var target = new Ref<General>();
            yield return PickGeneral("登用哪位在野人才？", G.FreeFoundIn(c.id), g => string.Format("武{0} 智{1} 政{2}", g.war, g.intel, g.pol), target);
            if (target.v == null) yield break;
            var who = new Ref<General>();
            yield return PickGeneral("派谁前去？", G.OfficersIn(c), g => string.Format("智力 {0}　成功率约 {1}", g.intel, Pct(Commands.HireChance(target.v, g, G.player))), who);
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
            UIKit.Toast(Faces(Commands.Reward(c, who.v), new FacesOpts { gens = new[] { who.v } }));
        }

        IEnumerator CmdAppoint(City c)
        {
            var who = new Ref<General>();
            yield return PickGeneral("任命太守", G.OfficersIn(c), g => string.Format("政{0} 智{1}", g.pol, g.intel), who);
            if (who.v == null) yield break;
            if (G.OfficersIn(c).Any(G.IsRuler) && !G.IsRuler(who.v)) { yield return Report(G.Ruler(G.player), "此城由我亲自坐镇，不另设太守。", "任命"); yield break; }
            c.governor = who.v.id;
            UIKit.Toast(Face(who.v) + who.v.name + "出任" + c.name + "太守");
        }

        // 移动：目的地为经由己方城池相连可达的所有己方城（不必相邻），列表注明路程
        IEnumerator CmdMove(City c)
        {
            var blocked = Commands.MoveBlocked(c);
            if (blocked != null) { yield return UIKit.Say(blocked); yield break; }
            var routes = Commands.MoveTargets(c);
            var r = new Ref<int>();
            yield return UIKit.Choose("移往何处？", routes.Select(rt => new UIKit.Item(rt.city.name,
                RouteText(rt) + "　武将 " + G.OfficersIn(rt.city).Count() + "　兵 " + G.TroopsIn(rt.city), true, ViaText(rt))).ToList(), r,
                "经由己方城池相连即可前往，不必相邻（1 枚令牌）");
            if (r.v < 0) yield break;
            var route = routes[r.v];
            var dest = route.city;
            var gens = Available(c).ToList();
            if (gens.Count == 0) { yield return UIKit.Say("本月已无可调动的武将。"); yield break; }
            var sel = new Ref<List<int>>();
            yield return UIKit.ChooseMany("调动武将至" + dest.name + "（" + RouteText(route) + "）",
                gens.Select(g => new UIKit.Item(g.name + RulerTag(g), "兵 " + g.troops + "　武" + g.war + " 智" + g.intel, true, null, g)).ToList(), 10, sel, "可一次调动多名武将（消耗 1 枚令牌）");
            if (sel.v == null || sel.v.Count == 0) yield break;
            UseToken();
            yield return MarchRoute(route.path, G.PlayerFaction.Col);
            var moved = new List<General>();
            foreach (var i in sel.v) { Commands.Move(gens[i], dest); if (gens[i].city == dest.id) moved.Add(gens[i]); }
            UIKit.Toast(string.Concat(moved.Take(6).Select(Face).ToArray()) + " " + moved.Count + " 名武将移驻" + dest.name, 2.8f);
        }

        // 沿己方路线逐城行军：相邻 1.2 秒；路线越长每段越快，总长不超过约 2.6 秒
        IEnumerator MarchRoute(List<int> path, Color color)
        {
            int hops = path.Count - 1;
            if (hops <= 0) yield break;
            float per = Mathf.Min(1.2f, 2.6f / hops);
            for (int i = 0; i < hops; i++) yield return Game.I.Map.March(G.cities[path[i]], G.cities[path[i + 1]], color, per);
        }

        IEnumerator CmdTransport(City c)
        {
            var blocked = Commands.TransportBlocked(c);
            if (blocked != null) { yield return UIKit.Say(blocked); yield break; }
            var routes = Commands.TransportTargets(c);
            var r = new Ref<int>();
            yield return UIKit.Choose("输送至何处？", routes.Select(rt => new UIKit.Item(rt.city.name,
                RouteText(rt) + "　金 " + rt.city.gold + "　粮 " + rt.city.food, true, ViaText(rt))).ToList(), r,
                "经由己方城池相连即可送达，不必相邻（1 枚令牌）");
            if (r.v < 0) yield break;
            var dest = routes[r.v].city;
            var gold = new Ref<int>(); var food = new Ref<int>();
            yield return UIKit.PickNumber("输送金", 0, c.gold, 50, c.gold / 2, null, gold);
            if (gold.v < 0) yield break;
            yield return UIKit.PickNumber("输送粮", 0, c.food, 500, c.food / 2, null, food);
            if (food.v < 0) yield break;
            if (gold.v == 0 && food.v == 0) { UIKit.Toast("未输送任何金粮。"); yield break; }
            UseToken();
            UIKit.Toast(Commands.Transport(c, dest, gold.v, food.v));
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

        // ---------------------------------------------------------- 按地域分组 --
        // 势力列表按地域分组：分组标题行（不可选）+ 各势力行。玩家所在地域排在最前，其余由近及远。
        // fs：势力（已排序）；row(f) → 列表行。pick[i] 为列表第 i 行的势力（标题行为 null）
        sealed class Grouped { public List<UIKit.Item> items = new List<UIKit.Item>(); public List<Faction> pick = new List<Faction>(); }
        static Grouped GroupedFactions(List<Faction> fs, System.Func<Faction, UIKit.Item> row)
        {
            var o = new Grouped();
            var regs = RegionsOf();
            if (regs == null) { foreach (var f in fs) { o.items.Add(row(f)); o.pick.Add(f); } return o; }
            var g = G;
            string mine = g.PlayerFaction != null ? g.PlayerFaction.region : null;
            var near = RegionNearness(regs);
            var order = regs.OrderByDescending(r => r.Id == mine).ThenBy(r => near[r.Id]).ToList();   // 稳定排序
            foreach (var r in order)
            {
                var list = fs.Where(f => f.region == r.Id).ToList();
                if (list.Count == 0) continue;
                o.items.Add(new UIKit.Item(r.Name, "势力 " + list.Count + "　城 " + list.Sum(f => g.CityCount(f.id)), false) { header = true });
                o.pick.Add(null);
                foreach (var f in list) { o.items.Add(row(f)); o.pick.Add(f); }
            }
            foreach (var f in fs) if (!o.pick.Contains(f)) { o.items.Add(row(f)); o.pick.Add(f); }
            return o;
        }
        // 各地域离我方的远近：自我方城池沿连线到该地域势力所据城池的最少步数（并列时按地图上的直线距离），
        // 使邻近的地域排在前面，而不是固定把中原十八路诸侯放在第二组
        static Dictionary<string, double> RegionNearness(ScenarioRegion[] regs)
        {
            var g = G;
            var outD = new Dictionary<string, double>();
            var hops = new int[g.cities.Count];
            for (int i = 0; i < hops.Length; i++) hops[i] = int.MaxValue;
            var q = g.CitiesOf(g.player).Select(c => c.id).ToList();
            foreach (var id in q) hops[id] = 0;
            for (int d = 1; q.Count > 0; d++)
            {
                var next = new List<int>();
                foreach (var id in q) foreach (var j in g.cities[id].links) if (hops[j] == int.MaxValue) { hops[j] = d; next.Add(j); }
                q = next;
            }
            City capC = null;
            if (g.player >= 0) { var ru = g.Ruler(g.player); if (ru != null && ru.city >= 0) capC = g.cities[ru.city]; }
            Vector2? cp = capC != null ? capC.MapPos : (Vector2?)null;
            foreach (var r in regs)
            {
                int h = int.MaxValue; double dx = 0; int n = 0;
                foreach (var c in g.cities)
                {
                    if (c.owner < 0 || g.factions[c.owner].region != r.Id) continue;
                    h = Mathf.Min(h, hops[c.id]);
                    if (cp != null) { var p = c.MapPos; dx += Vector2.Distance(p, cp.Value); n++; }
                }
                outD[r.Id] = (h == int.MaxValue ? 1e6 : h * 1e4) + (n > 0 ? dx / n : 0);
            }
            return outD;
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
                bool world = RegionsOf() != null;
                var grp = GroupedFactions(fs, f => new UIKit.Item(Swatch(f) + " " + G.Ruler(f.id).name + (world && f.name != G.Ruler(f.id).name ? "  " + Small(f.name, 19) : ""),
                    G.Allied(f.id, G.player) ? "已同盟" : "城 " + G.CityCount(f.id) + "　成功率约 " + Pct(Commands.AllyChance(G.player, f.id, Balance.AllianceGift)),
                    !G.Allied(f.id, G.player), null, G.Ruler(f.id)));
                var fi = new Ref<int>();
                yield return UIKit.Choose("与谁结盟？", grp.items, fi);
                var ff = fi.v >= 0 ? grp.pick[fi.v] : null;
                if (ff == null) yield break;
                var gift = new Ref<int>();
                yield return UIKit.PickNumber("赠送礼金", 0, c.gold, 50, Mathf.Min(c.gold, Balance.AllianceGift), v => "成功率约 " + Pct(Commands.AllyChance(G.player, ff.id, v)), gift);
                if (gift.v < 0) yield break;
                UseToken();
                // 对方君主以头像答复（同意 / 拒绝），其后为结果
                var res0 = Commands.Ally(c, G.player, ff.id, gift.v);
                bool okAlly = G.Allied(ff.id, G.player);
                var fru = G.Ruler(ff.id);
                yield return UIKit.Say((okAlly ? "……愿与贵方结为同盟，共图大业。" : "……此事不必再提。") + "\n" + res0, fru, new UIKit.SayOpts { mood = okAlly ? PortraitMood.Win : PortraitMood.Angry, sub = GenSub(fru) });
                yield break;
            }
            var agent = new Ref<General>();
            yield return PickGeneral("派谁执行？", Available(c), g => "智力 " + g.intel, agent);
            if (agent.v == null) yield break;
            var targets = c.links.Select(i => G.cities[i]).Where(x => x.owner >= 0 && x.owner != G.player).SelectMany(x => G.OfficersIn(x)).Where(g => !G.IsRuler(g)).ToList();
            if (targets.Count == 0) { yield return UIKit.Say("邻近城池中没有可以下手的敌将。"); yield break; }
            var target = new Ref<General>();
            if (r.v == 1) yield return PickGeneral("离间哪位敌将？", targets, g => G.factions[g.faction].name + "·" + G.cities[g.city].name + "　智" + g.intel + "　忠诚 " + g.loyalty, target);
            else yield return PickGeneral("拉拢哪位敌将？", targets, g => G.factions[g.faction].name + "·" + G.cities[g.city].name + "　忠诚 " + g.loyalty + "　成功率约 " + Pct(Commands.PersuadeChance(agent.v, g, G.player)), target);
            if (target.v == null) yield break;
            UseToken();
            agent.v.moved = true;
            var res = r.v == 1 ? Commands.Discord(agent.v, target.v) : Commands.Persuade(agent.v, target.v, G.player);
            bool won = r.v == 1 ? res.IndexOf("成功", System.StringComparison.Ordinal) >= 0 : target.v.faction == G.player;
            yield return Report(agent.v, res, r.v == 1 ? "离间" : "拉拢", won ? PortraitMood.Win : PortraitMood.Neutral);
        }

        // ---------------------------------------------------------- 战争 --
        IEnumerator CmdAttack(City c)
        {
            var targets = c.links.Select(i => G.cities[i]).Where(x => x.owner != G.player && !G.Allied(x.owner, G.player)).ToList();
            // 每座目标城列出守将的小头像（至多 5 人，再多显示 +N）
            System.Func<City, string> guards = t =>
            {
                var os = G.OfficersIn(t).OrderByDescending(x => G.IsRuler(x)).ThenByDescending(x => x.troops).ToList();
                int n = os.Count > 5 ? 4 : os.Count;
                return os.Count > 0 ? string.Concat(os.Take(n).Select(Face).ToArray()) + (os.Count > n ? "<color=#a8a194>+" + (os.Count - n) + "</color>" : "") : null;
            };
            var r = new Ref<int>();
            yield return UIKit.Choose("攻打何处？", targets.Select(t => new UIKit.Item(t.name, (t.owner >= 0 ? G.factions[t.owner].name : "空城") + "　武将 " + G.OfficersIn(t).Count() + "　兵 " + G.TroopsIn(t) + "　防 " + t.Defense, true, guards(t))).ToList(), r);
            if (r.v < 0) yield break;
            var target = targets[r.v];
            var gens = Available(c).Where(g => g.troops > 0).ToList();
            if (gens.Count == 0) { yield return UIKit.Say("没有可以出征的部队（需有兵力且本月未行动）。"); yield break; }
            var sel = new Ref<List<int>>();
            yield return UIKit.ChooseMany("出征武将（至多 " + Balance.MaxSortieGenerals + " 名）", gens.Select(g => new UIKit.Item(g.name + RulerTag(g), string.Format("兵{0} 武{1} 智{2} 训{3}", g.troops, g.war, g.intel, g.training), true, null, g)).ToList(), Balance.MaxSortieGenerals, sel, "君主出征时由君主任主将，否则为列表中最靠前的武将；主将败走则全军撤退。");
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
            yield return Alarm(s);
            yield return Fight(s, 1);
        }

        // 急报：来犯主将的头像（怒容、敌方势力色边框）示警，随行武将带小头像
        IEnumerator Alarm(BattleSetup s)
        {
            var lead = s.atk[0];
            var fac = G.factions[s.attacker];
            string leader = G.Ruler(s.attacker).name == lead.name ? lead.name : fac.name + "军" + lead.name;
            var rest = s.atk.Skip(1).ToList();
            string text = "<color=#ff7361><b>急报！</b></color>" + leader + "率 " + s.atk.Count + " 名武将、兵 <b>" + N0(s.atk.Sum(x => x.troops)) + "</b> 来犯" + s.target.name + "！" +
                (rest.Count > 0 ? "\n<color=#a8a194>随行</color> " + string.Join("　", rest.Select(x => Face(x) + x.name).ToArray()) : "");
            yield return UIKit.Say(text, lead, new UIKit.SayOpts { mood = PortraitMood.Angry, sub = GenSub(lead), alarm = true });
        }

        // 战斗与战后处理。playerSide：玩家是攻方 0 还是守方 1
        IEnumerator Fight(BattleSetup s, int playerSide)
        {
            if (s.def.Count == 0 || s.def.All(d => d.troops <= 0))
            {
                Conquest.AutoResolve(s);
                Conquest.Apply(s);
                Game.I.Map.Refresh(G); RefreshAll();
                var fr = new Ref<List<CaptiveFate>>();
                if (s.captives.Count > 0) yield return HandleCaptives(s, playerSide == 0, fr);
                Game.I.Map.Refresh(G); RefreshAll();
                // 空城：同样由我方主将汇报（守城武将全被俘时由君主汇报）
                bool won0 = s.attackerWon == (playerSide == 0);
                var who0 = OurReporter(s, playerSide);
                if (who0 != null) yield return Report(who0, s.summary, s.target.name + "之战", won0 ? PortraitMood.Win : PortraitMood.Hurt);
                else yield return UIKit.Say(Faces(s.summary));
                yield return ReportCaptives(fr.v, s.attackerWon ? s.attacker : s.defender);
                yield return Growth(s, playerSide);
                yield break;
            }
            // 说明区：双方出阵武将的小头像
            System.Func<List<General>, string, string, string> side = (list, col, nm) => "<color=" + col + "><b>" + nm + "</b></color> " + string.Concat(list.Take(5).Select(Face).ToArray()) + (list.Count > 5 ? "<color=#a8a194>+" + (list.Count - 5) + "</color>" : "");
            string vs = side(s.atk, "#73bfff", "攻") + "　" + side(s.def, "#ff806b", "守");
            var mode = new Ref<int>();
            yield return UIKit.Choose(s.target.name + "之战", new List<UIKit.Item> { new UIKit.Item("亲自指挥", "在战场上调兵遣将"), new UIKit.Item("委任", "由部将自行作战，立即得出结果") }, mode, vs, 560);
            if (mode.v == 0)
            {
                hud.gameObject.SetActive(false);
                SetMapLabelsVisible(false);
                bool failed = false;
                // 防御：战斗画面出错时由电脑结算，避免整局卡死
                yield return UIKit.Guard(BattleController.Run(s, playerSide), e => { Debug.LogException(e); failed = true; });
                if (failed) Conquest.AutoResolve(s);
                hud.gameObject.SetActive(true);
                SetMapLabelsVisible(true);
                MapMusic();
            }
            else Conquest.AutoResolve(s);
            Conquest.Apply(s);
            // 先刷新地图与城池面板（处置俘虏的对话期间显示战后兵力与归属）
            Game.I.Map.Refresh(G); RefreshAll();
            bool playerWon = s.attackerWon == (playerSide == 0);
            List<CaptiveFate> fates = null;
            if (s.captives.Count > 0)
            {
                if (playerWon) yield return HandleCaptives(s, true, new Ref<List<CaptiveFate>>());
                else fates = Conquest.AiDecideCaptives(s, playerSide == 0 ? s.defender : s.attacker);
            }
            foreach (var f in G.factions) if (f.alive && G.CityCount(f.id) == 0) G.CheckFactionDeath(f.id);
            Game.I.Map.Refresh(G);
            RefreshAll();
            string res = playerSide == 0
                ? (s.attackerWon ? "我军攻陷了" + s.target.name + "！" : "攻城失利，全军撤回" + s.src.name + "。")
                : (s.attackerWon ? s.target.name + "失守了……" : "我军成功守住了" + s.target.name + "！");
            // 由我方主将汇报战果（主将阵亡 / 被俘时由其余在籍的武将汇报，都不在时由君主汇报）
            var who = OurReporter(s, playerSide);
            if (who != null) yield return Report(who, res, s.target.name + "之战", playerWon ? PortraitMood.Win : PortraitMood.Hurt);
            else yield return UIKit.Say(res);
            yield return ReportCaptives(fates, playerSide == 0 ? s.defender : s.attacker);
            if (s.attackerWon && s.defender >= 0 && !G.factions[s.defender].alive) yield return UIKit.Say(Faces(G.factions[s.defender].name + "势力就此灭亡。"));
            yield return Growth(s, playerSide);
        }

        // 汇报战果的我方武将：出阵（守城）武将中第一位仍在籍、未被俘的；都不在时为君主；我方已灭亡 → null
        General OurReporter(BattleSetup s, int playerSide)
        {
            var g = G;
            var caps = new HashSet<int>((s.captives ?? new List<General>()).Select(x => x.id));
            System.Func<General, bool> ok = x => x != null && !x.dead && x.faction == g.player && !caps.Contains(x.id);
            var who = (playerSide == 0 ? s.atk : s.def).FirstOrDefault(ok);
            if (who != null) return who;
            var pf = g.player >= 0 ? g.factions[g.player] : null;
            var ru = pf != null && pf.alive ? g.Ruler(g.player) : null;
            return ok(ru) ? ru : null;
        }

        // ---------------------------------------------------------- 成长（第三版 §4） --
        // 战后：我方武将的升级通知（头像、「关羽 升至 5 级」、经验条），然后为有空格的武将逐个弹出习得对话框。
        // playerSide：玩家是攻方 0 / 守方 1（-1 不限）。战后归降我方的敌将不算（他们的经验来自对我军作战）
        IEnumerator Growth(BattleSetup s, int playerSide)
        {
            var g = G;
            if (!Progress.enabled || g.PlayerFaction == null || !g.PlayerFaction.alive) yield break;
            System.Func<ProgressResult, bool> ours = x => x != null && x.gen != null && !x.gen.dead && x.gen.faction == g.player && (!(playerSide >= 0) || x.side == playerSide);
            var prog = (s != null && s.progress != null ? s.progress : new List<ProgressResult>()).Where(ours).ToList();
            var ups = prog.Where(x => x.to > x.from).ToList();
            if (ups.Count > 0) yield return UIKit.Guard(LevelUpNotice(ups, prog.Where(x => !(x.to > x.from) && x.exp > 0).ToList(), s), e => Debug.LogException(e));
            else if (prog.Any(x => x.exp > 0))
            {
                int tot = prog.Sum(x => x.exp);
                UIKit.Toast("出阵武将获得经验 <b>+" + tot + "</b>", 1.8f);
            }
            // 升级的武将先选，其余保留着空格的随后（例如上次「稍后再选」）
            var order = new List<General>();
            foreach (var x in ups) if (!order.Contains(x.gen)) order.Add(x.gen);
            foreach (var x in prog) if (!order.Contains(x.gen)) order.Add(x.gen);
            foreach (var gen in order)
            {
                if (!GrowthText.CanLearn(gen)) continue;
                var r = new Ref<string>();
                yield return LearnDialog(gen, r);
                if (r.v == "later-all") break;
            }
            RefreshAll();
        }

        // 升级通知：每位升级的武将一行（头像、升至 N 级、经验条与本战所得、新解锁的格）；其余出阵武将的经验附在末尾。
        // 说明区可滚动（矮屏时按钮留在框底）
        IEnumerator LevelUpNotice(List<ProgressResult> ups, List<ProgressResult> rest, BattleSetup s)
        {
            bool pend = ups.Any(x => GrowthText.CanLearn(x.gen));
            string title = (s != null && s.target != null ? s.target.name + "之战 · " : "") + "武将成长";
            System.Func<RectTransform, float, float> body = (parent, w) =>
            {
                float y = 0;
                foreach (var x in ups)
                {
                    var gen = x.gen;
                    var e = Progress.ExpInfo(gen);
                    var op = Progress.OpeningsOf(gen);
                    var learn = new List<string>();
                    if (op.special > 0) learn.Add("可习得新必杀" + (op.special > 1 ? " " + op.special + " 招" : "") + "（" + GrowthText.OpenTiers(Progress.SpecialSlots(gen), GrowthText.SpecialTier) + "）");
                    if (op.tactic > 0) learn.Add("可习得新计策" + (op.tactic > 1 ? " " + op.tactic + " 计" : "") + "（" + GrowthText.OpenTiers(Progress.TacticSlots(gen), GrowthText.TacticTier) + "）");
                    string learned = string.Concat((x.learned ?? new List<LearnCandidate>()).Select(c => "「" + c.name + "」").ToArray());
                    // 一行：金色渐变底、淡金描边（.sg-lvup-row）
                    var row = UIKit.Img(parent, UIKit.RR, new Color32(0x2c, 0x25, 0x1a, 255), "LvUp");
                    row.raycastTarget = false;
                    var ol = UIKit.Img(row.transform, UIKit.RROutline, new Color(0.953f, 0.788f, 0.412f, 0.28f), "Outline"); UIKit.Stretch(ol.rectTransform); ol.raycastTarget = false;
                    var tint = UIKit.Img(row.transform, UIKit.RR, new Color(0.953f, 0.788f, 0.412f, 0.1f), "Tint"); UIKit.Stretch(tint.rectTransform, 2, 2, w * 0.35f, 2); tint.raycastTarget = false;
                    var rr = row.rectTransform;
                    float face = UIKit.FaceLvUp, tx = 15 + face + 17, tw = w - tx - 15;
                    float ty = 10;
                    var head = UIKit.Label(rr, "", 24, UIKit.Text, TextAnchor.UpperLeft, false, "Head");
                    UIKit.Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(tx, -ty), new Vector2(tw, 34));
                    head.text = "<color=#fffaf0><b>" + gen.name + "</b></color> 升至 <color=#f3c969><b><size=" + UIKit.Fs(32) + ">" + e.level + "</size></b></color> 级" +
                        (x.to - x.from > 1 ? "<color=#a8a194>（连升 " + (x.to - x.from) + " 级）</color>" : "");
                    ty += 36;
                    var bar = UIKit.ExpBar(rr, e.pct, 150, 10);
                    bar.anchoredPosition = new Vector2(tx, -ty - 12);
                    var exl = UIKit.Label(rr, "+" + x.exp + "<color=#a8a194>　" + (e.max ? "已满级" : e.exp + " / " + e.next) + "</color>", 18, UIKit.Text, TextAnchor.MiddleLeft, false, "Exp");
                    UIKit.Place(exl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(tx + 162, -ty), new Vector2(tw - 162, 24));
                    ty += 26;
                    if (learn.Count > 0)
                    {
                        float lh = UIKit.FlowText(rr, "<color=#73d98c>" + string.Join("　", learn.ToArray()) + "</color>", 18, UIKit.Text, tx, ty, tw, false, "New");
                        ty += lh + 2;
                    }
                    if (learned.Length > 0) { ty += UIKit.FlowText(rr, "<color=#73d98c>习得" + learned + "</color>", 18, UIKit.Text, tx, ty, tw, false, "Learned") + 2; }
                    float rh = Mathf.Max(face + 20, ty + 8);
                    UIKit.Place(rr, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -y), new Vector2(w, rh));
                    var pim = UIKit.PortraitImg(rr, gen, face, PortraitMood.Win);
                    UIKit.Place(pim.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(15, 0), new Vector2(face, face));
                    var pr = UIKit.Img(pim.transform, UIKit.RROutline, new Color(0.953f, 0.788f, 0.412f, 0.6f), "Ring"); UIKit.Stretch(pr.rectTransform, -2, -2, -2, -2); pr.raycastTarget = false;
                    row.gameObject.AddComponent<PopIn>();          // 逐行弹出（.sg-lvup-row 的 sg-lvup-in）
                    y += rh + 11;
                }
                if (rest.Count > 0)
                {
                    string tail = "<color=#a8a194>其余出阵</color> " + string.Join("　", rest.Select(r0 => Face(r0.gen) + r0.gen.name + " <b>+" + r0.exp + "</b>").ToArray());
                    var tl = UIKit.Label(parent, "", 19, UIKit.Text, TextAnchor.UpperLeft, false, "Rest");
                    tl.lineSpacing = 1.3f;
                    UIKit.Place(tl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -y - 3), new Vector2(w - 12, 30));
                    UIKit.SetText(tl, tail);
                    float th = Mathf.Ceil(tl.preferredHeight) + 4;
                    tl.rectTransform.sizeDelta = new Vector2(w - 12, th);
                    y += th + 6;
                }
                return y;
            };
            yield return UIKit.Notice(title, body, pend ? "选择新招" : "确定", 640);
        }

        // 习得对话框：3 个候选（必杀 / 计策）→ Progress.Learn；可连续学（同时开了多个格）。「稍后再选」→ 保留空格（武将详情里可再学）
        // → result：learned | later | later-all（× / Esc：本次战后不再逐个询问其余武将；已学的招式照常保留）
        IEnumerator LearnDialog(General gen, Ref<string> result)
        {
            result.v = "later";
            if (!Progress.enabled) yield break;
            var g = G;
            bool learnedAny = false;
            for (int guard = 0; guard < 10; guard++)
            {
                var cands = Progress.Candidates(gen, 3);
                if (cands.Count == 0) break;
                bool shortS = ShortScreen();
                var items = new List<UIKit.Item>();
                foreach (var c in cands)
                {
                    bool sp = c.type == "special";
                    string label = UIText.Tag(sp ? TagStyle.KindSpecial : TagStyle.KindTactic, sp ? "必杀" : "计策") + "<color=" + Specials.UiColor(c.color ?? "#f3c969") + ">" + c.name + "</color>";
                    string lore = sp ? (c.special != null ? c.special.lore : null) : (c.tactic != null ? c.tactic.Lore : null);
                    string bodyText = GrowthText.StripLore(c.desc, lore);
                    // 计策的摘要以等阶开头（右栏已有），去掉
                    string rules = GrowthText.RulesNoTier(c.rules);
                    // 矮屏（横屏手机）：说明只留第一句（截断），数值摘要另起一行
                    string brief = shortS ? GrowthText.Brief(bodyText) : bodyText;
                    string desc = brief + "\n<color=#a8a194>" + rules + "</color>";
                    items.Add(new UIKit.Item(label, UIText.Tag(UIKit.TierTag(c.tier), GrowthText.TierText(c)), true, desc) { descBlock = true });
                }
                items.Add(new UIKit.Item("<color=#a8a194>稍后再选</color>", "<color=#a8a194>保留空格</color>", true));
                var e = Progress.ExpInfo(gen);
                var op = Progress.OpeningsOf(gen);
                var open = new List<string>();
                if (op.special > 0) open.Add("必杀 " + op.special);
                if (op.tactic > 0) open.Add("计策 " + op.tactic);
                var card = new UIKit.GenCardSpec { gen = gen, mood = PortraitMood.Win, sub = UIKit.LvTag(gen), bar = e.pct, subAfter = "<color=#a8a194>　可学：" + string.Join("、", open.ToArray()) + "</color>" };
                var r = new Ref<int>();
                yield return UIKit.Choose(gen.name + " · 习得新招", items, r, null, 820, new UIKit.ChooseOpts { card = card });
                // × / Esc：无论本窗口前是否已学过，都不再逐个询问其余武将（已学的招式照常保留）
                if (r.v < 0) { result.v = "later-all"; yield break; }
                if (r.v >= cands.Count) { result.v = learnedAny ? "learned" : "later"; yield break; }
                var cc = cands[r.v];
                if (!Progress.Learn(gen, cc)) { UIKit.Toast("无法习得「" + cc.name + "」"); break; }
                learnedAny = true;
                string kindName = cc.type == "special" ? "必杀" : "计策";
                try { g.Log(gen.name + "习得" + kindName + "「" + cc.name + "」。"); } catch (System.Exception ex) { Debug.LogWarning(ex); }
                Sfx.Play("horn", 0.35f);
                UIKit.Toast(Face(gen) + gen.name + " 习得" + kindName + "「<color=" + Specials.UiColor(cc.color ?? "#f3c969") + ">" + cc.name + "</color>」", 2.2f);
                RefreshAll();
            }
            result.v = learnedAny ? "learned" : "later";
        }

        // 月初：有保留着空格的我方武将时提示一次
        int growthToldAt = -1;
        void GrowthReminder()
        {
            var g = G;
            if (!Progress.enabled || g == null || g.PlayerFaction == null || !g.PlayerFaction.alive) return;
            List<General> list;
            try { list = Progress.Pending(g.player); } catch (System.Exception) { list = new List<General>(); }
            if (list.Count == 0) return;
            int key = g.year * 12 + g.month;
            if (growthToldAt == key) return;
            growthToldAt = key;
            UIKit.Toast(string.Join("、", list.Take(3).Select(x => Face(x) + x.name).ToArray()) + (list.Count > 3 ? " 等 " + list.Count + " 人" : "") + " 可习得新招<color=#a8a194>（城池面板中点选武将）</color>", 3.2f);
        }

        // 战败后我方被俘武将的下落（fates：Conquest.AiDecideCaptives 的结果；winner：处置俘虏的势力）
        IEnumerator ReportCaptives(List<CaptiveFate> fates, int winner)
        {
            var mine = (fates ?? new List<CaptiveFate>()).Where(x => x.from == G.player).ToList();
            if (mine.Count == 0 || winner < 0) yield break;
            var wn = G.factions[winner].name;
            var lines = mine.Select(x =>
            {
                var gen = x.gen;
                if (x.fate == CaptiveFate.Hired) { G.Log(gen.name + "被俘，归降了" + wn + "。"); return gen.name + "被俘，归降了" + wn + "。"; }
                if (x.fate == CaptiveFate.Executed) return gen.name + "被俘，遭" + wn + "处斩。";
                return gen.faction == G.player ? gen.name + "被俘后获释，回到了" + G.cities[gen.city].name + "。" : gen.name + "被俘后获释，流落在野。";
            }).ToArray();
            yield return UIKit.Say(Faces(string.Join("\n", lines), new FacesOpts { gens = mine.Select(x => x.gen).ToList(), max = 12 }));
        }

        IEnumerator HandleCaptives(BattleSetup s, bool playerDecides, Ref<List<CaptiveFate>> fates)
        {
            if (!playerDecides) { fates.v = Conquest.AiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); yield break; }
            // 玩家只在获胜后处置俘虏，胜方必然据有 s.target（攻方刚攻下 / 守方守住），降将应编入该城
            var holderCity = s.target;
            var recruiter = G.Ruler(G.player);
            foreach (var cap in s.captives.ToList())
            {
                // 被俘时的身份（攻下其最后一城时势力已灭、faction 已是 −1：仍按原君主 / 原属势力显示与处置）
                var info = Conquest.CaptiveInfoOf(s, cap);
                bool isRuler = info != null ? info.wasRuler : cap.faction >= 0 && G.factions[cap.faction].ruler == cap.id;
                var lostF = cap.faction < 0 && info != null && info.faction >= 0 ? G.factions[info.faction] : null;
                string sub = lostF != null ? "<color=#a8a194>原属</color><color=" + Lifted(lostF.Col) + ">" + lostF.name + "</color>" + (isRuler ? "·君主" : "") + "<color=#a8a194>（已灭）</color>" : GenSub(cap);
                var r = new Ref<int>();
                yield return UIKit.Choose("俘虏：" + cap.name + (isRuler ? "（君主）" : ""), new List<UIKit.Item>
                {
                    new UIKit.Item("登用", isRuler ? "君主不会投降" : "成功率约 " + Pct(Commands.HireChance(cap, recruiter, G.player)), !isRuler),
                    new UIKit.Item("释放", "放其归去"),
                    new UIKit.Item("处斩", "以绝后患"),
                }, r, null, 640, new UIKit.ChooseOpts { card = new UIKit.GenCardSpec { gen = cap, mood = PortraitMood.Hurt, sub = sub + (cap.faction >= 0 && !isRuler ? "　忠诚 " + cap.loyalty : "") } });
                if (r.v == 0)
                {
                    if (Commands.Hire(cap, recruiter, G.player, holderCity.id)) yield return UIKit.Say(cap.name + "：败军之将，承蒙不弃，愿降！", cap, new UIKit.SayOpts { mood = PortraitMood.Win, sub = GenSub(cap) }, UIKit.Cinnabar);
                    else { yield return UIKit.Say(cap.name + "：忠臣不事二主！", cap, new UIKit.SayOpts { mood = PortraitMood.Angry, sub = sub }, GreySpeaker); Conquest.Release(cap); }
                }
                else if (r.v == 2) { Conquest.Execute(cap); yield return UIKit.Say(cap.name + "被处斩了。", cap, new UIKit.SayOpts { mood = PortraitMood.Hurt }, GreySpeaker); }
                else Conquest.Release(cap);
            }
        }

        // ---------------------------------------------------------- 武将详情 --
        // 大头像、能力、所属与所在；第三版：等级块（Lv、经验条、战绩、老练、「习得新招」）、全部必杀（专属在前）、计策（原四计 + 习得）；
        // 世界武将另有全名 / 原名 / 身份 / 生卒 / 介绍（WorldScenarioData.InfoOfGeneral）。
        // 「习得新招」：关闭详情 → 习得对话框 → 重新打开详情（显示新学的招式）
        void ShowGeneral(General gen)
        {
            if (gen == null || UIKit.AnyModal || genInfoOpen) return;
            Sfx.Click();
            genInfoOpen = true;
            StartCoroutine(UIKit.Guard(GeneralLoop(gen), e => { Debug.LogException(e); genInfoOpen = false; }));
        }
        IEnumerator GeneralLoop(General gen)
        {
            for (int k = 0; k < 6; k++)
            {
                var r = new Ref<string>();
                yield return GeneralInfo(gen, r);
                if (r.v != "learn") break;
                var lr = new Ref<string>();
                yield return LearnDialog(gen, lr);
                RefreshAll();
            }
            genInfoOpen = false;
        }

        IEnumerator GeneralInfo(General gen, Ref<string> result)
        {
            result.v = null;
            var W = WorldScenarioData.InfoOfGeneral(gen.name);
            var f = gen.faction >= 0 ? G.factions[gen.faction] : null;
            var city = gen.city >= 0 && gen.city < G.cities.Count ? G.cities[gen.city] : null;
            bool isRuler = G.IsRuler(gen);
            const float Wd = 940, pad = 30, sideW = 210, gap = 24;
            var m = UIKit.OpenModal(Wd, 600);
            float w = m.panel.sizeDelta.x;
            bool done = false;
            var bb = m.blocker.gameObject.AddComponent<Button>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(() => done = true);
            m.panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // 点在对话框内不关闭
            // 标题：君 / 守 标记、姓名、势力（提亮的势力色）与所在城
            string tag = isRuler ? UIText.Tag(TagStyle.Ruler, "君") : city != null && city.governor == gen.id ? UIText.Tag(TagStyle.Gov, "守") : "";
            var head = UIKit.Label(m.panel, tag + gen.name + "  " + UIKit.Sz(22) + (f != null ? "<color=" + Readable(f.Col) + ">" + f.name + "</color>" : "<color=#a8a194>在野</color>") + (city != null ? "<color=#a8a194>　" + city.name + "</color>" : "") + "</size>", 32, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(pad, -18), new Vector2(w - 120, 48));
            var close = UIKit.Button(m.panel, "×", () => done = true, 24);
            UIKit.Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            ScrollRect sr;
            var body = UIKit.Scroll(m.panel, out sr);
            body.GetComponent<VerticalLayoutGroup>().enabled = false;
            body.GetComponent<ContentSizeFitter>().enabled = false;
            // 左栏：头像与能力条
            var pim = UIKit.PortraitImg(body, gen, sideW);
            UIKit.Place(pim.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -4), new Vector2(sideW, sideW));
            float sy = sideW + 16;
            var stats = new[] { new KeyValuePair<string, int>("武力", gen.war), new KeyValuePair<string, int>("智力", gen.intel), new KeyValuePair<string, int>("政治", gen.pol) };
            var cols = new[] { new Color32(0xff, 0x8c, 0x73, 255), new Color32(0x73, 0xbf, 0xff, 255), new Color32(0x73, 0xd9, 0x8c, 255) };
            for (int i = 0; i < 3; i++)
            {
                var k = UIKit.Label(body, stats[i].Key, 20, UIKit.Muted, TextAnchor.MiddleLeft);
                UIKit.Place(k.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -sy), new Vector2(50, 28));
                var v = UIKit.Label(body, "<b>" + stats[i].Value + "</b>", 20, UIKit.Text, TextAnchor.MiddleRight);
                UIKit.Place(v.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, -sy), new Vector2(44, 28));
                var bar = UIKit.Bar(body, Mathf.Clamp01(stats[i].Value / 100f), cols[i]);
                UIKit.Place(bar, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -sy - 9), new Vector2(sideW - 104, 10));
                sy += 34;
            }
            // 右栏：兵 / 训练 / 忠诚 / 生年、等级块、必杀、计策、史实资料
            float mx = sideW + gap, mw = w - 2 * pad - mx, y = 2;
            var chips = new List<string>();
            System.Func<string, string, string> chip = (k, v) => "<color=#a8a194>" + k + "</color> <b>" + v + "</b>";
            if (f != null)
            {
                chips.Add(chip("兵", gen.troops.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
                chips.Add(chip("训练", gen.training.ToString()));
                chips.Add(chip("忠诚", (isRuler ? 100 : gen.loyalty).ToString()));
            }
            if (gen.born > 0) chips.Add(chip("生年", gen.born.Value.ToString()) + (G.year >= gen.born.Value ? " <color=#a8a194>" + (G.year - gen.born.Value) + "岁</color>" : ""));
            if (chips.Count > 0) y += UIKit.FlowText(body, string.Join("　　", chips.ToArray()), 21, UIKit.Text, mx, y, mw) + 14;
            // 等级、经验、战绩（第三版成长）
            if (Progress.enabled)
            {
                try { y += GenInfoLevel(body, gen, f != null && gen.faction == G.player, () => { result.v = "learn"; done = true; }, mx, y, mw) + 14; }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            // 必杀：专属在前，其后为习得的可习得必杀
            try { y += GenInfoSpecials(body, gen, mx, y, mw); } catch (System.Exception e) { Debug.LogException(e); }
            // 计策：原四计（按智力）+ 习得的计策
            try { y += GenInfoTactics(body, gen, mx, y, mw); } catch (System.Exception e) { Debug.LogException(e); }
            if (W != null)
            {
                string life = (W.Born != null ? W.Born.Value.ToString() : "?") + "—" + (W.Died != null ? W.Died.Value.ToString() : "?") + "年";
                y += UIKit.FlowText(body, "<b>" + (string.IsNullOrEmpty(W.Full) ? gen.name : W.Full) + "</b>" + (!string.IsNullOrEmpty(W.Orig) ? "　" + UIKit.Sz(20) + "<i><color=#a8a194>" + W.Orig + "</color></i></size>" : ""), 22, UIKit.Text, mx, y, mw) + 6;
                y += UIKit.FlowText(body, "<color=#f3c969>" + (W.Role ?? "") + "</color>" + (W.Born != null || W.Died != null ? "<color=#a8a194>　" + life + "</color>" : ""), 20, UIKit.Text, mx, y, mw) + 6;
                if (!string.IsNullOrEmpty(W.Note)) y += UIKit.FlowText(body, W.Note, 21, new Color(0.96f, 0.93f, 0.86f, 0.92f), mx, y, mw) + 6;
                if (!string.IsNullOrEmpty(W.Liberty)) y += Game.LibertyBox(body, W.Liberty, mx, y, mw) + 6;
            }
            float bodyH = Mathf.Max(sy, y) + 8;
            body.anchorMin = new Vector2(0, 1); body.anchorMax = new Vector2(1, 1); body.pivot = new Vector2(0.5f, 1);
            body.sizeDelta = new Vector2(0, bodyH);
            const float headH = 80;
            float h = Mathf.Min(UIKit.MaxModalHeight, headH + bodyH + 26);
            m.panel.sizeDelta = new Vector2(w, h);
            UIKit.Stretch((RectTransform)sr.transform, pad, 22, pad, headH);
            yield return null;
            while (!done)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) done = true;
                yield return null;
            }
            Sfx.Click();
            m.Close();
        }

        // 详情：等级块（Lv、经验条、距下一级、战绩、老练；我方武将有空格时「习得新招」按钮）→ 高度
        float GenInfoLevel(RectTransform parent, General gen, bool mine, System.Action onLearn, float x, float y, float w)
        {
            var e = Progress.ExpInfo(gen);
            var box = UIKit.Img(parent, UIKit.RR, new Color(0.953f, 0.788f, 0.412f, 0.07f), "Level");
            box.raycastTarget = false;
            var ol = UIKit.Img(box.transform, UIKit.RROutline, new Color(0.953f, 0.788f, 0.412f, 0.2f), "Outline"); UIKit.Stretch(ol.rectTransform); ol.raycastTarget = false;
            var rb = box.rectTransform;
            UIKit.Place(rb, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, 10));
            const float px = 15;
            float iy = 10;
            // Lv（标题字体、金色；数字大一号）+ 经验条 + 经验
            var lv = UIKit.Label(rb, "Lv<color=#fff1c8><b><size=" + UIKit.Fs(32) + ">" + e.level + "</size></b></color>", 23, UIKit.Gold, TextAnchor.MiddleLeft, true, "Lv");
            lv.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Place(lv.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(px, -iy), new Vector2(84, 40));
            var bar = UIKit.ExpBar(rb, e.pct, 180, 12);
            bar.anchoredPosition = new Vector2(px + 84, -iy - 20);
            string need = e.max ? "已满级" : "经验 " + e.exp + " / " + e.next + "<color=#a8a194>（距 " + (e.level + 1) + " 级 " + e.need + "）</color>";
            var nl = UIKit.Label(rb, need, 19, UIKit.Text, TextAnchor.MiddleLeft, false, "Exp");
            UIKit.Place(nl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(px + 84 + 196, -iy), new Vector2(Mathf.Max(60, w - 2 * px - 84 - 196), 40));
            iy += 44;
            var rec = gen.record ?? new GenRecord();
            if (rec.battles > 0)
                iy += UIKit.FlowText(rb, string.Format("<color=#a8a194>出阵</color> <b>{0}</b>　<color=#a8a194>胜</color> <b>{1}</b>　<color=#a8a194>击溃</color> <b>{2}</b>　<color=#a8a194>单挑胜</color> <b>{3}</b>", rec.battles, rec.wins, rec.routs, rec.duels), 19, new Color(1f, 0.98f, 0.94f), px, iy, w - 2 * px) + 2;
            // 老练：每高 1 级，战场上攻击、防御与计策威力 +4%（Progress.VetK）
            int per = Mathf.RoundToInt((float)(Progress.VetPerLevel * 100));
            double vk = Progress.VetK(gen);
            if (per > 0)
                iy += UIKit.FlowText(rb, vk > 1.0001 ? "<color=#a8a194>老练　战场上攻击、防御与计策威力</color> <color=#f3c969><b>+" + Mathf.RoundToInt((float)((vk - 1) * 100)) + "%</b></color><color=#a8a194>（每级 +" + per + "%）</color>"
                    : "<color=#a8a194>老练　每升一级，战场上攻击、防御与计策威力 +" + per + "%</color>", 19, UIKit.Text, px, iy, w - 2 * px) + 2;
            if (mine && GrowthText.CanLearn(gen))
            {
                var op = Progress.OpeningsOf(gen);
                var what = new List<string>();
                if (op.special > 0) what.Add("必杀 " + op.special);
                if (op.tactic > 0) what.Add("计策 " + op.tactic);
                var b = UIKit.Button(rb, "习得新招 " + UIKit.Sz(19) + "<color=#ffe9c2>" + string.Join("、", what.ToArray()) + "</color></size>", () => onLearn(), 24, true, "Learn");
                float bw = Mathf.Max(200, b.label.preferredWidth + 40);
                UIKit.Place(b.rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(px, -iy - 4), new Vector2(bw, Mathf.Max(50, UIKit.MinTouch)));
                iy += Mathf.Max(50, UIKit.MinTouch) + 8;
            }
            float h = iy + 8;
            rb.sizeDelta = new Vector2(w, h);
            return h;
        }

        // 区块标题：「必杀」/「计策」标签 + 灰色小字（招数 · 格数说明）→ 高度
        static float SectionHead(RectTransform parent, string label, Color labelCol, string text, float x, float y, float w)
        {
            // 标签宽高随字（.sg-geninfo-sp-label：.85rem、内边距 .05rem .4rem、字距 .1em、不折行），字号下限抬高时也放得下；
            // 其后间隔 .6rem 接灰色小字
            var badge = UIKit.Img(parent, UIKit.RR, labelCol, "Badge");
            badge.raycastTarget = false;
            var bl = UIKit.Label(badge.transform, label, 17, new Color32(0xff, 0xf2, 0xe0, 255), TextAnchor.MiddleCenter);
            bl.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Stretch(bl.rectTransform);
            float bw = Mathf.Ceil(bl.preferredWidth + 0.1f * bl.fontSize * label.Length + 16), bh = Mathf.Max(24, Mathf.Ceil(bl.preferredHeight) + 2);
            UIKit.Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - 2), new Vector2(bw, bh));
            float th = UIKit.FlowText(parent, text, 18, UIKit.Muted, x + bw + 12, y + 1, w - bw - 12, false, "SecHead");
            return Mathf.Max(bh + 4, th) + 8;
        }

        // 详情：必杀列表（每招：等阶 / 专属、名称、机制、说明、数值摘要）→ 高度
        float GenInfoSpecials(RectTransform parent, General gen, float x, float y, float w)
        {
            List<Special> list;
            try { list = Specials.Known(gen); } catch (System.Exception e) { Debug.LogWarning(e); list = new List<Special>(); }
            if (list.Count == 0) return 0;
            float y0 = y;
            string slot = Progress.enabled ? GrowthText.SlotText(gen, "special") : "";
            y += SectionHead(parent, "必杀", UIKit.Cinnabar, list.Count + " 招" + (slot.Length > 0 ? " · " + slot : ""), x, y, w);
            foreach (var sp in list) y += SpecialBox(parent, sp, x, y, w) + 8;
            return y - y0 + 6;
        }

        // 详情：计策列表（原四计按智力自带；习得的计策带等阶、典故与说明；两列）→ 高度
        float GenInfoTactics(RectTransform parent, General gen, float x, float y, float w)
        {
            List<TacticDef> list;
            try { list = Tactics.Known(gen); } catch (System.Exception e) { Debug.LogWarning(e); list = new List<TacticDef>(); }
            float y0 = y;
            string slot = Progress.enabled ? GrowthText.SlotText(gen, "tactic") : "";
            y += SectionHead(parent, "计策", new Color32(0x3f, 0x6f, 0xb0, 255), (list.Count > 0 ? list.Count + " 计" : "未习计策") + (slot.Length > 0 ? " · " + slot : ""), x, y, w);
            if (list.Count == 0) return y - y0 + 6;
            // 网格：每格至少 15rem（300），放得下两列就两列
            int ncol = w >= 2 * 300 + 8 ? 2 : 1;
            float cw = (w - (ncol - 1) * 8) / ncol;
            float rowTop = y, rowH = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                int c = i % ncol;
                if (c == 0 && i > 0) { rowTop += rowH + 8; rowH = 0; }
                bool isNew = Tactics.IsNew(t);
                var box = UIKit.Img(parent, UIKit.RR, isNew ? new Color(0.31f, 0.51f, 0.86f, 0.07f) : new Color(1, 1, 1, 0.035f), "Tactic");
                box.raycastTarget = false;
                var ol = UIKit.Img(box.transform, UIKit.RROutline, isNew ? new Color(0.47f, 0.67f, 1f, 0.3f) : new Color(1, 1, 1, 0.06f), "Outline"); UIKit.Stretch(ol.rectTransform); ol.raycastTarget = false;
                var rb = box.rectTransform;
                UIKit.Place(rb, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + c * (cw + 8), -rowTop), new Vector2(cw, 10));
                const float px = 13;
                float iy = 8;
                string tier = Tactics.TierNameOf(t.Tier);
                iy += UIKit.FlowText(rb, "<b><color=" + Specials.UiColor(t.Color ?? "#f3c969") + ">" + t.Name + "</color></b>  " + UIKit.Sz(16) + "<color=#a8a194>" + (isNew ? tier : "自带") + (t.ReqInt > 0 ? " · 智" + t.ReqInt : "") + "</color></size>", 22, UIKit.Text, px, iy, cw - 2 * px) + 2;
                string d = isNew ? GrowthText.StripLore(t.Desc, t.Lore) : (t.Desc ?? "");
                if (isNew && !string.IsNullOrEmpty(t.Lore)) iy += UIKit.FlowText(rb, "<i>" + t.Lore + "</i>", 18, new Color(0.96f, 0.93f, 0.86f, 0.82f), px, iy, cw - 2 * px) + 2;
                if (d.Length > 0) iy += UIKit.FlowText(rb, d, 18, new Color32(0xdd, 0xd3, 0xbf, 255), px, iy, cw - 2 * px) + 2;
                float bh = iy + 8;
                rb.sizeDelta = new Vector2(cw, bh);
                rowH = Mathf.Max(rowH, bh);
            }
            y = rowTop + rowH;
            return y - y0 + 10;
        }

        // 必杀技说明框：等阶（专属为朱红、可习得为淡金）、技名（技色）与类别、说明、规则
        static float SpecialBox(RectTransform parent, Special sp, float x, float y, float w)
        {
            var box = UIKit.Img(parent, UIKit.RR, new Color(1, 1, 1, sp.pool ? 0.03f : 0.04f), "Special");
            box.raycastTarget = false;
            UIKit.Place(box.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, 10));
            var ol = UIKit.Img(box.transform, UIKit.RROutline, new Color(0.95f, 0.79f, 0.42f, sp.pool ? 0.14f : 0.25f), "Outline");
            UIKit.Stretch(ol.rectTransform); ol.raycastTarget = false;
            var rbox = box.rectTransform;
            const float px = 15;
            float iy = 11;
            string col = Specials.UiColor(sp);
            string kind = Specials.KindName(sp.kind);
            string tier = sp.pool ? UIText.Tag(TagStyle.SpTier, Specials.TierNameOf(sp.tier)) : UIText.Tag(TagStyle.SpSig, "专属");
            iy += UIKit.FlowText(rbox, tier + "<color=" + col + ">" + sp.name + "</color>" + (!string.IsNullOrEmpty(kind) ? "　" + UIKit.Sz(15) + "<color=#a8a194>" + kind + "</color></size>" : ""), 25, UIKit.Text, px, iy, w - 2 * px, true) + 4;
            if (!string.IsNullOrEmpty(sp.desc)) iy += UIKit.FlowText(rbox, sp.desc, 21, UIKit.Text, px, iy, w - 2 * px) + 4;
            string rules = "";
            try { rules = Specials.Rules(sp); } catch (System.Exception e) { Debug.LogWarning(e); }
            if (!string.IsNullOrEmpty(rules)) iy += UIKit.FlowText(rbox, rules, 18, UIKit.Muted, px, iy, w - 2 * px) + 4;
            float h = iy + 9;
            rbox.sizeDelta = new Vector2(w, h);
            return h;
        }

        // ---------------------------------------------------------- 情报 --
        IEnumerator FactionInfo()
        {
            var fs = G.factions.Where(f => f.alive).OrderByDescending(f => G.CityCount(f.id)).ToList();
            bool world = RegionsOf() != null;
            var grp = GroupedFactions(fs, f => new UIKit.Item(
                Swatch(f) + " " + G.Ruler(f.id).name + (world && f.name != G.Ruler(f.id).name ? "  " + Small(f.name, 19) : "") + (f.id == G.player ? "（我方）" : G.Allied(f.id, G.player) ? "（同盟）" : ""),
                string.Format("城 {0}　将 {1}　兵 {2}", G.CityCount(f.id), G.GeneralsOf(f.id).Count(), G.CitiesOf(f.id).Sum(c => G.TroopsIn(c))), true, null, G.Ruler(f.id)));
            var r = new Ref<int>();
            yield return UIKit.Choose("天下势力", grp.items, r, world ? "按地域分组（我方所在地域在前，其余由近及远）；点选势力，镜头飞往其都城。" : null, 760);
            if (r.v < 0) yield break;
            var f0 = grp.pick[r.v];
            if (f0 == null) yield break;
            var cap = G.cities[G.Ruler(f0.id).city];
            Game.I.Rig.Focus(new Vector3(cap.MapPos.x, 0, cap.MapPos.y), 45);
            SelectCity(cap.id);
        }
    }
}
