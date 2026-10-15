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
    ///   rail = `TotalContactCount − BallBallContactCount`；pocket = `PocketProximityEvents`）；
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
        public const float LineWidth = 0.004f;

        /// <summary>入袋"下沉"动画时长（秒）——球可视物下沉 + 缩小至消失。</summary>
        public const float SinkDurationSeconds = 0.3f;

        private const int SparkCount = 3;
        private const float PocketInset = 0.013f;
        private const float SparkSpreadRad = 0.65f;
        private const float SparkStartFactor = 1.03f;
        private const float SparkTravelFactor = 0.65f;

        private static readonly Color ChalkColor = new Color(0.973f, 0.961f, 0.906f, 1f);   // #f8f5e7
        private static readonly Color BrassColor = new Color(0.953f, 0.851f, 0.600f, 1f);   // #f3d999
        private static readonly Color RailColor = new Color(0.800f, 0.855f, 0.796f, 1f);    // #ccdacb

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

            int pocket = sim.PocketProximityEvents;
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
            _lastPocket = sim != null ? sim.PocketProximityEvents : 0;
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
            float strength = 0.7f + Mathf.Clamp01(drive.power) * 0.6f;
            Vector2 center = cue - aimDir * (PoolConstants.BallRadius * 0.85f);

            Spawn(true, center, aimDir: drive.aimDegrees + 180f,
                radius: 0.012f * strength, duration: 0.12f, expansion: 0.4f, opacity: 0.72f,
                color: ChalkColor, sparks: 3, sparkLength: 0.008f * strength);
        }

        private void SpawnHit(PoolSimulator sim)
        {
            CollectBallStates(sim);
            if (!FindHitContact(_posBuffer, _velBuffer, PoolConstants.BallRadius,
                                out Vector2 point, out float angle, out float _))
            {
                return;
            }

            Spawn(false, point, angle, radius: 0.011f, duration: 0.11f, expansion: 0.5f,
                opacity: 0.58f, color: ChalkColor, sparks: 2, sparkLength: 0.008f);
        }

        private void SpawnRail(PoolSimulator sim)
        {
            if (tableConfig == null || tableConfig.segments == null)
            {
                return;
            }

            CollectBallStates(sim);
            if (!FindRailContact(_posBuffer, _velBuffer, tableConfig.segments, PoolConstants.BallRadius,
                                 out Vector2 point, out float angle, out float _))
            {
                return;
            }

            Spawn(true, point, angle, radius: 0.014f, duration: 0.15f, expansion: 0.2f,
                opacity: 0.46f, color: RailColor, sparks: 0, sparkLength: 0.008f);
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

            Spawn(false, center, inwardDeg, radius: 0.023f, duration: 0.29f, expansion: 0.5f,
                opacity: 0.8f, color: BrassColor, sparks: 3, sparkLength: 0.012f);

            // **短暂高亮**：叠加一个更亮更短的内环（"进球有回报"的可判定实现）。
            Spawn(false, center, inwardDeg, radius: 0.015f, duration: 0.18f, expansion: 0.2f,
                opacity: 0.9f, color: new Color(1f, 1f, 1f, 1f), sparks: 0, sparkLength: 0.008f);
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
                           float expansion, float opacity, Color color, int sparks, float sparkLength)
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
            slot.age = 0f;
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

            WriteSlot(slot, 0f);
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
            ring.startColor = color;
            ring.endColor = color;

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

            WriteLine(ring, _ringBuffer, !slot.isArc);

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
                spark.startColor = sparkColor;
                spark.endColor = sparkColor;

                Vector2 tip = slot.center + new Vector2(Mathf.Cos(sparkAngle), Mathf.Sin(sparkAngle)) * startRadius;
                Vector2 tail = tip + new Vector2(Mathf.Cos(sparkAngle), Mathf.Sin(sparkAngle)) * slot.sparkLength;
                _sparkBuffer.Clear();
                _sparkBuffer.Add(AimAssistView.MarkerWorldPoint(playfieldRoot, tip));
                _sparkBuffer.Add(AimAssistView.MarkerWorldPoint(playfieldRoot, tail));
                WriteLine(spark, _sparkBuffer, false);
            }
        }

        /// <summary>
        /// **写顶点的单点**（全类唯一的 `SetPosition` 调用处）。
        ///
        /// ── 为什么必须先 `positionCount` 后 `SetPosition`（实测缺陷，2026-10-15）────────────
        /// `LineRenderer.SetPosition` 在索引越界时**只打错误日志、不抛异常**（`LineRenderer.SetPosition index
        /// out of bounds!`）。原实现是「先循环写 32 个点、循环之后才设 `positionCount = 32`」，
        /// 而环出厂时 `positionCount = 0` ⇒ **每个点都越界**；且槽位在「环(32) / 弧(16)」之间复用时
        /// 数量会由大变小 ⇒ **反复越界刷屏**，既淹掉真错误又极难定位（用户实机截图定位）。
        /// 把"数量"与"顶点"收进同一个函数、顺序固定 ⇒ 该失效模式**在结构上不可能发生**。
        ///
        /// 判据 `T99` 直接驱动本函数（含 `positionCount = 0` 的出厂态）。
        /// </summary>
        public static void WriteLine(LineRenderer line, IReadOnlyList<Vector3> points, bool loop)
        {
            line.loop = loop;
            line.positionCount = points.Count;
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
            lr.startWidth = LineWidth;
            lr.endWidth = LineWidth;
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
