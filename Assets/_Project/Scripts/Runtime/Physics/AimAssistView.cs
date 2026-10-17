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
        /// <summary>
        /// **段间隙（规范米）—— 现已取 `0` = 实线**（2026-10-16 用户截图反馈："线都断了"）。
        ///
        /// ⚠ **绘制层已不再使用本常量**（白线走 <see cref="SyncPathLine"/>：整条折线 = **一个** `LineRenderer`，天然连续）。
        /// 保留它只为：① `T80(a)` 的**可判定表征**（"实线" ⇔ 本值 = 0）；② 若将来要改回虚线，**只改这一处**。
        /// 旧值 `0.002`（× `DrawScale 1.2` = 0.0024 m ≈ **1.4 px** @596.04 px/米）当时用于**逼近**连续观感。
        /// </summary>
        public const float PredictionGapLength = 0f;

        /// <summary>线宽（**规范米**）。世界宽 = 本值 × <see cref="DrawScale"/>。</summary>
        public const float PredictionLineWidth = 0.006f;

        /// <summary>目标球出射线段长度（**规范米**，与 `PhysicsTestDrive.aimIndicatorLength = 0.55` 同量级）。</summary>
        public const float TargetLineLength = 0.40f;

        /// <summary>白球偏转线长度（**规范米**）。比目标球出射线短（主次分明）。</summary>
        public const float CueDeflectLineLength = 0.26f;

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

        /// <summary>
        /// **每帧复用的虚线段缓冲**（消除 `LateUpdate` / `OnDrawGizmos` 里"每帧 `new List` + 线段再分配"）。
        /// 绘制时先 `Clear` 再追加（见 <see cref="AppendDashSegments"/>）；循环结束即消费完，无别名风险。
        /// </summary>
        private readonly List<(Vector2 a, Vector2 b)> _segmentBuffer = new List<(Vector2 a, Vector2 b)>(512);

        /// <summary>上一次**重绘**时的预测版本（= `AimAssistController.PredictionComputeCount`）；未绘制过 ⇒ -1。</summary>
        private int _lastDrawnVersion = -1;

        /// <summary>当前是否处于"线已清空"状态（避免每帧重复清空，并保证重新显示时强制重绘一次）。</summary>
        private bool _linesCleared;

        private LineRenderer _ghostCircle;
        private LineRenderer _targetLine;
        private LineRenderer _cueDeflectLine;

        /// <summary>**白线本体**（实线：整条折线由**一个** `LineRenderer` 承载；见 <see cref="SyncPathLine"/>）。</summary>
        private LineRenderer _pathLine;

        /// <summary>
        /// **库边接触圆**（用户 2026-10-16："白线碰到边缘…**前面有个圆圈**"）—— 与幽灵球圆**同口径**
        /// （同 `GhostCircleSegments` / 同 `PredictionLineWidth` / `loop = true`），圆心 = `CushionContactPosition`（**精确接触点**）。
        /// </summary>
        private LineRenderer _cushionCircle;
        private Material _runtimeLineMaterial;

        // ── 纯几何 / 单点（判据直接驱动，不必真画）────────────────────────────────

        /// <summary>
        /// **把一条线段切成虚线**（便利重载，返回新建的列表）。热路径/循环请用
        /// <see cref="BuildDashSegments(Vector2, Vector2, float, float, List{(Vector2 a, Vector2 b)})"/> 复用缓冲。
        /// </summary>
        public static IReadOnlyList<(Vector2 a, Vector2 b)> BuildDashSegments(
            Vector2 from, Vector2 to, float dashLength, float gapLength)
        {
            var segments = new List<(Vector2 a, Vector2 b)>();
            AppendDashSegments(from, to, dashLength, gapLength, segments);
            return segments;
        }

        /// <summary>
        /// **把一条线段切成虚线**（复用缓冲版）：**先 `Clear` 再填** ⇒ 结果与便利重载逐元素相同，
        /// 但**不分配**（供每帧绘制路径复用同一列表）。
        /// </summary>
        public static IReadOnlyList<(Vector2 a, Vector2 b)> BuildDashSegments(
            Vector2 from, Vector2 to, float dashLength, float gapLength, List<(Vector2 a, Vector2 b)> buffer)
        {
            buffer.Clear();
            AppendDashSegments(from, to, dashLength, gapLength, buffer);
            return buffer;
        }

        /// <summary>
        /// **把一条线段切成虚线并追加到缓冲**（不清空 ⇒ 可把多条线段拼成同一批）。
        /// 段数 `n = max(1, ceil(L / (dashLength + gapLength)))`；每段长度 `≤ dashLength`，除**末段**外每段**恰为** `dashLength`。
        /// `from` / `to` / 两个长度参数**同空间**（本工程用规范米）。
        /// </summary>
        public static void AppendDashSegments(
            Vector2 from, Vector2 to, float dashLength, float gapLength, List<(Vector2 a, Vector2 b)> buffer)
        {
            if (!(dashLength > 0f))
            {
                return;
            }

            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (!(length > 0f))
            {
                return;
            }

            float gap = gapLength > 0f ? gapLength : 0f;
            Vector2 dir = delta / length;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / (dashLength + gap)));

            float cursor = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = cursor;
                float b = Mathf.Min(cursor + dashLength, length);
                buffer.Add((from + dir * a, from + dir * b));
                cursor += dashLength + gap;
            }
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
                // **实线**（2026-10-16 用户截图反馈："线都断了" ⇒ 白线改实线，不再按弧长铺虚线）。
                // 末点 = `VisiblePathEnd`（**精确接触点**）⇒ 线**画到库边为止**（旧口径"止于最后一次采样顶点"已作废）。
                Gizmos.color = PathColor;
                int gizUse = VisiblePathVertexCount(prediction, path);
                for (int s = 0; s + 1 < gizUse; s++)
                {
                    Gizmos.DrawLine(
                        DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(path[s])),
                        DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(path[s + 1])));
                }

                if (gizUse >= 1)
                {
                    Vector2 lastDrawn = path[gizUse - 1];
                    if (lastDrawn != prediction.VisiblePathEnd)
                    {
                        Gizmos.DrawLine(
                            DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(lastDrawn)),
                            DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(prediction.VisiblePathEnd)));
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

            if (!controller.ShouldShow)
            {
                // 球在滚 / 开关关闭 / 结算卡打开 ⇒ **清空所有线**（只清一次，之后保持空）。
                // ⚠ 必须清空而不是"什么都不做"：`controller.Prediction` 在球运动期间**不重算**，
                // 里存的是**上一杆的旧结果**；不复位就会看到"瞄准线停在原地不动"（用户实测反馈）。
                if (!_linesCleared)
                {
                    // ⚠ **唯一实现点**（缺陷 A 的修法）：以前这里只清了"虚线池 + 接触标记"，
                    //   而白线本体的载体**换成 `_pathLine` 后忘了同步清空** ⇒ 出杆后白线**留在台上**
                    //   （用户第三轮反馈："怎么发射完还留线"）。⇒ 现在一律只调 `HideAllAimVisuals()`。
                    HideAllAimVisuals();
                    _linesCleared = true;
                    _lastDrawnVersion = -1;
                }

                return;
            }

            // **只在预测真的重算过（版本变化）时重绘**：拖动瞄准时预测被节流到 ~30 Hz，
            // 而渲染帧是 60–144 Hz ⇒ 若每帧都重铺/重写全部 `LineRenderer`，就把节流省下的算力又花回去。
            // 预测未变 ⇒ 线已是当前值（顶点全在显示世界，不随帧漂移）⇒ 跳过。
            int version = controller.PredictionComputeCount;
            if (!_linesCleared && version == _lastDrawnVersion)
            {
                return;
            }

            ShotPrediction prediction = controller.Prediction;
            IReadOnlyList<Vector2> path = prediction.CueWorldPath;

            // **实线**（2026-10-16 用户反馈"线都断了"）：整条白线 = **一个** `LineRenderer`（不是每段一个）
            // ⇒ 既保证视觉连续，又把渲染器数从"路径长 ÷ dash"（16 m ÷ 0.25 = 64，**超预算 60**）降到 **1** ✓
            SyncPathLine(prediction, path);
            SyncContactMarkers(prediction);
            _lastDrawnVersion = version;
            _linesCleared = false;
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

            // **只读诊断上报**（本轮取证用，不参与判定）：虚线池大小 + **本帧实际启用**的线段数
            // ⇒ 判定"可渲染物变少"是 (i) 瞄准表现被隐藏（真回归）还是 (ii) 预测路径变短、线仍在画。
            // ⚠ 依赖方向 **Physics → Core**（与 `SmokeProbe.PhysicsStepsReported` 同规，不得反向）。
            SmokeProbe.ReportAimDash(_dashPool.Count, segments != null ? segments.Count : 0);
        }

        /// <summary>
        /// 首接触标记（Game 视图）：幽灵球空心圆 + 目标球出射线 + 白球偏转线。
        /// 顶点一律走 <see cref="MarkerWorldPoint"/> / <see cref="WorldDirection"/>（与 Scene Gizmos 同口径）。
        /// 无首接触（打空）⇒ 三者全部隐藏，只留路径线。
        /// </summary>
        /// <summary>
        /// **按「首个接触」决定该画哪些标记**（**唯一实现点**；判据直接驱动本函数，不必读渲染对象）。
        ///
        /// · **首接触 = 库边** ⇒ 只画【库边圆】；**不画**幽灵球圆 / 目标出射线 / 白球偏转线
        ///   （那三样都是"**碰球之后**"才发生的事 ⇒ 库里首碰时画它们**语义错误、且会误导**）；
        /// · **首接触 = 球** ⇒ 画【幽灵球圆 + 目标出射线 + 白球偏转线】；**不画**库边圆；
        /// · **无接触** ⇒ 全不画。
        /// ⚠ 缺陷 B（用户第三轮："怎么有两个圈"）：旧实现里幽灵球圆用 `HasFirstContact`（= **球-球**接触，
        ///   可能在**碰库之后**才发生）、库边圆用 `HasCushionFirstContact` ⇒ "**先碰库再碰球**"的杆里**两者同时为真**
        ///   ⇒ **两个圈**。旧注释那句"两圆互斥，不会叠"**是假的**（它默认了 `HasFirstContact` == 首个接触）。
        /// </summary>
        public readonly struct AimMarkerSet
        {
            public readonly bool GhostCircle;
            public readonly bool CushionCircle;
            public readonly bool TargetLine;
            public readonly bool CueDeflectLine;

            public AimMarkerSet(bool ghostCircle, bool cushionCircle, bool targetLine, bool cueDeflectLine)
            {
                GhostCircle = ghostCircle;
                CushionCircle = cushionCircle;
                TargetLine = targetLine;
                CueDeflectLine = cueDeflectLine;
            }
        }

        /// <summary>见 <see cref="AimMarkerSet"/>：按**首个接触的类别**给出该显示哪组标记（纯函数）。</summary>
        public static AimMarkerSet MarkersFor(ContactKind firstContactKind, bool hasFirstContactEvent)
        {
            if (!hasFirstContactEvent)
            {
                return new AimMarkerSet(false, false, false, false);
            }

            // ⚠ **必须穷举四档、不许有"兜底成球首碰"的分支**（缺陷 C 的根因就在这个 `else`）：
            //   白线指向**球袋**时首接触是 `ContactKind.Pocket`（越袋喉）⇒ 旧实现把它当成"球首碰"⇒ 去画幽灵球圆；
            //   而"无球-球接触"时 `GhostBall` **从未被赋值**（`Predict` 的提前返回分支里没有它）= `default` = **(0,0)**
            //   ⇒ **圆画在台面正中**（用户第三轮："圆圈分离出来了"）。
            switch (firstContactKind)
            {
                case ContactKind.Cushion:
                    return new AimMarkerSet(false, true, false, false);    // 库边首碰：只库边圆

                case ContactKind.BallBall:
                    return new AimMarkerSet(true, false, true, true);      // 球首碰：幽灵球圆 + 黄线 + 蓝线

                case ContactKind.Pocket:
                    // 越袋喉 = 球进袋 ⇒ **没有"接触物"要标**（画幽灵球圆/出射线都是**语义错误**）。
                    return new AimMarkerSet(false, false, false, false);

                default:
                    // ⚠ **未知/新增类别一律不画**（**不得**落入"球首碰"兜底）。
                    //   将来加类别时，`switch` 的穷举会提醒在此处显式决定（编译器不会静默兜底）。
                    return new AimMarkerSet(false, false, false, false);
            }
        }

        /// <summary>
        /// **隐藏全部瞄准可视物**（**唯一实现点**；清空分支与任何"该隐藏"的路径都只调它）。
        ///
        /// ⚠ **迁移检查清单（本处最值钱的一条）**：**换渲染载体时，必须同时更新"所有"引用该载体的路径** ——
        /// **绘制 / 清空 / 显隐门 / 池回收**，并**加一条"清空后无残留"的判据**（否则下次还会漏）。
        /// 本缺陷 A 漏的正是"**清空**"那一列（白线载体由虚线池换成 `_pathLine` 时只改了绘制）。
        /// 本方法**显式枚举**全部对象（不依赖 `SyncContactMarkers(default)` 的内部逻辑 ⇒ 更稳）：
        /// 虚线池（已退役，仍一并清）/ `_pathLine` / `_ghostCircle` / `_cushionCircle` / `_targetLine` / `_cueDeflectLine`。
        /// </summary>
        private void HideAllAimVisuals()
        {
            SyncRenderers(System.Array.Empty<(Vector2 a, Vector2 b)>());

            HideLine(_pathLine);
            HideLine(_ghostCircle);
            HideLine(_cushionCircle);
            HideLine(_targetLine);
            HideLine(_cueDeflectLine);
        }

        /// <summary>隐藏单个瞄准可视物（`null` 安全）。</summary>
        private static void HideLine(LineRenderer line)
        {
            if (line != null)
            {
                line.gameObject.SetActive(false);
            }
        }

        private void SyncContactMarkers(in ShotPrediction prediction)
        {
            if (playfieldRoot == null)
            {
                return;
            }

            // ⚠ **按"首个接触"互斥**（缺陷 B 的修法）：可见性一律由纯函数 `MarkersFor` 决定。
            AimMarkerSet markers = MarkersFor(prediction.FirstContactKind, prediction.HasFirstContactEvent);
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

            _ghostCircle.gameObject.SetActive(markers.GhostCircle);
            if (markers.GhostCircle)
            {
                float radius = PoolConstants.BallRadius * scale;
                for (int i = 0; i < GhostCircleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / GhostCircleSegments;
                    _ghostCircle.SetPosition(i, ghostWorld + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }
            }

            // ── **库边接触圆**（用户："像喷到球一样**前面有个圆圈**"）──
            // ⚠ 与幽灵球圆**同口径**（同段数/同线宽/`loop`），只是圆心 = **库边接触点**、且**首个接触是库边时**才画。
            // ⚠ 球-球首碰时**不画**库圆（那种情形本来就画幽灵球圆）⇒ 两圆互斥，不会叠。
            if (_cushionCircle == null)
            {
                _cushionCircle = CreateMarkerLine("AimAssistCushionCircle", PathColor, GhostCircleSegments);
                _cushionCircle.loop = true;
            }

            if (_cushionCircle.positionCount != GhostCircleSegments)
            {
                _cushionCircle.positionCount = GhostCircleSegments;
            }

            bool hasCushionCircle = markers.CushionCircle;
            _cushionCircle.gameObject.SetActive(hasCushionCircle);
            if (hasCushionCircle)
            {
                // ⚠ 用**单一对**里的 `FirstContactPosition`（对所有类别都填）—— 不再有"只在库边分支才填"的字段。
                Vector3 cushionWorld = DrawPoint(playfieldRoot,
                    PoolGeometry.PhysicsBallToUnity(prediction.FirstContactPosition));
                float cushionRadius = PoolConstants.BallRadius * scale;
                for (int i = 0; i < GhostCircleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / GhostCircleSegments;
                    _cushionCircle.SetPosition(i,
                        cushionWorld + new Vector3(Mathf.Cos(angle) * cushionRadius, 0f, Mathf.Sin(angle) * cushionRadius));
                }
            }

            bool hasTarget = markers.TargetLine && prediction.TargetOutgoingDirection != Vector2.zero;
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

            bool hasCue = markers.CueDeflectLine && prediction.CueOutgoingDirection != Vector2.zero;
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

        /// <summary>
        /// **可见路径的顶点数**（绘制口径，**Gizmos 与运行时共用** = 单点实现）：
        /// `path[0 .. VisiblePathCount-1]` 是**接触前**的采样前缀；其后由 `VisiblePathEnd`（**精确接触点**）收尾。
        /// ⚠ 非法/越界一律夹到 `[0, path.Count]`。
        /// </summary>
        /// <summary>**可见前缀的顶点数**（`<=0` 或 `>= Count` ⇒ 整条）。P10-D 判据**必须**用这一份口径
        /// （private → public 只为让判据**和渲染读同一个函数**，不另写一份）。</summary>
        public static int VisiblePathVertexCount(in ShotPrediction prediction, IReadOnlyList<Vector2> path)
        {
            if (path == null || path.Count == 0)
            {
                return 0;
            }

            int use = prediction.VisiblePathCount <= 0 ? path.Count : Mathf.Min(prediction.VisiblePathCount, path.Count);
            return Mathf.Max(use, 1);
        }

        /// <summary>
        /// **把白线画成实线**（单个 `LineRenderer` 承载整条折线）：顶点 = 可见前缀 + **精确接触点**（`VisiblePathEnd`）。
        /// ⚠ 为何不用"每段一个 `LineRenderer`"：段数 = 路径长 ÷ `PredictionDashLength`（16 m ÷ 0.25 = **64**）
        /// ⇒ 会**撞穿渲染器预算 60**；整条折线一个渲染器则是 **1** ✓。
        /// </summary>
        private void SyncPathLine(in ShotPrediction prediction, IReadOnlyList<Vector2> path)
        {
            if (_pathLine == null)
            {
                _pathLine = CreateMarkerLine("AimAssistPathLine", PathColor, 2);
            }

            int use = VisiblePathVertexCount(prediction, path);
            if (use == 0)
            {
                _pathLine.gameObject.SetActive(false);
                return;
            }

            Vector2 end = prediction.VisiblePathEnd;
            bool extraEnd = path[use - 1] != end;
            int count = use + (extraEnd ? 1 : 0);

            _pathLine.positionCount = count;
            for (int i = 0; i < use; i++)
            {
                _pathLine.SetPosition(i, DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(path[i])));
            }

            if (extraEnd)
            {
                _pathLine.SetPosition(use, DrawPoint(playfieldRoot, PoolGeometry.PhysicsToUnity(end)));
            }

            _pathLine.gameObject.SetActive(true);
        }

        /// <summary>
        /// **沿折线按弧长铺虚线**（唯一的"沿整条路径"虚线实现）：从起点起，每 `dashLength + gapLength` 的
        /// 弧长铺一段 `dashLength`（末段截到路径末端）。
        ///
        /// <para>⚠ **这是拖动性能的关键**：预测路径按 `Step` 取样（每步约 20 mm），若**逐段**铺虚线
        /// （<see cref="AppendDashSegments"/> 的语义：每段至少 1 段），~240 个取样点 ⇒ ~240 个
        /// `LineRenderer`（实测拖动冒烟 `rendererCount` 67 → 260、帧率掉到 38 fps）。
        /// 按**弧长**铺则段数只由**路径总长**决定（数米 ⇒ 约 20 段），观感几乎不变（间隙仍 2 mm），
        /// 渲染器数量降一个数量级。</para>
        /// </summary>
        public static void AppendPolylineDashSegments(IReadOnlyList<Vector2> path, float dashLength, float gapLength,
            List<(Vector2 a, Vector2 b)> buffer)
        {
            AppendPolylineDashSegments(path, dashLength, gapLength, buffer, -1);
        }

        /// <summary>
        /// 同上，但**只取前 <paramref name="visibleCount"/> 个顶点**（`≤ 0` 或 ≥ `Count` ⇒ 整条）。
        ///
        /// **为什么需要它**（用户实机反馈"白色瞄准线碰到台球桌边就不用反射了"）：
        /// 白线取 <see cref="ShotPrediction.VisiblePathCount"/> 作前缀 ⇒ **不画折返段**。
        /// ⚠ 截断**只发生在绘制侧** —— `CueWorldPath` 本体不变（其"末点 == 幽灵球"**仅在球-球首碰时**成立、被 `ShotPredictorTests` 逐位钉死；**撞库情形末点是库边接触点**）。
        /// </summary>
        public static void AppendPolylineDashSegments(IReadOnlyList<Vector2> path, float dashLength, float gapLength,
            List<(Vector2 a, Vector2 b)> buffer, int visibleCount)
        {
            if (path == null || !(dashLength > 0f))
            {
                return;
            }

            int use = visibleCount <= 0 ? path.Count : Mathf.Min(visibleCount, path.Count);
            if (use < 2)
            {
                return;
            }

            float total = 0f;
            for (int i = 0; i + 1 < use; i++)
            {
                total += (path[i + 1] - path[i]).magnitude;
            }

            if (!(total > 0f))
            {
                return;
            }

            float gap = gapLength > 0f ? gapLength : 0f;
            float period = dashLength + gap;
            int count = Mathf.Max(1, Mathf.CeilToInt(total / period));

            for (int k = 0; k < count; k++)
            {
                float s0 = k * period;
                float s1 = Mathf.Min(s0 + dashLength, total);
                if (!(s1 > s0))
                {
                    break;
                }

                buffer.Add((PointAlongPath(path, s0, use), PointAlongPath(path, s1, use)));
            }
        }

        /// <summary>折线上累计弧长 <paramref name="s"/> 处的点（`s ≤ 0` ⇒ 起点；超长 ⇒ 终点）。</summary>
        private static Vector2 PointAlongPath(IReadOnlyList<Vector2> path, float s)
        {
            return PointAlongPath(path, s, path.Count);
        }

        /// <summary>同上，但只在**前 <paramref name="use"/> 个顶点**构成的折线上取点。</summary>
        private static Vector2 PointAlongPath(IReadOnlyList<Vector2> path, float s, int use)
        {
            if (s <= 0f)
            {
                return path[0];
            }

            float acc = 0f;
            for (int i = 0; i + 1 < use; i++)
            {
                Vector2 a = path[i];
                Vector2 b = path[i + 1];
                float segmentLength = (b - a).magnitude;
                if (segmentLength <= 0f)
                {
                    continue;
                }

                if (acc + segmentLength >= s)
                {
                    return a + (b - a) * ((s - acc) / segmentLength);
                }

                acc += segmentLength;
            }

            return path[path.Count - 1];
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
