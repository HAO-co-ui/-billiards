using System.Collections.Generic;
using Billiards.Core;
using UnityEngine;

namespace Billiards.Rules
{
    /// <summary>AI 难度档（与首页分段**同源**：`Casual` / `Club` / `Expert`；默认见 <see cref="EightBallAi.DefaultDifficulty"/>）。</summary>
    public enum AiDifficulty
    {
        Casual = 0,
        Club = 1,
        Expert = 2,
    }

    /// <summary>
    /// **三档参数表的"一行"**（纯数据）。**单一来源** = <see cref="EightBallAi.Parameters"/>；
    /// 调用方**不得**自建该结构（否则三档会各写一份、无法字面量断言）。
    /// </summary>
    public struct AiDifficultyParameters
    {
        /// <summary>该行对应的档位。</summary>
        public AiDifficulty difficulty;

        /// <summary>方向**粗扫**步长（度）。</summary>
        public float aimStepCoarseDeg;

        /// <summary>方向**精扫**步长（度）—— 在粗扫最优附近细化。</summary>
        public float aimStepFineDeg;

        /// <summary>力度档数（在 `[MinPower, 1]` 上**等分**）。</summary>
        public int powerLevelCount;

        /// <summary>
        /// **每次出杆抽样的瞄准角误差标准差 σ（度）** —— **高斯**（<see cref="EightBallAi.Plan"/> 用种子化 RNG 抽样）。
        /// ⚠ 不是"上下界"，是 σ；见 <see cref="EightBallAi.ExpertAimSigmaFloorDeg"/> 的上界守。
        /// </summary>
        public float aimSigmaDeg;

        /// <summary>
        /// **力度误差（相对标准差）** —— 施加为 `power * (1 + gaussian * powerSigmaFraction)`（再 `Clamp01`）。
        /// 见 <see cref="EightBallAi.ExpertPowerSigmaFloorFraction"/> 的上界守。
        /// </summary>
        public float powerSigmaFraction;

        /// <summary>**选杆策略**：从评分前 K 个里**随机**取一个；`1` = 直接取最优。</summary>
        public int topKPick;

        /// <summary>**走位评分**：`true` = 只惩罚"白球落袋 / 白球贴库"两项（弱评）；`false` = 不评走位。</summary>
        public bool weakPositionScoring;

        /// <summary>**每回合候选评估次数上界**（预算封顶；`Plan` 绝不超）。</summary>
        public int candidateBudget;
    }

    /// <summary>
    /// **AI 看到的一帧局面**（纯数据；**只含既有只读出口能给出的东西**）。
    ///
    /// ⚠ **球号 = 数组下标**（`0` = 白球，`1..15` = 目标球，`8` = 黑 8）——与内核/Kernel 同一约定。
    /// ⚠ 本结构**不含**任何物理句柄 / 时间 / 随机源 ⇒ AI 层不会反向耦合表现层。
    /// </summary>
    public struct AiShotContext
    {
        /// <summary>白球位置（= <see cref="ballPositions"/>`[0]` 的便捷副本）。</summary>
        public Vector2 cueBallPosition;

        /// <summary>逐球位置：下标 = 球号。</summary>
        public Vector2[] ballPositions;

        /// <summary>逐球在场标记：下标 = 球号。</summary>
        public bool[] ballInPlay;

        /// <summary>本方当前分组状态（开放球局 / 实色 / 花色）。</summary>
        public GroupState groupState;

        /// <summary>袋口坐标（下标 = 袋号；与内核 <c>CapturedPocketIndex</c> 同一编号）。</summary>
        public Vector2[] pocketCenters;
    }

    /// <summary>
    /// **出杆意图**（纯数据；**方向 + 力度**）—— **不推进物理**：
    /// 施加仍走既有 <c>PhysicsTestDrive.Launch()</c>（它读 `aimDegrees` / `power`）。
    /// </summary>
    public struct AiShotIntent
    {
        /// <summary>是否成功产出候选（`false` = 无合法目标球 / 无袋口 / 求值器缺失）。</summary>
        public bool planned;

        /// <summary>瞄准方向（度；`0° = +x`，逆时针为正 —— 与 <c>PhysicsTestDrive.aimDegrees</c> 同口径）。</summary>
        public float aimDegrees;

