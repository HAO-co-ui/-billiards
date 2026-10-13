using System.IO;
using Billiards.Core;
using Billiards.Diagnostics;
using Billiards.EditorTools;
using NUnit.Framework;

namespace Billiards.Tests
{
    /// <summary>
    /// **错误路径**测试：注入畸形 DTO，断言 <see cref="TableDataImporter.ValidateTopology"/> 抛异常。
    ///
    /// **为什么需要这个文件**（D4-FIX-03 D2）：本工程此前 <c>Assert.Throws</c> **全工程 = 0 处** ——
    /// 即**所有错误处理都从未被测试过**。根因是 <c>ValidateTopology</c> 原本是 <c>private</c>，
    /// 唯一调用者 <c>Import()</c> 用写死的文件路径，测试无法注入畸形输入 ⇒ **错误路径不可测**。
    /// 本轮把它开放为 <c>public</c> 纯函数，因此这里可以逐条覆盖它的每个 <c>throw</c>。
    ///
    /// **判别力自证**：本文件含 **1 条正向**（合法 DTO 必须**不**抛）与 **5 条负向**。
    /// 正负成对是刻意的 —— **只有负向测试的话，"永远抛异常"的实现会全部通过**；
    /// 有了正向测试，"永远抛"必然在 <see cref="ValidTopology_PassesValidation"/> 上失败。
    /// 反之，若把某处 <c>throw</c> 改成静默返回，对应的负向测试必然失败。
    /// **两个方向都必须能失败，这组测试才算有判别力。**
    /// </summary>
    public class TableImporterErrorPathTests
    {
        private const string Src = "synthetic";

        /// <summary>构造一份**恰好合法**的元数据，供各用例在其上做单点破坏。</summary>
        private static TableMetaDto ValidMeta()
        {
            var meta = new TableMetaDto { collision = new CollisionDto() };

            meta.collision.segments = new SegmentDto[PoolConstants.CushionSegmentCount];
            for (int i = 0; i < meta.collision.segments.Length; i++)
            {
                meta.collision.segments[i] = new SegmentDto
                {
                    a = new Vec2Dto { x = 0f, y = 0f },
                    b = new Vec2Dto { x = 1f, y = 0f },
                    rail = "synthetic",
                };
            }

            meta.collision.pockets = new PocketDto[PoolConstants.PocketCount];
            for (int i = 0; i < meta.collision.pockets.Length; i++)
            {
                meta.collision.pockets[i] = new PocketDto
                {
                    index = i,
                    kind = "corner",
                    runtimeCenter = new Vec2Dto(),
                    runtimeMouth = new[] { new Vec2Dto(), new Vec2Dto() },
                    runtimeThroat = new[] { new Vec2Dto(), new Vec2Dto() },
                    runtimeThroatCenter = new Vec2Dto(),
                    runtimeThroatNormal = new Vec2Dto { x = 0f, y = -1f },
                    openingRadius = 0.06f,
                    captureRadius = 0.03f,
                };
            }

            return meta;
        }

        // ── 正向：合法输入必须**不**抛。这是"永远抛异常"实现的杀手。 ──────────────

        [Test]
        public void ValidTopology_PassesValidation()
        {
            Assert.DoesNotThrow(() => TableDataImporter.ValidateTopology(ValidMeta(), Src),
                "合法拓扑不得抛异常 —— 否则本文件其余负向测试的通过毫无意义（恒抛即恒过）。");
        }

        // ── 负向：逐条覆盖 ValidateTopology 的每个 throw ──────────────────────

        [Test]
        public void NullSegments_Throws()
        {
            TableMetaDto meta = ValidMeta();
            meta.collision.segments = null;

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                "segments 为 null 必须抛 InvalidDataException。");
        }

        [Test]
        public void WrongSegmentCount_Throws()
        {
            TableMetaDto meta = ValidMeta();
            var shortened = new SegmentDto[PoolConstants.CushionSegmentCount - 1];
            for (int i = 0; i < shortened.Length; i++)
            {
                shortened[i] = meta.collision.segments[i];
            }

            meta.collision.segments = shortened;

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                $"段数 != {PoolConstants.CushionSegmentCount} 必须抛。");
        }

        [Test]
        public void NullPockets_Throws()
        {
            TableMetaDto meta = ValidMeta();
            meta.collision.pockets = null;

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                "pockets 为 null 必须抛 InvalidDataException。");
        }

        [Test]
        public void PocketMouthLengthNotTwo_Throws()
        {
            TableMetaDto meta = ValidMeta();
            meta.collision.pockets[0].runtimeMouth = new[] { new Vec2Dto() };   // 长度 1，应为 2

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                "袋口 mouth 长度为 1（应 2）必须抛。");
        }

        [Test]
        public void PocketThroatNormalMissing_Throws()
        {
            TableMetaDto meta = ValidMeta();
            meta.collision.pockets[PoolConstants.PocketCount - 1].runtimeThroatNormal = null;

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                "袋喉法向缺失必须抛（袋口捕获语义依赖它）。");
        }

        [Test]
        public void PocketRadiusNonPositive_Throws()
        {
            TableMetaDto meta = ValidMeta();
            meta.collision.pockets[0].captureRadius = 0f;

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                "捕获半径非正必须抛。");
        }

        [Test]
        public void OpeningRadiusNonPositive_Throws()
        {
            TableMetaDto meta = ValidMeta();
            meta.collision.pockets[0].openingRadius = -1f;

            Assert.Throws<InvalidDataException>(() => TableDataImporter.ValidateTopology(meta, Src),
                "开口半径非正必须抛。");
        }
    }
}
