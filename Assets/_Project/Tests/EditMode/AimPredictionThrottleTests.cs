using System.Collections.Generic;
using System.IO;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **拖动瞄准：重算节流 + 分配削减**（`T130`）。
    ///
    /// 动机（用户实机）：拖动瞄准线卡顿。定位到的热路径 = `AimAssistController.RefreshPrediction()`：
    /// 拖动时指纹**每帧都变** ⇒ 每帧跑一次 `ShotPredictor.Predict`（最多 240 步 × 17 球 × 82 段），
    /// 且旧指纹每帧对 17 球 × 4 个 float 走 `ToString("R") + StringBuilder`（每帧数十次格式化 + 分配）。
    ///
    /// 本判据三组：
    /// ① **节流纯函数真值表**（`ShouldRecompute`，不受计时影响 ⇒ 确定性）；
    /// ② **行为**：拖动中（间隔未到）**不重算**；输入停止后的**下一帧必重算**（**硬约束** —— 瞄准线不得停在旧值）；
    /// ③ **结构守卫**：控制器源码不得回退到字符串指纹（`ToString("R")` / `StringBuilder`）。
    ///
    /// **盲带**：真实帧率 / GC 不在 EditMode 判定；拖动帧时由构建产物的冒烟（`-billiards-smoke-aim`）取证。
    /// **注入负对照**：删掉尾沿分支（`settledSinceLastFrame` 直接 `return false`）⇒ ② 红；
    /// 把默认间隔改回 0（不节流）⇒ ② 的"拖动中不重算"红。
    /// </summary>
    public class AimPredictionThrottleTests
    {
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string ControllerSourcePath = "Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs";

        [Test]
        public void T130_AimRecomputeThrottle_TrailingEdgeGuarantee()
        {
            var failures = new List<string>();

            // ── ① 节流纯函数真值表 ──
            AssertDecision(failures, hasPrediction: false, stateChanged: true, settled: false,
                secondsSince: 0f, interval: 0.1f, expect: true, tag: "首算（无预测）⇒ 必算");
            AssertDecision(failures, hasPrediction: true, stateChanged: false, settled: false,
                secondsSince: 100f, interval: 0.1f, expect: false, tag: "脏标记优先（状态没变 ⇒ 不算）");
            AssertDecision(failures, hasPrediction: true, stateChanged: true, settled: true,
                secondsSince: 0f, interval: 0.1f, expect: true, tag: "**尾沿**：输入已停 ⇒ 必算（不受间隔约束）");
            AssertDecision(failures, hasPrediction: true, stateChanged: true, settled: false,
                secondsSince: 0.05f, interval: 0.1f, expect: false, tag: "前沿节流：未到间隔 ⇒ 本次不算");
            AssertDecision(failures, hasPrediction: true, stateChanged: true, settled: false,
                secondsSince: 0.2f, interval: 0.1f, expect: true, tag: "前沿：到间隔 ⇒ 算");
            AssertDecision(failures, hasPrediction: true, stateChanged: true, settled: false,
                secondsSince: 0f, interval: 0f, expect: true, tag: "间隔 ≤ 0 ⇒ 不节流");

            // ── ② 行为：拖动中节流 + 停拖后下一帧必为最新 ──
            var driveGo = new GameObject("T130Drive");
            var assistGo = new GameObject("T130Assist");
            try
            {
                var drive = driveGo.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
                Assert.IsNotNull(drive.tableConfig, "T130：需要 " + TablePath);
                drive.loadRackForBallBallDemo = true;
                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T130：Initialize 后应有内核。");
                for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.IsTrue(drive.Simulator.TableAtRest, "T130 前提：推进后应静止。");

                var assist = assistGo.AddComponent<AimAssistController>();
                assist.drive = drive;
                // 极大间隔 ⇒ 前沿必被节流；只有"尾沿"能通过 ⇒ 精确检验硬约束。
                assist.minRecomputeIntervalSeconds = 1000f;

                drive.enableShotPrediction = true;
                Assert.IsTrue(assist.RefreshPrediction(), "T130 前提：开启后第一次应重算。");
                int n0 = assist.PredictionComputeCount;

                Assert.IsFalse(assist.RefreshPrediction(), "T130 前提：同状态重复调用不应重算。");
                if (assist.PredictionComputeCount != n0)
                {
                    failures.Add($"② 同状态重复调用改了计数（{n0} → {assist.PredictionComputeCount}）。");
                }

                // 改变瞄准角 = 模拟"拖动中"⇒ 本帧应被前沿节流
                drive.aimDegrees += 7f;
                bool duringDrag = assist.RefreshPrediction();
                if (duringDrag)
                {
                    failures.Add("② 拖动中（间隔未到）竟重算了 ⇒ 前沿节流未生效。");
                }

                int n1 = assist.PredictionComputeCount;

                // 下一帧（状态不再变化）⇒ 尾沿：**必须**重算
                bool settled = assist.RefreshPrediction();
                if (!settled)
                {
                    failures.Add("② **尾沿失败**：输入停止后的下一帧没有重算 ⇒ 瞄准线会停在旧值（违反硬约束）。");
                }

                if (assist.PredictionComputeCount <= n1)
                {
                    failures.Add("② 尾沿重算未计入 PredictionComputeCount。");
                }

                TestContext.WriteLine($"T130: 真值表 6 例 ✓；拖动节流 + 尾沿（n0={n0} → {assist.PredictionComputeCount}）✓");

                // ── ③ 结构守卫：数值指纹（不得回退字符串指纹）──
                string full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", ControllerSourcePath);
                string code = StripCommentLines(File.ReadAllText(full));
                if (code.Contains("ToString(\"R\""))
                {
                    failures.Add("③ 控制器源码仍出现 `ToString(\"R\")` ⇒ 字符串指纹回归（每帧格式化/分配）。");
                }

                if (code.Contains("StringBuilder"))
                {
                    failures.Add("③ 控制器源码仍出现 `StringBuilder` ⇒ 字符串指纹回归。");
                }

                Assert.IsEmpty(failures, "T130 失败：\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(assistGo);
                Object.DestroyImmediate(driveGo);
            }
        }

        private static void AssertDecision(List<string> failures, bool hasPrediction, bool stateChanged,
            bool settled, float secondsSince, float interval, bool expect, string tag)
        {
            bool actual = AimAssistController.ShouldRecompute(hasPrediction, stateChanged, settled, secondsSince, interval);
            if (actual != expect)
            {
                failures.Add($"① [{tag}] 期望 {expect}，实测 {actual}。");
            }
        }

        /// <summary>去掉整行注释（`//` 起始，含 `///`）。</summary>
        private static string StripCommentLines(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                if (line.TrimStart().StartsWith("//", System.StringComparison.Ordinal))
                {
                    continue;
                }

                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }
    }
}
