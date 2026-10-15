using System;
using Billiards.Physics;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>一局的结局。`None` = 还没结束。</summary>
    public enum RackOutcome
    {
        None = 0,

        /// <summary>目标球全部落袋。</summary>
        TableCleared = 1,

        /// <summary>玩家主动重摆（本工程**唯一**可诚实判定的"非清台"结局，见规格 §2.1）。</summary>
        RackEnded = 2,
    }

    /// <summary>会话统计快照（可序列化；字段名即导出 JSON 的键）。</summary>
    [Serializable]
    public struct PracticeSessionSnapshot
    {
        public int shots;
        public int pots;
        public int fouls;
        public int racks;
        public int reracks;
        public int playAgain;
        public float elapsedMs;
        public RackOutcome outcome;
    }

    /// <summary>
    /// **练习会话的统计与结算判定**（纯逻辑，无 MonoBehaviour、不读 Unity 时间）。
    ///
    /// ── 三列数据各自的**真源**（全部是既有只读出口，本类不新建任何状态）──────────
    /// · **杆数** ← `PhysicsTestDrive.LaunchCount`（**跨杆单调、不因重摆归零**）⇒ 本类只做
    ///   "当前值 − 本局基线"的差；**刻意不重置**它，因为 `PoolAudio` / `PoolVfxView` 把它当
    ///   单调出杆计数器用，重置会让重摆后的第一杆**既不响也不出特效**。
    /// · **进球数** ← 球表逐球 `IsBallInPlay`（`CountBalls` 是**唯一实现点**，HUD 与结算卡共用）。
    ///   ⚠ 不能用 `PoolSimulator.CapturedBallCount`：它在**每杆**开头被 `ResetDiagnostics()` 清零
    ///   （见 `PhysicsTestDrive` 出杆路径），只反映"本杆"入袋数，不是本局累计。
    /// · **犯规数** ← `PhysicsTestDrive.LastLaunchRespawnedCueBall` 的**上升沿**计数
    ///   ⇒ 口径 = **白球落袋（scratch）**，**语义窄于**参考稿的"犯规"（完整分类属 35-B）。
    ///
    /// ── 结算判定（纯观测，不需要规则引擎）──────────────────────────────────
    /// 排期要求 `Table cleared` / `Rack ended` 两种标题；本工程**没有规则引擎也没有球号**
    /// ⇒ 只实现两条可观测结局，**清台优先于主动重摆**（语义不得含糊，`T100 (a)` 锁死）。
    /// 规则判负原因（如参考稿的 `8-ball potted too early`）**登记为 35-B**，此处不编造。
    /// </summary>
    public static class PracticeSession
    {
        // ── 文案常量（单点；视图与判据共用同一组字面量）──────────────────────
        public const string TitleTableCleared = "Table cleared";
        public const string TitleRackEnded = "Rack ended";
        public const string ReasonTableCleared = "Every ball is off the table";
        public const string ReasonRackEnded = "Re-racked by the player";

        /// <summary>
        /// **结算判定**：清台（目标球全落袋）**优先**，其次才是主动重摆。
        /// `objectBallsTotal ≤ 0`（空球台）不得判成清台 —— 那是"没有可打的球"，不是"打完了"。
        /// </summary>
        public static RackOutcome ResolveOutcome(int objectBallsPocketed, int objectBallsTotal, bool reRackRequested)
        {
            if (objectBallsTotal > 0 && objectBallsPocketed >= objectBallsTotal)
            {
                return RackOutcome.TableCleared;
            }

            return reRackRequested ? RackOutcome.RackEnded : RackOutcome.None;
        }

        /// <summary>结算卡标题。`None` ⇒ 空串（**不得**给出误导性标题）。</summary>
        public static string OutcomeTitle(RackOutcome outcome)
        {
            if (outcome == RackOutcome.TableCleared)
            {
                return TitleTableCleared;
            }

            return outcome == RackOutcome.RackEnded ? TitleRackEnded : string.Empty;
        }

        /// <summary>结算卡副标题（真实原因，不编造规则判负原因）。</summary>
        public static string OutcomeReason(RackOutcome outcome)
        {
            if (outcome == RackOutcome.TableCleared)
            {
                return ReasonTableCleared;
            }

            return outcome == RackOutcome.RackEnded ? ReasonRackEnded : string.Empty;
        }

        /// <summary>进球率 = 进球 / 杆数。**除零返回 0**（不得产生 NaN / Inf）。</summary>
        public static float PocketRate(int pots, int shots)
        {
            return shots > 0 ? (float)pots / shots : 0f;
        }

        /// <summary>
        /// **逐球清点**（唯一实现点，铁律 16）：0 号是白球，不算目标球。
        /// HUD 的 `POCKETED n / m` 与结算卡的三列都走这里 ⇒ 不存在第二份口径。
        /// </summary>
        public static void CountBalls(PoolSimulator simulator, out int pocketed, out int total)
        {
            pocketed = 0;
            total = 0;
            if (simulator == null)
            {
                return;
            }

            total = Mathf.Max(0, simulator.Balls.Count - 1);
            for (int i = 1; i < simulator.Balls.Count; i++)
            {
                if (!simulator.IsBallInPlay(i))
                {
                    pocketed++;
                }
            }
        }
    }

    /// <summary>
    /// **会话累积器**（纯 C#，不依赖 Unity 时间 —— `nowSeconds` 由调用方传入，故判据可完全确定性地驱动）。
    ///
    /// 生命周期：`BeginRack(launchCount)` 起一局 → 每帧 `Observe(...)` → 判定出结局后由视图显示结算卡
    /// → 玩家选 `Play again`（`NotePlayAgain()` + `BeginRack`）或直接 `BeginRack`（重摆）。
    /// </summary>
    public sealed class PracticeSessionTracker
    {
        private int _rackBaselineShots;
        private int _lastLaunchCount;
        private int _lastPocketed;
        private int _lastTotal;
        private int _fouls;
        private int _racks;
        private int _reracks;
        private int _playAgain;
        private float _elapsedMs;
        private float _lastSeconds;
        private bool _hasClock;
        private bool _reRackRequested;
        private bool _settled;
        private RackOutcome _outcome;

        /// <summary>本局结局（`None` = 进行中）。</summary>
        public RackOutcome Outcome => _outcome;

        /// <summary>起一局：记下杆数基线、清掉本局的结局与重摆请求。</summary>
        public void BeginRack(int launchCount)
        {
            _rackBaselineShots = launchCount;
            _lastLaunchCount = launchCount;
            _lastPocketed = 0;
            _lastTotal = 0;
            _elapsedMs = 0f;
            _hasClock = false;
            _reRackRequested = false;
            _settled = false;
            _outcome = RackOutcome.None;
        }

        /// <summary>
        /// 喂一帧观测值。**杆数用累计值**（`launchCount`），本类自己减基线；
        /// `cueBallRespawned` 是"这一杆白球是否落袋"的**每杆**标志，只在**新杆**（`launchCount` 上升沿）时计一次。
        /// </summary>
        public void Observe(int launchCount, int pocketed, int total, bool cueBallRespawned, float nowSeconds)
        {
            if (launchCount > _lastLaunchCount)
            {
                if (cueBallRespawned)
                {
                    _fouls++;   // scratch：白球落袋 ⇒ 上升沿计一次（同一杆内不重复计）
                }

                _lastLaunchCount = launchCount;
            }

            _lastPocketed = Mathf.Clamp(pocketed, 0, Mathf.Max(total, pocketed));
            _lastTotal = Mathf.Max(0, total);

            if (_hasClock)
            {
                float delta = nowSeconds - _lastSeconds;
                if (delta > 0f)
                {
                    _elapsedMs += delta * 1000f;
                }
            }

            _lastSeconds = nowSeconds;
            _hasClock = true;

            if (_settled)
            {
                return;
            }

            RackOutcome resolved = PracticeSession.ResolveOutcome(_lastPocketed, _lastTotal, _reRackRequested);
            if (resolved == RackOutcome.None)
            {
                return;
            }

            _settled = true;
            _outcome = resolved;
            _racks++;
            if (resolved == RackOutcome.RackEnded)
            {
                _reracks++;
            }
        }

        /// <summary>玩家请求重摆（`R` 键 / 结算卡按钮）。**只置请求**，判定仍由 `Observe` 统一做。</summary>
        public void RequestReRack()
        {
            _reRackRequested = true;
        }

        /// <summary>玩家在结算卡上点了 `Play again`。</summary>
        public void NotePlayAgain()
        {
            _playAgain++;
        }

        public PracticeSessionSnapshot Snapshot()
        {
            return new PracticeSessionSnapshot
            {
                shots = Mathf.Max(0, _lastLaunchCount - _rackBaselineShots),
                pots = _lastPocketed,
                fouls = _fouls,
                racks = _racks,
                reracks = _reracks,
                playAgain = _playAgain,
                elapsedMs = _elapsedMs,
                outcome = _outcome,
            };
        }
    }
}
