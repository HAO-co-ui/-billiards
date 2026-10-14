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
    /// **袋喉冻结（T13）** 与 **多球遍历顺序确定性（D-3）**。
    ///
    /// 补 `Docs/Reviews/D4-Day4独立复核.md` §7.2（袋喉冻结路径零覆盖）与 §7.3 D-3（多球遍历顺序未测）。
    ///
    /// 判别力声明：
    ///  · T13a 正向 —— `PocketProximityEvents` 在旧套件里**只被断言 `== 0`**，冻结分支
    ///    （`velocity=0` + `_frozen=true` + `Step()` 里 `continue`）**从未执行过**。本用例主动把球
    ///    射进中袋（`pocket[1]`，喉法向 (0,−1)），使它真的触发冻结。
    ///    ⚠ 它有一条**周期性盲带**（越界子步 126 可被 3 整除）—— 见其 doc 注释（铁律 12）。
    ///  · **T13c 正向（补 T13a 盲带）** —— 同构探针，速度取 2.5 ⇒ 越界子步 **101**（`%3=2`）
    ///    ⇒ 对"袋喉检查由每子步改成每 3 子步"敏感。**T13a 与 T13c 不可互相替代**（铁律 4）。
    ///  · T13b 负向 —— 台面正中滚动的球**不得**触发冻结，防"恒冻结"式假通过。
    ///  · D-3 —— 旧套件**全部只装 1 球**；本用例装 16 球，同初态跑两次逐位比较，
    ///    并**读取源码**断言热路径无非确定性容器 / 重排算子 / 并行。
    ///    ⚠ 源码断言是**禁用清单（denylist）**：**"没出现这些词" ≠ "确定性已证明"**（见其 doc）。
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
        ///
        /// ## ⚠ 本探针存在**周期性盲带**（显式声明，铁律 12）
        ///
        /// **算术原因**：本档 `v = 2 m/s`、`dt_sub = dt/SubSteps = (1/120)/3 = 1/360 s`
        /// ⇒ 每子步走 `2/360 m`；起点到喉平面 `0.6950635 m` ⇒ 越界发生在第
        /// `ceil(0.6950635 × 360 / 2) = ceil(125.11) = **126**` 个子步。
        /// **`126 % 3 == 0`（可被 3 整除）** ⇒ 若把袋喉检查从"每子步"改成"**每 3 子步**"，
        /// 采样点**恰好落回同一子步** ⇒ 冻结位置**逐位相同** ⇒ **本用例观测不到该突变**
        /// （实测：每 3 子步仍 Passed，每 4 子步才 FAIL；因为 `126 % 4 = 2 ≠ 0`）。
        ///
        /// ⇒ **不得**把本用例引用为"袋喉检查周期正确性"的检出保证。
        /// 该盲带由 <see cref="T13c_PocketProximity_SameBound_AtNonDivisibleCrossingSubStep"/> 补上：
        /// 它取一个使越界索引**不被 3 整除**的速度（`v = 2.5` ⇒ 索引 **101**，`101 % 3 = 2`）。
        /// **本探针不删** —— 它是"子步末检查"的正例（另一条互补覆盖）。
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

        // ── T13c：第二个正向探针 —— 越界子步索引**不被 3 整除**（补 T13a 的周期盲带）──

        /// <summary>
        /// 与 <see cref="T13a_PocketProximity_FreezesBall_AndStopsIntegration"/> 同构，
        /// 唯一区别是速度取 `2.5 m/s` ⇒ 越界子步索引 `ceil(0.6950635 × 360 / 2.5) = ceil(100.09) = **101**`，
        /// **`101 % 3 = 2 ≠ 0`** ⇒ 对「袋喉检查由**每子步**改成**每 3 子步**」这一突变**敏感**
        /// （而 T13a 的索引 126 可被 3 整除 ⇒ 对它**盲**，见 T13a 的 doc）。
        ///
        /// 断言与 T13a **同一条上界** `overshootBound = v·dt_sub`（冻结分辨率不因速度而放宽，
        /// 否则就是"换个速度顺便把判据放宽"）。
        /// 并把**越界子步索引**及其 `% 3` 余数写进消息；余数非 0 正是本探针存在的理由 ——
        /// 故它也被写成**断言**，防止后来者改速度后**悄悄**把本探针退化成 T13a 的同款盲带。
        /// </summary>
        [Test]
        public void T13c_PocketProximity_SameBound_AtNonDivisibleCrossingSubStep()
        {
            Pocket pocket = SidePocketFacingNegativeY();
            float speed = 2.5f;

            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            sim.LoadBalls(new[] { new BallState { position = Vector2.zero, velocity = new Vector2(0f, -speed) } });

            // 起点到喉平面的有向距离（起点在进网侧之前 ⇒ 点积为负，取负号翻正）。
            float distanceToPlane = -Vector2.Dot(Vector2.zero - pocket.throatCenter, pocket.throatNormal);
            int crossingSubStep = Mathf.CeilToInt(distanceToPlane / (speed * sim.SubStepDeltaTime));

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
                $"T13c：球正对中袋直下（v={speed}），却从未触发 PocketProximityEvents（冻结路径未被覆盖）。");
            Assert.AreEqual(0, sim.TotalContactCount, "T13c：中袋缺口应无库边接触（否则本用例测的不是'穿出'）。");

            BallState frozenState = sim.Balls[0];
            Assert.AreEqual(0f, frozenState.velocity.x, "T13c：冻结球速度应置 0（x）。");
            Assert.AreEqual(0f, frozenState.velocity.y, "T13c：冻结球速度应置 0（y）。");

            float netSide = Vector2.Dot(frozenState.position - pocket.throatCenter, pocket.throatNormal);
            Assert.Greater(netSide, 0f, $"T13c：冻结位置应在喉平面进网侧，实测 dot={netSide:R}。");

            float overshootBound = speed * sim.SubStepDeltaTime;
            Assert.LessOrEqual(netSide, overshootBound + 1e-6f,
                $"T13c：越界量 {netSide:E3} m 超过理论上界 v·dt_sub = {overshootBound:E3} m" +
                $"（v={speed} m/s，与 T13a **同一条上界**）。");

            // 探针的**存在理由**自证：越界索引必须**不被 3 整除**，否则本探针退化成 T13a 的同款盲带。
            Assert.AreNotEqual(0, crossingSubStep % 3,
                $"T13c：越界子步索引 {crossingSubStep} 可被 3 整除 ⇒ 本探针**失去**对'每 3 子步检查'的敏感性。" +
                " 请换一个 speed 使索引 % 3 != 0（设计取值 v=2.5 ⇒ 101）。");

            // 冻结后必须停止积分：再跑 20 步，位置逐位不变。
            Vector2 frozenPos = frozenState.position;
            for (int s = 0; s < 20; s++)
            {
                sim.Step();
            }

            Assert.AreEqual(frozenPos.x.ToString("R"), sim.Balls[0].position.x.ToString("R"),
                "T13c：冻结后继续 Step()，位置 x 发生变化（未停止积分）。");
            Assert.AreEqual(frozenPos.y.ToString("R"), sim.Balls[0].position.y.ToString("R"),
                "T13c：冻结后继续 Step()，位置 y 发生变化（未停止积分）。");
            Assert.AreEqual(1, sim.PocketProximityEvents, "T13c：冻结为一次事件，不应重复计数。");

            TestContext.WriteLine(
                $"T13c(v={speed}): 越界子步索引={crossingSubStep}（%3={crossingSubStep % 3}，非 0 ⇒ 对'每 3 子步检查'敏感）; " +
                $"冻结于第 {stepsToFreeze} 步; 末位={frozenState.position:R}; 越界量={netSide * 1000f:F4} mm " +
                $"（上界 v·dt_sub={overshootBound * 1000f:F4} mm，占用 {netSide / overshootBound * 100f:F1}%）");
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
        public void SixteenBalls_TraversalOrder_IsBitIdentical()
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
        /// 结构性确定性守卫：热路径源码**代码**（已剔除注释）不得含无序容器 / 重排算子 / 并行（R5-3）。
        /// 读取的是**被测源码文件本身**（非影子副本）。
        ///
        /// ## ⚠ 这是**禁用清单（denylist）**，**不是充分证据**（铁律 12）
        ///
        /// **"没出现这些词" ≠ "确定性已被证明"**：清单只能拦**列出来的**写法。未列出的等价形态
        /// （自写哈希容器、`ICollection`/`IEnumerable` 的意外迭代、排序后重排、`AsParallel()` 等
        /// PLINQ 入口、`Array.Sort` 之外的比较器重排…）**不会被发现**。
        /// ⇒ **不得**把本断言当「遍历顺序只由数组下标决定」的**充分**证明引用 ——
        /// 它证明的只是「热路径没有用**这批已知**危险写法」。
        /// 真正的确定性证据是**逐位一致**那条（
        /// <see cref="SixteenBalls_TraversalOrder_IsBitIdentical"/> 的 16 球双跑逐位比较）；
        /// 本断言是它的**结构性补充**，两者**互补、都不可删**（铁律 4）。
        ///
        /// **脆性说明**：下面的**正证据**改为**空白归一化**后匹配，
        /// 并接受 `i++` / `++i` / `i += 1` 三种**语义完全等价**的写法 ——
        /// 旧版直接 `Contains("for (int i = 0; i < _balls.Length; i++)")`，
        /// 只把 `i++` 改成 `++i` 就会**假失败**。
        /// **选择的是"归一化"这条**（而非"改述为可接受脆性"）：假失败的代价高于这点宽松。
        /// 归一化 = **去掉所有空白**后做**子串**匹配（非正则）；对一条"结构性存在性检查"而言，
        /// 它要抓的是"升序遍历还在不在"，**不是"格式"** ⇒ 这点宽松正是想要的。
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
                "OrderBy", "GroupBy", "ToLookup", "Random",
                // 补齐三项（此前缺失 ⇒ 清单不完备）：
                "ToDictionary<", "ToHashSet<", "Parallel.",
                // 补齐两项（清单此前仍不完备）：
                "AsParallel", ".Order("
            };

            foreach (string token in forbidden)
            {
                Assert.IsFalse(code.Contains(token),
                    $"D-3：热路径**代码**（已剔除注释）出现非确定性/无序结构/并行 `{token}` —— " +
                    "遍历顺序不再只由数组下标决定（违反 R5-3）。");
            }

            // 正证据：状态推进以 for **升序**循环。空白归一化 + 接受三种等价自增写法。
            string compact = WhitespaceStripped(code);
            string[] acceptedLoops =
            {
                "for(inti=0;i<_balls.Length;i++)",
                "for(inti=0;i<_balls.Length;++i)",
                "for(inti=0;i<_balls.Length;i+=1)"
            };

            string matched = null;
            foreach (string pattern in acceptedLoops)
            {
                if (compact.Contains(pattern))
                {
                    matched = pattern;
                    break;
                }
            }

            Assert.IsTrue(matched != null,
                "D-3：未找到升序球遍历（`for (int i = 0; i < _balls.Length; i++)` 及其语义等价写法 "
                + "`++i` / `i += 1`；已做空白归一化），确定性契约无从保证。");

            TestContext.WriteLine($"D-3(结构): 升序遍历命中写法 = `{matched}`（空白归一化后子串匹配）");
        }

        /// <summary>
        /// 去掉全部空白字符（空格 / Tab / CR / LF），用于"结构性存在性"匹配。
        /// 只服务于**正证据**那一处的等价写法容忍；禁用清单的匹配仍用原始文本（避免造出假阴性）。
        /// </summary>
        private static string WhitespaceStripped(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
