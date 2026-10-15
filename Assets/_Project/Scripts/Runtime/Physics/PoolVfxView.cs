using System.Collections.Generic;
using Billiards.Core;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **击球特效表现层**：4 类程序化特效（击杆 / 碰球 / 撞库 / 入袋）——按素材包 `pool-vfx.js` 的
    /// 形状 / 颜色 / 时序**按意图重建**（包内无序列帧或 Prefab，不追求逐帧复刻）。
    ///
    /// ── 触发架构（与音频层的事件流正交，如实登记）──────────────────────
    /// · **门 = 既有只读计数增量**（strike = `LaunchCount`；hit = `BallBallContactCount`；
    ///   rail = `TotalContactCount − BallBallContactCount`；pocket = `CapturedBallCount`）；
    ///   ⚠ `pocket` 用 `CapturedBallCount`（**真正入袋**）而**不是** `PocketProximityEvents`：
    ///   后者自袋角归属裁定起还包含"擦袋角/隧穿越界"（球离台但**并未入袋**）⇒ 用它会为一次越界冻结爆出"入袋"特效。
    ///   另：静止补判入袋（`AtRestMouthDrops`）只增 `CapturedBallCount` ⇒ 本门自动覆盖该支。
    /// · **位置 = 状态推导**（同参考实现：找质量最高的接触对 / 贴库球）——**不消费** `DrainContactEvents`
    ///   （那是音频的单消费者通道，避免争抢；若将来两者都要事件流，应改"单一分发者"，超出本单）。
    /// · 计数在 `ResetDiagnostics`（每次出杆）归零 ⇒ 门只认**上升**；下降（重置）静默更新基准、不触发。
    ///
    /// ── 池化与上限（排期「不 Instantiate 抖动 / 粒子数封顶」）──────────
    /// · 启动**一次性**预建 <see cref="SlotCapacity"/> 槽（每槽 = 1 环/弧 + 3 火花；全部 LineRenderer）；
    ///   运行期**零** Instantiate；槽满**丢弃**并计数（同参考实现）。
    /// · 绘制顶点一律走 <see cref="AimAssistView.MarkerWorldPoint"/>（显示层单点：X/Z 双取反 + 球心高度）。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────
    /// · 特效**观感**非判据可测（批处理 `-nographics` 无渲染）——判据只驱动纯逻辑
    ///   （动作曲线 / 接触推导 / 限频门）；LineRenderer 顶点沿用 AimAssistView 的既有盲带口径。
    /// · **顶点写入路径已由 `T99` 覆盖**（`WriteLine` 的"先数量后顶点"契约 + 源码级"`SetPosition`
    ///   只准出现在 `WriteLine` 内"）。该路径原先是纯盲带，直到实机刷出
    ///   `LineRenderer.SetPosition index out of bounds!` 才暴露 ⇒ 现已收成单点并加判据。
    /// </summary>
    [AddComponentMenu("Billiards/Pool Vfx View")]
    public sealed class PoolVfxView : MonoBehaviour
    {
        // ── 参考实现参数（pool-vfx.js）──────────────────────────────────────
        public const int SlotCapacity = 16;
        public const int RingSegments = 32;
        public const int ArcSegments = 16;
        public const float HitIntervalSeconds = 0.045f;
        public const float RailIntervalSeconds = 0.07f;
        public const float ContactMinSpeed = 0.1f;
        public const float HitDistanceTolerance = 0.007f;
        public const float RailDistanceTolerance = 0.01f;
        /// <summary>
        /// 线宽（**规范米**，全类**唯一**线宽常量——环 / 弧 / 火花共用）。世界宽 = 本值 ×
        /// <see cref="AimAssistView.DrawScale"/>。
        ///
        /// ⚠ **实测定值（2026-10-15，用户实机反馈"特效都是白的"）**：
        /// 原实现写 `0.004` 且**没有乘显示缩放** ⇒ 世界宽 4 mm；而瞄准线是
        /// `PredictionLineWidth(0.006) × DrawScale(1.2)` = **世界 7.2 mm** ⇒ 特效比瞄准线**细 44%**。
        /// 线越细，同 alpha 下颜色被深色台面稀释得越厉害（实测观感就是"一根白线"）。
        /// ⇒ 先取 **6 mm**（与瞄准线同值）并**乘显示缩放**，两条线的口径从此一致。
        ///
        /// ⚠ **强化定值（`D15-VF-02`，用户拍板 A 档）**：`6 mm → 8 mm` 规范 = **世界 9.6 mm**
        /// （= 瞄准线 7.2 mm 的 **1.33×**）⇒ 一眼可辨"更粗的是特效、不是瞄准线"（可读性预算 R4，判据 `T115 ②`）。
        ///
        /// ⚠ **绝不能再新增第二个线宽常量**：`T99 (f)` 用**子串包含**做源码级计数守卫
        /// （数 `LineWidth` 与 `AimAssistView.DrawScale(` 的**连写**是否恰 1 次）。若新增 `SparkLineWidth` 之类，
        /// 其连写会以 `...LineWidth` 结尾而把守卫数成第 2 次 ⇒ 误红。故**火花与环/弧共用本常量**。
        /// </summary>
        public const float LineWidth = 0.008f;

        /// <summary>入袋"下沉"动画时长（秒）——球可视物下沉 + 缩小至消失。</summary>
        public const float SinkDurationSeconds = 0.3f;

        /// <summary>
        /// 入袋**爆点延迟**（秒）：`SinkSlideFraction × SinkDurationSeconds`（**用常量相乘表达，不硬编码 0.18**）
        /// = 球**到达洞口、开始落下**的那一瞬 —— 两段式"吸（Roll-in）→ 爆（Pop）"的**爆**相位在此生成（判据 `T92 (f)`）。
        /// </summary>
        public const float PocketBurstDelaySeconds = SinkSlideFraction * SinkDurationSeconds;

        private const int SparkCount = 3;
        private const float PocketInset = 0.013f;
        private const float SparkSpreadRad = 0.65f;
        private const float SparkStartFactor = 1.03f;
        private const float SparkTravelFactor = 0.65f;

        /// <summary>
        /// 击杆 / 碰球的**粉笔色**（**冷青蓝 ≈206°**，`#a9d8ff`）。
        ///
        /// ⚠ **配色沿革（用户实机反馈驱动，2026-10-15 → `D15-VF-02`）**：
        /// ① 参考的 `COLORS.chalk = '#f8f5e7'`（近白暖白）；用户实机看到"一撮白色火花"**与瞄准线纯白无法区分**，
        ///    先改 `#cfe3ef`（粉笔青白）以拉开与纯白的距离；
        /// ② `D15-VF-02` 再改本值 **`#a9d8ff`** —— 青蓝纯度更高、相对亮度更高（无 Bloom 下更"亮"），
        ///    且与 brass（金 ≈43°）/ rail（紫灰 ≈229°）**色相三足鼎立**（可读性预算 R5/三色分离，判据 `T115 ⑤`）。
        /// **回退**：把本行换回 `new Color(0.973f, 0.961f, 0.906f, 1f)` 即回到参考原值（一行）。
        /// </summary>
        public static readonly Color ChalkColor = new Color(0.663f, 0.847f, 1.000f, 1f);   // #a9d8ff
        /// <summary>入袋**爆外环**的暖金（`#f3d999`，≈43°）：入袋奖励的"金"。**回退**：本值未变。</summary>
        public static readonly Color BrassColor = new Color(0.953f, 0.851f, 0.600f, 1f);   // #f3d999
        /// <summary>
        /// 入袋**爆内核**的暖白（`#fff4de`，≈36°）：全屏**最亮**的 VFX（峰值明度序之顶）。
        /// 刻意用**暖白**而非中性白 ⇒ 与瞄准线的纯白（`AimAssistView.PathColor`）色相可分。
        /// **回退**：换回 `new Color(1f, 1f, 1f, 1f)` 即回到原"纯白内核"（一行）。
        /// </summary>
        public static readonly Color BrassCoreColor = new Color(1.000f, 0.957f, 0.871f, 1f); // #fff4de
        /// <summary>
        /// 撞库的**冷紫灰**（`#aab4de`，≈229°）。
        /// ⚠ 由原 `#ccdacb`（**淡绿**，与绿台呢**同色系**、对比度最低，且易与 chalk 冷白混淆）改来 ⇒
        /// **脱离台呢同色系**、与 chalk（青）/brass（金）色相拉开；**饱和度低** ⇒ 撞库虽频发但不抢戏（"安静档"）。
        /// **回退**：把本行换回 `new Color(0.800f, 0.855f, 0.796f, 1f)` 即还原（一处常量，判据 `T115 ⑤`）。
        /// </summary>
        public static readonly Color RailColor = new Color(0.667f, 0.706f, 0.871f, 1f);    // #aab4de

        [Tooltip("被驱动的驱动器（读计数出口与内核）。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        [Tooltip("球桌配置（撞库 / 入袋位置推导用）。为空 ⇒ rail/pocket 特效不生成。")]
        public TableConfig tableConfig;

        [Tooltip("可视野缩放根（PlayfieldRoot）。")]
        public Transform playfieldRoot;

        [Tooltip("特效总开关（设置页预留）。关闭 ⇒ 全部隐藏且不再触发。")]
        public bool vfxEnabled = true;

        /// <summary>槽满被丢弃的特效数（只读诊断）。</summary>
        public int DroppedCount { get; private set; }

        private sealed class VfxSlot
        {
            public LineRenderer ring;
            public LineRenderer[] sparks;
            public bool active;
            public float age;
            public float duration;
            public float radius;
            public float expansion;
            public float opacity;
            public Color color;
            public bool isArc;
            public float directionDeg;
            public int sparkVisible;
            public float sparkLength;
            public Vector2 center;
            public float spawnDelay;   // 入袋"爆内核"用：age 从 −delay 起算（< 0 时不出层）
        }

        private readonly List<VfxSlot> _slots = new List<VfxSlot>();
        private readonly List<Vector2> _posBuffer = new List<Vector2>();
        private readonly List<Vector2> _velBuffer = new List<Vector2>();

        // 顶点暂存（复用，避免每帧分配）。**必须先攒点、再交给 `WriteLine`** ——
        // 那条路径是唯一会碰 `positionCount` 的地方（见 `WriteLine` 的注释：越界只打日志不抛异常）。
        private readonly List<Vector3> _ringBuffer = new List<Vector3>(32);
        private readonly List<Vector3> _sparkBuffer = new List<Vector3>(2);
        private readonly IntervalGate _hitGate = new IntervalGate(HitIntervalSeconds);
        private readonly IntervalGate _railGate = new IntervalGate(RailIntervalSeconds);
        private Material _lineMaterial;
        private int _lastLaunch;
        private int _lastBallBall;
        private int _lastSegment;
        private int _lastPocket;
        private bool _wasEnabled = true;

        // ── 纯函数（判据直接驱动）──────────────────────────────────────────

        /// <summary>
        /// **动作曲线**（参考实现的逐帧更新式）：半径按 `radius × (1 + p × expansion)` 扩张；
        /// 透明度按 `opacity × (1 − p)^1.7` 渐隐。`p` 先夹到 [0,1]。
        /// </summary>
        public static void VfxPose(float progress, float radius, float expansion, float opacity,
                                   out float scale, out float alpha)
        {
            float p = Mathf.Clamp01(progress);
            scale = radius * (1f + p * expansion);
            alpha = opacity * Mathf.Pow(1f - p, 1.7f);
        }

        /// <summary>**入袋下沉曲线**（线性）：`yOffset = −0.08 × p`；`scale = 1 − p`。</summary>
        public static void PocketSinkPose(float progress, out float yOffset, out float scale)
        {
            float p = Mathf.Clamp01(progress);
            yOffset = -0.08f * p;
            scale = 1f - p;
        }

        /// <summary>入袋动画里**水平滑向洞口**所占的进度比例：前 60% 滚到洞口，之后原地落下。</summary>
        public const float SinkSlideFraction = 0.6f;

        /// <summary>
        /// 入袋动画的**水平滑动进度**（0→1，Hermite 平滑）：`clamp01(p / SinkSlideFraction)` 平滑。
        /// 之所以"先滑后落"：球被冻结/判入的位置常**离洞口还有几十毫米**（骑在袋角上、或在库边附近），
        /// 若整段动画都只做垂直下沉，观感就是"贴着库边原地往下滑"（用户实测："像是滑墙，不符现实"）。
        /// </summary>
        public static float PocketSinkSlide(float progress)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Clamp01(progress) / SinkSlideFraction));
        }

        /// <summary>
        /// 入袋动画的**局部位置**（与球可视物同一个坐标系）：水平从 `originLocal` 滑到 `holeLocal`（洞口中心），
        /// 垂直按 <see cref="PocketSinkPose"/> 下沉、并按它缩小 —— 即"**滚进洞口再落下**"。
        /// </summary>
        public static Vector3 PocketSinkLocalPosition(Vector3 originLocal, Vector3 holeLocal, float progress)
        {
            PocketSinkPose(progress, out float yOffset, out _);
            float s = PocketSinkSlide(progress);
            return new Vector3(
                Mathf.Lerp(originLocal.x, holeLocal.x, s),
                originLocal.y + yOffset,
                Mathf.Lerp(originLocal.z, holeLocal.z, s));
        }

        // ════════════════════════════════════════════════════════════════════
        //  VFX 规格表 + 力度分级（纯函数 / 常量；判据 T115 / T116 / T117 直接驱动）
        // ════════════════════════════════════════════════════════════════════

        /// <summary>单个特效**层**的静态规格（**规范米**）：半径 / 扩张 / 时长 / 峰值 alpha / 形态。</summary>
        public readonly struct VfxLayer
        {
            public readonly string Name;
            public readonly float Radius;
            public readonly float Expansion;
            public readonly float Duration;
            public readonly float PeakAlpha;
            public readonly bool IsArc;

            public VfxLayer(string name, float radius, float expansion, float duration, float peakAlpha, bool isArc)
            {
                Name = name;
                Radius = radius;
                Expansion = expansion;
                Duration = duration;
                PeakAlpha = peakAlpha;
                IsArc = isArc;
            }
        }

        // —— 4 类事件 × 各层的**基准**规格（力度分档的"档位 T1"值；判据 `T115 ①` 逐层读）——
        public static readonly VfxLayer StrikeCoreLayer = new VfxLayer("strike.core", 0.011f, 0.45f, 0.14f, 0.95f, true);
        public static readonly VfxLayer StrikeHaloLayer = new VfxLayer("strike.halo", 0.014f, 0.50f, 0.20f, 0.30f, true);
        public static readonly VfxLayer HitCoreLayer = new VfxLayer("hit.core", 0.011f, 0.50f, 0.11f, 0.90f, false);
        public static readonly VfxLayer HitHaloLayer = new VfxLayer("hit.halo", 0.014f, 0.50f, 0.13f, 0.28f, false);
        public static readonly VfxLayer RailLayer = new VfxLayer("rail", 0.012f, 0.35f, 0.15f, 0.60f, true);
        public static readonly VfxLayer PocketOuterLayer = new VfxLayer("pocket.outer", 0.023f, 0.50f, 0.30f, 0.85f, false);
        public static readonly VfxLayer PocketCoreLayer = new VfxLayer("pocket.core", 0.015f, 0.25f, 0.20f, 1.00f, false);

        /// <summary>撞击类（strike / hit / rail）各层（判据 `T115 ①`）。</summary>
        public static readonly VfxLayer[] ImpactLayers =
        {
            StrikeCoreLayer, StrikeHaloLayer, HitCoreLayer, HitHaloLayer, RailLayer,
        };

        /// <summary>入袋各层（判据 `T115 ①`）。</summary>
        public static readonly VfxLayer[] PocketLayers = { PocketOuterLayer, PocketCoreLayer };

        // —— 可读性预算（R1）：**上限常量只有一处**（`D15-VF-02` 加固）──────────────────────
        // 唯一源 = 规格 §1.3 的**世界毫米值**；"规范半径"夹取上限一律由它**派生**（除以显示尺度），
        // **不再各写一份字面量**（两份上限必然分叉，且会破坏"上限只有一处"这一可核对性质）。
        /// <summary>撞击类各层"最大世界半径（含扩张）"上限 = 26 mm（环不压任一球心 / 球号）。**唯一源**。</summary>
        public const float ImpactMaxWorldRadius = 0.026f;

        /// <summary>入袋各层"最大世界半径（含扩张）"上限 = 44 mm（此时球已离台，放宽）。**唯一源**。</summary>
        public const float PocketMaxWorldRadius = 0.044f;

        /// <summary>
        /// 撞击类夹取上限（**规范 m**）= `ImpactMaxWorldRadius ÷ 显示尺度` = 26 ÷ 1.2 = **0.021667**。
        /// **派生量（不是第二处字面量）** —— §1.3 的唯一上限是世界 26 mm。
        /// </summary>
        public const float ImpactRadiusCap = ImpactMaxWorldRadius / PoolConstants.PlayfieldVisualScale;

        /// <summary>入袋夹取上限（**规范 m**）= `PocketMaxWorldRadius ÷ 显示尺度` = 44 ÷ 1.2 = **0.036667**。**派生量**。</summary>
        public const float PocketRadiusCap = PocketMaxWorldRadius / PoolConstants.PlayfieldVisualScale;

        // —— 力度分档（§2）：边界左闭右开，档位倍率单调非减（判据 T116）——
        /// <summary>力度档位（0 轻 → 3 猛）。</summary>
        public enum VfxIntensityTier { T0Light = 0, T1Medium = 1, T2Heavy = 2, T3Extreme = 3 }

        public const float TierLightMaxSpeed = 0.60f;    // [0.10, 0.60) ⇒ T0
        public const float TierMediumMaxSpeed = 1.80f;   // [0.60, 1.80) ⇒ T1
        public const float TierHeavyMaxSpeed = 3.60f;    // [1.80, 3.60) ⇒ T2；[3.60, 8.00] ⇒ T3

        /// <summary>**按接触相对速度分档**（纯函数；边界左闭右开；`s ≥ 3.60` 归 T3）。</summary>
        public static VfxIntensityTier ClassifyIntensity(float speed)
        {
            if (speed < TierLightMaxSpeed)
            {
                return VfxIntensityTier.T0Light;
            }

            if (speed < TierMediumMaxSpeed)
            {
                return VfxIntensityTier.T1Medium;
            }

            if (speed < TierHeavyMaxSpeed)
            {
                return VfxIntensityTier.T2Heavy;
            }

            return VfxIntensityTier.T3Extreme;
        }

        /// <summary>**档位倍率**（纯函数，**无任何随机 / 时间抖动**）：size / alpha / expansion / dur + 火花增减。</summary>
        public static void TierMultipliers(VfxIntensityTier tier, out float sizeMul, out float alphaMul,
                                           out float expansionMul, out float durMul, out int sparkDelta)
        {
            switch (tier)
            {
                case VfxIntensityTier.T0Light:
                    sizeMul = 0.85f; alphaMul = 0.85f; expansionMul = 0.95f; durMul = 0.90f; sparkDelta = -1;
                    break;
                case VfxIntensityTier.T2Heavy:
                    sizeMul = 1.15f; alphaMul = 1.00f; expansionMul = 1.05f; durMul = 1.10f; sparkDelta = 0;
                    break;
                case VfxIntensityTier.T3Extreme:
                    sizeMul = 1.25f; alphaMul = 1.00f; expansionMul = 1.10f; durMul = 1.20f; sparkDelta = 0;
                    break;
                default:   // T1Medium（常规对碰）
                    sizeMul = 1.00f; alphaMul = 1.00f; expansionMul = 1.00f; durMul = 1.00f; sparkDelta = 0;
                    break;
            }
        }

        /// <summary>
        /// **击杆力度缩放 `k`**（纯函数；`k = 0.7 + 0.6 · clamp01(power)` ∈ [0.7, 1.3]）——
        /// `SpawnStrike` 的环/弧半径都乘它（沿用现行口径，`T86` 的"拖距→力度"映射链不动）。
        /// 抽成**单点**是为了让 `T115 ①` 能取到**真实最坏半径** `Radius × k(1)`（而非基准 k=1）——
        /// 否则该守卫测不到"满力度 + 高力度档"这一真实存在的组合（k=1.3 与 T3 在满力度时同时发生）。
        /// </summary>
        public static float StrikePowerScale(float power)
        {
            return StrikePowerScaleMin + Mathf.Clamp01(power) * StrikePowerScaleSpan;
        }

        /// <summary>力度缩放 `k` 的下界（`power = 0` ⇒ `k = 0.7`）。</summary>
        public const float StrikePowerScaleMin = 0.7f;

        /// <summary>力度缩放 `k` 的跨度（`power` 0→1 ⇒ `k` 0.7→1.3）。</summary>
        public const float StrikePowerScaleSpan = 0.6f;

        /// <summary>
        /// **半径夹取（作用于"最终半径"＝含扩张）**（纯函数；`D15-VF-02` 加固）。
        ///
        /// 规格 §2.3 的**定义**：本层在 `VfxPose` 取极值处（`p → 1`）的**最大几何半径**
        /// `maxExtent = baseRadius × sizeMul × (1 + expansion × expansionMul)` 必须 **≤** `radiusCap`。
        ///
        /// 而 `VfxPose` 的入参是**未扩张**的半径、扩张另由 `expansion` 承担 ⇒ 本函数返回的是
        /// **喂给 `VfxPose` 的半径**（把上式解出 `radius`）：
        /// ```
        /// radius = min(baseRadius × sizeMul × (1 + E), cap) / (1 + E)
        ///        == min(baseRadius × sizeMul, cap / (1 + E))        // E = expansion × expansionMul &gt; 0，代数等价
        /// ```
        /// ⇒ 调用方随后用**同一个** `expansion = baseExpansion × expansionMul` 走 `VfxPose`，
        /// 故 `p → 1` 处的半径恰为 `min(..., cap)` ⇒ **上限作用于最终半径（含扩张）**。
        ///
        /// ⚠ **原实现只夹 `baseRadius × sizeMul`、不含扩张** ⇒ 档位 T2/T3 的"晕层"会越预算
        /// （实测 hit 晕 T3 ≈ **32.5 mm &gt; 26 mm**）—— 这正是本加固消除的。高力度档的"更狠"
        /// 改由**亮度 / 线宽 / 层数 / 火花**承担，**不再由无界半径**承担 ⇒ 可读性预算在任何档位都成立。
        /// </summary>
        public static float FinalRadius(float baseRadius, float sizeMul, float expansion, float expansionMul, float radiusCap)
        {
            float finalExpansion = expansion * expansionMul;
            return Mathf.Min(baseRadius * sizeMul, radiusCap / (1f + finalExpansion));
        }

        /// <summary>**峰值 alpha 夹取**：全部 ≤ 1.0（可读性预算 R6）。</summary>
        public static float FinalAlpha(float peakAlpha, float alphaMul)
        {
            return Mathf.Min(peakAlpha * alphaMul, 1f);
        }

        /// <summary>**槽内火花数夹取**：`clamp(base + delta, 1, 3)`（槽内火花上限 = `SparkCount`）。</summary>
        public static int FinalSparkCount(int baseSparks, int sparkDelta)
        {
            return Mathf.Clamp(baseSparks + sparkDelta, 1, SparkCount);
        }

        /// <summary>
        /// **对一层应用力度分档**（纯函数；夹取顺序 = 扩张 → 半径（含扩张的最终半径）→ alpha → 时长；
        /// 夹取后**再**走 `VfxPose`）。仅供**带接触速度**的层使用（strike / hit / rail）—— pocket 无速度输入
        /// ⇒ 恒用基准值（但其 `FinalRadius` 仍走同一夹取，见 `SpawnPocket`）。
        /// </summary>
        public static void ApplyIntensity(float speed, float baseRadius, float baseExpansion, float baseAlpha,
                                          float baseDuration, float radiusCap,
                                          out float radius, out float expansion, out float alpha, out float duration)
        {
            TierMultipliers(ClassifyIntensity(speed), out float sizeMul, out float alphaMul,
                            out float expansionMul, out float durMul, out _);
            expansion = baseExpansion * expansionMul;
            radius = FinalRadius(baseRadius, sizeMul, baseExpansion, expansionMul, radiusCap);
            alpha = FinalAlpha(baseAlpha, alphaMul);
            duration = baseDuration * durMul;
        }

        // —— 槽与顶点预算（§7；判据 T117）——
        public const int StrikeSlotCost = 2;   // 核弧 + 晕弧
        public const int HitSlotCost = 2;      // 核环 + 晕环
        public const int RailSlotCost = 1;     // 单弧
        public const int PocketSlotCost = 2;   // 爆外环 + 爆内核

        /// <summary>单杆最坏入袋数（开球实测 ≤2；§7.1 边界：同袋 ≥3 时末位特效被丢弃）。</summary>
        public const int PocketWorstEventsPerRack = 2;

        /// <summary>
        /// **最坏并发槽数**（Little 定律，§7.1；纯函数）：`Σ ceil(最长层时长 / 门间隔) × 每事件槽`。
        /// 实测（规格 §7.1）= hit 3×2 + rail 3×1 + strike 1×2 + pocket 2×2 = **15 ≤ `SlotCapacity`(16)**。
        /// </summary>
        public static int MaxConcurrentSlots()
        {
            int hit = Mathf.CeilToInt(HitHaloLayer.Duration / HitIntervalSeconds) * HitSlotCost;
            int rail = Mathf.CeilToInt(RailLayer.Duration / RailIntervalSeconds) * RailSlotCost;
            int strike = StrikeSlotCost;
            int pocket = PocketWorstEventsPerRack * PocketSlotCost;
            return hit + rail + strike + pocket;
        }

        /// <summary>**单帧顶点写入上界** = `SlotCapacity × (RingSegments + 3 火花 × 2)` = 16 × 38 = **608**（§7.2）。</summary>
        public static int MaxVerticesPerFrame()
        {
            return SlotCapacity * (RingSegments + SparkCount * 2);
        }

        /// <summary>
        /// **碰球接触推导**（参考实现的质量排序）：在"距离 ≈ 2r（±0.007）且对速度和 > 0.1"的球对中，
        /// 取 `quality = speed / (0.002 + |dist − 2r|)` 最大者 ⇒ 中点 / 连心线角度 / 对速度和。
        /// </summary>
        public static bool FindHitContact(IReadOnlyList<Vector2> positions, IReadOnlyList<Vector2> velocities,
                                          float ballRadius, out Vector2 point, out float angleDeg, out float speed)
        {
            point = default;
            angleDeg = 0f;
            speed = 0f;

            if (positions == null || velocities == null || positions.Count != velocities.Count)
            {
                return false;
            }

            float best = 0f;
            bool found = false;
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                {
                    Vector2 delta = positions[j] - positions[i];   // 与参考实现同向（i → j）
                    float gap = Mathf.Abs(delta.magnitude - 2f * ballRadius);
                    if (gap > HitDistanceTolerance)
                    {
                        continue;
                    }

                    float pairSpeed = velocities[i].magnitude + velocities[j].magnitude;
                    if (!(pairSpeed > ContactMinSpeed))
                    {
                        continue;
                    }

                    float quality = pairSpeed / (0.002f + gap);
                    if (quality > best)
                    {
                        best = quality;
                        found = true;
                        point = (positions[i] + positions[j]) * 0.5f;
                        angleDeg = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                        speed = pairSpeed;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// **撞库接触推导**（参考实现的质量排序）：在"距最近库段 ≈ r（±0.01）且球速 > 0.1"的球中，
        /// 取 `quality = speed / (0.002 + |dist − r|)` 最大者 ⇒ 垂足 / 段方向角 / 球速。
        /// </summary>
        public static bool FindRailContact(IReadOnlyList<Vector2> positions, IReadOnlyList<Vector2> velocities,
                                           IReadOnlyList<CushionSegment> segments, float ballRadius,
                                           out Vector2 point, out float angleDeg, out float speed)
        {
            point = default;
            angleDeg = 0f;
            speed = 0f;

            if (positions == null || velocities == null || segments == null
                || positions.Count != velocities.Count || segments.Count == 0)
            {
                return false;
            }

            float best = 0f;
            bool found = false;
            for (int i = 0; i < positions.Count; i++)
            {
                float ballSpeed = velocities[i].magnitude;
                if (!(ballSpeed > ContactMinSpeed))
                {
                    continue;
                }

                for (int s = 0; s < segments.Count; s++)
                {
                    Vector2 a = segments[s].a;
                    Vector2 b = segments[s].b;
                    Vector2 ab = b - a;
                    float ab2 = ab.x * ab.x + ab.y * ab.y;
                    if (!(ab2 > 1e-12f))
                    {
                        continue;
                    }

                    float t = Mathf.Clamp01(((positions[i].x - a.x) * ab.x + (positions[i].y - a.y) * ab.y) / ab2);
                    Vector2 q = a + ab * t;
                    float gap = Mathf.Abs((positions[i] - q).magnitude - ballRadius);
                    if (gap > RailDistanceTolerance)
                    {
                        continue;
                    }

                    float quality = ballSpeed / (0.002f + gap);
                    if (quality > best)
                    {
                        best = quality;
                        found = true;
                        point = q;
                        angleDeg = Mathf.Atan2(ab.y, ab.x) * Mathf.Rad2Deg;
                        speed = ballSpeed;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// **最小间隔门**（纯逻辑，判据 `T94` 直接驱动）：首次必过；距上次通过不足
        /// <see cref="IntervalGate.minInterval"/> 秒则拒；超出则过。不读 `Time`（时间由调用方传入）。
        /// </summary>
        public sealed class IntervalGate
        {
            private readonly float _minInterval;
            private float _lastPass = float.NaN;

            public IntervalGate(float minIntervalSeconds)
            {
                _minInterval = minIntervalSeconds;
            }

            public bool TryPass(float nowSeconds)
            {
                if (!float.IsNaN(_lastPass) && nowSeconds - _lastPass < _minInterval)
                {
                    return false;
                }

                _lastPass = nowSeconds;
                return true;
            }

            public void Reset()
            {
                _lastPass = float.NaN;
            }
        }

        // ── 胶水 ─────────────────────────────────────────────────────────

        private void Update()
        {
            if (drive == null)
            {
                return;
            }

            if (!vfxEnabled)
            {
                if (_wasEnabled)
                {
                    ClearAll();   // 关闭瞬间清一次（同参考实现的 setEnabled(false) ⇒ reset()）
                    _wasEnabled = false;
                }

                return;
            }

            if (!_wasEnabled)
            {
                // 重新开启（刚打开）⇒ 计数基准对齐当前值，避免"补播"关闭期间的事件
                SyncCounters();
                _hitGate.Reset();
                _railGate.Reset();
                _wasEnabled = true;
            }

            PoolSimulator sim = drive.Simulator;
            if (sim == null)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (drive.LaunchCount > _lastLaunch)
            {
                _lastLaunch = drive.LaunchCount;
                SpawnStrike();
            }

            int ballBall = sim.BallBallContactCount;
            if (ballBall > _lastBallBall && _hitGate.TryPass(now))
            {
                SpawnHit(sim);
            }

            _lastBallBall = ballBall;

            int segment = sim.TotalContactCount - ballBall;
            if (segment > _lastSegment && _railGate.TryPass(now))
            {
                SpawnRail(sim);
            }

            _lastSegment = segment;

            int pocket = sim.CapturedBallCount;
            if (pocket > _lastPocket)
            {
                SpawnPocket(sim);
            }

            _lastPocket = pocket;

            Advance(Time.deltaTime);
        }

        /// <summary>把四个计数基准对齐到当前值（用于"重新开启"时跳过关闭期间的事件）。</summary>
        private void SyncCounters()
        {
            PoolSimulator sim = drive.Simulator;
            _lastLaunch = drive.LaunchCount;
            _lastBallBall = sim != null ? sim.BallBallContactCount : 0;
            _lastSegment = sim != null ? sim.TotalContactCount - sim.BallBallContactCount : 0;
            _lastPocket = sim != null ? sim.CapturedBallCount : 0;
        }

        private void SpawnStrike()
        {
            Vector2 cue = Vector2.zero;
            PoolSimulator sim = drive.Simulator;
            if (sim != null && sim.Balls.Count > 0)
            {
                cue = sim.Balls[0].position;
            }

            float rad = drive.aimDegrees * Mathf.Deg2Rad;
            var aimDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            // `k`：沿用的力度缩放（`T86` 映射链不动，单点 `StrikePowerScale`）；`launchSpeed`：喂力度分档的初速（§2.1）。
            float power = Mathf.Clamp01(drive.power);
            float k = StrikePowerScale(power);
            float launchSpeed = Mathf.Lerp(drive.minLaunchSpeed, drive.maxLaunchSpeed, power);
            Vector2 center = cue - aimDir * (PoolConstants.BallRadius * 0.85f);
            float backDeg = drive.aimDegrees + 180f;

            // A 核弧（亮、短）
            ApplyIntensity(launchSpeed, StrikeCoreLayer.Radius * k, StrikeCoreLayer.Expansion,
                StrikeCoreLayer.PeakAlpha, StrikeCoreLayer.Duration, ImpactRadiusCap,
                out float cr, out float ce, out float ca, out float cd);
            Spawn(true, center, backDeg, cr, cd, ce, ca, ChalkColor,
                FinalSparkCount(3, TierSparkDelta(launchSpeed)), 0.008f * k);

            // B 晕弧（大、弱、久 —— 无 Bloom 的伪辉光）
            ApplyIntensity(launchSpeed, StrikeHaloLayer.Radius * k, StrikeHaloLayer.Expansion,
                StrikeHaloLayer.PeakAlpha, StrikeHaloLayer.Duration, ImpactRadiusCap,
                out float hr, out float he, out float ha, out float hd);
            Spawn(true, center, backDeg, hr, hd, he, ha, ChalkColor, 0, 0f);
        }

        private void SpawnHit(PoolSimulator sim)
        {
            CollectBallStates(sim);
            if (!FindHitContact(_posBuffer, _velBuffer, PoolConstants.BallRadius,
                                out Vector2 point, out float angle, out float speed))
            {
                return;
            }

            // A 核环（亮、短）
            ApplyIntensity(speed, HitCoreLayer.Radius, HitCoreLayer.Expansion, HitCoreLayer.PeakAlpha,
                HitCoreLayer.Duration, ImpactRadiusCap,
                out float cr, out float ce, out float ca, out float cd);
            Spawn(false, point, angle, cr, cd, ce, ca, ChalkColor,
                FinalSparkCount(3, TierSparkDelta(speed)), 0.010f);

            // B 晕环（大、弱、久）
            ApplyIntensity(speed, HitHaloLayer.Radius, HitHaloLayer.Expansion, HitHaloLayer.PeakAlpha,
                HitHaloLayer.Duration, ImpactRadiusCap,
                out float hr, out float he, out float ha, out float hd);
            Spawn(false, point, angle, hr, hd, he, ha, ChalkColor, 0, 0f);
        }

        private void SpawnRail(PoolSimulator sim)
        {
            if (tableConfig == null || tableConfig.segments == null)
            {
                return;
            }

            CollectBallStates(sim);
            if (!FindRailContact(_posBuffer, _velBuffer, tableConfig.segments, PoolConstants.BallRadius,
                                 out Vector2 point, out float angle, out float speed))
            {
                return;
            }

            // 单弧（安静档；力度越大越"实"，但颜色低饱和 ⇒ 不抢戏）
            ApplyIntensity(speed, RailLayer.Radius, RailLayer.Expansion, RailLayer.PeakAlpha,
                RailLayer.Duration, ImpactRadiusCap,
                out float r, out float e, out float a, out float d);
            Spawn(true, point, angle, r, d, e, a, RailColor, 0, 0f);
        }

        private void SpawnPocket(PoolSimulator sim)
        {
            if (tableConfig == null || tableConfig.pockets == null)
            {
                return;
            }

            // 找"刚离台"的球（本帧事件 ⇒ 在离台球里取第一个），推到最近的袋喉中心并向袋内偏 0.013。
            Vector2 anchor = Vector2.zero;
            bool hasAnchor = false;
            IReadOnlyList<BallState> balls = sim.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                if (!sim.IsBallInPlay(i))
                {
                    anchor = balls[i].position;
                    hasAnchor = true;
                    break;
                }
            }

            if (!hasAnchor)
            {
                return;
            }

            float bestDist = float.MaxValue;
            Vector2 center = anchor;
            float inwardDeg = 0f;
            for (int p = 0; p < tableConfig.pockets.Count; p++)
            {
                Pocket pocket = tableConfig.GetPocket(p);
                float d = (pocket.throatCenter - anchor).magnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    Vector2 tn = pocket.throatNormal;
                    center = pocket.throatCenter + tn * PocketInset;
                    inwardDeg = Mathf.Atan2(tn.y, tn.x) * Mathf.Rad2Deg;
                }
            }

            // pocket **不参与力度分档** ⇒ `sizeMul = expansionMul = 1`；但仍过 §2.3 **同一**夹取
            //（上限作用于"含扩张的最终半径" ⇒ 与撞击类同口径；§1.3 的 44 mm）。基值本就在上限内、
            // 故这是**结构性一致**（不是行为改变）：若将来调大 pocket 基半径，夹取在此生效。
            float outerRadius = FinalRadius(PocketOuterLayer.Radius, 1f, PocketOuterLayer.Expansion, 1f, PocketRadiusCap);
            float coreRadius = FinalRadius(PocketCoreLayer.Radius, 1f, PocketCoreLayer.Expansion, 1f, PocketRadiusCap);

            // **吸（Roll-in）**：捕获当帧 —— 爆外环（brass）+ 3 火花沿袋口法向飞散 ⇒ "球被吸进洞口"。
            Spawn(false, center, inwardDeg, outerRadius, PocketOuterLayer.Duration,
                PocketOuterLayer.Expansion, PocketOuterLayer.PeakAlpha, BrassColor, 3, 0.013f);

            // **爆（Pop）**：`PocketBurstDelaySeconds` 后（球到达洞口、开始落下）—— 爆内核（brassCore，暖白，全屏最亮）。
            // 延迟让"爆点"与"落球"严格同步；内核半径 15 mm < 球半径 ⇒ 环在球底、不盖号码。
            Spawn(false, center, inwardDeg, coreRadius, PocketCoreLayer.Duration,
                PocketCoreLayer.Expansion, PocketCoreLayer.PeakAlpha, BrassCoreColor, 0, 0f,
                spawnDelay: PocketBurstDelaySeconds);
        }

        /// <summary>取某速度所处档位的**火花增减**（纯函数；供无需完整 `ApplyIntensity` 的火花层用）。</summary>
        private static int TierSparkDelta(float speed)
        {
            TierMultipliers(ClassifyIntensity(speed), out _, out _, out _, out _, out int delta);
            return delta;
        }

        private void CollectBallStates(PoolSimulator sim)
        {
            _posBuffer.Clear();
            _velBuffer.Clear();
            IReadOnlyList<BallState> balls = sim.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                if (!sim.IsBallInPlay(i))
                {
                    continue;
                }

                _posBuffer.Add(balls[i].position);
                _velBuffer.Add(balls[i].velocity);
            }
        }

        private void Spawn(bool arc, Vector2 center, float aimDir, float radius, float duration,
                           float expansion, float opacity, Color color, int sparks, float sparkLength,
                           float spawnDelay = 0f)
        {
            EnsurePool();
            VfxSlot slot = null;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].active)
                {
                    slot = _slots[i];
                    break;
                }
            }

            if (slot == null)
            {
                DroppedCount++;   // 槽满丢弃（同参考实现）
                return;
            }

            slot.active = true;
            slot.age = -spawnDelay;   // `spawnDelay > 0` ⇒ 本层延后 `spawnDelay` 秒才生成（`age` 先到 0）
            slot.duration = duration;
            slot.radius = radius;
            slot.expansion = expansion;
            slot.opacity = opacity;
            slot.color = color;
            slot.isArc = arc;
            slot.directionDeg = aimDir;
            slot.sparkVisible = sparks;
            slot.sparkLength = sparkLength;
            slot.center = center;
            slot.spawnDelay = spawnDelay;

            if (spawnDelay <= 0f)
            {
                WriteSlot(slot, 0f);
            }
        }

        private void Advance(float dt)
        {
            if (!(dt > 0f))
            {
                return;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                VfxSlot slot = _slots[i];
                if (!slot.active)
                {
                    continue;
                }

                slot.age += dt;
                if (slot.age < 0f)
                {
                    continue;   // 延迟未到（入袋"爆内核"）：本层尚未生成 ⇒ 不写点、不激活
                }

                if (slot.age >= slot.duration)
                {
                    slot.active = false;
                    slot.ring.gameObject.SetActive(false);
                    for (int s = 0; s < slot.sparks.Length; s++)
                    {
                        slot.sparks[s].gameObject.SetActive(false);
                    }

                    continue;
                }

                WriteSlot(slot, slot.age / slot.duration);
            }
        }

        private void WriteSlot(VfxSlot slot, float progress)
        {
            VfxPose(progress, slot.radius, slot.expansion, slot.opacity, out float scale, out float alpha);

            LineRenderer ring = slot.ring;
            ring.gameObject.SetActive(true);
            Color color = slot.color;
            color.a = alpha;

            _ringBuffer.Clear();
            if (slot.isArc)
            {
                // 弧：以 directionDeg 为中心 ±60°（16 段），贴接触法向。
                float centerRad = slot.directionDeg * Mathf.Deg2Rad;
                for (int i = 0; i < ArcSegments; i++)
                {
                    float t = i / (float)(ArcSegments - 1);
                    float angle = centerRad + Mathf.Lerp(-Mathf.PI / 3f, Mathf.PI / 3f, t);
                    _ringBuffer.Add(PointOnRing(slot.center, scale, angle));
                }
            }
            else
            {
                for (int i = 0; i < RingSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / RingSegments;
                    _ringBuffer.Add(PointOnRing(slot.center, scale, angle));
                }
            }

            WriteLine(ring, _ringBuffer, color, !slot.isArc);

            for (int s = 0; s < slot.sparks.Length; s++)
            {
                LineRenderer spark = slot.sparks[s];
                bool used = s < slot.sparkVisible;
                if (spark.gameObject.activeSelf != used)
                {
                    spark.gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                float sparkAngle = slot.directionDeg * Mathf.Deg2Rad
                                   + (s - (slot.sparkVisible - 1) * 0.5f) * SparkSpreadRad;
                float startRadius = scale * SparkStartFactor + scale * SparkTravelFactor * progress;
                Color sparkColor = slot.color;
                sparkColor.a = alpha;

                Vector2 tip = slot.center + new Vector2(Mathf.Cos(sparkAngle), Mathf.Sin(sparkAngle)) * startRadius;
                Vector2 tail = tip + new Vector2(Mathf.Cos(sparkAngle), Mathf.Sin(sparkAngle)) * slot.sparkLength;
                _sparkBuffer.Clear();
                _sparkBuffer.Add(AimAssistView.MarkerWorldPoint(playfieldRoot, tip));
                _sparkBuffer.Add(AimAssistView.MarkerWorldPoint(playfieldRoot, tail));
                WriteLine(spark, _sparkBuffer, sparkColor, false);
            }
        }

        /// <summary>
        /// **写一条线的单点**（全类唯一的 `SetPosition` 调用处）。
        ///
        /// ── 顺序是硬契约：`loop` → `positionCount` → **颜色** → 顶点 ──────────────────
        /// 两条实测教训（都来自用户实机反馈，2026-10-15）：
        /// ① **越界**：`LineRenderer.SetPosition` 索引越界时**只打错误日志、不抛异常**
        ///    （`LineRenderer.SetPosition index out of bounds!`）。原实现「先写 32 个点、循环后才设
        ///    `positionCount = 32`」，而环出厂 `positionCount = 0` ⇒ 每个点都越界；槽位在
        ///    「环(32)/弧(16)」复用时数量**由大变小** ⇒ 反复越界刷屏。先设数量即消除。
        /// ② **颜色丢失**：原实现把 `startColor/endColor` 设在**改 `positionCount` 之前**，
        ///    而 `positionCount` 的赋值会重建顶点缓冲 ⇒ 顶点色有被重置为白的风险（用户实机反馈
        ///    "特效都是白的"）。**把设色排在数量之后、顶点之前**，使顶点色在写入时必然被
        ///    `SetPosition` 按 `startColor/endColor` 落到每个顶点上 —— 两种假设下都不会丢色。
        ///
        /// 判据 `T99` 直接驱动本函数（含 `positionCount = 0` 的出厂态与颜色回读）。
        /// </summary>
        public static void WriteLine(LineRenderer line, IReadOnlyList<Vector3> points, Color color, bool loop)
        {
            line.loop = loop;
            line.positionCount = points.Count;
            line.startColor = color;
            line.endColor = color;
            for (int i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i]);
            }
        }

        private Vector3 PointOnRing(Vector2 center, float radius, float angle)
        {
            Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            return AimAssistView.MarkerWorldPoint(playfieldRoot, point);
        }

        private void ClearAll()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                VfxSlot slot = _slots[i];
                slot.active = false;
                slot.ring.gameObject.SetActive(false);
                for (int s = 0; s < slot.sparks.Length; s++)
                {
                    slot.sparks[s].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>启动时**一次性**预建全部槽位（运行期不再 Instantiate——排期「不抖动」）。</summary>
        private void EnsurePool()
        {
            if (_slots.Count > 0 || playfieldRoot == null)
            {
                return;
            }

            if (_lineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _lineMaterial = new Material(shader);
                }
            }

            for (int i = 0; i < SlotCapacity; i++)
            {
                var slot = new VfxSlot
                {
                    // 环按**最大段数**预置（弧只有 16 段）：即使将来有人绕过 `WriteLine` 直写顶点，
                    // 出厂态也不会是"容量 0 却写 32 个点"。
                    ring = CreateLine("PoolVfxRing_" + i, RingSegments),
                    sparks = new LineRenderer[SparkCount],
                };

                for (int s = 0; s < SparkCount; s++)
                {
                    slot.sparks[s] = CreateLine("PoolVfxSpark_" + i + "_" + s, 2);
                }

                _slots.Add(slot);
            }

            ClearAll();
        }

        private LineRenderer CreateLine(string lineName, int positions)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(playfieldRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = positions;
            // 线宽乘显示缩放：`useWorldSpace = true` 时 `startWidth` 是**世界单位**，不受父缩放影响
            // ⇒ 必须显式乘（与瞄准线同口径，`AimAssistView.DrawScale` 是同一出口）。
            float width = LineWidth * AimAssistView.DrawScale(playfieldRoot);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.numCapVertices = 0;
            if (_lineMaterial != null)
            {
                lr.sharedMaterial = _lineMaterial;
            }

            go.SetActive(false);
            return lr;
        }
    }
}