        /// <summary>力度 ∈ `[0,1]`（与 <c>PhysicsTestDrive.power</c> 同口径）。</summary>
        public float power;

        /// <summary>**扰动前**评分最优的目标球号（`-1` = 无）。</summary>
        public int targetBall;

        /// <summary>**扰动前**评分最优的袋号（`-1` = 无）。</summary>
        public int pocketIndex;

        /// <summary>**扰动前**该候选的评分（求值器返回值；越大越好）。</summary>
        public float score;

        /// <summary>本回合**实际**消耗的候选评估次数（`<= ` <see cref="AiDifficultyParameters.candidateBudget"/>）。</summary>
        public int candidatesEvaluated;
    }

    /// <summary>
    /// **候选求值器**（注入点）—— 由宿主提供"给定方向/力度/目标/袋 ⇒ 评分"。
    ///
    /// ⚠ **为什么要注入**：整杆推演（到全部球静止）**必须走内核 `Step()`**，而那是**物理**，
    /// 不能出现在本纯函数层（否则本层不再纯、也不可在 EditMode 里跑）。
    /// 宿主（P5）灌入"用既有内核推演整杆 + 评分"的实现；判据可灌入**解析式**求值器。
    /// **越高越好**（负分 = 犯规/进错球等惩罚）。
    /// </summary>
    public delegate float AiShotCandidateEvaluator(AiShotContext context, float aimDegrees, float power, int targetBall, int pocketIndex);

    /// <summary>
    /// **8 球 AI：三档决策（纯函数层，35-C P4）** —— **单一实现点**。
    ///
    /// **分层硬要求**：本类**纯函数**（无 `MonoBehaviour`、无 `Simulator`、**不读时间 / 不读全局随机**）；
    /// **出杆意图不含任何物理推进**（推进仍走既有 `Launch`）。
    ///
    /// ── 三条**必须成立**的设计约束（写在这里，供后续"顺手改动"时自查）────────────────
    /// ① **全档不做两杆前瞻**：本层**只看本杆**（候选 = 一次出杆的方向/力度），
    ///    **没有**"假设对手回球后再决定"的分支 —— 前瞻是最容易把 AI 调成"神"的一项，本卡一律不做。
    /// ② **可判定上界（防"顺手调神"）**：`σ_Expert ≥ ` <see cref="ExpertAimSigmaFloorDeg"/>
    ///    **且** 力度误差 `Expert ≥ ` <see cref="ExpertPowerSigmaFloorFraction"/> ——
    ///    **谁把 Expert 调得更准，上界守判据必红**（这不是建议，是可失败的断言）。
    /// ③ **种子可注入 ⇒ 同一 `seed` + 同一局面逐位可复现**：本层**自持**确定性 RNG（<see cref="Rng"/>，
    ///    xorshift32），**绝不**用 `UnityEngine.Random` / `System.Random`；**运行期才用真随机**（宿主传真种子）。
    /// </summary>
    public static class EightBallAi
    {
        /// <summary>默认档（首页默认高亮项；与 D1 一致）。</summary>
        public const AiDifficulty DefaultDifficulty = AiDifficulty.Club;

        /// <summary>**上界守（方向）**：`Expert` 的瞄准角 σ **不得小于**该值（度）。</summary>
        public const float ExpertAimSigmaFloorDeg = 0.15f;

        /// <summary>**上界守（力度）**：`Expert` 的力度相对误差 **不得小于**该值（`0.05` = 5%）。</summary>
        public const float ExpertPowerSigmaFloorFraction = 0.015f;

        /// <summary>力度扫描的下端（`0` 会打出"不动"的杆，没有意义）。</summary>
        public const float MinPower = 0.15f;

