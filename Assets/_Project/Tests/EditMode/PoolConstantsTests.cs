using Billiards.Core;
using NUnit.Framework;

namespace Billiards.Tests
{
    /// <summary>
    /// 验收测试：证明 EditMode 测试链路可用，且 PoolConstants 与美术交付包的
    /// 实测数据自洽。这些断言是后续所有几何/物理工作的基准，一旦被改动即说明
    /// 单位制或数据源发生了非预期变化。
    /// </summary>
    public class PoolConstantsTests
    {
        private const float Exact = 1e-9f;

        /// <summary>
        /// 原版用 <c>Assert.Pass</c>：**恒真、永不失败 ⇒ 零判别力**
        /// （全工程唯一一处）。它想表达"测试程序集接线正常"，
        /// 而**编译通过本身**已保证该点，故断言恒真。
        ///
        /// 现改为**可失败且有意义**的形式：断言 `PoolConstants` **真的位于 `Billiards.Runtime` 程序集**，
        /// 且**没有**被编进测试程序集。这同时守住一条分层规则：
        /// 若有人删掉/改坏 `Billiards.Runtime.asmdef`（代码会落进 `Assembly-CSharp` 或被测试程序集吞并），
        /// 本断言立刻失败。
        /// </summary>
        [Test]
        public void RuntimeCode_LivesInBilliardsRuntimeAssembly_NotInlinedIntoTestAssembly()
        {
            System.Reflection.Assembly runtime = typeof(PoolConstants).Assembly;
            System.Reflection.Assembly tests = typeof(PoolConstantsTests).Assembly;

            Assert.AreNotSame(runtime, tests,
                "Runtime 代码被编进了测试程序集 ⇒ `Billiards.Runtime.asmdef` 失效，分层被破坏。");
            Assert.AreEqual("Billiards.Runtime", runtime.GetName().Name,
                "`PoolConstants` 必须位于名为 `Billiards.Runtime` 的程序集。");
        }

        [Test]
        public void BallRadius_MatchesSourceMeasurement()
        {
            Assert.AreEqual(0.028575f, PoolConstants.BallRadius, Exact);
            Assert.AreEqual(0.05715f, PoolConstants.BallDiameter, Exact);
        }

        [Test]
        public void ClothGeometry_HalfDimensionsAreConsistent()
        {
            Assert.AreEqual(PoolConstants.ClothInnerLength, PoolConstants.ClothHalfLength * 2f, Exact);
            Assert.AreEqual(PoolConstants.ClothInnerWidth, PoolConstants.ClothHalfWidth * 2f, Exact);
        }

        [Test]
        public void ClothLength_IsNineFootSpecification()
        {
            // 9 尺台台面 = 100 英寸 = 2.540 m，应精确等于标称值。
            Assert.AreEqual(2.54f, PoolConstants.ClothInnerLength, Exact);
        }

        [Test]
        public void ClothWidth_DeviatesFromNominalByUnderOneMillimetre()
        {
            // 标称 50 英寸 = 1.27 m，源网格实测为 1.270108866 m。
            // 偏差 0.000108866 m ≈ 0.109 mm（相对 0.0086%）。
            // 本项目采用实测值以保证物理轮廓与渲染网格对齐；本断言把该偏差显式钉住，
            // 若未来有人把常量"修正"为标称 1.27，或数据源被替换，这里会立刻失败。
            const float nominalWidth = 1.27f;
            const float knownDeviation = 0.000108866f;

            float actualDeviation = PoolConstants.ClothInnerWidth - nominalWidth;

            Assert.Greater(actualDeviation, 0f, "短轴实测值应略长于标称值。");
            Assert.AreEqual(knownDeviation, actualDeviation, 1e-7f);
            Assert.Less(actualDeviation, 1e-3f, "偏差必须小于 1 mm，否则视为数据源异常。");
        }

        [Test]
        public void CaptureRadius_IsOpeningRadiusMinusBallRadiusMinusTwoMillimetres()
        {
            // 这是袋口数据的内部不变式，直接来自美术包的 captureNote：
            // "Recommended capture circle subtracts ball radius and a 2mm margin"。
            // 它同时证明 BallRadius 与袋口参数出自同一次测量，不存在拼凑。
            const float safetyMargin = 0.002f;

            float derived = PoolConstants.PocketOpeningRadius
                            - PoolConstants.BallRadius
                            - safetyMargin;

            Assert.AreEqual(derived, PoolConstants.PocketCaptureRadius, 1e-6f);
        }

