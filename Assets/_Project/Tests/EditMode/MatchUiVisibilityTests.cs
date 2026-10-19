using System;
using System.Collections.Generic;
using Billiards.Physics;
using Billiards.Rules;
using Billiards.Table;
using Billiards.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **`T161`：三条"界面不许假陈述 / 不许静默"缺陷的「玩家看得见 / 生效了没有」层判据**。
    ///
    /// ── 为什么单独一个文件、为什么不并进既有判据 ────────────────────────────────
    /// 三条缺陷的**源码形态**已由 `T160`（`PhysicsTestDriveLaunchStateTests.cs`）守住：
    /// 常量已删、赋值来源含 `drive.MatchTier`、`R` 分支带对手门、`RequestReRack` 单一调用点。
    /// 但那三条**都是"代码长什么样"，不是"玩家看得到什么"**：
    /// · `T144③` 断的是**纯函数** `CaptionLine(..., opponentBlocked:true)` 的返回值 ⇒ 文案对、控件被隐藏照样绿；
    /// · `T146(h)` 断的是**源码喂参** `bool aiTurn = drive != null && drive.IsAiTurn;` ⇒ 喂对 ≠ 看得见；
    /// · 缺陷① 在 `T160` 之前**没有任何判据**（断 `OpponentDisplayName` 常量 = 与 bug 同源 = 恒真）。
    /// ⇒ 本文件只补**运行期/可见性**这一层，与 `T160` 互补、不替代。规格见
    ///   `Docs/Reports/35-C_三条缺陷验收标准.md`（§2/§3/§4 与本文件的 `(a)/(b)/(c)` 一一对应）。
    ///
    /// ── ⚠ 编译依赖（**这是刻意的，不许静默降级**）────────────────────────────────
    /// 本文件直接引用 P37 要落的 5 个**只读出口/接缝**（名字已由主理人冻结，见规格 §1）：
    /// · **E1** `PracticeHudView.VisibleStatusText`（`string`，可 `null`：不可见 ⇒ `null`，可见但空 ⇒ `""`）
    /// · **E3** `PracticeHudView.OpponentNameText`（`string`，可 `null`）
    /// · **E2** `PracticeResultView.ShowCount`（`int`，单调不减，`Present` 每执行一次 +1）
    /// · **S1** `PracticeResultView.TryRequestReRack()`（`bool`，门在方法内）
    /// · **E4** `PracticeResultView.TickSessionForJudge()`（跑一次 `Update` 里"会话观察→判定→展示"那段）
    /// ⇒ **这 5 个成员没落齐之前，本程序集编不过**。这是有意的：宁可编不过，也不要写一个
    ///   "永远绿"的假判据（`Show()` 只在 `Update` 里被调，EditMode 不跑 `Update`，
    ///   没有 E4 的话 `ShowCount`/`IsOpen` 在 EditMode 里永不变化 ⇒ 断言恒真）。
    ///
    /// ── 三组断言（对应实机清单 C2 / C4 / C15）──────────────────────────────────
    /// · **(a)** 对战 HUD 右栏对手名 = **所选档位**，且**换档后不滞留上一档**；练习形态为 `null`；
    /// · **(b)** 对战里"对手回合被拒"的提示**真的可见**（`VisibleStatusText`），且**不等于**常驻说明；
    /// · **(c)** 对战里按 `R` **不生效**：不开卡、`ShowCount` 不变、`MatchOver` 不变、**球心逐位不变**、
    ///   组别/回合不变；且**退出对战后再按 `R` 仍然有效**（防"一刀切禁掉"）。
    ///
    /// ── 本文件**不能**证明（如实登记，别当已覆盖）────────────────────────────────
    /// 1. "看着舒服"（提示挡不挡球杆、字号、配色可辨）⇒ 实机 C4；
    /// 2. "提示约 1.6 s 后消失"⇒ `Time.unscaledTime` 在 EditMode 不前进，故意不为此引入可注入时钟 ⇒ 实机 C4；
    /// 3. "点击那一帧就出现"（帧内时序）⇒ EditMode 无帧概念 ⇒ 实机；
    /// 4. 对战里是否还有**别的**能重置对局的入口 ⇒ 本判据只覆盖 `R`（本缺陷的实际入口）。
    /// </summary>
    public class MatchUiVisibilityTests
    {
        /// <summary>驱动夹具用的球台资产（与 `T146` 同一份，避免两处各写一个路径）。</summary>
        private const string TableConfigPath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>空球号列表（`ApplyMatchHud` 的入参；开放球局两侧都空）。</summary>
        private static readonly int[] NoNumbers = Array.Empty<int>();

        // ═══════════════════════════════════════════════════════════════════════
        //  T161 主用例
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **`T161`：三条缺陷的"可见 / 生效"层**（`(a)` 对手名按档位 · `(b)` 被拒提示真可见 · `(c)` 对战 `R` 不生效）。
        ///
        /// 失败名固定为 `T161_UiWiring_VisibleToPlayer_AndReRackGatedInMatch`；
        /// 子号 `(a1)…(c7)` 与规格文档逐条对应。**只收集失败、末尾统一断言**（沿用 `T125`/`T144`/`T146` 范式），
        /// 这样一次跑批能看到"三条缺陷各自还差什么"，而不是修一条跑一次。
        /// </summary>
        [Test]
        public void T161_UiWiring_VisibleToPlayer_AndReRackGatedInMatch()
        {
            var failures = new List<string>();

            GameObject hudGo = null;
            GameObject driveGo = null;
            GameObject resultGo = null;
            MatchTurnNotice.Reset();

            try
            {
                // ── 夹具（照 `ClubUiTests.cs:1619-1632` 的 drive 夹具 + `:986-990` 的 HUD 探针）──
                TableConfig config = AssetDatabase.LoadAssetAtPath<TableConfig>(TableConfigPath);
                if (config == null)
                {
                    // 夹具缺失 ⇒ 判据失效，必须当场红（不许"跳过"混过去）
                    Assert.Fail($"T161 载不到 {TableConfigPath} ⇒ 夹具无法建立，判据失效。");
                }

                driveGo = new GameObject("T161Drive");
                var drive = driveGo.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = config;
                drive.Initialize();

                // ⚠ `AddComponent` 在 EditMode 不触发 `Awake` ⇒ 必须显式 `EnsureBuilt`
                hudGo = new GameObject("T161Hud");
                var hud = hudGo.AddComponent<PracticeHudView>();
                hud.EnsureBuilt();
                hud.drive = drive;

                // ⚠ `PracticeResultView.EnsureBuilt()` 在 `hud == null` 时会**先把 `_built` 置真再早退**
                //   ⇒ `hud` 必须先赋好，否则后面再也建不出来（本仓踩过同类坑）
                resultGo = new GameObject("T161Result");
                var result = resultGo.AddComponent<PracticeResultView>();
                result.drive = drive;
                result.hud = hud;
                result.EnsureBuilt();
                result.Hide();

                // 结算卡必须关着：`OpponentNoticeAllowed(IsOpen)` 在开卡时会**丢弃**被拒提示
                // （`PracticeHudView.cs:950-954`）⇒ 关卡是 (b) 的前置条件，漏了会得到假红/假绿
                if (PracticeResultView.IsOpen)
                {
                    failures.Add("T161 前置：结算卡必须是关着的（`PracticeResultView.Hide()` 之后 `IsOpen` 仍为 true）——"
                                 + "否则被拒提示会被 `OpponentNoticeAllowed` 丢弃，(b) 全部断言失效。");
                }

                CheckOpponentNameFollowsTier(hud, drive, failures);
                CheckOpponentTurnNoticeVisible(hud, drive, failures);
                CheckReRackGatedInMatch(result, drive, failures);
            }
            finally
            {
                // 兜底：静态状态不许漏到别的用例（`IsOpen` / `MatchTurnNotice` 都是静态的）
                MatchTurnNotice.Reset();
                if (PracticeResultView.IsOpen)
                {
                    var anyResult = UnityEngine.Object.FindObjectOfType<PracticeResultView>();
                    if (anyResult != null)
                    {
                        anyResult.Hide();
                    }
                }

                DestroyImmediateSafe(resultGo);
                DestroyImmediateSafe(hudGo);
                DestroyImmediateSafe(driveGo);
            }

            Assert.IsEmpty(failures,
                "T161（对手名按档位 / 被拒提示真可见 / 对战 R 不生效）失败：\n" + string.Join("\n", failures));
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  (a) 对手名 = 所选档位（缺陷①）—— 对应实机 C2
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **(a) 对战 HUD 右栏对手名必须等于 `drive.MatchTier` 对应的档位名。**
        ///
        /// 四条断言：
        /// · **(a1)** 三档逐一：`OpponentNameText == PracticeHudState.OpponentName(tier)`；
        /// · **(a2)** 三档读值**两两不等**（把"恒等于某一档"单独钉死——只断 (a1) 可能在"比较对象写歪"时仍绿）；
        /// · **(a2b) 滞留检查**：**同一个 hud 实例不重建**，`Club` → 读 → `Expert` → 读，两次必须不同
        ///   （`MatchHudSignature` 少把档位算进去，名字就会停在上一个档位——同一类假陈述）；
        /// · **(a3)** 练习形态（`aiMatch == false`）⇒ `null`（对手块不可见时**不该有值**）。
        ///
        /// ⚠ 源码形态（`_oppName.text =` 的赋值来源含 `OpponentName(` + `drive.MatchTier`）由 `T160(a2)` 守，
        ///   本文件**不重复**——这里只断运行期读到的显示值。
        /// </summary>
        private static void CheckOpponentNameFollowsTier(PracticeHudView hud, PhysicsTestDrive drive,
                                                         List<string> failures)
        {
            var tiers = new[]
            {
                AiDifficulty.Casual,
                AiDifficulty.Club,
                AiDifficulty.Expert,
            };

            var shown = new Dictionary<AiDifficulty, string>();
            foreach (AiDifficulty tier in tiers)
            {
                drive.StartMatch(tier);
                hud.ApplyMatchHud(true, GroupState.OpenTable, GroupState.OpenTable, false, NoNumbers, NoNumbers);

                string actual = hud.OpponentNameText;
                string expected = PracticeHudState.OpponentName(tier);
                shown[tier] = actual;

                if (actual != expected)
                {
                    failures.Add($"(a1) {tier} 档的对战 HUD 右栏对手名应为 \"{expected}\"，"
                                 + $"实际 \"{actual ?? "<null>"}\"。"
                                 + "（写死常量会让三档都显示同一个名字；控件不可见会让它是 null。）");
                }
            }

            // (a2) 三档两两不等
            if (shown[AiDifficulty.Casual] == shown[AiDifficulty.Club]
                || shown[AiDifficulty.Club] == shown[AiDifficulty.Expert]
                || shown[AiDifficulty.Casual] == shown[AiDifficulty.Expert])
            {
                failures.Add("(a2) 三档对手名必须两两不等，实测 Casual="
                             + $"\"{shown[AiDifficulty.Casual] ?? "<null>"}\"、"
                             + $"Club=\"{shown[AiDifficulty.Club] ?? "<null>"}\"、"
                             + $"Expert=\"{shown[AiDifficulty.Expert] ?? "<null>"}\"。"
                             + "（相等 ⇒ 名字没按档位取，或指纹没含档位导致滞留。）");
            }

            // (a2b) 滞留检查：同一实例、只换档位（不重建 hud）
            drive.StartMatch(AiDifficulty.Club);
            hud.ApplyMatchHud(true, GroupState.OpenTable, GroupState.OpenTable, false, NoNumbers, NoNumbers);
            string clubText = hud.OpponentNameText;

            drive.StartMatch(AiDifficulty.Expert);
            hud.ApplyMatchHud(true, GroupState.OpenTable, GroupState.OpenTable, false, NoNumbers, NoNumbers);
            string expertText = hud.OpponentNameText;

            if (clubText == expertText)
            {
                failures.Add("(a2b) 同一 HUD 实例从 Club 换到 Expert 后，对手名必须变化，"
                             + $"实测两次都是 \"{clubText ?? "<null>"}\" ⇒ "
                             + "名字滞留在上一档（`MatchHudSignature` 没把 `drive.MatchTier` 算进指纹）。");
            }

            // (a3) 练习形态：对手块不可见 ⇒ 显示值为 null
            hud.ApplyMatchHud(false, GroupState.OpenTable, GroupState.OpenTable, false, NoNumbers, NoNumbers);
            if (hud.OpponentNameText != null)
            {
                failures.Add("(a3) 练习形态下对手块不可见，`OpponentNameText` 应为 null，"
                             + $"实际 \"{hud.OpponentNameText}\" ⇒ 练习里仍在对战形态显示对手名。");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  (b) 被拒提示必须真的显示出来（缺陷②）—— 对应实机 C4
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **(b) 对战里"对手回合被拒"的提示必须**可见**，且不等于常驻说明。**
        ///
        /// · **(b1)** `VisibleStatusText == PracticeHudState.OpponentBlockedDetail`（逐字）；
        /// · **(b2)** 该值 **不得等于** `PracticeHudState.AiTurnDetail`（常驻说明 = "没有新提示" = 静默）；
        /// · **(b3)** 该值 **不得为 `null`**（`null` 的定义 = 没有任何状态文本控件可见 ⇒ 静默）；
        /// · **(b4)** 练习形态同样要能看到（防"修对战把练习弄坏"）。
        ///
        /// ⚠ **本判据不断"1.6 s 后消失"**：窗口用 `Time.unscaledTime`（`PracticeHudView.cs:953`），
        ///   EditMode 里它不随等待前进 ⇒ 交给实机 C4。也**故意不**为此引入可注入时钟（少一个活门）。
        /// ⚠ **注入自检（B5，必须人工做一次）**：把 E1 临时改成"直接返回 `_stateCaption.text`、不看可见性"
        ///   ⇒ (b3) 必须红。不做这条，出口自己可能被写成恒真，"看得见"就永远测不出来。
        /// </summary>
        private static void CheckOpponentTurnNoticeVisible(PracticeHudView hud, PhysicsTestDrive drive,
                                                          List<string> failures)
        {
            // 前置：对战形态 + 结算卡关着（外面已 `Hide()`，这里只确认）
            drive.StartMatch(AiDifficulty.Club);

            // ⚠ **必须把回合摆到 AI**：对战状态行只在"状态 ≠ Aim"时才显示
            //   （`showStatus = aiMatch && status != StateAim`）。若回合是玩家，`_shotInfoValue.text == "Aim"`
            //   ⇒ 状态行整块隐藏 ⇒ `VisibleStatusText` 会落到被隐藏的说明行 ⇒ 得到**假红**。
            drive.SetTurn(ShotTurn.Ai);

            // ① 先让 `Refresh()` 跑一帧：消费 `RequestBlock`、开提示窗口、写 `_shotInfoValue.text`（= 状态标题）
            MatchTurnNotice.Reset();
            MatchTurnNotice.RequestBlock(MatchTurnNotice.ReasonOpponentTurn);
            hud.Refresh();

            // ② 再显式按**对战形态**写一次 HUD。
            //   为什么不能只靠 `Refresh()`：EditMode 里没有 `HomeView`，`HomeView.IsAiMatchSession` 是 false
            //   ⇒ `RefreshGroupChips()` 会按**练习**形态调 `ApplyMatchHud`（对战状态行被隐藏）。
            //   这里补这一次调用，等价于"真的进了对战"，而 `_opponentBlockedWindow` 已由 ① 置真
            //   ⇒ 状态行应当写出被拒提示而不是常驻标题。
            hud.ApplyMatchHud(true, GroupState.OpenTable, GroupState.OpenTable, true, NoNumbers, NoNumbers);

            string visible = hud.VisibleStatusText;

            if (visible == null)
            {
                failures.Add("(b3) 对战里 AI 回合被拒后，**没有任何状态文本控件可见**（`VisibleStatusText == null`）"
                             + " ⇒ 玩家点了台面看不到任何提示 = **静默**（§5.1-12 / D7 明令禁止）。");
            }
            else
            {
                if (visible != PracticeHudState.OpponentBlockedDetail)
                {
                    failures.Add($"(b1) 对战里 AI 回合被拒后的可见文本应为 "
                                 + $"\"{PracticeHudState.OpponentBlockedDetail}\"，实际 \"{visible}\"。");
                }

                if (visible == PracticeHudState.AiTurnDetail)
                {
                    failures.Add($"(b2) 被拒提示不得等于常驻说明 \"{PracticeHudState.AiTurnDetail}\""
                                 + " —— 那等于\"点了没有任何新反馈\"（静默）。");
                }
            }

            // (b4) 练习形态同样可见（`AI-turn` 在练习里不可达，但被拒提示的**可见性通道**必须通）
            hud.ApplyMatchHud(false, GroupState.OpenTable, GroupState.OpenTable, false, NoNumbers, NoNumbers);
            MatchTurnNotice.Reset();
            MatchTurnNotice.RequestBlock(MatchTurnNotice.ReasonOpponentTurn);
            hud.Refresh();

            string practiceVisible = hud.VisibleStatusText;
            if (practiceVisible != PracticeHudState.OpponentBlockedDetail)
            {
                failures.Add("(b4) 练习形态下被拒提示也必须可见（同一句文案），实际 "
                             + $"\"{practiceVisible ?? "<null>"}\" ⇒ 修对战形态时把练习形态弄坏了。");
            }

            MatchTurnNotice.Reset();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  (c) 对战里按 R 不得生效（缺陷③）—— 对应实机 C15
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **(c) 对战里"按 R"必须完全无效，且状态逐位不变；离开对战后 R 仍有效。**
        ///
        /// 为什么不能只断 `IsOpen`：`Show()` 只在 `Update()` 里被调，而 **EditMode 不跑 `Update`**
        /// ⇒ 没有 **E4**（`TickSessionForJudge()`）时 `ShowCount`/`IsOpen` 永不变化、断言恒真。
        /// 有了 E4，删掉门的负对照（INJ）会让这一次 tick 立刻 `Show()` ⇒ `(c2)/(c3)` 红。
        ///
        /// 断言：
        /// · **(c1)** `TryRequestReRack()` 返回 `false`（门生效）；
        /// · **(c2)** tick 后 `IsOpen == false`；
        /// · **(c3)** tick 后 `ShowCount` 不变（含"开了又关"这种现场自愈的情形）；
        /// · **(c4)** `MatchOver` 不变（不得结算）；
        /// · **(c5)** 全部球心**逐位不变**（含白球）；
        /// · **(c6)** `PlayerGroup` / `CurrentTurn` 不变；
        /// · **(c7)** `EndAiMatchSession()` 后再按 `R` ⇒ 返回 `true` 且 `ShowCount + 1`（练习里仍然有效）。
        ///
        /// ⚠ **顺序纪律（写死在这里，别省）**：`StartMatch` → `Hide()` → **先 tick 一次**（起局）→ 取基线
        ///   → `TryRequestReRack()` → 再 tick。
        ///   省掉"先 tick 一次"，`(c2)(c3)` 会变成**哑巴**：`BeginRack()` 会清 `_reRackRequested`，
        ///   请求被静默吞掉 ⇒ 删门也看不到 `Show()`（工程侧 `INJ-161c` 实测：只红 `(c1)`）。
        /// ⚠ **诚实边界**：`(c5)`（球心）与 `(c6)`（组别/回合）在本路径上是**廉价不变式**——
        ///   删门也不会触发 `ReloadRack()`（那要 `OnPlayAgain`/`StartNewRack` 这条链），
        ///   所以它们的牙主要来自 `(c1)+(c3)`；保留它们是为了挡"将来有人把 `ReloadRack` 挪进 tick"。
        ///   真正的"第三层后果"（静默清零）由实机 **C15** 覆盖。
        /// </summary>
        private static void CheckReRackGatedInMatch(PracticeResultView result, PhysicsTestDrive drive,
                                                    List<string> failures)
        {
            drive.StartMatch(AiDifficulty.Club);
            result.Hide();

            // ⚠ **必须先让会话 tick 一次，再按 `R`**（否则 (c2)(c3) 是哑巴）：
            //   `PracticeSessionTracker.BeginRack()` 会把 `_reRackRequested` **清零**
            //   （PracticeSession.cs 的 `BeginRack` 实现），而首帧 tick 才会 `BeginRack`。
            //   ⇒ 若先 `TryRequestReRack()` 再第一次 tick，"重摆请求"会被 `BeginRack` **静默吞掉**
            //   ⇒ 即使把模式门删掉（`INJ-161c`），也不会 `Show()` ⇒ `ShowCount`/`IsOpen` 恒为基线
            //   ⇒ **(c2)(c3) 无牙**（工程侧实测：删门后只有 (c1) 红）。
            //   修法 = 先起局（run 一次 tick），再取基线，再按 R，再 tick。
            result.TickSessionForJudge();

            if (PracticeResultView.IsOpen)
            {
                failures.Add("(c) 夹具前置：首帧 tick 之后结算卡不应是打开的"
                             + "（否则下面的基线不可信，(c2)(c3) 全部失效）。");
            }

            int showCount0 = PracticeResultView.ShowCount;
            bool matchOver0 = drive.MatchOver;
            ShotTurn turn0 = drive.CurrentTurn;
            GroupState group0 = drive.PlayerGroup;
            Vector2[] rack0 = SnapshotBallPositions(drive);

            // ── 触发：按 R（走接缝，不模拟输入）──
            bool accepted = result.TryRequestReRack();
            result.TickSessionForJudge();

            if (accepted)
            {
                failures.Add("(c1) 对战里按 `R` 必须被拦（`TryRequestReRack()` 应返回 false），实际返回 true "
                             + "⇒ 练习的重摆键在对战里生效。");
            }

            if (PracticeResultView.IsOpen)
            {
                failures.Add("(c2) 对战里按 `R` 之后不得出现结算卡（`IsOpen` 仍应为 false），实际已是 true。");
            }

            if (PracticeResultView.ShowCount != showCount0)
            {
                failures.Add($"(c3) 对战里按 `R` 之后结算卡不得被打开过：`ShowCount` 基线 {showCount0}，"
                             + $"实际 {PracticeResultView.ShowCount}。"
                             + "（只断 `IsOpen` 断不到\"开了又关\"，故必须断次数。）");
            }

            if (drive.MatchOver != matchOver0)
            {
                failures.Add($"(c4) 对战里按 `R` 不得改变结算位：`MatchOver` 基线 {matchOver0}，"
                             + $"实际 {drive.MatchOver}。");
            }

            CheckRackUnchanged(drive, rack0, failures);
            CheckTurnAndGroupUnchanged(drive, turn0, group0, failures);

            // ── (c7) 反向不变：离开对战（= 练习）后 R 必须仍然有效 ──
            drive.EndAiMatchSession();
            if (drive.AiOpponentEnabled)
            {
                failures.Add("(c7) 前置：`EndAiMatchSession()` 之后 `AiOpponentEnabled` 应为 false"
                             + "（否则下面的\"练习里 R 仍有效\"断不出来）。");
            }

            bool acceptedInPractice = result.TryRequestReRack();
            result.TickSessionForJudge();

            if (!acceptedInPractice)
            {
                failures.Add("(c7) 练习模式里按 `R` 必须仍然有效（`TryRequestReRack()` 应返回 true），实际 false "
                             + "⇒ 修对战门时把练习的重摆键也一起禁掉了（P23/P27 语义被带坏）。");
            }

            if (PracticeResultView.ShowCount != showCount0 + 1)
            {
                failures.Add($"(c7) 练习模式里按 `R` 后应弹一次练习结算卡（`ShowCount` 期望 {showCount0 + 1}），"
                             + $"实际 {PracticeResultView.ShowCount}。"
                             + "（若为基线值 ⇒ 练习的重摆判定断了；若更大 ⇒ 同一帧被弹了多次。）");
            }

            result.Hide();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  小工具
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>全部球心快照（含白球，按球号）。`drive.Simulator.Balls` 是唯一只读出口。</summary>
        private static Vector2[] SnapshotBallPositions(PhysicsTestDrive drive)
        {
            IReadOnlyList<BallState> balls = drive.Simulator.Balls;
            var snapshot = new Vector2[balls.Count];
            for (int i = 0; i < balls.Count; i++)
            {
                snapshot[i] = balls[i].position;
            }

            return snapshot;
        }

        /// <summary>球心**逐位**比对（`Vector2` 精确相等；本工程的确定性口径不允许"近似"）。</summary>
        private static void CheckRackUnchanged(PhysicsTestDrive drive, Vector2[] baseline, List<string> failures)
        {
            IReadOnlyList<BallState> balls = drive.Simulator.Balls;
            if (balls.Count != baseline.Length)
            {
                failures.Add($"(c5) 球数变了：基线 {baseline.Length} 颗，现在 {balls.Count} 颗"
                             + " ⇒ 对战里按 `R` 把球台重摆了。");
                return;
            }

            int moved = 0;
            for (int i = 0; i < balls.Count; i++)
            {
                Vector2 now = balls[i].position;
                if (now.x != baseline[i].x || now.y != baseline[i].y)
                {
                    moved++;
                    if (moved <= 3)
                    {
                        failures.Add($"(c5) 对战里按 `R` 后 {(i == 0 ? "白球" : $"{i} 号球")} 位置发生变化："
                                     + $"({baseline[i].x:F6}, {baseline[i].y:F6}) → ({now.x:F6}, {now.y:F6})。");
                    }
                }
            }

            if (moved > 3)
            {
                failures.Add($"(c5) ……共 {moved} 颗球位置变化（只列前 3 处）⇒ 对战状态被静默重置。");
            }
        }

        /// <summary>组别与回合不变（两者都是"被 `ReloadRack()` 清掉"的状态）。</summary>
        private static void CheckTurnAndGroupUnchanged(PhysicsTestDrive drive, ShotTurn turn0, GroupState group0,
                                                       List<string> failures)
        {
            if (drive.CurrentTurn != turn0)
            {
                failures.Add($"(c6) 对战里按 `R` 不得改变回合：基线 {turn0}，实际 {drive.CurrentTurn}。");
            }

            if (drive.PlayerGroup != group0)
            {
                failures.Add($"(c6) 对战里按 `R` 不得改变组别：基线 {group0}，实际 {drive.PlayerGroup}。");
            }
        }

        /// <summary>安全的立即销毁（`finally` 里也要能跑：已销毁/为空都不抛）。</summary>
        private static void DestroyImmediateSafe(GameObject go)
        {
            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
