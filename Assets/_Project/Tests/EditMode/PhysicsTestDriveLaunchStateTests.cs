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
        /// 前提断言：静止位置**确实不在** `footSpot`（否则本判据无区分度 —— 会与"被摆回 `footSpot`"混淆）。
        ///
        /// **能测出**：白球被摆回 `footSpot`（本缺陷的另一半）。
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
                Assert.Greater((stopped - Table().footSpot).magnitude, 0.05f,
                    "T66：前提 —— 白球应确实滚离了开球点（否则本判据与“被摆回 footSpot”不可区分）。");
                Assert.IsTrue(drive.Simulator.IsBallInPlay(0), "T66：前提 —— 白球应仍在场上（未入袋）。");

                Assert.Greater(drive.Launch().magnitude, 0f, "T66：前提 —— 第二杆应被接受。");
                Assert.IsFalse(drive.LastLaunchRespawnedCueBall, "T66：白球在场上，不得触发重摆。");

                Vector2 after = drive.Simulator.Balls[0].position;
                Assert.Less((after - stopped).magnitude, 1e-9f,
                    $"T66：第二杆的白球位置应 == 它停下的地方 {RVec(stopped)}，实测 {RVec(after)}"
                    + "（被摆回开球点 = 整盘重置缺陷）。");
                TestContext.WriteLine($"T66: 第二杆白球仍在 {RVec(after)}（开球点 {RVec(Table().footSpot)}）✓");
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

        // ══════════════════ T70 白球入袋 ⇒ 显式放回开球点（可观测）════════════

        /// <summary>
        /// **白球入袋 ⇒ 出杆时显式放回 `footSpot`，且这件事必须可观测**（不许静默）。
        ///
        /// 做法：单球模式 ⇒ 静止 ⇒ 用 `PlaceBall(0, …)` 把白球放到中袋喉口内侧并朝袋内给初速
        /// ⇒ 推进到静止（白球**确定性入袋**、离台）⇒ 断言前提（已入袋、不在场上）
        /// ⇒ 出杆 ⇒ 断言 ① 白球**已回到场上**；② 位置 == `footSpot`（逐位/容差 0）；
        /// ③ 只读出口 <see cref="PhysicsTestDrive.LastLaunchRespawnedCueBall"/> == `true`（**可观测性**）。
        ///
        /// **能测出**：白球入袋后"静默消失"（既不放回也不报错）／放回了但**没有任何出口**可断言。
        /// **注入负对照（实测）**：把 `Launch()` 的起点改成"白球当前位置"（不放回）⇒ FAIL。
        /// </summary>
        [Test]
        public void T70_Launch_RespawnsCapturedCueBallAtFootSpot_Observably()
        {
            var go = new GameObject("T70Drive");
            try
            {
                PhysicsTestDrive drive = NewDrive(go, rack: false);
                AdvanceToRest(drive, RestCap);
                Assert.IsTrue(drive.Simulator.TableAtRest, "T70：前提 —— 摆好后应在上限内静止。");

                Pocket pocket = SidePocket(Table());
                Vector2 tn = pocket.throatNormal;
                drive.Simulator.PlaceBall(0, pocket.throatCenter - 0.15f * tn, 1.0f * tn);

                int steps = AdvanceToRest(drive, RestCap);
                Assert.GreaterOrEqual(drive.Simulator.CapturedPocketIndex(0), 0,
                    $"T70：前提 —— 白球应已入袋（{steps} 步后仍未入袋）。");
                Assert.IsFalse(drive.Simulator.IsBallInPlay(0), "T70：前提 —— 已入袋的白球应不在场上。");
                Assert.IsTrue(drive.Simulator.TableAtRest, "T70：前提 —— 入袋后整盘应静止。");

                Vector2 footSpot = Table().footSpot;
                drive.aimDegrees = 180f;
                drive.power = 1f;
                Assert.Greater(drive.Launch().magnitude, 0f, "T70：前提 —— 出杆应被接受。");

                var failures = new List<string>();
                if (!drive.Simulator.IsBallInPlay(0))
                {
                    failures.Add("白球入袋后出杆，它**没有回到场上**（静默消失）。");
                }

                float off = (drive.Simulator.Balls[0].position - footSpot).magnitude;
                if (!(off < 1e-6f))
                {
                    failures.Add($"白球应被放回开球点 {RVec(footSpot)}，实测 {RVec(drive.Simulator.Balls[0].position)}（偏 {off:E3} m）。");
                }

                if (!drive.LastLaunchRespawnedCueBall)
                {
                    failures.Add("`LastLaunchRespawnedCueBall` 应为 true —— 「白球重摆」这件事**必须可观测**（不许静默）。");
                }

                Assert.IsEmpty(failures,
                    "T70 白球入袋后重摆失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T70: 白球入袋后出杆 ⇒ 回到 {RVec(drive.Simulator.Balls[0].position)}（= footSpot），"
                                      + "`LastLaunchRespawnedCueBall` = true ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