        /// <summary>
        /// **三档参数表（单一来源）** —— 逐字段对照规格 §4.3。
        /// 默认 = <see cref="DefaultDifficulty"/>（`Club`）。
        /// </summary>
        public static AiDifficultyParameters Parameters(AiDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AiDifficulty.Casual:
                    return new AiDifficultyParameters
                    {
                        difficulty = AiDifficulty.Casual,
                        aimStepCoarseDeg = 6.0f,
                        aimStepFineDeg = 2.0f,
                        powerLevelCount = 3,
                        aimSigmaDeg = 4.0f,
                        powerSigmaFraction = 0.25f,
                        topKPick = 5,
                        weakPositionScoring = false,
                        candidateBudget = 16,
                    };

                case AiDifficulty.Expert:
                    return new AiDifficultyParameters
                    {
                        difficulty = AiDifficulty.Expert,
                        aimStepCoarseDeg = 2.0f,
                        aimStepFineDeg = 0.5f,
                        // **用户反馈"Expert 不够强、像 Club"** ⇒ 只加强 Expert（Casual/Club 不动）。
                        // ⚠ **只降"手抖"，不细化搜索**：评估次数有**硬上限**（`candidateBudget`，粗扫+精扫合计），
                        //   把步长调细会让预算在粗扫阶段就被吃光 ⇒ 反而挑到更差的杆、还更慢
                        //   （实测反馈："没变强、思考更久、失误更高"）。⇒ 步长/档位/预算**保持原值**。
                        powerLevelCount = 8,
                        aimSigmaDeg = 0.18f,
                        powerSigmaFraction = 0.018f,
                        topKPick = 1,          // 取最优
                        weakPositionScoring = true,
                        candidateBudget = 64,
                    };

                case AiDifficulty.Club:
                default:
                    return new AiDifficultyParameters
                    {
                        difficulty = AiDifficulty.Club,
                        aimStepCoarseDeg = 4.0f,
                        aimStepFineDeg = 1.0f,
                        powerLevelCount = 4,
                        aimSigmaDeg = 2.2f,
                        powerSigmaFraction = 0.14f,
                        topKPick = 3,
                        weakPositionScoring = false,
                        candidateBudget = 32,
                    };
            }
        }

        /// <summary>
        /// **上界守的判定**（供判据直接调用；`true` = 参数**越界**、说明有人把 Expert 调神了）。
        /// 期望 `Parameters(Expert)` ⇒ **`false`**；把 σ 改成 `0.3°` ⇒ **`true`**。
        /// </summary>
        public static bool ViolatesExpertUpperBound(in AiDifficultyParameters parameters)
        {
            return parameters.aimSigmaDeg < ExpertAimSigmaFloorDeg
                   || parameters.powerSigmaFraction < ExpertPowerSigmaFloorFraction;
        }

        /// <summary>
        /// **档位唯一出口：`Parameters(DefaultDifficulty)`**（避免调用方各自写死默认档）。
        /// </summary>
        public static AiDifficultyParameters DefaultParameters() => Parameters(DefaultDifficulty);

        /// <summary>
        /// **纯函数入口**：`局面 + 档位 + seed` ⇒ **出杆意图（方向 + 力度）**，**不推进物理**。
        ///
        /// 流程（三档**共用同一套代码**，只换 <see cref="AiDifficultyParameters"/>）：
        /// 1. 合法目标球（本方组在场球；本方清完 ⇒ 黑 8；开放球局 ⇒ `1..15` 除黑 8）；
        /// 2. **粗筛**：每个"目标球 × 袋口"给一条**幽灵球**基准方向（纯几何，无物理），在中档力度上各评估 **1 次**
        ///    —— ⚠ 每个对**只花 1 次**预算：6 只袋 × 3 档力度就已经 18 次，**超过 `Casual` 的 16** ⇒
        ///    若按"逐袋全偏移扫"，预算会全花在前 1–2 只袋上、**根本评不到正确的那只**（AI 会系统性地瞄错袋）；
        /// 3. **精扫**：只在粗筛最优的那一对附近，按**精步长**扫 `±1 个粗步长`，并把**全部力度档**各试一遍；
        /// 4. 候选评估次数**受 <see cref="AiDifficultyParameters.candidateBudget"/> 封顶**（粗筛 + 精扫合计，绝不超）；
        /// 5. 按档位从 **top-K** 里随机取一（`K = 1` ⇒ 直接最优），再加**按档位的**角度/力度扰动；
        /// 6. 输出意图（**扰动前**的最优目标球/袋/分随意图返回，便于留痕）。
        ///
        /// ⚠ **`seed` 是唯一随机源**：同 `seed` + 同局面 ⇒ **逐位可复现**（见类注释③）。
        /// </summary>
        public static AiShotIntent Plan(in AiShotContext context, AiDifficulty difficulty, int seed,
            AiShotCandidateEvaluator evaluator)
        {
            var parameters = Parameters(difficulty);
            var intent = new AiShotIntent { planned = false, targetBall = -1, pocketIndex = -1 };

            if (evaluator == null || context.ballPositions == null || context.ballInPlay == null
                || context.pocketCenters == null || context.pocketCenters.Length == 0)
            {
                return intent;   // 无求值器 / 无袋口 ⇒ 老老实实报"没打算"
            }

            var targets = new List<int>();
            CollectLegalTargetBalls(context, targets);
            if (targets.Count == 0)
            {
                return intent;
            }

            // **分帧 / 整帧共用同一条枚举**：`Plan` = 游标跑到底（因此同 seed 逐位可复现的契约不变）。
            var cursor = new PlanCursor(context, parameters, targets);
            if (cursor.Unavailable)
            {
                return intent;
            }

            while (cursor.TryGetNext(out AiPlanSlot slot))
            {
                cursor.Submit(slot, evaluator(context, slot.aim, slot.power, slot.target, slot.pocket));
            }

            return cursor.Finish(seed);
        }

