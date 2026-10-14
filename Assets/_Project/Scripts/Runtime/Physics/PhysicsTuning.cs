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
    ///  · **台呢滚阻 = 两段模型**（本资产承载三个字段，见 <see cref="rollingDecelerationSlide"/> 等）：
    ///    出厂口径 = **滑动段 `0.5` / 滚动段 `0.25` / 过渡速度 `2.0`**（用户拍板档 ④）——
    ///    **不是退化档**（两段取值不同 ⇒ 减速度随速度变：高速段扣得多、低速段扣得少 ⇒ 球能"滑出去"）。
    ///    沿革：内核阶段旧单值曾取 `0`（"单一口径"）→ 调参阶段定为 `0.9`（**恒定**，两段同值）
    ///    → 本次起由三字段承载，且出厂**离开退化档**。
    ///    ⚠ **退化档仍是显式逃生舱**：把两段设成同值即可**逐位回退**到历史的单一恒定减速度
    ///    （由 `T54` 钉住"逐位等价"）。
    ///    ⚠ **这是「手感口径」、不是物理真值** —— 真实台呢滚阻约 **0.05–0.15 m/s²**，
    ///    本出厂**滚动段 `0.25`** 是其约 **1.7–5 倍**（旧恒定口径 `0.9` 曾是 **6–8 倍**），
    ///    为"**满力一杆滚得够远、又不过久**"而调高。仍在 spec 声明的 **`a_max = 5 m/s²`** 包线内 ⇒ 不属超纲。
    ///    ⚠ **参照场景 = 4 球灰盒**；**15 球满堆能量耗散更多 ⇒ 将来上满堆时可能需再调**。
    ///  · `e_ball = 0.95`（**调参目标口径**；依扫描裁定：白球行程随 `e_ball` 单调递减 ——
    ///    1.00→2.587 m / 0.95→2.249 m / 0.90→1.883 m ⇒ 取台球实测 0.93–0.96 的中点）。
    ///    ⚠ **两个数字的关系（勿误认为矛盾）**：
    ///      · **`PhysicsParams.SafeDefault` 的 `e_ball` = 1.0** —— 它是"**字段全零**"的**解药**（安全兜底），
    ///        由 `F2a` 钉住；**它不是调参默认值**。
    ///      · **本资产 / `PhysicsTestDrive` 的默认 = 0.95** —— 那是**调参目标口径**，随调参结论而变。
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

        [Header("台呢（滚阻两段模型 —— **手感唯一入口**）")]
        [Tooltip("**滑动段**减速度 a_slide（高速时取值），m/s²。台呢滚阻两段模型的**高速段**；量级 1–3。"
                 + "⚠ 与 `rollingDecelerationRoll` **取同值**即“恒定档”（与历史单一减速度逐位等价）。")]
        public float rollingDecelerationSlide = 0.5f;

        [Tooltip("**滚动段**减速度 a_roll（低速时取值），m/s²。台呢滚阻两段模型的**低速段**；量级 0.10–0.30。"
                 + "⚠ **这是“球能滑多远”的主旋钮**：调小它 ⇒ 尾巴更长（球滚更远）。")]
        public float rollingDecelerationRoll = 0.25f;

        [Tooltip("两段过渡速度 v_transition，m/s。`a(v) = a_roll + (a_slide − a_roll)·clamp01(v / v_transition)`；"
                 + "≤ 0 ⇒ 恒取 a_slide。量级 1–3。⚠ 两段同值时本项**不影响**结果。")]
        public float rollingTransitionSpeed = 2.0f;

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

        [Header("力度 → 初速（交互层手感旋钮；唯一可调入口）")]
        [Tooltip("**力度下限**对应的初速（m/s）。本两项是交互层手感旋钮，由本资产统一提供（`B4` 事故：此前无资产字段，只靠代码默认 + 场景序列化）。")]
        public float minLaunchSpeed = 0.5f;

        [Tooltip("**满力**对应的初速（m/s）。⚠ 与 `PoolConstants.MaxBallSpeed`（=8，喂 `DeriveSubSteps`）**不是一回事**，不得混用。")]
        public float maxLaunchSpeed = 4.5f;

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
            public float rollingDecelerationSlide;
            public float rollingDecelerationRoll;
            public float rollingTransitionSpeed;
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
                rollingDecelerationSlide = 0.5f,
                rollingDecelerationRoll = 0.25f,
                rollingTransitionSpeed = 2.0f,
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
                rollingDecelerationSlide = rollingDecelerationSlide,
                rollingDecelerationRoll = rollingDecelerationRoll,
                rollingTransitionSpeed = rollingTransitionSpeed,
            };
        }
    }
}
