using Billiards.EditorTools;
using Billiards.Physics;
using NUnit.Framework;

namespace Billiards.Tests
{
    /// <summary>
    /// 灰盒场景与场景驱动的守卫测试。
    ///
    /// 两条独立的判据：
    ///  1. <see cref="PhysicsTestDrive.WholeSteps"/> —— 累加器折算的边界语义（纯函数，可失败）；
    ///  2. 灰盒场景结构扫描 —— 与 <see cref="PhysicsValidator"/> **共用同一实现**
    ///     （`ScanGreyboxScene`，铁律 16 单点），并带**非空转证据**（根对象数 &gt; 0，R4-3）。
    ///
    /// 本测试**只读**：不写 Assets、不保存场景、不产生仓库改动（R3-5）。
    /// </summary>
    public class GreyboxSceneTests
    {
        private const float Step = PoolSimulator.FixedDeltaTime;

        // ── 1) 累加器折算 ──────────────────────────────────────────────────

        /// <summary>恰好一个步长必须算 1 步（边界用 <c>&gt;=</c>；写成 <c>&gt;</c> 会退化为 0，本用例即失败）。</summary>
        [Test]
        public void WholeSteps_ExactlyOneStepAtBoundary()
        {
            Assert.AreEqual(1, PhysicsTestDrive.WholeSteps(Step, Step),
                "累计恰好等于一个固定步长时必须算 1 步（边界语义为 >=）。");
        }

        /// <summary>余量必须**向下取整**且不丢：0.02 s = 2.4 × (1/120 s) ⇒ 2 步。</summary>
        [Test]
        public void WholeSteps_TruncatesRemainder()
        {
            Assert.AreEqual(2, PhysicsTestDrive.WholeSteps(0.02f, Step),
                "0.02 s 应折算为 2 步（2.4 向下取整），余 0.02 - 2×dt 留给下一帧。");
        }

        /// <summary>不足一步必须给 0（用 ceil 或四舍五入的实现会给出 1，本用例即失败）。</summary>
        [Test]
        public void WholeSteps_BelowOneStep_ReturnsZero_NotOne()
        {
            Assert.AreEqual(0, PhysicsTestDrive.WholeSteps(0.001f, Step),
                "0.001 s < dt ⇒ 0 步；返回 1 意味着用了 ceil/round，会系统性多推进。");
        }

        /// <summary>非正步长必须安全返回 0，而不是除零/死循环。</summary>
        [Test]
        public void WholeSteps_NonPositiveStep_ReturnsZero()
        {
            Assert.AreEqual(0, PhysicsTestDrive.WholeSteps(1f, 0f), "步长 0 必须返回 0。");
            Assert.AreEqual(0, PhysicsTestDrive.WholeSteps(1f, -1f), "负步长必须返回 0。");
        }

        // ── 2) 灰盒场景结构（与验收器共用单点实现）──────────────────────────

        /// <summary>
        /// 灰盒场景必须存在、**恰有** 1 个 <c>CushionGizmo</c> 与 1 个 <c>PhysicsTestDrive</c>，
        /// 且扫描**非空转**（根对象数 &gt; 0）—— 否则"找到 0 个"可能只是"没扫到场景"。
        /// </summary>
        [Test]
        public void PhysicsGreyboxScene_HasExactlyOneGizmoAndOneDrive_NonVacuous()
        {
            PhysicsValidator.GreyboxScan scan = PhysicsValidator.ScanGreyboxScene();

            Assert.IsTrue(scan.SceneFileExists, "灰盒场景必须存在：" + GreyboxSceneSetup.ScenePath);
            Assert.Greater(scan.RootObjectCount, 0,
                "扫描非空转：场景必须至少扫到 1 个根对象（否则下面的 1/1 断言恒真）。");
            Assert.AreEqual(1, scan.GizmoCount, "CushionGizmo 必须恰有 1 个。" + scan.Detail);
            Assert.AreEqual(1, scan.DriveCount, "PhysicsTestDrive 必须恰有 1 个。" + scan.Detail);
        }
    }
}
