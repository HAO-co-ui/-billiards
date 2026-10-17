using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Rules;
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

        /// <summary>
        /// **驱动里实际的球表位置**（下标 = 球号）—— `P5-A` 的 (f)/(g) 用来核对"接线后的摆位"。
        /// ⚠ 取的是**内核球表**（`Simulator.Balls`）而不是再算一遍纯函数：判据要证的是"装配真的用了它"。
        /// </summary>
        private static Vector2[] RackPositionsOf(PhysicsTestDrive drive)
        {
            var positions = new Vector2[drive.Simulator.Balls.Count];
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = drive.Simulator.Balls[i].position;
            }

            return positions;
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
        /// **35-C `P1` 追加（(a)–(e)）**：`EightBallRackPositions` 的纯函数契约（16 点 / 8 号第三排正中 /
        /// 底排两角一实一花 / 同种子逐位相同且不同种子不同 / 不误伤 3 颗版）。
        /// **35-C `P5-A` 追加（(f)–(g)）—— 从"纯函数对"升级到"**装配真的用了它**"**：
        /// · **(f) 接线**：15 颗（正式对局球数）建一个 drive ⇒ 读**内核球表**，要求逐位 ==
        ///   `EightBallRackPositions(footSpot, drive.ResolvedRackSeed)`，且**下标 8 在第三排正中**（独立几何式）；
        /// · **(g) 种子可复现**：同一 `rackSeed` 两次 `Initialize` **逐位相同**；不同 `rackSeed` **不得全等**。
        ///   ⇒ **打死法**：`BuildRack` 的 15 颗分支改回 `RackPositions` ⇒ (f) 红；让它忽略 `ResolvedRackSeed` ⇒ (g) 红。
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

            // ── **35-C P5-A：(f) 15 颗开局必须走"按球号编号"的 8 球摆位（**接线**）+ (g) 种子可复现** ──
            // 期望值来源：**独立几何式**（第三排正中 = `RackApexX − 2·(s·√3/2)` 且 `y = 0`；`s = BallDiameter + 2e-4`）＋ 纯函数契约。
            // **怎么打死它**：把 `BuildRack` 的 15 颗分支改回 `RackPositions` ⇒ (f) 必红（下标 8 不在第三排正中）；
            //   让 `BuildRack` 忽略 `ResolvedRackSeed`（或用常量）⇒ (g) 的"不同 seed 应不同"必红。
            var rackGo = new GameObject("T65RackDrive");
            try
            {
                PhysicsTestDrive rackDrive = rackGo.AddComponent<PhysicsTestDrive>();
                rackDrive.tableConfig = Table();
                rackDrive.loadRackForBallBallDemo = true;
                rackDrive.rackBallCount = PhysicsTestDrive.RackBallCount;   // 15（正式对局球数）
                rackDrive.rackSeed = 4242;                                 // **注入固定种子**
                rackDrive.Initialize();

                if (rackDrive.ResolvedRackSeed != 4242)
                {
                    failures.Add($"(f) 注入的 `rackSeed`（4242）应被原样采用，实测 `ResolvedRackSeed = {rackDrive.ResolvedRackSeed}`。");
                }

                if (rackDrive.Simulator == null || rackDrive.Simulator.Balls.Count != 16)
                {
                    failures.Add($"(f) 15 颗开局应有 16 颗球，实测 "
                                 + $"{(rackDrive.Simulator == null ? -1 : rackDrive.Simulator.Balls.Count)}。");
                }
                else
                {
                    // (f) 逐位 == `EightBallRackPositions(footSpot, ResolvedRackSeed)`（用**驱动自己解析出的**种子）
                    Vector2[] expectedRack = PhysicsTestDrive.EightBallRackPositions(rackFootSpot, rackDrive.ResolvedRackSeed);
                    for (int ball = 0; ball < expectedRack.Length; ball++)
                    {
                        if (RVec(rackDrive.Simulator.Balls[ball].position) != RVec(expectedRack[ball]))
                        {
                            failures.Add($"(f) 球表下标 {ball} 应逐位 == `EightBallRackPositions(seed)` 的 "
                                         + $"{RVec(expectedRack[ball])}，实测 {RVec(rackDrive.Simulator.Balls[ball].position)}"
                                         + "（⇒ `BuildRack` 没走 15 颗的编号摆位）。");
                            break;
                        }
                    }

                    // (f) **接线后的 8 号（下标 8）在第三排正中**（独立几何式，不复述实现）
                    var expectedEightInDrive = new Vector2(PhysicsTestDrive.RackApexX - 2f * rackRowSpacing, 0f);
                    if (RVec(rackDrive.Simulator.Balls[8].position) != RVec(expectedEightInDrive))
                    {
                        failures.Add($"(f) **接线后 8 号（下标 8）应在第三排正中** {RVec(expectedEightInDrive)}（逐位），"
                                     + $"实测 {RVec(rackDrive.Simulator.Balls[8].position)}。");
                    }
                }

                // (g) 同一 `rackSeed` 两次 `Initialize` ⇒ 逐位相同
                string sigFirst = RackSignature(RackPositionsOf(rackDrive));
                rackDrive.Initialize();
                if (RackSignature(RackPositionsOf(rackDrive)) != sigFirst)
                {
                    failures.Add("(g) 同一 `rackSeed` 两次 `Initialize` 应逐位相同（可复现）。");
                }

                // (g) 不同 `rackSeed` ⇒ 至少一处不同（防"种子被忽略 = 假随机"）
                rackDrive.rackSeed = 777;
                rackDrive.Initialize();
                if (RackSignature(RackPositionsOf(rackDrive)) == sigFirst)
                {
                    failures.Add("(g) 不同 `rackSeed` 应产生不同摆法（否则种子被忽略 = 假随机）。");
                }
            }
            finally
            {
                Object.DestroyImmediate(rackGo);
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

                // ⚠ **P10 裁定 (a) 后的前提修正**：单球模式下每一杆都判"空杆犯规" ⇒ 会给对手自由球
                //   ⇒ `IsWaitingForPlacement` 为真 ⇒ 第二杆会被"待放期一律拒杆"这道门拦下。
                //   本项要测的是"**白球从停下的地方**出杆"，所以先把球**放回它停下的位置**（合法、且不改变本项语义），
                //   再出杆。⇒ 旧前提"第二杆应被接受"**不是被打松，而是补上了规则要求的"先放球"**。
                if (drive.IsWaitingForPlacement)
                {
                    Assert.IsTrue(drive.TryPlaceCueBall(stopped), "T66：前提 —— 应能把白球放回它停下的位置。");
                    AdvanceToRest(drive, RestCap);
                }

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
        /// <summary>
        /// **`T149`：瞄准白线的渲染口径一致性扫描**（P10-D）—— **只读**。
        /// 4320 杆 = 12 起点 × 360 方向；数据源只许 `ShotPredictor.Predict` + **渲染真正用的那对**
        /// （`AimAssistView.VisiblePathVertexCount` / `prediction.VisiblePathEnd`）。
        /// **G1** 末段强接 >5cm；**G2** drawnLength<5cm 且 |起点−end|>20cm；**G3** >1mm
        /// （球-球只用 `|end − GhostBall|`，`FirstContactPosition` 是两球中点、拿它量必假红）；
        /// **G4** |path[0] − 起点| > 1mm。
        /// ⚠ 自证覆盖面：`scanned+skipped == 4320` 且 `contactCases > 0`，否则"0 命中"是**空扫**。
        /// </summary>
        [Test]
        public void T149a_AimPath_RenderConsistency_Scan_144()
        {
            var failures = new List<string>();
            var go = new GameObject("T149Scan");
            try
            {
                PhysicsTestDrive drive = NewAiDrive(go, rackSeed: 71);
                AdvanceToRest(drive, RestCap);

                string viewSrc = File.ReadAllText(Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? ".",
                    "Assets/_Project/Scripts/Runtime/Physics/AimAssistView.cs"));
                if (!viewSrc.Contains("path[use - 1] != end"))
                {
                    failures.Add("(P10-D) 源码守卫失败：`SyncPathLine` 里的 `path[use - 1] != end`（补末点判定）不见了"
                                 + " ⇒ 判据的 drawnLength 与渲染画的不再同一口径。");
                }

                // ── **扫描规模：默认小网格（常驻），分段跑满（按需）** ────────────────────
                // ⚠ 为什么必须分开：满扫 = 12 起点 × 360 方向 × `maxSteps(4000)` ⇒ 会让**任何人**跑全套时超窗口
                //   （实测被工具上限 SIGTERM 掐断、无 XML ⇒ 只能靠"没跑完"来发现，极危险）。
                //   ⇒ 常驻只用 12 × 12（30° 步长）= 144 杆；要满扫时设 `P10D_SEG=0..11`，
                //     每段 = 12 起点 × 30 方向（1° 步长）= 360 杆，**12 段合计 4320**。
                //   ⚠ 每段**只自证自己那 360**；"合计 4320"由**跨段聚合**断言（单段不许声称全扫过）。
                string segEnv = System.Environment.GetEnvironmentVariable("P10D_SEG");
                bool segmented = !string.IsNullOrEmpty(segEnv);
                int segIndex = 0;
                int dirCount = 12;
                int dirStepDeg = 30;
                if (segmented && (!int.TryParse(segEnv, out segIndex) || segIndex < 0 || segIndex > 11))
                {
                    Assert.Fail($"P10-D：`P10D_SEG` 取值非法（应为 0..11），实测 {segEnv}。");
                }

                if (segmented)
                {
                    dirCount = 30;
                    dirStepDeg = 1;
                }

                int degBase = segmented ? segIndex * 30 : 0;

                // **步数上限对齐产品**：`AimAssistController.maxPredictSteps = 240`（`P10D_MAXSTEPS` 可设压力档）。
                // ⚠ 用 4000 测出来的耗时**不能代表产品**：真机冒烟 600 帧 / 361 次预测 / 帧时上限 7.05 ms / 掉帧 0
                //   ⇒ 单次预测远小于编辑器侧 Stopwatch 读数（Mono 未优化 + 4 参重载每次新建工作区）。
                int maxSteps = 240;
                string maxStepsEnv = System.Environment.GetEnvironmentVariable("P10D_MAXSTEPS");
                int maxStepsParsed;
                if (!string.IsNullOrEmpty(maxStepsEnv) && int.TryParse(maxStepsEnv, out maxStepsParsed) && maxStepsParsed > 0)
                {
                    maxSteps = maxStepsParsed;
                }

                Vector2[] starts =
                {
                    new Vector2(-0.80f, 0.30f), new Vector2(-0.60f, -0.25f), new Vector2(-0.40f, 0.45f),
                    new Vector2(-0.20f, -0.45f), new Vector2(0.00f, 0.30f), new Vector2(0.20f, -0.30f),
                    new Vector2(0.40f, 0.45f), new Vector2(0.60f, -0.20f), new Vector2(0.80f, 0.30f),
                    new Vector2(-0.90f, -0.40f), new Vector2(0.90f, -0.40f), new Vector2(0.00f, 0.00f),
                };

                int expectedTotal = dirCount * starts.Length;

                int g1 = 0, g2 = 0, g3 = 0, g4 = 0, scanned = 0, skipped = 0, contactCases = 0, stepsHitCap = 0;
                var worst = new List<(float err, string line)>();
                const float speed = 5.0f;
                float twoR = 2f * PoolConstants.BallRadius;
                // 一个采样步长（m）与其 1.5 倍上界 —— G1 / 输出的共同口径（**可推导，不是魔法常数**）
                float stepMeters = speed * PoolSimulator.FixedDeltaTime;
                float g1Limit = 1.5f * stepMeters;

                for (int si = 0; si < starts.Length; si++)
                {
                    bool overlap = false;
                    for (int b = 1; b < drive.Simulator.Balls.Count; b++)
                    {
                        if (drive.Simulator.IsBallInPlay(b)
                            && (drive.Simulator.Balls[b].position - starts[si]).magnitude < twoR)
                        {
                            overlap = true;
                            break;
                        }
                    }

                    if (overlap)
                    {
                        skipped += dirCount;
                        continue;
                    }

                    drive.Simulator.PlaceBall(0, starts[si], Vector2.zero);
                    for (int k = 0; k < dirCount; k++)
                    {
                        int deg = (degBase + k * dirStepDeg) % 360;
                        float rad = deg * Mathf.Deg2Rad;
                        Vector2 v = new Vector2(-Mathf.Cos(rad), -Mathf.Sin(rad)) * speed;
                        ShotPrediction pred = ShotPredictor.Predict(drive.tableConfig, drive.Simulator, v, maxSteps);
                        IReadOnlyList<Vector2> path = pred.CueWorldPath;
                        if (path == null || path.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        scanned++;
                        if (pred.HasFirstContact)
                        {
                            contactCases++;
                        }

                        // ⚠ 跑到步数上限 = **成本信号**，**不是**"线被硬切"：多见于只撞库、没有球-球接触的杆
                        //   （循环只等球-球接触，见 `ShotPredictor.cs` 的 `while (steps < maxSteps && !probe.HasBallBallContact)`）。
                        //   真机冒烟 600 帧里 361 次预测、帧时上限 7.05 ms、掉帧 0 ⇒ 这条不影响观感，别据此改产品。
                        if (pred.StepsSimulated >= maxSteps)
                        {
                            stepsHitCap++;
                        }

                        int use = AimAssistView.VisiblePathVertexCount(pred, path);
                        Vector2 end = pred.VisiblePathEnd;
                        float drawn = 0f;
                        for (int i = 1; i < use; i++)
                        {
                            drawn += (path[i] - path[i - 1]).magnitude;
                        }

                        if (use < path.Count && path[use - 1] != end)
                        {
                            drawn += (end - path[use - 1]).magnitude;
                        }

                        float g1d = use < path.Count ? (path[use - 1] - end).magnitude : 0f;
                        float g2d = (starts[si] - end).magnitude;
                        float g3d = 0f;
                        // ⚠ 别再用 `-`：`HasFirstContact` 走**球-球**口径，只撞库时为 false，印成 `-` 会看起来像"空杆"
                        //   （实测 `!HasFirstContactEvent` 为 0 ⇒ 根本没有"什么都没碰到"的杆）。
                        string g3kind = pred.HasFirstContactEvent ? "event" : "none";
                        if (pred.HasFirstContact)
                        {
                            if (pred.FirstContactKind == ContactKind.BallBall)
                            {
                                g3d = (end - pred.GhostBall).magnitude;
                                g3kind = "ball";
                            }
                            else
                            {
                                g3d = (end - pred.FirstContactPosition).magnitude;
                                g3kind = "cushion";
                            }
                        }

                        float g4d = (path[0] - starts[si]).magnitude;
                        // **G1 的可推导上界**（`g1Limit` = 1.5 × 一个采样步长，见上方声明）：
                        //   前缀停在接触前最后一个采样顶点 ⇒ 末段长度 ≈ 一个步长（实测 40.1 mm @ 5 m/s、20.4 mm @ 2.5 m/s）。
                        //   ⚠ **不要**用魔法常数（例如 35.7 mm）：那会把所有正常杆判成缺陷，再逼人去加密采样点。
                        if (g1d > g1Limit) { g1++; }
                        if (drawn < 0.05f && g2d > 0.2f) { g2++; }
                        if (g3d > 0.001f) { g3++; }
                        if (g4d > 0.001f) { g4++; }

                        float worstErr = Mathf.Max(Mathf.Max(g1d * 1000f, g3d * 1000f), g4d * 1000f);
                        if (worstErr > 1f)
                        {
                            worst.Add((worstErr,
                                $"起点#{si} {deg}° use={use}/{path.Count} 首碰={g3kind} "
                                + $"G1={g1d * 1000f:F1}mm G2drawn={drawn * 1000f:F1}mm G3={g3d * 1000f:F1}mm "
                                + $"G4={g4d * 1000f:F1}mm |起点−end|={g2d * 1000f:F1}mm"));
                        }
                    }
                }

                worst.Sort((a, b) => b.err.CompareTo(a.err));
                var sb = new StringBuilder();
                sb.Append($"[P10-D] seg={(segmented ? segIndex.ToString() : "a")} maxSteps={maxSteps} scanned={scanned} skipped={skipped} contactCases={contactCases} "
                          + $"合计={scanned + skipped}（应 {expectedTotal}）⇒ G1={g1} G2={g2} G3={g3} G4={g4} stepsHitCap={stepsHitCap} "
                          + $"stepMm={stepMeters * 1000f:F1}mm g1Limit={g1Limit * 1000f:F1}mm");
                for (int i = 0; i < Mathf.Min(10, worst.Count); i++)
                {
                    sb.Append("\n  ").Append(worst[i].line);
                }

                TestContext.WriteLine(sb.ToString());

                if (scanned + skipped != expectedTotal)
                {
                    failures.Add($"P10-D 覆盖面失败：scanned+skipped 应 == {expectedTotal}"
                                 + $"（seg={(segmented ? segIndex.ToString() : "a")}；12×12 常驻 / 每段 12×30）"
                                 + $"，实测 {scanned}+{skipped}。");
                }

                if (contactCases <= 0)
                {
                    failures.Add("P10-D 覆盖面失败：**没有任何一杆产生首接触** ⇒ 0 命中是**空扫**，不能当证据。");
                }

                Assert.IsEmpty(failures, "T149 失败：\n" + string.Join("\n", failures));
                drive.Simulator.PlaceBall(0, PhysicsTestDrive.CueBallHomePosition(Table().footSpot), Vector2.zero);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// **`T149c`：白线末段"一个采样步长"假说的可证伪检验 + 4001 步的定性取证**（P10-D，只读）。
        ///
        /// 假说（主理人）：`G1` 量到的 ~40 mm **不是断裂**，而是**一个采样步长** `speed × FixedDeltaTime`
        /// （5 m/s × 1/120 = 41.7 mm）。⇒ 可证伪：**速度减半时它应近似减半（≈20 mm）**。
        /// 若 2.5 m/s 下仍是 ~40 mm ⇒ 假说错，转查几何。
        ///
        /// 另给 4001 步那类的五个数（判"预测截断"是否**真是用户看到的线的截断**）。
        /// </summary>
        [Test]
        public void T149c_SampleStepHypothesis_And_TruncationDiagnostics()
        {
            // **观测用例（默认跳过）**：两档速度全网格 ≈ 93 s，会把全套从 ~1 min 拖到 ~4 min。
            // 它不是判据、只是"末段长度 = 一个采样步长"的取证工具 ⇒ 设 `P10_PERF=1` 才跑。
            if (System.Environment.GetEnvironmentVariable("P10_PERF") != "1")
            {
                Assert.Ignore("观测用例：设 P10_PERF=1 运行（默认跳过以免拖慢全套）");
            }

            var sb = new StringBuilder();
            var go = new GameObject("T149c");
            try
            {
                PhysicsTestDrive drive = NewAiDrive(go, rackSeed: 71);
                AdvanceToRest(drive, RestCap);
                float dt = PoolSimulator.FixedDeltaTime;

                Vector2[] starts =
                {
                    new Vector2(-0.80f, 0.30f), new Vector2(-0.60f, -0.25f), new Vector2(-0.40f, 0.45f),
                    new Vector2(-0.20f, -0.45f), new Vector2(0.00f, 0.30f), new Vector2(0.20f, -0.30f),
                    new Vector2(0.40f, 0.45f), new Vector2(0.60f, -0.20f), new Vector2(0.80f, 0.30f),
                    new Vector2(-0.90f, -0.40f), new Vector2(0.90f, -0.40f), new Vector2(0.00f, 0.00f),
                };

                sb.Append($"FixedDeltaTime={dt * 1000f:F2}ms\n");
                foreach (float speed in new[] { 5.0f, 2.5f })
                {
                    float maxG1 = 0f;
                    int cap = 0, noEvent = 0, capSmallCount = 0;
                    var capUse = new List<int>();
                    for (int si = 0; si < starts.Length; si++)
                    {
                        drive.Simulator.PlaceBall(0, starts[si], Vector2.zero);
                        for (int deg = 0; deg < 360; deg += 30)
                        {
                            float rad = deg * Mathf.Deg2Rad;
                            Vector2 v = new Vector2(-Mathf.Cos(rad), -Mathf.Sin(rad)) * speed;
                            ShotPrediction pred = ShotPredictor.Predict(drive.tableConfig, drive.Simulator, v, 4000);
                            IReadOnlyList<Vector2> path = pred.CueWorldPath;
                            if (path == null || path.Count == 0)
                            {
                                continue;
                            }

                            int use = AimAssistView.VisiblePathVertexCount(pred, path);
                            float g1 = use < path.Count ? (path[use - 1] - pred.VisiblePathEnd).magnitude : 0f;
                            if (g1 > maxG1)
                            {
                                maxG1 = g1;
                            }

                            if (pred.StepsSimulated >= 4000)
                            {
                                cap++;
                                capUse.Add(pred.VisiblePathCount);
                                if (!pred.HasFirstContactEvent)
                                {
                                    noEvent++;
                                }

                                if (pred.VisiblePathCount <= 100)
                                {
                                    capSmallCount++;
                                }
                            }
                        }
                    }

                    sb.Append($"[speed={speed}] maxG1={maxG1 * 1000f:F1}mm  理论步长={speed * dt * 1000f:F1}mm  "
                              + $"上限(1.5×)=${speed * dt * 1000f * 1.5f:F1}mm  "
                              + $"stepsHitCap={cap}  其中!HasFirstContactEvent={noEvent}  其中VisiblePathCount<=100={capSmallCount}\n");
                    if (capUse.Count > 0)
                    {
                        capUse.Sort();
                        sb.Append($"  cap 的 VisiblePathCount：min={capUse[0]} 中位={capUse[capUse.Count / 2]} max={capUse[capUse.Count - 1]}\n");
                    }
                }

                // 4001 步那一例的五个数（起点#9 / 90°）
                drive.Simulator.PlaceBall(0, starts[9], Vector2.zero);
                float r90 = 90f * Mathf.Deg2Rad;
                ShotPrediction odd = ShotPredictor.Predict(drive.tableConfig, drive.Simulator,
                    new Vector2(-Mathf.Cos(r90), -Mathf.Sin(r90)) * 5.0f, 4000);
                Vector2 oddEnd = odd.VisiblePathEnd;
                float nearestCushion = float.MaxValue;
                for (int pi = 0; pi < drive.tableConfig.PocketCount; pi++)
                {
                    // 用库边矩形近似：到四条库边的最近距离
                }

                float halfLen = 1.27f;
                float halfWid = 0.635f;
                nearestCushion = Mathf.Min(
                    Mathf.Min(halfLen - Mathf.Abs(oddEnd.x), halfWid - Mathf.Abs(oddEnd.y)),
                    999f);
                sb.Append($"[odd] VisiblePathCount={odd.VisiblePathCount} path.Count={odd.CueWorldPath.Count} "
                          + $"end=({oddEnd.x:F3},{oddEnd.y:F3}) |起点−end|={(starts[9] - oddEnd).magnitude * 1000f:F1}mm "
                          + $"到最近库边(近似)={nearestCushion * 1000f:F1}mm "
                          + $"HasCushionFirstContact={odd.HasCushionFirstContact} HasFirstContactEvent={odd.HasFirstContactEvent} "
                          + $"StepsSimulated={odd.StepsSimulated}");

                TestContext.WriteLine(sb.ToString());
                drive.Simulator.PlaceBall(0, PhysicsTestDrive.CueBallHomePosition(Table().footSpot), Vector2.zero);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>**`T150`：同一杆连续预测 200 次必须逐位相同**（P10-D 确定性；只读）。</summary>
        [Test]
        public void T150_Prediction_SameShot_200x_IsBitIdentical()
        {
            var failures = new List<string>();
            var go = new GameObject("T150");
            try
            {
                PhysicsTestDrive drive = NewAiDrive(go, rackSeed: 71);
                AdvanceToRest(drive, RestCap);
                Vector2 start = new Vector2(-0.80f, 0.30f);
                drive.Simulator.PlaceBall(0, start, Vector2.zero);
                Vector2 v = new Vector2(-Mathf.Cos(0.7f), -Mathf.Sin(0.7f)) * 5.0f;

                ShotPrediction first = ShotPredictor.Predict(drive.tableConfig, drive.Simulator, v, 4000);
                string refKey = PathKey(first);
                int compared = 0;
                for (int i = 0; i < 200; i++)
                {
                    ShotPrediction p = ShotPredictor.Predict(drive.tableConfig, drive.Simulator, v, 4000);
                    compared++;
                    if (PathKey(p) != refKey)
                    {
                        failures.Add($"(T150) 第 {i} 次预测与首次**不一致**（path/end/count/kind 任一不同）⇒ 预测不确定。");
                        break;
                    }
                }

                if (compared != 200)
                {
                    failures.Add($"(T150) 覆盖面失败：应比较 200 次，实测 {compared}。");
                }

                if (first.CueWorldPath == null || first.CueWorldPath.Count == 0)
                {
                    failures.Add("(T150) 覆盖面失败：path.Count == 0 ⇒ 空扫。");
                }

                TestContext.WriteLine($"[P10-D/T150] compared={compared} pathCount={first.CueWorldPath.Count} "
                                      + $"visibleCount={first.VisiblePathCount} ok={failures.Count == 0}");
                Assert.IsEmpty(failures, "T150 失败：\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>**`T151`：交错预测 A,B,A,B… 各 100 轮，A/B 各自结果必须完全一致**（抓"上一杆污染下一杆"）。</summary>
        [Test]
        public void T151_Prediction_Interleaved_AB_NoCrossContamination()
        {
            // **观测用例（默认跳过）**：100 轮 A/B 交错 ≈ 51 s。确定性判据由 `T150`（同一杆 200 次逐位相同）
            // 承担；本条是"跨杆污染"的加严取证 ⇒ 设 `P10_PERF=1` 才跑。
            if (System.Environment.GetEnvironmentVariable("P10_PERF") != "1")
            {
                Assert.Ignore("观测用例：设 P10_PERF=1 运行（默认跳过以免拖慢全套）");
            }

            var failures = new List<string>();
            var go = new GameObject("T151");
            try
            {
                PhysicsTestDrive drive = NewAiDrive(go, rackSeed: 71);
                AdvanceToRest(drive, RestCap);

                Vector2 sa = new Vector2(-0.80f, 0.30f);
                Vector2 sb = new Vector2(0.40f, 0.45f);
                Vector2 va = new Vector2(-Mathf.Cos(0.7f), -Mathf.Sin(0.7f)) * 5.0f;
                Vector2 vb = new Vector2(-Mathf.Cos(2.1f), -Mathf.Sin(2.1f)) * 3.0f;

                drive.Simulator.PlaceBall(0, sa, Vector2.zero);
                string refA = PathKey(ShotPredictor.Predict(drive.tableConfig, drive.Simulator, va, 4000));
                drive.Simulator.PlaceBall(0, sb, Vector2.zero);
                string refB = PathKey(ShotPredictor.Predict(drive.tableConfig, drive.Simulator, vb, 4000));

                int roundsA = 0, roundsB = 0;
                for (int i = 0; i < 100; i++)
                {
                    drive.Simulator.PlaceBall(0, sa, Vector2.zero);
                    if (PathKey(ShotPredictor.Predict(drive.tableConfig, drive.Simulator, va, 4000)) == refA)
                    {
                        roundsA++;
                    }

                    drive.Simulator.PlaceBall(0, sb, Vector2.zero);
                    if (PathKey(ShotPredictor.Predict(drive.tableConfig, drive.Simulator, vb, 4000)) == refB)
                    {
                        roundsB++;
                    }
                }

                if (roundsA != 100 || roundsB != 100)
                {
                    failures.Add($"(T151) 交错预测出现**交叉污染**：A 一致 {roundsA}/100、B 一致 {roundsB}/100。");
                }

                TestContext.WriteLine($"[P10-D/T151] roundsA={roundsA} roundsB={roundsB} ok={failures.Count == 0}");
                Assert.IsEmpty(failures, "T151 失败：\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// **把一次预测压成一个哈希**（P10-D：**整条路径逐位**比较，不取前 64 点）。
        /// `path.Count` + `VisiblePathCount` + 端点位模式 + 首碰三个布尔 + **整条 `CueWorldPath` 的 x/y 位模式**
        /// 依次滚 **FNV-1a**（`System.BitConverter.SingleToInt32Bits`）⇒ O(n) 数值运算、无字符串分配。
        /// ⚠ 返回串里带 `path.Count`，因为**哈希相等必须同时长度相等**（不同长度可能碰撞）。
        /// ⚠ `in` 参数**不能**被局部函数捕获，所以 `Mix` 用 `ref` 传哈希。
        /// </summary>
        private static string PathKey(in ShotPrediction p)
        {
            uint h = 2166136261u;
            IReadOnlyList<Vector2> path = p.CueWorldPath;
            Mix(ref h, path == null ? -1 : path.Count);
            Mix(ref h, p.VisiblePathCount);
            Mix(ref h, System.BitConverter.SingleToInt32Bits(p.VisiblePathEnd.x));
            Mix(ref h, System.BitConverter.SingleToInt32Bits(p.VisiblePathEnd.y));
            Mix(ref h, (int)p.FirstContactKind);
            Mix(ref h, (p.HasFirstContact ? 1 : 0) | (p.HasCushionFirstContact ? 2 : 0) | (p.HasFirstContactEvent ? 4 : 0));
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)   // **整条**
                {
                    Mix(ref h, System.BitConverter.SingleToInt32Bits(path[i].x));
                    Mix(ref h, System.BitConverter.SingleToInt32Bits(path[i].y));
                }
            }

            return $"{h:X8}/{path?.Count ?? -1}";
        }

        /// <summary>FNV-1a 滚一步（`ref` 传哈希，避免在带 `in` 参数的方法里被局部函数捕获）。</summary>
        private static void Mix(ref uint h, int v)
        {
            unchecked
            {
                h ^= (uint)v;
                h *= 16777619u;
            }
        }

        /// <summary>**`T152b`：E-1 用**产品参数 `maxSteps=240`** 重测（只读）** —— 与 `T152` 的 4000 压力档分开。</summary>
        [Test]
        public void T152b_CostProfile_ProductMaxSteps240()
        {
            // **观测用例（默认跳过）**：约 9 s，且量的是**编辑器侧**耗时 —— 已用真机冒烟证伪"预测是瓶颈"，
            //   这条只作留档，**不得驱动产品改动**（详见 `Docs/测试数基线台账.md` 的 P10-J 段）。
            if (System.Environment.GetEnvironmentVariable("P10_PERF") != "1")
            {
                Assert.Ignore("观测用例：设 P10_PERF=1 运行（默认跳过以免拖慢全套）");
            }

            var go = new GameObject("T152b");
            try
            {
                PhysicsTestDrive drive = NewAiDrive(go, rackSeed: 71);
                AdvanceToRest(drive, RestCap);
                const int productMaxSteps = 240;   // = AimAssistController.maxPredictSteps

                Vector2[] starts =
                {
                    new Vector2(-0.80f, 0.30f), new Vector2(-0.60f, -0.25f), new Vector2(-0.40f, 0.45f),
                    new Vector2(-0.20f, -0.45f), new Vector2(0.00f, 0.30f), new Vector2(0.20f, -0.30f),
                    new Vector2(0.40f, 0.45f), new Vector2(0.60f, -0.20f), new Vector2(0.80f, 0.30f),
                    new Vector2(-0.90f, -0.40f), new Vector2(0.90f, -0.40f), new Vector2(0.00f, 0.00f),
                };

                var ratios = new List<float>();
                int n = 0, cap = 0;
                for (int si = 0; si < starts.Length; si++)
                {
                    drive.Simulator.PlaceBall(0, starts[si], Vector2.zero);
                    for (int deg = 0; deg < 360; deg += 30)
                    {
                        float rad = deg * Mathf.Deg2Rad;
                        Vector2 v = new Vector2(-Mathf.Cos(rad), -Mathf.Sin(rad)) * 5.0f;
                        ShotPrediction p = ShotPredictor.Predict(drive.tableConfig, drive.Simulator, v, productMaxSteps);
                        if (p.CueWorldPath == null || p.CueWorldPath.Count == 0)
                        {
                            continue;
                        }

                        n++;
                        if (p.StepsSimulated >= productMaxSteps)
                        {
                            cap++;
                        }

                        ratios.Add(p.StepsSimulated / (float)Mathf.Max(1, p.VisiblePathCount));
                    }
                }

                ratios.Sort();
                float med = ratios.Count > 0 ? ratios[ratios.Count / 2] : 0f;
                float max = ratios.Count > 0 ? ratios[ratios.Count - 1] : 0f;

                drive.Simulator.PlaceBall(0, starts[0], Vector2.zero);
                Vector2 tv = new Vector2(-Mathf.Cos(0.7f), -Mathf.Sin(0.7f)) * 5.0f;
                const int reps = 200;
                var ms = new List<float>(reps);
                for (int i = 0; i < reps; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    ShotPredictor.Predict(drive.tableConfig, drive.Simulator, tv, productMaxSteps);
                    sw.Stop();
                    ms.Add((float)sw.Elapsed.TotalMilliseconds);
                }

                ms.Sort();
                TestContext.WriteLine($"[P10-E1/240] n={n} cap={cap} 浪费比 中位={med:F1} 最大={max:F1} "
                                      + $"| Predict 中位={ms[reps / 2]:F3}ms p90={ms[(int)(reps * 0.9)]:F3}ms "
                                      + $"（节流周期 33.33ms）");
                if (n != 144)
                {
                    Assert.Fail($"T152b 覆盖面失败：应扫 144 杆，实测 {n}。");
                }

                drive.Simulator.PlaceBall(0, PhysicsTestDrive.CueBallHomePosition(Table().footSpot), Vector2.zero);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

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
        /// **`T70b`：待放球期间**出杆必须被拒**（P8-E，用户要求：白球落袋后「重新拿出来悬浮等待放置」）。
        ///
        /// 四条一起断：① `Launch()` 返回零向量；② `RefusedPendingPlacementCount` **+1**（拒绝**不许静默**）；
        /// ③ 白球**仍不在场上**、且**仍在待放态**（不许偷偷归位代打）；④ 悬浮出口可读且**离台面有高度**。
        /// 显式出路：`ReturnCueBallToHome()`（`Esc`「放回开球点」）仍可用 ⇒ 放回后必须能正常出杆（**不硬卡死**）。
        ///
        /// ⚠ **P8 重钉**：旧版断言「走兜底 ⇒ 回开球位 ⇒ `LastLaunchRespawnedCueBall = true`」——
        /// 那正是用户**要求撤销**的行为。旧期望**不是被打松，而是被替换**；
        /// 犯规留痕（`LastLaunchRespawnedCueBall`）改由 `_cueBallLeftTableSinceLastLaunch` 承担，见下方 F1② 仍守。
        /// </summary>
        [Test]
        public void T70b_PendingPlacement_LaunchIsRefused_AndStaysPending_UntilPlaced()
        {
            var go = new GameObject("T70bDrive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);
                AdvanceToRest(drive, RestCap);
                PotCueBall(drive);
                Assert.IsTrue(drive.PendingCueBallPlacement, "T70b：前提 —— 应处于待放球。");

                var failures = new List<string>();
                int refusedBefore = drive.RefusedLaunchWhileAwaitingCount;   // P10 裁定 (a)：待放期一律拒杆
                int launchesBefore = drive.LaunchCount;
                drive.aimDegrees = 180f;
                drive.power = 1f;
                if (drive.Launch().magnitude > 0f)
                {
                    failures.Add("**待放球期间出杆必须被拒**（用户要求「白球落袋后等放置」），实测打出去了。");
                }

                if (drive.LaunchCount != launchesBefore)
                {
                    failures.Add($"被拒时 `LaunchCount` 不该涨（{launchesBefore} → {drive.LaunchCount}）。");
                }

                if (drive.RefusedLaunchWhileAwaitingCount != refusedBefore + 1)
                {
                    failures.Add($"被拒**必须留痕**（`RefusedLaunchWhileAwaitingCount` 应 +1），"
                                 + $"实测 {refusedBefore} → {drive.RefusedLaunchWhileAwaitingCount}。");
                }

                if (!drive.PendingCueBallPlacement)
                {
                    failures.Add("被拒后应**仍在待放态**。");
                }

                if (drive.Simulator.IsBallInPlay(0))
                {
                    failures.Add("被拒后白球**仍不应在场上**（不许偷偷归位代打）。");
                }

                // **悬浮出口**：待放态下必须能读到世界坐标，且**离台面有高度**（贴地就看不出「悬浮」）
                Vector3 floatWorld = drive.CueBallFloatWorldPosition;
                if (floatWorld.y <= PhysicsTestDrive.CueBallFloatHeightMeters * 0.5f)
                {
                    failures.Add($"待放态悬浮高度应≈{PhysicsTestDrive.CueBallFloatHeightMeters:F3} m，实测 y={floatWorld.y:F3}。");
                }

                Assert.IsEmpty(failures, "T70b 待放态拒绝路径失败：\n" + string.Join("\n", failures));

                // **显式出路：不硬卡死**（玩家按 Esc 放回开球点后必须能正常出杆）
                drive.ReturnCueBallToHome();
                Assert.IsFalse(drive.PendingCueBallPlacement, "T70b：放回开球点后应退出待放态。");
                // ⚠ `PlaceBall` 会把"静止计数"清零 ⇒ 必须再推到静止，否则这一杆会被 `TableAtRest` 门拒
                //   （那会与"待放态不许出杆"混成同一个症状，判据就分不清是哪道门了）。
                AdvanceToRest(drive, RestCap);
                Assert.Greater(drive.Launch().magnitude, 0f, "T70b：放回开球点后应能**正常出杆**（不硬卡死）。");

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


        /// <summary>`PointerPowerController` 源码路径（T145 ⑧ 的调用点守卫用）。</summary>
        private const string PointerPowerSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PointerPowerController.cs";

        /// <summary>`CueStickController` 源码路径（P7⑧「出杆后杆不跟球」的**接线守卫**用）。</summary>
        private const string CueStickSourcePath = "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs";

        /// <summary>逐行剥掉 `//` 之后注释（T145 ⑧ 守卫用；避免注释里的片段被数成调用点）。</summary>
        private static string StripCommentLines(string source)
        {
            var sb = new StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>子串出现次数（T145 ⑧ 守卫用）。</summary>
        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = haystack.IndexOf(needle, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>T145 的干净局面里**唯一在场**的目标球号（实色、≠8）。</summary>
        private const int AiCleanTargetBall = 3;

        /// <summary>在场球数（T145 用来前置断言"只剩 1 颗目标球"）。</summary>
        private static int CountBallsInPlay(PhysicsTestDrive drive)
        {
            int count = 0;
            for (int ball = 0; ball < drive.Simulator.Balls.Count; ball++)
            {
                if (ball != 0 && drive.Simulator.IsBallInPlay(ball))
                {
                    count++;
                }
            }

            return count;
        }

        // ══════════════════ T145 AI 出杆入口（35-C P5-A）═════════════════════

        /// <summary>15 颗（正式对局球数）＋**注入固定 `rackSeed`** ⇒ 开局摆位确定（判据可复现的前提）。</summary>
        private static PhysicsTestDrive NewAiDrive(GameObject go, int rackSeed)
        {
            PhysicsTestDrive drive = go.AddComponent<PhysicsTestDrive>();
            drive.tableConfig = Table();
            drive.loadRackForBallBallDemo = true;
            drive.rackBallCount = PhysicsTestDrive.RackBallCount;   // 15
            drive.rackSeed = rackSeed;
            drive.Initialize();
            Assert.IsNotNull(drive.Simulator, "T145：Initialize 后应有内核。");
            Assert.AreEqual(16, drive.Simulator.Balls.Count, "T145：15 颗开局应有 16 颗球。");
            return drive;
        }

        /// <summary>两个意图**逐位**相同（含 `planned` / 目标球 / 袋 / 分 / 评估次数）。</summary>
        private static bool SameIntent(in AiShotIntent a, in AiShotIntent b)
        {
            return a.planned == b.planned && a.aimDegrees == b.aimDegrees && a.power == b.power
                   && a.targetBall == b.targetBall && a.pocketIndex == b.pocketIndex
                   && a.score == b.score && a.candidatesEvaluated == b.candidatesEvaluated;
        }

        /// <summary>本杆接触事件流签名（逐条：kind / 球号 / 位置）——用来比"两杆的事件流是否逐条相同"。</summary>
        private static string StreamSignature(PoolSimulator sim)
        {
            var sb = new StringBuilder();
            IReadOnlyList<ContactEvent> events = sim.ShotContactEvents;
            for (int i = 0; i < events.Count; i++)
            {
                sb.Append(i).Append(':').Append(events[i].kind).Append('|')
                  .Append(events[i].ballA).Append('|').Append(events[i].ballB).Append('|')
                  .Append(RVec(events[i].position)).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// **AI 出杆入口**（35-C `P5-A`）；编号 `T145`（⚠ `T144` 已被 `ClubUiTests` 的 `MatchIntro` 占用，避开重号）。
        ///
        /// · **① 种子可复现**：同 `seed` 两次 `PlanAiShot` **逐位相同**；换 `seed` ⇒ **不得全等**
        ///   （防"种子被忽略 = 假随机"）；**并且**多次规划后主内核快照**逐位不变**（规划是**只读**的）。
        /// · **② 预算封顶**：`candidatesEvaluated ≤ Parameters(档).candidateBudget` 且 `> 0`（空转即红）。
        /// · **③ 评估不污染诊断**（**可证伪**形式）：
        ///   (a) **只读性**：`EvaluateAiCandidate` 前后主内核快照**逐位不变**（评估若借用主内核，快照必变）；
        ///   (b) **分类与评估顺序无关**：一个**确定打偏**的候选，无论"单独评估"，还是"夹在一个**确实进袋**的候选之后评估"，
        ///       都**必须判为没进袋**（分 ≤ `-20`）。⚠ 不能只比"两次评分是否相等" —— 陈旧事件会让每个候选的分**饱和**到
        ///       同一个高值（实测：那样写会**漏检**，见下方注入实测记录）。
        /// · **④ 出杆后球真的动**：`TryLaunchAiShot` 返回的初速非零 + 推进后白球真的位移；
        ///   **并且**主内核事件流与"同一 aim/power 手工 `Launch()`"**逐条相同**。
        ///
        /// ⚠ **为什么不用字面口径"出杆后事件流只含这一杆"当主判据**：`Launch()` **自身**在出杆前就调
        /// `ResetDiagnostics()`（它清 `_shotContactEvents`）⇒ 无论评估有没有污染，出杆后的事件流都**只含这一杆**
        /// ⇒ 那条断言**恒真**（恒真证据 = 无证据）。故改用 ③(a) 快照只读性 —— 它**能**被"评估借用了主内核"打死。
        /// ⚠ **为什么 ④ 里那条对照不是恒真**：手工盘与 AI 盘**都**要 `Launch()`，若 AI 路径额外改动了球台状态
        /// （例如评估期把速度留在了某颗球上），两条事件流就会分叉。
        ///
        /// **注入负对照（实测）**：
        /// · 删掉 `EvaluateAiCandidate` 里的 `probe.ResetDiagnostics()` ⇒ **③(b) 红**；
        /// · 在 `PlanAiShot` 里把 `seed` 写死 ⇒ **① 的"换 seed 不得全等"红**。
        /// </summary>
        [Test]
        public void T145_AiShotEntry_PlanLaunchAndDiagnosticPurity()
        {
            var failures = new List<string>();
            var go = new GameObject("T145Drive");
            var goManual = new GameObject("T145Manual");
            try
            {
                PhysicsTestDrive drive = NewAiDrive(go, rackSeed: 4242);
                PhysicsTestDrive manual = NewAiDrive(goManual, rackSeed: 4242);
                AdvanceToRest(drive, RestCap);
                AdvanceToRest(manual, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T145：前提 —— AI 盘应在开球前静止。");
                Assert.IsTrue(manual.Simulator.TableAtRest, "T145：前提 —— 对照盘应在开球前静止。");

                // ⚠ **把两台都改成"干净进球局面"**（一颗目标球 + 白球正对一只角袋，其余球挪出台外）：
                //   满 15 颗三角堆里球彼此挡住 ⇒ "确实会进袋的候选"不存在 ⇒ ③(b) 会**真空**（实测踩过）。
                int cleanPocket = FirstCornerPocket(drive.tableConfig);
                SetUpCleanPot(drive, AiCleanTargetBall, cleanPocket, drive.Simulator.Balls.Count);
                SetUpCleanPot(manual, AiCleanTargetBall, cleanPocket, manual.Simulator.Balls.Count);
                AdvanceToRest(drive, RestCap);
                AdvanceToRest(manual, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T145：前提 —— 干净局面摆好后应静止。");
                Assert.AreEqual(1, CountBallsInPlay(drive), "T145：前提 —— 干净局面应只剩 1 颗目标球在场。");

                // ── ③ 评估不污染诊断（**必须最先做**：前提要在探针"全新"时量）──
                // ⚠ 若把它放到 ①②（会跑 `PlanAiShot`）之后，规划期的陈旧事件会把"前提①"一起弄脏
                //   （实测：注入版下 `missAlone` 直接变 true ⇒ 判据归因错误、被读成"前提不成立"而不是"被污染"）。
                AiShotContext context = drive.BuildAiContext();
                string beforeEvaluation = Snapshot(drive);

                // 参照候选 = **幽灵球基准方向**（几何上的完美线）；打偏候选 = 该方向 **+40°**。
                float perfectAim = EightBallAi.AimDegreesForPocket(context.cueBallPosition,
                    context.ballPositions[AiCleanTargetBall], context.pocketCenters[cleanPocket]);
                const float probePower = 0.5f;
                float missAim = perfectAim + 40f;

                // 前提①：打偏候选**不得**进袋（探针此刻全新 ⇒ 不受任何历史事件影响）
                bool missAlone = drive.AiCandidatePots(context, missAim, probePower, AiCleanTargetBall);
                // 前提②：参照候选**确实**进袋（否则没有"泄漏源"）
                bool potter = drive.AiCandidatePots(context, perfectAim, probePower, AiCleanTargetBall);
                // 判据：把一个**确实进袋**的候选评过之后，打偏候选**仍须**判为没进袋
                bool missAfterPotter = drive.AiCandidatePots(context, missAim, probePower, AiCleanTargetBall);

                if (missAlone)
                {
                    failures.Add("(③b) 非空转前提不成立：**故意打偏的候选**（基准 +40°、力度 0.5）在**全新探针**上就进袋了"
                                 + " ⇒ 「进袋 vs 没进袋」不可区分，本对照是**真空**的（需换局面）。");
                }
                else if (!potter)
                {
                    failures.Add("(③b) 非空转前提不成立：**幽灵球基准方向**在目标袋上（力度 0.5）**不进袋**"
                                 + " ⇒ 本对照没有「泄漏源」、是**真空**的（需换局面或力度）。");
                }
                else if (missAfterPotter)
                {
                    failures.Add("(③b) **评估被污染**：一个确定打偏的候选（基准 +40°、力度 0.5）单独评是「没进袋」，"
                                 + "但把一个**确实进袋**的候选评过之后再评，变成「进袋了」"
                                 + " ⇒ `ShotContactEvents` 未按候选归零，上一个候选的越喉事件被算进了这一杆。");
                }

                if (Snapshot(drive) != beforeEvaluation)
                {
                    failures.Add("(③a) 评估改动了主内核快照 ⇒ 评估**不是只读**的"
                                 + "（求值器借用了真实球台；真实对局会被评估过程污染）。");
                }

                // ⚠ **评分不能当"进没进"的分类器**：合成分里"白球落袋 −300"会把"被污染而虚高的进袋分 (+98)"
                //   **压回负数**（实测 `98.60 − 300 = −201.40`，看起来像"没进袋" ⇒ 判据假绿）。
                //   故 ③ 一律用 `AiCandidatePots`（**只答进没进**）。

                string beforeAnyPlanning = Snapshot(drive);

                // ── ① 种子可复现 + 规划只读 ──
                AiShotIntent first = drive.PlanAiShot(AiDifficulty.Club, 20261);
                AiShotIntent again = drive.PlanAiShot(AiDifficulty.Club, 20261);
                if (!SameIntent(first, again))
                {
                    failures.Add($"(①) 同 `seed` 两次 `PlanAiShot` 应逐位相同："
                                 + $"aim {first.aimDegrees} vs {again.aimDegrees}、power {first.power} vs {again.power}、"
                                 + $"目标 {first.targetBall} vs {again.targetBall}、评估次数 "
                                 + $"{first.candidatesEvaluated} vs {again.candidatesEvaluated}。");
                }

                AiShotIntent other = drive.PlanAiShot(AiDifficulty.Club, 999);
                if (SameIntent(first, other))
                {
                    failures.Add("(①) 换 `seed` 后意图**完全没变** ⇒ `seed` 被忽略（假随机）；"
                                 + $"两次分别是 aim {first.aimDegrees} / {other.aimDegrees}。");
                }

                if (Snapshot(drive) != beforeAnyPlanning)
                {
                    failures.Add("(①) 三次 `PlanAiShot` 后主内核快照**变了** ⇒ 规划不是只读的（污染了真实球台）。");
                }

                // ── ② 预算封顶 + 非空转 ──
                AiDifficultyParameters parameters = EightBallAi.Parameters(AiDifficulty.Club);
                if (!first.planned)
                {
                    failures.Add("(②) 15 颗开局 + 静止台面下 AI 应产出意图（`planned == true`），实测 false。");
                }

                if (first.candidatesEvaluated <= 0)
                {
                    failures.Add("(②) `candidatesEvaluated == 0` ⇒ 求值器根本没被调用（空转）。");
                }
                else if (first.candidatesEvaluated > parameters.candidateBudget)
                {
                    failures.Add($"(②) 候选评估 {first.candidatesEvaluated} 次 > 预算 {parameters.candidateBudget}。");
                }

                if (drive.LastAiSeed != 999)
                {
                    failures.Add($"(②) `LastAiSeed` 应记最近一次决策的种子（999），实测 {drive.LastAiSeed}。");
                }

                // ── ④ 出杆：初速非零 + 球真的动 + 与手工同杆事件流逐条相同 ──
                Vector2 aiVelocity = drive.TryLaunchAiShot(AiDifficulty.Club, 20261);
                if (aiVelocity.magnitude <= 0f)
                {
                    failures.Add("(④) `TryLaunchAiShot` 返回零向量 ⇒ 没有真的出杆（门控被拒或意图为空）。");
                }

                AiShotIntent launched = drive.LastAiShotIntent;
                if (!(drive.aimDegrees == launched.aimDegrees && drive.power == launched.power))
                {
                    failures.Add($"(④) 出杆前应把意图写进 `aimDegrees`/`power`（与交互层同一写入口径），"
                                 + $"实测 aim {drive.aimDegrees} / power {drive.power}，意图 {launched.aimDegrees} / {launched.power}。");
                }

                manual.aimDegrees = launched.aimDegrees;
                manual.power = launched.power;
                manual.Launch();

                // 两盘**推进同样的步数**（不各自"跑到静止"⇒ 否则步数不同会让事件流不可比）
                const int compareSteps = 1500;
                Vector2 cueBefore = drive.Simulator.Balls[0].position;
                for (int i = 0; i < compareSteps; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                    manual.Advance(PoolSimulator.FixedDeltaTime);
                }

                if ((drive.Simulator.Balls[0].position - cueBefore).magnitude <= 0.05f)
                {
                    failures.Add("(④) 白球在 1500 步内位移 ≤ 0.05 m ⇒ 球没真的动（非空转检查失败）。");
                }

                string aiStream = StreamSignature(drive.Simulator);
                if (aiStream.Length == 0)
                {
                    failures.Add("(④) AI 出杆后**一个接触事件都没有** ⇒ 本判据的事件流对照是**真空**的。");
                }

                if (aiStream != StreamSignature(manual.Simulator))
                {
                    failures.Add("(④) AI 出杆后的主内核事件流与「同一 aim/power 手工出杆」**逐条不同** ⇒ "
                                 + "AI 路径额外改动了球台状态（评估不得留在真实球台上）。");
                }

                TestContext.WriteLine($"T145: 意图 aim {launched.aimDegrees:F3}° / power {launched.power:F3} / "
                                      + $"目标 {launched.targetBall} → 袋 {launched.pocketIndex} / 评估 "
                                      + $"{launched.candidatesEvaluated} 次（预算 {parameters.candidateBudget}）；"
                                      + $"偏杆进袋 = {missAlone}（应为 False）、参照候选进袋 = {potter}；事件流 {compareSteps} 步逐条相同 ✓");

                // ── ⑤ 比赛宿主接缝（P5-B）：注册后可开局、且**球台真的建好** ──
                // ⚠ 本用例前面建了**两个**驱动器（`drive` / `manual`）⇒ **后建者**才持有 `MatchSeams.Host`
                //   （注册点只有一个，最后 `Awake` 的胜出）。所以要判"注册 + 开局"必须用**专用**宿主盘，
                //   否则会像首版那样打错对象（判据红，但红的是"我打错了"，不是产品缺陷）。
                if (!Billiards.UI.MatchSeams.HasHost)
                {
                    failures.Add("(⑤) `PhysicsTestDrive.Initialize()` 应把驱动器注册为 `MatchSeams.Host`（实测未注册）"
                                 + " —— ⚠ 注册点**不能**放 `Awake`：EditMode 下 `AddComponent` **不调 `Awake`**。");
                }

                var hostGo = new GameObject("T145Host");
                try
                {
                    PhysicsTestDrive host = NewAiDrive(hostGo, rackSeed: 91);
                    if (!ReferenceEquals(Billiards.UI.MatchSeams.Host, host))
                    {
                        failures.Add("(⑤) **最后创建的**驱动器应成为 `MatchSeams.Host`（注册点被别的对象占着）。");
                    }

                    if (!Billiards.UI.MatchSeams.TryStartMatch(AiDifficulty.Club))
                    {
                        failures.Add("(⑤) 已注册宿主时 `TryStartMatch(Club)` 应返回 true（否则赛前屏会误报「未接线」）。");
                    }
                    else
                    {
                        if (host.MatchTier != AiDifficulty.Club)
                        {
                            failures.Add($"(⑤) `StartMatch` 应记下档位 `Club`，实测 {host.MatchTier}。");
                        }

                        if (host.CurrentTurn != ShotTurn.Player)
                        {
                            failures.Add($"(⑤) 开局先手应为玩家，实测 {host.CurrentTurn}。");
                        }

                        if (host.aiGroupState != GroupState.OpenTable)
                        {
                            failures.Add($"(⑤) 新开一局应回到开放球局，实测 {host.aiGroupState}。");
                        }

                        if (host.Simulator == null || host.Simulator.Balls.Count != 16)
                        {
                            failures.Add($"(⑤) `StartMatch` 后应有 16 颗球，实测 "
                                         + $"{(host.Simulator == null ? -1 : host.Simulator.Balls.Count)}。");
                        }

                        if (host.ResolvedRackSeed == 91)
                        {
                            failures.Add("(⑤) `StartMatch` 是**新开一局** ⇒ 应把 `rackSeed` 置回真随机并重掷"
                                         + "（实测 `ResolvedRackSeed` 仍是注入值 91 ⇒ 每局摆法不会换）。");
                        }

                        AdvanceToRest(host, RestCap);
                        if (!host.Simulator.TableAtRest)
                        {
                            failures.Add("(⑤) `StartMatch` 后应在步数上限内整盘静止。");
                        }
                    }

                    // ── ⑥ 未注册宿主 ⇒ 不假装开局（返回 false + **世界状态一个字节都没变**）──
                    Billiards.UI.MatchSeams.Host = null;
                    string beforeUnregistered = Snapshot(host);
                    bool startedWithoutHost = Billiards.UI.MatchSeams.TryStartMatch(AiDifficulty.Expert);
                    if (startedWithoutHost)
                    {
                        failures.Add("(⑥) 未注册宿主时 `TryStartMatch` 必须返回 false（不许假装已开局）。");
                    }

                    if (Snapshot(host) != beforeUnregistered)
                    {
                        failures.Add("(⑥) 未注册宿主时 `TryStartMatch` 竟然改动了球台状态 ⇒ 界面会「假装已开局」。");
                    }

                    if (host.MatchTier != AiDifficulty.Club)
                    {
                        failures.Add($"(⑥) 未注册时不得改档位，实测 {host.MatchTier}（应仍为 Club）。");
                    }

                    // 注销语义（**只注销自己**）：注册点被**别人**占着时，注销**不得**把它一起抹掉。
                    // ⚠ `OnDestroy` 在 EditMode 里**不会被 Unity 调到**（与 `Awake` 同因）⇒ 判据直接调
                    //   `UnregisterMatchHost()`（`OnDestroy` 的唯一实现体），否则这条**无法判定**。
                    Billiards.UI.MatchSeams.Host = manual;
                    host.UnregisterMatchHost();
                    if (!ReferenceEquals(Billiards.UI.MatchSeams.Host, manual))
                    {
                        failures.Add("(⑥) `UnregisterMatchHost` **只应注销自己** —— 它把别人的注册也抹掉了。");
                    }

                    Billiards.UI.MatchSeams.Host = host;
                    host.UnregisterMatchHost();
                    if (Billiards.UI.MatchSeams.HasHost)
                    {
                        failures.Add("(⑥) `UnregisterMatchHost` 应能清空**自己的**注册（否则接缝会指向已销毁对象）。");
                    }

                    Billiards.UI.MatchSeams.Host = host;   // 还原注册（交 finally 销毁）
                }
                finally
                {
                    Object.DestroyImmediate(hostGo);
                }

                // ── ⑦ 接线（**跑真路径**）：AI 回合期间玩家输入被拒 ⇒ 必须登记可见反馈 ──
                Billiards.UI.MatchTurnNotice.Reset();
                drive.SetTurn(ShotTurn.Ai);
                bool consumedDuringAiTurn = drive.TryConsumePlayerInput();
                if (consumedDuringAiTurn)
                {
                    failures.Add("(⑦) AI 回合期间玩家输入应被**拒绝**（`TryConsumePlayerInput` 返回 false）。");
                }

                if (Billiards.UI.MatchTurnNotice.BlockedCount != 1)
                {
                    failures.Add($"(⑦) AI 回合被拒应登记**一次**可见反馈，实测 `BlockedCount` = "
                                 + $"{Billiards.UI.MatchTurnNotice.BlockedCount}（0 = 静默无反馈）。");
                }

                if (Billiards.UI.MatchTurnNotice.LastBlockedReason != Billiards.UI.MatchTurnNotice.ReasonOpponentTurn)
                {
                    failures.Add($"(⑦) 被拒原因应为字面量 `{Billiards.UI.MatchTurnNotice.ReasonOpponentTurn}`，"
                                 + $"实测 `{Billiards.UI.MatchTurnNotice.LastBlockedReason}`。");
                }

                if (Billiards.UI.MatchTurnNotice.BlockedCount == 1)
                {
                    drive.SetTurn(ShotTurn.Player);
                    if (!drive.TryConsumePlayerInput())
                    {
                        failures.Add("(⑦) 轮到玩家时 `TryConsumePlayerInput` 应放行（返回 true）。");
                    }

                    if (Billiards.UI.MatchTurnNotice.BlockedCount != 1)
                    {
                        failures.Add("(⑦) 轮到玩家时**不得**再登记被拒（计数应保持 1）。");
                    }
                }

                // ── ⑧ 调用点守卫（机械化：判「调用括号 + 实参前缀」）──
                // ⚠ 定义处住在 `PhysicsTestDrive.cs`；**这里只数调用点**，且限定在交互层的那个文件里。
                string powerSource = File.ReadAllText(Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? ".", PointerPowerSourcePath));
                if (CountOccurrences(StripCommentLines(powerSource), "drive.TryConsumePlayerInput()") < 1)
                {
                    failures.Add("(⑧) `PointerPowerController` 里找不到 `drive.TryConsumePlayerInput()` 调用点"
                                 + " ⇒ 「AI 回合拒绝玩家输入」的接线被摘掉了（值/设施还在，但没人调用它）。");
                }

                // ── ⑨ 端到端：真出一杆 ⇒ 规则管线自动结算到出口 ──
                // ⚠ **刻意不显式调 `SettleShotIfAtRest()`** —— 结算必须由 `Advance` 那条**唯一接线**触发；
                //   删掉那处调用 ⇒ 本判据必红（"设施在但没人接线"的实证）。
                var settleGo = new GameObject("T145Settle");
                try
                {
                    PhysicsTestDrive sd = NewAiDrive(settleGo, rackSeed: 5);
                    int potPocket = FirstCornerPocket(sd.tableConfig);
                    SetUpCleanPot(sd, 8, potPocket, sd.Simulator.Balls.Count);   // 8 号球，正对角袋
                    AdvanceToRest(sd, RestCap);

                    sd.ruleState = new RuleState
                    {
                        groupState = GroupState.Solid,   // 本方（玩家）已定实色
                        solidRemaining = 0,              // 已清完 ⇒ 该打黑 8
                        stripeRemaining = 7,
                        isBreak = false,
                    };
                    // ⚠ D3（指定袋）已按用户要求取消（P8-C）⇒ 这里**不再需要**叫袋，黑 8 进任意袋都算。
                    sd.SetTurn(ShotTurn.Player);

                    AiShotContext sctx = sd.BuildAiContext();
                    sd.aimDegrees = EightBallAi.AimDegreesForPocket(sctx.cueBallPosition, sctx.ballPositions[8],
                        sctx.pocketCenters[potPocket]);
                    sd.power = 0.5f;

                    if (sd.Launch().magnitude <= 0f)
                    {
                        failures.Add("(⑨) 前提不成立：端到端那一杆没打出去（门控被拒）。");
                    }
                    else
                    {
                        AdvanceToRest(sd, RestCap);   // ← 结算应由 `Advance` 的**唯一接线**自动触发

                        if (!sd.MatchOver)
                        {
                            failures.Add("(⑨) **端到端没结算**：合法进黑 8 之后 `MatchOver` 仍是 false"
                                         + " ⇒ 规则管线没接到结算口（`SettleShotIfAtRest` 无调用者）／静止门没过。");
                        }
                        else
                        {
                            if (sd.Winner != ShotTurn.Player)
                            {
                                failures.Add($"(⑨) 合法进黑 8 应由**玩家**胜，实测 {sd.Winner}。");
                            }

                            if (sd.EndReason != ShotOutcomeKind.LegalEightBallPotted)
                            {
                                failures.Add($"(⑨) 结算原因应为 `LegalEightBallPotted`，实测 {sd.EndReason}。");
                            }

                            if (!sd.TryConsumeSettlement())
                            {
                                failures.Add("(⑨) 结算应可被**消费一次**（`TryConsumeSettlement` 第一次应返回 true）。");
                            }
                            else if (sd.TryConsumeSettlement())
                            {
                                failures.Add("(⑨) **结算只该弹一次** —— 第二次调用仍返回 true。");
                            }

                            // ⑤ 一局定胜负：**什么都不做** ⇒ 不会冒出第 2 局
                            for (int i = 0; i < 60; i++)
                            {
                                sd.Advance(PoolSimulator.FixedDeltaTime);
                            }

                            if (!sd.MatchOver)
                            {
                                failures.Add("(⑤) 结算后**不得**自动进入第 2 局（`MatchOver` 应保持 true）。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(settleGo);
                }

                // ── 指定袋（D3）判据 ②③④ 与 AI 叫袋 ①②③ **已随 D3 一起删除（P8-C）** ──
                //   用户撤销理由：「取消最后呼叫球袋黑8，黑8最后打进哪个袋都行」⇒ 不再有
                //   `CalledPocket` / `RefusedCallPocketCount` / `IsEightBallShot` / `WrongPocket` 这些出口。

                // ── P7 规则（Runtime 侧）：`RuleVerdict` 的七个字段**一个都不许丢** ──
                // ① 定组（含 ⑦ 剩余数由球台重算）② 首碰违规 ⇒ 犯规 + 换手（含 ⑤ 对手拿全场自由球）
                // ③ 合法但没进本方球 ⇒ 换手 ④ 合法开球没进球 ⇒ 换手
                // ⑤(b) 自由球形态 / 头区落点 / 前向门 ⑥ 开球进黑 8 ⇒ 复位回 footSpot
                var p7GroupGo = new GameObject("T145P7Group");
                try
                {
                    PhysicsTestDrive g1 = NewAiDrive(p7GroupGo, rackSeed: 51);
                    int gp = FirstCornerPocket(g1.tableConfig);
                    SetUpCleanPot(g1, 3, gp, g1.Simulator.Balls.Count);   // 只剩**实色 3** 在场
                    AdvanceToRest(g1, RestCap);

                    if (g1.ruleState.groupState != GroupState.OpenTable)
                    {
                        failures.Add($"(P7①) 前提：新局应为开放球局，实测 {g1.ruleState.groupState}。");
                    }

                    g1.SetTurn(ShotTurn.Player);
                    AiShotContext gctx = g1.BuildAiContext();
                    g1.aimDegrees = EightBallAi.AimDegreesForPocket(gctx.cueBallPosition, gctx.ballPositions[3],
                        gctx.pocketCenters[gp]);
                    g1.power = 0.5f;

                    if (g1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P7①) 前提不成立：定组那一杆没打出去。");
                    }
                    else
                    {
                        AdvanceToRest(g1, RestCap);

                        if (g1.Simulator.IsBallInPlay(3))
                        {
                            failures.Add("(P7①) 前提不成立：那一杆没把 3 号打进袋 ⇒ 定组无从判定。");
                        }
                        else if (!g1.LastVerdict.reassignGroup)
                        {
                            failures.Add($"(P7①) 开放球局的第一颗合法进球应触发**定组**（`reassignGroup`），"
                                         + $"实测 false（outcome={g1.LastVerdict.outcome}）。");
                        }
                        else
                        {
                            if (g1.ruleState.groupState != GroupState.Solid)
                            {
                                failures.Add($"(P7①) 第一颗合法进球是**实色** ⇒ `ruleState.groupState` 应变 `Solid`，"
                                             + $"实测 {g1.ruleState.groupState}。");
                            }

                            if (g1.aiGroupState != GroupState.Stripe)
                            {
                                failures.Add($"(P7①) 玩家定实色 ⇒ **对手(AI)的组别**应为 `Stripe`（互补、不重复），"
                                             + $"实测 {g1.aiGroupState}。");
                            }

                            if (g1.IsAiTurn)
                            {
                                failures.Add("(P7①) 进了本方球应**续杆**（仍是玩家回合）。");
                            }

                            // ⑦ 本方球清完 ⇒ **剩余数必须由球台重算为 0**（P8 起 `IsEightBallShot` 已随 D3 删除，
                            //    这里直接断"剩余数与球台同步"这个根因 —— 它正是"黑 8 能不能合法打"的输入）。
                            if (g1.ruleState.solidRemaining != 0)
                            {
                                failures.Add($"(P7⑦) 本方球已清完 ⇒ `solidRemaining` 应被重算为 0，"
                                             + $"实测 {g1.ruleState.solidRemaining}（组别/剩余数与球台脱节）。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7GroupGo);
                }

                // ── ② 首碰违规 ⇒ 犯规 + 换手；⑤ 犯规 ⇒ 对手拿**全场**自由球 ──
                var p7FoulGo = new GameObject("T145P7Foul");
                try
                {
                    PhysicsTestDrive f1 = NewAiDrive(p7FoulGo, rackSeed: 52);
                    for (int ball = 0; ball < f1.Simulator.Balls.Count; ball++)
                    {
                        if (ball == 0)
                        {
                            f1.Simulator.PlaceBall(ball, new Vector2(-0.30f, 0f), Vector2.zero);
                            continue;
                        }

                        // 场上只留**花色 10**（正对 +x）⇒ 实色方先碰它 = 犯规
                        Vector2 pos = ball == 10 ? new Vector2(0.30f, 0f) : new Vector2(0f, 50f + ball * 0.5f);
                        f1.Simulator.PlaceBall(ball, pos, Vector2.zero);
                    }

                    AdvanceToRest(f1, RestCap);
                    f1.ruleState = new RuleState
                    {
                        groupState = GroupState.Solid,
                        solidRemaining = 7,
                        stripeRemaining = 1,   // 场上只有一颗花色
                        isBreak = false,
                    };
                    f1.SetTurn(ShotTurn.Player);
                    f1.aimDegrees = 0f;
                    f1.power = 0.5f;

                    if (f1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P7②) 前提不成立：那一杆没打出去。");
                    }
                    else
                    {
                        AdvanceToRest(f1, RestCap);

                        if (!f1.LastVerdict.foul)
                        {
                            failures.Add("(P7②) **先碰对方组的球**应判犯规（`foul == true`），实测 false"
                                         + "（组别恒 `OpenTable` 时首碰合法性整段被跳过）。");
                        }

                        if (!f1.IsAiTurn)
                        {
                            failures.Add("(P7②) 犯规应**换手**给对手，实测仍是玩家回合。");
                        }

                        if (f1.BallInHand != BallInHandKind.FullTable)
                        {
                            failures.Add($"(P7⑤) 犯规应给对手**全场自由球**（`BallInHand` 出口可读），"
                                         + $"实测 {f1.BallInHand}。");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7FoulGo);
                }

                // ── ③ 合法但**没进本方球** ⇒ 换手（正规 8 球；旧实现"只要不犯规就续杆"）──
                var p7NoPotGo = new GameObject("T145P7NoPot");
                try
                {
                    PhysicsTestDrive n1 = NewAiDrive(p7NoPotGo, rackSeed: 53);
                    for (int ball = 0; ball < n1.Simulator.Balls.Count; ball++)
                    {
                        if (ball == 0)
                        {
                            n1.Simulator.PlaceBall(ball, new Vector2(-0.30f, 0f), Vector2.zero);
                            continue;
                        }

                        // 场上只留**实色 3**（正对 +x，且**不正对任何袋**）⇒ 必碰库、必不进袋
                        Vector2 pos = ball == 3 ? Vector2.zero : new Vector2(0f, 50f + ball * 0.5f);
                        n1.Simulator.PlaceBall(ball, pos, Vector2.zero);
                    }

                    AdvanceToRest(n1, RestCap);
                    n1.ruleState = new RuleState
                    {
                        groupState = GroupState.Solid,
                        solidRemaining = 1,
                        stripeRemaining = 0,
                        isBreak = false,
                    };
                    n1.SetTurn(ShotTurn.Player);
                    n1.aimDegrees = 0f;
                    n1.power = 0.5f;

                    if (n1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P7③) 前提不成立：那一杆没打出去。");
                    }
                    else
                    {
                        AdvanceToRest(n1, RestCap);

                        if (!n1.Simulator.IsBallInPlay(3))
                        {
                            failures.Add("(P7③) 前提不成立：3 号竟然进袋了（本项要的是【合法但没进球】）。");
                        }
                        else if (n1.LastVerdict.foul)
                        {
                            failures.Add("(P7③) 前提不成立：这一杆不该犯规（先碰本方球、之后有球碰库）。");
                        }
                        else
                        {
                            if (n1.BallInHand != BallInHandKind.None)
                            {
                                failures.Add($"(P7③) 合法杆不该给自由球，实测 {n1.BallInHand}。");
                            }

                            if (!n1.IsAiTurn)
                            {
                                failures.Add("(P7③) **合法但没进本方球 ⇒ 必须换手**，实测仍是玩家回合。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7NoPotGo);
                }

                // ── ④ 合法开球但**没进任何目标球** ⇒ 换手（旧实现默认续杆）──
                var p7BreakGo = new GameObject("T145P7Break");
                try
                {
                    PhysicsTestDrive b1 = NewAiDrive(p7BreakGo, rackSeed: 54);
                    PlaceBreakCluster(b1);
                    AdvanceToRest(b1, RestCap);
                    b1.ruleState = new RuleState
                    {
                        groupState = GroupState.OpenTable,
                        solidRemaining = 7,
                        stripeRemaining = 0,
                        isBreak = true,
                    };
                    b1.SetTurn(ShotTurn.Player);
                    b1.aimDegrees = 0f;
                    b1.power = 1f;

                    if (b1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P7④) 前提不成立：开球那一杆没打出去。");
                    }
                    else
                    {
                        AdvanceToRest(b1, RestCap);

                        if (b1.LastVerdict.reRack)
                        {
                            failures.Add("(P7④) 前提不成立：这一杆被判成**无效开球**（碰库目标球不足 4 颗）"
                                         + " ⇒ 换手那一条无从判定（调布局或加力）。");
                        }
                        else
                        {
                            int inPlay = 0;
                            for (int ball = 1; ball <= 7; ball++)
                            {
                                if (b1.Simulator.IsBallInPlay(ball))
                                {
                                    inPlay++;
                                }
                            }

                            if (inPlay < 7)
                            {
                                failures.Add($"(P7④) 前提不成立：开球竟然进了球（场上只剩 {inPlay}/7 颗）"
                                             + " ⇒ 本项要的是【合法开球但没进球】。");
                            }
                            else if (!b1.IsAiTurn)
                            {
                                failures.Add("(P7④) **合法开球但没进任何目标球 ⇒ 必须换手**，实测仍是玩家回合。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7BreakGo);
                }

                // ── ⑤(b) 自由球：形态出口 / 头区落点 / 前向门 ──
                var p7BiHGo = new GameObject("T145P7BallInHand");
                try
                {
                    PhysicsTestDrive h1 = NewAiDrive(p7BiHGo, rackSeed: 55);
                    for (int ball = 0; ball < h1.Simulator.Balls.Count; ball++)
                    {
                        Vector2 pos = ball == 0 ? Vector2.zero : new Vector2(0f, 50f + ball * 0.5f);
                        h1.Simulator.PlaceBall(ball, pos, Vector2.zero);
                    }

                    AdvanceToRest(h1, RestCap);
                    h1.SetTurn(ShotTurn.Player);

                    // 直接施加"头区自由球 + 必须往前打"（本项测**施加侧**；规则层给不给由 ②/④ 守）
                    h1.ApplyVerdict(new RuleVerdict
                    {
                        nextTurn = ShotTurn.Player,
                        ballInHand = BallInHandKind.Kitchen,
                        mustShootForward = true,
                        winner = ShotTurn.Player,
                    });

                    if (h1.BallInHand != BallInHandKind.Kitchen)
                    {
                        failures.Add($"(P7⑤) `ballInHand` 未施加：应可读到 `Kitchen`，实测 {h1.BallInHand}。");
                    }

                    if (!h1.PendingCueBallPlacement)
                    {
                        failures.Add("(P7⑤) 自由球应打开【待放球】态（`PendingCueBallPlacement`），实测 false。");
                    }

                    float lineX = PoolConstants.KitchenLineX;
                    if (!h1.CanPlaceCueBall(new Vector2(lineX + 0.15f, 0.20f)))
                    {
                        failures.Add("(P7⑤) 头区自由球：**开球线之后**的落点应合法（复用 `IsPlacementLegalInKitchen`）。");
                    }

                    if (h1.CanPlaceCueBall(new Vector2(lineX - 0.15f, 0.20f)))
                    {
                        failures.Add("(P7⑤) 头区自由球：**开球线之前**的落点应非法。");
                    }

                    // ⚠ **必须先放好球**：P10 裁定 (a) 之后"待放期一律拒杆"这道门排在**更前面**
                    //   ⇒ 不放球的话下面测到的是"待放拒杆"，不是"前向门"（会把两条门混成一条）。
                    if (!h1.TryPlaceCueBall(new Vector2(lineX + 0.15f, 0.20f)))
                    {
                        failures.Add("(P7⑤) 前提不成立：头区合法落点应能放球。");
                    }

                    // ⚠ `PlaceBall` 会把"静止计数"清零 ⇒ 必须再推到静止，否则下一杆先被**静止门**拒
                    //   （那样测到的是第三条门，`RefusedForwardCount` 当然不涨）。
                    AdvanceToRest(h1, RestCap);

                    int fwdBefore = h1.RefusedForwardCount;
                    h1.aimDegrees = 0f;   // +x = **背向**头线（判定口径：前方 = −x）
                    h1.power = 0.5f;
                    if (h1.Launch().magnitude > 0f)
                    {
                        failures.Add("(P7⑤) `mustShootForward` 未施加：背向头线的出杆应被拒。");
                    }
                    else if (h1.RefusedForwardCount != fwdBefore + 1)
                    {
                        failures.Add($"(P7⑤) 前向门拒绝必须留痕（`RefusedForwardCount` 应 +1），"
                                     + $"实测 {fwdBefore} → {h1.RefusedForwardCount}。");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7BiHGo);
                }

                // ── ⑥ 开球进黑 8 ⇒ `respotEightBall` 生效（黑 8 回到 `footSpot`）──
                var p7RespotGo = new GameObject("T145P7Respot");
                try
                {
                    PhysicsTestDrive r1 = NewAiDrive(p7RespotGo, rackSeed: 56);
                    // 把黑 8 挪到台外（= 既有"已入袋"口径），再施加复位
                    r1.Simulator.PlaceBall(EightBallRules.EightBall, new Vector2(0f, 50f + 8 * 0.5f), Vector2.zero);
                    AdvanceToRest(r1, RestCap);

                    if (r1.Simulator.IsBallInPlay(EightBallRules.EightBall))
                    {
                        failures.Add("(P7⑥) 前提不成立：黑 8 应已不在场。");
                    }
                    else
                    {
                        r1.ApplyVerdict(new RuleVerdict
                        {
                            nextTurn = ShotTurn.Player,
                            winner = ShotTurn.Player,
                            respotEightBall = true,
                            outcome = ShotOutcomeKind.EightBallRespot,
                        });

                        if (!r1.Simulator.IsBallInPlay(EightBallRules.EightBall))
                        {
                            failures.Add("(P7⑥) `respotEightBall` 未施加：黑 8 应被**放回场上**，实测仍不在场。");
                        }
                        else
                        {
                            float off = (r1.Simulator.Balls[EightBallRules.EightBall].position
                                         - r1.tableConfig.footSpot).magnitude;
                            if (off > 1e-4f)
                            {
                                failures.Add($"(P7⑥) 黑 8 应复位到 `footSpot`，实测偏离 {off:F5} m。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7RespotGo);
                }

                // ── P8-D 球杆：**出杆后（球在滚的整段时间）整根隐藏**（用户：「AI 击玩球，画面上的球杆也要暂时消失」）──
                // ⚠ P7 的"冻结在击球位"方案已按用户要求**换掉**：他要的是"暂时消失"，不是"停在那儿"。
                if (CueStickController.ShouldShowStick(true, true, false, false, shotInFlight: true))
                {
                    failures.Add("(P8⑧) **出杆期间球杆必须不可见**（`shotInFlight == true` ⇒ false），实测仍返回可见。");
                }

                if (!CueStickController.ShouldShowStick(true, true, false, false, shotInFlight: false))
                {
                    failures.Add("(P8⑧) **回到瞄准时球杆必须可见**（`shotInFlight == false` ⇒ true），实测返回不可见。");
                }

                // **接线守卫**（纯函数绿 ≠ 真的用上了）：源码里必须把 `drive.ShotInFlight` 喂进可见性判定，
                // 并且 P7 的 `StickAnchorPosition`（冻结方案）**必须已删除**（不许留死函数）。
                string stickSource = File.ReadAllText(Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? ".", CueStickSourcePath));
                string stickNoComment = StripCommentLines(stickSource);
                if (CountOccurrences(stickNoComment, "drive.ShotInFlight") < 1)
                {
                    failures.Add("(P8⑧) `CueStickController` 没有把 `drive.ShotInFlight` 喂给可见性判定"
                                 + " ⇒ 出杆后球杆不会消失。");
                }

                if (CountOccurrences(stickNoComment, "StickAnchorPosition") != 0)
                {
                    failures.Add("(P8⑧) `StickAnchorPosition`（P7 的「冻结」方案）**应已删除**，"
                                 + "实测源码里仍有引用 ⇒ 两套方案并存。");
                }

                // ── P7⑨ AI 出杆**分阶段**（瞄准 → 拉杆 → 击出），不是一次性 aim+power+Launch ──
                var p7PhaseGo = new GameObject("T145P7AiPhase");
                try
                {
                    PhysicsTestDrive a2 = NewAiDrive(p7PhaseGo, rackSeed: 57);
                    int ap = FirstCornerPocket(a2.tableConfig);
                    SetUpCleanPot(a2, AiCleanTargetBall, ap, a2.Simulator.Balls.Count);
                    AdvanceToRest(a2, RestCap);
                    a2.ruleState = new RuleState
                    {
                        groupState = GroupState.Solid,
                        solidRemaining = 3,
                        stripeRemaining = 7,
                        isBreak = false,
                    };
                    a2.SetTurn(ShotTurn.Ai);
                    a2.aiThinkSeconds = 0.05f;
                    a2.aiAimSeconds = 0.10f;
                    a2.aiPullSeconds = 0.10f;

                    bool sawAiming = false;
                    bool sawPulling = false;
                    float powerMidPull = -1f;
                    int launchesBeforePhase = a2.LaunchCount;
                    for (int i = 0; i < 600 && a2.LaunchCount == launchesBeforePhase; i++)
                    {
                        a2.Advance(PoolSimulator.FixedDeltaTime);
                        if (a2.AiPhase == PhysicsTestDrive.AiShotPhase.Aiming)
                        {
                            sawAiming = true;
                        }

                        if (a2.AiPhase == PhysicsTestDrive.AiShotPhase.Pulling)
                        {
                            sawPulling = true;
                            if (powerMidPull < 0f && a2.power > 0.01f)
                            {
                                powerMidPull = a2.power;
                            }
                        }
                    }

                    if (a2.LaunchCount == launchesBeforePhase)
                    {
                        failures.Add("(P7⑨) 前提不成立：AI 在 600 步内没出手 ⇒ 分阶段判据无从判定。");
                    }
                    else
                    {
                        if (!sawAiming)
                        {
                            failures.Add("(P7⑨) AI 出杆应经过**瞄准段**（`AiPhase == Aiming` 可观测），实测没看到"
                                         + " ⇒ 仍是「一次性出杆」，玩家看不到对手在瞄。");
                        }

                        if (!sawPulling)
                        {
                            failures.Add("(P7⑨) AI 出杆应经过**拉杆段**（`AiPhase == Pulling` 可观测），实测没看到"
                                         + " ⇒ 没有「拉杆」这个动作。");
                        }

                        float planned = a2.LastAiShotIntent.power;
                        if (powerMidPull <= 0f || powerMidPull >= planned)
                        {
                            failures.Add($"(P7⑨) 拉杆段 `power` 应从 0 **线性涨**上去"
                                         + $"（中途应 0 < power < 规划值 {planned:F3}），实测中途 {powerMidPull:F3}。");
                        }

                        if (a2.AiPhase != PhysicsTestDrive.AiShotPhase.None)
                        {
                            failures.Add($"(P7⑨) 击出后 `AiPhase` 应回到 `None`，实测 {a2.AiPhase}。");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7PhaseGo);
                }

                // ── P7⑩ `isBreak` **只在开球那一杆为 true**（第二根因：旧代码从不清 ⇒ 之后每一杆都走 `JudgeBreak`）──
                var p7BreakFlagGo = new GameObject("T145P7BreakFlag");
                try
                {
                    PhysicsTestDrive k1 = NewAiDrive(p7BreakFlagGo, rackSeed: 58);
                    PlaceBreakCluster(k1);
                    AdvanceToRest(k1, RestCap);
                    k1.ruleState = new RuleState
                    {
                        groupState = GroupState.OpenTable,
                        solidRemaining = 7,
                        stripeRemaining = 0,
                        isBreak = true,
                    };
                    k1.SetTurn(ShotTurn.Player);
                    k1.aimDegrees = 0f;
                    k1.power = 1f;

                    if (k1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P7⑩) 前提不成立：开球那一杆没打出去。");
                    }
                    else
                    {
                        AdvanceToRest(k1, RestCap);

                        if (k1.LastVerdict.reRack)
                        {
                            failures.Add("(P7⑩) 前提不成立：这一杆被判成**无效开球** ⇒ 本项无从判定。");
                        }
                        else
                        {
                            if (k1.ruleState.isBreak)
                            {
                                failures.Add("(P7⑩) **有效开球之后 `ruleState.isBreak` 必须回到 false**，实测仍为 true"
                                             + " ⇒ 之后每一杆都会被 `JudgeBreak` 判（常规分支永远跑不到）。");
                            }

                            // 第二杆：**故意做成"若按开球判就会重摆"的形状**（0 球进袋 + 碰库目标球不足 4 颗）
                            // ⇒ 只有**真的走了 `JudgeRegular`** 才不会 `reRack` ⇒ 这条断的是"走的是哪个分支"本身。
                            for (int ball = 0; ball < k1.Simulator.Balls.Count; ball++)
                            {
                                if (ball == 0)
                                {
                                    k1.Simulator.PlaceBall(ball, new Vector2(-0.30f, 0f), Vector2.zero);
                                    continue;
                                }

                                Vector2 pos = ball == AiCleanTargetBall
                                    ? Vector2.zero
                                    : new Vector2(0f, 50f + ball * 0.5f);
                                k1.Simulator.PlaceBall(ball, pos, Vector2.zero);
                            }

                            AdvanceToRest(k1, RestCap);
                            k1.SetTurn(ShotTurn.Player);
                            k1.aimDegrees = 0f;
                            k1.power = 0.3f;

                            if (k1.Launch().magnitude <= 0f)
                            {
                                failures.Add("(P7⑩) 前提不成立：第二杆没打出去。");
                            }
                            else
                            {
                                AdvanceToRest(k1, RestCap);
                                if (k1.LastVerdict.reRack)
                                {
                                    failures.Add("(P7⑩) 第二杆被判成**重新摆球** ⇒ 它走的仍是 `JudgeBreak` 分支"
                                                 + "（`isBreak` 没清掉 ⇒ 常规分支一次都跑不到）。");
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7BreakFlagGo);
                }

                // ── P7⑪ AI 拿自由球 ⇒ **自己找合法位置放白球**再出杆（否则自由球白拿）──
                var p7AiPlaceGo = new GameObject("T145P7AiPlace");
                try
                {
                    PhysicsTestDrive v1 = NewAiDrive(p7AiPlaceGo, rackSeed: 59);
                    int vp = FirstCornerPocket(v1.tableConfig);
                    SetUpCleanPot(v1, AiCleanTargetBall, vp, v1.Simulator.Balls.Count);

                    // 白球先离台（= 犯规后"待放球"的既有形态）；此刻仍是玩家回合 ⇒ 驱动器不会动
                    v1.Simulator.PlaceBall(0, new Vector2(0f, 50f), Vector2.zero);
                    AdvanceToRest(v1, RestCap);

                    v1.ruleState = new RuleState
                    {
                        groupState = GroupState.Solid,
                        solidRemaining = 3,
                        stripeRemaining = 7,
                        isBreak = false,
                    };
                    v1.SetTurn(ShotTurn.Ai);
                    v1.ApplyVerdict(new RuleVerdict
                    {
                        nextTurn = ShotTurn.Ai,
                        winner = ShotTurn.Ai,
                        ballInHand = BallInHandKind.FullTable,
                    });

                    if (!v1.PendingCueBallPlacement)
                    {
                        failures.Add("(P7⑪) 前提不成立：施加自由球后应处于**待放球**态。");
                    }
                    else
                    {
                        v1.aiThinkSeconds = 0.05f;
                        v1.aiAimSeconds = 0.05f;
                        v1.aiPullSeconds = 0.05f;
                        int launchesBeforePlace = v1.LaunchCount;
                        int placeBefore = v1.AiAutoPlaceCount;
                        for (int i = 0; i < 600 && v1.LaunchCount == launchesBeforePlace; i++)
                        {
                            v1.Advance(PoolSimulator.FixedDeltaTime);
                        }

                        if (v1.AiAutoPlaceCount != placeBefore + 1)
                        {
                            failures.Add($"(P7⑪) AI 拿自由球时必须**自己把白球放到合法位置**"
                                         + $"（`AiAutoPlaceCount` 应 +1），实测 {placeBefore} → {v1.AiAutoPlaceCount}"
                                         + " ⇒ 只能吃 `Launch()` 的「白球回开球位」兜底 = **自由球白拿**。");
                        }

                        if (!v1.Simulator.IsBallInPlay(0))
                        {
                            failures.Add("(P7⑪) AI 出杆前白球必须在场上（放球成功）。");
                        }

                        if (v1.LaunchCount == launchesBeforePlace)
                        {
                            failures.Add("(P7⑪) AI 拿了自由球却**没能出杆**。");
                        }

                        // **P10 补**：这道"待放期一律拒杆"的门排在 `Launch()` 最前面 ⇒
                        // AI 的**自动放球必须发生在出杆之前**，否则 AI 会被自己拒掉、每 tick 空转
                        //（"AI 不出杆"的静默症状我们吃过一次：`TickAiTurn` 零调用者）。
                        // ⚠ 判的是**不变量**（放球先于击出），不是"同一 tick"——AI 是分阶段的（瞄准→拉杆→击出），
                        //   放球在 `AiShotPhase.None` 就完成了，击出在后面的 tick ⇒ 只要求"击出时已不在待放态"。
                        // **防真空（P10 补）**：先证明"AI **真的击出了**" —— 否则 `RefusedLaunchWhileAwaitingCount == 0`
                        // 在"AI 压根没出杆"时也成立 ⇒ 断言恒真、**注入版也绿**（"真空绿"家族，第 9 例同族）。
                        if (v1.AiLaunchCount < 1 || v1.LaunchCount != launchesBeforePlace + 1)
                        {
                            failures.Add($"(P10-AI) 前提不成立：AI 这一杆**没有真的击出**"
                                         + $"（AiLaunchCount={v1.AiLaunchCount}、LaunchCount {launchesBeforePlace}→{v1.LaunchCount}）"
                                         + " ⇒ 「放球先于击出」无从判定（**真空**）。");
                        }
                        else if (v1.RefusedLaunchWhileAwaitingCount != 0)
                        {
                            failures.Add($"(P10-AI) AI 出杆**不许被「待放」门拒**（自动放球必须先于击出），"
                                         + $"实测 RefusedLaunchWhileAwaitingCount={v1.RefusedLaunchWhileAwaitingCount}。");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p7AiPlaceGo);
                }

                // ── P9-B 给 UI 的**玩家视角**分组/剩余球出口：定组前空 / 定组后并集 / **进一颗少一颗** ──
                var p9Go = new GameObject("T145P9Roster");
                try
                {
                    PhysicsTestDrive r1 = NewAiDrive(p9Go, rackSeed: 61);
                    int p9Pocket = FirstCornerPocket(r1.tableConfig);
                    SetUpCleanPot(r1, 3, p9Pocket, r1.Simulator.Balls.Count);
                    // 再放一颗**安全位置**的实色 5（不在任何袋口线上）⇒ 定组后应出现在"玩家剩余球号"里
                    r1.Simulator.PlaceBall(5, new Vector2(0.60f, 0.45f), Vector2.zero);
                    AdvanceToRest(r1, RestCap);
                    r1.SetTurn(ShotTurn.Player);

                    if (r1.PlayerGroup != GroupState.OpenTable
                        || r1.PlayerRemainingNumbers.Count != 0
                        || r1.OpponentRemainingNumbers.Count != 0)
                    {
                        failures.Add($"(P9-B①) 开放球局时：`PlayerGroup` 应 `OpenTable`、两个剩余球号列表应**都为空**，"
                                     + $"实测 {r1.PlayerGroup} / {r1.PlayerRemainingNumbers.Count} / {r1.OpponentRemainingNumbers.Count}。");
                    }

                    AiShotContext p9ctx = r1.BuildAiContext();
                    r1.aimDegrees = EightBallAi.AimDegreesForPocket(p9ctx.cueBallPosition, p9ctx.ballPositions[3],
                        p9ctx.pocketCenters[p9Pocket]);
                    r1.power = 0.5f;
                    if (r1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P9-B) 前提不成立：定组那一杆没打出去。");
                    }
                    else
                    {
                        AdvanceToRest(r1, RestCap);

                        if (r1.Simulator.IsBallInPlay(3))
                        {
                            failures.Add("(P9-B) 前提不成立：3 号没进袋（定组无从判定）。");
                        }
                        else
                        {
                            // ② 定组后：玩家=Solid、对手=Stripe，玩家列表应含 5（不含黑 8、不含已进的 3）
                            if (r1.PlayerGroup != GroupState.Solid || r1.OpponentGroup != GroupState.Stripe)
                            {
                                failures.Add($"(P9-B②) 定组后：玩家应 `Solid`、对手应 `Stripe`，"
                                             + $"实测 {r1.PlayerGroup} / {r1.OpponentGroup}。");
                            }

                            bool hasFive = false;
                            foreach (int ball in r1.PlayerRemainingNumbers)
                            {
                                if (ball == 5)
                                {
                                    hasFive = true;
                                }
                            }

                            if (!hasFive || r1.PlayerRemainingNumbers.Count != 1)
                            {
                                failures.Add($"(P9-B②) 定组后玩家剩余球号应恰为 [5]，实测 "
                                             + $"[{string.Join(",", r1.PlayerRemainingNumbers)}]。");
                            }

                            if (r1.PlayerRemainingNumbers.Contains(3))
                            {
                                failures.Add("(P9-B②) **已进袋的 3 号不该还在**玩家剩余球号里。");
                            }

                            // ③ 进一颗少一颗：再把 5 打进 ⇒ 玩家列表应变空
                            int p9Pocket2 = FirstCornerPocket(r1.tableConfig);
                            SetUpCleanPot(r1, 5, p9Pocket2, r1.Simulator.Balls.Count);
                            AdvanceToRest(r1, RestCap);
                            r1.SetTurn(ShotTurn.Player);
                            AiShotContext p9ctx2 = r1.BuildAiContext();
                            r1.aimDegrees = EightBallAi.AimDegreesForPocket(p9ctx2.cueBallPosition,
                                p9ctx2.ballPositions[5], p9ctx2.pocketCenters[p9Pocket2]);
                            r1.power = 0.5f;
                            if (r1.Launch().magnitude <= 0f)
                            {
                                failures.Add("(P9-B) 前提不成立：第二杆没打出去。");
                            }
                            else
                            {
                                AdvanceToRest(r1, RestCap);
                                if (r1.Simulator.IsBallInPlay(5))
                                {
                                    failures.Add("(P9-B) 前提不成立：5 号没进袋（「进一颗少一颗」无从判定）。");
                                }
                                else if (r1.PlayerRemainingNumbers.Count != 0)
                                {
                                    failures.Add($"(P9-B③) **进一颗少一颗**：5 号进袋后玩家剩余球号应变空，"
                                                 + $"实测 [{string.Join(",", r1.PlayerRemainingNumbers)}]。");
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p9Go);
                }

                // ── P9-D 白球待放置时**真的被抬起来**（世界 Y = 台面 + 0.12 m）；放好后落回台面且可出杆 ──
                var p9dGo = new GameObject("T145P9Float");
                try
                {
                    PhysicsTestDrive f1 = NewAiDrive(p9dGo, rackSeed: 62);
                    var cueVisual = new GameObject("P9CueVisual").transform;
                    f1.ballVisuals = new[] { cueVisual };   // 运行期逐帧同步的那个可视物

                    int fp = FirstCornerPocket(f1.tableConfig);
                    SetUpCleanPot(f1, 3, fp, f1.Simulator.Balls.Count);
                    AdvanceToRest(f1, RestCap);
                    PotCueBall(f1);
                    AdvanceToRest(f1, RestCap);

                    if (!f1.PendingCueBallPlacement)
                    {
                        failures.Add("(P9-D) 前提不成立：白球入袋后应处于待放态。");
                    }
                    else
                    {
                        float floatY = PhysicsTestDrive.CueBallFloatHeightMeters;
                        float restY = PoolConstants.BallRadius;
                        const float dt = 1f / 120f;

                        // ── **P10-C：抬升必须是过渡，不是瞬移** ──
                        // 先推 1/4 的过渡时长 ⇒ 高度应**严格夹在两端之间**（既不是台面、也不是悬浮位）。
                        int quarterSteps = Mathf.Max(1, Mathf.RoundToInt(0.25f * 0.25f / dt));  // cueFloatTransitionSeconds=0.25 ⇒ 0.0625 s
                        for (int i = 0; i < quarterSteps; i++)
                        {
                            f1.Advance(dt);
                        }

                        float midY = cueVisual.localPosition.y;
                        if (!(midY > restY + 1e-4f && midY < floatY - 1e-4f))
                        {
                            failures.Add($"(P10-C) 抬起必须**过渡**：中途高度应严格夹在 {restY:F4} 与 {floatY:F2} 之间，"
                                         + $"实测 {midY:F4} ⇒ 是**瞬移**（用户：「不要马上出现，应该丝滑地过渡」）。");
                        }

                        // 再推足剩余时长 ⇒ 应稳定停在悬浮位
                        for (int i = 0; i < Mathf.CeilToInt(0.60f / dt); i++)
                        {
                            f1.Advance(dt);
                        }

                        if (Mathf.Abs(cueVisual.localPosition.y - floatY) > 2e-3f)
                        {
                            failures.Add($"(P9-D) 待放态白球应**停在悬浮位**（世界 Y ≈ {floatY:F2} m），"
                                         + $"实测 localY={cueVisual.localPosition.y:F4}。");
                        }

                        // **P10-A2③**：**落袋**待放的横向必须是"拿出来"那个固定落点（设计意图，不是瞬移）
                        Vector3 floatXY = f1.CueBallFloatWorldPosition;
                        float dxz = Mathf.Sqrt(
                            (cueVisual.localPosition.x - floatXY.x) * (cueVisual.localPosition.x - floatXY.x)
                            + (cueVisual.localPosition.z - floatXY.z) * (cueVisual.localPosition.z - floatXY.z));
                        if (dxz > 2e-3f)
                        {
                            failures.Add($"(P10-A2③) **落袋**待放的白球应出现在「拿出来」的固定落点 "
                                         + $"({floatXY.x:F3},{floatXY.z:F3})，实测 ({cueVisual.localPosition.x:F3},"
                                         + $"{cueVisual.localPosition.z:F3})，偏差 {dxz * 1000f:F2} mm。");
                        }

                        if (!f1.TryPlaceCueBall(Vector2.zero))
                        {
                            failures.Add("(P9-D) 前提不成立：原点应是合法落点。");
                        }
                        else
                        {
                            // ── **P10-C：落下同样是过渡** ──
                            for (int i = 0; i < quarterSteps; i++)
                            {
                                f1.Advance(dt);
                            }

                            float midDown = cueVisual.localPosition.y;
                            if (!(midDown < floatY - 1e-4f && midDown > restY + 1e-4f))
                            {
                                failures.Add($"(P10-C) **落下**也必须过渡：中途高度应严格夹在 {restY:F4} 与 {floatY:F2} 之间，"
                                             + $"实测 {midDown:F4} ⇒ 是瞬移。");
                            }

                            for (int i = 0; i < Mathf.CeilToInt(0.60f / dt); i++)
                            {
                                f1.Advance(dt);
                            }

                            if (Mathf.Abs(cueVisual.localPosition.y - restY) > 2e-3f)
                            {
                                failures.Add($"(P9-D) 放好之后白球应**落回台面**（Y ≈ {restY:F4}），"
                                             + $"实测 localY={cueVisual.localPosition.y:F4}。");
                            }

                            if (f1.Launch().magnitude <= 0f)
                            {
                                failures.Add("(P9-D) 放好之后应能**正常出杆**。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p9dGo);
                }

                // ── P10-A/B 待放置时**没有球杆、没有瞄准线**（用户：「白球浮空时不该有球杆」「瞄准线残余在球桌上」）──
                // ⚠ 关键：实机里"**犯规自由球**"那种待放**白球还在台上**（`IsBallInPlay(0) == true`）
                //   ⇒ 只靠 `cueBallInPlay` 挡不住 ⇒ 必须有**显式** `awaitingPlacement` 门。
                if (CueStickController.ShouldShowStick(true, true, false, false, false, awaitingPlacement: true))
                {
                    failures.Add("(P10-A) 待放置时球杆必须**不可见**（显式 `awaitingPlacement` 门），"
                                 + "实测仍返回可见 ⇒ 白球还在台上的那种待放（犯规自由球）挡不住。");
                }

                if (!CueStickController.ShouldShowStick(true, true, false, false, false, awaitingPlacement: false))
                {
                    failures.Add("(P10-A) 放下之后球杆必须**回来**（`awaitingPlacement == false` ⇒ true）。");
                }

                if (AimAssistController.ShouldShowAimAssist(true, true, true, false, false, awaitingPlacement: true))
                {
                    failures.Add("(P10-B) 待放置时瞄准线**不许画**（应落到唯一清空点 `HideAllAimVisuals()`），实测仍返回可见。");
                }

                if (!AimAssistController.ShouldShowAimAssist(true, true, true, false, false, awaitingPlacement: false))
                {
                    failures.Add("(P10-B) 放下之后瞄准线必须**回来**。");
                }

                // ── P10-A2（**定稿**）：**两种待放都要抬**（犯规自由球是 ball-in-hand 的重放入口），
                //    但**横向位置按来源分**：犯规 ⇒ 它自己的位置（不许瞬移到台心）；落袋 ⇒ 拿出来那个落点。
                var p10Go = new GameObject("T145P10FloatScope");
                try
                {
                    PhysicsTestDrive b1 = NewAiDrive(p10Go, rackSeed: 63);
                    var cueVisual2 = new GameObject("P10CueVisual").transform;
                    b1.ballVisuals = new[] { cueVisual2 };
                    int bp = FirstCornerPocket(b1.tableConfig);
                    SetUpCleanPot(b1, 3, bp, b1.Simulator.Balls.Count);
                    AdvanceToRest(b1, RestCap);
                    b1.SetTurn(ShotTurn.Player);

                    // **犯规自由球**：白球**仍在台上**，但进入了待放态
                    b1.ApplyVerdict(new RuleVerdict
                    {
                        nextTurn = ShotTurn.Player,
                        winner = ShotTurn.Player,
                        ballInHand = BallInHandKind.FullTable,
                    });

                    var p10Failures = new List<string>();
                    const float dt2 = 1f / 120f;
                    if (!b1.IsWaitingForPlacement)
                    {
                        p10Failures.Add("(P10-A2) 前提不成立：犯规自由球应进入待放态。");
                    }
                    else if (!b1.Simulator.IsBallInPlay(0))
                    {
                        p10Failures.Add("(P10-A2) 前提不成立：本项要的是**白球仍在台上**的待放。");
                    }
                    else
                    {
                        Vector2 ownPhysics = b1.Simulator.Balls[0].position;

                        // 推足整个过渡（"推一帧 ≠ 到达稳态" —— 上次的真空教训）
                        for (int i = 0; i < Mathf.CeilToInt(1.5f * PhysicsTestDrive.CueFloatTransitionSeconds / dt2)
                             + Mathf.CeilToInt(0.3f / dt2); i++)
                        {
                            b1.Advance(dt2);
                        }

                        // **②（定稿）**：犯规 + 球在台上 + 位置合法 ⇒ **不抬**（`MustPlaceCueBallBeforeShooting == false`）
                        //   ⇒ 杆/线都在、**能直接出杆**；出杆后自由球被**消耗掉**（否则"球老在飘"）。
                        float y2 = cueVisual2.localPosition.y;
                        if (Mathf.Abs(y2 - PoolConstants.BallRadius) > 2e-3f)
                        {
                            p10Failures.Add($"(P10-②) 犯规但**球在台上且位置合法**时**不许抬球**（应贴台面），实测 localY={y2:F4}。");
                        }

                        if (b1.MustPlaceCueBallBeforeShooting)
                        {
                            p10Failures.Add("(P10-②) 该情形 `MustPlaceCueBallBeforeShooting` 应为 **false**（允许直接出杆）。");
                        }

                        AdvanceToRest(b1, RestCap);
                        b1.SetTurn(ShotTurn.Player);
                        b1.aimDegrees = 180f;
                        b1.power = 0.6f;
                        if (b1.Launch().magnitude <= 0f)
                        {
                            p10Failures.Add("(P10-②) 球在台上且位置合法 ⇒ **必须能直接出杆**，实测被拒。");
                        }
                        else if (b1.IsWaitingForPlacement || b1.BallInHand != BallInHandKind.None)
                        {
                            p10Failures.Add($"(P10-②) 直接出杆后必须**消耗掉自由球**（`IsWaitingForPlacement == false` 且 `BallInHand == None`），"
                                            + $"实测 waiting={b1.IsWaitingForPlacement} ballInHand={b1.BallInHand}。");
                        }

                        // ② **横向不瞬移**：应停在它自己的物理位置，而不是"拿出来"那个固定落点
                        Vector3 ownWorld = PoolGeometry.PhysicsBallToUnity(ownPhysics);
                        Vector3 floatWorld = b1.CueBallFloatWorldPosition;
                        float dOwn = Mathf.Sqrt(
                            (cueVisual2.localPosition.x - ownWorld.x) * (cueVisual2.localPosition.x - ownWorld.x)
                            + (cueVisual2.localPosition.z - ownWorld.z) * (cueVisual2.localPosition.z - ownWorld.z));
                        float dFloat = Mathf.Sqrt(
                            (cueVisual2.localPosition.x - floatWorld.x) * (cueVisual2.localPosition.x - floatWorld.x)
                            + (cueVisual2.localPosition.z - floatWorld.z) * (cueVisual2.localPosition.z - floatWorld.z));
                        if (dOwn > 1e-3f)
                        {
                            p10Failures.Add($"(P10-A2②) 犯规自由球抬起时**横向不瞬移**：应停在它自己的位置 "
                                            + $"({ownWorld.x:F3},{ownWorld.z:F3})，实测 ({cueVisual2.localPosition.x:F3},"
                                            + $"{cueVisual2.localPosition.z:F3})，偏差 {dOwn * 1000f:F2} mm。");
                        }

                        if (dFloat < 1e-3f && dOwn > 1e-3f)
                        {
                            p10Failures.Add("(P10-A2②) 横向落点等于**「拿出来」的固定落点** ⇒ 犯规自由球被瞬移到台心。");
                        }
                    }

                    failures.AddRange(p10Failures);
                }
                finally
                {
                    Object.DestroyImmediate(p10Go);
                }

                // ── P10 裁定 (a)：**待放期一律拒杆**，且**不许拒过头**（三条负路径）──
                var p10RefuseGo = new GameObject("T145P10Refuse");
                try
                {
                    PhysicsTestDrive q1 = NewAiDrive(p10RefuseGo, rackSeed: 64);
                    AdvanceToRest(q1, RestCap);

                    // ① **开球那杆不许被拒**（`IsWaitingForPlacement` 必须是 false）
                    if (q1.IsWaitingForPlacement)
                    {
                        failures.Add("(P10-拒杆①) 前提不成立：开局不应处于待放态。");
                    }

                    q1.SetTurn(ShotTurn.Player);
                    q1.aimDegrees = 180f;
                    q1.power = 0.6f;
                    if (q1.Launch().magnitude <= 0f)
                    {
                        failures.Add("(P10-拒杆①) **开球那一杆不许被拒**（拒过头了）—— 实测返回零向量。");
                    }

                    AdvanceToRest(q1, RestCap);

                    // ③ 待放期开杆 ⇒ 零向量 + 计数 +1
                    PotCueBall(q1);
                    if (!q1.IsWaitingForPlacement)
                    {
                        failures.Add("(P10-拒杆③) 前提不成立：白球入袋后应进入待放态。");
                    }
                    else
                    {
                        int refuseBefore = q1.RefusedLaunchWhileAwaitingCount;
                        q1.aimDegrees = 180f;
                        q1.power = 0.6f;
                        if (q1.Launch().magnitude > 0f)
                        {
                            failures.Add("(P10-拒杆③) **待放期开杆必须被拒**（球是被拿在手上的），实测打出去了。");
                        }

                        if (q1.RefusedLaunchWhileAwaitingCount != refuseBefore + 1)
                        {
                            failures.Add($"(P10-拒杆③) 被拒**必须留痕**（`RefusedLaunchWhileAwaitingCount` 应 +1），"
                                         + $"实测 {refuseBefore} → {q1.RefusedLaunchWhileAwaitingCount}。");
                        }

                        // ② 放置完成后的第一杆必须**成功**
                        if (!q1.TryPlaceCueBall(Vector2.zero))
                        {
                            failures.Add("(P10-拒杆②) 前提不成立：原点应是合法落点。");
                        }
                        else
                        {
                            AdvanceToRest(q1, RestCap);
                            q1.aimDegrees = 180f;
                            q1.power = 0.6f;
                            if (q1.Launch().magnitude <= 0f)
                            {
                                failures.Add("(P10-拒杆②) 放置完成后的第一杆必须**成功**（不许拒过头）。");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(p10RefuseGo);
                }

                // ── P10-③ 犯规 + **位置不合法**（厨房外）⇒ 必须放球：抬 + 杆藏 + 拒杆 ──
                var p10cGo = new GameObject("T145P10MustPlace");
                try
                {
                    PhysicsTestDrive c1 = NewAiDrive(p10cGo, rackSeed: 65);
                    var cueVisual3 = new GameObject("P10C").transform;
                    c1.ballVisuals = new[] { cueVisual3 };
                    SetUpCleanPot(c1, 3, FirstCornerPocket(c1.tableConfig), c1.Simulator.Balls.Count);
                    AdvanceToRest(c1, RestCap);
                    // 把白球挪到**厨房外**（正 x 侧、远过头线）⇒ 厨房自由球下"当前位置不合法"
                    c1.Simulator.PlaceBall(0, new Vector2(0.60f, 0.20f), Vector2.zero);
                    AdvanceToRest(c1, RestCap);
                    c1.SetTurn(ShotTurn.Player);
                    c1.ApplyVerdict(new RuleVerdict
                    {
                        nextTurn = ShotTurn.Player,
                        winner = ShotTurn.Player,
                        ballInHand = BallInHandKind.Kitchen,
                    });

                    var cFail = new List<string>();
                    if (!c1.IsWaitingForPlacement)
                    {
                        cFail.Add("(P10-③) 前提不成立：厨房自由球应进入待放态。");
                    }
                    else if (!c1.Simulator.IsBallInPlay(0))
                    {
                        cFail.Add("(P10-③) 前提不成立：本项要的是**球在台上**的「位置不合法」，不是落袋那种（球离台）。");
                    }
                    else if (c1.CanPlaceCueBall(c1.Simulator.Balls[0].position))
                    {
                        cFail.Add("(P10-③) 前提不成立：**厨房外**的当前位置应被判**不合法**（`CanPlaceCueBall == false`）"
                                  + " ⇒ 这里为 true 说明厨房分支没生效，本项无从判定。");
                    }
                    else if (!c1.MustPlaceCueBallBeforeShooting)
                    {
                        cFail.Add("(P10-③) 前提不成立：**三前提齐**（待放 + 球在台上 + 位置不合法）时 "
                                  + "`MustPlaceCueBallBeforeShooting` 必须为 **true**。");
                    }
                    else
                    {
                        const float dtc = 1f / 120f;
                        for (int i = 0; i < Mathf.CeilToInt(1.2f * PhysicsTestDrive.CueFloatTransitionSeconds / dtc); i++)
                        {
                            c1.Advance(dtc);
                        }

                        if (Mathf.Abs(cueVisual3.localPosition.y - PhysicsTestDrive.CueBallFloatHeightMeters) > 2e-3f)
                        {
                            cFail.Add($"(P10-③) 必须放球时应当**抬起来**（Y ≈ {PhysicsTestDrive.CueBallFloatHeightMeters:F2}），"
                                      + $"实测 localY={cueVisual3.localPosition.y:F4}。");
                        }

                        if (CueStickController.ShouldShowStick(true, true, false, false, false, awaitingPlacement: true))
                        {
                            cFail.Add("(P10-③) 必须放球时球杆**不可见**。");
                        }

                        int refBefore = c1.RefusedLaunchWhileAwaitingCount;
                        c1.aimDegrees = 180f;
                        c1.power = 0.6f;
                        if (c1.Launch().magnitude > 0f)
                        {
                            cFail.Add("(P10-③) 必须放球时 `Launch()` **必须被拒**，实测打出去了。");
                        }

                        if (c1.RefusedLaunchWhileAwaitingCount != refBefore + 1)
                        {
                            cFail.Add($"(P10-③) 被拒必须留痕（应 +1），实测 {refBefore} → {c1.RefusedLaunchWhileAwaitingCount}。");
                        }
                    }

                    failures.AddRange(cFail);
                }
                finally
                {
                    Object.DestroyImmediate(p10cGo);
                }

                Assert.IsEmpty(failures, "T145（AI 出杆入口）失败：\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(goManual);
            }
        }

        /// <summary>取一只**角袋**（优先 +x/+y 象限；与具体资产编号解耦）。</summary>
        private static int FirstCornerPocket(TableConfig table)
        {
            int fallback = 0;
            bool hasFallback = false;
            for (int i = 0; i < table.PocketCount; i++)
            {
                Pocket pocket = table.GetPocket(i);
                if (pocket == null || !pocket.IsCorner)
                {
                    continue;
                }

                if (!hasFallback)
                {
                    fallback = i;
                    hasFallback = true;
                }

                if (pocket.throatCenter.x > 0f && pocket.throatCenter.y > 0f)
                {
                    return i;
                }
            }

            return fallback;
        }

        /// <summary>
        /// 造一个**可控开球**布局：白球 + **7 颗实色挤成小三角**（中区、远离袋口）
        /// ⇒ 满力冲开后 7 颗都会散开撞库（满足"≥4 颗目标球碰库"= **有效开球**），但**不该进球**。
        /// 判据 ④（开球换手）与 ⑩（`isBreak` 只在开球那杆为 true）**共用**这一个布局（同源，避免两处各写一份）。
        /// </summary>
        private static void PlaceBreakCluster(PhysicsTestDrive drive)
        {
            var starts = new[]
            {
                new Vector2(-0.86f, 0f),
                new Vector2(-0.80f, 0.030f), new Vector2(-0.80f, -0.030f),
                new Vector2(-0.74f, 0.060f), new Vector2(-0.74f, 0f), new Vector2(-0.74f, -0.060f),
                new Vector2(-0.68f, 0.030f),
            };
            int placed = 0;
            for (int ball = 0; ball < drive.Simulator.Balls.Count; ball++)
            {
                if (ball == 0)
                {
                    drive.Simulator.PlaceBall(ball, new Vector2(-0.95f, 0f), Vector2.zero);
                    continue;
                }

                Vector2 pos = placed < starts.Length
                    ? starts[placed]
                    : new Vector2(0f, 50f + ball * 0.5f);
                if (placed < starts.Length)
                {
                    placed++;
                }

                drive.Simulator.PlaceBall(ball, pos, Vector2.zero);
            }
        }

        /// <summary>
        /// 把球台改成**干净进球局面**：`targetBall` 放在"离某角袋 0.45 m"的直线上、白球在其**幽灵球**后方 0.30 m
        /// （正对 ⇒ 几何上必进），其余球一律挪到**台外停车位**（`y = 50 + i·0.5`，彼此不重叠 ⇒ 首次 `Step` 即冻结为"不在场"）。
        ///
        /// ⚠ **为什么必须这么做**：满 15 颗三角堆里**绝大多数球都被邻球挡住**，根本打不进
        /// ⇒ 拿满堆当"评估器"的判据会**真空**（**实测**：幽灵球基准方向在满堆上四个力度档**全不进袋**，
        /// 于是"进袋候选"这个泄漏源根本不存在）。
        /// </summary>
        private static void SetUpCleanPot(PhysicsTestDrive drive, int targetBall, int pocketIndex, int ballCount)
        {
            TableConfig table = drive.tableConfig;
            Vector2 pocket = table.GetPocket(pocketIndex).throatCenter;
            Vector2 dir = (pocket - Vector2.zero).normalized;
            Vector2 target = pocket - dir * 0.45f;
            Vector2 ghost = target - dir * (2f * PoolConstants.BallRadius);
            Vector2 cue = ghost - dir * 0.30f;

            for (int ball = 0; ball < ballCount; ball++)
            {
                if (ball == 0)
                {
                    drive.Simulator.PlaceBall(ball, cue, Vector2.zero);
                }
                else if (ball == targetBall)
                {
                    drive.Simulator.PlaceBall(ball, target, Vector2.zero);
                }
                else
                {
                    drive.Simulator.PlaceBall(ball, new Vector2(0f, 50f + ball * 0.5f), Vector2.zero);
                }
            }
        }
    }
}