        [Test]
        public void ClothPlane_IsWorldOrigin()
        {
            Assert.AreEqual(0f, PoolConstants.ClothHeightY, Exact);
        }

        [Test]
        public void CollisionTopology_MatchesSourceCounts()
        {
            Assert.AreEqual(82, PoolConstants.CushionSegmentCount);
            Assert.AreEqual(6, PoolConstants.PocketCount);
        }

        [Test]
        public void TableAspectRatio_DeviatesOnlyByMeasuredWidthError()
        {
            // 9 尺台标称 100 × 50 英寸，即严格 2:1。
            //
            // ⚠ **原版是近代数恒等式（已废弃）**：它把期望值从**被测常量自身**导出 ——
            //   `widthRelativeError = (ClothInnerWidth − 1.27) / 1.27`，`expectedRatio = 2 × (1 − 该值)`，
            //   而 `ClothInnerLength` 恰为 `2 × 1.27` ⇒ 等式两边代数等价。
            //   实测：把 `ClothInnerWidth` 改 ±1e-4 该断言**仍通过**（|Δ| ≤ 5e-8）⇒ 无判别力。
            //
            // 现改为**外部真值**：期望比值由「标称台面尺寸」与「独立测得的短轴实测值」在**测试内**算出，
            // 三个数都**不是** `PoolConstants` 的成员，故等式两侧无共享推导 —— 可失败。
            const double nominalLength = 2.540;          // 标称 100 英寸（外部规格）
            const double nominalWidth = 1.270;           // 标称 50 英寸（外部规格）
            const double measuredWidthDeviation = 0.000108866;   // 源网格实测短轴偏差（外部实测值）
            const double measuredWidth = nominalWidth + measuredWidthDeviation;

            double expectedRatio = nominalLength / measuredWidth;
            double actualRatio = PoolConstants.ClothInnerLength / PoolConstants.ClothInnerWidth;

            Assert.Less(actualRatio, 2.0, "短轴实测偏长，比值应略低于标称的 2。");
            Assert.AreEqual(expectedRatio, actualRatio, 1e-6,
                $"比值 {actualRatio:F9} 与外部真值 {expectedRatio:F9} 不符 ⇒ 内库尺寸常量被改动。");

            // 两道独立边界（都是可失败的）：
            Assert.Less(2.0 - actualRatio, 2e-4,
                "比值偏离 2 的幅度必须能被实测短轴偏差解释（>2e-4 说明短轴被改错）。");
            Assert.Greater(2.0 - actualRatio, 1e-5,
                "比值必须真的偏离 2 —— 若为 0，说明短轴被\"修正\"回了标称 1.27（本项目刻意采用实测值）。");
        }

        [Test]
        public void BallFitsInsideAllTableClearances()
        {
            Assert.Less(PoolConstants.BallDiameter, PoolConstants.ClothInnerWidth);
            Assert.Less(PoolConstants.BallDiameter, PoolConstants.CushionNoseHeight * 2f);
            Assert.Less(PoolConstants.PocketCaptureRadius, PoolConstants.PocketOpeningRadius);
        }

        [Test]
        public void PresentationScale_IsAppliedSeparatelyFromPhysicsUnits()
        {
            // "桌面扩大 20%" 只允许作用于表现层，物理必须保持规范米制。
            Assert.AreEqual(1.2f, PoolConstants.PlayfieldVisualScale, Exact);
            Assert.AreEqual(1f, PoolConstants.MetresPerUnit, Exact);
        }

        [Test]
        public void CueDimensions_MatchArtAsset()
        {
            Assert.AreEqual(1.4f, PoolConstants.CueLength, Exact);
        }

        [Test]
        public void SourceProvenance_IsRecorded()
        {
            Assert.AreEqual(19954, PoolConstants.SourceTableTriangles);
            Assert.AreEqual(2.550047226954685f, PoolConstants.SourceTableUniformScale, 1e-6f);
        }
    }
}
