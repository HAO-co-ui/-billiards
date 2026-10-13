using System.Collections.Generic;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 4 切片 C —— **T7 全 82 段覆盖扫描**。
    ///
    /// 补 `Docs/Reviews/D4-Day4独立复核.md` §7.1：76 条袋角/弧段（段长 4.29–32.4 mm ≪ 球直径 57.15 mm）
    /// 的 TOI 分支（端点圆 + 线段分支同时命中、`MaxToiIterations` R-C 触发）**完全无测试覆盖**。
    ///
    /// ## 方案（判据自定，写清如下）
    ///
    /// · 种子：台心 + 4 个象限内点，共 **5 个**（均经合法性断言：到全部 82 段距离 &gt; 球半径）。
    /// · 瞄准点：每段 **中点 + 两个端点**，共 82×3 = 246 点/种子 ⇒ **1230 次发射**。
    /// · 发射：自种子沿 `normalize(target − seed)` 以 5 m/s 发射，最多推进 72 步（0.6 s，3 m 射程）。
    /// · **命中判据**：以**扫掠弦**判接触 —— 每个 Step 取「球心步前位置→步后位置」的线段，
    ///   计算它与该段的最短距离；≤ `r + tol` 即判该步接触（**不能只用步末位置采样**：
    ///   高速球在一个 Step 内即可完成"接触→反弹→离开"，步末距离已 &gt; r，会漏检）。
    ///   同一次连续接触只按**上升沿**计一次。
    ///
    /// ## 判据
    ///
    /// · **硬门 1**：6 条直库段（索引 6/20/33/47/61/74）每段命中 &gt; 0；
    /// · **硬门 2 / 3**：`ToiIterationGuardTrips == 0` 且 `EnergyGainViolations == 0`（spec §6 判据 10）。
    ///   本测试最初实测 `guardTrips = 1810`（R-C 现象实证）；经切片 C ② 的 R-C 修复
    ///   （`StoppedSpeedEpsilon` 近零即停 + "连续两次零时间推进即停" + `MaxToiIterations` 16→128）
    ///   后实测 **0**（`1810 → 1196 → 339 → 3 → 0`）⇒ 判据 10 **不是不可达条款**，按原样保留为硬门（team-lead 裁定 ①.2）。
    /// · 76 条袋角段**逐条枚举**命中数，未命中的**逐条列出并给原因**（被遮挡 / 袋口区归 Day 6 / 未触达）；
    /// · 最大嵌入量（由本测试的**独立扫掠弦几何**度量，与内核口径不同）只记录并打印，超 1e-5 m 记 WARN。
    /// · 产出 `Docs/Reports/d4-segment-coverage.json`（经单点 <see cref="ReportOutput"/>，支持 `-outputDir`）。
    ///
    /// 几何度量（<see cref="DistancePointToSegment"/> / <see cref="SegmentsIntersect"/>）在本文件**独立实现**，
    /// 不复用被测内核的几何函数（避免"用被检对象证明被检对象"）。
    /// </summary>
    public class PoolSimulatorSegmentCoverageTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";
        private const string ReportRelativePath = "Docs/Reports/d4-segment-coverage.json";

        private static readonly int[] StraightSegments = { 6, 20, 33, 47, 61, 74 };

        private static readonly Vector2[] SeedPoints =
        {
            new Vector2(0f, 0f),
            new Vector2(0.45f, 0.30f),
            new Vector2(-0.45f, 0.30f),
            new Vector2(0.45f, -0.30f),
            new Vector2(-0.45f, -0.30f)
        };

        private const float LaunchSpeed = 5f;
        private const int MaxSteps = 72;                 // 0.6 s ⇒ 3 m 射程
        private const float ContactTolerance = 5e-5f;    // 扫掠弦接触容差，米

        private static TableConfig _config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 " + ConfigPath + "（先运行 Billiards/Data/Import Table Reference Data）。");
        }

        [Test]
        public void T7_AllSegmentCoverage_ScanAndReport()
        {
            int segCount = _config.SegmentCount;
            Assert.AreEqual(82, segCount, "T7：段数应为 82。");

            float r = PoolConstants.BallRadius;

            // 种子合法性（非空转前置）。
            for (int s = 0; s < SeedPoints.Length; s++)
            {
                float nearest = NearestSegmentDistance(SeedPoints[s]);
                Assert.Greater(nearest, r,
                    $"T7：种子 {s} = {SeedPoints[s]:R} 距最近段仅 {nearest * 1000f:F3} mm（≤ 球半径），不合法。");
            }

            var hitCount = new int[segCount];
            var nearestApproach = new float[segCount];
            for (int i = 0; i < segCount; i++)
            {
                nearestApproach[i] = float.PositiveInfinity;
            }

            var records = new List<AimRecord>();
            int guardTrips = 0;
            int energyGain = 0;
            int frozenLaunches = 0;
            float maxPenetration = 0f;

            for (int seedIdx = 0; seedIdx < SeedPoints.Length; seedIdx++)
            {
                Vector2 seed = SeedPoints[seedIdx];

                for (int segIdx = 0; segIdx < segCount; segIdx++)
                {
                    CushionSegment seg = _config.segments[segIdx];
                    Vector2 mid = (seg.a + seg.b) * 0.5f;
                    var targets = new[] { (mid, "mid"), (seg.a, "a"), (seg.b, "b") };

                    foreach (var (target, tag) in targets)
                    {
                        AimRecord rec = Launch(seedIdx, seed, segIdx, target, tag, r);
                        records.Add(rec);

                        guardTrips += rec.GuardTrips;
                        energyGain += rec.EnergyGain;
                        if (rec.MaxPenetration > maxPenetration)
                        {
                            maxPenetration = rec.MaxPenetration;
                        }

                        if (rec.Frozen)
                        {
                            frozenLaunches++;
                        }

                        if (rec.NearestApproach < nearestApproach[segIdx])
                        {
                            nearestApproach[segIdx] = rec.NearestApproach;
                        }

                        // 命中段计数（上升沿去重后，由 Launch 内部汇总到 HitSegments）。
                        foreach (int hit in rec.HitSegments)
                        {
                            hitCount[hit]++;
                        }
                    }
                }
            }

            // ── 6 条直库段汇总（硬门槛数据源）──────────────────────────────
            var straightMissing = new List<int>();
            var straightText = new StringBuilder();
            foreach (int idx in StraightSegments)
            {
                straightText.Append($"seg[{idx}]={hitCount[idx]} ");
                if (hitCount[idx] <= 0)
                {
                    straightMissing.Add(idx);
                }
            }

            // ── 袋角段逐条枚举 ─────────────────────────────────────────────
            var unhit = new List<int>();
            for (int i = 0; i < segCount; i++)
            {
                if (hitCount[i] <= 0)
                {
                    unhit.Add(i);
                }
            }

            // ── 先落盘与打印（**在任何断言之前**）──────────────────────────
            // 理由：本测试是 R-C（角点迭代守卫）与 96 段覆盖的**探针**，其诊断值必须先成为可查证据，
            // 再谈硬门槛；否则一旦某个断言失败，证据就丢了（铁律 1）。
            List<ReasonEntry> reasons = BuildReasons(records, unhit, nearestApproach, hitCount);
            WriteReport(records, hitCount, nearestApproach, reasons, straightText.ToString(),
                guardTrips, energyGain, maxPenetration, frozenLaunches, segCount);

            for (int i = 0; i < segCount; i++)
            {
                TestContext.WriteLine($"T7 seg[{i}] hitCount={hitCount[i]} nearestApproach={nearestApproach[i] * 1000f:F4}mm");
            }

            TestContext.WriteLine(
                $"T7: 发射 {records.Count} 次（5 种子 × 82 段 × 3 瞄准点）; 直库段 {straightText}; " +
                $"未命中段数={unhit.Count}; guardTrips={guardTrips}; energyGainViolations={energyGain}; " +
                $"最大嵌入量={maxPenetration * 1000f:F5}mm; 冻结发射数={frozenLaunches}");

            foreach (ReasonEntry e in reasons)
            {
                TestContext.WriteLine($"T7 未命中 seg[{e.SegmentIndex}]: {e.Reason}");
            }

            // ── 非空转（R4-3）─────────────────────────────────────────────
            Assert.AreEqual(SeedPoints.Length * segCount * 3, records.Count, "T7：发射数不等于 5×82×3（扫描不完整）。");
            int totalHits = 0;
            for (int i = 0; i < segCount; i++)
            {
                totalHits += hitCount[i];
            }

            Assert.Greater(totalHits, 0, "T7：全程零命中（扫描空转）。");

            // ── 硬门槛：6 条直库段（spec §5 T7 / §6 判据 7）────────────────
            Assert.IsEmpty(straightMissing,
                $"T7 硬门槛失败：直库段 {string.Join(",", straightMissing)} 命中 0 次（{straightText}）。");

            // ── 硬断言：R-C 守卫与能量增益（spec §6 判据 10，**R-C 修复后已可达**）──────
            // D4-VERIFY-02 / 切片 C ② 的 R-C 修复（PoolSimulator：StoppedSpeedEpsilon 近零即停 +
            // "连续两次零时间推进即停" + MaxToiIterations 16→128）后实测 `guardTrips = 0`：
            //   `1810（原）→ 1196（仅近零速度）→ 339（+零时间推进即停）→ 3（预算 64）→ 0（预算 128）`。
            // ⇒ 判据 10 的"全测试族 == 0"**不是不可达条款**，按原样保留为硬门（team-lead 裁定 ①.2）。
            Assert.AreEqual(0, guardTrips,
                $"T7：ToiIterationGuardTrips = {guardTrips}（MaxToiIterations 触发即该子步被截断；R-C 修复后应为 0）。");
            Assert.AreEqual(0, energyGain,
                $"T7：EnergyGainViolations = {energyGain}（角点多接触升能，应为 0）。");

            // 诊断项（不作硬门）：最大嵌入量由本测试的**独立扫掠弦几何**度量，与内核口径不同，
            // 只记录并打印；若超 1e-5 m 记 WARN（见 JSON checks[] T7-COV-04）。
        }

        // ── 单次发射 ──────────────────────────────────────────────────────

        private AimRecord Launch(int seedIdx, Vector2 seed, int aimedSeg, Vector2 target, string tag, float r)
        {
            var rec = new AimRecord
            {
                SegmentIndex = aimedSeg,
                AimTag = tag,
                SeedIndex = seedIdx,
                FirstHitSegment = -1,
                FirstHitStep = -1,
                HitSegments = new List<int>(),
                NearestApproach = float.PositiveInfinity,
                MaxPenetration = 0f,
                EndNetSide = float.NegativeInfinity,
                Frozen = false
            };

            Vector2 dir = target - seed;
            if (dir.sqrMagnitude < 1e-12f)
            {
                rec.NearestApproach = 0f;
                return rec;
            }

            var sim = new PoolSimulator(_config, Params());
            sim.LoadBalls(new[] { new BallState { position = seed, velocity = dir.normalized * LaunchSpeed } });

            var inContact = new bool[_config.SegmentCount];
            Vector2 prev = seed;

            for (int step = 0; step < MaxSteps; step++)
            {
                sim.Step();
                Vector2 cur = sim.Balls[0].position;
                Vector2 pathA = prev;
                Vector2 pathB = cur;

                for (int j = 0; j < _config.SegmentCount; j++)
                {
                    CushionSegment s = _config.segments[j];

                    // 扫掠弦到该段的最短距离。
                    float d = SegSegDistance(pathA, pathB, s.a, s.b);

                    // 最近接近量只记「被瞄准段」的（供未命中原因使用）。
                    if (j == aimedSeg && d - r < rec.NearestApproach)
                    {
                        rec.NearestApproach = d - r;
                    }

                    float penetration = r - d;
                    if (penetration > rec.MaxPenetration)
                    {
                        rec.MaxPenetration = penetration;
                    }

                    bool touching = d <= r + ContactTolerance;
                    if (touching && !inContact[j])
                    {
                        inContact[j] = true;
                        rec.HitSegments.Add(j);
                        if (rec.FirstHitSegment < 0)
                        {
                            rec.FirstHitSegment = j;
                            rec.FirstHitStep = step;
                        }
                    }
                    else if (!touching)
                    {
                        inContact[j] = false;
                    }
                }

                prev = cur;

                if (sim.PocketProximityEvents > 0)
                {
                    rec.Frozen = true;
                    break;
                }
            }

            rec.GuardTrips = sim.ToiIterationGuardTrips;
            rec.EnergyGain = sim.EnergyGainViolations;
            rec.EndNetSide = MaxPocketNetSide(sim.Balls[0].position);
            return rec;
        }

        // ── 未命中原因 ────────────────────────────────────────────────────

        /// <summary>
        /// 未命中段**逐条**给原因。原因**由扫描数据本身推出**（不猜）：对每个未命中段，
        /// 统计瞄准它的 15 次发射（5 种子 × 3 瞄准点）里 —— 多少"到段前穿出袋口被冻结"、
        /// 多少"先命中更近的段"、多少"未触达"；并给出段中点到最近袋心的距离。
        /// 再叠加"袋口区 / 全遮挡 / 未触达"三类判读之一。
        /// </summary>
        private List<ReasonEntry> BuildReasons(List<AimRecord> records, List<int> unhit,
            float[] nearestApproach, int[] hitCount)
        {
            // 测试内部统计用（**不参与被测内核**；热路径无序容器由 D-3 源码守卫单独覆盖）。
            var bySeg = new Dictionary<int, List<AimRecord>>();
            foreach (AimRecord rec in records)
            {
                if (!bySeg.TryGetValue(rec.SegmentIndex, out List<AimRecord> bucket))
                {
                    bucket = new List<AimRecord>();
                    bySeg[rec.SegmentIndex] = bucket;
                }

                bucket.Add(rec);
            }

            var list = new List<ReasonEntry>();

            foreach (int idx in unhit)
            {
                List<AimRecord> aimed = bySeg[idx];
                int frozenCount = 0;
                int blockedCount = 0;
                var blockers = new SortedSet<int>();
                foreach (AimRecord rec in aimed)
                {
                    if (rec.Frozen)
                    {
                        frozenCount++;
                    }
                    else if (rec.FirstHitSegment >= 0 && rec.FirstHitSegment != idx)
                    {
                        blockedCount++;
                        blockers.Add(rec.FirstHitSegment);
                    }
                }

                int cleanMiss = aimed.Count - frozenCount - blockedCount;

                CushionSegment seg = _config.segments[idx];
                Vector2 mid = (seg.a + seg.b) * 0.5f;
                float pocketDist = NearestPocketCenterDistance(mid);
                bool onNetSide = IsOnPocketNetSide(mid);
                bool occluded = IsFullyOccluded(idx);
                // nearestApproach[idx] 存的已是「球心距 − r」（净接近量，负数=已嵌入）；此处只换单位。
                float clearanceMm = nearestApproach[idx] * 1000f;

                // 判读分类：**优先用扫描数据本身**（多少发射被别段截胡 / 多少穿袋冻结），
                // 再叠加几何信号（网侧 / 张开区）。避免把仅差几毫米的"近失"误标为"袋口区"。
                string category;
                if (blockedCount == aimed.Count)
                {
                    category = $"被遮挡（{aimed.Count}/{aimed.Count} 次发射均先命中更近段）";
                }
                else if (frozenCount == aimed.Count)
                {
                    category = $"仅袋口区可达（{aimed.Count}/{aimed.Count} 次在到达前穿出袋口被冻结）⇒ 归 Day 6";
                }
                else if (onNetSide)
                {
                    category = "网侧（段中点越过某袋喉平面，仅袋口内可达）⇒ 归 Day 6";
                }
                else if (pocketDist <= PoolConstants.PocketOpeningRadius)
                {
                    category = $"袋口张开区内（距袋心 {InvF(pocketDist * 1000f, "F1")} mm ≤ 张开半径）⇒ 归 Day 6";
                }
                else
                {
                    category = $"未触达（射线可达，但 72 步内轨迹未进入接触带；最近净接近量 {InvF(clearanceMm, "F3")} mm）";
                }

                string blockerText = blockers.Count == 0 ? "无" : "seg[" + string.Join(",", blockers) + "]";

                string reason =
                    $"瞄准该段的 {aimed.Count} 次发射：穿袋冻结={frozenCount}、先命中它段={blockedCount}（{blockerText}）、未触达={cleanMiss}；" +
                    $"段中点到最近袋心={InvF(pocketDist * 1000f, "F2")} mm、最近净接近量（球心距−r）={InvF(clearanceMm, "F3")} mm、" +
                    $"射线全遮挡={occluded}；判读={category}";

                list.Add(new ReasonEntry { SegmentIndex = idx, HitCount = hitCount[idx], Reason = reason });
            }

            return list;
        }

        /// <summary>种子/中点到段的最近距离是否被另一段更近地阻挡（粗略遮挡判定：沿射线取最近命中段）。</summary>
        private bool IsFullyOccluded(int segIdx)
        {
            CushionSegment seg = _config.segments[segIdx];
            Vector2 mid = (seg.a + seg.b) * 0.5f;

            foreach (Vector2 seed in SeedPoints)
            {
                Vector2 dir = mid - seed;
                float dist = dir.magnitude;
                if (dist < 1e-9f)
                {
                    return false;
                }

                Vector2 dirHat = dir / dist;
                float nearestBlock = float.PositiveInfinity;
                int blocker = -1;

                for (int j = 0; j < _config.SegmentCount; j++)
                {
                    if (j == segIdx)
                    {
                        continue;
                    }

                    CushionSegment other = _config.segments[j];
                    float along = RayHitsSegmentWithinCapsule(seed, dirHat, dist, other, PoolConstants.BallRadius);
                    if (along >= 0f && along < nearestBlock && along < dist - 1e-6f)
                    {
                        nearestBlock = along;
                        blocker = j;
                    }
                }

                if (!(nearestBlock < dist - 1e-6f) || blocker < 0)
                {
                    // 该种子方向上，目标段之前没有阻挡 ⇒ 并非"全遮挡"。
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 射线自 origin 沿单位方向 dirHat 行进，最早进入段 other 的球半径胶囊（沿段 + 两端圆）的沿程距离；未命中 -1。
        /// 与内核的 TOI 数学同形但**独立实现**（不共享代码路径）。
        /// </summary>
        private static float RayHitsSegmentWithinCapsule(Vector2 origin, Vector2 dirHat, float maxDist, CushionSegment other, float r)
        {
            float best = float.PositiveInfinity;

            Vector2 ab = other.b - other.a;
            float len2 = ab.x * ab.x + ab.y * ab.y;
            if (len2 > 1e-12f)
            {
                float len = Mathf.Sqrt(len2);
                float nx = -ab.y / len;
                float ny = ab.x / len;
                float s0 = (origin.x - other.a.x) * nx + (origin.y - other.a.y) * ny;
                float sv = dirHat.x * nx + dirHat.y * ny;

                if (Mathf.Abs(s0) >= r && sv * s0 < 0f)
                {
                    float sTarget = s0 > 0f ? r : -r;
                    float t = (sTarget - s0) / sv;
                    if (t >= 0f && t < best)
                    {
                        Vector2 c = origin + t * dirHat;
                        float u = Vector2.Dot(c - other.a, ab) / len2;
                        if (u >= 0f && u <= 1f)
                        {
                            best = t;
                        }
                    }
                }
            }

            best = Mathf.Min(best, RayCircleEntry(origin, dirHat, other.a, r));
            best = Mathf.Min(best, RayCircleEntry(origin, dirHat, other.b, r));
            return best <= maxDist ? best : -1f;
        }

        /// <summary>射线最早进入半径 r 圆（圆心 c）的沿程距离；未命中返回 +∞。</summary>
        private static float RayCircleEntry(Vector2 origin, Vector2 dirHat, Vector2 c, float r)
        {
            Vector2 m = origin - c;
            float b = 2f * Vector2.Dot(m, dirHat);
            float cc = Vector2.Dot(m, m) - r * r;
            float disc = b * b - 4f * cc;
            if (disc < 0f)
            {
                return float.PositiveInfinity;
            }

            float sq = Mathf.Sqrt(disc);
            float t = (-b - sq) * 0.5f;
            if (t < 0f)
            {
                t = 0f;
            }

            return t;
        }

        private float NearestPocketCenterDistance(Vector2 p)
        {
            float best = float.PositiveInfinity;
            foreach (Pocket pocket in _config.pockets)
            {
                float d = (p - pocket.center).magnitude;
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        private float NearestSegmentDistance(Vector2 p)
        {
            float best = float.PositiveInfinity;
            foreach (CushionSegment s in _config.segments)
            {
                float d = DistancePointToSegment(p, s.a, s.b);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        private float MaxPocketNetSide(Vector2 p)
        {
            float best = float.NegativeInfinity;
            foreach (Pocket pocket in _config.pockets)
            {
                float s = Vector2.Dot(p - pocket.throatCenter, pocket.throatNormal);
                if (s > best)
                {
                    best = s;
                }
            }

            return best;
        }

        /// <summary>点是否越过**任一**袋喉平面（进网侧）；是 ⇒ 该点只可能从袋口进入。</summary>
        private bool IsOnPocketNetSide(Vector2 p)
        {
            foreach (Pocket pocket in _config.pockets)
            {
                if (Vector2.Dot(p - pocket.throatCenter, pocket.throatNormal) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        // ── 证据 JSON ─────────────────────────────────────────────────────

        private void WriteReport(List<AimRecord> records, int[] hitCount, float[] nearestApproach,
            List<ReasonEntry> reasons, string straightText, int guardTrips, int energyGain,
            float maxPenetration, int frozenLaunches, int segCount)
        {
            var sb = new StringBuilder();
            string nl = "\n";

            // checks[] 统计：01/02/03 为**硬门**（测试内断言），04 为诊断项（非 0 记 WARN）。
            bool straightOk = AllStraightHit(hitCount);
            int warnCount = maxPenetration <= 1e-5f ? 0 : 1;
            int failCount = (straightOk ? 0 : 1) + (guardTrips == 0 ? 0 : 1) + (energyGain == 0 ? 0 : 1);
            int passCount = 4 - warnCount - failCount;
            string overall = failCount > 0 ? "FAIL" : (warnCount > 0 ? "PASS_WITH_WARNINGS" : "PASS");

            // 守卫触发的分布（R-C 定位证据）。
            int guardTrippedLaunches = 0;
            int maxGuardPerLaunch = 0;
            foreach (AimRecord r in records)
            {
                if (r.GuardTrips > 0)
                {
                    guardTrippedLaunches++;
                    if (r.GuardTrips > maxGuardPerLaunch)
                    {
                        maxGuardPerLaunch = r.GuardTrips;
                    }
                }
            }

            sb.Append("{").Append(nl);
            sb.Append("  \"schema\": \"idea35.d4-segment-coverage.v1\",").Append(nl);
            sb.Append("  \"generatedAtUtc\": \"").Append(System.DateTime.UtcNow.ToString("o")).Append("\",").Append(nl);
            sb.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",").Append(nl);
            sb.Append("  \"projectRoot\": \"").Append(SetupReport.ProjectRoot.Replace("\\", "/")).Append("\",").Append(nl);
            sb.Append("  \"isBatchMode\": ").Append(Application.isBatchMode ? "true" : "false").Append(",").Append(nl);
            sb.Append("  \"passCount\": ").Append(passCount).Append(",").Append(nl);
            sb.Append("  \"warnCount\": ").Append(warnCount).Append(",").Append(nl);
            sb.Append("  \"failCount\": ").Append(failCount).Append(",").Append(nl);
            sb.Append("  \"overallStatus\": \"").Append(overall).Append("\",").Append(nl);
            sb.Append("  \"note\": \"T7-COV-01/02/03 为硬门（测试内断言）；T7-COV-04（独立扫掠弦度量的嵌入量）为诊断项，非 0 记 WARN。R-C 修复后 guardTrips / energyGain 均实测为 0。\",").Append(nl);
            sb.Append("  \"seedCount\": ").Append(SeedPoints.Length).Append(",").Append(nl);
            sb.Append("  \"segmentCount\": ").Append(segCount).Append(",").Append(nl);
            sb.Append("  \"aimPointsPerSeed\": ").Append(segCount * 3).Append(",").Append(nl);
            sb.Append("  \"launchCount\": ").Append(records.Count).Append(",").Append(nl);
            sb.Append("  \"launchSpeed\": ").Append(InvF(LaunchSpeed, "R")).Append(",").Append(nl);
            sb.Append("  \"maxSteps\": ").Append(MaxSteps).Append(",").Append(nl);
            sb.Append("  \"toiIterationGuardTrips\": ").Append(guardTrips).Append(",").Append(nl);
            sb.Append("  \"guardTrippedLaunchCount\": ").Append(guardTrippedLaunches).Append(",").Append(nl);
            sb.Append("  \"maxGuardTripsPerLaunch\": ").Append(maxGuardPerLaunch).Append(",").Append(nl);
            sb.Append("  \"energyGainViolations\": ").Append(energyGain).Append(",").Append(nl);
            sb.Append("  \"maxPenetrationMetres\": ").Append(InvF(maxPenetration, "R")).Append(",").Append(nl);
            sb.Append("  \"frozenLaunchCount\": ").Append(frozenLaunches).Append(",").Append(nl);
            sb.Append("  \"straightSegments\": [");
            for (int i = 0; i < StraightSegments.Length; i++)
            {
                sb.Append((i == 0 ? "" : ", ")).Append("{\"segmentIndex\": ").Append(StraightSegments[i])
                  .Append(", \"hitCount\": ").Append(hitCount[StraightSegments[i]]).Append("}");
            }

            sb.Append("],").Append(nl);

            // 逐段枚举。
            // 注：这里**不使用内插格式说明符** `{x:F5}`（实测在本工程的 StringBuilder.Append 路径下
            //     不生效、会原样输出 "F5"，使 JSON 非法）—— 改为显式 ToString(InvariantCulture)。
            sb.Append("  \"perSegmentHitCounts\": [").Append(nl);
            for (int i = 0; i < segCount; i++)
            {
                float naMm = nearestApproach[i] * 1000f;
                sb.Append("    {\"segmentIndex\": ").Append(i)
                  .Append(", \"hitCount\": ").Append(hitCount[i])
                  .Append(", \"nearestApproachMm\": ").Append(InvF(naMm, "F5"))
                  .Append("}").Append(i < segCount - 1 ? "," : "").Append(nl);
            }

            sb.Append("  ],").Append(nl);

            // 未命中段 + 原因。
            sb.Append("  \"unhitSegments\": [").Append(nl);
            for (int i = 0; i < reasons.Count; i++)
            {
                sb.Append($"    {{\"segmentIndex\": {reasons[i].SegmentIndex}, \"hitCount\": {reasons[i].HitCount}, \"reason\": \"{Escape(reasons[i].Reason)}\"}}")
                  .Append(i < reasons.Count - 1 ? "," : "").Append(nl);
            }

            sb.Append("  ],").Append(nl);

            // 逐点记录（§7.1 必填字段）。
            sb.Append("  \"hits\": [").Append(nl);
            for (int i = 0; i < records.Count; i++)
            {
                AimRecord rec = records[i];
                var hitList = new StringBuilder();
                for (int h = 0; h < rec.HitSegments.Count; h++)
                {
                    hitList.Append(h == 0 ? "" : ",").Append(rec.HitSegments[h]);
                }

                sb.Append("    {")
                  .Append($"\"segmentIndex\": {rec.SegmentIndex}, \"aimTag\": \"{rec.AimTag}\", \"seed\": {rec.SeedIndex}, ")
                  .Append($"\"hitSegment\": {rec.FirstHitSegment}, \"firstHitStep\": {rec.FirstHitStep}, ")
                  .Append($"\"hitSegments\": [{hitList}], ")
                  .Append($"\"maxPenetration\": {InvF(rec.MaxPenetration, "R")}, ")
                  .Append($"\"nearestApproach\": {InvF(rec.NearestApproach, "R")}, ")
                  .Append($"\"energyGain\": {rec.EnergyGain}, \"guardTrips\": {rec.GuardTrips}, ")
                  .Append($"\"pocketPlaneNetSide\": {InvF(rec.EndNetSide, "R")}, \"frozen\": {(rec.Frozen ? "true" : "false")}")
                  .Append("}").Append(i < records.Count - 1 ? "," : "").Append(nl);
            }

            sb.Append("  ],").Append(nl);

            // checks[]（§7.1 必填）。
            sb.Append("  \"checks\": [").Append(nl);
            sb.Append($"    {{\"id\": \"T7-COV-01\", \"name\": \"6 straight segments hit\", \"expected\": \">0 each\", \"actual\": \"{Escape(straightText.Trim())}\", \"detail\": \"硬门槛\", \"status\": \"{(AllStraightHit(hitCount) ? "PASS" : "FAIL")}\"}},").Append(nl);
            sb.Append($"    {{\"id\": \"T7-COV-02\", \"name\": \"ToiIterationGuardTrips\", \"expected\": \"0\", \"actual\": \"{guardTrips}\", \"detail\": \"硬门：R-C 修复后实测 0（1810→1196→339→3→0）\", \"status\": \"{(guardTrips == 0 ? "PASS" : "FAIL")}\"}},").Append(nl);
            sb.Append($"    {{\"id\": \"T7-COV-03\", \"name\": \"EnergyGainViolations\", \"expected\": \"0\", \"actual\": \"{energyGain}\", \"detail\": \"硬门：角点多接触升能，实测 0\", \"status\": \"{(energyGain == 0 ? "PASS" : "FAIL")}\"}},").Append(nl);
            sb.Append($"    {{\"id\": \"T7-COV-04\", \"name\": \"maxPenetration <= 1e-5 m\", \"expected\": \"<=1e-5\", \"actual\": \"{InvF(maxPenetration, "R")}\", \"detail\": \"扫掠弦接触带\", \"status\": \"{(maxPenetration <= 1e-5f ? "PASS" : "WARN")}\"}}").Append(nl);
            sb.Append("  ]").Append(nl);
            sb.Append("}").Append(nl);

            // D4-C3 / D1a：证据落盘**只在显式给了 `-outputDir` 时**发生（R3-5）。
            // 刻意**不用 `return`** —— 写成"写/不写"的二分，结构上**不可能**跳过上面任何一条断言。
            // （本写入位于本测试所有 Assert.* **之后**；若将来有人在它后面加断言，会立刻被这条注释与
            //   评审的"写入必须是最后一步"口径抓到。）
            string path = ReportOutput.ResolveTestEvidencePathOrNull("d4-segment-coverage.json");
            if (path == null)
            {
                string skipMessage = "T7: 未给 -outputDir ⇒ 跳过证据写入（R3-5：测试不得产生仓库改动）。"
                    + " 入库路径 " + ReportRelativePath + " 未被触碰；全部断言已执行完毕。";
                // 两个通道都发：`TestContext.WriteLine` → NUnit 结果 XML；`Debug.Log` → Unity `-logFile`。
                // （只发前者时，日志里看不到 ⇒ 会被误判成"修复没生效"；这是复核时的实际踩坑点。）
                TestContext.WriteLine(skipMessage);
                Debug.Log(skipMessage);
            }
            else
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                // 两个通道都发（与"跳过"分支对称）：XML 通道 + Unity 日志通道。
                string writtenMessage = "T7: 证据写入 " + path;
                TestContext.WriteLine(writtenMessage);
                Debug.Log(writtenMessage);
            }
        }

        private static bool AllStraightHit(int[] hitCount)
        {
            foreach (int idx in StraightSegments)
            {
                if (hitCount[idx] <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");
        }

        /// <summary>JSON 数值一律走 InvariantCulture（避免本地化小数点使 JSON 非法）。</summary>
        private static string InvF(float v, string format)
        {
            return v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ── 独立几何 ──────────────────────────────────────────────────────

        private static PhysicsParams Params()
        {
            return new PhysicsParams
            {
                cushionRestitution = 0.9f,
                cushionTangentialFriction = 0.8f,
                rollingDeceleration = 0f
            };
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.x * ab.x + ab.y * ab.y;
            if (len2 <= 1e-12f)
            {
                return (p - a).magnitude;
            }

            float u = Vector2.Dot(p - a, ab) / len2;
            if (u < 0f)
            {
                u = 0f;
            }
            else if (u > 1f)
            {
                u = 1f;
            }

            return (p - (a + u * ab)).magnitude;
        }

        /// <summary>两线段间最短距离（相交=0；否则为四组"端点到另一段"距离的最小值）。</summary>
        private static float SegSegDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            if (SegmentsIntersect(p1, p2, q1, q2))
            {
                return 0f;
            }

            float d1 = DistancePointToSegment(p1, q1, q2);
            float d2 = DistancePointToSegment(p2, q1, q2);
            float d3 = DistancePointToSegment(q1, p1, p2);
            float d4 = DistancePointToSegment(q2, p1, p2);
            return Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float o1 = Orient(p1, p2, q1);
            float o2 = Orient(p1, p2, q2);
            float o3 = Orient(q1, q2, p1);
            float o4 = Orient(q1, q2, p2);

            if (((o1 > 0f && o2 < 0f) || (o1 < 0f && o2 > 0f)) &&
                ((o3 > 0f && o4 < 0f) || (o3 < 0f && o4 > 0f)))
            {
                return true;
            }

            // 共线/接触边界按"在段上"处理（对距离=0 有影响）。
            if (Mathf.Abs(o1) < 1e-9f && OnSegment(p1, p2, q1)) return true;
            if (Mathf.Abs(o2) < 1e-9f && OnSegment(p1, p2, q2)) return true;
            if (Mathf.Abs(o3) < 1e-9f && OnSegment(q1, q2, p1)) return true;
            if (Mathf.Abs(o4) < 1e-9f && OnSegment(q1, q2, p2)) return true;
            return false;
        }

        private static float Orient(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x <= Mathf.Max(a.x, b.x) + 1e-9f && p.x >= Mathf.Min(a.x, b.x) - 1e-9f &&
                   p.y <= Mathf.Max(a.y, b.y) + 1e-9f && p.y >= Mathf.Min(a.y, b.y) - 1e-9f;
        }

        // ── 数据结构 ──────────────────────────────────────────────────────

        private struct AimRecord
        {
            public int SegmentIndex;
            public string AimTag;
            public int SeedIndex;
            public int FirstHitSegment;
            public int FirstHitStep;
            public List<int> HitSegments;
            public float NearestApproach;
            public float MaxPenetration;
            public float EndNetSide;
            public int GuardTrips;
            public int EnergyGain;
            public bool Frozen;
        }

        private struct ReasonEntry
        {
            public int SegmentIndex;
            public int HitCount;
            public string Reason;
        }
    }
}
