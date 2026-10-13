using System.IO;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 4 切片 C —— **袋喉冻结（T13）** 与 **多球遍历顺序确定性（D-3）**。
    ///
    /// 补 `Docs/Reviews/D4-Day4独立复核.md` §7.2（袋喉冻结路径零覆盖）与 §7.3 D-3（多球遍历顺序未测）。
    ///
    /// 判别力声明：
    ///  · T13a 正向 —— `PocketProximityEvents` 在旧套件里**只被断言 `== 0`**，冻结分支
    ///    （`velocity=0` + `_frozen=true` + `Step()` 里 `continue`）**从未执行过**。本用例主动把球
    ///    射进中袋（`pocket[1]`，喉法向 (0,−1)），使它真的触发冻结。
    ///  · T13b 负向 —— 台面正中滚动的球**不得**触发冻结，防"恒冻结"式假通过。
    ///  · D-3 —— 旧套件**全部只装 1 球**；本用例装 16 球，同初态跑两次逐位比较，
    ///    并**读取源码**断言热路径无非确定性容器。
    /// </summary>
    public class PoolSimulatorPocketAndMultiBallTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";
        private const string SimulatorSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs";

        private static TableConfig _config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 " + ConfigPath + "（先运行 Billiards/Data/Import Table Reference Data）。");
        }

        // ── T13a：正向，球正对中袋 —— 必须冻结 ────────────────────────────

        /// <summary>
        /// 自 `(0, 0)` 以 `v = (0, −2)` 沿 `x = 0` 直下，正对 side pocket（`index 1`，喉心 (0,−0.6950635)、
        /// 喉法向 (0,−1)）。中袋缺口 x∈(−0.107,+0.107) 无段覆盖（spec §2.2），球全程无接触直穿袋口。
        ///
        /// 断言：`PocketProximityEvents ≥ 1`；末速 `== 0`（冻结）；再 Step() 若干次位置**逐位不变**；
        /// 末态在喉平面**进网侧**。并**量化冻结分辨率**：实际越界量 vs 理论上界 `v·dt_sub`。
        /// </summary>
        [Test]
        public void T13a_PocketProximity_FreezesBall_AndStopsIntegration()
        {
            Pocket pocket = SidePocketFacingNegativeY();
            float speed = 2f;

            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            sim.LoadBalls(new[] { new BallState { position = Vector2.zero, velocity = new Vector2(0f, -speed) } });

            int stepsToFreeze = -1;
            for (int s = 0; s < 200; s++)
            {
                sim.Step();
                if (sim.PocketProximityEvents >= 1)
                {
                    stepsToFreeze = s;
                    break;
                }
            }

            Assert.GreaterOrEqual(sim.PocketProximityEvents, 1,
                "T13a：球正对中袋直下，却从未触发 PocketProximityEvents（冻结路径未被覆盖）。");
            Assert.AreEqual(0, sim.TotalContactCount, "T13a：中袋缺口应无库边接触（否则本用例测的不是'穿出'）。");

            BallState frozenState = sim.Balls[0];
            Assert.AreEqual(0f, frozenState.velocity.x, "T13a：冻结球速度应置 0（x）。");
            Assert.AreEqual(0f, frozenState.velocity.y, "T13a：冻结球速度应置 0（y）。");

            // 进网侧判据。
            float netSide = Vector2.Dot(frozenState.position - pocket.throatCenter, pocket.throatNormal);
            Assert.Greater(netSide, 0f, $"T13a：冻结位置应在喉平面进网侧，实测 dot={netSide:R}。");

            // 冻结分辨率：球可在子步内越过喉平面最多 v·dt_sub（CheckPocketProximity 每子步末才跑）。
            float overshootBound = speed * sim.SubStepDeltaTime;
            Assert.LessOrEqual(netSide, overshootBound + 1e-6f,
                $"T13a：越界量 {netSide:E3} m 超过理论上界 v·dt_sub = {overshootBound:E3} m。");

            // 冻结后必须**停止积分**：再跑 20 步，位置逐位不变。
            Vector2 frozenPos = frozenState.position;
            for (int s = 0; s < 20; s++)
            {
                sim.Step();
            }

            Assert.AreEqual(frozenPos.x.ToString("R"), sim.Balls[0].position.x.ToString("R"),
                "T13a：冻结后继续 Step()，位置 x 发生变化（未停止积分）。");
            Assert.AreEqual(frozenPos.y.ToString("R"), sim.Balls[0].position.y.ToString("R"),
                "T13a：冻结后继续 Step()，位置 y 发生变化（未停止积分）。");
            Assert.AreEqual(1, sim.PocketProximityEvents, "T13a：冻结为一次事件，不应重复计数。");

            TestContext.WriteLine(
                $"T13a: 冻结于第 {stepsToFreeze} 步; 末位={frozenState.position:R}; 越界量={netSide * 1000f:F4} mm " +
                $"（上界 v·dt_sub={overshootBound * 1000f:F4} mm，占用 {netSide / overshootBound * 100f:F1}%）；" +
                $"冻结后再 20 步位置不变 ✓; events={sim.PocketProximityEvents}");
        }

        // ── T13b：负向，台面正中滚动 —— 不得冻结 ──────────────────────────

        /// <summary>
        /// 球在台面正中以 `v = (0.6, 0)` 滚动 60 步（0.3 m），远离任何袋喉平面。
        /// 断言 `PocketProximityEvents == 0`、速度非零、位置确有变化。
        /// 能测出：把冻结写成"恒冻结"（例如判据符号写反）的实现。
        /// </summary>
        [Test]
        public void T13b_BallRollingInCentre_IsNotFrozen()
        {
            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            var v0 = new Vector2(0.6f, 0f);
            sim.LoadBalls(new[] { new BallState { position = Vector2.zero, velocity = v0 } });

            const int steps = 60;
            for (int s = 0; s < steps; s++)
            {
                sim.Step();
            }

            Assert.AreEqual(0, sim.PocketProximityEvents, "T13b：台面正中滚动却触发了袋喉冻结（判据恒真/符号反）。");
            Assert.AreEqual(0, sim.TotalContactCount, "T13b：本用例应为台面内自由滚动，不应接触库边。");
            Assert.Greater(sim.Balls[0].velocity.magnitude, 0f, "T13b：未越界球不应被置零速度（恒冻结）。");
            Assert.Greater(sim.Balls[0].position.x, 0f, "T13b：球应已沿 +x 移动（非空转）。");

            TestContext.WriteLine(
                $"T13b: 60 步后台面内位置={sim.Balls[0].position:R}, 速度={sim.Balls[0].velocity:R}, events={sim.PocketProximityEvents}（应为 0）");
        }

        // ── D-3：多球遍历顺序确定性 ───────────────────────────────────────

        /// <summary>
        /// 装 **16 球**（4×4 网格，|x| ≥ 0.20 避开中袋缺口，速度指向长库以制造多次接触），
        /// 同初态经**两个独立构造**的模拟器各跑 `Step() × 120` ⇒ 逐球位置/速度**逐位一致**（`R` 格式）。
        ///
        /// 排除"字典/集合迭代序泄漏"的两条独立手段：
        ///  1. **逐位一致**：任何依赖 `Time`/`Random`/未初始化状态的实现都会破坏它；
        ///  2. **源码断言**：热路径 `PoolSimulator.cs` 不得出现 `Dictionary`/`HashSet`/`SortedSet`/
        ///     `OrderBy`/`GroupBy` 等无序容器或重排 —— 这是"遍历顺序只由数组下标决定"的**结构性证据**
        ///     （进程内哈希容器迭代序其实稳定，故仅靠 (1) 不足以证明，必须有 (2)）。
        ///
        /// 非空转：断言确有接触发生（`TotalContactCount > 0`），否则"一致"只说明球都没动。
        /// </summary>
        [Test]
        public void D3_SixteenBalls_TraversalOrder_IsBitIdentical()
        {
            BallState[] initial = BuildSixteenBalls();
            const int steps = 120;

            PoolSimulator a = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            PoolSimulator b = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            a.LoadBalls(initial);
            b.LoadBalls(initial);

            for (int s = 0; s < steps; s++)
            {
                a.Step();
                b.Step();
            }

            Assert.Greater(a.TotalContactCount, 0, "D-3：16 球跑 120 步应发生库边接触（非空转）。");
            Assert.AreEqual(0, a.PocketProximityEvents, "D-3：本用例球位远离袋喉，不应冻结（保证测的是滚动而非冻结）。");

            for (int i = 0; i < initial.Length; i++)
            {
                Assert.AreEqual(a.Balls[i].position.x.ToString("R"), b.Balls[i].position.x.ToString("R"),
                    $"D-3：球 {i} 位置 x 逐位不一致。");
                Assert.AreEqual(a.Balls[i].position.y.ToString("R"), b.Balls[i].position.y.ToString("R"),
                    $"D-3：球 {i} 位置 y 逐位不一致。");
                Assert.AreEqual(a.Balls[i].velocity.x.ToString("R"), b.Balls[i].velocity.x.ToString("R"),
                    $"D-3：球 {i} 速度 x 逐位不一致。");
                Assert.AreEqual(a.Balls[i].velocity.y.ToString("R"), b.Balls[i].velocity.y.ToString("R"),
                    $"D-3：球 {i} 速度 y 逐位不一致。");
            }

            // 结构性证据：遍历顺序只由数组下标决定。
            AssertSimulatorHotPathHasNoUnorderedContainer();

            TestContext.WriteLine(
                $"D-3: 16 球 × {steps} 步，两次运行逐位一致 ✓；contacts={a.TotalContactCount}, " +
                $"events={a.PocketProximityEvents}; 样例 ball[0] pos={a.Balls[0].position:R} vel={a.Balls[0].velocity:R}");
        }

        // ── 辅助 ──────────────────────────────────────────────────────────

        private static PhysicsParams Params(float e, float mu, float a)
        {
            return new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                rollingDeceleration = a
            };
        }

        /// <summary>取朝 −y 的中袋（index 1）。</summary>
        private Pocket SidePocketFacingNegativeY()
        {
            foreach (Pocket p in _config.pockets)
            {
                if (p.kind == "side" && p.throatNormal.y < 0f && Mathf.Abs(p.throatNormal.x) < 1e-6f)
                {
                    return p;
                }
            }

            Assert.Fail("未找到朝 −y 的中袋（pocket[1]）。");
            return null;
        }

        /// <summary>4×4 = 16 球，|x| ≥ 0.20 避开中袋缺口（x∈(−0.107,0.107)），速度指向长库制造接触。</summary>
        private static BallState[] BuildSixteenBalls()
        {
            float[] xs = { -0.30f, -0.20f, 0.20f, 0.30f };
            float[] ys = { -0.30f, -0.15f, 0.15f, 0.30f };
            var balls = new BallState[16];

            int k = 0;
            foreach (float x in xs)
            {
                foreach (float y in ys)
                {
                    // 速度：沿 y 朝最近长库（±0.635），并带小 x 分量。
                    float vy = Mathf.Sign(y) * 0.8f;
                    float vx = Mathf.Sign(x) * 0.12f;
                    balls[k++] = new BallState
                    {
                        position = new Vector2(x, y),
                        velocity = new Vector2(vx, vy)
                    };
                }
            }

            return balls;
        }

        /// <summary>
        /// 结构性确定性守卫：热路径源码不得含无序容器 / 重排算子（R5-3）。
        /// 读取的是**被测源码文件本身**（非影子副本）。
        /// </summary>
        private static void AssertSimulatorHotPathHasNoUnorderedContainer()
        {
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), SimulatorSourcePath);
            Assert.IsTrue(File.Exists(full), "D-3：找不到内核源码 " + full);
            string src = File.ReadAllText(full);

            // 先剔除注释（`//` 之后的内容）—— 本类头部注释**特意点名**了这些容器作为"禁用清单"，
            // 若连注释一起扫描会假阳性（这正是本测试自身必须先被检验的地方）。
            var codeOnly = new System.Text.StringBuilder();
            foreach (string line in src.Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                codeOnly.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            string code = codeOnly.ToString();

            string[] forbidden =
            {
                "Dictionary<", "HashSet<", "SortedDictionary<", "SortedSet<", "Hashtable",
                "OrderBy", "GroupBy", "ToLookup", "Random"
            };

            foreach (string token in forbidden)
            {
                Assert.IsFalse(code.Contains(token),
                    $"D-3：热路径**代码**（已剔除注释）出现非确定性/无序结构 `{token}` —— 遍历顺序不再只由数组下标决定（违反 R5-3）。");
            }

            // 正证据：状态推进以 for 升序循环（`for (...; i < _balls.Length; i++)`）。
            Assert.IsTrue(code.Contains("for (int i = 0; i < _balls.Length; i++)"),
                "D-3：未找到升序球遍历（`for (int i = 0; i < _balls.Length; i++)`），确定性契约无从保证。");
        }
    }
}
