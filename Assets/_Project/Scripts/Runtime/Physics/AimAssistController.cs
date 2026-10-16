using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **瞄准辅助的胶水层**：读开关 → 取快照指纹 → 触发 <see cref="ShotPredictor.Predict"/> → 缓存结果。
    ///
    /// ── 三层分离（与 Day 8 的"纯状态 + 胶水"同范式）──────────────────────────
    /// · **纯核** <see cref="ShotPredictor"/>：无 `Input` / `Time` / `MonoBehaviour`；
    /// · **本类**：只做"读开关 / 判脏 / 调纯核 / 存结果"，**不写任何内核状态**；
    /// · **表现** <see cref="AimAssistView"/>：纯几何 + 绘制（过 `PlayfieldView` 单点）。
    ///
    /// ── 三条契约（可被源码 `grep` 与状态快照双重覆盖）──────────────────────────
    ///  1. **零扰动**：本类**不得**调用 `drive.Simulator` 的推进 / 装载 / 重摆 / 归零入口
    ///     （预测只读；在跑内核的扰动由 `T72` 与 `T82` 的源码守卫共同覆盖）。
    ///  2. **关闭即不算**：<see cref="RefreshPrediction"/> 首行判 `drive.PredictionEnabled` ⇒ 关闭期间
    ///     不调用 `Predict`（<see cref="PredictionComputeCount"/> 逐位不变）。
    ///  3. **开关不另存副本**：唯一读点是 `drive.PredictionEnabled`（→ `PhysicsTestDrive.enableShotPrediction`）。
    ///
    /// ── 刷新时机（脏标记 + 节流，**拖动性能**的核心）────────────────────────────
    /// 仅当 `TableAtRest` 且 `(aimDegrees, power, 在场上球的位置/速度指纹)` 变化时才重算。
    /// 指纹为**数值**（64 位哈希，见 <see cref="Fingerprint"/>）—— 旧实现每帧对 17 颗球 × 4 个 float
    /// 走 `ToString("R") + StringBuilder`，拖动时每秒数百次字符串格式化与分配。
    ///
    /// 拖动期间指纹**每帧都变** ⇒ 若不节流，就是"每帧一次 240 步 × 17 球 × 82 段"的预测（用户实测卡顿）。
    /// 本类加**前后沿节流**（<see cref="ShouldRecompute"/>）：
    /// · **前沿**：距上次重算 ≥ <see cref="minRecomputeIntervalSeconds"/> 才重算（限制拖动期间的频率）；
    /// · **尾沿（硬约束）**：当本帧指纹与**上一帧**相同（= 输入已停止变化）⇒ **无条件重算**
    ///   ⇒ **停止拖动后的下一帧必定是最新预测**，瞄准线不会停在旧值上。
    /// 预测与绘制路径的缓冲由 <see cref="ShotPredictionWorkspace"/> 复用（消除每帧分配）。
    ///
    /// ⚠ 关闭期间把脏缓存**置空**（重新开启必重算），但**不改** <see cref="Prediction"/>。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · 输入设备级触发（真鼠标/键盘）不在 EditMode 覆盖范围（Manual / PlayMode）。
    /// · 真实帧率不判（无 CI / 无固定机型）—— 拖动帧时由构建产物的冒烟模式（`-billiards-smoke-aim`）取证。
    /// </summary>
    [AddComponentMenu("Billiards/Aim Assist Controller")]
    public sealed class AimAssistController : MonoBehaviour
    {
        /// <summary>
        /// **预测步数上限**（`T83` 的乙-系数选定）。
        ///
        /// ⚠ **实测定值**：银行球（`T78` 场景）首接触实测需 **78** 个 `Step`；本值取 **240**（≈3.1× 余量，
        /// 亦覆盖两库反弹的更长银行球）。上限同时是"打不到任何球"场景的**有界停止**依据（`T83c`）。
        /// </summary>
        [Tooltip("预测步数上限（Step 数）。待实测定值。")]
        public int maxPredictSteps = 240;

        /// <summary>
        /// **拖动瞄准时两次重算之间的最小间隔（秒）**（前沿节流）。默认 <c>1/30</c>（约 30 Hz）——
        /// 远高于观感需求（瞄准线 30 Hz 已连续），又比"每帧一次"省下大半预测开销。
        /// <c>≤ 0</c> ⇒ **关闭节流**（每帧重算，退回旧行为，用于判据与对照）。
        /// ⚠ 尾沿（停止变化时）不受本值约束 ⇒ 不会让瞄准线停在旧值。
        /// </summary>
        [Tooltip("拖动时两次重算的最小间隔（秒）。≤ 0 ⇒ 关闭节流（每帧重算）。")]
        public float minRecomputeIntervalSeconds = 1f / 30f;

        /// <summary>被观察的驱动器（**只读**它的 `Simulator` / `PredictionEnabled`）。</summary>
        [Tooltip("被观察的 PhysicsTestDrive。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        /// <summary>瞄准相机（本类**不**用；留给表现层/后续扩展，与 spec §3.3 的序列化字段一致）。</summary>
        [Tooltip("瞄准相机（可空）。")]
        public Camera aimCamera;

        /// <summary>可视野缩放根（本类**不**用；由 <see cref="AimAssistView"/> 使用）。</summary>
        [Tooltip("可视野缩放根（PlayfieldRoot，可空）。")]
        public Transform playfieldRoot;

        private ShotPrediction _prediction;

        /// <summary>上一次**重算**所用状态的数值指纹（64 位哈希）。</summary>
        private long _lastFingerprint;

        /// <summary>上一帧的指纹（用于尾沿判定"输入是否已停止变化"）。</summary>
        private long _prevFrameFingerprint;

        private bool _hasPrediction;

        /// <summary>是否已有可用的"上一帧指纹"。</summary>
        private bool _hasPrevFrameFingerprint;

        /// <summary>缓存是否已被作废（关闭开关时置位）⇒ 重开后的第一次必重算（不受节流影响）。</summary>
        private bool _cacheInvalidated;

        /// <summary>上一次重算的时刻（`Time.unscaledTime`）；未算过 ⇒ 负无穷（⇒ 首算不受节流）。</summary>
        private float _lastRecomputeTime = float.NegativeInfinity;

        /// <summary>复用的预测工作区（探测内核 + 快照/路径缓冲）。</summary>
        private readonly ShotPredictionWorkspace _workspace = new ShotPredictionWorkspace();

        /// <summary>当前预测结果（只读出口）。关闭或未静止时为上一次结果或 <c>default</c>。</summary>
        public ShotPrediction Prediction => _prediction;

        /// <summary>
        /// **是否应当绘制瞄准辅助**（**纯函数**，唯一判据点；`ShouldShow` 与判据 `T104` 共用）。
        ///
        /// 四条同时成立才画：
        ///  · `predictionEnabled` —— 设置开关；
        ///  · `tableAtRest` —— **球运动期间不画**。预测的语义是"未出杆时的一次前向模拟"，
        ///    球在滚时它是**上一杆的旧结果**；继续画就会表现为"瞄准线停在原地不动"
        ///    （**用户实测反馈**）。隐藏到球停住再恢复，与本类文档里"与 `AimIndicator` 隐藏口径一致"的
        ///    说法终于对齐 —— 此前那句话只是注释、**实现里从未落地**。
        ///  · `cueBallInPlay` —— **白球必须在场上**。白球落袋后整盘会静止、相位也回到 `Aim`，
        ///    若不判这一条，线会从**袋口那个残留位置**画出来（用户实测截图：一条白线从顶袋射向台面）。
        ///  · `modalOpen == false` —— **模态让位**：结算卡或**首页**打开期间整体让位
        ///    （两者都是"台面还没轮到你"的状态；判据由 <see cref="ShouldShow"/> 合并后传入）。
        /// </summary>
        public static bool ShouldShowAimAssist(bool predictionEnabled, bool tableAtRest, bool cueBallInPlay,
                                               bool modalOpen)
        {
            return predictionEnabled && tableAtRest && cueBallInPlay && !modalOpen;
        }

        /// <summary>本组件当前是否应当绘制（把实时状态喂给 <see cref="ShouldShowAimAssist"/>）。</summary>
        public bool ShouldShow
        {
            get
            {
                PoolSimulator sim = drive != null ? drive.Simulator : null;
                return ShouldShowAimAssist(
                    drive != null && drive.PredictionEnabled,
                    sim != null && sim.TableAtRest,
                    sim != null && sim.IsBallInPlay(0),
                    // 两个模态判据点：结算卡（`PracticeResultView`）与首页（`HomeView`）。
                    Billiards.UI.PracticeResultView.IsOpen || Billiards.UI.HomeView.IsOpen);
            }
        }

        /// <summary>累计**重算**次数（只读诊断量）。关闭期间**逐位不变**（`T82a`）。</summary>
        public int PredictionComputeCount { get; private set; }

        /// <summary>
        /// 累计**预测步数**（只读诊断量；每次重算加 <c>ShotPrediction.StepsSimulated</c>）。
        /// 它是**与机器无关的"成本"量** —— 见 <see cref="Billiards.Core.SmokeProbe.ReportPredictionCost"/> 的动机。
        /// </summary>
        public long TotalPredictSteps { get; private set; }

        /// <summary>
        /// **节流决策**（**纯函数**，判据直接驱动，不依赖计时）：
        /// · 状态没变 ⇒ 不算（脏标记优先）；
        /// · 尚无预测（首算 / 缓存被作废）⇒ 必算；
        /// · 输入已停止变化（`settledSinceLastFrame`）⇒ **必算**（尾沿；保证"停拖后下一帧是最新预测"）；
        /// · 否则 ⇒ 距上次重算 ≥ 间隔才算（前沿节流；间隔 ≤ 0 ⇒ 不节流）。
        /// </summary>
        public static bool ShouldRecompute(bool hasPrediction, bool stateChanged, bool settledSinceLastFrame,
                                           float secondsSinceLastRecompute, float minIntervalSeconds)
        {
            if (!stateChanged)
            {
                return false;
            }

            if (!hasPrediction)
            {
                return true;
            }

            if (settledSinceLastFrame)
            {
                return true;
            }

            if (!(minIntervalSeconds > 0f))
            {
                return true;
            }

            return secondsSinceLastRecompute >= minIntervalSeconds;
        }

        /// <summary>
        /// **纯逻辑入口**（`LateUpdate` 与判据共用同一条路径）。
        /// </summary>
        /// <returns>本帧**是否发生了重算**（`true` ⇒ <see cref="PredictionComputeCount"/> 自增 1）。</returns>
        public bool RefreshPrediction()
        {
            if (drive == null || !drive.PredictionEnabled)
            {
                // 关闭 ⇒ 不重算、不绘制；把脏缓存作废（重新开启必重算），但**不动** `_prediction`。
                InvalidateCache();
                return false;
            }

            PoolSimulator sim = drive.Simulator;
            if (sim == null || !sim.TableAtRest)
            {
                // 球在动 / 未初始化 ⇒ 不重算。注意不更新"上一帧指纹"：恢复静止后首帧不算"已稳定"。
                _hasPrevFrameFingerprint = false;
                return false;
            }

            long fingerprint = Fingerprint(sim);
            bool stateChanged = _cacheInvalidated || !_hasPrediction || fingerprint != _lastFingerprint;
            bool settled = _hasPrevFrameFingerprint && fingerprint == _prevFrameFingerprint;
            _prevFrameFingerprint = fingerprint;
            _hasPrevFrameFingerprint = true;

            bool due = ShouldRecompute(_hasPrediction, stateChanged, settled,
                Time.unscaledTime - _lastRecomputeTime, minRecomputeIntervalSeconds);
            if (!due)
            {
                return false;
            }

            float rad = drive.aimDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Mathf.Lerp(drive.minLaunchSpeed, drive.maxLaunchSpeed, Mathf.Clamp01(drive.power));

            _prediction = ShotPredictor.Predict(drive.tableConfig, sim, direction * speed, maxPredictSteps, _workspace);
            _lastFingerprint = fingerprint;
            _hasPrediction = true;
            _cacheInvalidated = false;
            _lastRecomputeTime = Time.unscaledTime;
            PredictionComputeCount++;
            TotalPredictSteps += _prediction.StepsSimulated;

            // **上报预测成本**（`G6`）：毫秒是机器相关的（同一份代码忙机/空闲机能差 ~2×），
            // 而"重算了几次、累计步进了多少步"是**纯计数** ⇒ 与机器无关、可跨运行比较。
            // 依赖方向 **Physics → Core**（下层不反向依赖上层），与驱动器上报物理步数同族。
            Billiards.Core.SmokeProbe.ReportPredictionCost(PredictionComputeCount, TotalPredictSteps);
            return true;
        }

        private void LateUpdate()
        {
            RefreshPrediction();
        }

        /// <summary>作废脏缓存（不动 <see cref="Prediction"/>）；重开后的第一次必重算（`_lastRecomputeTime` 归负无穷）。</summary>
        private void InvalidateCache()
        {
            _lastFingerprint = 0L;
            _prevFrameFingerprint = 0L;
            _hasPrevFrameFingerprint = false;
            _cacheInvalidated = true;
            _lastRecomputeTime = float.NegativeInfinity;
        }

        /// <summary>
        /// 在场上球的**位置 / 速度指纹** + 瞄准角 / 力度的 **64 位 FNV-1a 哈希**。
        ///
        /// ── 为什么从"字符串指纹"改成"数值哈希"（性能）────────────────────────
        /// 旧实现每帧对 17 颗球 × 4 个 float 调 `ToString("R")` 并 `StringBuilder.Append`、最后拼成 string
        /// ⇒ 拖动瞄准时每帧数十次格式化 + 一次字符串分配。数值哈希**零分配**，
        /// 判别力足够（`2^64` 空间，单帧碰撞概率 ~1e-19）—— 它只承担"脏标记"，
        /// 不承担"逐位快照"（那由 `T72b` 的显式逐位基线负责）。
        ///
        /// ⚠ 哈希是**有损**的：极端情况下两次不同状态可能同哈希（漏一次重算，只影响一帧观感，不影响出杆）。
        /// 这是"脏标记"可接受的代价，已在此显式登记（不声称它在所有输入下都是完美判别器）。
        /// </summary>
        private long Fingerprint(PoolSimulator sim)
        {
            // FNV-1a 64 位：偏移基址 + 质数（与任何标准库无关，纯算术，确定性）。
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;
            hash = Mix(hash, unchecked((uint)System.BitConverter.SingleToInt32Bits(drive.aimDegrees)), prime);
            hash = Mix(hash, unchecked((uint)System.BitConverter.SingleToInt32Bits(drive.power)), prime);

            var balls = sim.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                if (!sim.IsBallInPlay(i))
                {
                    continue;
                }

                hash = Mix(hash, unchecked((uint)i), prime);
                hash = Mix(hash, unchecked((uint)System.BitConverter.SingleToInt32Bits(balls[i].position.x)), prime);
                hash = Mix(hash, unchecked((uint)System.BitConverter.SingleToInt32Bits(balls[i].position.y)), prime);
                hash = Mix(hash, unchecked((uint)System.BitConverter.SingleToInt32Bits(balls[i].velocity.x)), prime);
                hash = Mix(hash, unchecked((uint)System.BitConverter.SingleToInt32Bits(balls[i].velocity.y)), prime);
            }

            return unchecked((long)hash);
        }

        private static ulong Mix(ulong hash, uint value, ulong prime)
        {
            hash ^= value;
            return hash * prime;
        }
    }
}
