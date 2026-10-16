using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **"连续对局"契约**：`PhysicsTestDrive.Launch()` **不得**把整盘重置（`T65`–`T69`）。
    ///
    /// ── 背景（用户实机报的缺陷）────────────────────────────────────────────
    /// 旧 `Launch()` 每次都 `BuildRack()` + `LoadBalls()` ⇒ **16 颗球摆回开球位、白球回到 `footSpot`**；
    /// 再叠一处 `ResetDiagnostics()`（当时它还会清 `_capturedPocket`）⇒ **已入袋的球每杆"复活"**。
    /// 用户要的是"**球滚到哪就从哪打**"。
    ///
    /// ── 三条内核状态陷阱（本文件正是它们的牙）──────────────────────────────
    /// ① `LoadBalls` 清所有球的 `_frozen`/`_capturedPocket` ⇒ "复制状态 + `LoadBalls`"会**复活入袋球**（`T68`）；
    /// ② `ResetDiagnostics` 原会清 `_capturedPocket` ⇒ 出杆前清计数会**抹掉入袋记录**（`T68`）；
    /// ③ `_vAvg` 是端点均速缓存 —— ⚠ **实测**：`AdvanceSubStep` 在每个子步开头都会重算它**且重算早于任何读取**
    ///    ⇒ 出杆时不同步它**不可观测**（`PlaceBall` 的同步是**防御性**的；`T69` 因此**不**声称能抓它，
    ///    详见 `PoolSimulator.PlaceBall` 的文档）。
    ///
    /// ⚠ 本工程 NUnit **无 `Assert.Multiple`**（`CS0117`）⇒ 需要多条一起验的地方**手工汇总失败项**。
    /// </summary>
    public class PhysicsTestDriveLaunchStateTests
    {
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>推进上限（步）。白球满力（4.5 m/s）在新出厂口径下约 6.4 s ⇒ 768 步；此处留 5 倍余量。</summary>
        private const int RestCap = 4000;

        // ── 辅助 ──────────────────────────────────────────────────────────

        private static TableConfig Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(t, "需要 " + TablePath);
            return t;
        }

        /// <summary>
        /// 本文件全部用例的**小堆球数**（目标球 3 颗 ⇒ 加白球共 4 颗）。
        ///
        /// ⚠ **为什么显式钉成 3，而不是让正式默认值(15)决定**：本文件的用例都是**单元级**物理行为
        /// （出杆不清盘 / 出杆前置门 / 状态转移…），它们的摆位与步数上限**是在 4 球摆放下量出来的**。
        /// 本轮把正式球数从 3 改成 15（用户要求"15 个球 + 1 个白球"）后，满堆会让这些单元用例
        /// 面对完全不同的接触场景 —— 那是**另一件事**，由 `T111`（满堆开球必须收敛）与
        /// `T122`（场景里恰好 16 颗且逐号材质）覆盖。
        /// 把"球堆多大"从隐式默认变成**显式参数**，正是本轮踩的坑（默认值曾悄悄让场上只剩 4 颗）。
        /// </summary>
        private const int SmallRackBallCount = 3;

        private static PhysicsTestDrive NewDrive(GameObject go, bool rack)
        {
            var drive = go.AddComponent<PhysicsTestDrive>();
            drive.tableConfig = Table();
            drive.loadRackForBallBallDemo = rack;
            drive.rackBallCount = SmallRackBallCount;
            drive.Initialize();
            Assert.IsNotNull(drive.Simulator, "Initialize 后应有内核。");
            return drive;
        }

        /// <summary>推进到"整盘静止"；返回实际步数（不判成败 —— 由调用方断言）。</summary>
        private static int AdvanceToRest(PhysicsTestDrive drive, int cap)
        {
            int steps = 0;
            while (steps < cap && !drive.Simulator.TableAtRest)
            {
                drive.Advance(PoolSimulator.FixedDeltaTime);
                steps++;
            }

            return steps;
        }

        /// <summary>摆法签名（球号 → 位置，逐位可复现比较用；`P1` 判据 (d) 用）。</summary>
        private static string RackSignature(Vector2[] positions)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < positions.Length; i++)
            {
                sb.Append(i).Append(':').Append(RVec(positions[i])).Append(';');
            }

            return sb.ToString();
        }

        /// <summary>`PoolSimulator.cs` 源码路径（`P2` 白盒守卫用）。</summary>
        private const string PoolSimulatorSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs";

        /// <summary>逐行剥掉 `//` 之后注释（`P2` 守卫用；命名带后缀避免与既有重名）。</summary>
        private static string StripCommentsP2(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            return sb.ToString();
        }

        private static string RVec(Vector2 v)
            => v.x.ToString("R", CultureInfo.InvariantCulture) + ";"
             + v.y.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// 全盘状态快照（**逐位**串）：每球的 位置 / 速度 / 入袋索引 / 是否在场。
        /// 用途：断言"出杆被拒绝时**任何状态都没动**"（含入袋记录与在场标记）。
        /// </summary>
        private static string Snapshot(PhysicsTestDrive drive)
        {
            IReadOnlyList<BallState> balls = drive.Simulator.Balls;
            var sb = new StringBuilder();
            for (int i = 0; i < balls.Count; i++)
            {
                sb.Append(i).Append(':').Append(RVec(balls[i].position)).Append('|')
                  .Append(RVec(balls[i].velocity)).Append('|')
                  .Append(drive.Simulator.CapturedPocketIndex(i).ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(drive.Simulator.IsBallInPlay(i) ? '1' : '0').Append('\n');
            }

            return sb.ToString();
        }

        private static List<int> CapturedRecords(PhysicsTestDrive drive)
        {
            IReadOnlyList<BallState> balls = drive.Simulator.Balls;
            var list = new List<int>(balls.Count);
            for (int i = 0; i < balls.Count; i++)
            {
                list.Add(drive.Simulator.CapturedPocketIndex(i));
            }

            return list;
        }

        /// <summary>取一个**中袋**（`kind == "side"`，取第一个）。用于确定性入袋。</summary>
        private static Pocket SidePocket(TableConfig table)
        {
            for (int i = 0; i < table.PocketCount; i++)
            {
                Pocket p = table.GetPocket(i);
                if (p.kind == "side")
                {
                    return p;
                }
            }

            Assert.Fail("测试数据里没有中袋，用例前提不成立。");
            return default;
        }

        // ══════════════════ T65 其他球不被重置 ═════════════════════════════════

        /// <summary>
        /// **出杆不得移动任何"非白球"**。
        ///
        /// 做法：球堆模式（白球 + 3）⇒ 推进到静止 ⇒ 记录非白球初始坐标 ⇒ **朝球堆**（`180°` ⇒ 物理 `−x`）
        /// 出杆 ⇒ 再推进到静止 ⇒ **前置断言：至少一颗非白球确实动过**（否则本判据与"摆回原位"不可区分）
        /// ⇒ 记录非白球坐标（逐位串）⇒ **再出杆** ⇒ 断言这些坐标**逐位不变**。
        ///
        /// **能测出**：`Launch()` 把整盘摆回开球位（本缺陷本体）。
        /// **注入负对照（实测）**：把 `BuildRack()` + `LoadBalls()` 加回 `Launch()` ⇒ FAIL。
        ///
        /// ⚠ **两次实测教训（务必保留，否则会重复踩）**：
        /// ① **不要把 `Simulator.Balls` 的数组引用缓存到 `Launch()` 之前**：
        ///    `Balls` 是 `=> _balls` 的**活引用**，而 `LoadBalls` 会**重新分配** `_balls`
        ///    ⇒ 注入版（走 `LoadBalls`）下，缓存下来的旧数组**不再被仿真更新**，
        ///    于是"非白球没动"是**假象**、判据退化成**真空**（实测：l65 注入下 `maxMoved == 0.0` 而非 FAIL）。
        ///    ⇒ 本用例**每次 `Launch()` 之后重新取** `drive.Simulator.Balls`。
        /// ② **必须有"非白球确实动过"的前置断言**：若第一杆压根没碰到球堆，
        ///    "整盘重置"与"什么都不做"**观测等价**，判据同样真空。
        /// </summary>
        [Test]
        public void T65_Launch_DoesNotMoveOtherBalls()
        {
            var go = new GameObject("T65Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: true);

                // 正式对局的目标球数必须是 **15**（用户要求"15 个球 + 1 个白球"）——
                // 这里用**字面量 15** 而不是引用常量自比：常量自己改了自己当然还是相等，
                // 那种断言是恒真的（铁律 9）。本文件其余用例显式用小堆，故这一条是"正式值"的守。
                Assert.AreEqual(15, PhysicsTestDrive.RackBallCount,
                    "T65：正式对局的目标球数应为 15（白球 + 15 = 16 颗）。");

                Assert.AreEqual(4, drive.Simulator.Balls.Count, "T65：球堆模式应为 白球 + 3 颗。");

                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T65：前提 —— 摆好后应在上限内静止。");

                var initial = new List<Vector2>();
                for (int i = 1; i < drive.Simulator.Balls.Count; i++)
                {
                    initial.Add(drive.Simulator.Balls[i].position);
                }

                drive.aimDegrees = 180f;   // 物理 −x：**朝球堆**，让非白球真的散开
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T65：前提 —— 第一杆应被接受。");
                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T65：前提 —— 第一杆应在上限内静止。");

                // ★ 前置断言：至少一颗非白球**确实动过** —— 否则本判据无法区分"整盘重置"与"什么都不做"。
                // ⚠ 这里**重新取** `Balls`（见 summary 的教训①）。
                float maxMoved = 0f;
                for (int i = 1; i < drive.Simulator.Balls.Count; i++)
                {
                    maxMoved = Mathf.Max(maxMoved, (drive.Simulator.Balls[i].position - initial[i - 1]).magnitude);
                }

                Assert.Greater(maxMoved, 0.01f,
                    "T65：前提不成立 —— 第一杆后没有任何非白球移动"
                    + "（本判据会退化成真空判据，无法抓住“整盘重置”）。");

                var before = new List<string>();
                for (int i = 1; i < drive.Simulator.Balls.Count; i++)
                {
                    before.Add(RVec(drive.Simulator.Balls[i].position));
                }

                Assert.Greater(drive.Launch().magnitude, 0f, "T65：前提 —— 第二杆应被接受。");
                Assert.IsFalse(drive.LastLaunchRespawnedCueBall, "T65：前提 —— 白球在场上，不该触发重摆。");

                // ⚠ 再次**重新取** `Balls`（`Launch()` 可能换掉数组 —— 注入版就会）。
                var failures = new List<string>();
                for (int i = 1; i < drive.Simulator.Balls.Count; i++)
                {
                    string now = RVec(drive.Simulator.Balls[i].position);
                    if (now != before[i - 1])
                    {
                        failures.Add($"球 {i}：出杆前 {before[i - 1]} → 出杆后 {now}（被“整盘重置”挪动了）。");
                    }
                }

            // ── **35-C P1：球号（下标即球号）+ 8 球 15 颗摆位**（种子注入 ⇒ 可复现）──────────────────
            // 期望值来源：**独立几何式**（`BallDiameter + 2e-4` / `RackApexX` / `s·√3/2`）＋纯函数契约，非复述实现。
            // **怎么打死它**：把"8 号放槽 4"改掉 ⇒ (b) 必红；把种子参数忽略（恒定排法）⇒ (d) 必红。
            float rackSpacing = PoolConstants.BallDiameter + 2e-4f;
            float rackRowSpacing = rackSpacing * 0.8660254f;
            Vector2 rackFootSpot = Table().footSpot;

            Vector2[] rackA = PhysicsTestDrive.EightBallRackPositions(rackFootSpot, 12345);

            // (a) 16 个点（白球 + 15）＋ `[0]` = 白球开球位（逐位）＋ **最小球心距 == s**（与 3 颗版同源几何）
            if (rackA.Length != 16)
            {
                failures.Add($"(a) `EightBallRackPositions` 应返回 16 个点（白球 + 15），实测 {rackA.Length}。");
            }
            else
            {
                if (RVec(rackA[0]) != RVec(PhysicsTestDrive.CueBallHomePosition(rackFootSpot)))
                {
                    failures.Add($"(a) `[0]` 应为白球开球位（逐位），实测 {RVec(rackA[0])}。");
                }

                float minGap = float.MaxValue;
                for (int i = 1; i < rackA.Length; i++)
                {
                    for (int j = i + 1; j < rackA.Length; j++)
                    {
                        minGap = Mathf.Min(minGap, (rackA[i] - rackA[j]).magnitude);
                    }
                }

                if (!(Mathf.Abs(minGap - rackSpacing) <= 1e-5f))
                {
                    failures.Add($"(a) 15 颗最小球心距应 == {rackSpacing:R}（同源几何），实测 {minGap:R}。");
                }
            }

            // (b) **8 号在第三排正中**（逐位；期望 = 独立几何式）
            if (rackA.Length == 16)
            {
                var expectedEight = new Vector2(PhysicsTestDrive.RackApexX - 2f * rackRowSpacing, 0f);
                if (RVec(rackA[8]) != RVec(expectedEight))
                {
                    failures.Add($"(b) **8 号应在第三排正中** {RVec(expectedEight)}（逐位），实测 {RVec(rackA[8])}。");
                }
            }

            // (c) **底排两角一实一花**（角 = 几何上 |y| 最大的两颗；实色 1..7 / 花色 9..15）
            if (rackA.Length == 16)
            {
                int bestBall1 = -1;
                int bestBall2 = -1;
                float bestY1 = -1f;
                float bestY2 = -1f;
                for (int ball = 1; ball <= 15; ball++)
                {
                    float ay = Mathf.Abs(rackA[ball].y);
                    if (ay > bestY1)
                    {
                        bestY2 = bestY1;
                        bestBall2 = bestBall1;
                        bestY1 = ay;
                        bestBall1 = ball;
                    }
                    else if (ay > bestY2)
                    {
                        bestY2 = ay;
                        bestBall2 = ball;
                    }
                }

                if (bestBall1 < 0 || bestBall2 < 0)
                {
                    failures.Add("(c) 无法从几何上识别底排两角（|y| 最大的两颗）。");
                }
                else
                {
                    bool c1Solid = bestBall1 >= 1 && bestBall1 <= 7;
                    bool c2Solid = bestBall2 >= 1 && bestBall2 <= 7;
                    if (c1Solid == c2Solid)
                    {
                        failures.Add($"(c) 底排两角应**一实一花**，实测两颗同为「{(c1Solid ? "实色" : "花色")}」"
                                     + $"（球号 {bestBall1} 与 {bestBall2}）。");
                    }
                }
            }

            // (d) **可复现**：同种子逐位相同；多种子中**至少 2 种**不同（避免"假随机"）
            Vector2[] rackAgain = PhysicsTestDrive.EightBallRackPositions(rackFootSpot, 12345);
            for (int ball = 0; ball < Mathf.Min(rackA.Length, rackAgain.Length); ball++)
            {
                if (RVec(rackA[ball]) != RVec(rackAgain[ball]))
                {
                    failures.Add($"(d) 同种子两次应逐位相同，球 {ball}：{RVec(rackA[ball])} ≠ {RVec(rackAgain[ball])}。");
                    break;
                }
            }

            string firstSig = RackSignature(PhysicsTestDrive.EightBallRackPositions(rackFootSpot, 1));
            bool sawDifferent = false;
            for (int seed = 2; seed <= 8; seed++)
            {
                if (RackSignature(PhysicsTestDrive.EightBallRackPositions(rackFootSpot, seed)) != firstSig)
                {
                    sawDifferent = true;
                    break;
                }
            }

            if (!sawDifferent)
            {
                failures.Add("(d) 不同种子应产生不同摆法（否则随机未生效 = 假随机）。");
            }

            // (e) **不误伤 3 颗版**：仍 4 个点、最小球心距仍 == s
            Vector2[] rack3 = PhysicsTestDrive.RackPositions(rackFootSpot, 3);
            if (rack3.Length != 4)
            {
                failures.Add($"(e) 3 颗版应返回 4 个点，实测 {rack3.Length}。");
            }
            else
            {
                float minGap3 = float.MaxValue;
                for (int i = 1; i < rack3.Length; i++)
                {
                    for (int j = i + 1; j < rack3.Length; j++)
                    {
                        minGap3 = Mathf.Min(minGap3, (rack3[i] - rack3[j]).magnitude);
                    }
                }

                if (!(Mathf.Abs(minGap3 - rackSpacing) <= 1e-5f))
                {
                    failures.Add($"(e) 3 颗版最小球心距应 == {rackSpacing:R}（未误伤），实测 {minGap3:R}。");
                }
            }

                Assert.IsEmpty(failures,
                    "T65：`Launch()` **不得**移动任何非白球（整盘重置缺陷）：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T65: 第一杆后非白球最大位移 {maxMoved:F4} m；第二杆后其余 3 球坐标逐位不变；"
                                      + $"白球停在 {RVec(drive.Simulator.Balls[0].position)} ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ══════════════════ T66 白球从停下的地方出发 ═══════════════════════════

        /// <summary>
        /// **第二杆的白球位置 == 第一杆打完的静止位置**（"滚到哪就从哪打"）。
        ///
        /// 做法：单球模式 ⇒ 静止 ⇒ 满力出杆 ⇒ 推进到静止 ⇒ 记录白球位置 ⇒ **再出杆** ⇒ 立即断言位置未变。
        /// 前提断言：静止位置**确实不在<span>开球位</span>**（否则本判据无区分度 —— 会与"被摆回开球位"混淆）。
        /// ⚠ 参照点用 `PhysicsTestDrive.CueBallHomePosition(footSpot)` 而**不是** `footSpot`：
        ///   用户裁定开球距离改为 1.27 m 后，白球起点离 `footSpot` 已有 0.46 m ⇒ 拿 `footSpot` 当参照会让本前提**恒真**。
        ///
        /// **能测出**：白球被摆回**开球位**（本缺陷的另一半）。
        /// **注入负对照（实测）**：把 `Launch()` 里的起点写成 `footSpot` ⇒ FAIL。
        /// </summary>
        [Test]
        public void T66_Launch_CueBallStartsWhereItStopped()
        {
            var go = new GameObject("T66Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);
                Assert.AreEqual(1, drive.Simulator.Balls.Count, "T66：单球模式应恰好 1 颗球。");

                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T66：前提 —— 摆好后应在上限内静止。");

                drive.aimDegrees = 180f;   // 世界走向 ≈ +x（沿长库，不朝袋口）
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T66：前提 —— 第一杆应被接受。");
                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T66：前提 —— 第一杆应在上限内静止。");

                Vector2 stopped = drive.Simulator.Balls[0].position;
                // ⚠ 参照点 = **开球位**（不是 `footSpot`）：用户裁定开球距离改为 1.27 m 后，
                //   白球**起点本身**离 `footSpot` 已有 0.46 m ⇒ 若仍拿 `footSpot` 当参照，
                //   本条前提会**恒真**（失去"白球确实滚离了起始位"的判别力）。
                Vector2 cueHome = PhysicsTestDrive.CueBallHomePosition(Table().footSpot);
                Assert.Greater((stopped - cueHome).magnitude, 0.05f,
                    "T66：前提 —— 白球应确实滚离了开球位（否则本判据与“被摆回开球位”不可区分）。");
                Assert.IsTrue(drive.Simulator.IsBallInPlay(0), "T66：前提 —— 白球应仍在场上（未入袋）。");

                Assert.Greater(drive.Launch().magnitude, 0f, "T66：前提 —— 第二杆应被接受。");
                Assert.IsFalse(drive.LastLaunchRespawnedCueBall, "T66：白球在场上，不得触发重摆。");

                Vector2 after = drive.Simulator.Balls[0].position;
                Assert.Less((after - stopped).magnitude, 1e-9f,
                    $"T66：第二杆的白球位置应 == 它停下的地方 {RVec(stopped)}，实测 {RVec(after)}"
                    + "（被摆回开球点 = 整盘重置缺陷）。");
                TestContext.WriteLine($"T66: 第二杆白球仍在 {RVec(after)}（开球位 {RVec(cueHome)}）✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ══════════════════ T67 未静止出杆被拒绝 ═══════════════════════════════

        /// <summary>
        /// **整盘未静止 ⇒ 拒绝出杆**：返回 `Vector2.zero`，且**所有球的位置/速度/入袋索引/在场标记逐位不变**。
        ///
        /// 两种"未静止"都测：
        /// (a) **刚 `Initialize()`**（`RestStableFrames` 尚未满足 ⇒ `TableAtRest == false`）；
        /// (b) **球在动**（已出杆并推进 1 步）。
        ///
        /// **能测出**：门控缺失 / 门控写成"总是放行"。
        /// **注入负对照（实测）**：删掉 `Launch()` 里的 `TableAtRest` 门控 ⇒ FAIL。
        /// </summary>
        [Test]
        public void T67_Launch_RejectedWhileTableNotAtRest()
        {
            var go = new GameObject("T67Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: true);
                drive.aimDegrees = 0f;
                drive.power = 1f;

                // (a) 刚 Initialize：球位静止但 `RestStableFrames` 还没满足 ⇒ 仍须拒绝
                Assert.IsFalse(drive.Simulator.TableAtRest, "T67(a)：前提 —— 刚 Initialize 时 TableAtRest 应为 false。");
                string s0 = Snapshot(drive);
                Vector2 r0 = drive.Launch();
                Assert.AreEqual(Vector2.zero, r0, "T67(a)：未静止（刚摆好、未满足 RestStableFrames）必须拒绝出杆（返回零向量）。");
                Assert.AreEqual(s0, Snapshot(drive), "T67(a)：被拒绝时**不得**改任何状态。");

                // (b) 球在动
                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T67：前提 —— 应在上限内静止。");
                Assert.Greater(drive.Launch().magnitude, 0f, "T67：前提 —— 静止时出杆应被接受。");
                drive.Advance(PoolSimulator.FixedDeltaTime);   // 只推一步 ⇒ 球在动

                Assert.IsFalse(drive.Simulator.TableAtRest, "T67(b)：前提 —— 推进一步后 TableAtRest 应为 false。");
                string s1 = Snapshot(drive);
                Vector2 r1 = drive.Launch();
                Assert.AreEqual(Vector2.zero, r1, "T67(b)：球在动时必须拒绝出杆（返回零向量）。");
                Assert.AreEqual(s1, Snapshot(drive),
                    "T67(b)：被拒绝时**不得**改任何状态（位置 / 速度 / 入袋索引 / 在场标记）。");

                TestContext.WriteLine("T67: (a) 刚 Initialize、(b) 推进一步后，两次出杆均被拒绝且状态逐位不变 ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ══════════════════ T68 已入袋的球不许复活 ═════════════════════════════

        /// <summary>
        /// ★ **已入袋的球不得因为再出一杆而"复活"**（陷阱 ①② 的牙）。
        ///
        /// 做法：球堆模式 ⇒ 静止 ⇒ 用内核的 `PlaceBall(1, …)` 把 **1 号球**放到某中袋喉口内侧并给一个
        /// 朝袋内的初速 ⇒ 推进到静止（此时它**确定性入袋**）⇒ 断言前提（已入袋 + 不在场上）
        /// ⇒ **出杆** ⇒ 断言 1 号球**仍入袋、仍不在场上**，且**所有球**的入袋索引逐位不变。
        ///
        /// **能测出**：新入口被写成"整盘重置"的省事版本（清 `_capturedPocket` / 清 `_frozen`）。
        /// **注入负对照（实测）**：在 `PlaceBall` 里加一行"把所有球的 `_frozen`/`_capturedPocket` 清掉" ⇒ FAIL。
        /// </summary>
        [Test]
        public void T68_Launch_DoesNotReviveCapturedBalls()
        {
            var go = new GameObject("T68Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: true);
                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T68：前提 —— 摆好后应在上限内静止。");

                // 把 1 号球放到中袋喉口**内侧**并朝袋内给 1 m/s ⇒ 沿 `throatNormal` 直行 ⇒ 确定性入袋。
                Pocket pocket = SidePocket(Table());
                Vector2 tn = pocket.throatNormal;
                drive.Simulator.PlaceBall(1, pocket.throatCenter - 0.15f * tn, 1.0f * tn);

                int steps = AdvanceToRest(drive, RestCap);
                Assert.GreaterOrEqual(drive.Simulator.CapturedPocketIndex(1), 0,
                    $"T68：前提 —— 1 号球应已入袋（{steps} 步后仍未入袋；中袋喉心 {pocket.throatCenter}／法向 {tn}）。");
                Assert.IsFalse(drive.Simulator.IsBallInPlay(1), "T68：前提 —— 已入袋的球应“不在场上”。");
                Assert.IsTrue(drive.Simulator.TableAtRest, "T68：前提 —— 入袋后整盘应静止。");

                List<int> before = CapturedRecords(drive);

                drive.aimDegrees = 0f;
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T68：前提 —— 出杆应被接受。");

                var failures = new List<string>();
                if (drive.Simulator.IsBallInPlay(1))
                {
                    failures.Add("1 号球**复活**了（`IsBallInPlay(1)` 为 true ⇒ 被摆回场上）。");
                }

                List<int> after = CapturedRecords(drive);
                for (int i = 0; i < before.Count; i++)
                {
                    if (after[i] != before[i])
                    {
                        failures.Add($"球 {i} 的入袋索引由 {before[i]} 变成 {after[i]}。");
                    }
                }

                Assert.IsEmpty(failures,
                    "T68：出杆**不得**复活已入袋的球、也不得改动任何球的入袋记录：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T68: 1 号球入袋索引 {after[1]}（仍在袋），出杆后全盘入袋记录逐位不变 ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ══════════════════ T69 第一步不吞位移 ═════════════════════════════════

        /// <summary>
        /// **出杆后推进恰好一步，白球位移 == "整步"而不是"半步"**。
        ///
        /// 期望值来源 = **甲**（**积分夹逼**：`a(v) ≥ 0` ⇒ 速度单调不增 ⇒ 一步位移
        /// `∫v dt ∈ [v_end·T, v_0·T]`，`T = FixedDeltaTime`）。故断言：
        /// 位移 **&lt; 1.02·v_0·T**（不多走）且 **&gt; 0.95·v_0·T**（绝不是半步 `≈ v_0·T/2` 或零步）。
        ///
        /// **能测出**：出杆入口把速度/时间口径写错（例如只给一半速度、或漏掉端点均速）导致第一步吞位移。
        /// **注入负对照（实测）**：把出杆入口的速度乘 `0.5`（"半步"等价物）⇒ FAIL。
        ///
        /// ⚠ **本判据<b>不</b>能抓"漏设 `_vAvg`"**（实测）：`AdvanceSubStep` 在子步开头**先重算** `_vAvg`、
        /// 再读取它 ⇒ 出杆时的 `_vAvg` 值**不可观测**。`PoolSimulator.PlaceBall` 仍会同步它（**防御性**），
        /// 但**不**据此声称有判据覆盖 —— 见该方法的 XML doc。
        /// </summary>
        [Test]
        public void T69_Launch_FirstStepDoesNotEatDisplacement()
        {
            var go = new GameObject("T69Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);   // 单球：路径干净、一步内不触库
                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T69：前提 —— 摆好后应在上限内静止。");

                drive.aimDegrees = 180f;   // 世界走向 ≈ +x
                drive.power = 1f;
                Vector2 v0 = drive.Launch();
                float v0Mag = v0.magnitude;
                Assert.Greater(v0Mag, 0f, "T69：前提 —— 出杆应被接受。");

                IReadOnlyList<BallState> balls = drive.Simulator.Balls;
                Vector2 p0 = balls[0].position;

                drive.Advance(PoolSimulator.FixedDeltaTime);

                float moved = (balls[0].position - p0).magnitude;
                float vEnd = balls[0].velocity.magnitude;
                float t = PoolSimulator.FixedDeltaTime;

                var failures = new List<string>();
                if (!(moved > 0.95f * v0Mag * t))
                {
                    failures.Add($"一步位移 {moved * 1000f:F4} mm 低于 0.95·v0·T = {0.95f * v0Mag * t * 1000f:F4} mm"
                                 + $"（半步 ≈ {0.5f * v0Mag * t * 1000f:F4} mm）⇒ 第一步被吞了位移。");
                }

                if (!(moved < 1.02f * v0Mag * t))
                {
                    failures.Add($"一步位移 {moved * 1000f:F4} mm 超过 1.02·v0·T = {1.02f * v0Mag * t * 1000f:F4} mm"
                                 + " ⇒ 端点均速口径被写错（多走）。");
                }

                if (!(moved > 0.95f * vEnd * t))
                {
                    failures.Add($"一步位移 {moved * 1000f:F4} mm 低于 0.95·v_end·T = {0.95f * vEnd * t * 1000f:F4} mm"
                                 + "（积分下界被破坏）。");
                }

                Assert.IsEmpty(failures, "T69 第一步位移失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T69: v0={v0Mag:F4} / v_end={vEnd:F4} m/s；一步位移 {moved * 1000f:F4} mm"
                                      + $"（夹逼 [{vEnd * t * 1000f:F4}, {v0Mag * t * 1000f:F4}] mm）✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ══════════════════ T70a / T70b 白球入袋 ⇒ **全场自由球**（可观测）════════════

        /// <summary>把白球确定性打进一个中袋（`T70a`/`T70b`/`T141` 共用）。</summary>
        private static void PotCueBall(PhysicsTestDrive drive)
        {
            Pocket pocket = SidePocket(Table());
            Vector2 tn = pocket.throatNormal;
            drive.Simulator.PlaceBall(0, pocket.throatCenter - 0.15f * tn, 1.0f * tn);
            AdvanceToRest(drive, RestCap);
            Assert.GreaterOrEqual(drive.Simulator.CapturedPocketIndex(0), 0, "前提 —— 白球应已入袋。");
            Assert.IsFalse(drive.Simulator.IsBallInPlay(0), "前提 —— 已入袋的白球应不在场上。");
            Assert.IsTrue(drive.Simulator.TableAtRest, "前提 —— 入袋后整盘应静止。");
        }

        /// <summary>
        /// **`T70a`：白球入袋 ⇒ 进入「待放球」⇒ 玩家指定点放下 ⇒ 白球**逐位**在该点、且能正常出杆。**
        ///
        /// **用户原话**：「练习模式白球进袋了，全场自由球，可以把白球放在台面任意位置击球」。
        ///
        /// ⚠ **本判据取代旧 `T70` 的"自动放回开球位"语义**（主理人批准**改语义**、不是重钉数值）：
        /// 旧 `T70` 守的是"**白球被静默摆回 = 整盘重置缺陷**"这条内核意图 —— **它依然成立**，
        /// 只是落点从"自动"变成"玩家指定"；"**逐位、容差 0、不许静默**"三个性质**一个都没放宽** ✓。
        /// 兜底路径（玩家不放、直接出杆）由 **`T70b`** 守。
        ///
        /// **怎么打死它**：把 `TryPlaceCueBall` 写成 no-op ⇒ 红；把落点写成 `CueBallHomePosition`（偷偷自动归位）⇒ 逐位断言红。
        /// </summary>
        [Test]
        public void T70a_CapturedCueBall_GoesToPendingPlacement_ThenPlayerPicksSpot()
        {
            var go = new GameObject("T70aDrive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);
                AdvanceToRest(drive, RestCap);
                PotCueBall(drive);

                Assert.IsTrue(drive.PendingCueBallPlacement,
                    "T70a：白球入袋后应进入**待放球**（全场自由球）—— 不再自动归位。");

                var target = new Vector2(0.60f, 0.25f);
                Assert.IsTrue(drive.CanPlaceCueBall(target), "T70a：前提 —— 该点应是合法落点。");
                Assert.IsTrue(drive.TryPlaceCueBall(target), "T70a：合法落点应放球成功。");

                Assert.IsTrue(drive.Simulator.IsBallInPlay(0), "T70a：放下后白球应在场上。");
                Assert.AreEqual(RVec(target), RVec(drive.Simulator.Balls[0].position),
                    "T70a：白球必须落在**玩家指定的那一点**（**逐位**，容差 0）。");
                Assert.IsFalse(drive.PendingCueBallPlacement, "T70a：放下后应退出待放态。");

                AdvanceToRest(drive, RestCap);
                drive.aimDegrees = 180f;
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T70a：放下后应能**正常出杆**。");

                TestContext.WriteLine($"T70a: 落袋 ⇒ 待放 ⇒ 放在 {RVec(target)} ⇒ 出杆成功 ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// **`T70b`：兜底路径** —— 待放球期间**玩家直接出杆** ⇒ 回落到"放回开球位再出杆"（= 旧行为），
        /// 且 `LastLaunchRespawnedCueBall` 仍为 **true**（**犯规留痕不许丢**）。
        ///
        /// **为什么必须有这条**（主理人裁定「兜底是必需的」）：没有兜底，一旦放置输入失效 / 找不到合法点，
        /// 游戏会**永久卡在待放态**（比"自动放回"更糟的失败模式）。
        ///
        /// **怎么打死它**：删掉 `Launch()` 里的兜底分支 ⇒ 红；把兜底落点改成别处 ⇒ 逐位断言红。
        /// </summary>
        [Test]
        public void T70b_PendingPlacement_LaunchFallsBackToCueBallHome_AndKeepsFoulFlag()
        {
            var go = new GameObject("T70bDrive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);
                AdvanceToRest(drive, RestCap);
                PotCueBall(drive);
                Assert.IsTrue(drive.PendingCueBallPlacement, "T70b：前提 —— 应处于待放球。");

                Vector2 cueHome = PhysicsTestDrive.CueBallHomePosition(Table().footSpot);
                drive.aimDegrees = 180f;
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T70b：待放球期间出杆应被接受（走兜底，不是卡住）。");

                var failures = new List<string>();
                if (!drive.Simulator.IsBallInPlay(0))
                {
                    failures.Add("兜底出杆后白球**不在场上**（静默消失）。");
                }

                float off = (drive.Simulator.Balls[0].position - cueHome).magnitude;
                if (!(off < 1e-6f))
                {
                    failures.Add($"兜底应把白球放回开球位 {RVec(cueHome)}，实测 {RVec(drive.Simulator.Balls[0].position)}（偏 {off:E3} m）。");
                }

                if (!drive.LastLaunchRespawnedCueBall)
                {
                    failures.Add("`LastLaunchRespawnedCueBall` 应为 true —— 犯规留痕**必须可观测**（不许静默）。");
                }

                Assert.IsFalse(drive.PendingCueBallPlacement, "T70b：兜底出杆后应退出待放态。");
                Assert.IsEmpty(failures, "T70b 兜底路径失败：\n" + string.Join("\n", failures));

                TestContext.WriteLine($"T70b: 待放球期间直接出杆 ⇒ 兜底回 {RVec(cueHome)}，"
                                      + "`LastLaunchRespawnedCueBall` = true ✓");

                // ── **F1 状态泄漏负路径**（独立复核抓出；可达路径：白球落袋 ⇒ 按 `R`（无门拦 R）⇒ 卡开 ⇒ `Play again`）──
                // 断言：`ReloadRack()` **必须复位本批两个新状态**；否则 ① 新局误报"待放球"、第一次左键被当成放球
                //（`CanPlaceCueBall` 跳过索引 0 ⇒ 必成功 ⇒ 那次点击把白球挪走而不是瞄准）；② 新局第一杆记一次**假犯规**。
                PotCueBall(drive);
                Assert.IsTrue(drive.PendingCueBallPlacement, "F1：前提 —— 落袋后应处于待放球。");
                drive.ReloadRack();
                // ⚠ 同样用**收集全部失败**范式：F1① 与 F1② 是**两个独立不变**，裸 `Assert` 会掩盖后者。
                var f1Failures = new List<string>();
                if (drive.PendingCueBallPlacement)
                {
                    f1Failures.Add("F1①：`ReloadRack()` 必须复位 `PendingCueBallPlacement` —— 否则新局开局误报待放球、"
                                   + "且**第一次左键被当成放球**（把白球挪走而不是瞄准）。");
                }

                AdvanceToRest(drive, RestCap);
                drive.aimDegrees = 180f;
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "F1：重摆后第一杆应被接受。");
                if (drive.LastLaunchRespawnedCueBall)
                {
                    f1Failures.Add("F1②：新局第一杆**不得**记假犯规 —— `_cueBallLeftTableSinceLastLaunch` 必须随重摆复位"
                                   + "（否则 `LastLaunchRespawnedCueBall = false || true = true` ⇒ `PracticeSession._fouls++`）。");
                }

                Assert.IsEmpty(f1Failures, "F1 重摆状态泄漏（**收集全部**）：\n" + string.Join("\n", f1Failures));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// **`T140`：全场自由球的落点合法性（纯函数核 `PoolSimulator.IsPlacementLegal`）**。
        ///
        /// 六组：① 空地**合法** ② **出界**非法 ③ **贴库边**（球心 = 半长 − 球半径）**合法**（边界可放）
        /// ④ **压别的球**非法 ⑤ **在袋口区域**非法 ⑥ 刚好不压（间距 = `2r`）**合法**。
        /// **口径**：重叠下限复用 `PoolSimulator.BallInitialOverlapTolerance`（**不许另写容差**，铁律 16）。
        ///
        /// **怎么打死它**：去掉"台面内"分支 ⇒ ②红；去掉"不重叠" ⇒ ④红；去掉"不在袋口" ⇒ ⑤红。
        /// </summary>
        [Test]
        public void T140_CueBallPlacementLegality_IsPureAndComplete()
        {
            TableConfig t = Table();
            var pockets = t.pockets;
            float r = PoolConstants.BallRadius;

            Assert.IsTrue(PoolSimulator.IsPlacementLegal(new Vector2(0.5f, 0.2f), null, pockets,
                t.halfLength, t.halfWidth), "T140①：空地应合法。");

            Assert.IsFalse(PoolSimulator.IsPlacementLegal(new Vector2(t.halfLength + 0.05f, 0f), null, pockets,
                t.halfLength, t.halfWidth), "T140②：出界（超库边）应非法。");

            Assert.IsTrue(PoolSimulator.IsPlacementLegal(new Vector2(t.halfLength - r, 0f), null, pockets,
                t.halfLength, t.halfWidth), "T140③：**贴库边**（球心 = 半长 − 球半径）应**合法**（边界可放）。");

            var occupied = new List<Vector2> { new Vector2(0.5f, 0.2f) };
            Assert.IsFalse(PoolSimulator.IsPlacementLegal(new Vector2(0.5f, 0.2f), occupied, pockets,
                t.halfLength, t.halfWidth), "T140④：压别的球应非法。");

            Pocket pocket = SidePocket(t);
            // ⚠ **探针点必须在台面内**（独立复核实测的教训）：`pocket.throatCenter` 的 `|y| = 0.6951 > halfWidth − r + 1e-4`
            //   ⇒ **已被 ①（台面内）拒掉** ⇒ 单关 ③（袋口分支）**⑤ 不会红**（红的是 ⑨）⇒ ⑤ 在本实现下**零独立判别力**。
            //   ⇒ 改用**中袋前方、且在台面内**的点 `(0, 0.6060)`：`|y| = 0.6060 ≤ 0.6065794` **通过 ①**；
            //      到中袋喉心 `(0, 0.6950634)` 的**径向距离 = 0.0890634 ≤ openingRadius + r = 0.0898916**
            //      ⇒ **只有 ③ 会拒** ⇒ ⑤ 从此**独立可判**。
            //      （该点是 ① 放行、**由 ③ 唯一拒掉**的点；**单关 ③ 的注入验证见规格 §12.2.9**。）
            Assert.IsFalse(PoolSimulator.IsPlacementLegal(new Vector2(0f, 0.6060f), null, pockets,
                t.halfLength, t.halfWidth), "T140⑤：**袋口区域**（中袋前方、台面内的点）应非法 —— 这是唯一由 ③ 拒掉的点。");

            var justClear = new List<Vector2> { new Vector2(0.5f, 0.2f) };
            Assert.IsTrue(PoolSimulator.IsPlacementLegal(new Vector2(0.5f + 2f * r, 0.2f), justClear, pockets,
                t.halfLength, t.halfWidth), "T140⑥：间距恰为 `2r` 应**合法**（与初始摆放同口径）。");

            // ⚠ **本用例的 ⑦⑧⑨⑩ 无法在同一次运行中各自被独立观察**（给复核方 / 下一个做注入的人）：
            //   它们同处**一个 `[Test]`**，而 `Assert` 在**首个失败处即中止** ⇒ 例如"把判据改回旧式"只会让
            //   **⑦** 先爆；**⑧**（同判据、不同坐标）**结构上观察不到** ⇒ 它的判别力是**推得**、不是观察。
            //   ⇒ 若将来需要逐条独立留证：把本用例改成「**收集全部失败**」范式（与 `ClubUiTests` 的 failures 列表同范式）。
            //   ⇒ 相关登记见规格 `Docs/35-B_Main入口与瞄准性能_spec.md` §12.2.3。
            //
            // ⑦⑧⑨⑩ **判别用例（权威表 a–d；旧三点已作废 —— 它们区分不了两版，铁律 17）**
            // 口径：**沿库方向偏离袋口**才能区分"喉面法向深度"与"径向距喉心"两版。
            // 下表 a–d 与主理人给的权威表逐点一致（离线按资产真源复算：修前 / 修后）。
            //   a: (x = −0.6, y = 0.6065) 修前 false → **修后必须 true**
            //      ⚠ **本用例的判别力全在 `x` 上**：`x = 0` 时两版**都是** false（那是正对袋口）；
            //        只有 `x = −0.6`（**沿库横向离中袋 600 mm**）才 false→true ⇒ 坐标必须逐字给出、可一条命令复算。
            //   b: (x = −0.6, y = 0.6060) 修前 false → **修后必须 true**
            //   c: (x =  0.0, y = 0.6065) 两版都 false（**正对中袋**）⇒ **必须 false**
            //   d: (x = −0.6, y = 0.5500) 两版都 true（对照）⇒ **必须 true**
            // ⚠ **本组用「收集全部失败」范式**（不是裸 `Assert` 串）—— 规矩 3 的正解：
            //   ⑦⑧⑨⑩ 是**同一个用例里的四个独立不变**，裸 `Assert` 会在 ⑦ 处中止 ⇒ ⑧⑩ **结构上不可独立观察**
            //   （独立复核实测打不死 ⑧）。改成收集范式后，一次注入能让 ⑦ 与 ⑧ **同时**留证。
            var t140Failures = new List<string>();
            void Chk140(bool ok, string msg)
            {
                if (!ok)
                {
                    t140Failures.Add(msg);
                }
            }

            Chk140(PoolSimulator.IsPlacementLegal(new Vector2(-0.6f, 0.6065f), null, pockets, t.halfLength, t.halfWidth),
                "T140⑦（a）：贴长库、横向离中袋 600 mm 必须**合法** —— 旧公式（喉面法向点积）会误拒它，这是唯一能证伪旧实现的点。");
            Chk140(PoolSimulator.IsPlacementLegal(new Vector2(-0.6f, 0.6060f), null, pockets, t.halfLength, t.halfWidth),
                "T140⑧（b）：同上、稍离库一点，也必须合法。");
            Chk140(!PoolSimulator.IsPlacementLegal(new Vector2(0f, 0.6065f), null, pockets, t.halfLength, t.halfWidth),
                "T140⑨（c）：**正对中袋**应非法（两版都必须拒）。");
            Chk140(PoolSimulator.IsPlacementLegal(new Vector2(-0.6f, 0.55f), null, pockets, t.halfLength, t.halfWidth),
                "T140⑩（d）：对照点，两版都合法。");
            // ── **35-C P2：开球线（kitchen）放置契约 + 前向出杆**（折进 T140 ⇒ 计数不变）──────────────
            // **怎么打死它**：① 把严格 `>` 改成 `>=` ⇒ (P2-②) 必红；② 去掉 kitchen 线判定（恒 true）⇒ (P2-②③) 必红。
            var p2Failures = new List<string>();
            float kitchenLineX = PoolConstants.KitchenLineX;

            if (!(Mathf.Abs(kitchenLineX - PoolConstants.ClothHalfLength / 2f) <= 1e-6f))
            {
                p2Failures.Add($"(P2-0) `KitchenLineX` 必须**由 `ClothHalfLength` 派生**"
                               + $"（期望 {PoolConstants.ClothHalfLength / 2f:R}），实测 {kitchenLineX:R}。");
            }

            // 六例边界（y = 0；规范米）
            bool k1 = PoolSimulator.IsPlacementLegalInKitchen(new Vector2(0.97f, 0f), null, pockets, t.halfLength, t.halfWidth, kitchenLineX);
            bool k2 = PoolSimulator.IsPlacementLegalInKitchen(new Vector2(0.635f, 0f), null, pockets, t.halfLength, t.halfWidth, kitchenLineX);
            bool k3 = PoolSimulator.IsPlacementLegalInKitchen(new Vector2(0.30f, 0f), null, pockets, t.halfLength, t.halfWidth, kitchenLineX);
            bool k4 = PoolSimulator.IsPlacementLegalInKitchen(new Vector2(1.24f, 0f), null, pockets, t.halfLength, t.halfWidth, kitchenLineX);
            var occupiedKitchen = new List<Vector2> { new Vector2(0.97f, 0.02f) };
            bool k5 = PoolSimulator.IsPlacementLegalInKitchen(new Vector2(0.97f, 0f), occupiedKitchen, pockets, t.halfLength, t.halfWidth, kitchenLineX);
            var cornerProbe = new Vector2(t.halfLength - PoolConstants.BallRadius, t.halfWidth - PoolConstants.BallRadius);
            bool k6 = PoolSimulator.IsPlacementLegalInKitchen(cornerProbe, null, pockets, t.halfLength, t.halfWidth, kitchenLineX);

            if (!k1)
            {
                p2Failures.Add("(P2-①) `(0.97, 0)`（= 白球开球位）应**合法**。");
            }

            if (k2)
            {
                p2Failures.Add("(P2-②) `(0.635, 0)`（**恰在开球线上**）应**不合法**（严格不等式）。");
            }

            if (k3)
            {
                p2Failures.Add("(P2-③) `(0.30, 0)`（**在线后 = 脚区**）应**不合法**。");
            }

            if (!k4)
            {
                p2Failures.Add("(P2-④) `(1.24, 0)`（**贴头库、仍在台内**）应**合法**。");
            }

            if (k5)
            {
                p2Failures.Add("(P2-⑤) `(0.97, 0)` 但 occupied 含 `(0.97, 0.02)` ⇒ **压球**应**不合法**。");
            }

            if (k6)
            {
                p2Failures.Add($"(P2-⑥) 角袋口附近 `{RVec(cornerProbe)}`（**占袋口**）应**不合法**。");
            }

            // **单点实现守**（白盒：**调用形态**，不是方法名 —— 判方法名会命中**定义**⇒恒红）
            string poolSource = StripCommentsP2(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), PoolSimulatorSourcePath)));
            if (!poolSource.Contains("IsPlacementLegal(position, occupiedCenters, pockets, halfLength, halfWidth)"))
            {
                p2Failures.Add("(P2-⑦) `IsPlacementLegalInKitchen` 必须**调用**既有单点实现 "
                               + "`IsPlacementLegal(position, occupiedCenters, pockets, halfLength, halfWidth)`（不得重写三条规则）。");
            }

            // **前向出杆**（头端 +x ⇒ 前方 = −x）
            const float minForward = 1.0f;
            if (!PoolSimulator.IsBreakShotForwardEnough(new Vector2(-minForward, 0f), minForward))
            {
                p2Failures.Add("(P2-⑧) 前向分量**恰好等于** `minForwardComponent` 应**合法**（判据是 `<=`）。");
            }

            if (PoolSimulator.IsBreakShotForwardEnough(new Vector2(-minForward * 0.99f, 0f), minForward))
            {
                p2Failures.Add("(P2-⑨) 前向分量**略小**于 `minForwardComponent` 应**不合法**。");
            }

            if (PoolSimulator.IsBreakShotForwardEnough(new Vector2(minForward, 0f), minForward))
            {
                p2Failures.Add("(P2-⑩) **反方向（+x）**应**不合法**（前方 = −x = 脚端方向）。");
            }

            Assert.IsEmpty(p2Failures, "T140 P2（kitchen/前向）失败项（**收集全部**）：\n" + string.Join("\n", p2Failures));

            Assert.IsEmpty(t140Failures, "T140⑦–⑩ 失败项（**收集全部**，不在首个失败处中止）：\n"
                + string.Join("\n", t140Failures));

            TestContext.WriteLine("T140: 空地✓ 出界✗ 贴库✓ 压球✗ 袋口✗ 恰不压✓ + a/b/贴合库✓ c/正对中袋✗ d✓");
        }

        /// <summary>
        /// **`T141`：全场自由球端到端 + 犯规留痕**：落袋 ⇒ 待放球 ⇒ **非法点被拒（不改状态、计数上升，不静默）**
        /// ⇒ 合法点放下 ⇒ 白球逐位在该点、能出杆 ⇒ **`LastLaunchRespawnedCueBall` 仍为 true**。
        ///
        /// ⚠ 最后那条是"**把待放球与犯规留痕拆成两个 flag**"那个决定的**守卫**：
        /// 玩家自己放球后 `cueOffTable` 已变 false（那个 flag 的旧写法会把它置 false）⇒ **犯规会被静默丢掉** ⇒ 本条红。
        ///
        /// **怎么打死它**：去掉 `_cueBallLeftTableSinceLastLaunch` 的粘滞位 ⇒ 最后一条断言红；
        /// 把非法落点也照放 ⇒ 第一条断言红。
        /// </summary>
        [Test]
        public void T141_BallInHand_EndToEnd_AndFoulTraceSurvives()
        {
            var go = new GameObject("T141Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);
                AdvanceToRest(drive, RestCap);
                PotCueBall(drive);
                Assert.IsTrue(drive.PendingCueBallPlacement, "T141：前提 —— 应处于待放球。");

                int refusedBefore = drive.RefusedPlacementCount;

                // ① 非法落点（出界）⇒ **不放、状态不变、计数上升**（不静默）
                Assert.IsFalse(drive.TryPlaceCueBall(new Vector2(Table().halfLength + 0.3f, 0f)),
                    "T141①：出界落点应被拒。");
                Assert.IsTrue(drive.PendingCueBallPlacement, "T141①：被拒后应**仍在待放态**（状态不变）。");
                Assert.IsFalse(drive.Simulator.IsBallInPlay(0), "T141①：被拒后白球仍应不在场上。");
                Assert.AreEqual(refusedBefore + 1, drive.RefusedPlacementCount,
                    "T141①：被拒必须**计数上升**（不许静默）。");

                // ② 合法落点 ⇒ 放下
                var target = new Vector2(-0.55f, -0.20f);
                Assert.IsTrue(drive.TryPlaceCueBall(target), "T141②：合法落点应放球成功。");
                Assert.AreEqual(RVec(target), RVec(drive.Simulator.Balls[0].position),
                    "T141②：白球应**逐位**落在玩家指定点。");

                // ③ 能正常出杆 + **犯规留痕仍在**
                AdvanceToRest(drive, RestCap);
                drive.aimDegrees = 180f;
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T141③：放下后应能正常出杆。");
                Assert.IsTrue(drive.LastLaunchRespawnedCueBall,
                    "T141③：**犯规留痕必须仍在** —— 玩家自己放球**不得**让 `LastLaunchRespawnedCueBall` 变 false"
                    + "（否则犯规统计会静默丢杆；这正是「拆两个 flag」要守住的东西）。");

                TestContext.WriteLine($"T141: 被拒 {drive.RefusedPlacementCount} 次；放在 {RVec(target)}；"
                                      + "出杆成功且犯规留痕 = true ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