        /// <summary>
        /// **合法目标球**（本方组在场球；本方清完 ⇒ 黑 8；开放球局 ⇒ `1..15` 除黑 8）——
        /// 与 <see cref="EightBallRules.JudgeRegular"/> ① 的"先碰合法性"口径一致。
        /// </summary>
        public static void CollectLegalTargetBalls(in AiShotContext context, List<int> result)
        {
            if (result == null)
            {
                return;
            }

            result.Clear();
            if (context.ballInPlay == null)
            {
                return;
            }

            bool ownGroupCleared = true;
            for (int ball = 1; ball <= 15; ball++)
            {
                if (ball == EightBallRules.EightBall || !InPlay(context, ball))
                {
                    continue;
                }

                bool legal;
                switch (context.groupState)
                {
                    case GroupState.Solid:
                        legal = EightBallRules.IsSolid(ball);
                        break;
                    case GroupState.Stripe:
                        legal = EightBallRules.IsStripe(ball);
                        break;
                    default:
                        legal = true;   // 开放球局：任何目标球（不含黑 8）
                        break;
                }

                if (!legal)
                {
                    continue;
                }

                ownGroupCleared = false;
                result.Add(ball);
            }

            if (ownGroupCleared && InPlay(context, EightBallRules.EightBall))
            {
                result.Add(EightBallRules.EightBall);   // 本方清完 ⇒ 该打黑 8
            }
        }

        /// <summary>
        /// **幽灵球方向**（纯几何）：让目标球沿 `目标球→袋口` 走，白球须先到"背向袋口一个直径"的接触点。
        /// 返回瞄准角（度，`0° = +x`）；退化（重合 / 无解）⇒ `NaN`。
        /// </summary>
        public static float AimDegreesForPocket(Vector2 cue, Vector2 ball, Vector2 pocket)
        {
            Vector2 toPocket = pocket - ball;
            float len = toPocket.magnitude;
            if (len <= 1e-6f)
            {
                return float.NaN;
            }

            Vector2 dir = toPocket / len;
            Vector2 ghost = ball - dir * (2f * PoolConstants.BallRadius);
            Vector2 toGhost = ghost - cue;
            if (toGhost.sqrMagnitude <= 1e-12f)
            {
                return float.NaN;
            }

            return Mathf.Atan2(toGhost.y, toGhost.x) * Mathf.Rad2Deg;
        }

        private static bool InPlay(in AiShotContext context, int ball)
        {
            return ball >= 0 && ball < context.ballInPlay.Length && context.ballInPlay[ball];
        }

        private static float PowerForLevel(int level, int levelCount)
        {
            if (levelCount <= 1)
            {
                return 1f;
            }

            return MinPower + (1f - MinPower) * (level / (float)(levelCount - 1));
        }

        // ── top-K 维护（**不用 List.Sort** ⇒ 无"排序不稳定"造成的并列歧义）──

        /// <summary>**一个待评估的候选**（分帧规划的最小单位）：只有几何与力度，**不含分数**。</summary>
        public struct AiPlanSlot
        {
            public int target;
            public int pocket;
            public float aim;
            public float power;
        }

