using System.Collections.Generic;
using Billiards.Core;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **瞄准辅助的表现层**：纯几何（可判据驱动）+ Scene 预览 + Game 视图虚线。
    ///
    /// ── 两条"单点"铁律（重犯即回归 `T71` 修过的缺陷）────────────────────────
    ///  1. **绘制坐标只经显示层**：一律 <see cref="DrawPoint"/>（→ 既有包装 <see cref="CushionGizmo.DisplayPoint"/>
    ///     → <see cref="PlayfieldView.DisplayFromCanonical"/>）。**不得**直画规范世界坐标、**不得**手写固定倍数。
    ///  2. **世界方向只经唯一转换点**：一律 <see cref="WorldDirection"/>（→ <see cref="PoolGeometry.PhysicsDirectionToUnity"/>）。
    ///     **不得**在别处手写 `(−x, −z)`（第二处转换点违铁律 16）。
    ///
    /// ── 空间口径（**必读**）──────────────────────────────────────────────────
    /// <see cref="BuildDashSegments"/> 的 `from` / `to` / `dashLength` / `gapLength` 全在**同一空间**（本视图用
    /// **规范物理平面**）；其**世界长度** = 规范长度 × <see cref="DrawScale"/>。
    /// ⇒ `T80c` 断言的"世界段长 == 规范段长 × 显示尺度"由此成立。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · 无法捕获 `Gizmos.DrawLine` / `LineRenderer` 的**实际**顶点（引擎内部）⇒ "是否真的调用了 `DrawPoint`"
    ///   由源码守卫（无字面量倍数）+ 复核 `grep` 覆盖。
    /// · **观感**（虚线好不好看）不作验收（`R3-4`）；判据只判"非零间隙 + 段数闭式 + 随尺度 + 屏幕下限"。
    /// · `LineRenderer` 渲染只在 **PlayMode** 生效（`Application.isPlaying` 守卫）⇒ EditMode 判据**不产生**
    ///   场景对象（不污染场景快照）。
    /// </summary>
    [AddComponentMenu("Billiards/Aim Assist View")]
    public sealed class AimAssistView : MonoBehaviour
    {
        /// <summary>
        /// 虚线段长（**规范米**，乙-系数选定）。世界段长 = 本值 × <see cref="DrawScale"/>（1.2 根 ⇒ 0.072 m）。
        /// </summary>
        public const float PredictionDashLength = 0.06f;

        /// <summary>
        /// 虚线间隙（**规范米**，乙-系数选定）。必须 `> 0`（`T80a`：间隙为 0 即实线 = "糊"的可判定反面）。
        /// 世界间隙 = 本值 × <see cref="DrawScale"/>（1.2 根 ⇒ 0.030 m）≫ <see cref="MinGapWorld"/>。
        /// </summary>
        public const float PredictionGapLength = 0.025f;

        /// <summary>线宽（**规范米**）。世界宽 = 本值 × <see cref="DrawScale"/>。</summary>
        public const float PredictionLineWidth = 0.006f;

        /// <summary>目标球出射线段长度（**规范米**，与 `PhysicsTestDrive.aimIndicatorLength = 0.55` 同量级）。</summary>
        public const float TargetLineLength = 0.55f;

        /// <summary>
        /// **屏幕可辨下限**（**世界米**，乙-系数选定）= 16:9 基准取景、**1920 px 宽**下 ≥1 像素所对应的世界米数。
        ///
        /// ⚠ **实测定值**：在灰盒 16:9 基准（相机 FOV 52）桌面中心处，实测
        /// `viewport/米 = 0.310438871` ⇒ `596.04 px/米`（1920 px）⇒ **1 px = 0.00167773 m** ⇒ 本值取 **0.0017 m**。
        /// 断言形式（`T80d`）：`PredictionGapLength × DrawScale(root) ≥ MinGapWorld`。
        /// </summary>
        public const float MinGapWorld = 0.0017f;

        /// <summary>预测结果来源（只读）。为空 ⇒ 不绘制。</summary>
        [Tooltip("预测结果来源 AimAssistController（只读）。为空 ⇒ 不绘制。")]
        public AimAssistController controller;

        /// <summary>可视野缩放根（`PlayfieldRoot`）。为空 ⇒ 绘制退化为 scale 1（与 `CushionGizmo` 同口径）。</summary>
        [Tooltip("可视野缩放根（PlayfieldRoot）。为空 ⇒ 绘制退化为 scale 1。")]
        public Transform playfieldRoot;

        private static readonly Color PathColor = new Color(0.35f, 1.00f, 0.55f, 1f);
        private static readonly Color GhostColor = new Color(1.00f, 0.85f, 0.35f, 1f);
        private static readonly Color TargetColor = new Color(1.00f, 0.45f, 0.25f, 1f);

        private readonly List<LineRenderer> _dashPool = new List<LineRenderer>();
        private Material _runtimeLineMaterial;

        // ── 纯几何 / 单点（判据直接驱动，不必真画）────────────────────────────────

        /// <summary>
        /// **把一条线段切成虚线**：段数 `n = max(1, ceil(L / (dashLength + gapLength)))`；
        /// 每段长度 `≤ dashLength`，除**末段**外每段**恰为** `dashLength`。
        /// `from` / `to` / 两个长度参数**同空间**（本工程用规范米）。
        /// </summary>
        public static IReadOnlyList<(Vector2 a, Vector2 b)> BuildDashSegments(
            Vector2 from, Vector2 to, float dashLength, float gapLength)
        {
            var segments = new List<(Vector2 a, Vector2 b)>();
            if (!(dashLength > 0f))
            {
                return segments;
            }

            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (!(length > 0f))
            {
                return segments;
            }

            float gap = gapLength > 0f ? gapLength : 0f;
            Vector2 dir = delta / length;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / (dashLength + gap)));

            float cursor = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = cursor;
                float b = Mathf.Min(cursor + dashLength, length);
                segments.Add((from + dir * a, from + dir * b));
                cursor += dashLength + gap;
            }

            return segments;
        }

        /// <summary>**规范世界点 → 绘制用（显示世界）点**：委托 <see cref="CushionGizmo.DisplayPoint"/>（唯一落点）。</summary>
        public static Vector3 DrawPoint(Transform playfieldRoot, Vector3 canonicalWorldPoint)
        {
            return CushionGizmo.DisplayPoint(playfieldRoot, canonicalWorldPoint);
        }

        /// <summary>**显示层尺度因子**：委托 <see cref="CushionGizmo.DisplayScale"/>（由显示映射导出，**不硬编码**）。</summary>
        public static float DrawScale(Transform playfieldRoot)
        {
            return CushionGizmo.DisplayScale(playfieldRoot);
        }

        /// <summary>**物理平面方向 → 世界方向**：委托 <see cref="PoolGeometry.PhysicsDirectionToUnity"/>（唯一方向入口）。</summary>
        public static Vector3 WorldDirection(Vector2 physicsDirection)
        {
            return PoolGeometry.PhysicsDirectionToUnity(physicsDirection);
        }

        // ── Scene 预览（只读；不产生任何对象）────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (controller == null)
            {
                return;
            }

            ShotPrediction prediction = controller.Prediction;
            IReadOnlyList<Vector2> path = prediction.CueWorldPath;

            if (path != null && path.Count >= 2)
            {
                Gizmos.color = PathColor;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    Vector2 a = path[i];
                    Vector2 b = path[i + 1];
                    foreach ((Vector2 sa, Vector2 sb) in BuildDashSegments(a, b, PredictionDashLength, PredictionGapLength))
                    {
                        Gizmos.DrawLine(
                            DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(sa)),
                            DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(sb)));
                    }
                }
            }

            if (!prediction.HasFirstContact)
            {
                return;
            }

            float scale = DrawScale(playfieldRoot);
            Vector3 ghostWorld = DrawPoint(playfieldRoot, PoolGeometry.PhysicsBallToUnity(prediction.GhostBall));

            Gizmos.color = GhostColor;
            Gizmos.DrawWireSphere(ghostWorld, PoolConstants.BallRadius * scale);

            if (prediction.TargetOutgoingDirection != Vector2.zero)
            {
                Gizmos.color = TargetColor;
                Gizmos.DrawLine(ghostWorld,
                    ghostWorld + WorldDirection(prediction.TargetOutgoingDirection) * (TargetLineLength * scale));
            }
        }

        // ── Game 视图虚线（PlayMode；EditMode 不产生对象 ⇒ 不污染场景）────────────

        private void LateUpdate()
        {
            if (controller == null || !Application.isPlaying)
            {
                return;
            }

            ShotPrediction prediction = controller.Prediction;
            IReadOnlyList<Vector2> path = prediction.CueWorldPath;

            var segments = new List<(Vector2 a, Vector2 b)>();
            if (path != null && path.Count >= 2)
            {
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    segments.AddRange(BuildDashSegments(path[i], path[i + 1], PredictionDashLength, PredictionGapLength));
                }
            }

            SyncRenderers(segments);
        }

        /// <summary>
        /// 把虚线渲染为 `LineRenderer` 池：挂在 <paramref name="playfieldRoot"/> 下、`useWorldSpace = false`
        /// ⇒ 顶点用**局部坐标 = 规范坐标**，天然继承根的显示缩放（等价于过 `DisplayFromCanonical`）。
        /// </summary>
        private void SyncRenderers(IReadOnlyList<(Vector2 a, Vector2 b)> segments)
        {
            if (playfieldRoot == null)
            {
                return;
            }

            if (_runtimeLineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _runtimeLineMaterial = new Material(shader);
                }
            }

            while (_dashPool.Count < segments.Count)
            {
                var go = new GameObject("AimAssistDash_" + _dashPool.Count);
                go.transform.SetParent(playfieldRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.startWidth = PredictionLineWidth;
                lr.endWidth = PredictionLineWidth;
                lr.numCapVertices = 0;
                if (_runtimeLineMaterial != null)
                {
                    lr.sharedMaterial = _runtimeLineMaterial;
                }

                _dashPool.Add(lr);
            }

            for (int i = 0; i < _dashPool.Count; i++)
            {
                LineRenderer lr = _dashPool[i];
                bool used = i < segments.Count;
                if (lr.gameObject.activeSelf != used)
                {
                    lr.gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                lr.SetPosition(0, new Vector3(segments[i].a.x, PoolConstants.ClothHeightY, segments[i].a.y));
                lr.SetPosition(1, new Vector3(segments[i].b.x, PoolConstants.ClothHeightY, segments[i].b.y));
            }
        }
    }
}
