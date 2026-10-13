using Billiards.Core;
using NUnit.Framework;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 1 验收测试：证明 EditMode 测试链路可用，且 PoolConstants 与美术交付包的
    /// 实测数据自洽。这些断言是后续所有几何/物理工作的基准，一旦被改动即说明
    /// 单位制或数据源发生了非预期变化。
    /// </summary>
    public class PoolConstantsTests
    {
        private const float Exact = 1e-9f;

        [Test]
        public void TableTestAssembly_IsWired()
        {
            // 本测试存在的意义：若 EditMode 测试程序集未正确接入，本类不会被执行。
            Assert.Pass("EditMode 测试程序集已接入 Billiards.Runtime。");
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
            // 9 尺台标称 100 × 50 英寸，即严格 2:1（2.54 = 2 × 1.27）。
            // 短轴实测值比标称长 0.109 mm，相对误差 eps ≈ 8.57e-5，
            // 因此比值 = 2 / (1 + eps) ≈ 2 × (1 - eps)，偏离 2 约 1.714e-4。
            //
            // 这里刻意不硬编码期望比例，而是从"短轴相对误差"推导：
            // 本测试第一版把偏差写成 1.72e-5，比真实值小一个量级，
            // 被断言当场拦下。推导式不依赖人工换算，杜绝同类错误。
            float widthRelativeError =
                (PoolConstants.ClothInnerWidth - 1.27f) / 1.27f;
            float expectedRatio = 2f * (1f - widthRelativeError);

            float actualRatio = PoolConstants.ClothInnerLength / PoolConstants.ClothInnerWidth;

            Assert.Less(actualRatio, 2f, "短轴实测偏长，比值应略低于 2。");
            Assert.AreEqual(expectedRatio, actualRatio, 1e-6f);
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