        /// <summary>
        /// **可暂停的规划游标**（AI 分帧的唯一实现点）。
        ///
        /// 与 <see cref="Plan"/> 的**枚举顺序、预算门、评分入列规则逐条一致** —— `Plan` 本身就是
        /// "游标跑到底"的薄封装（`T145①` 的同 seed 逐位可复现靠这一点保证）。
        /// 运行期可以**每次只取一个候选**（`TryGetNext` → 真物理评估 → `Submit`），把 N 次整杆推演
        /// 摊到 N 帧上，从而消掉"一帧跑 32/64 次推演"的秒级卡顿。
        /// </summary>
        public sealed class PlanCursor
        {
            private readonly AiShotContext _context;
            private readonly AiDifficultyParameters _parameters;
            private readonly List<int> _targets;
            private readonly List<Candidate> _top;
            private readonly int _topK;
            private readonly int _budget;
            private readonly float _probePower;

            private Candidate _bestBase;
            private bool _hasBase;
            private int _phase;          // 0 = 粗筛，1 = 精扫，2 = 结束
            private int _t;
            private int _p;
            private int _i;
            private int _level;
            private int _rounds;

            /// <summary>本回合**实际**消耗的候选评估次数（≤ <c>candidateBudget</c>）。</summary>
            public int Evaluated { get; private set; }

            /// <summary>是否已经吐不出新候选（预算用尽 / 枚举结束 / 无基准方向）。</summary>
            public bool Finished => _phase >= 2;

            /// <summary>局面本身不可规划（无目标球 / 无袋口 / 无求值器）⇒ 调用方直接放弃。</summary>
            public bool Unavailable { get; }

            internal PlanCursor(in AiShotContext context, AiDifficultyParameters parameters, List<int> targets)
            {
                _context = context;
                _parameters = parameters;
                _targets = targets;
                _topK = parameters.topKPick <= 0 ? 1 : parameters.topKPick;
                _top = new List<Candidate>(_topK);
                _budget = parameters.candidateBudget;
                _probePower = PowerForLevel(parameters.powerLevelCount / 2, parameters.powerLevelCount);
                _rounds = 1;
                Unavailable = targets == null || targets.Count == 0 || parameters.candidateBudget <= 0;
                if (Unavailable)
                {
                    _phase = 2;
                }
            }

            /// <summary>取下一个候选。返回 false ⇒ 规划枚举已结束（此时调 <see cref="Finish"/>）。</summary>
            public bool TryGetNext(out AiPlanSlot slot)
            {
                slot = default;
                if (_phase == 0)
                {
                    while (_t < _targets.Count)
                    {
                        int target = _targets[_t];
                        while (_p < _context.pocketCenters.Length)
                        {
                            if (Evaluated >= _budget)
                            {
                                _phase = 2;
                                return false;
                            }

                            float aim = AimDegreesForPocket(_context.cueBallPosition,
                                _context.ballPositions[target], _context.pocketCenters[_p]);
                            int pocket = _p;
                            _p++;
                            if (float.IsNaN(aim))
                            {
                                continue;
                            }

                            slot = new AiPlanSlot
                            {
                                target = target, pocket = pocket, aim = aim, power = _probePower,
                            };
                            return true;
                        }

                        _p = 0;
                        _t++;
                    }

                    if (!_hasBase)
                    {
                        _phase = 2;
                        return false;
                    }

                    _rounds = Mathf.Max(1,
                        Mathf.RoundToInt(_parameters.aimStepCoarseDeg / _parameters.aimStepFineDeg));
                    _phase = 1;
                    _i = -_rounds;
                    _level = 0;
                }

                if (_phase == 1)
                {
                    while (_i <= _rounds)
                    {
                        while (_level < _parameters.powerLevelCount)
                        {
                            if (Evaluated >= _budget)
                            {
                                _phase = 2;
                                return false;
                            }

                            float aim = _bestBase.aim + _i * _parameters.aimStepFineDeg;
                            float power = PowerForLevel(_level, _parameters.powerLevelCount);
                            int level = _level;
                            _level++;
                            slot = new AiPlanSlot
                            {
                                target = _bestBase.target,
                                pocket = _bestBase.pocket,
                                aim = aim,
                                power = power,
                            };
                            return true;
                        }

                        _level = 0;
                        _i++;
                    }

                    _phase = 2;
                }

                return false;
            }

            /// <summary>回填某个候选的评分（与 `Plan` 里 `evaluated++ / Offer / bestBase` 完全同序）。</summary>
            public void Submit(in AiPlanSlot slot, float score)
            {
                Evaluated++;
                var candidate = new Candidate(score, slot.aim, slot.power, slot.target, slot.pocket);
                Offer(_top, _topK, candidate);
                if (!_hasBase || candidate.Beats(_bestBase))
                {
                    _bestBase = candidate;
                    _hasBase = true;
                }
            }

