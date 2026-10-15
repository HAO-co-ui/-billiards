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
        /// 虚线段长（**规范米**）。世界段长 = 本值 × <see cref="DrawScale"/>。
        /// 观感对齐：段长加大、间隙缩小 ⇒ Game 视图接近《8 Ball Pool》的连续瞄准线，
        /// 同时保持 <c>gap &gt; 0</c>（`T80a`）与 <c>gap × scale ≥ MinGapWorld</c>（`T80d`）仍可判定。
        /// </summary>
        public const float PredictionDashLength = 0.25f;

        /// <summary>
        /// 虚线间隙（**规范米**）。必须 `> 0`（`T80a`：间隙为 0 即实线 = "糊"的可判定反面）。
        /// 世界间隙 = 本值 × <see cref="DrawScale"/>（1.2 根 ⇒ 0.0024 m）≥ <see cref="MinGapWorld"/>。
        /// </summary>
        public const float PredictionGapLength = 0.002f;

        /// <summary>线宽（**规范米**）。世界宽 = 本值 × <see cref="DrawScale"/>。</summary>
        public const float PredictionLineWidth = 0.006f;

        /// <summary>目标球出射线段长度（**规范米**，与 `PhysicsTestDrive.aimIndicatorLength = 0.55` 同量级）。</summary>
        public const float TargetLineLength = 0.55f;

        /// <summary>白球偏转线长度（**规范米**）。比目标球出射线短（主次分明）。</summary>
        public const float CueDeflectLineLength = 0.35f;

        /// <summary>幽灵球空心圆的圆周采样段数（平滑度）。</summary>
        public const int GhostCircleSegments = 32;

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

        private static readonly Color PathColor = new Color(1.00f, 1.00f, 1.00f, 0.95f);
        private static readonly Color GhostColor = new Color(1.00f, 1.00f, 1.00f, 0.60f);
        private static readonly Color TargetColor = new Color(1.00f, 0.85f, 0.25f, 0.95f);
        private static readonly Color CueDeflectColor = new Color(0.55f, 0.85f, 1.00f, 0.80f);

        private readonly List<LineRenderer> _dashPool = new List<LineRenderer>();
        private LineRenderer _ghostCircle;
        private LineRenderer _targetLine;
        private LineRenderer _cueDeflectLine;
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

        /// <summary>
        /// **Game 视图标记线顶点（唯一口径）**：规范物理点 → 世界坐标 =
        /// <see cref="PoolGeometry.PhysicsBallToUnity"/>（**X/Z 双取反** `(−px, 球心高, −py)`）→ <see cref="DrawPoint"/>（显示层缩放）。
        /// 与 Scene Gizmos 的画线**逐字同口径**；高度取**球心**而非台呢 ⇒ 与球杆（球心高度）同层，
        /// 倾斜相机下无线杆视差（用户实测：台呢高度的线与球心高度的杆存在可见错位）。
        /// ⚠ 规范 y_physics 与世界 Z **符号相反**——直写 `(px, h, +py)` 会把线画到绕桌面中心旋转 180° 的
        /// 镜像位置上（用户实测截图定位；`T85` 锁死：手写双取反 + 根变换做独立期望比对）。
        /// </summary>
        public static Vector3 MarkerWorldPoint(Transform playfieldRoot, Vector2 physicsPoint)
        {
            return DrawPoint(playfieldRoot, PoolGeometry.PhysicsBallToUnity(physicsPoint));
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

            if (prediction.CueOutgoingDirection != Vector2.zero)
            {
                Gizmos.color = CueDeflectColor;
                Gizmos.DrawLine(ghostWorld,
                    ghostWorld + WorldDirection(prediction.CueOutgoingDirection) * (CueDeflectLineLength * scale));
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
            SyncContactMarkers(prediction);
        }

        /// <summary>
        /// 把虚线渲染为 `LineRenderer` 池：**世界空间顶点 = <see cref="MarkerWorldPoint"/>**，
        /// 与 Scene Gizmos 的画线（`DrawPoint(root, PoolGeometry.PhysicsToUnity(p))`）**逐字同口径**。
        /// ⚠ 历史缺陷（已修，勿回改）：曾用 `useWorldSpace = false` + 局部坐标直写 `(px, h, +py)`，
        /// 漏掉 `PoolGeometry` 的 **X/Z 双取反** ⇒ Game 视图的线画在真实路径绕桌面中心
        /// 旋转 180° 的镜像位置上（用户实测截图定位）。`T85` 锁死本口径。
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

            float width = PredictionLineWidth * DrawScale(playfieldRoot);

            while (_dashPool.Count < segments.Count)
            {
                var go = new GameObject("AimAssistDash_" + _dashPool.Count);
                go.transform.SetParent(playfieldRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = width;
                lr.endWidth = width;
                lr.numCapVertices = 0;
                lr.startColor = PathColor;
                lr.endColor = PathColor;
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

                lr.SetPosition(0, MarkerWorldPoint(playfieldRoot, segments[i].a));
                lr.SetPosition(1, MarkerWorldPoint(playfieldRoot, segments[i].b));
            }
        }

        /// <summary>
        /// 首接触标记（Game 视图）：幽灵球空心圆 + 目标球出射线 + 白球偏转线。
        /// 顶点一律走 <see cref="MarkerWorldPoint"/> / <see cref="WorldDirection"/>（与 Scene Gizmos 同口径）。
        /// 无首接触（打空）⇒ 三者全部隐藏，只留路径线。
        /// </summary>
        private void SyncContactMarkers(in ShotPrediction prediction)
        {
            if (playfieldRoot == null)
            {
                return;
            }

            bool hasContact = prediction.HasFirstContact;
            float scale = DrawScale(playfieldRoot);
            Vector3 ghostWorld = DrawPoint(playfieldRoot, PoolGeometry.PhysicsBallToUnity(prediction.GhostBall));

            if (_ghostCircle == null)
            {
                _ghostCircle = CreateMarkerLine("AimAssistGhostCircle", GhostColor, GhostCircleSegments);
                _ghostCircle.loop = true;
            }

            if (_ghostCircle.positionCount != GhostCircleSegments)
            {
                _ghostCircle.positionCount = GhostCircleSegments;
            }

            _ghostCircle.gameObject.SetActive(hasContact);
            if (hasContact)
            {
                float radius = PoolConstants.BallRadius * scale;
                for (int i = 0; i < GhostCircleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / GhostCircleSegments;
                    _ghostCircle.SetPosition(i, ghostWorld + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }
            }

            bool hasTarget = hasContact && prediction.TargetOutgoingDirection != Vector2.zero;
            if (_targetLine == null)
            {
                _targetLine = CreateMarkerLine("AimAssistTargetLine", TargetColor, 2);
            }

            _targetLine.gameObject.SetActive(hasTarget);
            if (hasTarget)
            {
                _targetLine.SetPosition(0, ghostWorld);
                _targetLine.SetPosition(1, ghostWorld + WorldDirection(prediction.TargetOutgoingDirection) * (TargetLineLength * scale));
            }

            bool hasCue = hasContact && prediction.CueOutgoingDirection != Vector2.zero;
            if (_cueDeflectLine == null)
            {
                _cueDeflectLine = CreateMarkerLine("AimAssistCueDeflectLine", CueDeflectColor, 2);
            }

            _cueDeflectLine.gameObject.SetActive(hasCue);
            if (hasCue)
            {
                _cueDeflectLine.SetPosition(0, ghostWorld);
                _cueDeflectLine.SetPosition(1, ghostWorld + WorldDirection(prediction.CueOutgoingDirection) * (CueDeflectLineLength * scale));
            }
        }

        /// <summary>创建一条挂在可视野根下的标记线（局部坐标 = 规范坐标；颜色/宽度见常量与调用方）。</summary>
        private LineRenderer CreateMarkerLine(string markerName, Color color, int positions)
        {
            if (_runtimeLineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _runtimeLineMaterial = new Material(shader);
                }
            }

            var go = new GameObject(markerName);
            go.transform.SetParent(playfieldRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = positions;
            float width = PredictionLineWidth * DrawScale(playfieldRoot);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.numCapVertices = 0;
            lr.startColor = color;
            lr.endColor = color;
            if (_runtimeLineMaterial != null)
            {
                lr.sharedMaterial = _runtimeLineMaterial;
            }

            return lr;
        }
    }
}
