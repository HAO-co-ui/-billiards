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
    /// ── 默认值 = 灰盒现行**实际生效**值（接线后行为不变）────────────────────
    ///  · `cushion e = 0.8` / `μ = 0.9` —— 这正是 `PhysicsGreybox` 之前走"回落路径"时**实际生效**的占位值；
    ///    把它们搬进资产 ⇒ **接线不改变行为**，但**改资产真的会改变灰盒**（R1 缓解措施成立）。
    ///  · **排期所说的「初始阻尼」在本模型里 = `rollingDeceleration`（台呢滚阻）** ——
    ///    **不另设字段**（避免"声明了却不生效"的惰性字段）。
    ///  · `e_ball = 0.95`（**调参目标口径**；Day 7 依扫描裁定：白球行程随 `e_ball` 单调递减 ——
    ///    1.00→2.587 m / 0.95→2.249 m / 0.90→1.883 m ⇒ 取台球实测 0.93–0.96 的中点）。
    ///    ⚠ **两个数字的关系（勿误认为矛盾）**：
    ///      · **`PhysicsParams.SafeDefault` 的 `e_ball` = 1.0** —— 它是"**字段全零**"的**解药**（安全兜底），
    ///        由 `F2a` 钉住；**它不是调参默认值**。
    ///      · **本资产 / `PhysicsTestDrive` 的默认 = 0.95** —— 那是**调参目标口径**，随调参结论而变。
    ///  · `rollingDeceleration = 0`（内核阶段单一口径）。
    ///  · `pocketCaptureRadiusScale = 1.0`（逐袋值不变）。
    /// </summary>
    [CreateAssetMenu(fileName = "PhysicsTuning", menuName = "Billiards/Physics Tuning", order = 0)]
    public class PhysicsTuning : ScriptableObject
    {
        [Header("库边")]
        [Tooltip("库边法向恢复系数 e，[0,1]。默认 0.8 = **灰盒现行实际生效值**（改动会真的改变灰盒行为）。")]
        [Range(0f, 1f)]
        public float cushionRestitution = 0.8f;

        [Tooltip("库边切向摩擦保留系数 μ，[0,1]。默认 0.9 = **灰盒现行实际生效值**。")]
        [Range(0f, 1f)]
        public float cushionTangentialFriction = 0.9f;

        [Header("台呢")]
        [Tooltip("台呢滚动减速度 a，m/s²。物理内核阶段 = 0。")]
        public float rollingDeceleration = 0f;

        [Header("球-球")]
        [Tooltip("球-球法向恢复系数 e_ball，[0,1]。台球实测 0.93–0.96；**调参目标口径 = 0.95**（Day 7 定）。")]
        [Range(0f, 1f)]
        public float ballRestitution = 0.95f;

        [Tooltip("球-球切向保留系数 μ_ball，[0,1]。本模型无自旋 ⇒ 必须 1.0（<1 属非物理耗散）。")]
        [Range(0f, 1f)]
        public float ballTangentialFriction = 1f;

        [Header("袋口")]
        [Tooltip("袋口捕获半径的**全局乘子**（默认 1 = 不变）。真源仍是 TableConfig.Pocket.captureRadius（逐袋）。")]
        public float pocketCaptureRadiusScale = 1f;

        [Header("运动终止（游戏级静止声明）")]
        [Tooltip("宏观静止速度阈值，m/s。默认与 PoolSimulator.RestSpeedThreshold 一致（1e-3）。**内核真的读它**。")]
        public float restSpeedThreshold = 1e-3f;

        [Tooltip("宏观静止需连续满足阈值的定长步数。默认与 PoolSimulator.RestStableFrames 一致（8）。**内核真的读它**。")]
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
                cushionRestitution = 0.8f,
                cushionTangentialFriction = 0.9f,
                rollingDeceleration = 0f,
                ballRestitution = 0.95f,
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
