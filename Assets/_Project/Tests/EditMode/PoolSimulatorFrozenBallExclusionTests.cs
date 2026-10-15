using System.Collections.Generic;
using System.Globalization;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **离台冻结球不参与球-球解算** —— `T84`。
    ///
    /// ── 背景：为什么需要它 ─────────────────────────────────────────────────
    /// 球越过袋喉平面即离台（<see cref="PoolSimulator.IsBallInPlay"/> == false：速度置零、`_frozen = true`，
    /// 含"正常落袋"与"擦袋角越界"两种分类）。若球-球 TOI 扫描**未排除**离台球，落袋球会以
    /// 「幽灵碰撞体」继续参与碰撞：其碰撞圆盘（半径 `2r`）从喉平面向台内一侧回伸约 `2r − 1 mm`
    /// （实测冻结点位于喉平面外 1.05 mm），恰好覆盖该袋的进袋走廊 ⇒ **同一袋在结构上不可再被捕获**
    /// （后续来球在过喉平面之前被弹回）；同时冻结球自身会被写入速度、被位置修正推动。
    ///
    /// ── 判据（三段）───────────────────────────────────────────────────────
    /// · **(a) 袋口不再死锁**：两颗球沿同一袋法线列队进袋（前球 0.35 m、后球 0.70 m、同速 2 m/s）
    ///   ⇒ **两颗都被捕获**（`CapturedBallCount == 2`，后球 `IsBallInPlay == false`）。
    /// · **(b) 冻结球位置逐位不变**：前球入袋后（跨过入袋所在步 + 8 步缓冲）取逐位快照，
    ///   此后整段跑动**不得改变其位置**（不得被 `SeparatePair` 推动）。
    /// · **(c) 冻结球速度保持零**：同上，速度全程保持 `0;0`（不得被球-球解算写入）。
    ///
    /// ── 注入负对照（实测）────────────────────────────────────────────────
    /// 把 TOI 扫描的跳过条件回退为 `if (!_active[i] && !_active[k])`（= 缺陷原状）
    /// ⇒ **(a)(b)(c) 同时 FAIL**（失败原文见执行记录）。
    /// </summary>
    public class PoolSimulatorFrozenBallExclusionTests
    {
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";

        // ── 辅助（只用公开 API + 资产构造；不复制任何解算逻辑）────────────────

        private static TableConfig Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(t, "T84：需要 " + TablePath);
            return t;
        }

        private static PhysicsParams FactoryParams()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "T84：需要 " + TuningPath);
            return tuning.ToPhysicsParams();
        }

        private static BallState Ball(float x, float y, float vx, float vy)
        {
            return new BallState { position = new Vector2(x, y), velocity = new Vector2(vx, vy) };
        }

        private static string RVec(Vector2 v)
        {
            return v.x.ToString("R", CultureInfo.InvariantCulture) + ";"
                 + v.y.ToString("R", CultureInfo.InvariantCulture);
        }

        // ══════════════════ T84 ══════════════════════════════════════════════

        [Test]
        public void T84_FrozenBall_IsExcludedFromBallBallResolution()
        {
            var failures = new List<string>();
            TableConfig table = Table();

            // 场景：中袋（索引 1）法线列队 —— 前球 0.35 m、后球 0.70 m，同速朝袋口直行。
            Pocket pocket = table.GetPocket(1);
            Vector2 tn = pocket.throatNormal;
            Vector2 v = 2f * tn;
            Vector2 front = pocket.throatCenter - 0.35f * tn;
            Vector2 rear = pocket.throatCenter - 0.70f * tn;

            var sim = new PoolSimulator(table, FactoryParams());
            sim.LoadBalls(new[]
            {
                Ball(front.x, front.y, v.x, v.y),
                Ball(rear.x, rear.y, v.x, v.y),
            });

            // ── 阶段 1：跑到前球入袋（前提）────────────────────────────────
            int guard = 0;
            while (sim.CapturedBallCount < 1 && guard < 600)
            {
                sim.Step();
                guard++;
            }

            if (sim.CapturedBallCount != 1 || sim.IsBallInPlay(0))
            {
                Assert.Fail("T84 前提不成立：前球应在 600 步内越过喉平面被捕获"
                            + $"（CapturedBallCount={sim.CapturedBallCount}，IsBallInPlay(0)={sim.IsBallInPlay(0)}，步数={guard}）。");
            }

            // ── 阶段 2：跨过入袋所在步 + 缓冲，取冻结球逐位快照 ────────────
            const int settleSteps = 8;
            for (int s = 0; s < settleSteps; s++)
            {
                sim.Step();
            }

            string frozenPosition = RVec(sim.Balls[0].position);
            string frozenVelocity = RVec(sim.Balls[0].velocity);

            // ── 阶段 3：继续跑（覆盖后球的完整行程）───────────────────────
            const int runSteps = 900;
            for (int s = 0; s < runSteps; s++)
            {
                sim.Step();
            }

            // (a) 袋口不再死锁
            if (sim.CapturedBallCount != 2)
            {
                failures.Add($"(a) 后球未被同一袋捕获（CapturedBallCount = {sim.CapturedBallCount}，应为 2）"
                             + " ⇒ 落袋球以幽灵碰撞体堵死了该袋的进袋走廊。");
            }

            if (sim.IsBallInPlay(1))
            {
                failures.Add("(a) 后球仍在场上（IsBallInPlay(1) == true）。");
            }

            // (b) 冻结球位置逐位不变
            string nowPosition = RVec(sim.Balls[0].position);
            if (nowPosition != frozenPosition)
            {
                failures.Add($"(b) 冻结球位置被改变：快照 {frozenPosition} → 实测 {nowPosition}"
                             + " ⇒ 离台球仍被球-球路径推动。");
            }

            // (c) 冻结球速度保持零
            string nowVelocity = RVec(sim.Balls[0].velocity);
            if (nowVelocity != frozenVelocity)
            {
                failures.Add($"(c) 冻结球速度被改写：快照 {frozenVelocity} → 实测 {nowVelocity}"
                             + " ⇒ 离台球仍被球-球路径写入速度。");
            }

            Assert.IsEmpty(failures, "T84 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T84: 前球入袋于第 {guard} 步（+{settleSteps} 步缓冲取快照）；"
                                  + $"两球均捕获 = {sim.CapturedBallCount == 2}；"
                                  + $"冻结球位置逐位 {frozenPosition}（不变）；速度逐位 {frozenVelocity}（不变） ✓");
        }
    }
}
