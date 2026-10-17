using System.Collections.Generic;
using System.IO;
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
    /// **8 球 AI 三档**（35-C P4）—— 判据 `J1`…`J5` + 真内核成功率。
    ///
    /// ⚠ **为什么求值器必须走真内核**（而不是"离最优角度越近分越高"的解析式）：
    /// 解析式下，"σ 越小 ⇒ 成功率越高"只是把 σ 的大小关系**换个说法重述一遍** ⇒ 判据**恒真**（恒真证据 = 无证据）；
    /// 真内核下，"瞄偏 δ ⇒ 是否进袋"由**接触几何 + 滚动 + 袋口捕获**决定 ⇒ 这是一条**可被证伪的经验命题**。
    ///
    /// ── 判据 ──────────────────────────────────────────────────────────────
    /// · **J1 上界守**：`ViolatesExpertUpperBound(Parameters(Expert)) == false`，且 σ/力度误差的**字面值**成立
    ///   （"谁把 Expert 调神，这条必红"）；同批逐字段比对**三档参数表字面量**。
    /// · **J2 单调性（真内核）**：固定种子下**合法进球杆成功率** `Casual &lt; Club &lt; Expert`（**严格**不等）。
    /// · **J3 种子可复现**：同 `seed` + 同局面 ⇒ 两次 `Plan` **逐位相同**；**不同** seed ⇒ 输出**不得全等**。
    /// · **J4 预算封顶**：`candidatesEvaluated &lt;= candidateBudget`（三档各自）+ 预算字面值 `16/32/64`。
    /// · **J5 无两杆前瞻**：白盒 —— `EightBallAi.cs` 里 `Plan(` 只允许出现 **1 次**（即声明本身；
    ///   本层内部**再规划一次**是"两杆前瞻"在该层的唯一形态）；求值器调用点按**调用括号 + 实参前缀**计数。
    ///
    /// ── 盲带（**明确不判**）────────────────────────────────────────────────
    /// · 真随机（运行期）不判：本层只接受注入 `seed`；
    /// · 单帧摊帧 / 1.5 s 超时收尾（`§4.2`）属宿主（P5）回路，**不在本判据**；
    /// · **借库（库边反射）线路不在 AI 的候选空间内**（候选只由"幽灵球 → 袋口"的**直球**线生成）
    ///   ⇒ 一个"只能靠借库进球"的局面会让三档**同为 0**（并列）——那是**判据设计的坑**，不是 AI 缺陷。
    ///   因此第三个局面取**长台直球**，借库建模若要做，应作为 P4 的独立增量。
    ///
    /// ── 注入负对照（每条都要求恰好打红 J1/J2/J5）─────────────────────────
    /// · `INJ-α`：`σ_Expert → 0.3°` ⇒ **J1 红**；
    /// · `INJ-β`：三档参数相同 ⇒ **J2 红**（并同时打红接线副判据）；
    /// · `INJ-γ`：`seed` 固定成常量 ⇒ **J3 的"不同 seed 不得全等"红**；
    /// · `INJ-δ`：在 `Plan` 内加一次自递归/前瞻规划调用 ⇒ **J5 红**。
    /// </summary>
    public class EightBallAiTests
    {
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";
        private const string AiSourcePath = "Assets/_Project/Scripts/Runtime/Rules/EightBallAi.cs";

        private const int CueBall = 0;
        private const int ObjectBall = 3;                 // 实色 3 号（组别 = 实色）
        private const int ShotsPerScenario = 12;
        private const int MaxRollSteps = 8000;
        private const int CueBallIndexInSim = 0;

        private static readonly AiDifficulty[] Tiers =
        {
            AiDifficulty.Casual, AiDifficulty.Club, AiDifficulty.Expert,
        };

        private static readonly string[] TierNames = { "Casual", "Club", "Expert" };

        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void T143_EightBallAi_ThreeTiers_UpperBound_Determinism_Budget_AndKernelPotRate()
        {
            var failures = new List<string>();

            var table = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(table, "T143：需要 " + TablePath);
            Assert.IsNotNull(tuning, "T143：需要 " + TuningPath);
            var physics = tuning.ToPhysicsParams();
            float minSpeed = tuning.minLaunchSpeed;
            float maxSpeed = tuning.maxLaunchSpeed;

            CheckJ1_UpperBoundAndLiteralTable(failures);
            CheckJ5_NoLookaheadSourceGuard(failures);
            CheckWiring_TierIntensitySeparates(failures);
            CheckJ3_SeedDeterminism(failures);
            CheckJ2_And_J4_RealKernel(failures, table, physics, minSpeed, maxSpeed);

            Assert.IsEmpty(failures, "T143 失败：\n" + string.Join("\n", failures));
        }

        // ── J1：上界守 + 三档参数表字面量 ────────────────────────────────────

        private static void CheckJ1_UpperBoundAndLiteralTable(List<string> failures)
        {
            var expert = EightBallAi.Parameters(AiDifficulty.Expert);
            if (EightBallAi.ViolatesExpertUpperBound(expert))
            {
                failures.Add($"J1 上界守失败：Expert 被调得更准了 —— σ={expert.aimSigmaDeg}°（下限 "
                             + $"{EightBallAi.ExpertAimSigmaFloorDeg}°）、力度误差={expert.powerSigmaFraction * 100f}%"
                             + $"（下限 {ExpertPowerSigmaFloorPercent()}%）。");
            }

            // 上界守必须是**字面值**（不许把下限悄悄改小来"让判据过"）。
            // ⚠ **必须经局部变量比较**：`ExpertAimSigmaFloorDeg` 是**别的程序集的 const**，会被编译期内联
            //   ⇒ 直接与字面量比会被**常量折叠**成恒假，分支整段被丢掉（`CS0162` 就是它的症状）——那是**假绿**。
            float floorSigmaDeg = EightBallAi.ExpertAimSigmaFloorDeg;
            float floorPowerFraction = EightBallAi.ExpertPowerSigmaFloorFraction;
            AiDifficulty defaultTier = EightBallAi.DefaultDifficulty;

            if (floorSigmaDeg != 0.5f)
            {
                failures.Add($"J1 字面值失败：ExpertAimSigmaFloorDeg 期望 0.5，实测 {floorSigmaDeg}。");
            }

            if (floorPowerFraction != 0.05f)
            {
                failures.Add($"J1 字面值失败：ExpertPowerSigmaFloorFraction 期望 0.05，实测 {floorPowerFraction}。");
            }

            if (defaultTier != AiDifficulty.Club)
            {
                failures.Add($"J1 失败：默认档期望 Club，实测 {defaultTier}。");
            }

            // 三档参数表逐字段比对（§4.3 字面量）
            CheckParams(failures, AiDifficulty.Casual, 6.0f, 2.0f, 3, 4.0f, 0.25f, 5, 16);
            CheckParams(failures, AiDifficulty.Club, 4.0f, 1.0f, 4, 2.2f, 0.14f, 3, 32);
            CheckParams(failures, AiDifficulty.Expert, 2.0f, 0.5f, 6, 0.8f, 0.06f, 1, 64);
        }

        private static float ExpertPowerSigmaFloorPercent() => EightBallAi.ExpertPowerSigmaFloorFraction * 100f;

        private static void CheckParams(List<string> failures, AiDifficulty difficulty, float coarse, float fine,
            int powerLevels, float sigma, float powerSigma, int topK, int budget)
        {
            var p = EightBallAi.Parameters(difficulty);
            var bad = new List<string>();
            if (p.difficulty != difficulty) bad.Add($"difficulty={p.difficulty}");
            if (p.aimStepCoarseDeg != coarse) bad.Add($"coarse={p.aimStepCoarseDeg}≠{coarse}");
            if (p.aimStepFineDeg != fine) bad.Add($"fine={p.aimStepFineDeg}≠{fine}");
            if (p.powerLevelCount != powerLevels) bad.Add($"levels={p.powerLevelCount}≠{powerLevels}");
            if (p.aimSigmaDeg != sigma) bad.Add($"σ={p.aimSigmaDeg}≠{sigma}");
            if (p.powerSigmaFraction != powerSigma) bad.Add($"力σ={p.powerSigmaFraction}≠{powerSigma}");
            if (p.topKPick != topK) bad.Add($"topK={p.topKPick}≠{topK}");
            if (p.candidateBudget != budget) bad.Add($"budget={p.candidateBudget}≠{budget}");
            if (p.weakPositionScoring != (difficulty == AiDifficulty.Expert))
            {
                bad.Add($"weakPositionScoring={p.weakPositionScoring}（仅 Expert 应为 true）");
            }

            if (bad.Count > 0)
            {
                failures.Add($"J1 参数表字面量失败 [{difficulty}]：{string.Join("，", bad)}。");
            }
        }

        // ── J5：白盒守卫（无两杆前瞻）────────────────────────────────────────

        private static void CheckJ5_NoLookaheadSourceGuard(List<string> failures)
        {
            string raw = ReadSource(AiSourcePath);
            string code = StripCommentLines(raw);

            // **机械化对策**：判"方法名 + 调用括号"，且**限定在本文件**（宿主持有调用点不算违规）。
            int planOccurrences = CountOccurrences(code, "Plan(");
            if (planOccurrences != 1)
            {
                failures.Add($"J5 失败：`EightBallAi.cs` 里 `Plan(` 出现 {planOccurrences} 次（期望 1 = 仅声明本身）"
                             + " ⇒ 本层内部出现了**第二次规划**（两杆前瞻在该层的唯一形态）。");
            }

            // 求值器调用点：按"调用括号 + 实参前缀"计数（粗筛 1 + 精扫 1）。
            int evalSites = CountOccurrences(code, "evaluator(context, ");
            if (evalSites != 2)
            {
                failures.Add($"J5 失败：求值器调用点 {evalSites} 个（期望 2 = 粗筛 1 + 精扫 1）"
                             + " ⇒ 候选评估的结构被改动（可能引入「评估对手回球」这类前瞻）。");
            }

            if (!raw.Contains("全档不做两杆前瞻"))
            {
                failures.Add("J5 失败：`EightBallAi.cs` 的类注释里没有显式声明「全档不做两杆前瞻」。");
            }
        }

        // ── 接线副判据（解析求值器；**只守接线，不守表现**）───────────────────

        /// <summary>
        /// ⚠ 本条**只证明三档的扰动强度接线不同**（喂同一组种子时，输出偏离最优的幅度按档位递减）。
        /// **它不能替代 J2**：解析求值器把"σ 的大小关系"重述了一遍 ⇒ 对"AI 打得更好"**零信息**。
        /// </summary>
        private static void CheckWiring_TierIntensitySeparates(List<string> failures)
        {
            const int probeSeeds = 64;
            var meanDeviation = new float[Tiers.Length];

            // 单袋口 + 单目标球 ⇒ 最优方向唯一（= 幽灵球基准方向），偏离量纯粹来自"选杆 + 扰动"。
            var context = MakeSyntheticContext();
            float bestAim = EightBallAi.AimDegreesForPocket(context.cueBallPosition, context.ballPositions[ObjectBall],
                context.pocketCenters[0]);

            for (int tier = 0; tier < Tiers.Length; tier++)
            {
                float sum = 0f;
                for (int s = 0; s < probeSeeds; s++)
                {
                    var intent = EightBallAi.Plan(context, Tiers[tier], 4000 + s, AnalyticEvaluator);
                    if (!intent.planned)
                    {
                        failures.Add($"接线副判据失败：{TierNames[tier]} 在合成局面上未产出意图。");
                        return;
                    }

                    sum += Mathf.Abs(Mathf.DeltaAngle(intent.aimDegrees, bestAim));
                }

                meanDeviation[tier] = sum / probeSeeds;
            }

            if (!(meanDeviation[0] > meanDeviation[1] && meanDeviation[1] > meanDeviation[2]))
            {
                failures.Add($"接线副判据失败（**只守接线**）：三档平均瞄准偏离未按档位递减 —— "
                             + $"Casual {meanDeviation[0]:F3}° / Club {meanDeviation[1]:F3}° / Expert {meanDeviation[2]:F3}°。");
            }

            TestContext.WriteLine($"[接线副判据] 平均瞄准偏离 Casual {meanDeviation[0]:F3}° / Club "
                                  + $"{meanDeviation[1]:F3}° / Expert {meanDeviation[2]:F3}°（{probeSeeds} 个种子）");
        }

        private static AiShotContext MakeSyntheticContext()
        {
            var positions = new Vector2[16];
            var inPlay = new bool[16];
            positions[CueBall] = new Vector2(-0.30f, 0.00f);
            positions[ObjectBall] = new Vector2(0.05f, 0.00f);
            inPlay[CueBall] = true;
            inPlay[ObjectBall] = true;

            return new AiShotContext
            {
                cueBallPosition = positions[CueBall],
                ballPositions = positions,
                ballInPlay = inPlay,
                groupState = GroupState.Solid,
                pocketCenters = new[] { new Vector2(1.20f, 0.60f) },
            };
        }

        /// <summary>解析求值器：`100 − 与基准方向的夹角²`（**仅接线检查用**）。</summary>
        private static float AnalyticEvaluator(AiShotContext context, float aimDegrees, float power, int targetBall,
            int pocketIndex)
        {
            float baseAim = EightBallAi.AimDegreesForPocket(context.cueBallPosition, context.ballPositions[targetBall],
                context.pocketCenters[pocketIndex]);
            if (float.IsNaN(baseAim))
            {
                return -1000f;
            }

            float delta = Mathf.DeltaAngle(aimDegrees, baseAim);
            return 100f - delta * delta;
        }

        // ── J3：种子可复现 ───────────────────────────────────────────────────

        private static void CheckJ3_SeedDeterminism(List<string> failures)
        {
            var context = MakeSyntheticContext();

            // (a) 同 seed ⇒ 逐位相同。
            foreach (var tier in Tiers)
            {
                var a = EightBallAi.Plan(context, tier, 20261, AnalyticEvaluator);
                var b = EightBallAi.Plan(context, tier, 20261, AnalyticEvaluator);
                if (a.aimDegrees != b.aimDegrees || a.power != b.power || a.targetBall != b.targetBall
                    || a.pocketIndex != b.pocketIndex || a.score != b.score
                    || a.candidatesEvaluated != b.candidatesEvaluated)
                {
                    failures.Add($"J3 失败：同一 seed（20261）下 {tier} 的两次 `Plan` 输出不一致（"
                                 + $"aim {a.aimDegrees} vs {b.aimDegrees}，power {a.power} vs {b.power}）。");
                }
            }

            // (b) 不同 seed ⇒ 输出不得全等（防"假随机"：把 seed 丢掉、用常量）。
            var aims = new List<float>();
            for (int s = 0; s < 5; s++)
            {
                aims.Add(EightBallAi.Plan(context, AiDifficulty.Casual, 900 + s, AnalyticEvaluator).aimDegrees);
            }

            bool anyDifferent = false;
            for (int i = 1; i < aims.Count && !anyDifferent; i++)
            {
                anyDifferent = aims[i] != aims[0];
            }

            if (!anyDifferent)
            {
                failures.Add("J3 失败：5 个不同 seed 下瞄准角**完全相同** ⇒ `seed` 没有真正参与随机（假随机）。");
            }
        }

        // ── J2（真内核成功率）+ J4（预算封顶）────────────────────────────────

        private static void CheckJ2_And_J4_RealKernel(List<string> failures, TableConfig table, PhysicsParams physics,
            float minSpeed, float maxSpeed)
        {
            var scenarios = new List<Scene>
            {
                BuildScene(table, "近直球", objectToPocket: 0.35f, cueDistance: 0.18f, cutDegrees: 0f),
                BuildScene(table, "带角度", objectToPocket: 0.50f, cueDistance: 0.20f, cutDegrees: 25f),
                BuildScene(table, "长台", objectToPocket: 0.70f, cueDistance: 0.22f, cutDegrees: 0f),
            };

            var potted = new int[Tiers.Length];
            var shots = new int[Tiers.Length];
            var rateLines = new List<string>();
            var perScenario = new string[Tiers.Length, scenarios.Count];

            for (int tier = 0; tier < Tiers.Length; tier++)
            {
                var parameters = EightBallAi.Parameters(Tiers[tier]);
                for (int si = 0; si < scenarios.Count; si++)
                {
                    var scene = scenarios[si];
                    var evaluator = KernelEvaluator(table, physics, scene, minSpeed, maxSpeed);
                    int potCount = 0;

                    for (int shot = 0; shot < ShotsPerScenario; shot++)
                    {
                        int seed = 7000 + shot * 31;
                        var intent = EightBallAi.Plan(scene.context, Tiers[tier], seed, evaluator);
                        if (!intent.planned)
                        {
                            failures.Add($"J2 失败：{TierNames[tier]} / {scene.name} 第 {shot} 杆未产出意图。");
                            continue;
                        }

                        // J4：预算封顶（三档各自）
                        if (intent.candidatesEvaluated > parameters.candidateBudget)
                        {
                            failures.Add($"J4 失败：{TierNames[tier]} 候选评估 {intent.candidatesEvaluated} 次 "
                                         + $"超过预算 {parameters.candidateBudget}。");
                        }

                        if (intent.candidatesEvaluated <= 0)
                        {
                            failures.Add($"J4 失败：{TierNames[tier]} 候选评估 0 次 ⇒ 求值器根本没被调用。");
                        }

                        shots[tier]++;
                        if (ExecuteAndPots(table, physics, scene, minSpeed, maxSpeed, intent))
                        {
                            potCount++;
                            potted[tier]++;
                        }
                    }

                    perScenario[tier, si] = $"{potCount}/{ShotsPerScenario}";
                }

                float rate = shots[tier] > 0 ? potted[tier] / (float)shots[tier] : 0f;
                rateLines.Add($"{TierNames[tier]} {potted[tier]}/{shots[tier]} = {rate:P1}"
                              + $"（{scenarios[0].name} {perScenario[tier, 0]}，{scenarios[1].name} "
                              + $"{perScenario[tier, 1]}，{scenarios[2].name} {perScenario[tier, 2]}）");
            }

            TestContext.WriteLine("[J2 真内核] 合法进球杆成功率：" + string.Join(" ｜ ", rateLines));

            // J2：**严格**递增（并列即红 —— 三档不许等价）。
            if (!(potted[0] < potted[1] && potted[1] < potted[2]))
            {
                failures.Add("J2 失败：真内核成功率未严格递增 —— " + string.Join(" ｜ ", rateLines));
            }
        }

        // ── 真内核：候选求值器 ───────────────────────────────────────────────

        private static AiShotCandidateEvaluator KernelEvaluator(TableConfig table, PhysicsParams physics, Scene scene,
            float minSpeed, float maxSpeed)
        {
            return (AiShotContext context, float aimDegrees, float power, int targetBall, int pocketIndex) =>
            {
                var sim = BuildSim(table, physics, scene);
                sim.PlaceBall(CueBallIndexInSim, scene.cue, VelocityFor(aimDegrees, power, minSpeed, maxSpeed));
                Roll(sim, MaxRollSteps);

                bool cueGone = sim.CapturedPocketIndex(CueBallIndexInSim) >= 0 || !sim.IsBallInPlay(CueBallIndexInSim);
                int targetIndex = scene.simIndexOf[targetBall];
                int captured = targetIndex >= 0 ? sim.CapturedPocketIndex(targetIndex) : -1;

                float score = captured >= 0 ? 100f - 60f * Centrality(table, sim, targetIndex, captured) : -20f;
                if (cueGone)
                {
                    score -= 300f;
                }

                return score;
            };
        }

        /// <summary>进袋的"居中程度" `∈[0,1]`（0 = 正中最准）—— 由 `ShotContactEvents` 的越喉位置算出。</summary>
        private static float Centrality(TableConfig table, PoolSimulator sim, int simIndex, int pocketIndex)
        {
            var pocket = table.GetPocket(pocketIndex);
            if (pocket == null || pocket.throatWidth <= 1e-6f || sim.ShotContactEvents == null)
            {
                return 0.5f;
            }

            for (int i = 0; i < sim.ShotContactEvents.Count; i++)
            {
                ContactEvent ev = sim.ShotContactEvents[i];
                if (ev.kind == ContactKind.Pocket && ev.ballA == simIndex)
                {
                    return Mathf.Clamp01((ev.position - pocket.throatCenter).magnitude / pocket.throatWidth);
                }
            }

            return 0.5f;
        }

        // ── 局面构造 ────────────────────────────────────────────────────────

        private sealed class Scene
        {
            public string name;
            public Vector2 cue;
            public Vector2 target;
            public int aimPocket;
            public AiShotContext context;
            public int[] simIndexOf;
        }

        private static Scene BuildScene(TableConfig table, string name, float objectToPocket, float cueDistance,
            float cutDegrees)
        {
            int pocketIndex = CornerPocketIndex(table);
            Vector2 pocket = table.GetPocket(pocketIndex).throatCenter;

            // 目标球放在"袋口 → 台心"方向上、距袋口 objectToPocket 处 ⇒ 目标球行程确定。
            Vector2 dir = (pocket - Vector2.zero).normalized;
            Vector2 target = pocket - dir * objectToPocket;

            // 幽灵球 = 目标球背向袋口一个直径；白球沿 approach 方向退 cueDistance ⇒ 白球行程确定（切角 = cutDegrees）。
            Vector2 ghost = target - dir * (2f * PoolConstants.BallRadius);
            Vector2 approach = Rotate(dir, cutDegrees);
            Vector2 cue = ghost - approach * cueDistance;

            var positions = new Vector2[16];
            var inPlay = new bool[16];
            positions[CueBall] = cue;
            positions[ObjectBall] = target;
            inPlay[CueBall] = true;
            inPlay[ObjectBall] = true;

            var simIndexOf = new int[16];
            for (int i = 0; i < simIndexOf.Length; i++)
            {
                simIndexOf[i] = -1;
            }

            simIndexOf[CueBall] = 0;
            simIndexOf[ObjectBall] = 1;

            return new Scene
            {
                name = name,
                cue = cue,
                target = target,
                aimPocket = pocketIndex,
                simIndexOf = simIndexOf,
                context = new AiShotContext
                {
                    cueBallPosition = cue,
                    ballPositions = positions,
                    ballInPlay = inPlay,
                    groupState = GroupState.Solid,
                    pocketCenters = PocketCenters(table),
                },
            };
        }

        private static Vector2[] PocketCenters(TableConfig table)
        {
            var centers = new Vector2[table.PocketCount];
            for (int i = 0; i < centers.Length; i++)
            {
                centers[i] = table.GetPocket(i).throatCenter;
            }

            return centers;
        }

        /// <summary>取一只**角袋**（优先 +x/+y 象限；与具体资产编号解耦）。</summary>
        private static int CornerPocketIndex(TableConfig table)
        {
            int fallback = 0;
            bool hasFallback = false;
            for (int i = 0; i < table.PocketCount; i++)
            {
                var pocket = table.GetPocket(i);
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

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ── 内核驱动 ────────────────────────────────────────────────────────

        private static PoolSimulator BuildSim(TableConfig table, PhysicsParams physics, Scene scene)
        {
            var sim = new PoolSimulator(table, physics);
            sim.LoadBalls(new[]
            {
                new BallState { position = scene.cue, velocity = Vector2.zero },
                new BallState { position = scene.target, velocity = Vector2.zero },
            });
            return sim;
        }

        private static Vector2 VelocityFor(float aimDegrees, float power, float minSpeed, float maxSpeed)
        {
            float rad = aimDegrees * Mathf.Deg2Rad;
            float speed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.Clamp01(power));
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * speed;
        }

        private static void Roll(PoolSimulator sim, int maxSteps)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                sim.Step();
                if (sim.TableAtRest)
                {
                    return;
                }
            }
        }

        /// <summary>执行意图（**不加任何额外扰动**）并判"是否合法进球"（本方组球进袋、且白球未落袋）。</summary>
        private static bool ExecuteAndPots(TableConfig table, PhysicsParams physics, Scene scene, float minSpeed,
            float maxSpeed, in AiShotIntent intent)
        {
            var sim = BuildSim(table, physics, scene);
            sim.PlaceBall(CueBallIndexInSim, scene.cue, VelocityFor(intent.aimDegrees, intent.power, minSpeed, maxSpeed));
            Roll(sim, MaxRollSteps);

            if (sim.CapturedPocketIndex(CueBallIndexInSim) >= 0 || !sim.IsBallInPlay(CueBallIndexInSim))
            {
                return false;   // 白球落袋 / 离台 ⇒ 不算合法进球杆
            }

            var targets = new List<int>();
            EightBallAi.CollectLegalTargetBalls(scene.context, targets);
            for (int i = 0; i < targets.Count; i++)
            {
                int index = scene.simIndexOf[targets[i]];
                if (index >= 0 && sim.CapturedPocketIndex(index) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // ── 源码读取工具 ────────────────────────────────────────────────────

        private static string ReadSource(string relativePath)
        {
            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            return File.ReadAllText(Path.Combine(root, relativePath), Encoding.UTF8);
        }

        private static string StripCommentLines(string source)
        {
            var sb = new StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                if (line.TrimStart().StartsWith("//", System.StringComparison.Ordinal))
                {
                    continue;
                }

                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

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
    }
}
