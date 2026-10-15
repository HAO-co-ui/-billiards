using System.Globalization;
using System.Text;
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
    ///  1. **零扰动**：本类**不得**调用 `drive.Simulator` 的 `Step` / `PlaceBall` / `LoadBalls` /
    ///     `ResetDiagnostics`（预测只读；在跑内核的扰动由 `T72` 与 `T82` 的源码守卫共同覆盖）。
    ///  2. **关闭即不算**：<see cref="RefreshPrediction"/> 首行判 `drive.PredictionEnabled` ⇒ 关闭期间
    ///     **不调用** `Predict`（<see cref="PredictionComputeCount"/> 逐位不变）。
    ///  3. **开关不另存副本**：唯一读点是 `drive.PredictionEnabled`（→ `PhysicsTestDrive.enableShotPrediction`）。
    ///
    /// ── 刷新时机（脏标记）────────────────────────────────────────────────────
    /// 仅当 `TableAtRest`（球运动期间预测无意义、且与 `AimIndicator` 的隐藏口径一致）**且**
    /// `(aimDegrees, power, 在场上球的位置/速度指纹)` 变化时才重算 ⇒ 静止瞄准时**不每帧重算**。
    /// ⚠ 关闭期间把脏缓存**置空**（重新开启必重算），但**不改** <see cref="Prediction"/>。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · 输入设备级触发（真鼠标/键盘）不在 EditMode 覆盖范围（Manual / PlayMode）。
    /// · 真实帧率不判（无 CI / 无固定机型）。
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
        private string _lastFingerprint;
        private bool _hasPrediction;

        /// <summary>当前预测结果（只读出口）。关闭或未静止时为上一次结果或 <c>default</c>。</summary>
        public ShotPrediction Prediction => _prediction;

        /// <summary>
        /// **是否应当绘制瞄准辅助**（**纯函数**，唯一判据点；`ShouldShow` 与判据 `T104` 共用）。
        ///
        /// 三条同时成立才画：
        ///  · `predictionEnabled` —— 设置开关；
        ///  · `tableAtRest` —— **球运动期间不画**。预测的语义是"未出杆时的一次前向模拟"，
        ///    球在滚时它是**上一杆的旧结果**；继续画就会表现为"瞄准线停在原地不动"
        ///    （**用户实测反馈**）。隐藏到球停住再恢复，与本类文档里"与 `AimIndicator` 隐藏口径一致"的
        ///    说法终于对齐 —— 此前那句话只是注释、**实现里从未落地**。
        ///  · `cueBallInPlay` —— **白球必须在场上**。白球落袋后整盘会静止、相位也回到 `Aim`，
        ///    若不判这一条，线会从**袋口那个残留位置**画出来（用户实测截图：一条白线从顶袋射向台面）。
        ///  · `resultCardOpen == false` —— 结算卡是模态，打开期间整体让位。
        /// </summary>
        public static bool ShouldShowAimAssist(bool predictionEnabled, bool tableAtRest, bool cueBallInPlay,
                                               bool resultCardOpen)
        {
            return predictionEnabled && tableAtRest && cueBallInPlay && !resultCardOpen;
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
                    Billiards.UI.PracticeResultView.IsOpen);
            }
        }

        /// <summary>累计**重算**次数（只读诊断量）。关闭期间**逐位不变**（`T82a`）。</summary>
        public int PredictionComputeCount { get; private set; }

        /// <summary>
        /// **纯逻辑入口**（`LateUpdate` 与判据共用同一条路径）。
        /// </summary>
        /// <returns>本帧**是否发生了重算**（`true` ⇒ <see cref="PredictionComputeCount"/> 自增 1）。</returns>
        public bool RefreshPrediction()
        {
            if (drive == null || !drive.PredictionEnabled)
            {
                // 关闭 ⇒ 不重算、不绘制；把脏缓存作废（重新开启必重算），但**不动** `_prediction`。
                _lastFingerprint = null;
                return false;
            }

            PoolSimulator sim = drive.Simulator;
            if (sim == null || !sim.TableAtRest)
            {
                return false;
            }

            string fingerprint = Fingerprint(sim);
            if (_hasPrediction && fingerprint == _lastFingerprint)
            {
                return false;   // 脏标记未变 ⇒ 不重算
            }

            float rad = drive.aimDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Mathf.Lerp(drive.minLaunchSpeed, drive.maxLaunchSpeed, Mathf.Clamp01(drive.power));

            _prediction = ShotPredictor.Predict(drive.tableConfig, sim, direction * speed, maxPredictSteps);
            _lastFingerprint = fingerprint;
            _hasPrediction = true;
            PredictionComputeCount++;
            return true;
        }

        private void LateUpdate()
        {
            RefreshPrediction();
        }

        /// <summary>
        /// 在场上球的**位置 / 速度指纹**（`ToString("R")`，与既有逐位快照同口径）＋ 瞄准角 / 力度。
        /// 只读 `Balls` / `IsBallInPlay`，不碰任何内核写入口。
        /// </summary>
        private string Fingerprint(PoolSimulator sim)
        {
            var sb = new StringBuilder(128);
            sb.Append(drive.aimDegrees.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(drive.power.ToString("R", CultureInfo.InvariantCulture)).Append('|');

            var balls = sim.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                if (!sim.IsBallInPlay(i))
                {
                    continue;
                }

                sb.Append(i).Append(':')
                  .Append(balls[i].position.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(balls[i].position.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(balls[i].velocity.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(balls[i].velocity.y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }

            return sb.ToString();
        }
    }
}
