using System;
using System.Collections.Generic;
using Billiards.Core;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 自研二维定长步进台球物理内核（单球 + 球-库边 + 袋喉越界冻结）。
    ///
    /// 设计要点（每条都对应一个可失败的判别测试，见 spec §2）：
    ///  1. **定长步进**：外部只调 <see cref="Step"/>，内部固定 1/120 s，再细分为
    ///     <see cref="SubSteps"/> 个子步（子步数由 <see cref="DeriveSubSteps"/> **推导**，
    ///     不是硬编码）。
    ///  2. **端点精确积分**：子步内位置按**梯形/中点均速**推进（<c>v_avg = v − ½·a·Δt·v̂</c>），
    ///     而非前向欧拉 <c>p += v·Δt</c>；TOI 与位置推进共用同一 <c>v_avg</c>。
    ///  3. **TOI（time of impact）**：每个子步按"最早接触时刻"精确推进到接触点，
    ///     同刻接触（|Δt| ≤ <see cref="ToiTimeEpsilon"/>）归并为一组统一解算。
    ///  4. **接触解算**：法向与切向**各自独立赋值**（先法向恢复 e，再切向乘 μ），
    ///     法向由 <c>normalize(C − Q)</c> 在接触点现场求得，**不需要逐段朝向数据**。
    ///  5. **确定性**：全部状态推进按固定顺序（子步 → 球索引升序 → 段索引升序），
    ///     不使用 <c>HashSet</c>/<c>Dictionary</c> 迭代序，不引 <c>Time</c>/<c>Random</c>/PhysX。
    ///     段数组顺序 = 资源顺序 = JSON 顺序，构造时摊平、**不重排**。
    ///
    /// 本类只依赖 <see cref="PoolMath"/> 做开方/三角（工程标准 R5-3）。
    /// </summary>
    public sealed class PoolSimulator
    {
        // ── 定长步进与 TOI 契约常量 ────────────────────────────────────────

        /// <summary>固定步长，1/120 s。外部每次 <see cref="Step"/> 推进的时间。</summary>
        public const float FixedDeltaTime = 1f / 120f;

        /// <summary>
        /// 单个子步内 TOI 迭代次数上限。超过即计 <see cref="ToiIterationGuardTrips"/> 并截断该子步（不静默）。
        ///
        /// **R-C 修复后迭代预算由 16 提升为 128**：原 16 在"球楔死在凹袋角、
        /// 以 nm 级间隙反复微接触"时不足（实测 `ToiIterationGuardTrips = 1810`，326/1230 次发射）。
        /// 配合 <see cref="StoppedSpeedEpsilon"/> 与"零时间推进即停"，实测 `guardTrips` 变化：
        /// `1810（原）→ 1196（仅近零速度）→ 339（+零时间推进即停）→ 3（预算 64）→ 0（预算 128）`，
        /// 且 `EnergyGainViolations = 0`、最大嵌入 6.147e-08 m、85/85 测试全绿。
        /// 128 仍是**有限**安全网（配合两个即停判据，循环不会无进展空转），仅覆盖"真实多次接触链"。
        /// </summary>
        public const int MaxToiIterations = 128;

        /// <summary>同刻接触归并窗口，秒。|t − earliest| ≤ 该值视为同刻接触。</summary>
        public const float ToiTimeEpsilon = 1e-9f;

        /// <summary>子步剩余时间终止阈值，秒。循环条件是 <c>remaining &gt; 该值</c>（**不能写 &gt; 0**）。</summary>
        public const float ToiRemainingEpsilon = 1e-9f;

        /// <summary>
        /// 接触弦-弧误差界，米（= 5e-5 m）。用于推导子步数：以离散弦逼近库边弧线时，
        /// 半个子步内的最大弦高误差 <c>½·a_max·Δt_sub²</c> 不得超过该值。
        /// </summary>
        public const float ContactArcErrorBound = 5e-5f;

        /// <summary>
        /// 子步数推导所用的前瞻加速度上界 <c>a_max = 5 m/s²</c>。
        /// 这是**前瞻声明值**：实际的 <c>rollingDeceleration</c> 为 0，
        /// 该上界用于给未来的滚动阻力留出子步数余量，不参与实际的滚动积分。
        /// </summary>
        public const float MaxRollingDeceleration = 5f;

        /// <summary>
        /// 推导子步数时对"比值恰为整数"的浮点舍入保护。见 <see cref="DeriveSubSteps"/>。
        /// </summary>
        private const float SubStepCeilEpsilon = 1e-5f;

        /// <summary>
        /// 视为"已停止"的速度阈值，m/s。**R-C 修复（实测依据见下）**。
        ///
        /// 依据实测：球被两侧袋角段同时接触时，接触解算把速度按 `e^{2k}` **几何衰减**
        /// （实测 `speedAfterResolve` 4.969e-9 → 4.025e-9 → …，逐次 ×0.81 = e²），
        /// **永不为精确 0**。原实现用**精确零**比较判停止（`velocity.x == 0f && velocity.y == 0f`）
        /// ⇒ 永不触发 ⇒ 同一批 `t = 0` 接触被反复重解 16 次后被 <see cref="MaxToiIterations"/> 截断
        /// （实测 `ToiIterationGuardTrips = 1810`，`zeroEarliestRatio = 0.6495`，`remaining` 一步不减）。
        ///
        /// `1e-6 m/s`（= 1 µm/s，1 秒位移 1 µm，可忽略）**远高于**该残差（~5e-9）、
        /// **远低于**任何玩法相关速度 ⇒ 用近零阈值取代精确零，既消除空转又不改变可观测物理。
        /// </summary>
        public const float StoppedSpeedEpsilon = 1e-6f;

        // ── 只读几何缓存（构造时摊平，顺序 = 资源顺序）──────────────────────

        private readonly Vector2[] _segA;
        private readonly Vector2[] _segB;
        private readonly Vector2[] _pocketThroatCenter;
        private readonly Vector2[] _pocketThroatNormal;

        private readonly PhysicsParams _params;

        // ── 球状态与诊断 ──────────────────────────────────────────────────

        private BallState[] _balls = new BallState[0];
        private bool[] _frozen = new bool[0];
        private readonly List<int> _contactGroup = new List<int>(8);

        /// <summary>球-库边接触总次数（诊断，非物理量）。</summary>
        public int TotalContactCount { get; private set; }

        /// <summary>TOI 循环超过 <see cref="MaxToiIterations"/> 的次数（每次意味着该子步被截断）。</summary>
        public int ToiIterationGuardTrips { get; private set; }

        /// <summary>接触后速度模长增大的次数（应为 0；非 0 表示接触解算引入了能量增益）。</summary>
        public int EnergyGainViolations { get; private set; }

        /// <summary>球心越过袋喉平面的次数（每次随即冻结该球）。</summary>
        public int PocketProximityEvents { get; private set; }

        /// <summary>库边/袋角/弧段总数（= <c>TableConfig.SegmentCount</c>）。</summary>
        public int SegmentCount => _segA.Length;

        /// <summary>子步数 = <see cref="DeriveSubSteps"/>(<see cref="PoolConstants.MaxBallSpeed"/>)。</summary>
        public int SubSteps { get; }

        /// <summary>子步时间步长 = <see cref="FixedDeltaTime"/> / <see cref="SubSteps"/>。</summary>
        public float SubStepDeltaTime { get; }

        /// <summary>
        /// **仅测试用**。为 true 时把"逐步 TOI 接触检测"替换为"子步末采样检测"，
        /// 但**复用同一条生产推进路径**（同一 <c>v_avg</c> 积分、同一 <see cref="ResolveContact"/>、
        /// 同一子步末减速与袋喉检查）——否则负对照测的不是同一个东西。
        /// 用于 T5b 证明 T5a 有判别力。
        /// </summary>
        public bool DisableToiForDiagnosticsOnly { get; set; }

        /// <summary>当前球状态（只读视图）。返回元素为值类型副本。</summary>
        public IReadOnlyList<BallState> Balls => _balls;


        // ── 构造 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 用球桌碰撞几何与物理参数构造内核。构造时把 <c>TableConfig.segments</c>
        /// 摊平为数组，**保持资源/JSON 原始顺序**（顺序参与确定性，不得重排）。
        /// </summary>
        public PoolSimulator(TableConfig table, PhysicsParams parameters)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            _params = parameters;

            int segCount = table.SegmentCount;
            _segA = new Vector2[segCount];
            _segB = new Vector2[segCount];
            for (int i = 0; i < segCount; i++)
            {
                CushionSegment s = table.segments[i];
                _segA[i] = s.a;
                _segB[i] = s.b;
            }

            int pocketCount = table.PocketCount;
            _pocketThroatCenter = new Vector2[pocketCount];
            _pocketThroatNormal = new Vector2[pocketCount];
            for (int i = 0; i < pocketCount; i++)
            {
                Pocket p = table.pockets[i];
                _pocketThroatCenter[i] = p.throatCenter;
                _pocketThroatNormal[i] = p.throatNormal;
            }

            SubSteps = DeriveSubSteps(PoolConstants.MaxBallSpeed);
            SubStepDeltaTime = FixedDeltaTime / SubSteps;

            ResetDiagnostics();
        }

        // ── 子步数推导（必须是推导值，不是硬编码）──────────────────────────

        /// <summary>
        /// 由 <paramref name="maxBallSpeed"/> 推导子步数，取两条判据的**较大者**：
        /// <list type="bullet">
        /// <item>A 离散回退界：<c>v_max·(dt/n) ≤ ½·BallDiameter</c>
        ///       —— 一个子步内的位移不超过半个球直径，保证 TOI 不会被"跳过"。</item>
        /// <item>B 接触弦-弧误差界：<c>½·a_max·(dt/n)² ≤ ContactArcErrorBound</c>
        ///       —— 半个子步的弦高误差不超过 5e-5 m。</item>
        /// </list>
        /// 当前 <c>MaxBallSpeed = 8</c> ⇒ A 得 3、B 得 2 ⇒ n = 3。
        ///
        /// 采用 **ceil 语义**（比值恰为整数时取该整数，例如恰为 3 时返回 3 而非 4）。
        /// 为避免浮点尾差把"恰为整数"抬成整数 + 1（实测 <c>v=10.287</c> 时比值 =
        /// 3.0000002384），在取 ceil 前减去极小量 <see cref="SubStepCeilEpsilon"/>；
        /// 这不是把 <c>≤</c> 换成 <c>&lt;</c>（那会系统性偏离 1），而是对边界做浮点保护。
        /// </summary>
        public static int DeriveSubSteps(float maxBallSpeed)
        {
            float dt = FixedDeltaTime;

            float halfDiameter = PoolConstants.BallDiameter * 0.5f;
            float ratioA = (maxBallSpeed * dt) / halfDiameter;

            float dtForArcError = PoolMath.Sqrt((2f * ContactArcErrorBound) / MaxRollingDeceleration);
            float ratioB = dt / dtForArcError;

            int nA = CeilWithEpsilon(ratioA);
            int nB = CeilWithEpsilon(ratioB);
            return Mathf.Max(1, Mathf.Max(nA, nB));
        }

        private static int CeilWithEpsilon(float ratio)
        {
            return Mathf.CeilToInt(ratio - SubStepCeilEpsilon);
        }

        // ── 装载与推进 ────────────────────────────────────────────────────

        /// <summary>装载球状态（拷贝进内核，外部数组后续变动不影响内核）。</summary>
        public void LoadBalls(IReadOnlyList<BallState> balls)
        {
            if (balls == null)
            {
                _balls = new BallState[0];
                _frozen = new bool[0];
                return;
            }

            _balls = new BallState[balls.Count];
            _frozen = new bool[balls.Count];
            for (int i = 0; i < balls.Count; i++)
            {
                _balls[i] = balls[i];
                _frozen[i] = false;
            }
        }

        /// <summary>清零全部诊断计数器。</summary>
        public void ResetDiagnostics()
        {
            TotalContactCount = 0;
            ToiIterationGuardTrips = 0;
            EnergyGainViolations = 0;
            PocketProximityEvents = 0;
        }

        /// <summary>
        /// 推进固定步长 <see cref="FixedDeltaTime"/>。推进顺序为
        /// **子步（升序）→ 球索引（升序）**，保证逐帧可复现。
        /// </summary>
        public void Step()
        {
            for (int sub = 0; sub < SubSteps; sub++)
            {
                for (int i = 0; i < _balls.Length; i++)
                {
                    if (_frozen[i])
                    {
                        continue;
                    }

                    IntegrateBall(i);
                }
            }
        }

        // ── 单球单子步积分（生产与诊断采样**共用同一路径**）────────────────

        private void IntegrateBall(int index)
        {
            float dt = SubStepDeltaTime;
            BallState ball = _balls[index];

            // R-C 修复：近零速度即视为停止（原用精确零判据，几何衰减永不达零 ⇒ 空转）。
            if (Length(ball.velocity) <= StoppedSpeedEpsilon)
            {
                ball.velocity = Vector2.zero;
                _balls[index] = ball;
                return;
            }

            float a = _params.rollingDeceleration;
            Vector2 vAvg = AverageVelocity(ball.velocity, a, dt);

            float remaining = dt;
            int iterations = 0;
            bool previousIterationWasZeroTime = false; // R-C 修复用

            while (remaining > ToiRemainingEpsilon)
            {
                if (++iterations > MaxToiIterations)
                {
                    // 不静默：计成可断言的计数器并截断该子步。
                    ToiIterationGuardTrips++;
                    break;
                }

                float earliest;
                _contactGroup.Clear();

                if (DisableToiForDiagnosticsOnly)
                {
                    // 诊断负对照：不做逐步 TOI，只在子步末采样接触。
                    // 其余（位置推进 / 解算 / 减速 / 袋喉）与生产路径完全一致。
                    earliest = remaining;
                    CollectOverlaps(ball.position + vAvg * remaining, _contactGroup);
                }
                else
                {
                    earliest = EarliestToi(ball.position, vAvg, remaining, _contactGroup);
                }

                if (_contactGroup.Count == 0)
                {
                    ball.position += vAvg * remaining;
                    break;
                }

                // ★ 精确推进到接触时刻（与 TOI 共用同一 v_avg）。
                ball.position += vAvg * earliest;
                remaining -= earliest;

                ResolveContact(ref ball, _contactGroup);

                // R-C 修复：接触后若速度已降至近零，明确置零并结束该子步
                // （原用精确零比较，几何衰减永不达零 ⇒ 同一批 t=0 接触被重解 16 次后截断）。
                if (Length(ball.velocity) <= StoppedSpeedEpsilon)
                {
                    ball.velocity = Vector2.zero;
                    break;
                }

                // ★ R-C 修复（第二半）：**连续两次"零时间推进"的接触 ⇒ 持续接触/楔死**。
                // 依据实测（空转诊断，d4-rc-diagnosis.json）：球被两侧袋角段同时接触时
                // `earliest = 0`、`remaining` 一步不减，接触解算只把速度乘 e^k，**不产生自由飞行时间**
                // ⇒ 再迭代只会重解同一批 t=0 接触，直至耗尽 MaxToiIterations（实测 1810 次）。
                // 一次 t=0 接触是合法的（球恰在区间起点接触、解算后即分离，下一迭代无接触）；
                // **连续两次** t=0 才说明解算无法把球分离出去 ⇒ 该子步已无自由飞行时间，结束。
                // （若某次是真实入射，解算后速度朝外 ⇒ 下一迭代不再产生 t=0 接触，不会误触发。）
                bool zeroTimeAdvance = earliest <= ToiTimeEpsilon;
                if (zeroTimeAdvance && previousIterationWasZeroTime)
                {
                    break;
                }

                previousIterationWasZeroTime = zeroTimeAdvance;

                // 接触后按新速度重取均速。
                vAvg = AverageVelocity(ball.velocity, a, remaining);
            }

            if (a > 0f)
            {
                ApplyRollingDeceleration(ref ball, a, dt);
            }

            _balls[index] = ball;
            CheckPocketProximity(index);
        }

        /// <summary>
        /// 端点精确均速：<c>v_avg = v − ½·a·Δt·v̂</c>；若与 <c>v</c> 反向（减速把球推回）取零。
        /// 位置推进与 TOI 必须共用本函数的结果。
        /// </summary>
        private static Vector2 AverageVelocity(Vector2 v, float a, float dt)
        {
            if (a <= 0f)
            {
                return v;
            }

            Vector2 vHat = Normalize(v);
            Vector2 vAvg = v - (0.5f * a * dt) * vHat;
            if (Dot(vAvg, v) < 0f)
            {
                return Vector2.zero;
            }

            return vAvg;
        }

        private static void ApplyRollingDeceleration(ref BallState ball, float a, float dt)
        {
            float speed = Length(ball.velocity);
            if (speed <= 0f)
            {
                return;
            }

            float dv = a * dt;
            if (speed <= dv)
            {
                ball.velocity = Vector2.zero;
                return;
            }

            float keep = (speed - dv) / speed;
            ball.velocity = ball.velocity * keep;
        }

        // ── TOI 求解 ──────────────────────────────────────────────────────

        /// <summary>
        /// 求球心从 <paramref name="p"/> 以均速 <paramref name="v"/> 运动时，
        /// 与线段 [a,b] 的"带球半径胶囊"的**最早进入**时刻 t ∈ [0, remaining]。
        /// 无接触返回 <c>+∞</c>；同刻接触（|t−earliest| ≤ 归并窗口）的段并入同组。
        /// 仅接受"进入"接触（接触点法向与速度相反），避免已分离时 t=0 反复自触发。
        /// </summary>
        private float EarliestToi(Vector2 p, Vector2 v, float remaining, List<int> group)
        {
            float earliest = float.PositiveInfinity;
            float r = PoolConstants.BallRadius;

            for (int i = 0; i < _segA.Length; i++)
            {
                float t = SolveToi(p, v, _segA[i], _segB[i], r);
                if (t < 0f || t > remaining)
                {
                    continue;
                }

                if (t < earliest - ToiTimeEpsilon)
                {
                    earliest = t;
                    group.Clear();
                    group.Add(i);
                }
                else if (Mathf.Abs(t - earliest) <= ToiTimeEpsilon)
                {
                    group.Add(i);
                }
            }

            return earliest;
        }

        /// <summary>
        /// 单段 TOI：把"动点 vs 半径 r 的线段胶囊"拆成两类解析求交 ——
        /// ①动点 vs 线段所在直线（投影须落在段内）；②动点 vs 两端点圆。
        /// 取较早的有效"进入"根。
        /// </summary>
        private static float SolveToi(Vector2 p, Vector2 v, Vector2 a, Vector2 b, float r)
        {
            float best = float.PositiveInfinity;

            float abx = b.x - a.x;
            float aby = b.y - a.y;
            float len2 = abx * abx + aby * aby;

            if (len2 > 1e-12f)
            {
                float len = PoolMath.Sqrt(len2);
                // 线段单位法向。
                float nx = -aby / len;
                float ny = abx / len;

                float s0 = (p.x - a.x) * nx + (p.y - a.y) * ny;
                float sv = v.x * nx + v.y * ny;

                // 仅当球在带外、且法向速度指向该面时才存在"进入"解。
                if (Mathf.Abs(s0) >= r && sv * s0 < 0f)
                {
                    float sTarget = s0 > 0f ? r : -r;
                    float t = (sTarget - s0) / sv;
                    if (t >= 0f && t < best)
                    {
                        float cx = p.x + t * v.x;
                        float cy = p.y + t * v.y;
                        float u = ((cx - a.x) * abx + (cy - a.y) * aby) / len2;
                        if (u >= 0f && u <= 1f)
                        {
                            best = t;
                        }
                    }
                }
            }

            best = Mathf.Min(best, RayCircleToi(p, v, a, r));
            best = Mathf.Min(best, RayCircleToi(p, v, b, r));
            return best;
        }

        /// <summary>动点 vs 半径 r 的圆的最早"进入"时刻；无进入返回 +∞。</summary>
        private static float RayCircleToi(Vector2 p, Vector2 v, Vector2 c, float r)
        {
            float A = v.x * v.x + v.y * v.y;
            if (A <= 1e-20f)
            {
                return float.PositiveInfinity;
            }

            float mx = p.x - c.x;
            float my = p.y - c.y;
            float B = 2f * (mx * v.x + my * v.y);
            float C = mx * mx + my * my - r * r;

            float disc = B * B - 4f * A * C;
            if (disc < 0f)
            {
                return float.PositiveInfinity;
            }

            float sq = PoolMath.Sqrt(disc);
            float t = (-B - sq) / (2f * A);
            if (t < 0f)
            {
                t = 0f;
            }

            // 校验"进入"：接触点法向与速度相反。
            float cnx = p.x + t * v.x - c.x;
            float cny = p.y + t * v.y - c.y;
            float cnLen = PoolMath.Sqrt(cnx * cnx + cny * cny);
            if (cnLen > 1e-12f)
            {
                float vn = (v.x * cnx + v.y * cny) / cnLen;
                if (vn >= 0f)
                {
                    return float.PositiveInfinity;
                }
            }

            return t;
        }

        // ── 接触解算 ──────────────────────────────────────────────────────

        /// <summary>
        /// 解算一组同刻接触。法向由**接触点现场**求得 <c>n̂ = normalize(C − Q)</c>
        /// （Q = 段上距球心最近点），因此**不需要逐段朝向数据**：球从内侧撞上来时它恒为内法向。
        /// 法向与切向**各自独立赋值**（先 <c>v −= (1+e)·v_n·n̂</c>，再 <c>v −= (1−μ)·v_t</c>）。
        /// </summary>
        private void ResolveContact(ref BallState ball, List<int> group)
        {
            float speedIn = Length(ball.velocity);

            for (int g = 0; g < group.Count; g++)
            {
                int i = group[g];
                Vector2 q = ClosestPointOnSegment(ball.position, _segA[i], _segB[i]);
                Vector2 diff = ball.position - q;

                Vector2 n;
                float d2 = diff.x * diff.x + diff.y * diff.y;
                if (d2 < 1e-18f)
                {
                    // 退化：球心恰在段上，取速度反方向为法向（无定义时的唯一可行约定）。
                    n = Normalize(-ball.velocity);
                    if (n.x == 0f && n.y == 0f)
                    {
                        n = new Vector2(0f, 1f);
                    }
                }
                else
                {
                    n = diff / PoolMath.Sqrt(d2);
                }

                Vector2 v = ball.velocity;
                float vn = Dot(v, n);
                Vector2 vt = v - vn * n;

                // 法向：独立赋值（恢复系数 e）。
                v = v - ((1f + _params.cushionRestitution) * vn) * n;

                // 切向：独立赋值（摩擦保留系数 μ）。
                if (vt.x * vt.x + vt.y * vt.y > 0f)
                {
                    v = v - (1f - _params.cushionTangentialFriction) * vt;
                }

                ball.velocity = v;
                TotalContactCount++;
            }

            float speedOut = Length(ball.velocity);
            if (speedOut > speedIn * (1f + 1e-6f))
            {
                EnergyGainViolations++;
            }
        }

        /// <summary>子步末采样：收集与球心距离 &lt; r 的段（仅诊断负对照使用）。</summary>
        private void CollectOverlaps(Vector2 p, List<int> into)
        {
            float r = PoolConstants.BallRadius;
            float r2 = r * r;

            for (int i = 0; i < _segA.Length; i++)
            {
                Vector2 q = ClosestPointOnSegment(p, _segA[i], _segB[i]);
                float dx = p.x - q.x;
                float dy = p.y - q.y;
                if (dx * dx + dy * dy < r2)
                {
                    into.Add(i);
                }
            }
        }

        // ── 袋喉越界冻结 ──────────────────────────────────────────────────

        /// <summary>
        /// 袋口判据用**喉平面**而非半径：球心满足 <c>dot(C − throatCenter, throatNormal) &gt; 0</c>
        /// 即视为越界，冻结该球（速度置零、停止积分）并累计 <see cref="PocketProximityEvents"/>。
        /// 喉平面判据对悬空端点的分辨余量为毫米级，远高于半径判据的微米级余量。
        /// </summary>
        private void CheckPocketProximity(int index)
        {
            if (_frozen[index])
            {
                return;
            }

            Vector2 c = _balls[index].position;

            for (int p = 0; p < _pocketThroatCenter.Length; p++)
            {
                Vector2 tc = _pocketThroatCenter[p];
                Vector2 tn = _pocketThroatNormal[p];
                float s = (c.x - tc.x) * tn.x + (c.y - tc.y) * tn.y;
                if (s > 0f)
                {
                    PocketProximityEvents++;
                    _balls[index].velocity = Vector2.zero;
                    _frozen[index] = true;
                    return;
                }
            }
        }

        // ── 几何/向量小工具（开方走 PoolMath 单一口径）─────────────────────

        private static Vector2 ClosestPointOnSegment(Vector2 q, Vector2 a, Vector2 b)
        {
            float abx = b.x - a.x;
            float aby = b.y - a.y;
            float len2 = abx * abx + aby * aby;
            if (len2 <= 1e-12f)
            {
                return a;
            }

            float u = ((q.x - a.x) * abx + (q.y - a.y) * aby) / len2;
            if (u < 0f)
            {
                u = 0f;
            }
            else if (u > 1f)
            {
                u = 1f;
            }

            return new Vector2(a.x + u * abx, a.y + u * aby);
        }

        private static float Dot(Vector2 a, Vector2 b)
        {
            return a.x * b.x + a.y * b.y;
        }

        private static float Length(Vector2 v)
        {
            return PoolMath.Sqrt(v.x * v.x + v.y * v.y);
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
    }
}
