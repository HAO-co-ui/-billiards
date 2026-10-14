using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **击球预测纯核** `T72`–`T76`（Day 9 切片 A）。
    ///
    /// ── 被测对象 ───────────────────────────────────────────────────────────
    /// `ShotPredictor.Predict(table, source, cueVelocity, maxSteps)`：**独立探测内核** + 前向推演 +
    /// 首次球-球接触处的几何（幽灵球 / 目标球出射方向 / 白球出射方向 / 路径 / 时间）。
    ///
    /// ── 期望值来源（本文件逐条标明）────────────────────────────────────────
    /// · **甲** = 独立闭式 / 几何恒等式（`T75a` 的 `2r`、`T76` 的 `d − 2r`）；
    /// · **乙** = 只读出口 / 契约常量 / 前后不变（`T72`、`T73a`、`T74`、`T75b/c`）。
    /// · **丙 = 0**（无一条转写被测代码公式）。
    ///
    /// ⚠ **`maxSteps` 的口径**：本文件各用例显式给足步数（预测**首次接触即停**，故给足即可）；
    /// 生产侧的 `maxPredictSteps`（`AimAssistController` 字段）属 Day 9 切片 B 的"待实测定值"，**不在本切片**。
    ///
    /// ⚠ 本工程 NUnit 无 `Assert.Multiple` ⇒ 需要多条一起验的地方**手工汇总失败项**。
    /// </summary>
    public class ShotPredictorTests
    {
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";

        private const float TwoR = 2f * 0.028575f;   // = 2·PoolConstants.BallRadius，仅用于"排除退化"的前置断言

        // ── 辅助 ──────────────────────────────────────────────────────────

        private static TableConfig Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(t, "需要 " + TablePath);
            return t;
        }

        private static PhysicsParams FactoryParams()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "需要 " + TuningPath);
            return tuning.ToPhysicsParams();
        }

        private static PoolSimulator SimOf(PhysicsParams p, params BallState[] balls)
        {
            var sim = new PoolSimulator(Table(), p);
            sim.LoadBalls(balls);
            return sim;
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

        /// <summary>在跑内核的**全盘逐位快照**（位置 / 速度 / 入袋索引 / 在场标记 / 终止状态 / 每杆计数）。</summary>
        private static string SimSnapshot(PoolSimulator sim)
        {
            var sb = new StringBuilder();
            IReadOnlyList<BallState> balls = sim.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                sb.Append(i).Append(':').Append(RVec(balls[i].position)).Append('|').Append(RVec(balls[i].velocity))
                  .Append('|').Append(sim.CapturedPocketIndex(i).ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(sim.IsBallInPlay(i) ? '1' : '0').Append('\n');
            }

            sb.Append("atRest=").Append(sim.TableAtRest ? '1' : '0')
              .Append(";restFrames=").Append(sim.ConsecutiveRestFrameCount.ToString(CultureInfo.InvariantCulture))
              .Append(";total=").Append(sim.TotalContactCount.ToString(CultureInfo.InvariantCulture))
              .Append(";bb=").Append(sim.BallBallContactCount.ToString(CultureInfo.InvariantCulture))
              .Append(";pocketProx=").Append(sim.PocketProximityEvents.ToString(CultureInfo.InvariantCulture))
              .Append(";captured=").Append(sim.CapturedBallCount.ToString(CultureInfo.InvariantCulture))
              .Append(";jaw=").Append(sim.PocketJawCrossingCount.ToString(CultureInfo.InvariantCulture))
              .Append(";energy=").Append(sim.EnergyGainViolations.ToString(CultureInfo.InvariantCulture))
              .Append(";guard=").Append(sim.ToiIterationGuardTrips.ToString(CultureInfo.InvariantCulture))
              .Append(";sep=").Append(sim.BallBallSeparationCount.ToString(CultureInfo.InvariantCulture))
              .Append(";hasBB=").Append(sim.HasBallBallContact ? '1' : '0');
            return sb.ToString();
        }

        /// <summary>一次预测结果的逐位指纹（用于"同一输入重复两次结果相同"）。</summary>
        private static string PredictionFingerprint(in ShotPrediction p)
        {
            var sb = new StringBuilder();
            sb.Append(p.HasFirstContact ? '1' : '0').Append('|').Append(RVec(p.CueStart)).Append('|')
              .Append(RVec(p.GhostBall)).Append('|').Append(RVec(p.TargetBallCenter)).Append('|')
              .Append(RVec(p.TargetOutgoingDirection)).Append('|').Append(RVec(p.CueOutgoingDirection)).Append('|')
              .Append(p.TimeToFirstContactSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(p.StepsSimulated.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.CushionBounceCountBeforeContact.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.BallBallContactCount.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.TotalContactCount.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.ProbeBallCount.ToString(CultureInfo.InvariantCulture)).Append('|');

            IReadOnlyList<Vector2> path = p.CueWorldPath;
            sb.Append(path == null ? "null" : path.Count.ToString(CultureInfo.InvariantCulture));
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    sb.Append(';').Append(RVec(path[i]));
                }
            }

            return sb.ToString();
        }

        /// <summary>球堆模式（白球 + 3）的静止初态。</summary>
        private static PoolSimulator RackAtRest(PhysicsParams p)
        {
            Vector2[] rack = PhysicsTestDrive.RackPositions(Table().footSpot, 3);
            var balls = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                balls[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            PoolSimulator sim = SimOf(p, balls);
            for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
            {
                sim.Step();
            }

            return sim;
        }

        // ══════════════════ T72 预测对在跑内核零扰动 ══════════════════════════

        /// <summary>
        /// **`Predict` 不得改动在跑内核的任何状态**（逐位，0 容差），且**预测确实运行过**、同一输入幂等。
        ///
        /// 期望值来源：**乙**（被测对象自身的**前后**状态，要求**不变** —— 不是"期望值 = 被测对象产出值"）。
        /// **判别力**：把"就地用在跑内核推演"这一**最省事的错误实现**变成可判定失败。
        /// **盲带**：只覆盖"调用 `Predict` 这一动作"；不覆盖"表现层每帧顺手写内核"（那由切片 B 的源码守卫补）。
        /// **注入负对照（实测）**：把 `Predict` 改成对 `source` 直接 `Step()` ⇒ **(a) FAIL**；提前 `return` ⇒ **(b) FAIL**。
        /// </summary>
        [Test]
        public void T72_Predict_DoesNotDisturbSourceSimulator()
        {
            TableConfig table = Table();
            PoolSimulator source = RackAtRest(FactoryParams());
            Assert.IsTrue(source.TableAtRest, "T72：前提 —— 球堆应已静止。");

            string before = SimSnapshot(source);

            var velocities = new[]
            {
                new Vector2(3.5f, 0f),
                new Vector2(-4.5f, 0f),
                new Vector2(2.0f, 1.5f),
            };

            var failures = new List<string>();
            var fingerprints = new List<string>();

            foreach (Vector2 v0 in velocities)
            {
                ShotPrediction p = ShotPredictor.Predict(table, source, v0, 600);
                if (p.StepsSimulated <= 0)
                {
                    failures.Add($"(b) 初速 {RVec(v0)}：`StepsSimulated` = {p.StepsSimulated} ⇒ 预测**没跑**（空转不能算零扰动）。");
                }

                fingerprints.Add(PredictionFingerprint(p));
            }

            string after = SimSnapshot(source);
            if (before != after)
            {
                failures.Add("(a) 预测**改动了在跑内核**（前后逐位快照不一致）：\n  before="
                             + before.Replace("\n", " / ") + "\n  after =" + after.Replace("\n", " / "));
            }

            // (c) 同一输入重复两次 ⇒ 结果逐位相同
            ShotPrediction again = ShotPredictor.Predict(table, source, velocities[0], 600);
            if (PredictionFingerprint(again) != fingerprints[0])
            {
                failures.Add("(c) 同一输入重复两次结果**不一致**（预测不是纯函数）。");
            }

            Assert.IsEmpty(failures, "T72 零扰动失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T72: {velocities.Length} 组初速预测后，在跑内核逐位快照**不变**；"
                                  + $"StepsSimulated={ShotPredictor.Predict(table, source, velocities[0], 600).StepsSimulated} > 0；幂等 ✓");
        }

        // ══════════════════ T73 预测世界与在跑内核同源 ════════════════════════

        /// <summary>
        /// **`probe` 的参数 / 步长 / 终止 / 袋口乘子与在跑内核逐字段位相等**，且**预测确实用到**了它们。
        ///
        /// 期望值来源：**乙**（在跑内核的只读出口 —— 与 `probe` 是**不同对象**）。
        /// **判别力**：把"预测用 `PhysicsParams.SafeDefault` / 默认 `captureScale=1` / 默认终止参数"这类
        /// **静默不同源**变成可判定失败。
        /// **盲带**：(a) 只证明"读进去了"；"**用到**了"由 (b) 的行为佐证 + `T76`/`T78` 的实跑一致兜底。
        /// **注入负对照（实测）**：`probe` 用 `SafeDefault`（`e_ball` 1.0 而非 0.95）或 `captureScale = 1f` ⇒ FAIL。
        /// </summary>
        [Test]
        public void T73_PredictWorld_IsSameSourceAsRunningKernel()
        {
            TableConfig table = Table();
            PoolSimulator source = RackAtRest(FactoryParams());

            // (a) **同源（逐字段位相等）**：按 `Predict` 的构造口径建一个探测内核，与 source 比对。
            var probe = new PoolSimulator(table, source.Params, source.PocketCaptureRadiusScaleValue,
                source.RestSpeedThresholdValue, source.RestStableFramesValue);

            var failures = new List<string>();
            PhysicsParams sp = source.Params;
            PhysicsParams pp = probe.Params;
            CompareField(failures, "cushionRestitution", sp.cushionRestitution, pp.cushionRestitution);
            CompareField(failures, "cushionTangentialFriction", sp.cushionTangentialFriction, pp.cushionTangentialFriction);
            CompareField(failures, "ballRestitution", sp.ballRestitution, pp.ballRestitution);
            CompareField(failures, "ballTangentialFriction", sp.ballTangentialFriction, pp.ballTangentialFriction);
            CompareField(failures, "rollingDecelerationSlide", sp.rollingDecelerationSlide, pp.rollingDecelerationSlide);
            CompareField(failures, "rollingDecelerationRoll", sp.rollingDecelerationRoll, pp.rollingDecelerationRoll);
            CompareField(failures, "rollingTransitionSpeed", sp.rollingTransitionSpeed, pp.rollingTransitionSpeed);

            if (source.SubSteps != probe.SubSteps)
            {
                failures.Add($"(a) `SubSteps` {source.SubSteps} ≠ {probe.SubSteps}");
            }

            CompareField(failures, "RestSpeedThresholdValue", source.RestSpeedThresholdValue, probe.RestSpeedThresholdValue);
            CompareField(failures, "PocketCaptureRadiusScaleValue", source.PocketCaptureRadiusScaleValue, probe.PocketCaptureRadiusScaleValue);

            if (source.RestStableFramesValue != probe.RestStableFramesValue)
            {
                failures.Add($"(a) `RestStableFramesValue` {source.RestStableFramesValue} ≠ {probe.RestStableFramesValue}");
            }

            if (probe.SegmentCount != table.SegmentCount)
            {
                failures.Add($"(a) `probe` 段数 {probe.SegmentCount} ≠ TableConfig.SegmentCount {table.SegmentCount}");
            }

            // 前置：出厂参数**不等于** `SafeDefault`（否则"同源"与"用默认"观测等价、本判据真空）。
            if (sp.ballRestitution == PhysicsParams.SafeDefault.ballRestitution)
            {
                failures.Add($"(a) 前提不成立 —— 出厂 `e_ball` {sp.ballRestitution} 恰等于 `SafeDefault` 的值 ⇒ 本判据对"
                             + "\"用 SafeDefault\" 这类注入**无区分度**。");
            }

            // (b) **行为佐证：预测确实用了 `source.Params`**
            //     同一几何、同一初速，只改台呢滚阻 ⇒ 到首接触的**时间**必须变
            //     （幽灵球位置只由几何决定、与速度无关 ⇒ 时间才是"参数被用到"的可观测量）。
            BallState cue = Ball(-0.40f, 0f, 0f, 0f);
            BallState target = Ball(0.20f, 0f, 0f, 0f);

            PhysicsParams slow = FactoryParams();
            slow.rollingDecelerationSlide = 0.5f;
            slow.rollingDecelerationRoll = 0.25f;
            slow.rollingTransitionSpeed = 2.0f;

            PhysicsParams fast = slow;
            fast.rollingDecelerationSlide = 5.0f;   // 上限内（`a_max = 5`）
            fast.rollingDecelerationRoll = 5.0f;

            ShotPrediction pSlow = ShotPredictor.Predict(table, SimOf(slow, cue, target), new Vector2(4.5f, 0f), 1200);
            ShotPrediction pFast = ShotPredictor.Predict(table, SimOf(fast, cue, target), new Vector2(4.5f, 0f), 1200);

            Assert.IsTrue(pSlow.HasFirstContact && pFast.HasFirstContact,
                "(b) 前提不成立 —— 两种滚阻下都应发生首次接触（否则时间不可比）。");
            if (pSlow.TimeToFirstContactSeconds == pFast.TimeToFirstContactSeconds)
            {
                failures.Add("(b) 台呢滚阻 0.5 vs 5.0 下，到首接触的时间**完全相同**"
                             + $"（{pSlow.TimeToFirstContactSeconds:R}）⇒ `Predict` **没有用** `source.Params`"
                             + "（\"同源\"因此不可达）。");
            }

            Assert.IsEmpty(failures, "T73 同源失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"(T73) 同源逐字段位相等；行为佐证：滚阻 0.5 → t={pSlow.TimeToFirstContactSeconds:R}，"
                                  + $"5.0 → t={pFast.TimeToFirstContactSeconds:R}（不同 ⇒ 参数确实被用到）✓");
        }

        private static void CompareField(List<string> failures, string name, float expected, float actual)
        {
            if (expected.ToString("R", CultureInfo.InvariantCulture) != actual.ToString("R", CultureInfo.InvariantCulture))
            {
                failures.Add($"(a) `{name}` 逐位不等：source={expected:R} probe={actual:R}");
            }
        }

        // ══════════════════ T74 快照语义：只装在场上球 ════════════════════════

        /// <summary>
        /// **快照只收在场上球**、**已入袋球不复活**，且预测**不改**在跑内核的入袋记录。
        ///
        /// 期望值来源：**乙**（在跑内核的 `IsBallInPlay` / `CapturedPocketIndex`）＋ **甲**（`inPlayCount` 直接计数）。
        /// **判别力**：把"`LoadBalls` 会清 `_frozen`/`_capturedPocket`"（本工程已登记事实）变成**必须被正面处理**的一步。
        /// **盲带**：只钉**装载语义**；不覆盖"预测与实跑一致"（那由 `T76`/`T78` 承担）。
        /// **注入负对照（实测）**：快照传 `source.Balls` **全集** ⇒ **(a) FAIL**（入袋球复活 ⇒ 球数 &gt; `inPlayCount`）。
        /// </summary>
        [Test]
        public void T74_Snapshot_ContainsOnlyBallsInPlay()
        {
            TableConfig table = Table();
            PhysicsParams p = FactoryParams();

            // 造一个"含已入袋球"的局面：把 1 号球放到中袋喉口内侧、朝袋内给初速 ⇒ 确定性入袋。
            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 3);
            var balls = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                balls[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            PoolSimulator source = SimOf(p, balls);
            Pocket pocket = SidePocket(table);
            Vector2 tn = pocket.throatNormal;
            source.PlaceBall(1, pocket.throatCenter - 0.15f * tn, 1.0f * tn);

            for (int i = 0; i < 4000 && !source.TableAtRest; i++)
            {
                source.Step();
            }

            Assert.GreaterOrEqual(source.CapturedPocketIndex(1), 0, "T74：前提 —— 1 号球应已入袋。");
            Assert.IsFalse(source.IsBallInPlay(1), "T74：前提 —— 已入袋的球应不在场上。");
            Assert.IsTrue(source.IsBallInPlay(0), "T74：前提 —— 白球应仍在场上（本用例的主场景）。");

            int inPlayCount = 0;
            for (int i = 0; i < source.Balls.Count; i++)
            {
                if (source.IsBallInPlay(i))
                {
                    inPlayCount++;
                }
            }

            var beforeRecords = new List<int>();
            for (int i = 0; i < source.Balls.Count; i++)
            {
                beforeRecords.Add(source.CapturedPocketIndex(i));
            }

            var failures = new List<string>();

            // ── (a) probe 装载后的球数 == 在场上球数（不含已入袋球） ──
            ShotPrediction pred = ShotPredictor.Predict(table, source, new Vector2(3.0f, 0f), 600);
            if (pred.ProbeBallCount != inPlayCount)
            {
                failures.Add($"(a) 探测内核装到的球数 {pred.ProbeBallCount} ≠ 在场上球数 {inPlayCount}"
                             + "（已入袋球**复活**了，或漏装了在场上球）。");
            }

            if (pred.StepsSimulated <= 0)
            {
                failures.Add("(a) 前提：预测没跑（StepsSimulated ≤ 0）。");
            }

            // ── (b) 预测不改在跑内核的入袋记录 ──
            for (int i = 0; i < source.Balls.Count; i++)
            {
                if (source.CapturedPocketIndex(i) != beforeRecords[i])
                {
                    failures.Add($"(b) 球 {i} 的入袋索引 {beforeRecords[i]} → {source.CapturedPocketIndex(i)}（预测改动了在跑内核）。");
                }
            }

            // ── (c) 反证"不复活"：把**全部**球（含已入袋）喂给 `LoadBalls` ⇒ 那颗球回到场上 ──
            var allBalls = new BallState[source.Balls.Count];
            for (int i = 0; i < source.Balls.Count; i++)
            {
                allBalls[i] = new BallState { position = source.Balls[i].position, velocity = source.Balls[i].velocity };
            }

            var naive = new PoolSimulator(table, p);
            naive.LoadBalls(allBalls);
            if (!naive.IsBallInPlay(1))
            {
                failures.Add("(c) 反证失败 —— `LoadBalls` 竟**没有**把已入袋球清回场上；"
                             + "说明本工程的 `LoadBalls` 语义已变（快照筛选的前提不再成立）。");
            }

            // ── 附加：白球也离台时 ⇒ 按出杆口径补一颗在 `footSpot` 的白球（球数 = inPlayCount + 1） ──
            // ⚠ **必须另起一个干净局面**：若在"1 号球已入袋"的同一局里把白球也打进**同一个袋**，
            //   白球会在**喉平面之前**撞上袋里那颗**已冻结**的球（在跑内核**不跳过**"运动球 vs 冻结球"的对）
            //   ⇒ 白球根本进不了袋。**这正是规格登记的盲带 `R-AP`**（预测侧把离台球排除、实跑侧不排除）；
            //   本用例刻意避开它（附加场景里只让白球入袋，袋口附近没有其它冻结球）。
            var freshBalls = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                freshBalls[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            var cueOnly = SimOf(p, freshBalls);
            cueOnly.PlaceBall(0, pocket.throatCenter - 0.15f * tn, 1.0f * tn);
            for (int i = 0; i < 4000 && !cueOnly.TableAtRest; i++)
            {
                cueOnly.Step();
            }

            if (cueOnly.IsBallInPlay(0) || cueOnly.CapturedPocketIndex(0) < 0)
            {
                failures.Add($"(附加) 前提不成立 —— 白球未入袋：pos={RVec(cueOnly.Balls[0].position)}"
                             + $", pocketProx={cueOnly.PocketProximityEvents}, captured={cueOnly.CapturedBallCount}"
                             + $", atRest={cueOnly.TableAtRest}");
            }
            else
            {
                int inPlayAfter = 0;
                for (int i = 0; i < cueOnly.Balls.Count; i++)
                {
                    if (cueOnly.IsBallInPlay(i))
                    {
                        inPlayAfter++;
                    }
                }

                ShotPrediction predCueOff = ShotPredictor.Predict(table, cueOnly, new Vector2(3.0f, 0f), 600);
                if (predCueOff.ProbeBallCount != inPlayAfter + 1)
                {
                    failures.Add($"(附加) 白球离台时探测内核球数 {predCueOff.ProbeBallCount} ≠ 在场上球数 {inPlayAfter} + 1"
                                 + "（应与出杆口径一致：白球放回开球点）。");
                }

                if ((predCueOff.CueStart - table.footSpot).magnitude > 1e-6f)
                {
                    failures.Add($"(附加) 白球离台时预测起点应为 `footSpot` {RVec(table.footSpot)}，实测 {RVec(predCueOff.CueStart)}。");
                }

                TestContext.WriteLine($"T74(附加): 白球离台 ⇒ inPlay={inPlayAfter} ⇒ probe 球数 {predCueOff.ProbeBallCount}，"
                                      + $"起点 {RVec(predCueOff.CueStart)} = footSpot ✓");
            }

            Assert.IsEmpty(failures, "T74 快照语义失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T74: 主场景 inPlay={inPlayCount} ⇒ probe 球数 {pred.ProbeBallCount}（恰等）；"
                                  + "已入袋球的入袋记录未被改动；反证 `LoadBalls` 会清 `_frozen` 成立 ✓");
        }

        private static Pocket SidePocket(TableConfig table)
        {
            for (int i = 0; i < table.PocketCount; i++)
            {
                Pocket pk = table.GetPocket(i);
                if (pk.kind == "side")
                {
                    return pk;
                }
            }

            Assert.Fail("测试数据里没有中袋，用例前提不成立。");
            return default;
        }

        // ══════════════════ T75 幽灵球 == 首次接触瞬间的白球位置 ══════════════

        /// <summary>
        /// **幽灵球与目标球心距恰为 `2r`**（几何恒等式，**绝对判别**），且路径终点与它逐位一致、时间在合理区间。
        ///
        /// 期望值来源：**甲**（(a) 的 `2r` 几何恒等式，与被测对象无关）＋ **乙**（(b)(c) 的内部一致性）。
        /// **判别力**：`2r` 容差取 1e-6 m（float 级联残差 ~1e-7，留 10× 余量）；
        /// 错误定义的偏差是它的几万倍：漏 `2r` = 57.15 mm（5.7e4×）、"`Step()` 末端采样" ≈ 37.5 mm（3.75e4×）。
        /// **盲带**：只覆盖"白球 + 单目标球、无库接触"；(b)(c) 是**内部一致性**（**不**能检出内核缺陷）。
        /// **注入负对照（实测）**：幽灵球改取 `Step()` 末端白球位置 ⇒ **(a) FAIL**；改取"起点 + 方向 × 球心距"（漏 `2r`）⇒ **(a) FAIL**。
        /// </summary>
        [Test]
        public void T75_GhostBall_IsCuePositionAtFirstContact()
        {
            TableConfig table = Table();
            var cue = Ball(-0.40f, 0f, 0f, 0f);
            var target = Ball(0.20f, 0f, 0f, 0f);

            float d = (target.position - cue.position).magnitude;
            Assert.Greater(d, TwoR + 0.1f, "T75：前提 —— 两球间距应显著大于 2r。");

            const int maxSteps = 1200;
            ShotPrediction p = ShotPredictor.Predict(table, SimOf(FactoryParams(), cue, target), new Vector2(4.5f, 0f), maxSteps);

            var failures = new List<string>();
            if (!p.HasFirstContact)
            {
                failures.Add("前提：应发生首次球-球接触。");
                Assert.IsEmpty(failures, "T75 失败：\n" + string.Join("\n", failures));
                return;
            }

            // (a) 几何恒等式
            float gap = (p.GhostBall - p.TargetBallCenter).magnitude;
            float twoR = 2f * PoolConstants.BallRadius;
            if (!(Mathf.Abs(gap - twoR) <= 1e-6f))
            {
                failures.Add($"(a) |幽灵球 − 目标球心| = {gap:F9} m ≠ 2r = {twoR:F9} m（差 {gap - twoR:E3} m，容差 1e-6）"
                             + " ⇒ 幽灵球取的不是\"相切那一瞬\"。");
            }

            // (b) 路径终点 == 幽灵球（逐位）+ 非空转
            if (p.StepsSimulated < 1)
            {
                failures.Add($"(b) `StepsSimulated` = {p.StepsSimulated}（应为 ≥1）。");
            }

            if (p.CueWorldPath == null || p.CueWorldPath.Count == 0)
            {
                failures.Add("(b) `CueWorldPath` 为空。");
            }
            else if (RVec(p.CueWorldPath[p.CueWorldPath.Count - 1]) != RVec(p.GhostBall))
            {
                failures.Add($"(b) 路径末点 {RVec(p.CueWorldPath[p.CueWorldPath.Count - 1])} ≠ 幽灵球 {RVec(p.GhostBall)}（逐位要求）。");
            }

            // (c) 时间区间
            float upper = p.StepsSimulated * PoolSimulator.FixedDeltaTime;
            if (!(p.TimeToFirstContactSeconds > 0f) || !(p.TimeToFirstContactSeconds <= upper))
            {
                failures.Add($"(c) 到首接触时间 {p.TimeToFirstContactSeconds:R} 不在区间 (0, {upper:R}] 内。");
            }

            Assert.IsEmpty(failures, "T75 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T75: |幽灵球−目标球心| = {gap:R}（2r = {twoR:R}，残差 {gap - twoR:E3}）；"
                                  + $"路径点数 {p.CueWorldPath.Count}；t = {p.TimeToFirstContactSeconds:R} ∈ (0, {upper:R}] ✓");
        }

        // ══════════════════ T76 直球（无库）闭式一致 ═══════════════════════════

        /// <summary>
        /// **一维等质量正碰**的独立闭式：`GhostBall == c0 + u·(d − 2r)`、出射方向 == `u`、无库接触。
        ///
        /// 期望值来源：**甲**（`d − 2r` 与速度、减速度**无关** ⇒ 不受滚阻影响；**不**转写内核代码）。
        /// **盲带**：只覆盖**无库、单目标**；不覆盖斜碰与擦库（切片 B）。
        /// **判别力**：把"幽灵球定义/距离"钉到解析几何 —— 任何"漏 `2r` / 用球心距当接触点"的实现在此必红。
        /// **注入负对照（实测）**：幽灵球取 `c0 + u·d`（漏 `2r`）⇒ **(a) FAIL**；出射方向取 `u` 的反向 ⇒ **(b) FAIL**。
        /// </summary>
        [Test]
        public void T76_StraightShot_MatchesClosedForm()
        {
            TableConfig table = Table();
            Vector2 c0 = new Vector2(-0.40f, 0f);
            Vector2 t = new Vector2(0.20f, 0f);
            var cue = new BallState { position = c0, velocity = Vector2.zero };
            var target = new BallState { position = t, velocity = Vector2.zero };

            Vector2 u = (t - c0).normalized;
            float d = (t - c0).magnitude;
            Vector2 closedFormGhost = c0 + u * (d - 2f * PoolConstants.BallRadius);

            ShotPrediction p = ShotPredictor.Predict(table, SimOf(FactoryParams(), cue, target), u * 4.5f, 1200);

            var failures = new List<string>();
            if (!p.HasFirstContact)
            {
                failures.Add("前提：应发生首次球-球接触。");
                Assert.IsEmpty(failures, "T76 失败：\n" + string.Join("\n", failures));
                return;
            }

            // (a) 闭式幽灵球
            float err = (p.GhostBall - closedFormGhost).magnitude;
            if (!(err <= 1e-6f))
            {
                failures.Add($"(a) 幽灵球 {RVec(p.GhostBall)} ≠ 闭式 c0 + u·(d − 2r) = {RVec(closedFormGhost)}"
                             + $"（差 {err:E3} m，容差 1e-6；漏 2r 会差 2r = {2f * PoolConstants.BallRadius:F5} m）。");
            }

            // (b) 目标球出射方向 == u（< 0.05°）
            float angle = Vector2.Angle(p.TargetOutgoingDirection, u);
            if (!(angle < 0.05f))
            {
                failures.Add($"(b) 目标球出射方向与瞄准方向夹角 {angle:F4}°（要求 < 0.05°）"
                             + $"：实测 {RVec(p.TargetOutgoingDirection)} / 期望 {RVec(u)}。");
            }

            // (c) 无库接触
            if (p.CushionBounceCountBeforeContact != 0)
            {
                failures.Add($"(c) 首次接触前的库边接触次数 = {p.CushionBounceCountBeforeContact}（直球应为 0）。");
            }

            Assert.IsEmpty(failures, "T76 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T76: 幽灵球闭式差 {err:E3} m；出射方向夹角 {angle:F4}°；库接触 {p.CushionBounceCountBeforeContact} 次 ✓");
        }
    }
}
