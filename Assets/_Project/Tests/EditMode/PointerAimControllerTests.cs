using System.Collections.Generic;
using Billiards.Core;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **拖拽瞄准 / 松开锁定**（`T50`）与**方向键微调粒度**（`T51`）的判据。
    ///
    /// ── 为什么不放进 `PointerAimTests.cs` ─────────────────────────────────────
    /// `PointerAimTests.cs` 的 `T45`–`T49` 是**切片一已入库证据**（`6e6847e` 起），本单**不得改动其任何一行**。
    /// ⇒ 切片二的判据落**新文件**（编号承 `T50`/`T51`，spec §5 的编号不变）。
    ///
    /// ── 期望值来源 ───────────────────────────────────────────────────────────
    /// · `T50`：**契约语义**（`AimLock` = "按下拖拽瞄准，松开锁定方向"，仓库内唯一陈述见
    ///   `Docs/35-A_Day1_工程基建_执行记录.md`）。
    /// · `T51`：排期"方向键微调（粒度约 **0.2°**）" + `PoolConstants`/场景**序列化**值 + 代码默认（新实例探针）。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · `T50` **不测输入设备**（真鼠标按下/松开/移动）—— EditMode 不跑 `Update`、也不碰设备；那属 Manual/PlayMode。
    /// · `T50` 断言的是**状态机**（`PointerAimState`）与"指针→角度"的合成；`PointerAimController.Update` 的
    ///   **输入读取**不被覆盖（同盲带）。
    /// · `T51` **不测**"`→` 在屏幕上使瞄准线顺/逆时针"（依赖相机方位，spec 标为待实测定值）。
    /// </summary>
    public class PointerAimControllerTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        private static PhysicsTestDrive FindDrive(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                PhysicsTestDrive drive = go.GetComponentInChildren<PhysicsTestDrive>(true);
                if (drive != null)
                {
                    return drive;
                }
            }

            return null;
        }

        // ══════════════════ T50 拖拽瞄准 / 松开锁定 ═══════════════════════════════

        /// <summary>
        /// **三段序列**（纯状态机驱动，不碰输入设备）：
        /// ① `lockHeld=true` + 指针移动 ⇒ 方向**随指针更新**；
        /// ② `lockHeld=false`（松开）+ 指针继续移动 ⇒ 方向**逐位不变**（冻结）；
        /// ③ `CancelAim` 解锁 → 再 `lockHeld=true` + 移动 ⇒ **恢复更新**。
        ///
        /// **期望值来源**：契约语义（`AimLock` = "按下拖拽瞄准，松开锁定方向"）。
        /// **判别力来源**：把"松开后仍在跟随"（漏冻结）与"按下时不更新"（状态写反）各自变成可判定失败。
        /// **注入负对照**：① `ShouldFollowPointer` 恒 `true`（漏冻结）⇒ 第 ② 段 FAIL；
        /// ② `ShouldFollowPointer = (Phase == Locked)`（写反）⇒ 第 ① 段 FAIL。
        /// </summary>
        [Test]
        public void T50_DragAim_Then_ReleaseLocks()
        {
            var state = new PointerAimState();
            var failures = new List<string>();

            // 模型：交互层每帧做"若状态机说该跟随 ⇒ 目标角 = 指针位置导出的角"。此处用确定性映射 角 := 指针.x（度）。
            float aim = 100f;

            bool Step(bool lockHeld, bool cancel, float pointerX)
            {
                bool follow = state.Advance(lockHeld, cancel);
                if (follow)
                {
                    aim = pointerX;
                }

                return follow;
            }

            // ── ① 按住拖拽：方向随指针更新 ───────────────────────────────────────
            bool a0 = Step(true, false, 10f);
            float aim1 = aim;
            Step(true, false, 20f);
            Step(true, false, 30f);
            float aimEnd1 = aim;
            bool changedInSegment1 = !(Mathf.Approximately(aim1, aimEnd1)) ;

            if (!a0)
            {
                failures.Add($"① 按下（lockHeld=true）时状态机应允许跟随，实际返回 false。");
            }

            if (!changedInSegment1)
            {
                failures.Add($"① 按住并移动指针后方向未更新（aim 停在 {aimEnd1}）⇒ 拖拽跟随失效。");
            }

            if (!(Mathf.Approximately(aimEnd1, 30f)))
            {
                failures.Add($"① 按住拖拽结束时应等于最后指针位（期望 30，实际 {aimEnd1}）。");
            }

            if (state.Phase != AimPhase.Dragging)
            {
                failures.Add($"① 按住期间阶段应为 Dragging，实际 {state.Phase}。");
            }

            // ── ② 松开：方向冻结（逐位不变）─────────────────────────────────────
            float frozen = aim;
            bool anyFollowedInSegment2 = false;
            for (int i = 0; i < 3; i++)
            {
                if (Step(false, false, 40f + 10f * i))
                {
                    anyFollowedInSegment2 = true;
                }

                if (aim != frozen)
                {
                    failures.Add($"② 松开（lockHeld=false）后指针移动竟改写了方向：{frozen} → {aim}（冻结失效）。");
                    break;
                }
            }

            if (anyFollowedInSegment2)
            {
                failures.Add("② 松开后状态机仍返回\"应跟随\"（应冻结）。");
            }

            if (state.Phase != AimPhase.Locked)
            {
                failures.Add($"② 松开后阶段应为 Locked，实际 {state.Phase}。");
            }

            // ── ③ CancelAim 解锁 → 再按住 ⇒ 恢复更新 ────────────────────────────
            bool afterCancel = Step(false, true, 55f);
            if (!afterCancel)
            {
                failures.Add("③ CancelAim 后状态机应恢复跟随（返回 false）。");
            }

            if (state.Phase != AimPhase.Free)
            {
                failures.Add($"③ CancelAim 后阶段应为 Free，实际 {state.Phase}。");
            }

            Step(true, false, 70f);
            if (!(Mathf.Approximately(aim, 70f)))
            {
                failures.Add($"③ 解锁后再次按住并移动应恢复更新（期望 70，实际 {aim}）。");
            }

            if (state.Phase != AimPhase.Dragging)
            {
                failures.Add($"③ 再次按住后阶段应为 Dragging，实际 {state.Phase}。");
            }

            Assert.IsEmpty(failures, "T50 失败判据：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T50: ① 变（100→30）② 冻结（逐位 == {frozen}）③ 又变（→70）；阶段 Free→Dragging→Locked→Free→Dragging ✓");
        }

        // ══════════════════ T51 微调粒度（0.2° / 1.0°，且界 == 场景值）═══════════

        /// <summary>
        /// **三条**（缺一不可）：
        /// ① 场景**序列化** `PhysicsTestDrive.aimStepDegrees == 1.0`（且 == 新实例的**代码默认**）；
        /// ② 细调系数常量 == `0.2`；
        /// ③ 一次微调的实际变化量：细调 `== 0.2°`、基础 `== 1.0°`（容差 1e-3°），且符号 (`←` 为 `+`)。
        ///
        /// **期望值来源**：排期"粒度约 0.2°" + 场景序列化值 + 代码默认（新实例探针）。
        /// **判别力来源**：把"0.2° 是基础还是细调"钉死；并防"场景把基础改成 0.2° ⇒ 细调变 0.04°"的静默退化。
        /// **注入负对照**：① 场景 `aimStepDegrees` 改 `0.2` ⇒ ①③ FAIL；
        /// ② 细调系数改 `0.5` ⇒ ③ FAIL。
        /// </summary>
        [Test]
        public void T51_NudgeGranularity_And_SceneValue()
        {
            var failures = new List<string>();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = FindDrive(scene);
                Assert.IsNotNull(drive, "T51：场景内找不到 PhysicsTestDrive。");

                // ① 场景序列化值 == 意图值（契约常量 1.0）
                float sceneStep = drive.aimStepDegrees;
                if (!(Mathf.Abs(sceneStep - 1.0f) <= 1e-6f))
                {
                    failures.Add($"① 场景序列化 aimStepDegrees = {sceneStep:R} ≠ 意图 1.0（基础步长被改）。");
                }

                // ①' 场景值 == 代码默认（防"代码改了、场景没同步"的 F-10 型假绿）
                var probeGo = new GameObject("T51Probe");
                try
                {
                    var probe = probeGo.AddComponent<PhysicsTestDrive>();
                    if (!(Mathf.Abs(probe.aimStepDegrees - sceneStep) <= 1e-6f))
                    {
                        failures.Add($"①' 场景 aimStepDegrees {sceneStep:R} ≠ 代码默认 {probe.aimStepDegrees:R}（场景漂移）。");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(probeGo);
                }

                // ② 细调系数 == 0.2（排期"粒度约 0.2°"）
                if (!(Mathf.Abs(PointerAimController.FineAimStepFactor - 0.2f) <= 1e-6f))
                {
                    failures.Add($"② 细调系数 = {PointerAimController.FineAimStepFactor:R} ≠ 0.2。");
                }

                // ③ 行为：细调 0.2°、基础 1.0°（用**场景值**当基础步长，与运行时同一入口）
                float fine = PointerAimController.ApplyNudgeDegrees(100f, 1f, true, sceneStep);
                float coarse = PointerAimController.ApplyNudgeDegrees(100f, 1f, false, sceneStep);

                if (!(Mathf.Abs(fine - 100.2f) <= 1e-3f))
                {
                    failures.Add($"③ 细调一步应使角变化 +0.2°（100 → 100.2），实际 {fine:R}。");
                }

                if (!(Mathf.Abs(coarse - 101.0f) <= 1e-3f))
                {
                    failures.Add($"③ 基础一步应使角变化 +1.0°（100 → 101.0），实际 {coarse:R}。");
                }

                // ③' 键向（物理平面符号）：+轴向增角、−轴向减角
                float plus = PointerAimController.ApplyNudgeDegrees(100f, 1f, false, sceneStep);
                float minus = PointerAimController.ApplyNudgeDegrees(100f, -1f, false, sceneStep);
                if (!(plus > 100f && minus < 100f))
                {
                    failures.Add($"③' 键向错误：+1 应增角（得 {plus:R}）、−1 应减角（得 {minus:R}）。");
                }

                Assert.IsEmpty(failures, "T51 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine(
                    $"T51: 场景 aimStepDegrees={sceneStep:R}（== 代码默认）；细调系数={PointerAimController.FineAimStepFactor:R}"
                    + $" ⇒ 细调 {fine:R} / 基础 {coarse:R} ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
