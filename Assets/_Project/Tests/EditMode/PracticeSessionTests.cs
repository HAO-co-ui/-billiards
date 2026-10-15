using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.Table;
using Billiards.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Billiards.Tests
{
    /// <summary>
    /// **练习结算与埋点** —— `T100`（统计与结算判定）/ `T101`（持久化与防御式读取）/ `T102`（结算卡布局与接线）。
    ///
    /// 期望值来源：**甲**（构造用例 + 参考实现的语义，与被测对象无关）＋ **乙**（文案/尺寸契约常量）。
    /// **盲带**：真实鼠标点按的端到端、卡片观感不被覆盖 —— 判据只锁几何矩形、文案常量与
    /// `EventSystem`/射线器在场；**点击是否真的响应需用户实机确认**。
    /// **注入负对照（三组，均实测红）**：`ResolveOutcome` 改成"重摆优先" ⇒ `T100 (a)` 红；
    /// `TryParse` 去掉 `mode` 校验 ⇒ `T101 (b)` 红；卡高改 `1200` 设计 px ⇒ `T102 (a)` 红。
    /// </summary>
    public class PracticeSessionTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";

        private static readonly (string Name, int Width, int Height)[] Aspects =
        {
            ("1280x720", 1280, 720),
            ("1920x1080", 1920, 1080),
            ("1920x1200", 1920, 1200),
            ("1024x768", 1024, 768),
            ("2560x1080", 2560, 1080),
            ("1024x1280", 1024, 1280),
        };

        private static TableConfig Table()
        {
            var table = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(table, "需要 " + TablePath);
            return table;
        }

        private static PhysicsParams FactoryParams()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "需要 " + TuningPath);
            return tuning.ToPhysicsParams();
        }

        // ════════════════ T100 统计与结算判定 ══════════════════════════════════

        [Test]
        public void T100_SessionStats_AndOutcome()
        {
            var failures = new List<string>();

            // (a) 结算判定：**清台优先于主动重摆**（语义不得含糊）
            CheckOutcome("(a) 全清台", 3, 3, false, RackOutcome.TableCleared);
            CheckOutcome("(a) 未清台", 2, 3, false, RackOutcome.None);
            CheckOutcome("(a) 主动重摆", 0, 3, true, RackOutcome.RackEnded);
            CheckOutcome("(a) 清台且请求重摆", 3, 3, true, RackOutcome.TableCleared);
            CheckOutcome("(a) 空球台不得判清台", 0, 0, false, RackOutcome.None);

            void CheckOutcome(string label, int pocketed, int total, bool reRack, RackOutcome expected)
            {
                RackOutcome actual = PracticeSession.ResolveOutcome(pocketed, total, reRack);
                if (actual != expected)
                {
                    failures.Add($"{label}：应 {expected}，实际 {actual}。");
                }
            }

            // (b) 文案（`None` ⇒ 空串，不得给误导性标题）
            CheckText("(b) cleared 标题", PracticeSession.OutcomeTitle(RackOutcome.TableCleared), PracticeSession.TitleTableCleared);
            CheckText("(b) ended 标题", PracticeSession.OutcomeTitle(RackOutcome.RackEnded), PracticeSession.TitleRackEnded);
            CheckText("(b) none 标题为空", PracticeSession.OutcomeTitle(RackOutcome.None), string.Empty);
            CheckText("(b) cleared 原因", PracticeSession.OutcomeReason(RackOutcome.TableCleared), PracticeSession.ReasonTableCleared);
            CheckText("(b) ended 原因", PracticeSession.OutcomeReason(RackOutcome.RackEnded), PracticeSession.ReasonRackEnded);

            void CheckText(string label, string actual, string expected)
            {
                if (actual != expected)
                {
                    failures.Add($"{label}：应 \"{expected}\"，实际 \"{actual}\"。");
                }
            }

            // (c) 进球率：除零不得产生 NaN / Inf
            CheckRate("(c) 0/0", PracticeSession.PocketRate(0, 0), 0f);
            CheckRate("(c) 3/6", PracticeSession.PocketRate(3, 6), 0.5f);
            CheckRate("(c) 3/0", PracticeSession.PocketRate(3, 0), 0f);

            void CheckRate(string label, float actual, float expected)
            {
                if (float.IsNaN(actual) || float.IsInfinity(actual) || Mathf.Abs(actual - expected) > 1e-6f)
                {
                    failures.Add($"{label}：应 {expected:R}，实际 {actual:R}。");
                }
            }

            // (d) 累积器：杆数用**累计值减基线**；scratch 只在**新杆**计一次；时长单调不减
            var tracker = new PracticeSessionTracker();
            tracker.BeginRack(100);                      // 假设进场景前已经打过 100 杆（跨局单调）
            tracker.Observe(100, 0, 3, false, 10.0f);    // 起局帧
            tracker.Observe(101, 0, 3, true, 11.0f);     // 第 1 杆：白球落袋 ⇒ fouls=1
            tracker.Observe(101, 0, 3, true, 11.5f);     // 同杆重复观测：**不得**再计
            tracker.Observe(102, 1, 3, true, 13.0f);     // 第 2 杆：又落袋 ⇒ fouls=2
            tracker.Observe(103, 1, 3, false, 14.0f);    // 第 3 杆
            PracticeSessionSnapshot snapshot = tracker.Snapshot();

            if (snapshot.shots != 3)
            {
                failures.Add($"(d) 杆数应为 3（= 103 − 基线 100），实际 {snapshot.shots}。");
            }

            if (snapshot.pots != 1)
            {
                failures.Add($"(d) 进球数应为 1，实际 {snapshot.pots}。");
            }

            if (snapshot.fouls != 2)
            {
                failures.Add($"(d) scratch 应为 2（同一杆内不重复计），实际 {snapshot.fouls}。");
            }

            if (Mathf.Abs(snapshot.elapsedMs - 4000f) > 1f)
            {
                failures.Add($"(d) 本局时长应为 4000 ms（10.0 → 14.0 s），实际 {snapshot.elapsedMs:R}。");
            }

            // 时长只应累加正增量（回拨的时钟不得把时长减小）
            float before = tracker.Snapshot().elapsedMs;
            tracker.Observe(103, 1, 3, false, 9.0f);
            if (tracker.Snapshot().elapsedMs < before)
            {
                failures.Add("(d) 时钟回拨时时长不得减小。");
            }

            // (e) 结算与计数：主动重摆 ⇒ RackEnded + reracks=1 + racks=1
            var racing = new PracticeSessionTracker();
            racing.BeginRack(0);
            racing.Observe(0, 0, 3, false, 1f);
            racing.RequestReRack();
            racing.Observe(0, 0, 3, false, 2f);
            PracticeSessionSnapshot reRacked = racing.Snapshot();

            if (reRacked.outcome != RackOutcome.RackEnded || reRacked.reracks != 1 || reRacked.racks != 1)
            {
                failures.Add($"(e) 主动重摆应 outcome=RackEnded / reracks=1 / racks=1，"
                             + $"实际 {reRacked.outcome} / {reRacked.reracks} / {reRacked.racks}。");
            }

            racing.NotePlayAgain();
            if (racing.Snapshot().playAgain != 1)
            {
                failures.Add($"(e) `NotePlayAgain` 后 playAgain 应为 1，实际 {racing.Snapshot().playAgain}。");
            }

            // (f) `CountBalls` 口径：0 号是白球、**不计入目标球**
            var simulator = new PoolSimulator(Table(), FactoryParams());
            var balls = new List<BallState>();
            for (int i = 0; i < 4; i++)
            {
                balls.Add(new BallState { position = new Vector2(-0.3f + i * 0.2f, 0f), velocity = Vector2.zero });
            }

            simulator.LoadBalls(balls);
            PracticeSession.CountBalls(simulator, out int pocketed, out int total);
            if (total != 3 || pocketed != 0)
            {
                failures.Add($"(f) 4 球（含白球）应报 total=3 / pocketed=0，实际 {total} / {pocketed}。");
            }

            PracticeSession.CountBalls(null, out int nullPocketed, out int nullTotal);
            if (nullTotal != 0 || nullPocketed != 0)
            {
                failures.Add($"(f) 空内核应报 0 / 0，实际 {nullTotal} / {nullPocketed}。");
            }

            Assert.IsEmpty(failures, "T100 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T100: 判定 5 例 / 文案 5 例 / 进球率 3 例 / 累积器 ✓ / CountBalls 口径 ✓");
        }

        // ════════════════ T101 持久化与防御式读取 ══════════════════════════════

        [Test]
        public void T101_SessionStore_RoundTripAndDefensiveRead()
        {
            var failures = new List<string>();

            // (a) 往返：逐字段相等
            var snapshot = new PracticeSessionSnapshot
            {
                shots = 6, pots = 3, fouls = 1, racks = 2, reracks = 1, playAgain = 1,
                elapsedMs = 12345.5f, outcome = RackOutcome.TableCleared,
            };
            PracticeSessionRecord record = PracticeSessionStore.NewRecord(snapshot);
            string json = PracticeSessionStore.ToJson(record);

            if (!PracticeSessionStore.TryParse(json, out PracticeSessionRecord parsed))
            {
                failures.Add("(a) 自产 JSON 解析失败。");
            }
            else
            {
                CompareInt("(a) shots", parsed.shots, record.shots);
                CompareInt("(a) pots", parsed.pots, record.pots);
                CompareInt("(a) fouls", parsed.fouls, record.fouls);
                CompareInt("(a) racks", parsed.racks, record.racks);
                CompareInt("(a) reracks", parsed.reracks, record.reracks);
                CompareInt("(a) playAgain", parsed.playAgain, record.playAgain);
                CompareInt("(a) outcome", parsed.outcome, record.outcome);
                if (Mathf.Abs(parsed.elapsedMs - record.elapsedMs) > 1e-3f)
                {
                    failures.Add($"(a) elapsedMs 往返不一致：{record.elapsedMs:R} → {parsed.elapsedMs:R}。");
                }
            }

            void CompareInt(string label, int actual, int expected)
            {
                if (actual != expected)
                {
                    failures.Add($"{label} 往返不一致：{expected} → {actual}。");
                }
            }

            // (b) 身份字段不合格 ⇒ 整条丢弃，且**不得抛异常**
            string valid = json;
            string[] badInputs =
            {
                string.Empty,
                "{",
                "null",
                valid.Replace(record.id, string.Empty),                       // id 置空
                valid.Replace(record.startedAt, "not-a-date"),                // startedAt 不可解析
                valid.Replace("\"practice\"", "\"sandbox\""),                 // mode 不在枚举内
            };
            foreach (string bad in badInputs)
            {
                bool parsedOk;
                try
                {
                    parsedOk = PracticeSessionStore.TryParse(bad, out _);
                }
                catch (System.Exception exception)
                {
                    failures.Add($"(b) 坏输入抛出异常（应为 false）：{exception.GetType().Name} / " + Trim(bad));
                    continue;
                }

                if (parsedOk)
                {
                    failures.Add($"(b) 坏输入被接受（应为 false）：" + Trim(bad));
                }
            }

            // (c) 计数值不合格 ⇒ **逐字段归 0**，其余字段保留
            string negative = valid.Replace("\"shots\":" + record.shots, "\"shots\":-5");
            if (!PracticeSessionStore.TryParse(negative, out PracticeSessionRecord normalized))
            {
                failures.Add("(c) 负数 shots 不应导致整条丢弃。");
            }
            else
            {
                if (normalized.shots != 0)
                {
                    failures.Add($"(c) 负数 shots 应归 0，实际 {normalized.shots}。");
                }

                if (normalized.pots != record.pots)
                {
                    failures.Add($"(c) 归一时不应动其它字段（pots 应仍为 {record.pots}，实际 {normalized.pots}）。");
                }
            }

            // (d) 历史：空会话不入档；超上限只留最近 N 条
            var empty = new PracticeSessionRecord { id = "empty", startedAt = record.startedAt, mode = PracticeSessionStore.ModePractice };
            List<PracticeSessionRecord> afterEmpty = PracticeSessionStore.ReadHistory(
                PracticeSessionStore.AppendToHistory(string.Empty, empty));
            if (afterEmpty.Count != 0)
            {
                failures.Add($"(d) 空会话（shots=0 且 racks=0）不应入档，实际历史 {afterEmpty.Count} 条。");
            }

            string history = string.Empty;
            for (int i = 0; i < 35; i++)
            {
                var item = PracticeSessionStore.NewRecord(new PracticeSessionSnapshot { shots = 1 });
                history = PracticeSessionStore.AppendToHistory(history, item, 30);
            }

            List<PracticeSessionRecord> trimmed = PracticeSessionStore.ReadHistory(history);
            if (trimmed.Count != 30)
            {
                failures.Add($"(d) 超上限应只留最近 30 条，实际 {trimmed.Count}。");
            }

            // (e) 导出路径必须在**仓库外**（R3-5：跑测试不得产仓库改动）
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string fullExport = Path.GetFullPath(PracticeSessionStore.ExportPath);
            if (fullExport.StartsWith(Path.GetFullPath(projectRoot), System.StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"(e) 导出路径落在工程目录内（{fullExport}）⇒ 会污染仓库。");
            }

            if (!PracticeSessionStore.ExportPath.EndsWith(PracticeSessionStore.ExportFileName, System.StringComparison.Ordinal))
            {
                failures.Add($"(e) 导出文件名应为 {PracticeSessionStore.ExportFileName}，实际 {PracticeSessionStore.ExportPath}。");
            }

            Assert.IsEmpty(failures, "T101 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T101: 往返 ✓；坏输入 6 例全丢 ✓；归一 ✓；历史截断 30 ✓；导出路径在仓库外 ✓");
        }

        // ════════════════ T102 结算卡布局与接线 ════════════════════════════════

        [Test]
        public void T102_ResultCard_LayoutAndWiring()
        {
            var failures = new List<string>();
            const float designTolerance = 1e-3f;

            // (a)(b) 六档：卡 ⊆ 屏内；画布单位尺寸 == 参考稿量得的 583 × 635；两键 ⊆ 卡、不重叠、够高
            foreach ((string name, int width, int height) in Aspects)
            {
                Rect card = PracticeResultLayout.CardRect(width, height);
                Vector2 canvas = PracticeResultLayout.CanvasSize(width, height);

                if (card.xMin < 0f || card.yMin < 0f || card.xMax > 1f || card.yMax > 1f)
                {
                    failures.Add($"(a) {name}：卡越出屏幕 x[{card.xMin:R},{card.xMax:R}] y[{card.yMin:R},{card.yMax:R}]。");
                }

                float cardWidthPx = card.width * canvas.x;
                float cardHeightPx = card.height * canvas.y;
                if (Mathf.Abs(cardWidthPx - PracticeResultLayout.CardWidthPx) > designTolerance
                    || Mathf.Abs(cardHeightPx - PracticeResultLayout.CardHeightPx) > designTolerance)
                {
                    failures.Add($"(a) {name}：卡在画布单位下应为 "
                                 + $"{PracticeResultLayout.CardWidthPx:R} × {PracticeResultLayout.CardHeightPx:R}，"
                                 + $"实际 {cardWidthPx:R} × {cardHeightPx:R}。");
                }

                Rect play = PracticeResultLayout.PlayAgainButtonRect(width, height);
                Rect export = PracticeResultLayout.ExportButtonRect(width, height);

                if (!PracticeHudLayout.ContainsFully(card, play) || !PracticeHudLayout.ContainsFully(card, export))
                {
                    failures.Add($"(b) {name}：按钮未完全落在卡内。");
                }

                if (play.Overlaps(export))
                {
                    failures.Add($"(b) {name}：两个按钮重叠。");
                }

                float buttonHeightPx = play.height * canvas.y;
                if (buttonHeightPx < 40f)
                {
                    failures.Add($"(b) {name}：按钮高仅 {buttonHeightPx:R} 设计 px（需 ≥ 40）。");
                }
            }

            // (c) 场景侧：`EventSystem` + `StandaloneInputModule`（这两个**是**序列化在场景里的）。
            //     ⚠ `GraphicRaycaster` **不在**场景里 —— HUD 画布由 `PracticeHudView.EnsureBuilt()`
            //     在 `Awake` **运行期**创建 ⇒ 场景文件中本就不该有射线器（本判据如实锁这一点）。
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int eventSystems = 0;
                int inputModules = 0;
                int serializedRaycasters = 0;
                PracticeResultView host = null;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    eventSystems += root.GetComponentsInChildren<EventSystem>(true).Length;
                    inputModules += root.GetComponentsInChildren<StandaloneInputModule>(true).Length;
                    serializedRaycasters += root.GetComponentsInChildren<GraphicRaycaster>(true).Length;

                    foreach (PracticeResultView candidate in root.GetComponentsInChildren<PracticeResultView>(true))
                    {
                        host = candidate;
                        break;
                    }
                }

                if (eventSystems != 1)
                {
                    failures.Add($"(c) 场景应恰有 1 个 EventSystem，实际 {eventSystems}。");
                }

                if (inputModules != 1)
                {
                    failures.Add($"(c) 场景应恰有 1 个 StandaloneInputModule（旧输入口径），实际 {inputModules}。");
                }

                if (serializedRaycasters != 0)
                {
                    failures.Add($"(c) 场景**不应**序列化 `GraphicRaycaster`（画布是运行期建的），实际 {serializedRaycasters}。");
                }

                if (host == null)
                {
                    failures.Add($"(d) 场景里找不到 {GreyboxSceneSetup.PracticeResultObjectName} 的 PracticeResultView。");
                }
                else
                {
                    if (host.drive == null)
                    {
                        failures.Add("(d) 结算卡宿主的 drive 未接线。");
                    }

                    if (host.hud == null)
                    {
                        failures.Add("(d) 结算卡宿主的 hud 未接线 ⇒ 卡没有画布可挂。");
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            // (e) 运行期：画布建出来后**恰好一个** `GraphicRaycaster`，且结算卡挂在该画布下
            //     （EditMode 下 `AddComponent` 不触发 `Awake` ⇒ 显式调 `EnsureBuilt`）。
            var probeGo = new GameObject("T102Probe");
            try
            {
                var probeHud = probeGo.AddComponent<PracticeHudView>();
                var probeResult = probeGo.AddComponent<PracticeResultView>();
                probeResult.hud = probeHud;
                probeHud.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudAssetBuilder.TitleFontAssetPath);
                probeHud.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudAssetBuilder.BodyFontAssetPath);

                probeHud.EnsureBuilt();
                probeResult.EnsureBuilt();

                var raycasters = probeGo.GetComponentsInChildren<GraphicRaycaster>(true);
                if (raycasters.Length != 1)
                {
                    failures.Add($"(e) 运行期画布应恰有 1 个 GraphicRaycaster，实际 {raycasters.Length}。");
                }

                Transform canvas = probeGo.transform.Find("PracticeHudCanvas");
                Assert.IsNotNull(canvas, "T102：HUD 未建出 PracticeHudCanvas。");
                if (canvas.Find("PracticeResultCard") == null)
                {
                    failures.Add("(e) 结算卡未挂在 HUD 画布下 ⇒ 会出现两份画布缩放口径。");
                }
            }
            finally
            {
                Object.DestroyImmediate(probeGo);
            }

            Assert.IsEmpty(failures, "T102 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T102: 六档卡不越界且画布单位 {PracticeResultLayout.CardWidthPx:R}×"
                                  + $"{PracticeResultLayout.CardHeightPx:R} ✓；两键 ⊆ 卡且不重叠 ✓；"
                                  + "场景 EventSystem / 输入模块 各 1、无序列化射线器 ✓；运行期射线器 1 且卡挂同画布 ✓；宿主接线 ✓");
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "(空)";
            }

            string flat = Regex.Replace(value, @"\s+", " ");
            return flat.Length <= 48 ? flat : flat.Substring(0, 48) + "…";
        }
    }
}
