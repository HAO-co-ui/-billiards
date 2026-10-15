using Billiards.Physics;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **HUD 的显示映射单点**：击球流程状态 → 状态条文案与 `Shoot` 可用性；力度 → 百分比文案；计数 → `n / m`。
    ///
    /// ── 文案口径（规格 §4.3；**全英文**，本阶段文案不得含中文）────────────────────
    /// 素材包的两套字体只覆盖拉丁子集（`R4` 风险条目）⇒ 35-A 的 HUD 文案全部英文是**排期明写的规避手段**，
    /// 不是偷懒。中文方案属正式版决策项。
    ///
    /// ── 与排期「状态条（`aim` / `moving`）」的对应 ────────────────────────────
    /// 流程状态机有五个相位（`Aim`/`Charging`/`Shooting`/`Moving`/`Settle`），状态条只有两态：
    /// 玩家可操作（`Aim`/`Charging`）显示 `Aim`；球未停（`Shooting`/`Moving`/`Settle`）显示 `Moving`
    /// —— 与设计说明 §5「球运动：击球与力条在球停止前不可用」同口径。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// 本类**不接点击**：`Shoot` 按钮只做**显示态**（可用/禁用）。出杆通路仍是既有的右键拉杆 + `Space`。
    /// </summary>
    public static class PracticeHudState
    {
        // ── 文案常量（单点；视图与判据 `T97` 的字符覆盖检查共用同一组字面量）────────
        public const string StateAim = "Aim";
        public const string StateMoving = "Moving";
        public const string LabelBallsLeft = "BALLS LEFT";
        public const string LabelShots = "SHOTS";
        public const string LabelPocketed = "POCKETED";
        public const string LabelPower = "POWER";
        public const string ButtonShoot = "Shoot";

        /// <summary>犯规态文案（设计说明 §9：犯规用**酒红**、克制、不以颜色作唯一识别手段 ⇒ 同时给文字）。</summary>
        public const string StateFoul = "Foul";

        /// <summary>犯规的处置说明 —— 本工程的实际处置是**白球放回开球点**（见下）。</summary>
        public const string FoulDetail = "Ball in hand";

        /// <summary>状态条在无状态时显示的常驻说明（与犯规/被拒提示共用同一个文本位）。</summary>
        public const string CaptionIdle = "PRACTICE";

        /// <summary>出杆被拒（整盘未静止）时的状态条文案 —— 把"按了没反应"变成"看得见的原因"。</summary>
        public const string StateBallsMoving = "Balls moving";

        /// <summary>出杆被拒时的处置说明。</summary>
        public const string RefusedDetail = "Wait for the table";

        /// <summary>
        /// **状态条该显示什么**（纯函数，唯一判据点）：犯规窗口内**优先**显示犯规，否则按流程相位。
        ///
        /// ⚠ 犯规的处置口径（如实登记）：本工程**没有规则引擎**（35-B），可观测的犯规只有
        /// **白球落袋（scratch）** 一种；处置是 `PhysicsTestDrive` 把白球**放回开球点**、
        /// 并把该次计进 `FOULS` 列。**手动自由球摆位未实现**（登记 35-B）⇒ 文案用
        /// `Ball in hand` 描述"球权交回你手上"，**不**声称可以任意摆位。
        /// </summary>
        public static string StatusLabel(ShotPhase phase, bool foulWindow)
        {
            return StatusLabel(phase, foulWindow, false);
        }

        /// <summary>
        /// **状态条的完整优先级**：出杆被拒（未静止）> 犯规 > 流程相位。
        /// 「被拒」排在犯规之前 —— 犯规是**已经发生的事**，被拒是**你正想做的事没做成**，
        /// 后者更需要当场的解释。
        /// </summary>
        public static string StatusLabel(ShotPhase phase, bool foulWindow, bool refusedWindow)
        {
            if (refusedWindow)
            {
                return StateBallsMoving;
            }

            return foulWindow ? StateFoul : StateLabel(phase);
        }

        /// <summary>与 <see cref="StatusLabel(ShotPhase, bool, bool)"/> 配套的副标题。</summary>
        public static string StatusDetail(bool foulWindow, bool refusedWindow)
        {
            if (refusedWindow)
            {
                return RefusedDetail;
            }

            return foulWindow ? FoulDetail : CaptionIdle;
        }

        /// <summary>状态条文案。</summary>
        public static string StateLabel(ShotPhase phase)
        {
            return phase == ShotPhase.Aim || phase == ShotPhase.Charging ? StateAim : StateMoving;
        }

        /// <summary>`Shoot` 是否可用 —— **只在 `Aim` 态可用**（蓄力中/出杆中/球运动中/静止过渡都不可用）。</summary>
        public static bool ShootEnabled(ShotPhase phase)
        {
            return phase == ShotPhase.Aim;
        }

        /// <summary>力度（0–1，越界先夹紧）→ 整数百分比文案。</summary>
        public static string PowerLabel(float normalizedPower)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(normalizedPower) * 100f) + "%";
        }

        /// <summary>已进球 / 目标球总数。</summary>
        public static string PocketedLabel(int pocketed, int total)
        {
            return pocketed + " / " + total;
        }

        /// <summary>
        /// HUD 会出现的**全部字符**（字面量 + 动态片段），供 `T97 (c)` 的字符覆盖检查使用。
        /// 动态片段取边界值（`0%` / `100%` / 两位数计数）以覆盖数字与符号。
        /// </summary>
        public static string AllDisplayCopy()
        {
            return string.Concat(
                StateAim, StateMoving,
                LabelBallsLeft, LabelShots, LabelPocketed, LabelPower, ButtonShoot,
                PowerLabel(0f), PowerLabel(0.45f), PowerLabel(1f),
                PocketedLabel(0, 15), PocketedLabel(15, 15),
                "0123456789");
        }

        /// <summary>计数组三列的标签（顺序 = 左→右）。</summary>
        public static readonly string[] CounterLabels = { LabelBallsLeft, LabelShots, LabelPocketed };
    }
}
