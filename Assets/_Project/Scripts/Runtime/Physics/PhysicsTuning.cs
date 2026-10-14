using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **物理手感参数的唯一可调入口**（ScriptableObject）。排期 Day 7 要求把
    /// 「库边弹性 / 台呢滚阻 / 球-球恢复 / 袋口半径 / 初始阻尼」收敛到一个资产。
    ///
    /// ── 单一真源图（谁读 / 谁覆盖）──────────────────────────────────────────
    ///  · **本资产（`PhysicsTuning.asset`）= 玩家/调参者可调参数的唯一入口**。
    ///  · `PhysicsTestDrive.Initialize()` **读本资产**（`tuning == null` 时回落到
    ///    <see cref="DefaultValues"/>）→ <see cref="ToPhysicsParams"/> → `PoolSimulator`。
    ///  · **`PhysicsParams.SafeDefault` 不被本资产覆盖**：它是"字段全零兜底"的**安全基线**
    ///    （由 `F2a` 钉住 e=1/μ=1）。本资产的**默认值与它刻意一致** ⇒ 行为不变。
    ///  · **袋口半径的真源仍是 `TableConfig.Pocket.captureRadius`（逐袋）**；
    ///    本资产只提供**全局乘子** <see cref="pocketCaptureRadiusScale"/>（默认 1.0 = 不变），
    ///    **不把逐袋值搬进来**（否则违反 `R-Y`：用单值代替逐袋）。
    ///
    /// ── 默认值 = 现口径（保持行为不变）────────────────────────────────────────
    ///  · `e_ball = 1.0`（**注意**：Day-5 spec §2.1 文字写"运行时默认 0.95"，
    ///    但**代码现行默认是 1.0**，且 `F2b` 断言实机档 `e_ball == 1.0` ⇒ 本资产默认取 **1.0**。
    ///    `0.95` 作为**扫描建议值**由调参裁定，不擅自改默认。）
    ///  · `rollingDeceleration = 0`（内核阶段单一口径）。
    ///  · `pocketCaptureRadiusScale = 1.0`（逐袋值不变）。
    /// </summary>
    [CreateAssetMenu(fileName = "PhysicsTuning", menuName = "Billiards/Physics Tuning", order = 0)]
    public class PhysicsTuning : ScriptableObject
    {
        [Header("库边")]
        [Tooltip("库边法向恢复系数 e，[0,1]。1 = 完全弹性。")]
        [Range(0f, 1f)]
        public float cushionRestitution = 1f;

        [Tooltip("库边切向摩擦保留系数 μ，[0,1]。1 = 无摩擦。")]
        [Range(0f, 1f)]
        public float cushionTangentialFriction = 1f;

        [Header("台呢")]
        [Tooltip("台呢滚动减速度 a，m/s²。物理内核阶段 = 0。")]
        public float rollingDeceleration = 0f;

        [Header("球-球")]
        [Tooltip("球-球法向恢复系数 e_ball，[0,1]。台球实测 0.93–0.96；现行代码默认 1.0（F2b 钉住）。")]
        [Range(0f, 1f)]
        public float ballRestitution = 1f;

        [Tooltip("球-球切向保留系数 μ_ball，[0,1]。本模型无自旋 ⇒ 必须 1.0（<1 属非物理耗散）。")]
        [Range(0f, 1f)]
        public float ballTangentialFriction = 1f;

        [Header("袋口")]
        [Tooltip("袋口捕获半径的**全局乘子**（默认 1 = 不变）。真源仍是 TableConfig.Pocket.captureRadius（逐袋）。")]
        public float pocketCaptureRadiusScale = 1f;

        [Header("初始阻尼（预留）")]
        [Tooltip("预留位：全局初始阻尼。**当前内核无此自由度 ⇒ 改它无效果**；保持 0，不得用于出厂。")]
        public float initialDamping = 0f;

        [Header("运动终止（游戏级静止声明）")]
        [Tooltip("宏观静止速度阈值，m/s。默认与 PoolSimulator.RestSpeedThreshold 一致（1e-3）。")]
        public float restSpeedThreshold = 1e-3f;

        [Tooltip("宏观静止需连续满足阈值的定长步数。默认与 PoolSimulator.RestStableFrames 一致（8）。")]
        public int restStableFrames = 8;

        /// <summary>
        /// **默认值（= 现口径）**。**不构造 ScriptableObject 实例**，只给出一组标量默认，
        /// 供 `PhysicsTestDrive` 在未指定资产时回落使用。
        /// </summary>
        public struct Defaults
        {
            public float cushionRestitution;
            public float cushionTangentialFriction;
            public float rollingDeceleration;
            public float ballRestitution;
            public float ballTangentialFriction;
            public float pocketCaptureRadiusScale;
        }

        /// <summary>默认值（逐字段显式给出；与 `PhysicsParams.SafeDefault` 刻意一致）。</summary>
        public static Defaults DefaultValues()
        {
            return new Defaults
            {
                cushionRestitution = 1f,
                cushionTangentialFriction = 1f,
                rollingDeceleration = 0f,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                pocketCaptureRadiusScale = 1f,
            };
        }

        /// <summary>
        /// 转换为内核参数。**逐字段显式赋值**（满足 `F2c`：不得出现字段不全的 `new PhysicsParams`）。
        /// </summary>
        public PhysicsParams ToPhysicsParams()
        {
            return new PhysicsParams
            {
                cushionRestitution = cushionRestitution,
                cushionTangentialFriction = cushionTangentialFriction,
                ballRestitution = ballRestitution,
                ballTangentialFriction = ballTangentialFriction,
                rollingDeceleration = rollingDeceleration,
            };
        }
    }
}
