using System;
using System.Collections.Generic;
using Billiards.Core;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 自研二维定长步进台球物理内核（球-库边 + **球-球** + 袋口捕获 / 擦袋角分类 + 越界冻结 + 运动终止）。
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
        /// **R-C 缺陷的幅度（这是"为什么需要两个即停判据"的依据，仍然有效）**：
        /// 球楔死在凹袋角、以 nm 级间隙反复微接触时，原实现（精确零判速度停止）实测触发
        /// `ToiIterationGuardTrips = 1810`（326/1230 次发射）⇒ 故引入
        /// <see cref="StoppedSpeedEpsilon"/>（近零速度即停）与"连续两次零时间推进即停"。
        ///
        /// **⚠ 迭代预算的依据已随事件循环的作用域变更而作废、并已重写（R-L）**：
        /// 本常量原为"**每球每子步**"的迭代上限；引入球-球后事件循环提升为"**每子步全局**"
        /// （见 <see cref="AdvanceSubStep"/>）⇒ 统计口径变了。
        /// **上一版此处曾写"`16 → 128` 逐档实测（`1810 → 1196 → 339 → 3 → 0`）⇒ 0"作为 128 的取值依据 ——
        /// 那串数据是在"每球每子步"口径下测得的，在新口径下不再适用，故已从此处移除**
        ///（历史记录保留在 `Docs/35-A_Day4_物理内核I_spec.md` / `…_执行记录.md`，并已加勘误块）。
        ///
        /// **现行依据（新口径下自证）**：**"每子步全局"的迭代峰值**由
        /// <see cref="BallBallMaxIterationsObserved"/> 实测，并由判据断言其落在 `[1, 0.5 × MaxToiIterations]`
        /// ⇒ 即要求**余量 ≥ 2×**（实测峰值远低于该上界；余量结论**不适用于 16 球密集场景**）。
        /// **取值本身仍为 128（未改）**；`0.5 × 128 = 64` 是**判据的上界阈值**，不是常量被改过。
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
        /// 这是**前瞻声明值**：用于给滚阻的**滑动段**减速度 <c>a_slide</c> 留出子步数余量
        /// （出厂口径下 <c>a_slide</c> 远小于该上界），不参与实际的滚动积分。
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

        /// <summary>
        /// **游戏级静止**速度阈值，m/s。与 <see cref="StoppedSpeedEpsilon"/>（数值终止，1e-6 m/s）**分层**：
        /// 后者只让事件循环与积分退出（避免 TOI 空转），前者用于"**本杆结束**"的宏观静止声明
        /// （<see cref="TableAtRest"/>）。滚动阻力为 0 时，速度落在 `(1e-6, 1e-3]` 的球会"永远爬行"，
        /// 故游戏级阈值必须高于数值阈值。
        ///
        /// 取值依据：`1e-3 m/s` ⇒ 1 秒位移 1 mm，低于视觉可辨阈值。**系数为选定值**（乙-系数选定），
        /// 由物理调参阶段复核。本类**不因静止而置零速度**（<see cref="TableAtRest"/> 只作声明）。
        /// </summary>
        public const float RestSpeedThreshold = 1e-3f;

        /// <summary>
        /// 宏观静止需**连续**满足 <see cref="RestSpeedThreshold"/> 的定长步数（乙-系数选定）。
        /// 取 8 ⇒ 约 66 ms @120 Hz；用"连续帧数"而非单帧，避免把瞬时慢速误判为停止。
        /// </summary>
        public const int RestStableFrames = 8;

        // ── 只读几何缓存（构造时摊平，顺序 = 资源顺序）──────────────────────

        private readonly Vector2[] _segA;
        private readonly Vector2[] _segB;
        private readonly Vector2[] _pocketThroatCenter;
        private readonly Vector2[] _pocketThroatNormal;

        /// <summary>
        /// **逐袋**的捕获半径（米），来自 <c>TableConfig.Pocket.captureRadius</c>。
        /// 语义 = "开口内**容一球**"的**横向（面内）偏移上限** = `openingRadius − BallRadius − 0.002`。
        /// **必须用逐袋值**：6 袋的 `captureRadius` 在 1e-8 量级互不相同，
        /// 用 `PoolConstants.PocketCaptureRadius`（单值代表）做判定会使逐袋边界用例不可预期。
        /// 构造时按可选全局乘子 <c>pocketCaptureRadiusScale</c>（默认 1.0）整体缩放 —— **真源仍是逐袋值**。
        /// </summary>
        private readonly float[] _pocketCaptureRadius;

        private readonly PhysicsParams _params;

        /// <summary>宏观静止阈值（**实例字段** ⇒ 可由 `PhysicsTuning` 资产覆盖；默认 = 同名常量）。</summary>
        private readonly float _restSpeedThreshold;

        /// <summary>宏观静止连续帧数（**实例字段** ⇒ 可由 `PhysicsTuning` 资产覆盖；默认 = 同名常量）。</summary>
        private readonly int _restStableFrames;

        /// <summary>袋口捕获半径的**全局乘子**（实例字段；默认 1 ⇒ 逐袋值不变）。</summary>
        private readonly float _pocketCaptureRadiusScale;

        private readonly float _ballRestitution;
        private readonly float _ballTangentialFriction;

        // ── 球状态与诊断 ──────────────────────────────────────────────────

        private BallState[] _balls = new BallState[0];
        private bool[] _frozen = new bool[0];

        /// <summary>逐球记录"落入哪个袋"；未捕获为 <c>-1</c>（供判据断言"入对袋"）。</summary>
        private int[] _capturedPocket = new int[0];

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

        /// <summary>
        /// **球-球位置修正真的把两球推开了**的次数 —— 即"**位置修正是否被走到**"的**可观测量**。
        ///
        /// 为什么需要它：此前位置修正**零覆盖**（`ResolveDeepOverlaps` 里的一处重复实现总先把它抹平），
        /// 而"是否走到了"只能靠间接推断。把它变成计数器后，判据可以直接断言它 ≥1。
        /// **注意**：本计数只说明"走到了"，**"推得对不对"由结果断言守**
        /// （分开后间距须 == `2r`）—— 两者合起来才是双边约束。
        /// </summary>
        public int BallBallSeparationCount { get; private set; }

        /// <summary>球心越过袋喉平面的次数（每次随即冻结该球）。</summary>
        public int PocketProximityEvents { get; private set; }

        /// <summary>
        /// **正常落袋**次数：球心越过袋喉平面（`s &gt; 0`）**且**面内横向偏移
        /// `ρ = |dot(C − tc, td)| ≤ 该袋 captureRadius`。
        /// 由 <see cref="ResetDiagnostics"/> 归零。
        ///
        /// **⚠ 与 <see cref="PocketProximityEvents"/>、<see cref="PocketJawCrossingCount"/> 的关系**：
        /// 三者**必须各自独立累加**（在各自分支里 `++`），恒满足
        /// `PocketProximityEvents == CapturedBallCount + PocketJawCrossingCount` ——
        /// 该等式是**待判据验证的结论**，**不得**在实现里用"一变量 = 另两者之和"来省略独立计数
        /// （否则"归类完备"判据恒真、对"设计本身错"完全盲）。
        /// </summary>
        public int CapturedBallCount { get; private set; }

        /// <summary>
        /// **擦袋角越界**次数：球心越过袋喉平面但面内横向偏移 **超出**该袋 `captureRadius`。
        /// 这类球**不再逃逸**（一律冻结，保持"越喉平面即离台"不变量），但其归属
        /// （落袋 / 弹回）由后续裁定；本计数是该窗口的可观测量。
        /// </summary>
        public int PocketJawCrossingCount { get; private set; }

        /// <summary>
        /// **宏观静止**：全部未冻结球已**连续** <see cref="RestStableFrames"/> 步速度 ≤ <see cref="RestSpeedThreshold"/>。
        /// 游戏级"本杆结束"信号；**本类不因此置零速度**。
        /// </summary>
        public bool TableAtRest { get; private set; }

        /// <summary>当前"全员静止"的连续帧数（诊断）：任一未冻结球超过阈值即清零。</summary>
        public int ConsecutiveRestFrameCount { get; private set; }

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

        /// <summary>
        /// 本内核**实际生效**的参数（值语义副本）。
        /// 用途：判据可断言"调用点是否真的传了**安全值**"——只断言调用点源码不够，
        /// 因为 `default(PhysicsParams)` 恒为全零且能一路静默传到物理内核里。
        /// </summary>
        public PhysicsParams Params => _params;

        /// <summary>**生效的**宏观静止速度阈值（实例值；供守卫比对资产，属"不可观测参数"的只读出口）。</summary>
        public float RestSpeedThresholdValue => _restSpeedThreshold;

        /// <summary>**生效的**宏观静止连续帧数（实例值；只读出口）。</summary>
        public int RestStableFramesValue => _restStableFrames;

        /// <summary>**生效的**袋口捕获半径全局乘子（实例值；只读出口）。</summary>
        public float PocketCaptureRadiusScaleValue => _pocketCaptureRadiusScale;

        // ── 构造 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 用球桌碰撞几何与物理参数构造内核。构造时把 <c>TableConfig.segments</c>
        /// 摊平为数组，**保持资源/JSON 原始顺序**（顺序参与确定性，不得重排）。
        /// </summary>
        public PoolSimulator(TableConfig table, PhysicsParams parameters, float pocketCaptureRadiusScale = 1f,
            float restSpeedThreshold = RestSpeedThreshold, int restStableFrames = RestStableFrames)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            _params = parameters;
            _restSpeedThreshold = restSpeedThreshold;
            _restStableFrames = restStableFrames;
            _pocketCaptureRadiusScale = pocketCaptureRadiusScale;
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
            _pocketCaptureRadius = new float[pocketCount];
            for (int i = 0; i < pocketCount; i++)
            {
                Pocket p = table.pockets[i];
                _pocketThroatCenter[i] = p.throatCenter;
                _pocketThroatNormal[i] = p.throatNormal;
                _pocketCaptureRadius[i] = p.captureRadius * pocketCaptureRadiusScale;
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

        /// <summary>
        /// **初始摆放校验（校验期单点实现）**：任意两球间距须 ≥ `2r − BallInitialOverlapTolerance`。
        /// 合法摆放可落在**边界**上（与"贴库静止球"同族），故用的是**容差**而非严格不等。
        /// 无违规返回 <c>null</c>；否则返回首个违规的可读描述。
        ///
        /// 由 `PhysicsValidator` 的球-球检查项与 EditMode 判据**共用**（同一不变量单点实现）。
        /// </summary>
        public static string ValidateInitialPlacement(IReadOnlyList<Vector2> positions)
        {
            if (positions == null)
            {
                return null;
            }

            float minAllowed = 2f * PoolConstants.BallRadius - BallInitialOverlapTolerance;
            for (int i = 0; i < positions.Count; i++)
            {
                for (int k = i + 1; k < positions.Count; k++)
                {
                    float d = Length(positions[i] - positions[k]);
                    if (d < minAllowed)
                    {
                        return $"球 {i} 与球 {k} 间距 {d:F9} m < 下限 {minAllowed:F9} m（重叠摆放）";
                    }
                }
            }

            return null;
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
                _capturedPocket = new int[0];
                return;
            }

            _balls = new BallState[balls.Count];
            _frozen = new bool[balls.Count];
            _vAvg = new Vector2[balls.Count];
            _active = new bool[balls.Count];
            _capturedPocket = new int[balls.Count];
            for (int i = 0; i < balls.Count; i++)
            {
                _balls[i] = balls[i];
                _frozen[i] = false;
                _capturedPocket[i] = -1;
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
            BallBallSeparationCount = 0;
            CapturedBallCount = 0;
            PocketJawCrossingCount = 0;
            TableAtRest = false;
            ConsecutiveRestFrameCount = 0;

            for (int i = 0; i < _capturedPocket.Length; i++)
            {
                _capturedPocket[i] = -1;
            }
        }

        /// <summary>
        /// 该球**落入哪个袋**（袋索引）；未捕获返回 <c>-1</c>。越界索引亦返回 <c>-1</c>。
        /// </summary>
        public int CapturedPocketIndex(int ballIndex)
        {
            if (ballIndex < 0 || ballIndex >= _capturedPocket.Length)
            {
                return -1;
            }

            return _capturedPocket[ballIndex];
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

            UpdateTermination();
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
                    ? AverageVelocity(_balls[i].velocity, DecelerationAt(_balls[i].velocity), dt)
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
                        ? AverageVelocity(_balls[i].velocity, DecelerationAt(_balls[i].velocity), remaining)
                        : Vector2.zero;
                }
            }

            // 门控：**两段任一非零即生效**（`||`，**不是** `&&`）。
            // ⚠ 历史坑：原门控是"单一滚阻 > 0"；两段都取 0 时**整段跳过**（与历史"无滚阻"逐位一致）。
            // ⚠ 若误写成 `&&`，"只有一段非零"时滚阻完全不生效（整桌永不静止）—— `T59` 专门钉这条。
            if (_params.rollingDecelerationSlide > 0f || _params.rollingDecelerationRoll > 0f)
            {
                for (int i = 0; i < _balls.Length; i++)
                {
                    if (!_frozen[i])
                    {
                        ApplyRollingDeceleration(ref _balls[i], DecelerationAt(_balls[i].velocity), dt);
                    }
                }
            }

            for (int i = 0; i < _balls.Length; i++)
            {
                CheckPocketProximity(i);
            }
        }

        /// <summary>
        /// 台呢减速度的**唯一实现点**（两段模型）：<c>a(v) = a_roll + (a_slide − a_roll)·clamp01(v / v_transition)</c>。
        ///
        /// 边界（显式定义）：
        ///  · <c>v_transition ≤ 0</c> ⇒ 取 <c>a_slide</c>（不分区）；
        ///  · <c>speed ≤ 0</c> ⇒ 取 <c>a_roll</c>。
        /// ⇒ <c>a_slide == a_roll</c> 时两条边界返回**同值**，故退化档**逐位等价**于历史的单一恒定减速度。
        ///
        /// 返回前取 <c>Max(0, ·)</c>：**不得造能**（负的滚动段会加速球）。
        /// 本函数是**当前速度的纯函数**（无历史 / 无时间 / 无随机）⇒ 不破坏逐位确定性。
        /// </summary>
        private float DecelerationAt(Vector2 v)
        {
            float aSlide = _params.rollingDecelerationSlide;
            float aRoll = _params.rollingDecelerationRoll;
            float vTransition = _params.rollingTransitionSpeed;

            float a;
            if (vTransition <= 0f)
            {
                a = aSlide;
            }
            else
            {
                float speed = Length(v);
                if (speed <= 0f)
                {
                    a = aRoll;
                }
                else
                {
                    float t = speed / vTransition;
                    if (t > 1f)
                    {
                        t = 1f;
                    }

                    // ⚠ 必须写成 `a_roll + (a_slide − a_roll) * t`：退化时 `(a_slide − a_roll)` 恰为 `+0`
                    //   ⇒ `a_roll + 0*t ≡ a_roll`（IEEE 精确）⇒ 与历史恒定档**逐位一致**。
                    a = aRoll + (aSlide - aRoll) * t;
                }
            }

            return Mathf.Max(0f, a);
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

            Vector2 vi = _balls[i].velocity;
            Vector2 vk = _balls[k].velocity;

            Vector2 rel = vi - vk;
            float vn = Dot(rel, n);

            // ⚠ **计数必须反映"真实解算"**：只在"确实施加了冲量"或"确实做了位置分离"时才计。
            // 否则"保留计数、停止解算"的注入会因计数虚增而**全部通过**（计数与动量交换解耦）。
            // **准确性边界（勿过度解读）**：本修复**只**消除了**原来的"入口无条件计数"注入点**（注入点漂移）；
            // 计数仍可被**人为虚增注入**（例如在早退前手工 `count++`）⇒ 那由 T20/T23/T24a/T26 的**上界**判据承担。
            // **本处不声称**"计数已免疫虚增"。
            if (vn >= 0f)
            {
                // 已在分离 ⇒ 不产生冲量；只做位置修正（深层重叠/球心重合路径会走到这里）。
                if (SeparatePair(i, k, n))
                {
                    BallBallContactCount++;
                    TotalContactCount++;
                }

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

            // 冲量已施加 ⇒ 这是一次真实解算（与上面的"只分离不施冲量"情形区分开）。
            BallBallContactCount++;
            TotalContactCount++;

            float energyAfter = vi.x * vi.x + vi.y * vi.y + vk.x * vk.x + vk.y * vk.y;
            if (energyAfter > energyBefore * (1f + 1e-6f))
            {
                EnergyGainViolations++;
            }
        }

        /// <summary>
        /// 位置修正：把重叠的两球沿 <c>n̂</c> **各推一半**，使间距 = <c>2r</c>。
        /// **本函数是"把重叠推开"这条不变量在本类内的单点实现**（另一处曾有的"手动贴到相切"
        /// 已删除 —— 那是重复实现，且会让本函数永不被走到）。
        /// 修正量只依赖 `(n̂, 重叠量, <see cref="BallBallSeparationShare"/>)` ⇒ **不依赖访问顺序**。
        /// 返回**是否真的发生了分离**（供调用方决定是否计入"真实解算"）。
        /// </summary>
        private bool SeparatePair(int i, int k, Vector2 n)
        {
            Vector2 pi = _balls[i].position;
            Vector2 pk = _balls[k].position;

            float dist = Length(pi - pk);
            float overlap = 2f * PoolConstants.BallRadius - dist;
            if (overlap <= 0f)
            {
                return false;
            }

            float push = overlap * BallBallSeparationShare;
            _balls[i].position = pi + push * n;
            _balls[k].position = pk - push * n;
            BallBallSeparationCount++;
            return true;
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

                // ⚠ **不在此处手动"贴到相切"**：那与 `SeparatePair` **在实现同一不变量**
                //（把重叠推开）—— 属重复实现，且会让 `SeparatePair` 永远走不到（零覆盖）。
                // 改为直接把球交给解算路径：法向由现场几何求得，**位置修正由 `SeparatePair` 单点完成**。
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

        // ── 袋口捕获与越界冻结（单点实现）─────────────────────────────────

        /// <summary>
        /// **袋口判定单点实现**：越界、正常落袋、擦袋角越界三者都在此一处求值与分类。
        ///
        /// 判据（规范二维物理平面，米；`tc/tn` 取自 <c>TableConfig.Pocket</c> 的逐袋真源）：
        ///  · 越界量   `s = dot(C − tc, tn)`（`tn` 指向网内）；
        ///  · 横向偏移 `ρ = |dot(C − tc, td)|`，`td = (−tn.y, tn.x)` —— **面内**偏移、**不含法向分量**。
        ///
        /// 分类：`s &gt; 0`（球心穿过袋喉、朝网方向）为**必要**条件；在此之上
        /// `ρ ≤ 该袋 captureRadius` ⇒ **正常落袋**（<see cref="CapturedBallCount"/>）；
        /// 否则 ⇒ **擦袋角越界**（<see cref="PocketJawCrossingCount"/>）。
        /// 三计数**各自独立累加**，恒满足
        /// `PocketProximityEvents == CapturedBallCount + PocketJawCrossingCount`（待判据验证的结论）。
        ///
        /// **不变量**：只要 `s &gt; 0`（**不论是否落袋**）⇒ 该球**离台**（速度置零、停止积分）。
        /// 这是"球不得静默离台"的唯一保证。
        ///
        /// **为什么用 ρ 而非 |C − tc|**：判据在**子步末**求值，此时球可能已越界 `v·dt_sub`
        /// （8 m/s ⇒ 22.2 mm）。`ρ` 沿法向直行时恒定 ⇒ 鲁棒；`|C − tc|` 含法向分量 ⇒ 越界越多值越大
        /// ⇒ 正中进袋的球会在越界那一子步被判"未捕获"（假阴性）。
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
                if (s <= 0f)
                {
                    continue;
                }

                // 袋喉切向（面内、单位）：td = (−tn.y, tn.x)。
                float tdx = -tn.y;
                float tdy = tn.x;
                float rho = Mathf.Abs((c.x - tc.x) * tdx + (c.y - tc.y) * tdy);

                // ★ 三计数各自独立累加（不得由另两者相加得到 —— 否则"归类完备"判据恒真）。
                PocketProximityEvents++;
                if (rho <= _pocketCaptureRadius[p])
                {
                    CapturedBallCount++;
                    _capturedPocket[index] = p;
                }
                else
                {
                    PocketJawCrossingCount++;
                }

                // 不变量：越喉平面即离台（不论分类）。
                _balls[index].velocity = Vector2.zero;
                _frozen[index] = true;
                return;
            }
        }

        // ── 运动终止（游戏级静止声明）─────────────────────────────────────

        /// <summary>
        /// 每 <see cref="Step"/> 末求值一次：全部未冻结球速度 ≤ <see cref="RestSpeedThreshold"/> 时
        /// 累加"连续静止帧数"，任一球超过阈值即清零；达到 <see cref="RestStableFrames"/> 即声明
        /// <see cref="TableAtRest"/>。**不置零速度**（避免改变物理）—— 数值终止由
        /// <see cref="StoppedSpeedEpsilon"/> 承担，两者**分层**、不得互相代替。
        /// </summary>
        private void UpdateTermination()
        {
            bool allAtRest = true;
            for (int i = 0; i < _balls.Length; i++)
            {
                if (_frozen[i])
                {
                    continue;
                }

                if (Length(_balls[i].velocity) > _restSpeedThreshold)
                {
                    allAtRest = false;
                    break;
                }
            }

            ConsecutiveRestFrameCount = allAtRest ? ConsecutiveRestFrameCount + 1 : 0;
            TableAtRest = ConsecutiveRestFrameCount >= _restStableFrames;
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
