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

            // (f) 入袋爆点延迟闭式：`PocketBurstDelaySeconds == SinkSlideFraction × SinkDurationSeconds`（= 0.18）。
            //     两段式"吸→爆"的**爆**相位 = 球到达洞口、开始落下那一瞬 ⇒ 必须与下沉曲线**同源**、不得硬编码，
            //     否则将来改下沉节奏时"爆点"会与落球脱同步（`D15-VF-02`，规格 §5）。
            const float expectDelay = 0.6f * 0.3f;
            const float delayClosed = PoolVfxView.SinkSlideFraction * PoolVfxView.SinkDurationSeconds;
            if (Mathf.Abs(PoolVfxView.PocketBurstDelaySeconds - delayClosed) > 1e-6f
                || Mathf.Abs(PoolVfxView.PocketBurstDelaySeconds - expectDelay) > 1e-6f)
            {
                failures.Add($"(f) 入袋爆点延迟应为 SinkSlideFraction×SinkDurationSeconds = {expectDelay:R}，"
                             + $"实际 {PoolVfxView.PocketBurstDelaySeconds:R}。");
            }

            Assert.IsEmpty(failures, "T92 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(
                $"T92: scale {s0:R}/{s5:R}/{s1:R}；alpha {a0:R}/{a5:R}/{a1:R}；下沉曲线三段 ✓；"
                + $"爆点延迟闭式 {PoolVfxView.PocketBurstDelaySeconds:R} ✓");
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

        // ════════════ T115 VFX 可读性预算（半径 / 线宽 / 明度阶梯 / alpha 上限 / 三色分离）═══════════

        /// <summary>
        /// **VFX 可读性预算**（`D15-VF-02`；规格 §1.3 R1–R6）。
        /// ① 各事件各层最大世界半径（**含扩张**）≤ 档位上限（撞击类 26 mm / 入袋 44 mm）——
        ///    **全档 T0–T3 × 全层**扫描（只测基准 T1 会让守卫"只在基准档有效"）；strike 取力度缩放
        ///    `k` 的**上界**（= 真实最坏：`k=1.3` 与 `T3` 在满力度时同时发生）；
        /// ② 特效线**严格**比瞄准线粗；③ 明度阶梯严格单调 `pocketCore &gt; strike ≥ hit &gt; rail`；
        /// ④ 峰值 alpha ≤ 1.0；⑤ chalk / brass / rail 三色两两**色相**相距 ≥ 20°。
        /// **注入（本单实测）**：把 §2.3 夹取里的 `expansionMul` 去掉（夹取退回"不含扩张"）⇒ ① 红
        /// （hit 晕 **T3 ≈ 26.9 mm**、T2 ≈ 26.4 mm &gt; 26 mm）；若连同整个扩张因子一起去掉 ⇒ T3 ≈ **32.5 mm**。
        /// </summary>
        [Test]
        public void T115_ReadabilityBudget()
        {
            var failures = new List<string>();
            const float display = PoolConstants.PlayfieldVisualScale;   // 1.2（唯一显示尺度真源）

            // ① 各层最大世界半径（含扩张）≤ 档位上限 —— **全档 T0–T3 × 全层**（`D15-VF-02` 加固）。
            //    加固前只测"基准层值"（即 T1）⇒ 这条预算守卫**只在基准档有效**：T2/T3 的晕层会越界
            //    （hit 晕 T3 ≈ 32.5 mm）。现对**每一档 × 每一层**都成立。
            //    另：strike 半径乘力度缩放 `k(power) ∈ [0.7, 1.3]`，最坏组合是 **满力度 k=1.3 且 T3**
            //    （满力度时二者同时发生）⇒ 必须取 `k` 上界，否则测不到真实最坏半径。
            var tiers = new[]
            {
                PoolVfxView.VfxIntensityTier.T0Light, PoolVfxView.VfxIntensityTier.T1Medium,
                PoolVfxView.VfxIntensityTier.T2Heavy, PoolVfxView.VfxIntensityTier.T3Extreme,
            };
            // 各档代表速度（落在档内；边界左闭右开 0.60 / 1.80 / 3.60）。
            var tierSpeeds = new[] { 0.30f, 1.20f, 2.70f, 7.90f };
            for (int t = 0; t < tiers.Length; t++)
            {
                if (PoolVfxView.ClassifyIntensity(tierSpeeds[t]) != tiers[t])
                {
                    failures.Add($"① 代表速度 {tierSpeeds[t]:R} 未落在档 {tiers[t]} ⇒ 全档扫描测错了档"
                                 + "（档位边界漂移时本表须同步）。");
                }
            }

            float strikeKMax = PoolVfxView.StrikePowerScale(1f);   // = 1.3（力度缩放上界）
            var impact = new (PoolVfxView.VfxLayer layer, float baseRadius)[]
            {
                (PoolVfxView.StrikeCoreLayer, PoolVfxView.StrikeCoreLayer.Radius * strikeKMax),
                (PoolVfxView.StrikeHaloLayer, PoolVfxView.StrikeHaloLayer.Radius * strikeKMax),
                (PoolVfxView.HitCoreLayer, PoolVfxView.HitCoreLayer.Radius),
                (PoolVfxView.HitHaloLayer, PoolVfxView.HitHaloLayer.Radius),
                (PoolVfxView.RailLayer, PoolVfxView.RailLayer.Radius),
            };
            foreach (var (layer, baseRadius) in impact)
            {
                for (int t = 0; t < tiers.Length; t++)
                {
                    AssertLayerMaxWorldWithinCap(failures, "撞击类层", layer, baseRadius, tierSpeeds[t],
                        PoolVfxView.ImpactRadiusCap, PoolVfxView.ImpactMaxWorldRadius, display);
                }
            }

            // 入袋各层：生产**不参与力度分档**（恒基准值），但 §2.3 的夹取与 §1.3 的 44 mm 上限对
            // "任何档位"都必须成立（防御性：证夹取对档位不敏感）⇒ 同样逐一过全档。
            var pocketed = new (PoolVfxView.VfxLayer layer, float baseRadius)[]
            {
                (PoolVfxView.PocketOuterLayer, PoolVfxView.PocketOuterLayer.Radius),
                (PoolVfxView.PocketCoreLayer, PoolVfxView.PocketCoreLayer.Radius),
            };
            foreach (var (layer, baseRadius) in pocketed)
            {
                for (int t = 0; t < tiers.Length; t++)
                {
                    AssertLayerMaxWorldWithinCap(failures, "入袋层", layer, baseRadius, tierSpeeds[t],
                        PoolVfxView.PocketRadiusCap, PoolVfxView.PocketMaxWorldRadius, display);
                }
            }

            // ② 特效线严格比瞄准线粗（世界 9.6 mm > 7.2 mm）
            Assert.IsTrue(PoolVfxView.LineWidth > AimAssistView.PredictionLineWidth,
                $"T115 ② 特效线宽 {PoolVfxView.LineWidth:R} 应严格大于瞄准线宽 "
                + $"{AimAssistView.PredictionLineWidth:R}（「更粗 = 是特效」）。");

            // ③ 明度阶梯严格单调
            float pocketCore = PoolVfxView.PocketCoreLayer.PeakAlpha;
            float strike = PoolVfxView.StrikeCoreLayer.PeakAlpha;
            float hit = PoolVfxView.HitCoreLayer.PeakAlpha;
            float rail = PoolVfxView.RailLayer.PeakAlpha;
            if (!(pocketCore > strike && strike >= hit && hit > rail))
            {
                failures.Add($"③ 明度阶梯应 pocketCore > strike ≥ hit > rail，"
                             + $"实际 {pocketCore:R} / {strike:R} / {hit:R} / {rail:R}。");
            }

            // ④ 峰值 alpha ≤ 1.0
            foreach (PoolVfxView.VfxLayer layer in AllVfxLayers())
            {
                if (layer.PeakAlpha > 1f + 1e-6f)
                {
                    failures.Add($"④ 层 {layer.Name} 峰值 alpha {layer.PeakAlpha:R} > 1.0。");
                }
            }

            // ⑤ 三色（chalk / brass / rail）两两色相相距 ≥ 20°（避免四类趋同成白）
            AssertHueSeparation(failures, "chalk", PoolVfxView.ChalkColor, "brass", PoolVfxView.BrassColor, 20f);
            AssertHueSeparation(failures, "chalk", PoolVfxView.ChalkColor, "rail", PoolVfxView.RailColor, 20f);
            AssertHueSeparation(failures, "brass", PoolVfxView.BrassColor, "rail", PoolVfxView.RailColor, 20f);

            Assert.IsEmpty(failures, "T115 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T115 ①: **全档 × 全层**（7 层 × 4 档 = 28 组）最大世界半径（含扩张）"
                                  + $"均 ≤ 上限（撞击类 {PoolVfxView.ImpactMaxWorldRadius * 1000f:R} mm / 入袋 "
                                  + $"{PoolVfxView.PocketMaxWorldRadius * 1000f:R} mm）✓；"
                                  + $"② 线宽 {PoolVfxView.LineWidth:R} > 瞄准线 {AimAssistView.PredictionLineWidth:R}；"
                                  + $"③ 明度 {pocketCore:R}/{strike:R}/{hit:R}/{rail:R}；⑤ 三色色相分离 ✓");
        }

        // ════════════ T116 力度分级闭式 + 无随机（确定性）═══════════════════

        /// <summary>
        /// **力度分级**（`D15-VF-02`；规格 §2）。① 边界（左闭右开）；② 倍率随档**单调非减**、`alphaMul ≤ 1`；
        /// ③ **夹取**（`sizeMul = 10` ⇒ **含扩张的最终半径** ≤ `MaxRadiusCap`）；④ **确定性**（同一 `s` 序列双跑逐位一致）；
        /// ⑤ **源码级**：`PoolVfxView.cs` 中 `Random` 出现 **0** 次。
        /// **注入**：把 `TierLightMaxSpeed`（0.60）改成 `0.50` ⇒ `s = 0.59` 用例红（应 T0，实际 T1）。
        /// </summary>
        [Test]
        public void T116_IntensityTier_ClosedForm_And_Deterministic()
        {
            var failures = new List<string>();

            // ① 档位边界（左闭右开）
            var cases = new[]
            {
                (s: 0.59f, expected: PoolVfxView.VfxIntensityTier.T0Light),
                (s: 0.60f, expected: PoolVfxView.VfxIntensityTier.T1Medium),
                (s: 1.79f, expected: PoolVfxView.VfxIntensityTier.T1Medium),
                (s: 1.80f, expected: PoolVfxView.VfxIntensityTier.T2Heavy),
                (s: 3.59f, expected: PoolVfxView.VfxIntensityTier.T2Heavy),
                (s: 3.60f, expected: PoolVfxView.VfxIntensityTier.T3Extreme),
                (s: 8.00f, expected: PoolVfxView.VfxIntensityTier.T3Extreme),
            };
            foreach (var c in cases)
            {
                PoolVfxView.VfxIntensityTier got = PoolVfxView.ClassifyIntensity(c.s);
                if (got != c.expected)
                {
                    failures.Add($"① s={c.s:R} 应 {c.expected}，实际 {got}。");
                }
            }

            // ② 倍率随档单调非减（size / expansion / dur）；alphaMul ≤ 1.0
            var order = new[]
            {
                PoolVfxView.VfxIntensityTier.T0Light, PoolVfxView.VfxIntensityTier.T1Medium,
                PoolVfxView.VfxIntensityTier.T2Heavy, PoolVfxView.VfxIntensityTier.T3Extreme,
            };
            float prevSize = float.NegativeInfinity;
            float prevExp = float.NegativeInfinity;
            float prevDur = float.NegativeInfinity;
            foreach (PoolVfxView.VfxIntensityTier tier in order)
            {
                PoolVfxView.TierMultipliers(tier, out float sizeMul, out float alphaMul,
                                            out float expansionMul, out float durMul, out _);
                if (sizeMul < prevSize - 1e-6f || expansionMul < prevExp - 1e-6f || durMul < prevDur - 1e-6f)
                {
                    failures.Add($"② 倍率应随档单调非减，档 {tier} 出现回落 "
                                 + $"(size {sizeMul:R} / exp {expansionMul:R} / dur {durMul:R})。");
                }

                if (alphaMul > 1f + 1e-6f)
                {
                    failures.Add($"② 档 {tier} 的 alphaMul {alphaMul:R} > 1.0。");
                }

                prevSize = sizeMul;
                prevExp = expansionMul;
                prevDur = durMul;
            }

            // ③ 夹取：极端 sizeMul 不得击穿上限（上限作用于**含扩张的最终半径** ⇒ 判 maxExtent）
            const float capExpansion = 0.5f;
            const float capExpansionMul = 1f;
            float clampedRadius = PoolVfxView.FinalRadius(0.011f, 10f, capExpansion, capExpansionMul,
                                                          PoolVfxView.ImpactRadiusCap);
            float clampedExtent = clampedRadius * (1f + capExpansion * capExpansionMul);
            if (clampedExtent > PoolVfxView.ImpactRadiusCap + 1e-6f)
            {
                failures.Add($"③ sizeMul=10 时最终半径（含扩张）{clampedExtent:R} 应 ≤ 上限 "
                             + $"{PoolVfxView.ImpactRadiusCap:R}。");
            }

            // ④ 确定性：同一 s 序列双跑，输出逐位一致（无随机 / 无时间抖动）
            var seq = new[] { 0.30f, 0.59f, 0.60f, 1.20f, 1.80f, 3.59f, 3.60f, 7.90f };
            string runA = TierTrace(seq);
            string runB = TierTrace(seq);
            if (!string.Equals(runA, runB, System.StringComparison.Ordinal))
            {
                failures.Add($"④ 双跑不一致：\n  A={runA}\n  B={runB}");
            }

            // ⑤ 源码级：无任何随机源
            string root = System.IO.Path.GetDirectoryName(Application.dataPath);
            string source = System.IO.File.ReadAllText(
                System.IO.Path.Combine(root ?? ".", "Assets/_Project/Scripts/Runtime/Physics/PoolVfxView.cs"));
            int randomCount = CountOccurrences(source, "Random");
            if (randomCount != 0)
            {
                failures.Add($"⑤ `PoolVfxView.cs` 中 `Random` 应出现 0 次，实际 {randomCount} 次。");
            }

            Assert.IsEmpty(failures, "T116 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T116: 边界 7 例 ✓；倍率单调非减 ✓；夹取生效 ✓；"
                                  + $"双跑逐位一致 ✓；`Random` 出现 {randomCount} 次 ✓");
        }

        // ════════════ T117 槽与顶点预算 ═══════════════════════════════════

        /// <summary>
        /// **槽与顶点预算**（`D15-VF-02`；规格 §7）。① 每事件槽成本 ≤ 声明；② 最坏并发槽 ≤ `SlotCapacity`(16)；
        /// ③ 单帧顶点上界 ≤ `SlotCapacity × (RingSegments + 3×2)` = 608。
        /// **注入**：把 hit 每事件槽成本改成 `4` ⇒ ② 红（最坏并发 21 &gt; 16）。
        /// </summary>
        [Test]
        public void T117_SlotAndVertexBudget()
        {
            var failures = new List<string>();

            // ① 每事件槽成本 ≤ 声明
            CheckSlotCost(failures, "strike", PoolVfxView.StrikeSlotCost, 2);
            CheckSlotCost(failures, "hit", PoolVfxView.HitSlotCost, 2);
            CheckSlotCost(failures, "rail", PoolVfxView.RailSlotCost, 1);
            CheckSlotCost(failures, "pocket", PoolVfxView.PocketSlotCost, 2);

            // ② 最坏并发槽 ≤ SlotCapacity
            int worst = PoolVfxView.MaxConcurrentSlots();
            if (worst > PoolVfxView.SlotCapacity)
            {
                failures.Add($"② 最坏并发槽 {worst} > SlotCapacity {PoolVfxView.SlotCapacity}。");
            }

            // ③ 单帧顶点上界
            const int expectVerts = 16 * (32 + 3 * 2);   // = 608，独立十进制
            int verts = PoolVfxView.MaxVerticesPerFrame();
            if (verts > expectVerts)
            {
                failures.Add($"③ 单帧顶点上界 {verts} > {expectVerts}。");
            }

            Assert.IsEmpty(failures, "T117 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T117: 槽成本 strike/hit/rail/pocket = "
                                  + $"{PoolVfxView.StrikeSlotCost}/{PoolVfxView.HitSlotCost}/{PoolVfxView.RailSlotCost}/{PoolVfxView.PocketSlotCost}；"
                                  + $"最坏并发 {worst} ≤ {PoolVfxView.SlotCapacity}；单帧顶点 {verts} ≤ {expectVerts} ✓");
        }

        private static string TierTrace(float[] speeds)
        {
            var sb = new System.Text.StringBuilder();
            foreach (float s in speeds)
            {
                PoolVfxView.VfxIntensityTier tier = PoolVfxView.ClassifyIntensity(s);
                PoolVfxView.TierMultipliers(tier, out float sizeMul, out float alphaMul,
                                            out float expansionMul, out float durMul, out int sparkDelta);
                sb.Append(s.ToString("R")).Append('|').Append((int)tier).Append('|')
                  .Append(sizeMul.ToString("R")).Append('|').Append(alphaMul.ToString("R")).Append('|')
                  .Append(expansionMul.ToString("R")).Append('|').Append(durMul.ToString("R")).Append('|')
                  .Append(sparkDelta).Append(';');
            }

            return sb.ToString();
        }

        private static void CheckSlotCost(List<string> failures, string name, int actual, int declared)
        {
            if (actual > declared)
            {
                failures.Add($"① {name} 每事件槽成本 {actual} > 声明 {declared}。");
            }
        }

        /// <summary>
        /// `T115 ①` 的一格：对「层 × 档位」算"**最终半径（含扩张）** → 世界半径"，超上限即记失败。
        /// 走**生产同一条路径** `ApplyIntensity`（→ `FinalRadius`）—— 判据若自算公式，就会与实现脱节
        /// （本工程已有"判据重述设计公式、实现画的却是另一条线"的教训）。
        /// </summary>
        private static void AssertLayerMaxWorldWithinCap(List<string> failures, string group,
            PoolVfxView.VfxLayer layer, float baseRadius, float speed,
            float radiusCap, float maxWorldCap, float display)
        {
            PoolVfxView.ApplyIntensity(speed, baseRadius, layer.Expansion, layer.PeakAlpha, layer.Duration,
                radiusCap, out float radius, out float expansion, out _, out _);
            float maxWorld = radius * (1f + expansion) * display;
            if (maxWorld > maxWorldCap + 1e-6f)
            {
                failures.Add($"① {group} {layer.Name} @ s={speed:R} 最大世界半径 {maxWorld * 1000f:R} mm > 上限 "
                             + $"{maxWorldCap * 1000f:R} mm（夹取须作用于含扩张的最终半径）。");
            }
        }

        private static IEnumerable<PoolVfxView.VfxLayer> AllVfxLayers()
        {
            foreach (PoolVfxView.VfxLayer layer in PoolVfxView.ImpactLayers)
            {
                yield return layer;
            }

            foreach (PoolVfxView.VfxLayer layer in PoolVfxView.PocketLayers)
            {
                yield return layer;
            }
        }

        private static void AssertHueSeparation(List<string> failures, string nameA, Color a,
                                                string nameB, Color b, float minDegrees)
        {
            float ha = HueDeg(a);
            float hb = HueDeg(b);
            float dist = HueDistanceDeg(ha, hb);
            if (dist < minDegrees)
            {
                failures.Add($"⑤ 色相分离不足：{nameA}({ha:F1}°) vs {nameB}({hb:F1}°) 相距 {dist:F1}° < {minDegrees}°。");
            }
        }

        private static float HueDeg(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float delta = max - min;
            if (delta < 1e-6f)
            {
                return 0f;
            }

            float h;
            if (max == c.r)
            {
                h = ((c.g - c.b) / delta) % 6f;
            }
            else if (max == c.g)
            {
                h = (c.b - c.r) / delta + 2f;
            }
            else
            {
                h = (c.r - c.g) / delta + 4f;
            }

            h *= 60f;
            if (h < 0f)
            {
                h += 360f;
            }

            return h;
        }

        private static float HueDistanceDeg(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        private static int CountOccurrences(string source, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
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
