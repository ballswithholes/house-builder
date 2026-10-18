using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sanguo
{
    // 战斗流程：玩家操作、电脑行动、动画与界面
    // 第二版（网页版 battle-controller.js 的移植）：
    //   攻击 → 攻击画面 Clash.Play（开 / 快 / 关；电脑行动时 ×1.5，委任时 ×1.8，再乘「快」的 ×2）
    //   必杀：行动菜单「策略」之后一项（招式名；不可用时灰显并注明原因）→ 选目标 → 特写 Clash.CutIn → 结算与特效（PerformSpecial，
    //     即网页版 Specials.perform）；电脑 PlanAction 的 kind "special"
    //   单挑：玩家一方参与且未委任时进入格斗画面 DuelGame.Play → BattleModel.DuelFinish；电脑对电脑 / 委任时照旧 Duel() + 单挑对话框；
    //     格斗画面出错时退回简易单挑 + 旧对话框
    //   「动画」按钮与 V 键切换攻击画面模式；战斗音乐（进攻 battle / 守城 battle-defend，地域取敌方势力文化），胜负短曲后回到地图曲；
    //   竖屏开场镜头；电脑行动后镜头回到我军；部队情报卡显示头像与必杀
    // 第三版（← battle-controller.js v3，DESIGN-V3 §2、§4；规则见 BattleModel / Specials / Tactics / Progress）：
    //   战场 18×13：开场镜头距离 38（触屏 40），最远 72。
    //   开战时预热攻击画面里双方文化的士兵（Clash.Prepare）；开场横幅之后等战场人物模型就绪（BattleView.Ready，最多再等 4 秒）再开始第一回合。
    //   行动菜单「必杀」→ 列出该部队会的全部必杀（专属在前，每招每战一次；已施展 / 无目标的灰显并注明原因）→ 选目标 →
    //     特写（所选招式）+ 结算与特效（PerformSpecial，special = 所选招式）；只会一招时直接选目标。
    //   「策略」→ 列出已会的计策（原四计按智力 + 习得的新计策，Tactics.MenuItems）；新计策经 Tactics.Perform 结算，
    //     播放各自的 BattleView.SpecialFx 风格与飘字（PerformTactic）；电脑的 "special" / "tactic" 计划走同一条路径（plan.special / plan.tactic）。
    //   子菜单（必杀 / 策略 / 目标 / 阵型）取消时回到行动菜单；行动菜单本身取消时与旧版相同（移动过则待机）。
    //   情报卡：姓名旁显示等级（Lv）与经验进度条（Progress.ExpInfo）、老练加成；必杀一行「未施展 / 已施展」，习得的招式另起一行。
    //   战后的经验结算由 BattleModel.SettleProgress 写入 BattleSetup.progress，回到战略画面后由 StrategyScreen.Growth 显示。
    public class BattleController
    {
        BattleModel M; BattleView V; int playerSide; bool autoPlayer, endTurn;
        UIKit.Btn endBtn;
        Vector2? tap;
        RectTransform hud; Text topText, cardText, cardHead, cardSub; RectTransform card, cardBarFill; Image cardPic, cardBarImg;
        UIKit.Btn animBtn; string animShown;
        bool aiMovedCam;
        string battleMusic = "battle";
        public static BattleController Current;     // 测试 / 调试用
        static GameState G { get { return GameState.Current; } }
        static Color SideColor(int s) { return s == 0 ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.5f, 0.42f); }

        public static IEnumerator Run(BattleSetup s, int playerSide)
        {
            var bc = new BattleController { playerSide = playerSide };
            Current = bc;
            try { yield return bc.Main(s); }
            finally { if (Current == bc) Current = null; }
        }

        IEnumerator Main(BattleSetup s)
        {
            M = new BattleModel(s);
            V = BattleView.Create(M);
            var rig = Game.I.Rig;
            var oldBounds = rig.Bounds; var oldTarget = rig.Desired; var oldDist = rig.DesiredDistance; float oldMin = rig.MinDist, oldMax = rig.MaxDist;
            string oldCulture = Sfx.Culture;
            // 布置战场也放进 try：其中任何一步抛出异常时，finally 照样恢复地图、镜头与音乐
            try
            {
                // 攻击画面预热本战双方文化的士兵模板与动作（网页版 SG.Clash.prepare；汉军已在启动后预热，其余文化在开场横幅期间后台分片生成）
                try { if (Clash.Enabled) Clash.Prepare(M.units.Select(u => u.gen != null && !string.IsNullOrEmpty(u.gen.culture) ? u.gen.culture : "han")); }
                catch (Exception e) { Debug.LogWarning(e); }
                Game.I.Map.Root.gameObject.SetActive(false);
                rig.Bounds = new Rect(BattleView.Origin.x, BattleView.Origin.z, M.W * BattleView.T, M.H * BattleView.T);
                rig.MinDist = 12; rig.MaxDist = 72;        // 第三版战场 18×13：可拉得更远
                rig.Focus(V.BoardCenter - new Vector3(0, 0, 2), Game.IsTouch ? 40 : 38, true);
                try { PortraitOpening(rig); } catch (Exception e) { Debug.LogException(e); }
                rig.OnTap += OnTap;
                // 战斗音乐：守城用「孤城」（battle-defend），进攻用「出阵」；地域取敌方势力的文化
                try
                {
                    var cul = CultureOfFoe(s, playerSide);
                    if (!string.IsNullOrEmpty(cul) && cul != Sfx.Culture) Sfx.Culture = cul;
                }
                catch (Exception) { /* 无地域资料 */ }
                battleMusic = playerSide == 1 ? "battle-defend" : "battle";
                Sfx.Music(battleMusic);
                V.StartCoroutine(KeyLoop());
                BuildHud();
                UpdateHud();                       // 开场横幅期间顶栏即显示日数 / 兵力 / 粮草

                string an = G.factions[s.attacker].name, dn = s.defender >= 0 ? G.factions[s.defender].name : "守军";
                UIKit.Banner(s.target.name + "之战", an + "军 进攻 " + dn + "军", 2.2f);
                Sfx.Play("horn");
                yield return new WaitForSeconds(1.6f);
                // 人物模型分片就绪后再开始第一回合（最多再等 4 秒；网页版 Promise.race([V.ready, SG.wait(4)])）
                for (float w = 0; !V.Ready && w < 4f; w += Time.unscaledDeltaTime) yield return null;

                while (M.result == 0)
                {
                    UpdateHud();
                    bool human = !autoPlayer && M.side == playerSide;
                    if (human) yield return PlayerPhase(); else yield return AiPhase();
                    if (M.result != 0) break;
                    V.ClearHighlights(); V.SetCursor(null);
                    bool newDay = M.EndSide();
                    foreach (var u in M.units) V.Refresh(u);
                    foreach (var u in M.units.Where(x => !x.alive && V.IsShown(x)).ToList()) yield return V.Rout(u);
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
                // 胜负乐曲（凯旋 / 残阳）：一次性短曲，回到地图后继续播完再接地图曲；没有音乐时用旧音效
                if (playerSide >= 0 && MusicLive()) Sfx.PlayOneShotMusic(playerWon ? "victory" : "defeat");
                else Sfx.Play(playerSide < 0 ? "horn" : playerWon ? "win" : "lose");
                UIKit.Banner(M.result == 1 ? "攻方胜利" : "守方胜利", M.resultReason, 3f);
                yield return new WaitForSeconds(2.6f);
            }
            finally
            {
                rig.OnTap -= OnTap;
                if (hud != null) UnityEngine.Object.Destroy(hud.gameObject);
                if (V != null) UnityEngine.Object.Destroy(V.gameObject);
                if (Game.I != null && Game.I.Map != null && Game.I.Map.Root != null) Game.I.Map.Root.gameObject.SetActive(true);
                rig.Bounds = oldBounds; rig.MinDist = oldMin; rig.MaxDist = oldMax; rig.Focus(oldTarget, oldDist, true);
                if (!string.IsNullOrEmpty(oldCulture)) Sfx.Culture = oldCulture;
                Sfx.Music("map");   // 胜负短曲未奏完时由 Sfx 暂缓（HOLD_AFTER_ONE_SHOT），曲终再接地图曲
                try { Portrait.ReleaseRetired(); } catch (Exception e) { Debug.LogException(e); }   // 战斗结束时释放被淘汰的大头像
            }
        }

        void OnTap(Vector2 p) { tap = p; }

        // 背景音乐是否在用（音乐开、引擎在播放、有胜负曲）：此时胜负由乐曲表现，不再叠旧的 win / lose 音效
        static bool MusicLive()
        {
            try { return Sfx.MusicOn && Sfx.PlayingMusic != null && Sanguo.Audio.MusicLib.Resolve("victory", Sfx.Culture) != null; }
            catch (Exception) { return false; }
        }
        // 敌方势力的地域文化（无主城：按城市所在地域）；缺少时为 null
        public static string CultureOfFoe(BattleSetup s, int playerSide)
        {
            try
            {
                int foe = playerSide == 1 ? s.attacker : s.defender;
                var g = GameState.Current;
                var f = g != null && foe >= 0 && foe < g.factions.Count ? g.factions[foe] : null;
                if (f != null && !string.IsNullOrEmpty(f.key)) return WorldLookup.CultureOfFaction(f.key);
                var c = s.target;
                if (c != null && !string.IsNullOrEmpty(c.key)) return WorldLookup.CultureOfCity(c.key);
            }
            catch (Exception) { /* 无地域资料 */ }
            return null;
        }

        // 竖屏（视口高大于宽）开场镜头：横向视野窄，全局视角只看得到战场中段（往往只有敌军）。
        // 两军能一起框进来（距离不太远）就框两军，否则先框住玩家自己的部队（观战时为攻方），余下视野朝向敌军
        bool PortraitOpening(CameraRig rig)
        {
            var cam = rig.Cam;
            float aspect = cam != null && cam.aspect > 0 ? cam.aspect : Screen.width / (float)Mathf.Max(1, Screen.height);
            if (aspect >= 1) return false;
            const float T = BattleView.T;
            Func<List<BUnit>, Rect> rectOf = units =>
            {
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                foreach (var u in units)
                {
                    var p = V.Tile(u.x, u.y);                     // 地图坐标（Unity x / z）
                    xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                    yMin = Mathf.Min(yMin, p.z); yMax = Mathf.Max(yMax, p.z);
                }
                // 留出一格边距；竖屏上下有顶栏与按钮，纵向再多留一些
                return Rect.MinMaxRect(xMin - T * 1.3f, yMin - T * 1.4f, xMax + T * 1.3f, yMax + T * 1.1f);
            };
            var all = M.Alive(0).Concat(M.Alive(1)).ToList();
            if (all.Count == 0) return false;
            var both = rectOf(all);
            if (rig.FitDistance(both) <= rig.MaxDist * 0.85f) { rig.FitRect(both, true); return true; }
            var mine = M.Alive(playerSide == 1 ? 1 : 0).ToList(); var foes = M.Alive(playerSide == 1 ? 0 : 1).ToList();
            if (mine.Count == 0) return false;
            // 框住我军，距离不近于横屏开场（40），我军靠在画面一侧、其余视野朝向敌军
            var own = rectOf(mine);
            float d = Mathf.Clamp(Mathf.Max(40, rig.FitDistance(own)), rig.MinDist, rig.MaxDist);
            float fov = (cam != null && cam.fieldOfView > 0 ? cam.fieldOfView : 34) * Mathf.Deg2Rad;
            float wVis = d * 2 * Mathf.Tan(fov / 2) * aspect / 1.05f;
            float ox = (own.xMin + own.xMax) / 2, bx = (both.xMin + both.xMax) / 2;
            float ex = ox;
            if (foes.Count > 0) { ex = 0; foreach (var u in foes) ex += V.Tile(u.x, u.y).x; ex /= foes.Count; }
            int dir = ex >= ox ? 1 : -1;
            float cx = (dir > 0 ? own.xMin : own.xMax) + dir * wVis / 2;
            cx = dir > 0 ? Mathf.Clamp(cx, ox, Mathf.Max(ox, bx)) : Mathf.Clamp(cx, Mathf.Min(ox, bx), ox);
            rig.FocusMap(cx, (own.yMin + own.yMax) / 2, d, true);
            return true;
        }

        // ---------------------------------------------------------- 界面 --
        void BuildHud()
        {
            hud = UIKit.NewRect("BattleHUD", UIKit.Screens); UIKit.Stretch(hud);
            var top = UIKit.Panel(hud, "Top");
            UIKit.Place(top, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(980, 64));
            topText = UIKit.Label(top, "", 24, UIKit.Text, TextAnchor.MiddleCenter);
            UIKit.Stretch(topText.rectTransform, 16, 0, 16, 0);
            var end = endBtn = UIKit.Button(hud, "结束回合", () => endTurn = true, 28, true);
            UIKit.Place(end.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(220, 70));
            UIKit.Btn auto = null;
            auto = UIKit.Button(hud, "委任作战", () => { autoPlayer = !autoPlayer; endTurn = true; auto.label.text = autoPlayer ? "亲自指挥" : "委任作战"; SyncEndBtn(); }, 26);
            UIKit.Place(auto.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-260, 24), new Vector2(200, 70));
            // 攻击画面 开 / 快 / 关（Clash.Mode；快捷键 V）
            animBtn = UIKit.Button(hud, "", CycleAnim, 24, false, "AnimMode");
            UIKit.Place(animBtn.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-476, 24), new Vector2(150, 70));
            animShown = null;
            SyncAnimBtn();
            if (playerSide < 0) { end.rt.gameObject.SetActive(false); auto.rt.gameObject.SetActive(false); animBtn.rt.gameObject.SetActive(false); }
            // 部队情报卡：头像（左上）+ 姓名 / 兵力条 / 兵力士气训练（头像右侧）+ 其余各行（下方）；仅显示情报，点击穿透到棋盘
            card = UIKit.Panel(hud, "Card");
            UIKit.Place(card, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(470, 250));
            var cardBg = card.GetComponent<Image>(); if (cardBg != null) cardBg.raycastTarget = false;
            cardPic = UIKit.Img(card, null, Color.white, "Portrait");
            cardPic.raycastTarget = false; cardPic.preserveAspect = true;
            UIKit.Place(cardPic.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -18), new Vector2(88, 88));
            cardHead = UIKit.Label(card, "", 22, UIKit.Text, TextAnchor.MiddleLeft);
            cardHead.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Place(cardHead.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(118, -14), new Vector2(336, 42));
            // 姓名旁的细经验条（.sg-card-exp：2.6em × .32em）
            cardExp = UIKit.ExpBar(card, 0, 46, 6);
            cardExp.gameObject.SetActive(false);
            var bar = UIKit.Bar(card, 1, SideColor(0));
            UIKit.Place(bar, new Vector2(0, 1), new Vector2(0, 1), new Vector2(118, -60), new Vector2(330, 10));
            cardBarFill = (RectTransform)bar.Find("Fill");
            cardBarImg = cardBarFill != null ? cardBarFill.GetComponent<Image>() : null;
            cardSub = UIKit.Label(card, "", 22, UIKit.Text, TextAnchor.MiddleLeft);
            UIKit.Place(cardSub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(118, -74), new Vector2(340, 34));
            cardText = UIKit.Label(card, "", 22, UIKit.Text, TextAnchor.UpperLeft);
            cardText.lineSpacing = 1.15f;
            UIKit.Place(cardText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -114), new Vector2(430, 128));
            card.gameObject.SetActive(false);
        }

        void UpdateHud()
        {
            string side = M.side == 0 ? "<color=#73bfff>攻方</color>" : "<color=#ff806b>守方</color>";
            string dots = new string('●', Mathf.Max(0, M.ap)) + new string('○', Mathf.Max(0, M.ActionPoints(M.side) - M.ap));
            int t0 = M.Alive(0).Sum(u => u.Troops), t1 = M.Alive(1).Sum(u => u.Troops);
            topText.text = string.Format("第 <b>{0}</b>/{1} 日　{2}行动　行动力 <color=#f3c969>{3}</color>　｜　攻 {4}  粮{5}　守 {6}  粮{7}",
                Mathf.Min(M.day, Balance.BattleDays), Balance.BattleDays, side, dots, t0, M.food[0], t1, M.food[1]);
            SyncEndBtn();
            SyncAnimBtn();
        }
        // 「结束回合」只在玩家亲自指挥的本方行动时可按（= 网页版 updateHud 的 endBtn.disabled）
        void SyncEndBtn() { if (endBtn != null && endBtn.button != null) endBtn.button.interactable = M.side == playerSide && !autoPlayer; }

        RectTransform cardExp; string cardExpKey;
        void ShowCard(BUnit u)
        {
            if (u == null) { card.gameObject.SetActive(false); return; }
            card.gameObject.SetActive(true);
            var f = u.Form;
            // 姓名 + 等级（Lv，第三版成长）+ 攻方 / 守方·主将；Lv 之后放一条细经验条
            string nameLv = "<size=" + UIKit.Fs(30) + "><b>" + u.gen.name + "</b></size>";
            ExpInfo ei = null;
            if (ProgressOn()) { try { ei = Progress.ExpInfo(u.gen); } catch (Exception ex) { Debug.LogException(ex); ei = null; } }
            if (ei != null) nameLv += "  <color=#f3c969><b>Lv<color=#fff1c8>" + ei.level + "</color></b></color>";
            string sub = "<color=#a8a194>" + (u.side == 0 ? "攻方" : "守方") + (u.commander ? "·主将" : "") + "</color>";
            float pre = 0;
            if (ei != null)
            {
                try { pre = cardHead.cachedTextGeneratorForLayout.GetPreferredWidth(nameLv, cardHead.GetGenerationSettings(Vector2.zero)) / Mathf.Max(0.01f, cardHead.pixelsPerUnit); }
                catch (Exception) { pre = 0; }
            }
            cardHead.text = nameLv + (ei != null ? "<size=" + UIKit.Fs(22) + ">" + new string('　', 3) + "</size>" : "  ") + sub;
            if (cardExp != null)
            {
                cardExp.gameObject.SetActive(ei != null && pre > 0);
                if (ei != null && pre > 0)
                {
                    cardExp.anchoredPosition = new Vector2(118 + pre + 10, -14 - 21);
                    // 换了部队或经验变了才重播长出动画
                    string key = u.gen.id + ":" + ei.exp;
                    var fill = cardExp.Find("Fill");
                    var anim = fill != null ? fill.GetComponent<ExpFill>() : null;
                    if (anim != null && key != cardExpKey) { anim.target = Mathf.Clamp01((float)ei.pct); anim.enabled = false; anim.enabled = true; }
                    cardExpKey = key;
                }
            }
            if (cardBarFill != null) cardBarFill.anchorMax = new Vector2(Mathf.Clamp01(u.Troops / (float)Mathf.Max(1, u.gen.MaxTroops)), 1);
            if (cardBarImg != null) cardBarImg.color = SideColor(u.side);
            cardSub.text = string.Format("兵力 <b>{0}</b>　士气 {1}　训练 {2}", u.Troops, u.morale, u.gen.training);
            cardText.text = string.Format("武力 {0}　智力 {1}{8}\n阵型 <color=#f3c969>{2}</color>（攻×{3:0.00} 防×{4:0.00}）\n地形 {5}{6}{7}",
                u.gen.war, u.gen.intel, f.Name, f.Atk, f.Def, Defs.TerrainName(M.map[u.x, u.y]), u.confused > 0 ? "　<color=#c79bff>混乱中</color>" : "", SpecialLine(u), VetText(u.gen));
            // 卡片高度随行数（习得的必杀另起一行）
            float th = Mathf.Ceil(cardText.preferredHeight) + 2;
            cardText.rectTransform.sizeDelta = new Vector2(430, Mathf.Max(128, th));
            card.sizeDelta = new Vector2(470, Mathf.Max(250, 114 + th + 14));
            // 头像（势力色边框）；阵亡部队灰暗显示
            try
            {
                cardPic.sprite = Portrait.SpriteOf(Portrait.Texture(u.gen, 128));
                cardPic.color = u.alive ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1);
                cardPic.enabled = true;
            }
            catch (Exception e) { Debug.LogException(e); cardPic.enabled = false; }
        }

        static bool ProgressOn() { return Progress.enabled; }
        // 老练加成（每高 1 级攻防与计策威力 +4%，Progress.VetK）：1 级时为空
        static string VetText(General gen)
        {
            if (!ProgressOn() || gen == null) return "";
            double k = Progress.VetK(gen);
            return k > 1.0001 ? "　<color=#f3c969>老练 +" + Mathf.RoundToInt((float)((k - 1) * 100)) + "%</color>" : "";
        }
        // 情报卡的必杀：专属必杀一行「名」未施展 / 已施展（「可用」留给行动菜单：此刻是否有目标）；
        // 习得的招式压成一行：未施展的着色，已施展的灰显（← Specials.cardLine）
        string SpecialLine(BUnit u)
        {
            try
            {
                if (u == null || u.gen == null) return "";
                var list = Specials.Known(u.gen);
                if (list.Count == 0) return "";
                var sig = list[0];
                string head = "\n必杀 <color=" + Specials.UiColor(sig) + ">「" + sig.name + "」</color>" + (Specials.UsedIn(u, sig) ? "<color=#a8a194>　已施展</color>" : "<color=#73d98c>　未施展</color>");
                if (list.Count == 1) return head;
                var more = list.Skip(1).Select(sp => Specials.UsedIn(u, sp) ? "<color=#7d776c>" + sp.name + "</color>" : "<color=" + Specials.UiColor(sp) + ">" + sp.name + "</color>");
                return head + "\n习得 " + string.Join("<color=#a8a194> · </color>", more.ToArray());
            }
            catch (Exception e) { Debug.LogException(e); return ""; }
        }

        // ---------------------------------------------------------- 动画开关 --
        static string AnimName(ClashMode m) { return m == ClashMode.On ? "开" : m == ClashMode.Fast ? "快" : "关"; }
        void SyncAnimBtn()
        {
            if (animBtn == null) return;
            var m = Clash.Mode;
            string key = Clash.ModeName(m);
            if (animShown == key) return;
            animShown = key;
            animBtn.label.text = "动画 <b><color=" + (m == ClashMode.Off ? "#a8a194" : "#f3c969") + ">" + AnimName(m) + "</color></b>";
        }
        void CycleAnim()
        {
            var m = Clash.CycleMode();
            SyncAnimBtn();
            UIKit.Toast("攻击动画：" + AnimName(m) + (m == ClashMode.Fast ? "（加速播放）" : m == ClashMode.Off ? "（直接显示结果）" : ""), 1.4f);
            Sfx.Click();
        }
        // 键盘：V 切换攻击动画（对话框、单挑格斗、攻击画面进行中时不响应）
        IEnumerator KeyLoop()
        {
            while (true)
            {
                if (Input.GetKeyDown(KeyCode.V) && playerSide >= 0 && !Modifier() && !UIKit.AnyModal && !DuelGame.Active && !Clash.Active && !Clash.CutActive && !TextFocused())
                    CycleAnim();
                yield return null;
            }
        }
        static bool Modifier()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }
        static bool TextFocused()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            return go != null && go.GetComponent<InputField>() != null;
        }

        // ---------------------------------------------------------- 玩家 --
        IEnumerator PlayerPhase()
        {
            endTurn = false; tap = null;
            BUnit sel = null;
            UpdateHud();
            // 镜头回到我军（仅当电脑行动时镜头跟随了敌军；开战首回合保持全局视角）
            if (aiMovedCam)
            {
                aiMovedCam = false;
                var lead = M.Commander(M.side);
                var focusU = lead != null && lead.alive ? lead : M.Alive(M.side).FirstOrDefault(M.CanAct);
                if (focusU != null) Game.I.Rig.Focus(V.Tile(focusU.x, focusU.y));
            }
            UIKit.Toast("我军行动：点选部队", 1.4f);
            while (!endTurn && M.result == 0 && M.ap > 0 && M.Alive(M.side).Any(M.CanAct))
            {
                UpdateHud();
                if (sel != null) { V.SetCursor(sel); ShowCard(sel); }
                tap = null;
                while (tap == null && !endTurn) yield return null;
                if (endTurn) break;
                Vector2Int t;
                // 先看是否点在部队头顶的名牌上（窄屏上名牌比部队本身更显眼，常被当作点选目标），否则按地面取格
                var lu = V.UnitFromLabel(tap.Value);
                if (lu != null) t = new Vector2Int(lu.x, lu.y);
                else if (!V.TileFromScreen(Game.I.Rig.Cam, tap.Value, out t)) continue;
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

        // 部队会的计策（原四计按智力 + 习得）
        List<TacticDef> TacticsOf(BUnit u)
        {
            try { return M.KnownTactics(u); } catch (Exception e) { Debug.LogException(e); }
            return Defs.Tactics.Where(t => u.gen.intel >= t.ReqInt).ToList();
        }

        // 行动菜单。子菜单（必杀 / 策略 / 选目标 / 阵型）取消时回到本菜单；本菜单取消时：移动过的部队待机
        IEnumerator ActionMenu(BUnit u, bool moved = false)
        {
            bool SP = SpecialsOn();
            for (int guard = 0; guard < 40; guard++)
            {
                if (!u.alive || M.result != 0) break;
                var adj = M.AdjacentEnemies(u);
                var tacs = TacticsOf(u);
                int tacOk = tacs.Count(t => { try { return M.TacticUsable(u, t) && M.TacticTargets(u, t).Count > 0; } catch (Exception) { return false; } });
                var items = new List<UIKit.Item>
                {
                    new UIKit.Item("攻击", adj.Count > 0 ? adj.Count + " 支敌军相邻" : "无相邻敌军", adj.Count > 0),
                    new UIKit.Item("策略", tacs.Count > 0 ? "智力 " + u.gen.intel + "<color=#a8a194> · " + tacs.Count + " 计" + (tacOk < tacs.Count ? " · 可用 " + tacOk : "") + "</color>" : "<color=#a8a194>未习计策</color>", tacs.Count > 0),
                    new UIKit.Item("单挑", "武力 " + u.gen.war, adj.Count > 0),
                    new UIKit.Item("阵型", u.Form.Name),
                    new UIKit.Item("待机"),
                };
                // 必杀：插在「策略」之后，显示招式名；已施展 / 无目标时灰显并注明原因
                if (SP)
                {
                    UIKit.Item it = null;
                    try { it = SpecialMenuItem(u); } catch (Exception e) { Debug.LogException(e); }
                    items.Insert(2, it ?? new UIKit.Item("必杀", "无", false));
                }
                var r = new Ref<int>();
                yield return UIKit.Choose(u.gen.name + " 的行动", items, r, null, SP ? 620 : 520);
                int ri = r.v;
                if (ri < 0) { if (moved) M.Spend(u); break; }
                if (SP && ri == 2)
                {
                    var sd = new Ref<bool>();
                    yield return SpecialMenu(u, sd);
                    if (sd.v) break;
                    continue;
                }
                if (SP && ri > 2) ri--;
                bool done = true;
                switch (ri)
                {
                    case 0: { var tgt = new Ref<BUnit>(); yield return PickEnemy(adj, "攻击目标", tgt); if (tgt.v != null) { yield return DoAttack(u, tgt.v); M.Spend(u); } else done = false; break; }
                    case 1: { var td = new Ref<bool>(); yield return TacticMenu(u, td); done = td.v; break; }
                    case 2: { var tgt = new Ref<BUnit>(); yield return PickEnemy(adj, "单挑对手", tgt); if (tgt.v != null) { yield return DoDuel(u, tgt.v); M.Spend(u); } else done = false; break; }
                    case 3: { var fd = new Ref<bool>(); yield return FormationMenu(u, fd); done = fd.v; break; }
                    case 4: M.Spend(u); break;
                    default: if (moved) M.Spend(u); break;
                }
                if (done) break;
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

        // 「策略」：已会的计策（原四计按智力 + 习得的新计策；Tactics.MenuItems）。施展后 Spend → done = true；取消（回到行动菜单）→ false
        IEnumerator TacticMenu(BUnit u, Ref<bool> done)
        {
            done.v = false;
            List<TacticMenuRow> rows;
            try { rows = Tactics.MenuItems(M, u); } catch (Exception e) { Debug.LogException(e); rows = new List<TacticMenuRow>(); }
            if (rows.Count == 0) { UIKit.Toast("尚未掌握计策"); yield break; }
            bool shortS = ShortScreen();
            // 每行：计策名（计策色）；原四计右栏为说明（不可用时为原因），新计策右栏为等阶（及原因）、说明行为数值摘要
            var items = rows.Select(x =>
            {
                var t = x.tactic;
                string name = "<color=" + (t.Color ?? "#f3c969") + ">" + t.Name + "</color>";
                if (!t.IsNew) return new UIKit.Item(name, x.ok ? t.Desc : "<color=#a8a194>" + x.why + "</color>", x.ok, null);
                string tag = "<color=#a8a194>" + x.tierLabel + "</color>";
                return new UIKit.Item(name, x.ok ? tag : tag + " <color=#a8a194>" + x.why + "</color>", x.ok, shortS ? x.summaryShort : x.summary);
            }).ToList();
            var r = new Ref<int>();
            yield return UIKit.Choose(u.gen.name + " 的策略", items, r, null, 680);
            if (r.v < 0) yield break;
            var tac = rows[r.v].tactic;
            var tgt = new Ref<BUnit>();
            yield return PickTacticTarget(u, tac, tgt);
            if (tgt.v == null) yield break;
            yield return DoTactic(u, tac, tgt.v);
            M.Spend(u);
            done.v = true;
        }
        // 计策目标：自身为中心 → 自身；只有一个 → 它；否则在棋盘上标出可选目标（友军绿、敌军红）后沿用 PickEnemy
        IEnumerator PickTacticTarget(BUnit u, TacticDef tac, Ref<BUnit> result)
        {
            result.v = null;
            var targets = M.TacticTargets(u, tac);
            if (targets.Count == 0) yield break;
            if (tac.Kind == TacticKind.Inspire || tac.Target == "self") { result.v = u; yield break; }
            if (targets.Count == 1) { result.v = targets[0]; yield break; }
            bool ally = tac.Target == "ally";
            try
            {
                V.ClearHighlights();
                var c = Art.Hex(ally ? "#73d98c" : "#ff4d40"); c.a = 0.75f;
                V.Highlight(targets.Select(o => new Vector2Int(o.x, o.y)), c);
            }
            catch (Exception e) { Debug.LogException(e); }
            try { var r = new Ref<BUnit>(); yield return PickEnemy(targets, tac.Name + "的目标", r); result.v = r.v; }
            finally { try { V.ClearHighlights(); } catch (Exception) { /* 忽略 */ } }
        }

        IEnumerator FormationMenu(BUnit u, Ref<bool> done)
        {
            done.v = false;
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
            done.v = true;
        }
        // 矮屏（横屏手机，视口高 < 560 点）：菜单说明只留一行
        static bool ShortScreen() { float h = UIKit.ViewHeightPt; return h > 0 && h < 560; }

        // ---------------------------------------------------------- 动作 --
        // 当前是否在「看」电脑行动（敌方回合、委任或旁观）：攻击画面 / 必杀特写加速
        bool Watching() { return autoPlayer || playerSide < 0 || M.side != playerSide; }
        bool SpecialsOn() { return BattleModel.specialsEnabled && M != null; }

        IEnumerator DoAttack(BUnit a, BUnit t)
        {
            ShowCard(a);
            yield return V.Lunge(a, t);
            int beforeA = a.Troops, beforeT = t.Troops;
            var r = M.Attack(a, t);
            // 攻击画面（开 / 快 / 关）。电脑行动时 ×1.5，委任时 ×1.8（再乘「快」的 ×2）
            if (Clash.Enabled)
            {
                ClashSide ca = null, cd = null;
                try { ca = Clash.FromUnit(M, a, beforeA, a.Troops); cd = Clash.FromUnit(M, t, beforeT, t.Troops); }
                catch (Exception e) { Debug.LogException(e); }
                if (ca != null && cd != null)
                {
                    float speed = autoPlayer ? 1.8f : Watching() ? 1.5f : 1f;
                    yield return Co.Guard(Clash.Play(ca, cd, M.map[t.x, t.y], null, playerSide >= 0 ? playerSide : -1, speed, null), e => Debug.LogException(e));
                }
                SyncAnimBtn();                      // 攻击画面里的「动画」切换也同步到按钮
            }
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
            // 第三版新计策：各自的 BattleView.SpecialFx 风格与飘字（Tactics.Perform；不 Spend）
            if (tac != null && tac.IsNew)
            {
                yield return Co.Guard(PerformTactic(u, tac, t), e => Debug.LogException(e));
                foreach (var x in M.units) V.Refresh(x);
                UpdateHud();
                yield break;
            }
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

        // ---------------------------------------------------------- 新计策 --
        // 网页版 Tactics.perform（新计策部分）：结算 → V.SpecialFx（计策的风格与主色；成功或有命中时）→ 伤害 / 飘字 → 刷新 → 溃散
        IEnumerator PerformTactic(BUnit u, TacticDef t, BUnit target)
        {
            ShowCard(u);
            string tc = t.Color ?? "#f3c969";
            UIKit.Toast(u.gen.name + "施展「<color=" + tc + ">" + t.Name + "</color>」！", 1.5f);
            var res = Tactics.Perform(M, u, t, target);
            if (res == null) yield break;
            var shown = new bool[res.hits.Count];
            Action<int> onHit = i =>
            {
                if (i < 0 || i >= res.hits.Count || shown[i]) return;
                shown[i] = true;
                try { V.Hit(res.hits[i].unit, res.hits[i].dmg, true); } catch (Exception e) { Debug.LogException(e); }
            };
            // 特效的替身招式（同网页版 { id: 'tac:'+id, name, kind: id, fx, color, radius, range }）
            var sp = new Special { id = "tac:" + t.Id, name = t.Name, kind = t.Id, fx = t.Fx, color = tc, radius = Mathf.Max(1, t.Radius), range = t.Range, tactic = true };
            if (res.success || res.hits.Count > 0)
                yield return Co.Guard(V.SpecialFx(u, Tactics.FxTargets(res, u), t.Fx, Art.Hex(tc), new SpecialFxInfo { sp = sp, res = res, target = res.target ?? target, onHit = onHit }), e => Debug.LogException(e));
            else
                yield return Co.Guard(V.MagicFx(target ?? u, Art.Hex("#a8a194")), e => Debug.LogException(e));
            for (int i = 0; i < shown.Length; i++) if (!shown[i]) onHit(i);
            Action<BUnit, string, string, int, float> text = (x, s, col, size, k) => { try { if (x != null) V.FloatText(V.Tile(x.x, x.y) + Vector3.up * k, s, Art.Hex(col), size); } catch (Exception e) { Debug.LogException(e); } };
            if (!res.success) text(res.target ?? target ?? u, "识破", "#a8a194", 34, 1.6f);
            foreach (var x in res.confused) text(x, "混乱", "#cc99ff", 38, 2.4f);
            if (res.success) foreach (var x in res.resisted) text(x, "识破", "#a8a194", 30, 2.4f);
            foreach (var h in res.healed) text(h.unit, "+" + h.amount, "#73d98c", 40, 1.6f);
            string word = Tactics.CurseWord(t.Id);
            if (word != null) foreach (var x in res.cursed) text(x, word, Tactics.CurseColor(t.Id), 32, 2.6f);
            string buff = Tactics.BuffWord(t.Id);
            if (buff != null) foreach (var x in res.buffed) text(x, buff, "#f3c969", 30, 2.3f);
            foreach (var x in M.units) V.Refresh(x);
            UpdateHud();
            yield return new WaitForSeconds(0.4f);
            var gone = new List<BUnit>();
            foreach (var x in res.routed.Concat(res.affected).Concat(target != null ? new[] { target } : new BUnit[0])) if (x != null && !x.alive && !gone.Contains(x)) gone.Add(x);
            foreach (var x in gone) if (V.IsShown(x)) { UIKit.Toast(x.gen.name + "部溃散！"); yield return V.Rout(x); }
        }

        // ---------------------------------------------------------- 必杀 --
        // 行动菜单的「必杀」条目（← Specials.menuItem）：会一招时显示招式名（不可用时注明原因）与说明；
        // 会多招时显示「名」等 N 招 · 可用 k（第一招可用的；都不可用时注明原因）
        UIKit.Item SpecialMenuItem(BUnit u)
        {
            var info = Specials.MenuInfo(M, u);
            if (info.list.Count == 0) return new UIKit.Item("必杀", "<color=#a8a194>无</color>", false);
            var sp = info.shown;
            string nm = "<color=" + Specials.UiColor(sp) + ">「" + sp.name + "」</color>";
            if (info.list.Count == 1) return new UIKit.Item("必杀", info.ok ? nm : nm + " <color=#a8a194>" + info.why + "</color>", info.ok, MenuDesc(sp));
            string more = "<color=#a8a194>等 " + info.list.Count + " 招 · 可用 " + info.usable + "</color>";
            string why = info.ok ? "" : " <color=#a8a194>" + (info.why ?? "") + "</color>";
            return new UIKit.Item("必杀", nm + " " + more + why, info.ok, null);
        }
        // 行动菜单里的说明：矮屏（横屏手机）只留一行（机制名 + 说明的第一句，截断），免得把「待机」挤出菜单；
        // 完整说明在选择目标时的提示、部队卡片与武将详情里都有
        static string MenuDesc(Special sp)
        {
            string full = sp.desc ?? "";
            if (!ShortScreen()) return full;
            string first = full.Split('。', '；', '！')[0];
            if (first.Length == 0) first = full;
            const int MAX = 16;
            if (first.Length > MAX) first = first.Substring(0, MAX) + "…";
            return Specials.KindName(sp.kind) + "：" + first;
        }

        // 「必杀」：列出全部已会的必杀（专属在前），每招一行：等阶 / 专属、名称、机制 / 不可用的原因、数值摘要（与典故）。
        // 只会一招时直接选目标。施展后 Spend → done = true；取消（回到行动菜单）→ false
        IEnumerator SpecialMenu(BUnit u, Ref<bool> done)
        {
            done.v = false;
            var list = M.KnownSpecials(u);
            if (list.Count == 0) { UIKit.Toast("无必杀技"); yield break; }
            var sp = list[0];
            if (list.Count > 1)
            {
                var rows = list.Select(x => SpecialItem(u, x)).ToList();
                int nUsed = list.Count(x => M.SpWasUsed(u, x));
                string info = "<color=#a8a194>每招每战限一次" + (nUsed > 0 ? " · 已施展 " + nUsed + " / " + list.Count : "") + "</color>";
                var r = new Ref<int>();
                yield return UIKit.Choose(u.gen.name + " 的必杀", rows, r, info, 700);
                if (r.v < 0) { u.spPick = null; yield break; }
                sp = list[r.v];
            }
            string why;
            if (!M.SpecialUsable(u, sp.id, out why)) { UIKit.Toast(string.IsNullOrEmpty(why) ? "无法施展" : why); yield break; }
            var tgt = new Ref<BUnit>();
            yield return PickSpecialTarget(u, sp, tgt);
            if (tgt.v == null) { u.spPick = null; yield break; }
            yield return DoSpecial(u, tgt.v, false, null, sp.id);
            M.Spend(u);
            done.v = true;
        }
        // 必杀列表的一行
        UIKit.Item SpecialItem(BUnit u, Special sp)
        {
            string why; bool ok = M.SpecialUsable(u, sp.id, out why);
            string col = Specials.UiColor(sp);
            string tier = sp.pool ? UIText.Tag(TagStyle.SpTier, Specials.TierNameOf(sp.tier)) : UIText.Tag(TagStyle.SpSig, "专属");
            string label = tier + "<color=" + col + ">" + sp.name + "</color>";
            string kind = Specials.KindName(sp.kind) ?? "";
            string right = ok ? "<color=#a8a194>" + kind + "</color>" : "<color=#a8a194>" + (string.IsNullOrEmpty(why) ? "不可用" : why) + "</color>";
            string rules = "";
            try { rules = Specials.Rules(sp) ?? ""; } catch (Exception e) { Debug.LogException(e); }
            // 去掉开头的机制名（右栏已有）
            if (kind.Length > 0 && rules.StartsWith(kind + " · ", StringComparison.Ordinal)) rules = rules.Substring(kind.Length + 3);
            // 矮屏（横屏手机）：只留数值摘要，去掉末尾的「依武力 / 依智力」
            string desc = ShortScreen() ? System.Text.RegularExpressions.Regex.Replace(rules, " · 依(武力|智力|政治)$", "")
                : rules + (!string.IsNullOrEmpty(sp.lore) ? "\n<i><color=#cfc6b4>" + sp.lore + "</color></i>" : "");
            return new UIKit.Item(label, right, ok, desc) { descBlock = true };
        }

        // 选择目标（← Specials.pickTarget）：记入 u.spPick；自身为中心的招式直接返回 u；只有一个目标时直接返回；
        // 否则在棋盘上标出射程与可选目标后沿用 PickEnemy。没有目标时清除 u.spPick
        IEnumerator PickSpecialTarget(BUnit u, Special sp, Ref<BUnit> result)
        {
            result.v = null;
            if (sp == null) yield break;
            u.spPick = sp.id;
            var list = M.SpecialTargets(u, sp.id);
            if (list.Count == 0) { u.spPick = null; yield break; }
            if (!Specials.NeedsTarget(sp)) { result.v = u; yield break; }
            try
            {
                V.ClearHighlights();
                var ring = new List<Vector2Int>();
                int range = Mathf.Max(1, sp.Range);
                for (int x = 0; x < M.W; x++) for (int y = 0; y < M.H; y++)
                    {
                        int d = Mathf.Abs(x - u.x) + Mathf.Abs(y - u.y);
                        if (d > 0 && d <= range && !list.Any(o => o.x == x && o.y == y)) ring.Add(new Vector2Int(x, y));
                    }
                var rc = Art.Hex(sp.color ?? "#f3c969"); rc.a = 0.28f;
                V.Highlight(ring, rc);
                var tc = Specials.TargetSide(sp) == "ally" ? Art.Hex("#73d98c") : Art.Hex("#ff4d40"); tc.a = 0.75f;
                V.Highlight(list.Select(o => new Vector2Int(o.x, o.y)), tc);
            }
            catch (Exception e) { Debug.LogException(e); }
            try
            {
                if (list.Count == 1) result.v = list[0];
                else { var r = new Ref<BUnit>(); yield return PickEnemy(list, sp.name + " 的目标", r); result.v = r.v; }
            }
            finally { V.ClearHighlights(); }
            if (result.v == null) u.spPick = null;
        }

        // 必杀技：特写（按动画设定播放，显示所选的招式）→ PerformSpecial（结算、特效、伤害、溃散）。不消耗行动（调用方 Spend）
        // id：招式编号（缺省 = u.spPick → 第一招可施展的；电脑的计划带 plan.special）
        IEnumerator DoSpecial(BUnit u, BUnit target, bool ai, Ref<SpecialResult> outRes, string id = null)
        {
            var sp = M.SpecialOf(u, id);
            string why = "";
            if (sp == null) why = "无必杀技";
            if (sp == null || !M.SpecialUsable(u, sp.id, out why)) { UIKit.Toast(string.IsNullOrEmpty(why) ? "无法施展" : why); yield break; }
            u.spPick = sp.id;
            ShowCard(u); V.SetCursor(u);
            yield return SpecialCutIn(u, sp, ai);
            var res = new Ref<SpecialResult>();
            yield return Co.Guard(PerformSpecial(u, target, sp.id, res), e => Debug.LogException(e));
            V.SpLabelsDim = false;
            if (spRigBack >= 0 && Game.I != null && Game.I.Rig != null) { Game.I.Rig.DesiredDistance = spRigBack; spRigBack = -1; }
            foreach (var x in M.units) V.Refresh(x);
            UpdateHud();
            if (outRes != null) outRes.v = res.v;
        }
        // 特写时长：「开」1.1 秒（电脑 / 委任 0.85 秒），「快」0.75 秒；「关」不播。
        // Clash.CutIn 自己会再除以 Clash.Speed，这里换算成最终时长，避免「快」时被加速两次。
        IEnumerator SpecialCutIn(BUnit u, Special sp, bool ai)
        {
            if (!Clash.Enabled) yield break;
            bool quick = ai || autoPlayer;
            var color = Art.Hex(Specials.UiColor(sp));
            float k = Mathf.Max(0.1f, Clash.Speed);
            float dur = k > 1.01f ? 0.75f : quick ? 0.85f : 1.1f;
            yield return Co.Guard(Clash.CutIn(u.gen, sp.name, color, u.side, sp.cry, 1.1f / (dur * k)), e => Debug.LogException(e));
        }

        // 特效要照顾的部队（主目标在前）
        static List<BUnit> FxTargets(SpecialResult res, BUnit u)
        {
            var o = new List<BUnit>();
            Action<BUnit> add = x => { if (x != null && !o.Contains(x)) o.Add(x); };
            if (res.target != null && res.target != u) add(res.target);
            foreach (var h in res.hits) add(h.unit);
            foreach (var x in res.affected) add(x);
            if (o.Count == 0) add(res.target ?? u);
            return o;
        }

        // 网页版 Specials.perform（特写由 DoSpecial 先播）：结算 → 镜头推近 → 特效（命中时显示伤害）→ 飘字 → 刷新 → 溃散
        float spRigBack = -1;
        IEnumerator PerformSpecial(BUnit u, BUnit target, string sid, Ref<SpecialResult> outRes)
        {
            var sp = M.SpecialOf(u, sid);
            string why;
            if (sp == null || !M.SpecialUsable(u, sp.id, out why)) { UIKit.Toast("无法施展"); yield break; }
            sid = sp.id;
            u.spPick = sid;
            var cands = M.SpecialTargets(u, sid);
            if (target == null || !cands.Contains(target)) target = cands.Count > 0 ? cands[0] : null;
            ShowCard(u); V.SetCursor(u);
            string uc = Specials.UiColor(sp);
            UIKit.Toast(u.gen.name + "施展必杀「<color=" + uc + ">" + sp.name + "</color>」！", 1.6f);
            // 结算（所选的招式）
            var res = M.UseSpecial(u, target, sid);
            outRes.v = res;
            if (res.sp == null) yield break;
            // 镜头推近：施展者与主目标的中点；部队名牌暂时淡出，让出画面
            var rig = Game.I != null ? Game.I.Rig : null;
            if (rig != null)
            {
                var tg = res.target ?? u;
                Vector3 a = V.Tile(u.x, u.y), b = V.Tile(tg.x, tg.y);
                var mid = Vector3.Lerp(a, b, 0.5f);
                spRigBack = rig.DesiredDistance;
                float span = Vector3.Distance(a, b) + (Specials.NeedsTarget(sp) ? 0 : 2 * Mathf.Max(1, (float)sp.radius) * 2);
                rig.Focus(mid, Mathf.Clamp(Mathf.Max(18, span * 2.2f), rig.MinDist, spRigBack));
            }
            V.SpLabelsDim = true;
            // 特效（SpecialFx 在每次命中时回调 onHit(i)，显示该次伤害）
            var shown = new bool[res.hits.Count];
            Action<int> onHit = i =>
            {
                if (i < 0 || i >= res.hits.Count || shown[i]) return;
                shown[i] = true;
                var h = res.hits[i];
                V.Hit(h.unit, h.dmg, true);
            };
            yield return V.SpecialFx(u, FxTargets(res, u), sp.fx, Art.Hex(sp.color ?? "#ffd36b"), new SpecialFxInfo { sp = sp, res = res, target = target, onHit = onHit });
            // 突击 / 击退后的位置（特效未处理时直接归位）。写逻辑位置 v.pos（= specials.js v.pos.copy(t)）：
            // UnitVisual 每帧按 pos 重设变换，直接改 transform 会被覆盖
            var movedUnits = new List<BUnit>();
            if (res.moved != null) movedUnits.Add(u);
            foreach (var p in res.pushed) movedUnits.Add(p.unit);
            foreach (var x in movedUnits)
            {
                var v = V.VisOf(x);
                if (v != null) { var t = V.Tile(x.x, x.y); if (Vector3.Distance(v.pos, t) > 0.05f) v.pos = t; }
            }
            // 飘字
            Action<BUnit, string, string, int, float> text = (x, s, col, size, k) => { try { V.FloatText(V.Tile(x.x, x.y) + Vector3.up * k, s, Art.Hex(col), size); } catch (Exception e) { Debug.LogException(e); } };
            int n = 0;
            for (int i = 0; i < res.hits.Count; i++) if (!shown[i]) { if (n++ > 0) yield return new WaitForSeconds(0.06f); onHit(i); }
            if (res.killed != null) text(res.killed, "一击毙命", "#ff5a5a", 46, 2.3f);
            else if (res.kind == "assassinate" && !res.success && res.target != null) text(res.target, "失手", "#a8a194", 36, 2.3f);
            foreach (var x in res.confused) text(x, "混乱", "#cc99ff", 38, 2.4f);
            foreach (var x in res.resisted) text(x, "识破", "#a8a194", 32, 2.4f);
            foreach (var x in res.cursed) text(x, "中毒", "#9be06a", 34, 2.6f);
            foreach (var h in res.healed) text(h.unit, "+" + h.amount, "#73d98c", 40, 1.6f);
            string buffWord = res.kind == "fortify" ? "坚守" : res.kind == "command" ? "攻防提升" : res.kind == "rally" ? "攻击提升" : res.kind == "haste" ? "机动 +" + sp.Move : null;
            if (buffWord != null) foreach (var x in res.buffed) text(x, buffWord, res.kind == "fortify" ? "#8cc8ff" : "#f3c969", 32, 2.3f);
            if (res.kind == "rally") foreach (var x in res.affected) if (!res.buffed.Contains(x) && x.side == u.side) text(x, "士气高涨", "#73d98c", 32, 2.3f);
            foreach (var x in res.refreshed) text(x, "再动！", "#7affd9", 38, 2.6f);
            if (res.gained > 0) text(u, "+" + res.gained + " 收编", "#73d98c", 36, 2.4f);
            if (res.blocked != null && res.blocked.alive) text(res.blocked, "撞击", "#ffb04a", 30, 2.5f);
            V.SpLabelsDim = false;
            if (rig != null && spRigBack >= 0) { rig.DesiredDistance = spRigBack; spRigBack = -1; }
            // 刷新
            foreach (var x in M.units) V.Refresh(x);
            UpdateHud();
            ShowCard(u.alive ? u : null);
            if (res.refreshed.Count > 0) UIKit.Toast(string.Join("、", res.refreshed.Select(x => x.gen.name).ToArray()) + " 可再次行动", 1.8f);
            if (res.killed != null) UIKit.Toast(res.killed.gen.name + "遭刺杀，全军溃散！", 2);
            yield return new WaitForSeconds(0.4f);
            // 溃散
            var gone = new List<BUnit>();
            foreach (var x in res.routed.Concat(res.affected)) if (x != null && !x.alive && !gone.Contains(x)) gone.Add(x);
            foreach (var x in gone)
            {
                if (!V.IsShown(x)) continue;
                if (res.killed == null || x != res.killed) UIKit.Toast(x.gen.name + "部溃散！");
                yield return V.Rout(x);
            }
        }

        // ---------------------------------------------------------- 单挑 --
        // 是否进入格斗画面：玩家参与、未委任、未关闭（DuelGame.Enabled）。电脑对电脑 / 委任时照旧 Duel()
        bool ShouldPlayDuel(BUnit a, BUnit b)
        {
            if (!DuelGame.Enabled || a == null || b == null) return false;
            if (playerSide != 0 && playerSide != 1) return false;
            if (autoPlayer) return false;
            return a.side == playerSide || b.side == playerSide;
        }
        Color FactionColorOf(BUnit u)
        {
            var g = G;
            int idx = M.setup != null ? (u.side == 0 ? M.setup.attacker : M.setup.defender) : -1;
            if (g != null && idx >= 0 && idx < g.factions.Count) return g.factions[idx].Col;
            return SideColor(u.side);
        }

        // 单挑：玩家一方参与且未委任时进入格斗画面（应战判定 → 格斗 → DuelFinish → 溃散）；
        // 电脑对电脑 / 委任时照旧 Duel() + 单挑对话框。格斗流程出错时退回简易单挑 + 旧对话框。
        IEnumerator DoDuel(BUnit a, BUnit b)
        {
            bool fight = false;
            try { fight = ShouldPlayDuel(a, b); } catch (Exception e) { Debug.LogException(e); }
            if (fight)
            {
                bool failed = false;
                yield return Co.Guard(PerformDuel(a, b), e => { Debug.LogException(e); failed = true; });
                foreach (var x in M.units) V.Refresh(x);
                UpdateHud();
                if (!failed) yield break;
                if (!a.alive || !b.alive || M.result != 0)
                {   // 已结算：只补溃散
                    foreach (var x in new[] { a, b }) if (!x.alive && V.IsShown(x)) yield return V.Rout(x);
                    yield break;
                }
                UIKit.Toast("单挑画面出错，改为简易单挑", 1.6f);
                yield return DuelDialog(a, b, ScriptedDuel(a, b), false);
                yield break;
            }
            var res = M.Duel(a, b);
            if (!res.accepted)
            {
                yield return UIKit.Say(b.gen.name + "：哼，匹夫之勇，不足与战！\n（" + b.gen.name + "拒绝单挑，其部士气下降）", b.gen.name, SideColor(b.side));
                foreach (var x in M.units) V.Refresh(x);
                yield break;
            }
            yield return DuelDialog(a, b, res, true);
        }

        // 网页版 DuelGame.perform：应战判定（DuelAccepts）→ 挑战台词 → 格斗画面 → DuelFinish → 刷新部队 → 败者溃散
        IEnumerator PerformDuel(BUnit a, BUnit b)
        {
            if (!M.DuelAccepts(a, b))
            {
                yield return UIKit.Say(b.gen.name + "：哼，匹夫之勇，不足与战！\n（" + b.gen.name + "拒绝单挑，其部士气下降）", b.gen.name, SideColor(b.side));
                foreach (var x in M.units) V.Refresh(x);
                UpdateHud();
                yield break;
            }
            yield return UIKit.Say(a.gen.name + "：" + b.gen.name + "，可敢与我一战？", a.gen.name, SideColor(a.side));
            var terrain = M.map[b.x, b.y];
            DuelGame.DuelResult res = null;
            DuelGame.MusicAfter = battleMusic;          // 单挑结束后回到战斗音乐
            yield return Co.Guard(DuelGame.Play(a.gen, a.side, FactionColorOf(a), b.gen, b.side, FactionColorOf(b), a.side == playerSide ? 0 : 1, terrain, r => res = r),
                e => Debug.LogException(e));
            DuelGame.MusicAfter = null;
            if (res == null)
            {   // 格斗画面出错：改用无画面模拟（同一套规则）分出胜负，保证士气与溃散照常结算
                try { res = DuelGame.Simulate(a.gen, b.gen); }
                catch (Exception e) { Debug.LogException(e); res = ScriptedDuelResult(a, b); }
                try { Sfx.Music(battleMusic); } catch (Exception) { /* 无音频 */ }
            }
            M.DuelFinish(a, b, res.winner == 0, res.rounds);
            var loser = res.winner == 0 ? b : a;
            // 败者的名牌留到溃散动画时再消失（与旧流程一致）
            var lv = V.VisOf(loser);
            if (lv != null) lv.holdLabel = true;
            foreach (var x in M.units) V.Refresh(x);
            if (lv != null) lv.holdLabel = false;
            UpdateHud();
            UIKit.Toast((res.winner == 0 ? a : b).gen.name + "于单挑中击败了" + loser.gen.name + "！", 2);
            yield return V.Rout(loser);
        }

        // 第一版的回合制单挑回合（不做应战判定；规则随机数同第一版 Duel 的回合部分）
        List<DuelRound> ScriptedRounds(BUnit a, BUnit b, out bool winnerIsA, out int hpAOut, out int hpBOut)
        {
            int hpA = 100, hpB = 100;
            var rounds = new List<DuelRound>();
            for (int i = 0; i < 30 && hpA > 0 && hpB > 0; i++)
            {
                int who = UnityEngine.Random.value < (double)a.gen.war / (a.gen.war + b.gen.war) ? 0 : 1;
                var striker = who == 0 ? a : b;
                int dmg = Mathf.RoundToInt(UnityEngine.Random.Range(6, 16) * (0.6f + striker.gen.war / 120f));
                if (who == 0) hpB = Mathf.Max(0, hpB - dmg); else hpA = Mathf.Max(0, hpA - dmg);
                rounds.Add(new DuelRound { who = who, dmg = dmg, hpA = hpA, hpB = hpB });
            }
            winnerIsA = hpA >= hpB; hpAOut = hpA; hpBOut = hpB;
            return rounds;
        }
        // 格斗画面失败时的后备：回合制单挑并结算后果
        DuelResult ScriptedDuel(BUnit a, BUnit b)
        {
            bool aw; int ha, hb;
            var rounds = ScriptedRounds(a, b, out aw, out ha, out hb);
            return M.DuelFinish(a, b, aw, rounds);
        }
        DuelGame.DuelResult ScriptedDuelResult(BUnit a, BUnit b)
        {
            bool aw; int ha, hb;
            var rounds = ScriptedRounds(a, b, out aw, out ha, out hb);
            return new DuelGame.DuelResult { winner = aw ? 0 : 1, kind = "time", hpA = ha, hpB = hb, rounds = rounds, log = new List<string>() };
        }

        // 旧单挑对话框：按 res.rounds 播放（胜负已由模型结算），最后败者溃散
        IEnumerator DuelDialog(BUnit a, BUnit b, DuelResult res, bool challenge)
        {
            var winner = res.winner == 0 ? a : b; var loser = res.winner == 0 ? b : a;
            // Duel 已判定胜负（败者 alive = false）；单挑画面播完前保留败者头顶标签，免得提前泄露结果
            var lv = V.VisOf(loser);
            if (lv != null) lv.holdLabel = true;
            UIKit.Modal m = null;
            try
            {
                if (challenge) yield return UIKit.Say(a.gen.name + "：" + b.gen.name + "，可敢与我一战？", a.gen.name, SideColor(a.side));
                // 单挑画面
                m = UIKit.OpenModal(980, 420);
                var title = UIKit.Label(m.panel, "单　挑", 52, UIKit.Gold, TextAnchor.MiddleCenter, true);
                UIKit.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(400, 70));
                var groups = new Dictionary<BUnit, CanvasGroup>();
                Func<BUnit, float, RectTransform> side = (u, x) =>
                {
                    var col = UIKit.NewRect("Side", m.panel);
                    UIKit.Stretch(col);
                    groups[u] = col.gameObject.AddComponent<CanvasGroup>();
                    // 头像（第二版）：与单挑画面、切入特写同一套肖像；生成失败时退回单字徽章
                    Image pic = null;
                    try
                    {
                        var spr = Portrait.Sprite(u.gen, 160, SideColor(u.side), PortraitMood.Angry, true, u == b);
                        pic = UIKit.Img(col, spr, Color.white, "Portrait");
                        pic.preserveAspect = true; pic.raycastTarget = false;
                    }
                    catch (Exception e) { Debug.LogException(e); pic = null; }
                    var medal = pic != null ? pic : UIKit.Medal(col, u.gen.name.Substring(0, 1), SideColor(u.side), 130);
                    UIKit.Place(medal.rectTransform, new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(130, 130));
                    var n = UIKit.Label(col, u.gen.name + "　武力 " + u.gen.war, 26, UIKit.Text, TextAnchor.MiddleCenter, true);
                    UIKit.Place(n.rectTransform, new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(380, 40));
                    var bar = UIKit.Bar(col, 1, SideColor(u.side));
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
                CanvasGroup lg;
                if (groups.TryGetValue(loser, out lg) && lg != null) lg.alpha = 0.55f;
                log.text = string.Format("<color=#f3c969>{0}</color>击败了{1}！", winner.gen.name, loser.gen.name);
                Sfx.Play("win", 0.5f);
                yield return new WaitForSeconds(1.4f);
            }
            finally
            {
                if (m != null) m.Close();
                foreach (var x in M.units) V.Refresh(x);
                if (lv != null) lv.holdLabel = false;          // 单挑结束：标签随溃散动画一起消失
            }
            yield return V.Rout(loser);
        }

        // ---------------------------------------------------------- 电脑 --
        IEnumerator AiPhase()
        {
            yield return new WaitForSeconds(0.3f);
            var order = M.Alive(M.side).Where(M.CanAct).OrderBy(u => u.commander ? 1 : 0)
                .ThenBy(u => M.Alive(1 - M.side).Select(e => BattleModel.Dist(e, u)).DefaultIfEmpty(99).Min()).ToList();
            for (int oi = 0; oi < order.Count; oi++)
            {
                var u = order[oi];
                if (M.result != 0 || M.ap <= 0) break;
                if (!M.CanAct(u)) continue;
                if (endTurn && playerSide >= 0 && M.side == playerSide && !autoPlayer) break;
                var plan = M.PlanFor(u);
                Game.I.Rig.Focus(V.Tile(u.x, u.y));
                aiMovedCam = true;
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
                else if (act.kind == "special" && SpecialsOn())
                {
                    var rr = new Ref<SpecialResult>();
                    yield return DoSpecial(u, act.target, true, rr, act.special != null ? act.special.id : null);
                    if (rr.v != null) foreach (var x in rr.v.refreshed) order.Add(x);   // 疾行：再动的友军排到队尾（CanAct 防止重复行动）
                }
                M.Spend(u);
                UpdateHud();
                yield return new WaitForSeconds(0.2f);
            }
            V.SetCursor(null); ShowCard(null);
        }
    }
}
