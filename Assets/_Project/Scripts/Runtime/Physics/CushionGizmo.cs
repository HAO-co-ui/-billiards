using System.Collections.Generic;
using Billiards.Core;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 库边碰撞几何（<see cref="PoolConstants.CushionSegmentCount"/> 段）与当前球状态的 **Gizmo 可视化**。
    ///
    /// ── 契约（四条，均可被评审逐条对照）────────────────────────────────────
    ///  1. **只画不算**：本类在 <c>OnDrawGizmos</c> 内**不做任何物理推进、不写任何状态**。
    ///     球状态只从 <see cref="Simulator"/> 读出后绘制。Gizmo 是纯只读视图。
    ///  2. **坐标只经唯一转换点**：物理平面 → Unity 世界一律走
    ///     <see cref="PoolGeometry.PhysicsToUnity(Vector2, float)"/>；矢量走
    ///     <see cref="PhysicsVectorToUnity"/>（由同一转换点导出，不另写变换）。工程标准 R5-2。
    ///  3. **按 rail 分组着色**：同一条链同色。颜色按 rail 名**首次出现顺序**从固定调色板取用
    ///     （确定性，不使用 <c>Dictionary</c> 迭代序）。
    ///  4. **绘制坐标必须过显示层**（Day 8 切片 A 之后新增）：所有库边线段 / 端点标记 / 法向刻度 /
    ///     球线框的坐标，一律经 <see cref="PlayfieldView.DisplayFromCanonical"/>（**唯一**显示映射点）映射到
    ///     **显示世界** ⇒ 与挂在 `PlayfieldRoot`（`localScale = PoolConstants.PlayfieldVisualScale`）下的
    ///     球可视物保持同一尺度。**不得**直接拿 <see cref="PoolGeometry"/> 的**规范世界**坐标去画
    ///     （那会让线框停在 1.0× 而显示球是 1.2× ⇒ 错位）；**不得**在本类手写固定倍数
    ///     （根节点带平移/旋转时会静默算错）。`playfieldRoot == null` ⇒ 退化为 scale = 1
    ///     （警戒色 + 一次性告警），不抛异常、不静默画在错位置。
    ///
    /// 段几何以 <see cref="TableConfig.segments"/> 为唯一真源（82 段 = 6 段直库 + 76 段袋角侧壁）。
    /// </summary>
    [AddComponentMenu("Billiards/Cushion Gizmo")]
    public sealed class CushionGizmo : MonoBehaviour
    {
        /// <summary>球桌碰撞几何真源。为空时本组件不绘制任何库边。</summary>
        [Tooltip("球桌碰撞几何真源（TableConfig.asset）。为空时不绘制库边。")]
        public TableConfig tableConfig;

        /// <summary>
        /// **可视野缩放根**（表现层等比缩放，`PoolConstants.PlayfieldVisualScale`）。
        /// 所有绘制坐标都经本 Transform 映射到**显示世界**（映射的唯一实现是 <see cref="PlayfieldView"/>）。
        /// **为空** ⇒ 绘制退化为 `scale = 1`（警戒色 + 一次性告警），与 1.2 显示层的球错位。
        /// </summary>
        [Tooltip("可视野缩放根（PlayfieldRoot）。为空时绘制退化为 scale = 1。")]
        public Transform playfieldRoot;

        /// <summary>
        /// 可选的物理内核引用，**仅用于读取球状态**。运行时由
        /// <see cref="PhysicsTestDrive"/> 注入；编辑器非运行态下为 <c>null</c>（此时只画库边）。
        /// </summary>
        [Tooltip("可选：运行时由 PhysicsTestDrive 注入，仅用于读取球位置/速度。")]
        [System.NonSerialized]
        public PoolSimulator Simulator;

        /// <summary>内法向短刻度的长度（米）。仅可视化用。</summary>
        public const float NormalTickLength = 0.05f;

        /// <summary>段端点小球标记的半径（米）。仅可视化用。</summary>
        public const float EndpointMarkerRadius = 0.004f;

        /// <summary>速度矢量绘制比例（世界米 / (m·s⁻¹)）。仅可视化用。</summary>
        public const float VelocityVectorScale = 0.1f;

        /// <summary>`playfieldRoot` 未接线时的**警戒绘制色**（橙红）：提示"绘制坐标未过显示层"。</summary>
        private static readonly Color MissingRootColor = new Color(1f, 0.35f, 0.05f, 1f);

        /// <summary>退化告警是否已打过（**只报一次**，避免每帧刷屏）。</summary>
        private bool _warnedMissingPlayfieldRoot;

        /// <summary>
        /// rail 分组调色板。本工程实测 **6 条链**（end_negative / end_positive /
        /// long_negative_negative / long_negative_positive / long_positive_negative / long_positive_positive），
        /// 调色板留 2 个余量以便数据未来扩展时不会立刻回绕。
        /// </summary>
        private static readonly Color[] RailPalette =
        {
            new Color(0.25f, 0.70f, 1.00f), // 蓝
            new Color(1.00f, 0.45f, 0.25f), // 橙
            new Color(0.45f, 1.00f, 0.45f), // 绿
            new Color(1.00f, 0.90f, 0.25f), // 黄
            new Color(0.85f, 0.45f, 1.00f), // 紫
            new Color(0.25f, 1.00f, 0.90f), // 青
            new Color(1.00f, 0.50f, 0.75f), // 粉
            new Color(0.80f, 0.80f, 0.80f), // 灰
        };

        private TableConfig _cachedTable;
        private Color[] _cachedCorners;
        private int _cachedSegmentCount = -1;

        /// <summary>运行时注入物理内核（由 <see cref="PhysicsTestDrive"/> 在初始化时调用）。</summary>
        public void SetSimulator(PoolSimulator simulator)
        {
            Simulator = simulator;
        }

        /// <summary>
        /// 物理平面上的**矢量**（速度方向）→ Unity 世界方向。
        /// 由唯一转换点导出：<c>ToUnity(v) − ToUnity(0)</c>，因此不会与位置映射分叉。
        /// </summary>
        public static Vector3 PhysicsVectorToUnity(Vector2 vector)
        {
            Vector3 origin = PoolGeometry.PhysicsToUnity(Vector2.zero, PoolConstants.ClothHeightY);
            return PoolGeometry.PhysicsToUnity(vector, PoolConstants.ClothHeightY) - origin;
        }

        // ── 显示层映射（绘制坐标的**唯一落点**；抽成纯函数以便判据直接驱动，不必真画）──────

        /// <summary>
        /// **规范世界点 → 绘制用（显示世界）点**。经 <see cref="PlayfieldView.DisplayFromCanonical"/>
        /// （唯一显示映射点）；<paramref name="playfieldRoot"/> 为 <c>null</c> ⇒ 退化为**原样返回**
        /// （等价 `scale = 1`），不抛异常。
        ///
        /// ⚠ 纯函数、无副作用 ⇒ 判据可直接断言"绘制点 == 显示映射点"，无需运行 <c>Gizmos</c>。
        /// </summary>
        public static Vector3 DisplayPoint(Transform playfieldRoot, Vector3 canonicalWorldPoint)
        {
            return playfieldRoot == null
                ? canonicalWorldPoint
                : PlayfieldView.DisplayFromCanonical(playfieldRoot, canonicalWorldPoint);
        }

        /// <summary>
        /// **显示层尺度因子**（半径 / 刻度这类"长度"要乘的量）。
        /// 由**唯一显示映射点**导出：<c>|DisplayFromCanonical(root, +X) − DisplayFromCanonical(root, 0)|</c>
        /// —— 对等比缩放恒等于该尺度，且**自动含**平移（相减抵消）与旋转（旋转保长）。
        /// **不硬编码** <see cref="PoolConstants.PlayfieldVisualScale"/>。
        /// <paramref name="playfieldRoot"/> 为 <c>null</c> ⇒ `1f`（退化）。
        /// </summary>
        public static float DisplayScale(Transform playfieldRoot)
        {
            if (playfieldRoot == null)
            {
                return 1f;
            }

            Vector3 origin = PlayfieldView.DisplayFromCanonical(playfieldRoot, Vector3.zero);
            Vector3 unitX = PlayfieldView.DisplayFromCanonical(playfieldRoot, Vector3.right);
            return (unitX - origin).magnitude;
        }

        private void OnDrawGizmos()
        {
            if (tableConfig == null || tableConfig.segments == null || tableConfig.segments.Count == 0)
            {
                return;
            }

            // 退化：playfieldRoot 未接线 ⇒ 只报一次（不抛异常、不每帧刷屏），并按警戒色绘制以示"未过显示层"。
            if (playfieldRoot == null && !_warnedMissingPlayfieldRoot)
            {
                _warnedMissingPlayfieldRoot = true;
                Debug.LogWarning("CushionGizmo：playfieldRoot 未接线 ⇒ 绘制退化为 scale = 1，"
                                 + "会与 PlayfieldRoot 下的球可视物（显示层尺度）错位。请在 GreyboxSceneSetup 里接线。", this);
            }

            EnsureRailColors();

            float displayScale = DisplayScale(playfieldRoot);
            float markerRadius = EndpointMarkerRadius * displayScale;
            float tickLength = NormalTickLength * displayScale;

            // 台心也过显示层（法向刻度的内向判定用）。
            Vector3 center = DisplayPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(Vector2.zero, PoolConstants.ClothHeightY));

            for (int i = 0; i < tableConfig.segments.Count; i++)
            {
                CushionSegment s = tableConfig.segments[i];
                Vector3 a = DisplayPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(s.a, PoolConstants.ClothHeightY));
                Vector3 b = DisplayPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(s.b, PoolConstants.ClothHeightY));

                Gizmos.color = playfieldRoot == null ? MissingRootColor : _cachedCorners[i];

                // 段本体（显示世界坐标）
                Gizmos.DrawLine(a, b);

                // 段端点小球标记
                Gizmos.DrawWireSphere(a, markerRadius);
                Gizmos.DrawWireSphere(b, markerRadius);

                // 内法向短刻度：取段的水平垂线中"指向台心 (0,0)"的那一侧。
                // 选点依据：本 spec §2.2 结论 B 已证「球恒在内侧」不成立，故**不引入逐段朝向数据**；
                // 这里只是可视化辅助（台心侧 ≡ 台面内侧），不参与任何物理判定。
                Vector3 dir = b - a;
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                float len = perp.magnitude;
                if (len > 1e-9f)
                {
                    perp /= len;
                    Vector3 mid = (a + b) * 0.5f;
                    if (Vector3.Dot(perp, center - mid) < 0f)
                    {
                        perp = -perp;
                    }

                    Gizmos.DrawLine(mid, mid + perp * tickLength);
                }
            }

            DrawBalls(displayScale);
        }

        /// <summary>按 rail 首次出现顺序分配颜色，结果按段索引缓存（仅在球桌或段数变化时重建）。</summary>
        private void EnsureRailColors()
        {
            int count = tableConfig.segments.Count;
            if (_cachedTable == tableConfig && _cachedSegmentCount == count && _cachedCorners != null)
            {
                return;
            }

            var order = new List<string>(8);
            var corners = new Color[count];
            for (int i = 0; i < count; i++)
            {
                string rail = tableConfig.segments[i].rail ?? "<null>";
                int index = order.IndexOf(rail);
                if (index < 0)
                {
                    order.Add(rail);
                    index = order.Count - 1;
                }

                corners[i] = RailPalette[index % RailPalette.Length];
            }

            _cachedTable = tableConfig;
            _cachedSegmentCount = count;
            _cachedCorners = corners;
        }

        private void DrawBalls(float displayScale)
        {
            if (Simulator == null)
            {
                return;
            }

            IReadOnlyList<BallState> balls = Simulator.Balls;
            if (balls == null)
            {
                return;
            }

            // 球线框的**显示半径** = 规范半径 × 显示尺度（与挂在 PlayfieldRoot 下的球网格同尺度）。
            float displayRadius = PoolConstants.BallRadius * displayScale;

            Gizmos.color = new Color(1f, 0.96f, 0.62f, 1f);
            for (int i = 0; i < balls.Count; i++)
            {
                BallState ball = balls[i];
                Vector3 p = DisplayPoint(playfieldRoot, PoolGeometry.PhysicsBallToUnity(ball.position));

                Gizmos.DrawWireSphere(p, displayRadius);

                // 速度矢量同样按显示尺度放大，避免与已放大的位置/半径不成比例。
                Vector3 v = PhysicsVectorToUnity(ball.velocity) * displayScale;
                if (v.sqrMagnitude > 1e-12f)
                {
                    Gizmos.DrawLine(p, p + v * VelocityVectorScale);
                }
            }
        }
    }
}