            /// <summary>收尾：选杆（top-K）+ 按档位扰动，产出意图（**与 `Plan` 尾部逐句一致**）。</summary>
            public AiShotIntent Finish(int seed)
            {
                var intent = new AiShotIntent { planned = false, targetBall = -1, pocketIndex = -1 };
                if (!_hasBase || _top.Count == 0)
                {
                    return intent;
                }

                var rng = new Rng(seed);
                Candidate chosen = best0(_top);
                int k = _parameters.topKPick <= 1
                    ? 1
                    : (_parameters.topKPick < _top.Count ? _parameters.topKPick : _top.Count);
                if (k > 1)
                {
                    int pick = (int)(rng.NextFloat() * k);
                    if (pick >= k || pick < 0)
                    {
                        pick = k - 1;
                    }

                    chosen = bestK0(_top, pick);
                }

                float aimNoise = _parameters.aimSigmaDeg * rng.NextGaussian();
                float powerNoise = _parameters.powerSigmaFraction * rng.NextGaussian();

                intent.planned = true;
                intent.targetBall = chosen.target;
                intent.pocketIndex = chosen.pocket;
                intent.score = chosen.score;
                intent.candidatesEvaluated = Evaluated;
                intent.aimDegrees = chosen.aim + aimNoise;
                intent.power = Mathf.Clamp01(chosen.power * (1f + powerNoise));
                return intent;
            }
        }

        private struct Candidate
        {
            public readonly float score;
            public readonly float aim;
            public readonly float power;
            public readonly int target;
            public readonly int pocket;

            public Candidate(float score, float aim, float power, int target, int pocket)
            {
                this.score = score;
                this.aim = aim;
                this.power = power;
                this.target = target;
                this.pocket = pocket;
            }

            /// <summary>**全序**判优（并列时按 目标球号 / 袋号 / 方向 定序 ⇒ 结果与遍历顺序无关）。</summary>
            public bool Beats(Candidate other)
            {
                if (score != other.score)
                {
                    return score > other.score;
                }

                if (target != other.target)
                {
                    return target < other.target;
                }

                if (pocket != other.pocket)
                {
                    return pocket < other.pocket;
                }

                return aim < other.aim;
            }
        }

        private static void Offer(List<Candidate> top, int k, Candidate candidate)
        {
            int cap = k <= 0 ? 1 : k;
            int insertAt = top.Count;
            for (int i = 0; i < top.Count; i++)
            {
                if (candidate.Beats(top[i]))
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt >= cap)
            {
                return;   // 进不了前列 ⇒ 丢弃（预算内的"早退"，省内存）
            }

            top.Insert(insertAt, candidate);
            if (top.Count > cap)
            {
                top.RemoveAt(top.Count - 1);
            }
        }

        private static Candidate best0(List<Candidate> top) => top[0];

        private static Candidate bestK0(List<Candidate> top, int rank)
        {
            int index = rank < top.Count ? rank : top.Count - 1;
            return top[index];
        }

        /// <summary>
        /// **确定性 RNG**（xorshift32）—— 让"同一 `seed` + 同一局面逐位可复现"**不依赖**运行时版本。
        /// ⚠ **禁止**换成 `UnityEngine.Random` / `System.Random`（前者的种子语义跨版本不保证一致）。
        /// </summary>
        private struct Rng
        {
            private uint _state;

            public Rng(int seed)
            {
                _state = seed == 0 ? 0x9E3779B9u : (uint)seed;
            }

            public uint NextUInt()
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x;
                return x;
            }

            /// <summary>`[0,1)` 均匀（取高 24 位 ⇒ 恰好落在 float 的可表示区间内）。</summary>
            public float NextFloat()
            {
                return (NextUInt() >> 8) * (1f / 16777216f);
            }

            /// <summary>标准正态（Box–Muller；同 `seed` ⇒ 同序列）。</summary>
            public float NextGaussian()
            {
                float u1 = NextFloat();
                if (u1 < 1e-7f)
                {
                    u1 = 1e-7f;
                }

                float u2 = NextFloat();
                return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
            }
        }
    }
}
