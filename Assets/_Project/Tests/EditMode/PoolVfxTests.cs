using System.Collections.Generic;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Billiards.Tests
{
    /// <summary>
    /// **VFX 纯逻辑** —— `T92`（动作曲线）/ `T93`（接触推导）/ `T94`（限频门）。
    ///
    /// 期望值来源：**甲**（闭式 / 构造用例，与被测对象无关）＋ **乙**（契约常量）。
    /// **盲带**：特效观感非判据可测（批处理无渲染）；LineRenderer 顶点与 `SyncBallVisual` 的下沉接线
    /// 属表现层盲带（曲线函数有判据）。
    /// **注入负对照（三档实测）**：`alpha` 幂律改线性 ⇒ `T92` (c) 红（0.29 ≠ 0.178515866）；
    /// speed 门改 0 ⇒ `T93` **(c) 低速对**红（**不是** (c') 纯静止对 —— 静止对 `speed=0`
    /// 会被 `quality > 0` 的严格比较兜住，无判别力）；`IntervalGate` 间隔改 0 ⇒ `T94`
    /// 「间隔内（0.02 < 0.045）应拒绝」红。三档均逐字节还原（sha1 一致）。
    /// </summary>
    public class PoolVfxTests
    {
        // ════════════ T92 动作曲线（扩张 / 幂律渐隐 / 下沉）═══════════════════

        [Test]
        public void T92_VfxPose_ClosedForm()
        {
            var failures = new List<string>();

            // (a) p=0 ⇒ scale = radius、alpha = opacity
            PoolVfxView.VfxPose(0f, 0.011f, 0.5f, 0.58f, out float s0, out float a0);
            if (Mathf.Abs(s0 - 0.011f) > 1e-6f || Mathf.Abs(a0 - 0.58f) > 1e-6f)
            {
                failures.Add($"(a) p=0 应 (scale=0.011, alpha=0.58)，实际 ({s0:R}, {a0:R})。");
            }

            // (b) p=1 ⇒ alpha = 0
            PoolVfxView.VfxPose(1f, 0.011f, 0.5f, 0.58f, out float s1, out float a1);
            if (Mathf.Abs(a1) > 1e-6f)
            {
                failures.Add($"(b) p=1 应 alpha=0，实际 {a1:R}。");
            }

            // (c) p=0.5 ⇒ scale = r×(1+0.5e) = 0.01375；alpha = o×0.5^1.7 = 0.58×0.307786（独立十进制）
            PoolVfxView.VfxPose(0.5f, 0.011f, 0.5f, 0.58f, out float s5, out float a5);
            const float expectScale = 0.011f * 1.25f;
            const float expectAlpha = 0.58f * 0.307786f;
            if (Mathf.Abs(s5 - expectScale) > 1e-6f)
            {
                failures.Add($"(c) p=0.5 扩张应 {expectScale:R}，实际 {s5:R}。");
            }

            if (Mathf.Abs(a5 - expectAlpha) > 1e-6f)
            {
                failures.Add($"(c) p=0.5 渐隐应 {expectAlpha:R}（幂律 1.7，非线性的 0.29），实际 {a5:R}。");
            }

            // (d) 下沉曲线 0/0.5/1 ⇒ (0,1)/(−0.04,0.5)/(−0.08,0)
            PoolVfxView.PocketSinkPose(0f, out float y0, out float sc0);
            PoolVfxView.PocketSinkPose(0.5f, out float y5, out float sc5);
            PoolVfxView.PocketSinkPose(1f, out float y1, out float sc1);
            if (Mathf.Abs(y0) > 1e-6f || Mathf.Abs(sc0 - 1f) > 1e-6f
                || Mathf.Abs(y5 + 0.04f) > 1e-6f || Mathf.Abs(sc5 - 0.5f) > 1e-6f
                || Mathf.Abs(y1 + 0.08f) > 1e-6f || Mathf.Abs(sc1) > 1e-6f)
            {
                failures.Add($"(d) 下沉曲线应为 (0,1)/(−0.04,0.5)/(−0.08,0)，"
                             + $"实际 ({y0:R},{sc0:R})/({y5:R},{sc5:R})/({y1:R},{sc1:R})。");
            }

            // (e) 入袋动画必须"**先滑到洞口、再落下**"（用户实测："贴着库边原地慢慢往下掉，像滑墙"）。
            //     球被冻结/判入的位置常离洞口几十毫米 ⇒ 只做垂直下沉就会被看成"滑库边"。
            Vector3 origin = new Vector3(0.30f, PoolConstants.BallRadius, -0.20f);
            Vector3 hole = PoolGeometry.PhysicsToUnity(new Vector2(0f, -0.6951f), PoolConstants.BallRadius);
            Vector3 q0 = PoolVfxView.PocketSinkLocalPosition(origin, hole, 0f);
            Vector3 q6 = PoolVfxView.PocketSinkLocalPosition(origin, hole, PoolVfxView.SinkSlideFraction);
            Vector3 q1 = PoolVfxView.PocketSinkLocalPosition(origin, hole, 1f);

            if ((new Vector3(q0.x, 0f, q0.z) - new Vector3(origin.x, 0f, origin.z)).magnitude > 1e-6f)
            {
                failures.Add($"(e) p=0 应停在原位，实际水平 {q0}（原位 {origin}）。");
            }

            if (Mathf.Abs(q6.x - hole.x) > 1e-6f || Mathf.Abs(q6.z - hole.z) > 1e-6f
                || Mathf.Abs(q1.x - hole.x) > 1e-6f || Mathf.Abs(q1.z - hole.z) > 1e-6f)
            {
                failures.Add($"(e) 滑动段结束（p={PoolVfxView.SinkSlideFraction:R}）起应已到洞口水平位置 "
                             + $"{hole}，实际 p={PoolVfxView.SinkSlideFraction:R} → {q6}、p=1 → {q1}"
                             + " ⇒ 会在库边原地往下掉。");
            }

            if (!(q0.y > q6.y) || !(q6.y > q1.y))
            {
                failures.Add($"(e) 垂直应单调下沉，实际 {q0.y:R}/{q6.y:R}/{q1.y:R}。");
            }

            if (Mathf.Abs(q1.y - (origin.y - 0.08f)) > 1e-6f)
            {
                failures.Add($"(e) 终点下沉量应为 −0.08，实际 {q1.y - origin.y:R}。");
            }

            Assert.IsEmpty(failures, "T92 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(
                $"T92: scale {s0:R}/{s5:R}/{s1:R}；alpha {a0:R}/{a5:R}/{a1:R}；下沉曲线三段 ✓");
        }

        // ════════════ T93 接触推导（质量排序 + 门）════════════════════════════

        [Test]
        public void T93_ContactDerivation_QualityAndGates()
        {
            const float r = 0.028575f;
            var failures = new List<string>();

            // (a) 接触且快速 ⇒ 找到（中点 / 连心线角 / 对速度和）
            var pos = new List<Vector2> { new Vector2(0f, 0f), new Vector2(2f * r + 0.001f, 0f) };
            var vel = new List<Vector2> { new Vector2(1f, 0f), new Vector2(-1f, 0f) };
            bool hit = PoolVfxView.FindHitContact(pos, vel, r, out Vector2 hp, out float hang, out float hs);
            if (!hit)
            {
                failures.Add("(a) 接触且快速 ⇒ 应找到。");
            }
            else
            {
                if (Mathf.Abs(hp.x - (pos[0].x + pos[1].x) * 0.5f) > 1e-6f || Mathf.Abs(hp.y) > 1e-6f)
                {
                    failures.Add($"(a) 接触点应为两球中点，实际 ({hp.x:R}, {hp.y:R})。");
                }

                if (Mathf.Abs(hang) > 1e-3f)
                {
                    failures.Add($"(a) 连心线角度应为 0°，实际 {hang:R}。");
                }

                if (Mathf.Abs(hs - 2f) > 1e-5f)
                {
                    failures.Add($"(a) 对速度和应为 2，实际 {hs:R}。");
                }
            }

            // (b) 相距 1 m ⇒ 未找到
            pos = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f) };
            if (PoolVfxView.FindHitContact(pos, vel, r, out _, out _, out _))
            {
                failures.Add("(b) 相距 1 m ⇒ 不应找到。");
            }

            // (c) speed 门：贴距但**对速 0.08 < 0.1**（低速非零）⇒ 未找到。
            //     ⚠ 用"低速"而非"纯静止"：静止对 speed=0 ⇒ quality=0 会被"quality > 0"的严格比较
            //     意外兜住（对"门失效"注入**无判别力**）——低速对的 quality > 0 才会暴露门失效（实测教训）。
            var slowVel = new List<Vector2> { new Vector2(0.04f, 0f), new Vector2(-0.04f, 0f) };
            pos = new List<Vector2> { new Vector2(0f, 0f), new Vector2(2f * r + 0.001f, 0f) };
            if (PoolVfxView.FindHitContact(pos, slowVel, r, out _, out _, out _))
            {
                failures.Add("(c) 低速对（对速 0.08 < 0.1）⇒ 不应找到（speed 门）。");
            }

            // (c') 纯静止同样不应找到（语义兜底；对"门失效"注入无判别力——如实登记）。
            var stillVel = new List<Vector2> { Vector2.zero, Vector2.zero };
            if (PoolVfxView.FindHitContact(pos, stillVel, r, out _, out _, out _))
            {
                failures.Add("(c') 纯静止对 ⇒ 不应找到。");
            }

            // (d) 质量排序：A 对 (gap 0.006, speed 0.3 ⇒ q≈37.5) vs B 对 (gap 0.0001, speed 2 ⇒ q≈952)
            //     ⇒ 选中 B 对中点（0.5 高度处）。
            pos = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(2f * r + 0.006f, 0f),
                new Vector2(0f, 0.5f), new Vector2(2f * r + 0.0001f, 0.5f),
            };
            vel = new List<Vector2>
            {
                new Vector2(0.2f, 0f), new Vector2(0.1f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 0f),
            };
            if (!PoolVfxView.FindHitContact(pos, vel, r, out Vector2 qp, out _, out float qs))
            {
                failures.Add("(d) 两对均接触 ⇒ 应找到（质量最高者）。");
            }
            else
            {
                if (Mathf.Abs(qp.y - 0.5f) > 1e-6f)
                {
                    failures.Add($"(d) 应选质量最高的 B 对（y=0.5 处），实际 y={qp.y:R}。");
                }

                if (Mathf.Abs(qs - 2f) > 1e-5f)
                {
                    failures.Add($"(d) B 对速度和应为 2，实际 {qs:R}。");
                }
            }

            // (e) 撞库：球在竖段 (1,−1)-(1,1) 左侧 r+0.002、朝 +x 运动 ⇒ 找到（垂足 (1,0)、角度 90°）
            var segments = new List<CushionSegment>
            {
                new CushionSegment { a = new Vector2(1f, -1f), b = new Vector2(1f, 1f), rail = "test" },
            };
            pos = new List<Vector2> { new Vector2(1f - r - 0.002f, 0f) };
            vel = new List<Vector2> { new Vector2(1f, 0f) };
            if (!PoolVfxView.FindRailContact(pos, vel, segments, r, out Vector2 rp, out float rang, out float rs))
            {
                failures.Add("(e) 贴库且快速 ⇒ 应找到。");
            }
            else
            {
                if (Mathf.Abs(rp.x - 1f) > 1e-5f || Mathf.Abs(rp.y) > 1e-5f)
                {
                    failures.Add($"(e) 垂足应为 (1,0)，实际 ({rp.x:R}, {rp.y:R})。");
                }

                if (Mathf.Abs(Mathf.Abs(rang) - 90f) > 1e-3f)
                {
                    failures.Add($"(e) 段方向角应为 ±90°，实际 {rang:R}。");
                }

                if (Mathf.Abs(rs - 1f) > 1e-5f)
                {
                    failures.Add($"(e) 球速应为 1，实际 {rs:R}。");
                }
            }

            // (f) 离库 0.2 m ⇒ 未找到
            pos = new List<Vector2> { new Vector2(1f - r - 0.2f, 0f) };
            if (PoolVfxView.FindRailContact(pos, vel, segments, r, out _, out _, out _))
            {
                failures.Add("(f) 离库 0.2 m ⇒ 不应找到。");
            }

            Assert.IsEmpty(failures, "T93 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T93: 碰球推导 4 例（接触/远距/静止/质量排序）+ 撞库 2 例 ✓");
        }

        // ════════════ T94 限频门 ═══════════════════════════════════════════

        [Test]
        public void T94_IntervalGate_Throttles()
        {
            var failures = new List<string>();
            var gate = new PoolVfxView.IntervalGate(0.045f);

            bool first = gate.TryPass(1.00f);          // 首次必过
            bool tooSoon = gate.TryPass(1.02f);        // 0.02 < 0.045 ⇒ 拒
            bool ok = gate.TryPass(1.05f);             // 0.05 ≥ 0.045 ⇒ 过
            gate.Reset();
            bool afterReset = gate.TryPass(1.06f);     // Reset 后首过

            if (!first)
            {
                failures.Add("首次应通过。");
            }

            if (tooSoon)
            {
                failures.Add("间隔内（0.02 < 0.045）应拒绝。");
            }

            if (!ok)
            {
                failures.Add("超间隔（0.05 ≥ 0.045）应通过。");
            }

            if (!afterReset)
            {
                failures.Add("Reset 后首次应通过。");
            }

            Assert.IsEmpty(failures, "T94 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T94: 首过 / 间隔内拒 / 超间隔过 / Reset 后过 ✓");
        }

        // ════════════════ T99 顶点写入单点（越小界 = 刷错误日志）══════════════════

        /// <summary>
        /// **顶点写入单点契约**（`loop` → `positionCount` → **颜色** → 顶点）。
        ///
        /// ── 为什么需要这条判据（两次用户实机反馈，2026-10-15）────────────────────────
        /// ① 刷屏 `LineRenderer.SetPosition index out of bounds!`：原实现「先循环写 32 个顶点、循环之后
        ///    才设 `positionCount = 32`」，而环出厂 `positionCount = 0` ⇒ **每个点都越界**；槽位在
        ///    「环(32)/弧(16)」之间复用时数量由大变小 ⇒ **反复越界**。
        /// ② "特效都是白的"：颜色原先设在**改 `positionCount` 之前**，而该赋值会重建顶点缓冲
        ///    ⇒ 顶点色有被重置为白的风险（`T99 (d)` 锁死"设色在数量之后、且写点会落到顶点上"）。
        /// 这两类失效**只打日志/只影响观感、不抛异常** ⇒ 纯函数判据（`T92`–`T94`）结构上覆盖不到。
        ///
        /// **可被打死**：把 `WriteLine` 里顺序打乱（先写点 / 把设色挪到 `positionCount` 之前）⇒ 红；
        /// 在 `WriteSlot` 里加一处直写 `SetPosition` 或 `startColor` ⇒ (e) 红。
        /// </summary>
        [Test]
        public void T99_VertexWrite_SetsCountBeforePositions()
        {
            var go = new GameObject("T99Line");
            try
            {
                var line = go.AddComponent<LineRenderer>();
                var brass = new Color(0.953f, 0.851f, 0.600f, 0.8f);
                var rail = new Color(0.800f, 0.855f, 0.796f, 0.46f);

                var points32 = new List<Vector3>();
                for (int i = 0; i < PoolVfxView.RingSegments; i++)
                {
                    points32.Add(new Vector3(i * 0.001f, 0f, i * 0.002f));
                }

                var points16 = new List<Vector3>();
                for (int i = 0; i < PoolVfxView.ArcSegments; i++)
                {
                    points16.Add(new Vector3(i * 0.003f, 0.01f, 0f));
                }

                // (a) 复现**旧 `CreateLine` 的出厂态**：`positionCount = 0`。
                //     ⚠ 实测记录：`AddComponent<LineRenderer>()` 的 Unity **默认值是 2（不是 0）** ——
                //     原缺陷之所以触发，是因为旧代码在 `CreateLine` 里**显式**写了 `positionCount = 0`。
                //     故这里必须显式置 0 才能复现；依赖"新组件默认是 0"会得到一个**错误前提**（本判据第一版就栽在这）。
                line.positionCount = 0;
                PoolVfxView.WriteLine(line, points32, brass, true);
                Assert.AreEqual(PoolVfxView.RingSegments, line.positionCount,
                    $"T99 (a) 写入 {PoolVfxView.RingSegments} 点后 positionCount 应为 {PoolVfxView.RingSegments}，实际 {line.positionCount}。");
                Assert.IsTrue(line.loop, "T99 (a) 环应 loop = true。");
                Assert.AreEqual(points32[7], line.GetPosition(7), "T99 (a) 第 7 个顶点值不符。");
                AssertColor("(a) 环", brass, line.startColor, line.endColor);

                // (b) 复用小序列：32 → 16 → 32（真实场景里环与弧共用槽位）。
                PoolVfxView.WriteLine(line, points16, rail, false);
                Assert.AreEqual(PoolVfxView.ArcSegments, line.positionCount, "T99 (b) 弧段写入后 positionCount 应为 16。");
                Assert.IsFalse(line.loop, "T99 (b) 弧应 loop = false。");
                AssertColor("(b) 弧", rail, line.startColor, line.endColor);

                PoolVfxView.WriteLine(line, points32, brass, true);
                Assert.AreEqual(PoolVfxView.RingSegments, line.positionCount, "T99 (b) 环复用后 positionCount 应回到 32。");
                AssertColor("(b) 环复用", brass, line.startColor, line.endColor);

                // (c) 硬判据：以上任意一步越界都会打出错误日志 ⇒ 这一步就是"越小界"的探测。
                LogAssert.NoUnexpectedReceived();

                // (d) 顶点色必须落到顶点上：`GetPositions` 读不回颜色，退而用"离屏可观测代理"——
                //     引擎不为顶点色提供 getter（如实登记），故本项只锁 LineRenderer 的公共色属性；
                //     "顶点是否真的带色"由 (e) 的源码级单点守 + 用户实机确认。
                AssertColor("(d) 色属性", brass, line.startColor, line.endColor);

                // (e) 源码级守卫：`SetPosition(` / `startColor =` / `endColor =` 各只准出现**一次**
                //     （都在写入单点内部）。绕过单点 = 重新打开越界或丢色，必须拦下。
                string root = System.IO.Path.GetDirectoryName(Application.dataPath);
                string source = System.IO.File.ReadAllText(
                    System.IO.Path.Combine(root ?? ".", "Assets/_Project/Scripts/Runtime/Physics/PoolVfxView.cs"));

                AssertOccurrences(source, "SetPosition(", 1,
                    "`SetPosition` 只准出现在写入单点内 —— 绕过它 = 重新打开「越界只打日志」的失效模式");
                AssertOccurrences(source, "startColor =", 1,
                    "`startColor` 只准在写入单点内设置 —— 设在 `positionCount` 之前有丢色风险");
                AssertOccurrences(source, "endColor =", 1,
                    "`endColor` 只准在写入单点内设置 —— 同 `startColor`");

                // (f) 线宽口径：**特效线不得比瞄准线细**，且必须**乘显示缩放**。
                //     原实现 `LineWidth = 0.004` 且**漏乘** `DrawScale(1.2)` ⇒ 世界 4 mm，
                //     而瞄准线是 `0.006 × 1.2` = **世界 7.2 mm** ⇒ 特效细 44%，颜色被深色台面稀释成"白线"。
                Assert.IsTrue(PoolVfxView.LineWidth >= AimAssistView.PredictionLineWidth,
                    $"T99 (f) 特效线宽（{PoolVfxView.LineWidth:R}）不得小于瞄准线宽"
                    + $"（{AimAssistView.PredictionLineWidth:R}）—— 用户实机反馈「特效都是白的」的机械成因之一。");
                AssertOccurrences(source, "LineWidth * AimAssistView.DrawScale(", 1,
                    "`useWorldSpace` 下线宽是世界单位、**不受父缩放影响** ⇒ 必须显式乘显示缩放"
                    + "（与瞄准线同一出口 `AimAssistView.DrawScale`）");

                TestContext.WriteLine("T99: 出厂态 positionCount=0 写入 32 点 ✓；32→16→32 复用 ✓；无越界错误日志 ✓；"
                                      + "颜色随写入单点逐次正确 ✓；`SetPosition`/`startColor`/`endColor` 单点 ✓；"
                                      + $"线宽 {PoolVfxView.LineWidth:R} ≥ 瞄准线 {AimAssistView.PredictionLineWidth:R} ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssertColor(string label, Color expected, Color start, Color end)
        {
            AssertColorComponent(label + " start.r", expected.r, start.r);
            AssertColorComponent(label + " start.g", expected.g, start.g);
            AssertColorComponent(label + " start.b", expected.b, start.b);
            AssertColorComponent(label + " start.a", expected.a, start.a);
            AssertColorComponent(label + " end.r", expected.r, end.r);
            AssertColorComponent(label + " end.g", expected.g, end.g);
            AssertColorComponent(label + " end.b", expected.b, end.b);
            AssertColorComponent(label + " end.a", expected.a, end.a);
        }

        /// <summary>
        /// 颜色分量比对。**容差按 `LineRenderer` 的实际存储精度定，不是随手取的数**：
        /// 实测（2026-10-15）`LineRenderer.startColor/endColor` 以 **Color32（8 位/通道）** 存储 ——
        /// 写入 `a = 0.46` 回读得到 `117/255 = 0.458823532`。最大量化误差 = `0.5/255 = 0.00196`，
        /// 故取容差 `1/255 = 0.00392`：足以覆盖量化，又远小于任何**真实改色**的偏差（通常 ≥ 0.05）。
        /// （本判据第一版用 `1e-4`，被这个量化误差判红 —— 不是实现错，是**容差口径错**。）
        /// </summary>
        private static void AssertColorComponent(string label, float expected, float actual)
        {
            const float tolerance = 1f / 255f;
            Assert.IsTrue(Mathf.Abs(expected - actual) <= tolerance,
                $"T99 颜色不符（{label}）：期望 {expected:R}，实际 {actual:R}"
                + $"（差 {Mathf.Abs(expected - actual):R} > 容差 {tolerance:R}）。");
        }

        private static void AssertOccurrences(string source, string token, int expected, string why)
        {
            int occurrences = 0;
            int index = 0;
            while ((index = source.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                index += token.Length;
            }

            Assert.AreEqual(expected, occurrences, $"T99 (e) `{token}` 应出现 {expected} 次，实际 {occurrences} 次 ⇒ {why}。");
        }
    }
}
