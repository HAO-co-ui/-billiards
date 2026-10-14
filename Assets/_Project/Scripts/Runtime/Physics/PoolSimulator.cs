using System;
using System.Collections.Generic;
using Billiards.Core;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 自研二维定长步进台球物理内核（球-库边 + **球-球** + 袋喉越界冻结）。
    ///
    /// 设计要点（每条都对应一个可失败的判别测试）：
    ///  1. **定长步进**：外部只调 <see cref="Step"/>，内部固定 1/120 s，再细分为
    ///     <see cref="SubSteps"/> 个子步（子步数由 <see cref="DeriveSubSteps"/> **推导**，不是硬编码）。
    ///  2. **端点精确积分**：子步内位置按**梯形/中点均速**推进（<c>v_avg = v − ½·a·Δt·v̂</c>），
    ///     而非前向欧拉 <c>p += v·Δt</c>；TOI 与位置推进共用同一 <c>v_avg</c>。
    ///  3. **全局最早事件**：每个子步内，**段接触**与**球-球接触**一起参与"最早接触时刻"竞争；
    ///     同刻事件（|Δt| ≤ <see cref="ToiTimeEpsilon"/>）归并为一组统一解算；
    ///     **所有活跃球**都推进到该时刻（否则时间轴在球之间错位）。
    ///     ⇒ 球与球之间**不会互相穿过**。
    ///  4. **接触解算**：法向与切向**各自独立赋值**。段接触的法向由 <c>normalize(C − Q)</c> 在接触点
    ///     现场求得（**不需要逐段朝向数据**）；球-球接触由 <c>normalize(p_i − p_k)</c> 求得。
    ///  5. **确定性**：全部状态推进按固定顺序（子步 → **球索引升序** → 段索引升序；配对 **i&lt;j 升序**），
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
        /// 单子步内 TOI 迭代次数上限。超过即计 <see cref="ToiIterationGuardTrips"/> 并截断该子步（不静默）。
        ///
        /// **R-C 修复后迭代预算由 16 提升为 128**：原 16 在"球楔死在凹袋角、
        /// 以 nm 级间隙反复微接触"时不足（实测 `ToiIterationGuardTrips = 1810`，326/1230 次发射）。
        /// 配合 <see cref="StoppedSpeedEpsilon"/> 与"零时间推进即停"，实测 `guardTrips` 变化：
        /// `1810（原）→ 1196（仅近零速度）→ 339（+零时间推进即停）→ 3（预算 64）→ 0（预算 128）`，
        /// 且 `EnergyGainViolations = 0`、最大嵌入 6.147e-08 m、全量测试全绿。
        /// 128 仍是**有限**安全网（配合两个即停判据，循环不会无进展空转），仅覆盖"真实多次接触链"。
        ///
        /// **⚠ R-L 的语义变更（引入球-球之后）**：本常量原为"**每球每子步**"的迭代上限；
        /// 引入球-球后事件循环提升为"**每子步全局**" ⇒ 迭代上限的**统计口径**变了
        /// ⇒ 上段那串上游实测依据是在"每球每子步"口径下测得的，**在"每子步全局"口径下不再直接适用**。
        /// 现由 <see cref="BallBallMaxIterationsObserved"/> **重新实测并核验余量**
        /// （实测值见该属性；余量结论**不适用于 16 球密集场景**）。
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

        /// <summary>推导子步数时对"比值恰为整数"的浮点舍入保护。见 <see cref="DeriveSubSteps"/>。</summary>
        private const float SubStepCeilEpsilon = 1e-5f;

        /// <summary>
        /// 视为"已停止"的速度阈值，m/s。**R-C 修复（实测依据见下）**。
        ///
        /// 依据实测：球被两侧袋角段同时接触时，接触解算把速度按 `e^{2k}` **几何衰减**
        /// （实测 `speedAfterResolve` 4.969e-9 → 4.025e-9 → …，逐次 ×0.81 = e²），
        /// **永不为精确 0**。原实现用**精确零**比较判停止 ⇒ 永不触发 ⇒ 同一批 `t = 0` 接触被反复重解
        /// 直至耗尽 <see cref="MaxToiIterations"/>（实测 `ToiIterationGuardTrips = 1810`）。
        ///
        /// `1e-6 m/s`（= 1 µm/s，1 秒位移 1 µm，可忽略）**远高于**该残差（~5e-9）、
        /// **远低于**任何玩法相关速度 ⇒ 用近零阈值取代精确零，既消除空转又不改变可观测物理。
        /// </summary>
        public const float StoppedSpeedEpsilon = 1e-6f;

        /// <summary>
        /// 合法初始摆放的**两球间距下限容差**（米）：要求 `|Δp| ≥ 2r − 该值`。
        /// 由**校验期**使用（拒绝重叠摆放），**不参与积分** ⇒ 不是运行时物理参数。
        /// </summary>
        public const float BallInitialOverlapTolerance = 1e-4f;

        /// <summary>
        /// 球-球重叠分离时**两侧各推的份额**。取 0.5/0.5 ⇒ 以质心对称分离，
        /// 且分离量只依赖 `(n̂, 重叠量, 0.5)`、**不依赖访问顺序** ⇒ 不破坏确定性。
        /// </summary>
        public const float BallBallSeparationShare = 0.5f;

        // ── 只读几何缓存（构造时摊平，顺序 = 资源顺序）──────────────────────

        private readonly Vector2[] _segA;
        private readonly Vector2[] _segB;
        private readonly Vector2[] _pocketThroatCenter;
        private readonly Vector2[] _pocketThroatNormal;

        private readonly PhysicsParams _params;

        private readonly float _ballRestitution;
        private readonly float _ballTangentialFriction;

        // ── 球状态与诊断 ──────────────────────────────────────────────────

        private BallState[] _balls = new BallState[0];
        private bool[] _frozen = new bool[0];

        /// <summary>子步内每球的均速缓存（与 TOI 共用同一 <c>v_avg</c>）。</summary>
        private Vector2[] _vAvg = new Vector2[0];

        /// <summary>子步内该球是否仍在参与事件循环（低于近零速度即退出）。</summary>
        private bool[] _active = new bool[0];

        // 段接触组：必须**同时记录"哪个球"与"哪个段"** —— 只记段索引会把该段解算到全部球上（越界反射）。
        private readonly List<int> _segBall = new List<int>(8);
        private readonly List<int> _segIndex = new List<int>(8);

        // 球-球接触组（同刻归并）。
        private readonly List<int> _pairFirst = new List<int>(8);
        private readonly List<int> _pairSecond = new List<int>(8);

        // 候选对缓冲（宽相位产出的复用缓冲，避免每次分配）。
        private readonly List<int> _pairBufferFirst = new List<int>(64);
        private readonly List<int> _pairBufferSecond = new List<int>(64);

        /// <summary>全部接触解算次数（段接触 + 球-球接触），诊断量。</summary>
        public int TotalContactCount { get; private set; }

        /// <summary>TOI 循环超过 <see cref="MaxToiIterations"/> 的次数（每次意味着该子步被截断）。</summary>
        public int ToiIterationGuardTrips { get; private set; }

        /// <summary>接触后速度模长增大的次数（应为 0；非 0 表示接触解算引入了能量增益）。</summary>
        public int EnergyGainViolations { get; private set; }

        /// <summary>球心越过袋喉平面的次数（每次随即冻结该球）。</summary>
        public int PocketProximityEvents { get; private set; }

        /// <summary>
        /// **球-球接触的解算次数（对 × 次）**：同一个球对在一次同刻事件内**只计 1**；
        /// 由 <see cref="ResetDiagnostics"/> 归零。
        ///
        /// 该定义**必须写死**：多球链用例的"至少两次接触"依赖"**按对计数**"；
        /// 若改成"按事件计数"，一次同刻事件里 A-B 与 B-C 会合并成 1 ⇒ 通过/失败不可预期。
        /// </summary>
        public int BallBallContactCount { get; private set; }

        /// <summary>
        /// **每个定长步**（一次 <see cref="Step"/>）内、**所有子步的全局事件循环迭代数的最大值**。
        /// 由 <see cref="ResetDiagnostics"/> 归零；测试读的是该步之后的值。
        ///
        /// 用途：`MaxToiIterations` 的语义在引入球-球后由"每球每子步"变为"每子步全局"
        /// （见该常量注释），故**必须实测峰值并核验余量** —— "恰好没过 128"与"远低于 128"是两件事。
        /// ⚠ 下界 `≥ 1` 只说明"事件循环至少跑过一轮"，而**球-库边事件也会让它跑** ⇒
        /// 它**不证明球-球事件发生**（后者由 <see cref="BallBallContactCount"/> 承担）。
        /// </summary>
        public int BallBallMaxIterationsObserved { get; private set; }

        /// <summary>库边/袋角/弧段总数（= <c>TableConfig.SegmentCount</c>）。</summary>
        public int SegmentCount => _segA.Length;

        /// <summary>子步数 = <see cref="DeriveSubSteps"/>(<see cref="PoolConstants.MaxBallSpeed"/>)。</summary>
        public int SubSteps { get; }

        /// <summary>子步时间步长 = <see cref="FixedDeltaTime"/> / <see cref="SubSteps"/>。</summary>
        public float SubStepDeltaTime { get; }

        /// <summary>
        /// **仅测试用**。为 true 时把"逐步 TOI 接触检测"替换为"子步末采样检测"，
        /// 但**复用同一条生产推进路径**（同一 <c>v_avg</c> 积分、同一解算、同一子步末减速与袋喉检查）。
        /// ⚠ 段接触**没有位置修正**（只改速度）⇒ 子步末采样时球会**停留在嵌入状态**，
        /// 故该模式可被用来测量"嵌入量"。
        /// </summary>
        public bool DisableToiForDiagnosticsOnly { get; set; }

        /// <summary>
        /// **仅测试用**。为 true 时**完全不做球-球的检测与解算**（纯穿透）。
        ///
        /// ⚠ **与 <see cref="DisableToiForDiagnosticsOnly"/> 的语义差别（刻意，须记）**：
        /// 球-球解算**带位置修正** ⇒ 若该模式仍解算，重叠会被当场抹掉、**测不出穿透量**。
        /// 故本开关**彻底关闭**球-球检测/解算，使"无球-球 TOI 时会重叠多少"可被观测。
        /// 段接触没有位置修正，故段侧不需要这样处理。
        /// </summary>
        public bool DisableBallBallToiForDiagnosticsOnly { get; set; }

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
            _ballRestitution = parameters.ballRestitution;
            _ballTangentialFriction = parameters.ballTangentialFriction;

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
        /// <item>A 离散回退界：<c>v_max·(dt/n) ≤ ½·BallDiameter</c> —— 一个子步内的位移不超过半个球直径。</item>
        /// <item>B 接触弦-弧误差界：<c>½·a_max·(dt/n)² ≤ ContactArcErrorBound</c> —— 弦高误差不超过 5e-5 m。</item>
        /// </list>
        /// 当前 <c>MaxBallSpeed = 8</c> ⇒ A 得 3、B 得 2 ⇒ n = 3。
        ///
        /// 采用 **ceil 语义**（比值恰为整数时取该整数）。为避免浮点尾差把"恰为整数"抬成整数 + 1，
        /// 在取 ceil 前减去极小量 <see cref="SubStepCeilEpsilon"/>；这不是把 <c>≤</c> 换成 <c>&lt;</c>，
        /// 而是对边界做浮点保护。
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
                _vAvg = new Vector2[0];
                _active = new bool[0];
                return;
            }

            _balls = new BallState[balls.Count];
            _frozen = new bool[balls.Count];
            _vAvg = new Vector2[balls.Count];
            _active = new bool[balls.Count];
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
            BallBallContactCount = 0;
            BallBallMaxIterationsObserved = 0;
        }

        /// <summary>
        /// 推进固定步长 <see cref="FixedDeltaTime"/>。推进顺序为
        /// **子步（升序）→ 球索引（升序）**，保证逐帧可复现。
        /// </summary>
        public void Step()
        {
            for (int sub = 0; sub < SubSteps; sub++)
            {
                AdvanceSubStep(SubStepDeltaTime);
            }
        }

        // ── 子步：全局最早事件循环 ────────────────────────────────────────

        private void AdvanceSubStep(float dt)
        {
            // 子步开始时先处置已存在的"深层重叠"（球-球 与 球-段 共用同一套处置，§2.6）。
            ResolveDeepOverlaps();

            for (int i = 0; i < _balls.Length; i++)
            {
                _active[i] = !_frozen[i] && Length(_balls[i].velocity) > StoppedSpeedEpsilon;
                _vAvg[i] = _active[i]
                    ? AverageVelocity(_balls[i].velocity, _params.rollingDeceleration, dt)
                    : Vector2.zero;
            }

            float remaining = dt;
            int iterations = 0;
            bool previousIterationWasZeroTime = false;

            while (remaining > ToiRemainingEpsilon)
            {
                if (++iterations > MaxToiIterations)
                {
                    ToiIterationGuardTrips++;
                    break;
                }

                if (iterations > BallBallMaxIterationsObserved)
                {
                    BallBallMaxIterationsObserved = iterations;
                }

                if (DisableToiForDiagnosticsOnly)
                {
                    // 诊断负对照：不做逐步 TOI，只在子步末采样接触（段 + 球-球）。
                    // 其余（位置推进 / 解算 / 袋喉）与生产路径完全一致。
                    for (int i = 0; i < _balls.Length; i++)
                    {
                        if (_active[i])
                        {
                            _balls[i].position += _vAvg[i] * remaining;
                        }
                    }

                    CollectSubstepEndContacts();
                    ResolveContactGroup();
                    remaining = 0f;
                    break;
                }

                _segBall.Clear();
                _segIndex.Clear();
                _pairFirst.Clear();
                _pairSecond.Clear();

                float earliest = EarliestSegmentToi(remaining);
                if (!DisableBallBallToiForDiagnosticsOnly)
                {
                    earliest = Mathf.Min(earliest, EarliestBallBallToi(remaining));
                }

                if (_segBall.Count == 0 && _pairFirst.Count == 0)
                {
                    for (int i = 0; i < _balls.Length; i++)
                    {
                        if (_active[i])
                        {
                            _balls[i].position += _vAvg[i] * remaining;
                        }
                    }

                    break;
                }

                // ★ 精确推进到接触时刻（与 TOI 共用同一 v_avg）。**所有活跃球**都要前进。
                for (int i = 0; i < _balls.Length; i++)
                {
                    if (_active[i])
                    {
                        _balls[i].position += _vAvg[i] * earliest;
                    }
                }

                remaining -= earliest;

                ResolveContactGroup();

                for (int i = 0; i < _balls.Length; i++)
                {
                    if (_active[i] && Length(_balls[i].velocity) <= StoppedSpeedEpsilon)
                    {
                        _balls[i].velocity = Vector2.zero;
                        _active[i] = false;
                    }
                }

                // ★ R-C 修复（第二半）：**连续两次"零时间推进"** ⇒ 已无自由飞行时间，结束该子步。
                bool zeroTimeAdvance = earliest <= ToiTimeEpsilon;
                if (zeroTimeAdvance && previousIterationWasZeroTime)
                {
                    break;
                }

                previousIterationWasZeroTime = zeroTimeAdvance;

                for (int i = 0; i < _balls.Length; i++)
                {
                    _vAvg[i] = _active[i]
                        ? AverageVelocity(_balls[i].velocity, _params.rollingDeceleration, remaining)
                        : Vector2.zero;
                }
            }

            if (_params.rollingDeceleration > 0f)
            {
                for (int i = 0; i < _balls.Length; i++)
                {
                    if (!_frozen[i])
                    {
                        ApplyRollingDeceleration(ref _balls[i], _params.rollingDeceleration, dt);
                    }
                }
            }

            for (int i = 0; i < _balls.Length; i++)
            {
                CheckPocketProximity(i);
            }
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

        // ── 事件搜索：球-段 ───────────────────────────────────────────────

        /// <summary>
        /// 求**全部活跃球**中最早的"球-段"接触时刻；同刻接触（|t−earliest| ≤ 归并窗口）的
        /// **(球, 段)** 对并入同组。无接触返回 <c>+∞</c>。
        /// </summary>
        private float EarliestSegmentToi(float remaining)
        {
            float earliest = float.PositiveInfinity;
            float r = PoolConstants.BallRadius;

            for (int i = 0; i < _balls.Length; i++)
            {
                if (!_active[i])
                {
                    continue;
                }

                Vector2 p = _balls[i].position;
                Vector2 v = _vAvg[i];

                for (int j = 0; j < _segA.Length; j++)
                {
                    float t = SolveToi(p, v, _segA[j], _segB[j], r);
                    if (t < 0f || t > remaining)
                    {
                        continue;
                    }

                    if (t < earliest - ToiTimeEpsilon)
                    {
                        earliest = t;
                        _segBall.Clear();
                        _segIndex.Clear();
                        _pairFirst.Clear();
                        _pairSecond.Clear();
                        _segBall.Add(i);
                        _segIndex.Add(j);
                    }
                    else if (Mathf.Abs(t - earliest) <= ToiTimeEpsilon)
                    {
                        _segBall.Add(i);
                        _segIndex.Add(j);
                    }
                }
            }

            return earliest;
        }

        /// <summary>
        /// 单段 TOI：把"动点 vs 半径 r 的线段胶囊"拆成两类解析求交 ——
        /// ① 动点 vs 线段所在直线（投影须落在段内）；② 动点 vs 两端点圆。
        /// 取较早的有效"进入"根。**`sv·s0 &lt; 0` 是"接近"判据**（与球-球同构）：
        /// 它保证**内侧正在分离的球不会被多余反射**。
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
                float nx = -aby / len;
                float ny = abx / len;

                float s0 = (p.x - a.x) * nx + (p.y - a.y) * ny;
                float sv = v.x * nx + v.y * ny;

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

        // ── 事件搜索：球-球 ───────────────────────────────────────────────

        /// <summary>求**最早**的球-球接触时刻；同刻接触的对并入同一组。无接触返回 <c>+∞</c>。</summary>
        private float EarliestBallBallToi(float remaining)
        {
            float earliest = float.PositiveInfinity;
            float r = PoolConstants.BallRadius;

            _pairBufferFirst.Clear();
            _pairBufferSecond.Clear();
            CollectCandidatePairs(_balls.Length, _pairBufferFirst, _pairBufferSecond);

            for (int c = 0; c < _pairBufferFirst.Count; c++)
            {
                int i = _pairBufferFirst[c];
                int k = _pairBufferSecond[c];
                // ⚠ 只在**两球都静止**时跳过：**静止的球必须能被撞到**
                //（否则"运动球 vs 静止球"的对会被漏掉，碰撞退化成下一子步的深层重叠处置 ⇒ 迟到且带嵌入）。
                // 注意 `_vAvg` 对非活跃球为 0 ⇒ 相对速度仍正确。
                if (!_active[i] && !_active[k])
                {
                    continue;
                }

                float t = SolveBallBallToi(
                    _balls[i].position, _vAvg[i],
                    _balls[k].position, _vAvg[k], r);

                if (t < 0f || t > remaining)
                {
                    continue;
                }

                if (t < earliest - ToiTimeEpsilon)
                {
                    earliest = t;
                    _pairFirst.Clear();
                    _pairSecond.Clear();
                    _pairFirst.Add(i);
                    _pairSecond.Add(k);
                }
                else if (Mathf.Abs(t - earliest) <= ToiTimeEpsilon)
                {
                    _pairFirst.Add(i);
                    _pairSecond.Add(k);
                }
            }

            return earliest;
        }

        /// <summary>
        /// **候选球对产生 —— 本工程唯一的宽相位边界**。
        ///
        /// 本轮实现为**双层升序暴力循环**、产出**全部 `n(n−1)/2` 对**、**不做任何距离过滤**
        /// ⇒ 产出**不依赖球位摆放**（测试的期望值因此可判定）。
        /// 后续（调用量放大时）**只替换本函数体**为网格/扫描-剪枝，**调用方与顺序语义不变**。
        /// </summary>
        public static void CollectCandidatePairs(int ballCount, List<int> first, List<int> second)
        {
            for (int i = 0; i < ballCount; i++)
            {
                for (int k = i + 1; k < ballCount; k++)
                {
                    first.Add(i);
                    second.Add(k);
                }
            }
        }

        /// <summary>
        /// 球-球 TOI（第一性原理，等半径）：
        /// <code>
        /// d = p_i − p_k （相对位置）;  w = v_i − v_k （相对速度）
        /// |d + w·t|² = (2r)²  ⇒  A·t² + B·t + C = 0
        /// A = |w|², B = 2(d·w), C = |d|² − 4r²
        /// 取较早根 t = (−B − √disc) / (2A)
        /// </code>
        /// **接受条件**：① `t ∈ [0, remaining]`；② **接近** `d·w &lt; 0`（否则已分离的对会在 t=0 自触发）；
        /// ③ 无未来根（较早根 &lt; 0）时返回 +∞（由深层重叠处置接管）。
        /// </summary>
        private static float SolveBallBallToi(Vector2 pi, Vector2 vi, Vector2 pk, Vector2 vk, float r)
        {
            float dx = pi.x - pk.x;
            float dy = pi.y - pk.y;
            float wx = vi.x - vk.x;
            float wy = vi.y - vk.y;

            float A = wx * wx + wy * wy;
            if (A <= 1e-20f)
            {
                return float.PositiveInfinity;
            }

            float dw = dx * wx + dy * wy;
            if (dw >= 0f)
            {
                return float.PositiveInfinity;   // 接近判据
            }

            float twoR = 2f * r;
            float B = 2f * dw;
            float C = dx * dx + dy * dy - twoR * twoR;

            float disc = B * B - 4f * A * C;
            if (disc < 0f)
            {
                return float.PositiveInfinity;
            }

            float sq = PoolMath.Sqrt(disc);
            float t = (-B - sq) / (2f * A);
            if (t < 0f)
            {
                return float.PositiveInfinity;
            }

            return t;
        }

        // ── 接触解算 ──────────────────────────────────────────────────────

        /// <summary>解算当前接触组（**段接触在前、球-球对在后**，组内各自**升序**，顺序固定）。</summary>
        private void ResolveContactGroup()
        {
            for (int g = 0; g < _segBall.Count; g++)
            {
                ResolveSegmentContact(_segBall[g], _segIndex[g]);
            }

            for (int c = 0; c < _pairFirst.Count; c++)
            {
                ResolveBallBallContact(_pairFirst[c], _pairSecond[c]);
            }
        }

        /// <summary>
        /// 解算一个"球-段"接触。法向由**接触点现场**求得 <c>n̂ = normalize(C − Q)</c>
        /// （Q = 段上距球心最近点），因此**不需要逐段朝向数据**：球从内侧撞上来时它恒为内法向。
        /// 法向与切向**各自独立赋值**（先 <c>v −= (1+e)·v_n·n̂</c>，再 <c>v −= (1−μ)·v_t</c>）。
        /// </summary>
        private void ResolveSegmentContact(int index, int seg)
        {
            BallState ball = _balls[index];
            float speedIn = Length(ball.velocity);

            Vector2 q = ClosestPointOnSegment(ball.position, _segA[seg], _segB[seg]);
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
            _balls[index] = ball;

            float speedOut = Length(ball.velocity);
            if (speedOut > speedIn * (1f + 1e-6f))
            {
                EnergyGainViolations++;
            }

            TotalContactCount++;
        }

        /// <summary>
        /// 解算一个"球-球"接触（**等质量**）。
        ///
        /// 法向 <c>n̂ = normalize(p_i − p_k)</c>（由 k 指向 i；与段接触同构：现场求得、无需预计算朝向）。
        /// 冲量 `j = −(1 + e_ball)·v_n / 2`（等质量 ⇒ 两球各承担一半），**等大反向**施加。
        /// 切向：`μ_ball = 1` ⇒ **切向分量不变**（无自旋模型下无切向冲量）。
        ///
        /// **能量判据按"两球之和"**：单个球的速度模长**可以合法增大**（后球撞上前球时），
        /// 故不能按单球判升能 —— 那会假阳性。
        /// </summary>
        private void ResolveBallBallContact(int i, int k)
        {
            Vector2 pi = _balls[i].position;
            Vector2 pk = _balls[k].position;

            Vector2 d = pi - pk;
            float d2 = d.x * d.x + d.y * d.y;

            Vector2 n;
            if (d2 < 1e-18f)
            {
                // 退化：球心重合 ⇒ 取相对速度反方向为法向；仍为零则取 (1,0)。
                n = Normalize(_balls[i].velocity - _balls[k].velocity);
                if (n.x == 0f && n.y == 0f)
                {
                    n = new Vector2(1f, 0f);
                }
            }
            else
            {
                n = d / PoolMath.Sqrt(d2);
            }

            // 无论是否需要冲量，本对在此刻被**解算过一次** ⇒ 计数。
            BallBallContactCount++;
            TotalContactCount++;

            Vector2 vi = _balls[i].velocity;
            Vector2 vk = _balls[k].velocity;

            Vector2 rel = vi - vk;
            float vn = Dot(rel, n);
            if (vn >= 0f)
            {
                // 已在分离 ⇒ 不产生冲量；只做位置修正（深层重叠路径会走到这里）。
                SeparatePair(i, k, n);
                return;
            }

            float energyBefore = vi.x * vi.x + vi.y * vi.y + vk.x * vk.x + vk.y * vk.y;

            float impulse = -(1f + _ballRestitution) * vn * 0.5f;

            vi += impulse * n;
            vk -= impulse * n;

            // 切向：本轮 μ_ball ≡ 1 ⇒ 切向分量不变（无自旋模型下无切向冲量）。
            // 保留该分支以显式声明"将来 μ<1 时的施加点"，且不得污染上面的法向分量。
            if (_ballTangentialFriction < 1f)
            {
                Vector2 vtI = vi - Dot(vi, n) * n;
                Vector2 vtK = vk - Dot(vk, n) * n;
                vi -= (1f - _ballTangentialFriction) * vtI;
                vk -= (1f - _ballTangentialFriction) * vtK;
            }

            _balls[i].velocity = vi;
            _balls[k].velocity = vk;

            SeparatePair(i, k, n);

            float energyAfter = vi.x * vi.x + vi.y * vi.y + vk.x * vk.x + vk.y * vk.y;
            if (energyAfter > energyBefore * (1f + 1e-6f))
            {
                EnergyGainViolations++;
            }
        }

        /// <summary>
        /// 位置修正：把重叠的两球沿 <c>n̂</c> **各推一半**，使间距 = <c>2r</c>。
        /// 修正量只依赖 `(n̂, 重叠量, <see cref="BallBallSeparationShare"/>)` ⇒ **不依赖访问顺序**。
        /// </summary>
        private void SeparatePair(int i, int k, Vector2 n)
        {
            Vector2 pi = _balls[i].position;
            Vector2 pk = _balls[k].position;

            float dist = Length(pi - pk);
            float overlap = 2f * PoolConstants.BallRadius - dist;
            if (overlap <= 0f)
            {
                return;
            }

            float push = overlap * BallBallSeparationShare;
            _balls[i].position = pi + push * n;
            _balls[k].position = pk - push * n;
        }

        // ── 深层重叠（球-球 与 球-段 共用同一套处置）──────────────────────

        /// <summary>
        /// **子步开始时**处置已存在的重叠：此时 TOI **无未来进入根**，若不处置则球会带着重叠继续飞
        /// （相当于**静默穿透**）。处置 = 位置修正（球-球另加一次解算），并计数。
        ///
        /// **球-球与球-段共用同一套处置形态** —— 球心距库段 <c>&lt; r</c> 时段 TOI 同样无进入根，
        /// 若不处置球会**静默钻入库边**（贴库球的 float 边界正是这一档）。
        /// </summary>
        private void ResolveDeepOverlaps()
        {
            float r = PoolConstants.BallRadius;

            for (int i = 0; i < _balls.Length; i++)
            {
                if (_frozen[i])
                {
                    continue;
                }

                for (int j = 0; j < _segA.Length; j++)
                {
                    Vector2 q = ClosestPointOnSegment(_balls[i].position, _segA[j], _segB[j]);
                    Vector2 diff = _balls[i].position - q;
                    float d2 = diff.x * diff.x + diff.y * diff.y;
                    if (d2 >= r * r || d2 < 1e-18f)
                    {
                        continue;
                    }

                    float dist = PoolMath.Sqrt(d2);
                    _balls[i].position = q + r * (diff / dist);
                    ResolveSegmentContact(i, j);
                }
            }

            if (DisableBallBallToiForDiagnosticsOnly)
            {
                // 诊断负对照：球-球**完全不解算** —— 否则重叠会被位置修正当场抹掉、测不出穿透量。
                return;
            }

            _pairBufferFirst.Clear();
            _pairBufferSecond.Clear();
            CollectCandidatePairs(_balls.Length, _pairBufferFirst, _pairBufferSecond);

            for (int c = 0; c < _pairBufferFirst.Count; c++)
            {
                int i = _pairBufferFirst[c];
                int k = _pairBufferSecond[c];
                if (_frozen[i] || _frozen[k])
                {
                    continue;
                }

                Vector2 delta = _balls[i].position - _balls[k].position;
                float dist = Length(delta);
                if (dist >= 2f * r || dist < 1e-9f)
                {
                    continue;
                }

                // 先把 i 放到"恰好相切"的位置 ⇒ 随后的解算/分离都在合法几何上进行。
                Vector2 n = delta / dist;
                _balls[i].position = _balls[k].position + (2f * r) * n;
                ResolveBallBallContact(i, k);
            }
        }

        /// <summary>子步末采样：收集嵌入的段接触与球-球重叠（仅诊断负对照使用）。</summary>
        private void CollectSubstepEndContacts()
        {
            float r = PoolConstants.BallRadius;

            _segBall.Clear();
            _segIndex.Clear();
            _pairFirst.Clear();
            _pairSecond.Clear();

            for (int i = 0; i < _balls.Length; i++)
            {
                if (_frozen[i])
                {
                    continue;
                }

                for (int j = 0; j < _segA.Length; j++)
                {
                    Vector2 q = ClosestPointOnSegment(_balls[i].position, _segA[j], _segB[j]);
                    float dx = _balls[i].position.x - q.x;
                    float dy = _balls[i].position.y - q.y;
                    if (dx * dx + dy * dy < r * r)
                    {
                        _segBall.Add(i);
                        _segIndex.Add(j);
                    }
                }
            }

            if (DisableBallBallToiForDiagnosticsOnly)
            {
                return;
            }

            _pairBufferFirst.Clear();
            _pairBufferSecond.Clear();
            CollectCandidatePairs(_balls.Length, _pairBufferFirst, _pairBufferSecond);

            for (int c = 0; c < _pairBufferFirst.Count; c++)
            {
                int i = _pairBufferFirst[c];
                int k = _pairBufferSecond[c];
                if (_frozen[i] || _frozen[k] || (!_active[i] && !_active[k]))
                {
                    continue;
                }

                if (Length(_balls[i].position - _balls[k].position) < 2f * r)
                {
                    _pairFirst.Add(i);
                    _pairSecond.Add(k);
                }
            }
        }

        // ── 袋喉越界冻结 ──────────────────────────────────────────────────

        /// <summary>
        /// 袋口判据用**喉平面**而非半径：球心满足 <c>dot(C − throatCenter, throatNormal) &gt; 0</c>
        /// 即视为越界，冻结该球（速度置零、停止积分）并累计 <see cref="PocketProximityEvents"/>。
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
