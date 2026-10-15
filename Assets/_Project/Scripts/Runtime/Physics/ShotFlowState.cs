namespace Billiards.Physics
{
    /// <summary>
    /// 击球流程的**阶段**（纯状态，无 <c>Input</c>、无 Unity 依赖）。
    /// </summary>
    public enum ShotPhase
    {
        /// <summary>瞄准：杆贴白球，可调方向（初始态）。</summary>
        Aim,

        /// <summary>蓄力：杆随力度后拉（1:1）。</summary>
        Charging,

        /// <summary>出杆：杆前推（程序动画），到位帧击球恰一次。</summary>
        Shooting,

        /// <summary>球在运动：杆隐藏。</summary>
        Moving,

        /// <summary>静止过渡：整盘停后的复位（再静止 ⇒ 回 Aim）。</summary>
        Settle,
    }

    /// <summary>
    /// **击球流程状态机**（<c>aim → charging → shooting → moving → settle → aim</c>）—— 纯逻辑核，
    /// 供 <see cref="CueStickController"/>（唯一驱动者）调用，也供判据 `T87` 直接驱动（不碰输入设备、不跑动画）。
    ///
    /// ── 语义（契约，排期「击球状态机」的可判定形式）──────────────────────
    ///  · <c>chargeStarted</c>（拉杆按下）：<see cref="ShotPhase.Aim"/> ⇒ <see cref="ShotPhase.Charging"/>；
    ///  · <c>chargeCancelled</c>（松手力度不足 / 蓄力被中止）：Charging ⇒ Aim；
    ///  · <c>chargeReleased</c>（松手力度达标）：Charging ⇒ Shooting —— **只在 Charging 态生效**
    ///    ⇒ 其余任何阶段的"松手"都**不产生出杆**（"无重复出杆"的集中守卫）；
    ///  · <c>strikeArrived</c>（前推到位帧）：Shooting ⇒ Moving（击球只在此迁移发生，恰一次）；
    ///  · <c>tableAtRest</c>（整盘静止）：Moving ⇒ Settle，Settle ⇒ Aim。
    ///  · **其余事件一律忽略（保持原态）** ⇒ 不死锁、不乱跳（如 Shooting 中再按拉杆键无效；
    ///    Moving 中按拉杆键无效 —— 与拉杆侧的 TableAtRest 门控互为双保险）。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────
    /// 本类不读输入、不驱动动画 ⇒ 端到端（真鼠标 → 杆动画 → 球动）属 PlayMode / 手动验收。
    /// </summary>
    public sealed class ShotFlowState
    {
        /// <summary>当前阶段（初始 <see cref="ShotPhase.Aim"/>）。</summary>
        public ShotPhase Phase { get; private set; } = ShotPhase.Aim;

        /// <summary>本阶段是否**允许进入蓄力**（只有 Aim 允许；Moving/Settle 中按拉杆被状态机拒绝）。</summary>
        public bool CanBeginCharge => Phase == ShotPhase.Aim;

        /// <summary>本阶段杆是否**应显示并对齐瞄准**（Aim/Charging/Shooting 显示；Moving/Settle 隐藏或复位）。</summary>
        public bool IsStickVisible => Phase == ShotPhase.Aim
                                      || Phase == ShotPhase.Charging
                                      || Phase == ShotPhase.Shooting;

        /// <summary>回到初始态（<see cref="ShotPhase.Aim"/>）。供重摆 / 重载时复位。</summary>
        public void Reset()
        {
            Phase = ShotPhase.Aim;
        }

        /// <summary>
        /// 推进**一步**（一步 = 一帧的事件）。纯函数式：只依赖入参与自身阶段；
        /// 事件互斥（同帧至多其一），按列出顺序判定。
        /// </summary>
        /// <returns>推进后的阶段（判据按此逐格断言）。</returns>
        public ShotPhase Advance(bool chargeStarted, bool chargeCancelled, bool chargeReleased, bool strikeArrived, bool tableAtRest)
        {
            if (chargeStarted && Phase == ShotPhase.Aim)
            {
                Phase = ShotPhase.Charging;
            }
            else if (chargeCancelled && Phase == ShotPhase.Charging)
            {
                Phase = ShotPhase.Aim;
            }
            else if (chargeReleased && Phase == ShotPhase.Charging)
            {
                Phase = ShotPhase.Shooting;
            }
            else if (strikeArrived && Phase == ShotPhase.Shooting)
            {
                Phase = ShotPhase.Moving;
            }
            else if (tableAtRest)
            {
                if (Phase == ShotPhase.Moving)
                {
                    Phase = ShotPhase.Settle;
                }
                else if (Phase == ShotPhase.Settle)
                {
                    Phase = ShotPhase.Aim;
                }
            }

            return Phase;
        }
    }
}
