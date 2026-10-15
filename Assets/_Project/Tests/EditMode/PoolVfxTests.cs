using System.Collections.Generic;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEngine;

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
    }
}
