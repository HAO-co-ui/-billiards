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
    ///   （前两项比对代码常量与资产实际值；第三项断言**出包入口的三重保护** + **正式入口 `Main.unity` 非空壳**）；
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
            long skipsBeforeBusy = busy.SegmentScreenSkips;   // 批次 C：预筛诊断计数（仅报告，不作断言）
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < busySteps; i++)
            {
                busy.Step();
            }

            sw.Stop();
            double busyUs = sw.Elapsed.TotalMilliseconds * 1000.0 / busySteps;
            long busySkips = busy.SegmentScreenSkips - skipsBeforeBusy;

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

            // ── 批次 C 预筛的**稳健下限**（防"预筛被静默禁用"）──────────────────────────
            // ⚠ 它**不是性能判据** —— 跳过率会随表 / 球数漂移，不适合当硬阈值；
            //    它只是一条**下限**：若有人把预筛改没了（例如把 ε 改成极大值使预筛永不命中，
            //    或把预筛调用删掉），**不会有任何其它判据喊** ⇒ 由这条来喊。**不改变任何既有期望值。**
            Assert.Greater(busySkips, 0L,
                "T119：忙期预筛**一对都没跳过** ⇒ 预筛被静默禁用（或 ε 被改成使预筛恒不命中的值）。");

            // ── 批次 C 预筛的**非空转证据**（仅报告，不作断言 ⇒ 期望值零改动）──────────────────
            // ⚠ **"逐位哈希相等"只能证明"没改坏"，不能证明"预筛真的跳过很多对"** ——
            //    一个"从不跳过任何对"的预筛同样能过哈希。故把**诊断计数**连同理论对数报出来自证。
            // 理论对数/步 = 球数 × 段数 × 子步数（`ResolveDeepOverlaps` 与 `EarliestSegmentToi` **各扫一遍** ⇒ ×2）。
            long pairsPerStepPerLoop = (long)rack.Length * table.SegmentCount * busy.SubSteps;
            long pairsPerStepBothLoops = pairsPerStepPerLoop * 2;
            double skipRate = pairsPerStepBothLoops > 0
                ? busySkips / (double)busySteps / pairsPerStepBothLoops
                : 0.0;
            report.Append(string.Format(CultureInfo.InvariantCulture,
                "  预筛（批次 C）：忙期 600 步累计跳过 **{0}** 对 ⇒ {1:F0} 对/步；"
                + "理论对数/步 = 球 {2} × 段 {3} × 子步 {4} = {5}（RDO+TOI 各一遍 ⇒ {6}）⇒ 跳过率 ≈ {7:F1}%\n",
                busySkips, busySkips / (double)busySteps, rack.Length, table.SegmentCount, busy.SubSteps,
                pairsPerStepPerLoop, pairsPerStepBothLoops, skipRate * 100.0));

            TestContext.WriteLine(report.ToString());            string Line(string tag, double us, float frameBudgetMs) =>
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
        /// ③ **构建场景列表 / 出包入口**：`BuildWindows` 三重保护（先应用 `PerformanceSetup` /
        ///    列表不对就拒绝出包 / `BuildPlayerOptions.scenes` 显式指定）+ **`Main.unity` 不是空壳**
        ///    （含驱动器 / HUD / 瞄准辅助等玩法宿主 ⇒ 不会打出黑屏死包；这条是本判据最值钱的一格）。
        ///
        /// **注入负对照（实测）**：把级联改回 4 ⇒ ① 红；把出包入口的场景指回空壳场景 ⇒ ③ 红。
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

            // ③ 出包不得是黑屏死包 —— **判据 = "出包入口指向正式入口 + 正式入口不是空壳"**。
            //
            // ── 口径现状（旧口径已作废，勿再引回）────────────────────────────────
            // · `SCN-02`（Day1 工程配置验收）与 `D4-BLD-01`（Day4 物理验收）**都**断言**提交态**的
            //   build list **恰好**是 `[Main.unity]`。
            // · 旧口径下 `Main.unity` 是**空壳**（0 个 MonoBehaviour）⇒ "出包不得黑屏"与上面那条**互斥**：
            //   谁后跑谁生效。当时的临时处置是让**出包入口**改用灰盒场景（自带三重保护），
            //   本判据因而写成"断言那三重保护 + 灰盒场景非空壳"。
            // · **现在 `Main.unity` 是正式游戏入口**（带完整玩法层）⇒ 出包入口与 build list **都指向它**，
            //   互斥的根因（"Main 是空壳"）已消除 ⇒ 本判据改回**断言正式入口本身**：
            //   ① 出包入口先应用 `PerformanceSetup`（把列表写成 `Main.unity`）；
            //   ② 列表不对就**拒绝出包**；
            //   ③ `BuildPlayerOptions.scenes` **显式**指定 `Main.unity`；
            //   ④ `Main.unity` **不是空壳**（含驱动器 / HUD / 瞄准辅助等玩法宿主）。
            const string buildEntryPath = "Assets/_Project/Scripts/Editor/ProjectSetup/BuildWindows.cs";
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string buildSource = File.ReadAllText(Path.Combine(projectRoot, buildEntryPath), Encoding.UTF8);

            if (!Regex.IsMatch(buildSource, @"PerformanceSetup\.Apply\s*\("))
            {
                failures.Add("③ 出包入口没有先应用 `PerformanceSetup`（构建场景列表不会被写成正式入口）。");
            }

            if (!Regex.IsMatch(buildSource, @"scenes\[0\]\.path\s*!=\s*PerformanceSetup\.BuildScenePath"))
            {
                failures.Add("③ 出包入口没有「列表不对就拒绝出包」的闸门（会静默打出黑屏死包）。");
            }

            if (!Regex.IsMatch(buildSource, @"scenes\s*=\s*new\[\]\s*\{\s*PerformanceSetup\.BuildScenePath\s*\}"))
            {
                failures.Add("③ 出包入口没有在 `BuildPlayerOptions` 里显式指定场景"
                             + "（那样出包内容会随持久列表漂移）。");
            }

            // 正式入口**必须不是空壳**：空壳场景编不出任何逻辑（黑屏的真因）。
            var mainScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(PerformanceSetup.BuildScenePath);
            if (mainScene == null)
            {
                failures.Add($"③ 正式入口场景资产不存在：{PerformanceSetup.BuildScenePath}。");
            }
            else
            {
                string sceneText = File.ReadAllText(
                    Path.Combine(projectRoot, PerformanceSetup.BuildScenePath), Encoding.UTF8);
                if (!sceneText.Contains(GreyboxSceneSetup.DriveObjectName)
                    || !sceneText.Contains(GreyboxSceneSetup.PracticeHudObjectName)
                    || !sceneText.Contains(GreyboxSceneSetup.AimAssistObjectName))
                {
                    failures.Add($"③ 正式入口 `{PerformanceSetup.BuildScenePath}` 里找不到 "
                                 + $"`{GreyboxSceneSetup.DriveObjectName}` / "
                                 + $"`{GreyboxSceneSetup.PracticeHudObjectName}` / "
                                 + $"`{GreyboxSceneSetup.AimAssistObjectName}` ⇒ 它是空壳，出包即黑屏。");
                }
            }

            Assert.IsEmpty(failures, "T120 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "T120: 阴影 {0}m/级联 {1}/图 {2}²；帧率 vSync={3}/target={4}；"
                + "出包入口三重保护（应用配置/拒绝闸门/显式场景）✓；正式入口 Main.unity 非空壳 ✓",
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

        // ── T131 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T131`：拖动瞄准冒烟的纯逻辑**（`-billiards-smoke-aim`）。
        ///
        /// 动机：原来的冒烟只测**静置** 600 帧 ⇒ **击球 / 拖动瞄准的峰值从未被测过**（既有登记盲带）。
        /// 本判据锁"拖动负载"这条新增路径的纯逻辑：
        /// ① **解析**：带 `-billiards-smoke-aim` ⇒ `aimSweep = true`（**无值标志**，位置任意，可在帧数开关前后）；
        ///    不带 ⇒ `false`；只有 aim 开关而无帧数 ⇒ **不进入**冒烟模式（防"参数写错静默不进冒烟"）。
        /// ② **角函数闭式**：`AimSweepDegrees` 第 0 帧 = 0°、逐帧增量恒为 `AimSweepDegreesPerFrame`（模 360 回绕后仍单调）、
        ///    80 帧恰覆盖整圈 360° 并回到起点。
        /// ③ **JSON**：字段含 `"aimSweep"` 与 `"gate"`（判定口径名）。
        /// ④ **闸门阈值对 spec**（`G4`）：拖动与静置现在**同判** `MeetsTarget` ⇒ 它的两个阈值（60 fps 单帧
        ///    预算 / 超预算占比 ≤ 2%）**用独立字面量**钉住 —— **不是**拿常量比常量。
        ///    （旧版这里钉的是已被删除的宽松闸门 `MeetsDragTarget`/`DragFrameBudgetMs`；见 ⑤ 的删除守卫。）
        ///
        /// **盲带**：只锁纯逻辑；**不证明**构建产物真的按此拖动（那要真跑一次 exe，见规格 §4）。
        /// </summary>
        [Test]
        public void T131_SmokeAimSweep_PureLogic()
        {
            var failures = new List<string>();

            // ① 解析
            bool ok1 = SmokeProbe.TryParse(new[] { "-billiards-smoke", "600", "-billiards-smoke-aim" },
                out int f1, out _, out bool s1);
            if (!ok1 || !s1 || f1 != 600)
            {
                failures.Add($"① 带 aim 开关应解析成功且 aimSweep=true/600，实测 ok={ok1} sweep={s1} frames={f1}。");
            }

            bool ok2 = SmokeProbe.TryParse(new[] { "-billiards-smoke-aim", "-billiards-smoke", "300" },
                out int f2, out _, out bool s2);
            if (!ok2 || !s2 || f2 != 300)
            {
                failures.Add($"① aim 开关在帧数开关**之前**也应生效，实测 ok={ok2} sweep={s2} frames={f2}。");
            }

            bool ok3 = SmokeProbe.TryParse(new[] { "-billiards-smoke", "600" }, out _, out _, out bool s3);
            if (!ok3 || s3)
            {
                failures.Add($"① 不带 aim 开关：应进入冒烟且 aimSweep=false，实测 ok={ok3} sweep={s3}。");
            }

            if (SmokeProbe.TryParse(new[] { "-billiards-smoke-aim" }, out _, out _, out _))
            {
                failures.Add("① 只有 aim 开关、没有帧数 ⇒ 不应进入冒烟模式（否则会静默不跑证据）。");
            }

            // ② 角函数闭式 —— **期望用独立字面量**（`G3`：修掉"拿常量比常量"的恒真）
            // ⚠ 旧写法把 `SmokeProbe.AimSweepDegreesPerFrame` **自身**当期望 ⇒ **恒真**：把常量改小 8 倍
            //   （0.5625°/帧）测试照样绿 —— 而那样"80 帧覆盖整圈"会退化成"只覆盖 45°"，sweep 语义已坏。
            //   ⇒ 期望改为**独立字面量 4.5f**（= 设计值：4.5°/帧 ⇒ 60 fps 下 270°/s，规格 §3.3），
            //     并**另行断言常量等于它** ⇒ 改常量必红。
            const float ExpectedDegreesPerFrame = 4.5f;
            if (Mathf.Abs(SmokeProbe.AimSweepDegreesPerFrame - ExpectedDegreesPerFrame) > 1e-6f)
            {
                failures.Add($"② `AimSweepDegreesPerFrame` 应为设计值 {ExpectedDegreesPerFrame}°，"
                             + $"实际 {SmokeProbe.AimSweepDegreesPerFrame}°（常量被改 ⇒ 相位增速 / 覆盖圈数已变）。");
            }

            if (Mathf.Abs(SmokeProbe.AimSweepDegrees(0) - 0f) > 1e-4f)
            {
                failures.Add($"② 第 0 帧应为 0°，实测 {SmokeProbe.AimSweepDegrees(0):R}°。");
            }

            // 覆盖整圈所需帧数 = 360 / **独立设计值**（不是读常量）
            int framesPerTurn = Mathf.RoundToInt(360f / ExpectedDegreesPerFrame);
            float prev = SmokeProbe.AimSweepDegrees(0);
            float total = 0f;
            for (int n = 1; n <= framesPerTurn; n++)
            {
                float cur = SmokeProbe.AimSweepDegrees(n);
                float delta = cur - prev;
                if (delta <= 0f)
                {
                    delta += 360f;   // 跨 360 回绕
                }

                if (Mathf.Abs(delta - ExpectedDegreesPerFrame) > 1e-3f)
                {
                    failures.Add($"② 第 {n} 帧角增量应为 {ExpectedDegreesPerFrame}°，实测 {delta}°（非连续 / 非单调）。");
                }

                total += delta;
                prev = cur;
            }

            if (Mathf.Abs(total - 360f) > 1e-2f)
            {
                failures.Add($"② {framesPerTurn} 帧应覆盖整圈 360°，实测 {total}°（独立设计步长 {ExpectedDegreesPerFrame}°）。");
            }

            if (SmokeProbe.AimSweepDegrees(framesPerTurn) != SmokeProbe.AimSweepDegrees(0))
            {
                failures.Add($"② {framesPerTurn} 帧后应回到起点角（整圈）⇒ 指纹每帧都变，拖动路径被真正触发。");
            }

            // ③ JSON 字段（**新可观测项也必须落盘**，否则"上报了却没写进证据"）
            string json = SmokeProbe.ToJson(new SmokeResult { aimSweep = true, passed = true });
            foreach (string field in new[]
                     {
                         "\"aimSweep\"", "\"gate\"",
                         "\"minMs\"", "\"loadRatio\"", "\"machineClass\"",
                         "\"roundMeanMsMin\"", "\"roundMeanMsMedian\"", "\"roundMeanMsMax\"",
                         "\"predictionComputeCount\"", "\"totalPredictSteps\"",
                         "\"frameGateOk\"", "\"sceneBudgetOk\"",
                         "\"rendererCountLimit\"", "\"distinctMaterialCountLimit\"",
                     })
            {
                if (!json.Contains(field))
                {
                    failures.Add($"③ JSON 缺字段 {field}。");
                }
            }

            // ④ **闸门阈值对 spec 值**（`G4`）：拖动与静置**同判** `MeetsTarget` ⇒ 它的两个阈值就是拖动路径的阈值。
            //    用**独立字面量**钉住（spec §3.3/§4.2：60 fps ⇒ 单帧 16.666667 ms；超预算占比 ≤ 2%），
            //    **不是**拿常量比常量。
            if (Mathf.Abs(SmokeProbe.FrameBudgetMs - 16.666667f) > 1e-4f)
            {
                failures.Add($"④ `FrameBudgetMs` 应为 60 fps 单帧预算 16.666667 ms，实际 {SmokeProbe.FrameBudgetMs}。");
            }

            // ⚠ 故意**经局部变量**再比较：直接把两个 `const int` 写进 `if` 会被编译器**常量折叠**成恒假
            //   ⇒ 分支判为"不可达"（`CS0162` 警告）。经变量后仍是运行时比较、语义不变。
            int droppedNum = SmokeProbe.DroppedFractionNumerator;
            int droppedDen = SmokeProbe.DroppedFractionDenominator;
            if (droppedNum != 2 || droppedDen != 100)
            {
                failures.Add("④ 超预算占比上限应为 2/100，实际 " + $"{droppedNum}/{droppedDen}。");
            }

            // ⑤ **"宽松闸门已删、两条负载同判"的源码级 + 类型级守卫**（`G6` 同批）
            //    ⚠ 只跑一次冒烟看 `gate` 字段证明不了"不许把宽松闸门加回来" ⇒ 静态也要守。
            string runnerPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".",
                "Assets/_Project/Scripts/Runtime/Core/SmokeProbeRunner.cs");
            Assert.IsTrue(File.Exists(runnerPath), "T131：找不到 SmokeProbeRunner.cs（源码级守卫无法执行）。");
            string runnerCode = File.ReadAllText(runnerPath);
            foreach (string needed in new[]
                     {
                         "MeetsTarget(", "MeetsSceneBudget(", "IsPremiumMachine(",
                         // ⚠ 必须写**调用形态**（带 `();`）—— 只写 `SampleScenePeaks(` 会命中**方法定义**，
                         //    于是"把调用删掉"这种注入**照样绿**（本批实测：这条守卫第一版就是空转的，
                         //    由 `batchB_injections.py g5-peaks` 抓出来）。
                         "SampleScenePeaks();", "ExitCodeEnvUnclean", "SmokeProbe.Rounds",
                     })
            {
                if (!runnerCode.Contains(needed))
                {
                    failures.Add($"⑤ 采样执行体未接上 `{needed}`（常量加了但没人用）。");
                }
            }

            if (runnerCode.Contains("MeetsDragTarget"))
            {
                failures.Add("⑤ 采样执行体仍引用 `MeetsDragTarget` —— 宽松闸门应**已删除**（不留死闸门）。");
            }

            if (typeof(SmokeProbe).GetMethod("MeetsDragTarget") != null)
            {
                failures.Add("⑤ `SmokeProbe.MeetsDragTarget` 仍存在 —— 已作废的闸门不得留在代码里（会被后人误用）。");
            }

            // ⑥ 场景预算纯函数的**判别力**（边界：口径是 `≤`）
            if (!SmokeProbe.MeetsSceneBudget(SmokeProbe.MaxRendererCount, SmokeProbe.MaxDistinctMaterialCount))
            {
                failures.Add("⑥ 恰在上限值应通过（口径是 ≤）。");
            }

            if (SmokeProbe.MeetsSceneBudget(SmokeProbe.MaxRendererCount + 1, 0))
            {
                failures.Add("⑥ 渲染器超上限 1 应不通过。");
            }

            if (SmokeProbe.MeetsSceneBudget(0, SmokeProbe.MaxDistinctMaterialCount + 1))
            {
                failures.Add("⑥ 材质超上限 1 应不通过。");
            }

            // ⑦ 机器分类纯函数的**判别力**（相对量；实测空载 ~1.00、忙机 ~1.5+）
            if (!SmokeProbe.IsPremiumMachine(7f, 6.9f))
            {
                failures.Add("⑦ 比值 1.01 应判 premium（空载形态）。");
            }

            if (SmokeProbe.IsPremiumMachine(12.6f, 6.9f))
            {
                failures.Add("⑦ 比值 1.83 应判 loaded（忙机形态）—— 否则机器负载会被误读成产物变慢。");
            }

            if (SmokeProbe.IsPremiumMachine(7f, 0f))
            {
                failures.Add("⑦ min = 0 应判 loaded（不可判）。");
            }

            // ⑤a 预测成本**必须真的上报**（`G6`）—— 源码级：控制器里必须有上报调用与累计字段
            //     （否则"加了上报 API 但没人调" ⇒ 冒烟的 `totalPredictSteps` 恒为 0，而字段存在性断言照样绿）。
            string assistPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".",
                "Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs");
            Assert.IsTrue(File.Exists(assistPath), "T131：找不到 AimAssistController.cs。");
            string assistCode = File.ReadAllText(assistPath);
            if (!assistCode.Contains("ReportPredictionCost("))
            {
                failures.Add("⑤ 控制器未调用 `ReportPredictionCost`（`G6`）⇒ 冒烟的步数证据会恒为 0。");
            }

            // ⚠ 必须写**累计使用形态**（`+=`）—— 只写 `TotalPredictSteps` 会命中**属性声明**
            //    （`AimAssistController.cs` 的 `public long TotalPredictSteps { get; private set; }`）
            //    ⇒ 「删掉累计行、保留声明」这种注入**照样绿**（同型空转；由复核注入证实、本批修）。
            if (!assistCode.Contains("TotalPredictSteps +="))
            {
                failures.Add("⑤ 控制器未累计 `TotalPredictSteps`（与机器无关的成本量；只声明不累计 ⇒ 步数证据恒为 0）。");
            }

            Assert.IsEmpty(failures, "T131 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T131: 解析 4 例 + 角函数（**独立**设计步长 {ExpectedDegreesPerFrame}°，"
                                  + $"{framesPerTurn} 帧整圈）+ JSON 字段 14 项 + 闸门阈值对 spec（16.666667 ms / 2:100）"
                                  + " + 执行体接线与「宽松闸门已删」守卫 + 场景预算 3 例 + 机器分类 3 例 ✓");
        }
    }
}
