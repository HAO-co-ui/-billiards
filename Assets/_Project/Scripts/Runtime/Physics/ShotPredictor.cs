using System.Collections.Generic;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **击球预测的纯核**：把"当前全盘状态 + 期望初速"喂给一个**独立的**内核实例，向前推演到
    /// **首次球-球接触**，取出**幽灵球**与两个出射方向。
    ///
    /// ── 三条结构保证（把"只读"变成可判定）────────────────────────────────
    /// ① **零扰动**：探测内核是**函数内的局部变量**，且本类**只读** `source` 的公开出口
    ///    （`Balls` / `Params` / `RestSpeedThresholdValue` / `RestStableFramesValue` /
    ///    `PocketCaptureRadiusScaleValue` / `IsBallInPlay`），**从不**调用 `source` 的
    ///    `Step` / `PlaceBall` / `LoadBalls` / `ResetDiagnostics`。
    /// ② **同源**：探测内核的构造参数**逐字段取自** `source` 的只读出口 ⇒ 参数 / 步长 / 终止 / 袋口乘子一致。
    /// ③ **纯函数**：无 `Input`、无 `Time`、无 `Random`、无 `MonoBehaviour` ⇒ 同一输入必得同一输出。
    ///
    /// ── 快照语义（**只装在场上球**）────────────────────────────────────────
    /// <see cref="PoolSimulator.LoadBalls"/> 会清掉 `_frozen` 与 `_capturedPocket`（= "全新一局"的定义）
    /// ⇒ **带冻结状态装载在结构上不可达**。故快照只收 `IsBallInPlay(i)` 为真的球（位置/速度原样照抄），
    /// **已入袋/擦袋角离台的球不表示**。白球若也不在场上，则按出杆口径补一颗在 `footSpot` 的白球。
    /// ⚠ **已知偏差（盲带）**：在跑内核里"运动球 vs 已冻结球"的对**不会**被跳过，理论上白球仍可撞到
    /// **袋里的球**；探测内核里没有那颗球 ⇒ 该窗口预测与实跑会分叉。窗口极窄（白球须自己也进入同一袋口区域），
    /// 本核**不声称**全场景等价。
    ///
    /// ── 盲带（如实声明）────────────────────────────────────────────────────
    /// · **虚线粒度**：路径按 `Step()`（1/120 s）采样 ⇒ 库边拐角处虚线有最多 ~37.5 mm 的圆角误差。
    ///   它**只影响虚线观感**；<see cref="ShotPrediction.GhostBall"/> 与出射方向来自内核 TOI 记录（~1e-6 m），**不受影响**。
    /// · **段接触计数粒度**：<see cref="ShotPrediction.CushionBounceCountBeforeContact"/> 只能按 `Step` 增量判定，
    ///   ⇒ 本核**只累计"接触之前那些完整 Step"**的段接触增量，**接触所在 Step 内**的段接触不参与判定
    ///   （保守、可判定；跨该 Step 的细分时刻无法从计数粒度分辨）。
    /// </summary>
    public static class ShotPredictor
    {
        /// <summary>
        /// 预测一杆。白球约定 = **索引 0**（与 `PhysicsTestDrive.Launch()` 同口径）。
        /// </summary>
        /// <param name="table">球桌几何真源；须与 <paramref name="source"/> 用同一资产。</param>
        /// <param name="source">在跑内核（**只读**；本方法不改它任何状态）。</param>
        /// <param name="cueVelocity">白球初速（规范物理平面，m/s）。</param>
        /// <param name="maxSteps">步数上限（≥1）。达到上限仍无首次接触 ⇒ <see cref="ShotPrediction.HasFirstContact"/> 为 false。</param>
        /// <remarks>
        /// **便利重载**：内部新建一个一次性工作区 ⇒ 输出与重载前**逐位相同**，但会分配。
        /// 热路径（每帧预测）请用带 <see cref="ShotPredictionWorkspace"/> 的重载以复用缓冲。
        /// </remarks>
        public static ShotPrediction Predict(TableConfig table, PoolSimulator source, Vector2 cueVelocity, int maxSteps)
        {
            return Predict(table, source, cueVelocity, maxSteps, new ShotPredictionWorkspace());
        }

        /// <inheritdoc cref="Predict(TableConfig, PoolSimulator, Vector2, int)"/>
        /// <param name="workspace">
        /// 可复用工作区（持有探测内核 + 快照/路径缓冲）。**调用方须为单消费者** ——
        /// 返回结果的 <see cref="ShotPrediction.CueWorldPath"/> 会**引用**该工作区的路径缓冲，
        /// 下一次用同一工作区调用本方法时会被改写（见 <see cref="ShotPredictionWorkspace"/> 的别名语义）。
        /// 传 <c>null</c> ⇒ 内部新建一个（等价便利重载）。
        /// </param>
        public static ShotPrediction Predict(TableConfig table, PoolSimulator source, Vector2 cueVelocity,
            int maxSteps, ShotPredictionWorkspace workspace)
        {
            if (workspace == null)
            {
                workspace = new ShotPredictionWorkspace();
            }

            var result = new ShotPrediction { CueWorldPath = workspace.Path };
            workspace.Path.Clear();
            if (table == null || source == null || maxSteps <= 0)
            {
                return result;
            }

            // ── ① 独立探测内核：参数 / 步长 / 终止 / 袋口乘子**逐字段取自只读出口** ──
            //   **复用**：缓存键（桌 / 参数 / 三个标量）任一不符即重建，保证与"新建"路径等价。
            PoolSimulator probe = workspace.Probe;
            if (probe == null
                || !ReferenceEquals(workspace.ProbeTable, table)
                || !workspace.ProbeParams.Equals(source.Params)
                || workspace.ProbePocketScale != source.PocketCaptureRadiusScaleValue
                || workspace.ProbeRestSpeed != source.RestSpeedThresholdValue
                || workspace.ProbeRestFrames != source.RestStableFramesValue)
            {
                probe = new PoolSimulator(table, source.Params, source.PocketCaptureRadiusScaleValue,
                    source.RestSpeedThresholdValue, source.RestStableFramesValue);
                workspace.Probe = probe;
                workspace.ProbeTable = table;
                workspace.ProbeParams = source.Params;
                workspace.ProbePocketScale = source.PocketCaptureRadiusScaleValue;
                workspace.ProbeRestSpeed = source.RestSpeedThresholdValue;
                workspace.ProbeRestFrames = source.RestStableFramesValue;
            }
            else
            {
                // 复用：`LoadBalls` **不清每杆计数** ⇒ 必须显式归零，否则接触计数会跨次累积。
                // （新建路径的归零发生在构造函数里 ⇒ 补这一步后两条路径状态等价。）
                probe.ResetDiagnostics();
            }

            // ── ② 快照：只装在场上球（白球离台时补一颗在**开球位**） ──
            IReadOnlyList<BallState> srcBalls = source.Balls;
            List<BallState> snapshot = workspace.Snapshot;
            snapshot.Clear();
            int cueLocalIndex = -1;

            if (!source.IsBallInPlay(0))
            {
                // 白球不在场上（入袋 / 擦袋角离台）⇒ 与出杆口径一致：放回**开球位**
                // （⚠ 与 `PhysicsTestDrive.Launch()` 的重摆点**必须同源** —— 否则预测线与实际出杆位置分叉）。
                snapshot.Add(new BallState { position = PhysicsTestDrive.CueBallHomePosition(table.footSpot), velocity = Vector2.zero });
                cueLocalIndex = 0;
            }

            for (int i = 0; i < srcBalls.Count; i++)
            {
                if (!source.IsBallInPlay(i))
                {
                    continue;   // 已离台：不表示（见类型注释的盲带）
                }

                if (i == 0)
                {
                    cueLocalIndex = snapshot.Count;
                }

                snapshot.Add(new BallState { position = srcBalls[i].position, velocity = srcBalls[i].velocity });
            }

            if (cueLocalIndex < 0)
            {
                // 兜底：源内核一颗球都没有（`LoadBalls` 之前）⇒ 至少放一颗白球在**开球位**。
                snapshot.Add(new BallState { position = PhysicsTestDrive.CueBallHomePosition(table.footSpot), velocity = Vector2.zero });
                cueLocalIndex = 0;
            }

            probe.LoadBalls(snapshot);

            // ── ③ 置初速：只动白球（`PlaceBall` 同时同步 `_vAvg` 与表级终止标志） ──
            Vector2 cueStart = snapshot[cueLocalIndex].position;
            probe.PlaceBall(cueLocalIndex, cueStart, cueVelocity);

            // 路径缓冲**复用**（`workspace.Path`，已在开头 Clear）；`result.CueWorldPath` 已引用它。
            List<Vector2> path = workspace.Path;
            path.Add(cueStart);

            // ── ④ 步进到首接触（首接触**即停**：不跑到整盘静止） ──
            // ⚠ **先清空接触事件缓冲**：探测内核是**复用**的 ⇒ 缓冲里可能残留上一次预测的事件
            //   （drain 语义 = 读即清；这里读出来直接丢掉 ⇒ 保证下面取到的是**本次**的首条事件）。
            workspace.ContactEvents.Clear();
            probe.DrainContactEvents(workspace.ContactEvents);
            workspace.ContactEvents.Clear();

            int steps = 0;
            int bounces = 0;
            int visibleBeforeFirstCushion = -1;   // ← **首次库边接触之前的顶点数**（绘制截断用；见 `VisiblePathCount`）
            while (steps < maxSteps && !probe.HasBallBallContact)
            {
                int segmentContactsBefore = probe.TotalContactCount - probe.BallBallContactCount;

                probe.Step();
                steps++;

                if (probe.HasBallBallContact)
                {
                    break;   // 本步的"白球位置"由幽灵球承担（下面补），不采接触后的位置
                }

                int segmentContactsAfter = probe.TotalContactCount - probe.BallBallContactCount;
                if (bounces == 0 && segmentContactsAfter > segmentContactsBefore)
                {
                    // **首次库边接触**就发生在**本步之内**，而本步的顶点**还没入列**
                    // ⇒ 此刻 `path.Count` 恰好 = "接触前已画出的顶点数"（= 折返开始前最后一刻）
                    // ⇒ 白线就画到这里（**不画折返**）。亚步误差 ≤ 一步位移（见 `ShotPrediction.VisiblePathCount`）。
                    // ⚠ 若首次库边接触发生在**第 0 步**（白球已贴库 + 瞄向该库），这里会取到 **1**
                    //   ⇒ 前缀不足 2 个顶点就画不出任何线段 ⇒ **白线整条消失**（实机：白球贴库时"瞄准线没了"）。
                    //   故下面的落地处用 `>= 2` 兜底回落到整条（详见 `ShotPrediction.VisiblePathCount` 文档）。
                    visibleBeforeFirstCushion = path.Count;
                }

                path.Add(probe.Balls[cueLocalIndex].position);
                bounces += segmentContactsAfter - segmentContactsBefore;
            }

            result.CueStart = cueStart;
            result.StepsSimulated = steps;
            result.CushionBounceCountBeforeContact = bounces;
            result.BallBallContactCount = probe.BallBallContactCount;
            result.TotalContactCount = probe.TotalContactCount;
            result.ProbeBallCount = snapshot.Count;
            result.HasFirstContact = probe.HasBallBallContact;

            if (!probe.HasBallBallContact)
            {
                result.CueWorldPath = path;
                result.VisiblePathCount = visibleBeforeFirstCushion >= 2 ? visibleBeforeFirstCushion : path.Count;
                FillFirstContact(ref result, path, probe, workspace.ContactEvents, default);
                return result;
            }

            // ── ⑤ 出几何：白球那一侧的位置 = 幽灵球；另一颗 = 目标球 ──
            BallBallContactRecord rec = probe.FirstBallBallContact;
            bool cueIsFirst = rec.first == cueLocalIndex;

            Vector2 ghost = cueIsFirst ? rec.firstPosition : rec.secondPosition;
            Vector2 targetCenter = cueIsFirst ? rec.secondPosition : rec.firstPosition;
            Vector2 cueVelocityAfter = cueIsFirst ? rec.firstVelocityAfter : rec.secondVelocityAfter;
            Vector2 targetVelocityAfter = cueIsFirst ? rec.secondVelocityAfter : rec.firstVelocityAfter;

            path.Add(ghost);   // 末点 == 幽灵球（逐位）

            // **可见路径**止于首次库边接触之前（不画折返）；无库边接触 ⇒ 整条。
            // ⚠ 必须在 `path.Add(ghost)` **之后**算：`path.Count` 此刻才是最终顶点数。
            result.VisiblePathCount = visibleBeforeFirstCushion >= 2 ? visibleBeforeFirstCushion : path.Count;

            // ⚠ 必须在 `ghost` 算出来**之后**调：球-球首碰的可见末点要用**精确幽灵球**（见 `FillFirstContact`）。
            FillFirstContact(ref result, path, probe, workspace.ContactEvents, ghost);

            result.GhostBall = ghost;
            result.TargetBallCenter = targetCenter;
            result.TimeToFirstContactSeconds = rec.timeSeconds;
            result.CueWorldPath = path;
            result.CueOutgoingDirection = Normalize(cueVelocityAfter);
            // 目标球出射：解算后速度方向；退化（≈0）⇒ 回落到"幽灵球 → 目标球心"连线
            //（等质量、无自旋下的精确出射方向，与法向 `n̂` 同向）。
            result.TargetOutgoingDirection = Normalize(targetVelocityAfter);
            if (IsDegenerate(result.TargetOutgoingDirection))
            {
                result.TargetOutgoingDirection = Normalize(targetCenter - ghost);
            }

            return result;
        }

        private static Vector2 Normalize(Vector2 v)
        {
            float len2 = v.x * v.x + v.y * v.y;
            if (len2 <= 1e-24f)
            {
                return Vector2.zero;
            }

            float inv = 1f / PoolMath.Sqrt(len2);
            return new Vector2(v.x * inv, v.y * inv);
        }

        private static bool IsDegenerate(Vector2 v)
        {
            return v.x == 0f && v.y == 0f;
        }

        /// <summary>
        /// **填充"首个接触"的只读出口 + 可见路径末点**（**单点实现**，`Predict` 的两个出口共用）。
        ///
        /// · 从内核事件流 drain 出**本次预测**的全部事件，取**第 0 条**（**时间序首条**）⇒ 类别 + 位置；
        /// · `VisiblePathEnd` = 有接触 ⇒ **精确接触点**（亚步误差归零）；无接触 ⇒ 路径末点；
        /// · ⚠ **必须比事件序号**：同一 `Step` 内的子步可能**先记库边、再记球-球** ⇒ 若只看 `HasFirstContact`（球-球）
        ///   会**漏掉**库边首碰，从而画错圆（该叫"库边圆"却画成幽灵球圆）；
        /// · ⚠ `buffer` 由调用方（workspace）复用；本方法开头先 `Clear`，语义与 `DrainContactEvents`（读即清）一致。
        /// </summary>
        private static void FillFirstContact(ref ShotPrediction result, IReadOnlyList<Vector2> path,
            PoolSimulator probe, List<ContactEvent> buffer, Vector2 ballBallEnd)
        {
            buffer.Clear();
            int count = probe.DrainContactEvents(buffer);

            if (count > 0)
            {
                ContactEvent first = buffer[0];
                result.HasFirstContactEvent = true;
                result.FirstContactKind = first.kind;
                result.FirstContactPosition = first.position;
                result.HasCushionFirstContact = first.kind == ContactKind.Cushion;
                if (result.HasCushionFirstContact)
                {
                    result.CushionContactPosition = first.position;
                }

                // ⚠ **球-球首碰**：事件里的 `position` 是**两球中点**，而"白线该停在哪"是**幽灵球**（= 白球球心在接触瞬间的位置）
                //   ⇒ 用调用方传入的**精确幽灵球**（与 `CueWorldPath` 末点逐位一致）⇒ 线不会多伸进球里半个球径。
                //   库边首碰：事件里的 `position` **就是球心** ✓ 直接用。
                result.VisiblePathEnd = first.kind == ContactKind.BallBall ? ballBallEnd : first.position;
                return;
            }

            result.HasFirstContactEvent = false;
            result.HasCushionFirstContact = false;
            result.VisiblePathEnd = path.Count > 0 ? path[path.Count - 1] : result.CueStart;
        }
    }
}
