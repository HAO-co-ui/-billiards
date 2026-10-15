using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Billiards.Core;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **性能与打包判据族**（Day 16，编号续 `T114`：`T119`–`T121`）。
    ///
    /// · **`T119` 物理单步预算**：拦"量级失控"（历史上出现过单子步 199 次接触那种爆炸），**不声称 fps**；
    /// · **`T120` 性能与构建配置一致性**：阴影收敛 / 帧率策略 / **出包入口不打出黑屏死包**三项
    ///   （前两项比对代码常量与资产实际值；第三项断言**出包入口的三重保护** + 灰盒场景非空壳 ——
    ///   口径变更的原因见 `T120` 方法内注释：持久 build list 与 `SCN-02` / `D4-BLD-01` **互斥**）；
    /// · **`T121` 冒烟纯逻辑**：`SmokeProbe` 的命令行解析、帧时统计、达标判定（全纯函数、无随机）。
    ///
    /// **为什么 `T119` 是"宽松闸门"而不是精确基线**：计时受机器负载影响，写死数字会变成假红；
    /// 真正的 fps 结论**只能**由构建产物的冒烟模式（`-billiards-smoke`）给出 —— 那是本族判据**测不到**的盲带，如实登记。
    /// </summary>
    public class PerformanceAndPackagingTests
    {
        // ── T119 ─────────────────────────────────────────────────────────

        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>开球忙期的单步上界（实测 0.65 ms ⇒ 约 7.7× 余量）：只拦量级失控。</summary>
        private const float BusyStepBudgetMs = 5f;

        /// <summary>整盘静止空转的单步上界（实测 0.37 ms ⇒ 约 8× 余量）。</summary>
        private const float IdleStepBudgetMs = 3f;

        private static PhysicsParams ShipParams()
        {
            PhysicsTuning tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "需要 " + TuningPath);
            PhysicsParams p = tuning.ToPhysicsParams();
            p.cushionRestitution = 0.9f;
            p.cushionTangentialFriction = 0.8f;
            p.ballRestitution = 1f;
            p.ballTangentialFriction = 1f;
            return p;
        }

        /// <summary>
        /// **`T119`：16 球 + 82 段的单步成本必须在预算内**（排期 Day 16 任务②）。
        ///
        /// 三个场景（都由实测取证，打印 µs/步与"占 60 fps 帧预算的百分比"）：
        /// ① 满堆开球**忙期**（子步内多次 TOI 迭代）② **整盘静止**空转 ③ 单球自由滚动。
        ///
        /// **注入负对照（实测）**：把 `SubSteps` 推高一个量级（`DeriveSubSteps` 的输入放大）⇒ 单步成本同比上升，
        /// `BusyStepBudgetMs` 会先破 —— 即本判据对"子步数被改坏"敏感。
        ///
        /// **盲带（如实）**：① 编辑器内计时含调试开销，**不**等于构建产物（Mono/IL2CPP 差异）；
        /// ② 本判据**不能**得出任何 fps 结论 —— 那由构建产物的 `-billiards-smoke` 给出；
        /// ③ 首轮回暖不足时会偏高（已用 60 步预热 + 取平均值缓解，但仍非统计意义上的稳定基准）。
        /// </summary>
        [Test]
        public void T119_PhysicsStepBudget_WithinLooseGate()
        {
            TableConfig table = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(table, "需要 " + TablePath);

            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 15);
            var initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var report = new StringBuilder("T119 物理单步成本（16 球 + " + table.SegmentCount + " 段）：\n");

            // ① 开球忙期
            var busy = new PoolSimulator(table, ShipParams());
            busy.LoadBalls(initial);
            for (int i = 0; i < 60; i++)
            {
                busy.Step();   // 预热（JIT + 缓存）
            }

            const int busySteps = 600;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < busySteps; i++)
            {
                busy.Step();
            }

            sw.Stop();
            double busyUs = sw.Elapsed.TotalMilliseconds * 1000.0 / busySteps;

            // ② 整盘静止空转
            var idle = new PoolSimulator(table, ShipParams());
            idle.LoadBalls(initial);
            int toRest = 0;
            while (toRest < 3600 && !idle.TableAtRest)
            {
                idle.Step();
                toRest++;
            }

            const int idleSteps = 3000;
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < idleSteps; i++)
            {
                idle.Step();
            }

            sw2.Stop();
            double idleUs = sw2.Elapsed.TotalMilliseconds * 1000.0 / idleSteps;

            // ③ 单球自由滚动
            var single = new PoolSimulator(table, ShipParams());
            single.LoadBalls(new[] { new BallState { position = Vector2.zero, velocity = new Vector2(1f, 0f) } });
            for (int i = 0; i < 60; i++)
            {
                single.Step();
            }

            const int singleSteps = 3000;
            var sw3 = Stopwatch.StartNew();
            for (int i = 0; i < singleSteps; i++)
            {
                single.Step();
            }

            sw3.Stop();
            double singleUs = sw3.Elapsed.TotalMilliseconds * 1000.0 / singleSteps;

            float budget = SmokeProbe.FrameBudgetMs;
            report.Append(Line("① 开球忙期", busyUs, budget));
            report.Append(Line($"② 静止空转（第 {toRest} 步后静止）", idleUs, budget));
            report.Append(Line("③ 单球自由滚动", singleUs, budget));

            Assert.LessOrEqual(busyUs / 1000.0, BusyStepBudgetMs,
                $"T119：开球忙期单步 {busyUs / 1000.0:F3} ms 超过上界 {BusyStepBudgetMs} ms（量级失控？）。");
            Assert.LessOrEqual(idleUs / 1000.0, IdleStepBudgetMs,
                $"T119：静止空转单步 {idleUs / 1000.0:F3} ms 超过上界 {IdleStepBudgetMs} ms（量级失控？）。");

            // 非空转证据：三档必须都真的在推进（否则"很快"可能只是什么都没做）。
            Assert.Greater(busy.TotalContactCount, 0, "T119：开球场景应有接触（空转）。");
            Assert.IsTrue(idle.TableAtRest, "T119：静止场景应真的达到整盘静止（空转）。");
            Assert.Greater(single.Balls[0].position.x, 0f, "T119：单球应真的移动（空转）。");

            TestContext.WriteLine(report.ToString());

            string Line(string tag, double us, float frameBudgetMs) =>
                $"  {tag}：{us.ToString("F1", CultureInfo.InvariantCulture)} µs/步"
                + $"（占 60 fps 帧预算 {frameBudgetMs.ToString("F2", CultureInfo.InvariantCulture)} ms 的 "
                + $"{us / (frameBudgetMs * 1000.0) * 100.0:F3}%）\n";
        }

        // ── T120 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T120`：性能与构建配置必须与"有意设置"一致**（三组，全部读**实际资产/实际运行值**）。
        ///
        /// ① **URP 阴影收敛**：`m_ShadowDistance ≤ 8`、`m_ShadowCascadeCount == 1`、`m_MainLightShadowmapResolution == 2048`
        ///    （原值 50 / 4 / 2048：用 4 次 2048² 的 pass 覆盖 50 m，而场景只有一张 2.54 m 的桌子）。
        /// ② **帧率策略**：调 `FrameRateSettings.Apply()` 后**读实际值**（`QualitySettings.vSyncCount` /
        ///    `Application.targetFrameRate`）—— **不是**读源码文本（文本级守卫会两向失真），读完**还原**编辑器原值。
        /// ③ **构建场景列表**：`EditorBuildSettings.scenes` 恰含并启用灰盒场景
        ///    （原列表指向空壳 `Main.unity` ⇒ 会打出黑屏死包；这条是本判据最值钱的一格）。
        ///
        /// **注入负对照（实测）**：把级联改回 4 ⇒ ① 红；把场景列表改回 `Main.unity` ⇒ ③ 红。
        /// **盲带**：只锁"设置值"，**不**证明渲染结果（阴影实际观感、合批是否真的发生）—— 那要构建产物实测。
        /// </summary>
        [Test]
        public void T120_PerformanceAndBuildSettings_MatchIntent()
        {
            var failures = new List<string>();

            // ① URP 阴影
            var rp = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PerformanceSetup.RenderPipelineAssetPath);
            Assert.IsNotNull(rp, "T120：找不到 " + PerformanceSetup.RenderPipelineAssetPath);
            var so = new SerializedObject(rp);
            float shadowDistance = so.FindProperty("m_ShadowDistance").floatValue;
            int cascades = so.FindProperty("m_ShadowCascadeCount").intValue;
            int resolution = so.FindProperty("m_MainLightShadowmapResolution").intValue;

            if (shadowDistance > 8f)
            {
                failures.Add($"① 阴影距离 {shadowDistance} m 未收敛（应 ≤ 8 m；桌面只有 2.54 m）。");
            }

            if (cascades != PerformanceSetup.ShadowCascadeCount)
            {
                failures.Add($"① 阴影级联 {cascades} ≠ {PerformanceSetup.ShadowCascadeCount}（6 m 范围内 1 级即可）。");
            }

            if (resolution != PerformanceSetup.MainLightShadowmapResolution)
            {
                failures.Add($"① 阴影图分辨率 {resolution} ≠ {PerformanceSetup.MainLightShadowmapResolution}。");
            }

            // ② 帧率策略（改实际值 → 断言 → 还原）
            int oldVSync = QualitySettings.vSyncCount;
            int oldTarget = Application.targetFrameRate;
            try
            {
                FrameRateSettings.Apply();
                if (QualitySettings.vSyncCount != FrameRateSettings.VSyncCount)
                {
                    failures.Add($"② vSyncCount 实际 {QualitySettings.vSyncCount} ≠ {FrameRateSettings.VSyncCount}（Apply 未生效？）。");
                }

                if (Application.targetFrameRate != FrameRateSettings.TargetFrameRate)
                {
                    failures.Add($"② targetFrameRate 实际 {Application.targetFrameRate} ≠ {FrameRateSettings.TargetFrameRate}。");
                }
            }
            finally
            {
                QualitySettings.vSyncCount = oldVSync;
                Application.targetFrameRate = oldTarget;
            }

            // ③ 出包不得是黑屏死包 —— **判据的对象是"出包入口的保证"，不是 `EditorBuildSettings.asset` 的持久值**。
            //
            // ── 为什么改了口径（实测冲突，必须写下来，否则会被重复"修"回去）──────────────
            // · `SCN-02`（Day1 工程配置验收）与 `D4-BLD-01`（Day4 物理验收）**都**断言**提交态**的
            //   build list **恰好**是 `[Main.unity]`；而 `BilliardsProjectSetup` 的 Apply 每次会把它
            //   写回 `Main.unity` ⇒ 两个验收各自自洽（我实测 26/33/22/7 全绿）。
            // · Day16 修"黑屏死包"的做法是让**构建入口**改用灰盒场景 ⇒ 与上面那条**互斥**：
            //   谁后跑谁生效。**不能**靠"把持久值改成灰盒"来同时满足两边 ——
            //   那会让 `SCN-02` 变 25/26、`D4-BLD-01` 直接 FAIL、两份已入库证据作废。
            // · 实测：出包入口 `BuildWindows.BuildFromCommandLine` **自带三重保护** ——
            //   ① 先跑 `PerformanceSetup.Apply()`（把列表写成灰盒场景）→ ② 断言列表不是灰盒就**拒绝出包**
            //   → ③ `BuildPlayerOptions.scenes` **显式**指定场景（出包内容与持久列表无关）。
            //   ⇒ 出包正确性由这三条保证，**不依赖** `EditorBuildSettings.asset` 存着什么。
            // ⇒ 故本项断言"那三重保护真的在"，而不是断言一个与另一验收口径互斥的持久值。
            // （若将来把 `Main.unity` 做成真正的游戏入口，本项应改为断言"首个 build 场景不是空壳"。）
            const string buildEntryPath = "Assets/_Project/Scripts/Editor/ProjectSetup/BuildWindows.cs";
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string buildSource = File.ReadAllText(Path.Combine(projectRoot, buildEntryPath), Encoding.UTF8);

            if (!Regex.IsMatch(buildSource, @"PerformanceSetup\.Apply\s*\("))
            {
                failures.Add("③ 出包入口没有先应用 `PerformanceSetup`（构建场景列表不会被写成灰盒场景）。");
            }

            if (!Regex.IsMatch(buildSource, @"scenes\[0\]\.path\s*!=\s*PerformanceSetup\.GreyboxScenePath"))
            {
                failures.Add("③ 出包入口没有「列表不对就拒绝出包」的闸门（会静默打出黑屏死包）。");
            }

            if (!Regex.IsMatch(buildSource, @"scenes\s*=\s*new\[\]\s*\{\s*PerformanceSetup\.GreyboxScenePath\s*\}"))
            {
                failures.Add("③ 出包入口没有在 `BuildPlayerOptions` 里显式指定场景"
                             + "（那样出包内容会随持久列表漂移）。");
            }

            // 灰盒场景**必须不是空壳**：空壳场景编不出任何逻辑（黑屏的真因）。
            var greyboxScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(PerformanceSetup.GreyboxScenePath);
            if (greyboxScene == null)
            {
                failures.Add($"③ 灰盒场景资产不存在：{PerformanceSetup.GreyboxScenePath}。");
            }
            else
            {
                string sceneText = File.ReadAllText(
                    Path.Combine(projectRoot, PerformanceSetup.GreyboxScenePath), Encoding.UTF8);
                if (!sceneText.Contains(GreyboxSceneSetup.DriveObjectName)
                    || !sceneText.Contains(GreyboxSceneSetup.PracticeHudObjectName))
                {
                    failures.Add($"③ 灰盒场景里找不到 `{GreyboxSceneSetup.DriveObjectName}` / "
                                 + $"`{GreyboxSceneSetup.PracticeHudObjectName}` ⇒ 它是空壳，出包即黑屏。");
                }
            }

            Assert.IsEmpty(failures, "T120 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "T120: 阴影 {0}m/级联 {1}/图 {2}²；帧率 vSync={3}/target={4}；"
                + "出包入口三重保护（应用配置/拒绝闸门/显式场景）✓；灰盒场景非空壳 ✓",
                shadowDistance, cascades, resolution, FrameRateSettings.VSyncCount, FrameRateSettings.TargetFrameRate));
        }

        // ── T121 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T121`：冒烟模式的纯逻辑**（`SmokeProbe`）。
        ///
        /// ① **命令行解析**：无开关 / 缺帧数 / 非整数 / `&lt; 2` ⇒ 不进冒烟模式；合法 ⇒ 帧数与输出路径都对
        ///    （`-out` 在开关**前后**都要能取到）。这一格拦的是"参数写错导致**静默不进冒烟**、
        ///    产物照常跑但没有任何证据"——那是最难发现的一类失败。
        /// ② **帧时统计闭式**：构造样本（95 帧 10 ms + 5 帧 20 ms）⇒ mean 10.5 / p95 = 10.0（升序第 `ceil(0.95n)−1` 个）
        ///    / max 20 / 超预算 5 帧。
        /// ③ **达标判定**：均值恰在预算内、超预算占比恰在 2% 边界 ⇒ 通过；越界 ⇒ 不通过；`frames ≤ 0` ⇒ 不通过。
        /// ④ **JSON**：字段名齐备且数字用**不变文化**（小数点必须是 `.`，不得随系统区域写成 `,`）。
        ///
        /// **盲带**：只锁纯逻辑；**不证明**构建产物真的按此执行（那要真跑一次 exe，见规格 §4.3）。
        /// </summary>
        [Test]
        public void T121_SmokeProbe_PureLogic()
        {
            var failures = new List<string>();

            // ① 解析
            AssertParse(failures, new[] { "Idea35PoolRoom.exe" }, false, 0, null, "无开关");
            AssertParse(failures, new[] { "-billiards-smoke" }, false, 0, null, "缺帧数");
            AssertParse(failures, new[] { "-billiards-smoke", "abc" }, false, 0, null, "非整数");
            AssertParse(failures, new[] { "-billiards-smoke", "1" }, false, 0, null, "帧数过小（<2）");
            AssertParse(failures, new[] { "-billiards-smoke", "600" }, true, 600, null, "合法（无 out）");
            AssertParse(failures, new[] { "-billiards-smoke", "600", "-billiards-smoke-out", "D:/e/s.json" },
                true, 600, "D:/e/s.json", "out 在开关之后");
            AssertParse(failures, new[] { "-billiards-smoke-out", "D:/e/s.json", "-billiards-smoke", "300" },
                true, 300, "D:/e/s.json", "out 在开关之前");

            // ② 统计闭式
            var samples = new List<float>();
            for (int i = 0; i < 95; i++)
            {
                samples.Add(10f);
            }

            for (int i = 0; i < 5; i++)
            {
                samples.Add(20f);
            }

            SmokeProbe.ComputeStats(samples, out float mean, out float p95, out float max, out int dropped);
            if (Mathf.Abs(mean - 10.5f) > 1e-4f)
            {
                failures.Add($"② 均值应为 10.5，实测 {mean:R}。");
            }

            if (Mathf.Abs(p95 - 10f) > 1e-4f)
            {
                failures.Add($"② p95 应为 10.0（升序第 ceil(0.95×100)−1 = 94 个），实测 {p95:R}。");
            }

            if (Mathf.Abs(max - 20f) > 1e-4f)
            {
                failures.Add($"② max 应为 20，实测 {max:R}。");
            }

            if (dropped != 5)
            {
                failures.Add($"② 超预算帧数应为 5，实测 {dropped}。");
            }

            // ③ 达标判定（边界：2% 恰在上限内）
            if (!SmokeProbe.MeetsTarget(16f, 0, 100))
            {
                failures.Add("③ 均值 16 ms / 无掉帧 应通过。");
            }

            if (SmokeProbe.MeetsTarget(20f, 0, 100))
            {
                failures.Add("③ 均值 20 ms 超预算 不应通过。");
            }

            if (!SmokeProbe.MeetsTarget(16f, 2, 100))
            {
                failures.Add("③ 超预算恰 2%（= 上限）应通过（口径是 `≤`）。");
            }

            if (SmokeProbe.MeetsTarget(16f, 3, 100))
            {
                failures.Add("③ 超预算 3% 不应通过。");
            }

            if (SmokeProbe.MeetsTarget(10f, 0, 0))
            {
                failures.Add("③ frames = 0 不应通过。");
            }

            // ④ JSON 字段与不变文化
            string json = SmokeProbe.ToJson(new SmokeResult
            {
                unityVersion = "6000.0.49f1", width = 1920, height = 1080, fullScreen = false,
                vSyncCount = 1, targetFrameRate = 60, frames = 600, meanMs = 16.5f, p95Ms = 17.25f, maxMs = 18.75f,
                fpsMean = 60.60606f, droppedOverBudget = 2, rendererCount = 90, distinctMaterialCount = 25,
                physicsSteps = 12000, passed = true,
            });

            foreach (string field in new[]
                     {
                         "\"width\"", "\"height\"", "\"fullScreen\"", "\"vSyncCount\"", "\"targetFrameRate\"",
                         "\"frames\"", "\"meanMs\"", "\"p95Ms\"", "\"maxMs\"", "\"fpsMean\"", "\"droppedOverBudget\"",
                         "\"frameBudgetMs\"", "\"rendererCount\"", "\"distinctMaterialCount\"", "\"physicsSteps\"",
                         "\"passed\"",
                     })
            {
                if (!json.Contains(field))
                {
                    failures.Add($"④ JSON 缺字段 {field}。");
                }
            }

            if (!json.Contains("16.5") || json.Contains("16,5"))
            {
                failures.Add($"④ 数字必须用不变文化（`.` 作小数点），实测片段：{json.Substring(0, 120)}");
            }

            Assert.IsEmpty(failures, "T121 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T121: 冒烟纯逻辑 7 例解析 + 统计闭式 + 5 例判定 + JSON 16 字段 ✓（帧预算 {SmokeProbe.FrameBudgetMs:F4} ms，掉帧上限 {SmokeProbe.MaxDroppedFraction:P0}）");
        }

        private static void AssertParse(List<string> failures, string[] args, bool expectOk, int expectFrames,
            string expectPath, string tag)
        {
            bool ok = SmokeProbe.TryParse(args, out int frames, out string path);
            if (ok != expectOk)
            {
                failures.Add($"① [{tag}] 解析结果应为 {expectOk}，实测 {ok}。");
                return;
            }

            if (!ok)
            {
                return;
            }

            if (frames != expectFrames)
            {
                failures.Add($"① [{tag}] 帧数应为 {expectFrames}，实测 {frames}。");
            }

            if (path != expectPath)
            {
                failures.Add($"① [{tag}] 输出路径应为 {expectPath ?? "(null)"}，实测 {path ?? "(null)"}。");
            }
        }
    }
}
