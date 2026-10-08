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
    public class StrategyScreen : MonoBehaviour
    {
        static GameState G { get { return GameState.Current; } }
        public const float RowPortrait = 44;           // 城池面板武将行的头像尺寸（画布单位）
        const float LabelHideBeyond = 170;             // 经典地图：镜头距离超过即隐藏城名
        const float LabelHideBeyondWorld = 420;        // 世界地图可拉远到 340：远景也显示（避让后剩下的）城名
        const float LabelFarCompact = 175;             // 世界地图镜头距离超过约 175 时城名改为紧凑样式
        const float LabelFade = 0.18f;                 // 城名标签避让时的淡入淡出时长（秒）
        static readonly Color GreySpeaker = new Color(0.4f, 0.4f, 0.45f);

        RectTransform hud, topBar, cityPanel, cmdGrid, genList, genScroll;
        Text topLeft, topCenter, cityTitle, cityStats;
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
        // 深色势力色作为文字时提亮，保证在漆黑面板上可读（标记 ■● 仍用原色）
        static string Readable(Color c)
        {
            float L = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (L >= 0.5f) return Hex(c);
            float k = Mathf.Clamp((0.5f - L) * 1.3f, 0, 0.6f);
            return Hex(new Color(c.r + (1 - c.r) * k, c.g + (1 - c.g) * k, c.b + (1 - c.b) * k));
        }
        static string Swatch(Faction f) { return "<color=" + Hex(f.Col) + ">■</color>"; }
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
                if (news.Count > 0) yield return UIKit.Say(PickNews(news));
                try { G.Save(); } catch (System.Exception e) { Debug.LogWarning(e); }
                UIKit.Banner(G.year + "年 " + G.month + "月", "令牌 " + G.tokens + " 枚", 1.8f);
                // 上月取得新城时「移动」还不能用（武将都已行动 / 令牌用完）：月初横幅之后补弹提示
                if (tipPending && MoveUsableCity() != null) yield return new WaitForSeconds(1.2f);
                busy = false;
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

        IEnumerator GameOver(bool won)
        {
            busy = true;
            RefreshAll();
            if (won)
            {
                Sfx.Play("win");
                UIKit.Banner("天下统一", G.Ruler(G.player).name + "终成霸业", 4f);
                yield return new WaitForSeconds(2.5f);
                yield return UIKit.Say(string.Format("{0}年{1}月，{2}平定四海，一统天下。\n麾下武将 {3} 员，历时 {4} 年。", G.year, G.month, G.Ruler(G.player).name, G.GeneralsOf(G.player).Count(), G.year - G.Scen.StartYear));
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
            UIKit.ScaleChanged -= LayoutCityPanel;
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
            UIKit.Stretch(topCenter.rectTransform, 300, 0, 300, 0);
            var row = UIKit.HList(top, 10);
            row.anchorMin = new Vector2(1, 0); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(1, 0.5f);
            row.offsetMin = new Vector2(-560, 12); row.offsetMax = new Vector2(-14, -12);
            UIKit.Button(row, "势力", () => Do(FactionInfo()), 24);
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
            UIKit.Place(cityStats.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -74), new Vector2(460, 170));
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
            UIKit.ScaleChanged += LayoutCityPanel;
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
                var cg = bg.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
                cg.alpha = 0;                // 首次避让计算后才显示，避免第一帧城名叠在一起
                labels[cv.city.id] = new CityLabel { rt = bg.rectTransform, text = t, cg = cg, cv = cv };
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
            genScroll.offsetMin = new Vector2(20, 18 + gridH + 12); genScroll.offsetMax = new Vector2(-20, -250);
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
            cityTitle.text = c.name + "  " + UIKit.Sz(24) + (owner != null ? "<color=" + Readable(owner.Col) + ">" + owner.name + "</color>" : "<color=#a8a194>空城</color>") + "</size>";
            var gov = c.governor >= 0 ? G.generals[c.governor].name : "—";
            var free = G.FreeFoundIn(c.id).ToList();
            cityStats.text = string.Format("太守 <b>{0}</b>　　人口 {1:N0}\n土地 <b>{2}</b>　产业 <b>{3}</b>　町 <b>{4}</b>\n金 <b>{5}</b>　粮 <b>{6}</b>　防御 {7}\n兵力 <b>{8}</b>　武将 {9} 人{10}",
                gov, c.population, c.land, c.industry, c.town, c.gold, c.food, c.Defense, G.TroopsIn(c), G.OfficersIn(c).Count(),
                free.Count > 0 && mine ? "　<color=#73d98c>在野 " + free.Count + " 人</color>" : "");
            foreach (Transform t in genList) Destroy(t.gameObject);
            // 武将列表（君主优先，其次兵力）；点按武将行 → 武将详情
            foreach (var g in G.OfficersIn(c).OrderByDescending(x => G.IsRuler(x)).ThenByDescending(x => x.troops))
            {
                var gen = g;
                // 底色 rgba(255,255,255,.05)（由按钮的 normalColor 着色）；按下时淡金 rgba(243,201,105,.14)
                var row = UIKit.Img(genList, UIKit.RR, Color.white, "Gen");
                UIKit.Size(row, -1, Mathf.Max(54, UIKit.MinTouch));
                var btn = row.gameObject.AddComponent<Button>();
                var cb = btn.colors;
                cb.normalColor = new Color(1, 1, 1, 0.05f); cb.highlightedColor = new Color(1, 1, 1, 0.09f); cb.pressedColor = new Color(0.953f, 0.788f, 0.412f, 0.14f);
                cb.selectedColor = cb.normalColor; cb.disabledColor = cb.normalColor; cb.colorMultiplier = 1; cb.fadeDuration = 0.08f;
                btn.colors = cb;
                btn.targetGraphic = row;
                btn.onClick.AddListener(() => ShowGeneral(gen));
                var pim = UIKit.PortraitImg(row.transform, gen, RowPortrait);
                UIKit.Place(pim.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(RowPortrait, RowPortrait));
                var tag = G.IsRuler(gen) ? "<color=#ff9a7a>君</color> " : gen.id == c.governor ? "<color=#f3c969>守</color> " : "";
                var l = UIKit.Label(row.transform, tag + "<b>" + gen.name + "</b>" + (gen.moved ? " " + Small("已行动", 15) : ""), 22, UIKit.Text, TextAnchor.MiddleLeft);
                UIKit.Stretch(l.rectTransform, 6 + RowPortrait + 10, 0, 220, 0);
                var r = UIKit.Label(row.transform, string.Format("武{0} 智{1} 政{2}\n兵{3} 训{4} 忠{5}", gen.war, gen.intel, gen.pol, gen.troops, gen.training, G.IsRuler(gen) ? 100 : gen.loyalty), 15, UIKit.Muted, TextAnchor.MiddleRight);
                UIKit.Stretch(r.rectTransform, 160, 0, 12, 0);
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
            AddCmd("任命", busyWhy, () => Do(CmdAppoint(c)));
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
            else yield return UIKit.Say(target.name + "：在下另有志向，请回吧。", target.name, GreySpeaker);
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
            yield return UIKit.ChooseMany("调动武将至" + dest.name + "（" + RouteText(route) + "）", gens.Select(g => new UIKit.Item(g.name, "兵 " + g.troops)).ToList(), 10, sel, "可一次调动多名武将（消耗 1 枚令牌）");
            if (sel.v == null || sel.v.Count == 0) yield break;
            UseToken();
            yield return MarchRoute(route.path, G.PlayerFaction.Col);
            int n = 0;
            foreach (var i in sel.v) { Commands.Move(gens[i], dest); if (gens[i].city == dest.id) n++; }
            UIKit.Toast(n + " 名武将移驻" + dest.name);
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
                var grp = GroupedFactions(fs, f => new UIKit.Item(G.Ruler(f.id).name + (world && f.name != G.Ruler(f.id).name ? "  " + Small(f.name, 19) : ""),
                    G.Allied(f.id, G.player) ? "已同盟" : "城 " + G.CityCount(f.id) + "　成功率约 " + Commands.AllyChance(G.player, f.id, Balance.AllianceGift).ToString("P0"),
                    !G.Allied(f.id, G.player)));
                var fi = new Ref<int>();
                yield return UIKit.Choose("与谁结盟？", grp.items, fi);
                var ff = fi.v >= 0 ? grp.pick[fi.v] : null;
                if (ff == null) yield break;
                var gift = new Ref<int>();
                yield return UIKit.PickNumber("赠送礼金", 0, c.gold, 50, Mathf.Min(c.gold, Balance.AllianceGift), v => "成功率约 " + Commands.AllyChance(G.player, ff.id, v).ToString("P0"), gift);
                if (gift.v < 0) yield break;
                UseToken();
                yield return UIKit.Say(Commands.Ally(c, G.player, ff.id, gift.v));
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
            yield return UIKit.ChooseMany("出征武将（至多 " + Balance.MaxSortieGenerals + " 名）", gens.Select(g => new UIKit.Item(g.name, string.Format("兵{0} 武{1} 智{2} 训{3}", g.troops, g.war, g.intel, g.training))).ToList(), Balance.MaxSortieGenerals, sel, "君主出征时由君主任主将，否则为列表中最靠前的武将；主将败走则全军撤退。");
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
                Game.I.Map.Refresh(G); RefreshAll();
                var fr = new Ref<List<CaptiveFate>>();
                if (s.captives.Count > 0) yield return HandleCaptives(s, playerSide == 0, fr);
                Game.I.Map.Refresh(G); RefreshAll();
                yield return UIKit.Say(s.summary);
                yield return ReportCaptives(fr.v, s.attackerWon ? s.attacker : s.defender);
                yield break;
            }
            var mode = new Ref<int>();
            yield return UIKit.Choose(s.target.name + "之战", new List<UIKit.Item> { new UIKit.Item("亲自指挥", "在战场上调兵遣将"), new UIKit.Item("委任", "由部将自行作战，立即得出结果") }, mode, null, 560);
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
            yield return UIKit.Say(res);
            yield return ReportCaptives(fates, playerSide == 0 ? s.defender : s.attacker);
            if (s.attackerWon && s.defender >= 0 && !G.factions[s.defender].alive) yield return UIKit.Say(G.factions[s.defender].name + "势力就此灭亡。");
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
            yield return UIKit.Say(string.Join("\n", lines));
        }

        IEnumerator HandleCaptives(BattleSetup s, bool playerDecides, Ref<List<CaptiveFate>> fates)
        {
            if (!playerDecides) { fates.v = Conquest.AiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); yield break; }
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
                    else { yield return UIKit.Say(cap.name + "：忠臣不事二主！", cap.name, GreySpeaker); Conquest.Release(cap); }
                }
                else if (r.v == 2) { Conquest.Execute(cap); yield return UIKit.Say(cap.name + "被处斩了。"); }
                else Conquest.Release(cap);
            }
        }

        // ---------------------------------------------------------- 武将详情 --
        // 大头像、能力、所属与所在、必杀技（Specials.Of）；世界武将另有全名 / 原名 / 身份 / 生卒 / 介绍（WorldScenarioData.InfoOfGeneral）
        void ShowGeneral(General gen)
        {
            if (gen == null || UIKit.AnyModal || genInfoOpen) return;
            Sfx.Click();
            genInfoOpen = true;
            StartCoroutine(UIKit.Guard(GeneralInfo(gen), e => { Debug.LogException(e); genInfoOpen = false; }));
        }

        IEnumerator GeneralInfo(General gen)
        {
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
            string tag = isRuler ? "<color=#ff9a7a>君</color> " : city != null && city.governor == gen.id ? "<color=#f3c969>守</color> " : "";
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
            // 右栏：兵 / 训练 / 忠诚 / 生年、必杀技、史实资料
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
            Special sp = null;
            try { sp = Specials.Of(gen); } catch (System.Exception e) { Debug.LogWarning(e); sp = null; }
            if (sp != null) y += SpecialBox(body, sp, mx, y, mw) + 14;
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
            genInfoOpen = false;
        }

        // 必杀技说明框：「必杀」标签、技名（技色）与类别、说明、规则
        static float SpecialBox(RectTransform parent, Special sp, float x, float y, float w)
        {
            var box = UIKit.Img(parent, UIKit.RR, new Color(1, 1, 1, 0.04f), "Special");
            box.raycastTarget = false;
            UIKit.Place(box.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, 10));
            var ol = UIKit.Img(box.transform, UIKit.RROutline, new Color(0.95f, 0.79f, 0.42f, 0.25f), "Outline");
            UIKit.Stretch(ol.rectTransform); ol.raycastTarget = false;
            var rbox = box.rectTransform;
            const float px = 15;
            float iy = 11;
            var badge = UIKit.Img(rbox, UIKit.RR, UIKit.Cinnabar, "Badge");
            UIKit.Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(px, -iy - 4), new Vector2(54, 26));
            var bl = UIKit.Label(badge.transform, "必杀", 15, new Color32(0xff, 0xf2, 0xe0, 255), TextAnchor.MiddleCenter);
            UIKit.Stretch(bl.rectTransform);
            string col = Specials.UiColor(sp);
            string kind = Specials.KindName(sp.kind);
            iy += UIKit.FlowText(rbox, "<color=" + col + ">" + sp.name + "</color>" + (!string.IsNullOrEmpty(kind) ? "　" + UIKit.Sz(15) + "<color=#a8a194>" + kind + "</color></size>" : ""), 25, UIKit.Text, px + 64, iy, w - 2 * px - 64, true) + 4;
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
                string.Format("城 {0}　将 {1}　兵 {2}", G.CityCount(f.id), G.GeneralsOf(f.id).Count(), G.CitiesOf(f.id).Sum(c => G.TroopsIn(c)))));
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
