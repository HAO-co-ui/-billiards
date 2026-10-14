namespace Billiards.Physics
{
    /// <summary>
    /// 拖拽瞄准的**交互阶段**（纯状态，无 <c>Input</c>、无 Unity 依赖）。
    /// </summary>
    public enum AimPhase
    {
        /// <summary>未锁定：指针移动即更新方向（初始态；<c>CancelAim</c> 之后也回到此态）。</summary>
        Free,

        /// <summary>按住 <c>AimLock</c>：**跟随指针**（拖拽瞄准）。</summary>
        Dragging,

        /// <summary>松开 <c>AimLock</c>：**冻结**当前方向（指针移动不再改方向）。</summary>
        Locked,
    }

    /// <summary>
    /// **拖拽瞄准 / 松开锁定**的状态机 —— 纯逻辑核，供 <see cref="PointerAimController"/> 的最外层胶水调用，
    /// 也供判据直接驱动（**不碰输入设备**）。
    ///
    /// ── 语义（契约）──────────────────────────────────────────────────────────
    /// 依 `Billiards.inputactions` 的 `AimLock`（`&lt;Mouse&gt;/leftButton`）与仓库内**唯一**对该交互的具体陈述
    /// （`Docs/35-A_Day1_工程基建_执行记录.md`：`AimLock` = "**按下拖拽瞄准，松开锁定方向**"）：
    ///  · **按住** ⇒ <see cref="AimPhase.Dragging"/>（跟随指针）；
    ///  · **松开**（且此前在拖拽）⇒ <see cref="AimPhase.Locked"/>（冻结）；
    ///  · **`CancelAim`** ⇒ <see cref="AimPhase.Free"/>（解锁，指针移动即更新）。
    ///
    /// 转移表（<see cref="Advance"/>，判据按此逐格驱动）：
    /// <code>
    /// cancel=true                 ⇒ Free      （优先级最高）
    /// cancel=false &amp;&amp; lockHeld=true  ⇒ Dragging
    /// cancel=false &amp;&amp; lockHeld=false &amp;&amp; 当前==Dragging ⇒ Locked
    /// 其余（cancel=false &amp;&amp; lockHeld=false &amp;&amp; 当前!=Dragging）⇒ 保持原态
    /// </code>
    /// ⇒ "**指针是否应改方向**"只有一条判据：<see cref="ShouldFollowPointer"/>（== 非 <see cref="AimPhase.Locked"/>）。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// 本类**不读输入设备** ⇒ 真鼠标按下/松开/移动的端到端行为不在此覆盖（Manual / PlayMode）。
    /// </summary>
    public sealed class PointerAimState
    {
        /// <summary>当前阶段（初始 <see cref="AimPhase.Free"/>）。</summary>
        public AimPhase Phase { get; private set; }

        /// <summary>
        /// 指针移动**是否应改写瞄准方向**。**唯一**的"该不该更新"出口
        /// （<see cref="AimPhase.Locked"/> ⇒ <c>false</c>，其余 ⇒ <c>true</c>）。
        /// </summary>
        public bool ShouldFollowPointer => Phase != AimPhase.Locked;

        /// <summary>回到初始态（<see cref="AimPhase.Free"/>）。供"重新开一杆 / 重载"时重置。</summary>
        public void Reset()
        {
            Phase = AimPhase.Free;
        }

        /// <summary>
        /// 推进**一步**（一步 = 一帧的输入采样）。**纯函数式**：只依赖入参与自身阶段。
        /// </summary>
        /// <param name="lockHeld">本帧 <c>AimLock</c> 是否被按住（鼠标左键按住）。</param>
        /// <param name="cancelAim">本帧是否触发 <c>CancelAim</c>（右键 / Esc）——<b>优先级最高</b>。</param>
        /// <returns>本帧指针移动**是否应更新方向**（= 转移后的 <see cref="ShouldFollowPointer"/>）。</returns>
        public bool Advance(bool lockHeld, bool cancelAim)
        {
            if (cancelAim)
            {
                Phase = AimPhase.Free;
            }
            else if (lockHeld)
            {
                Phase = AimPhase.Dragging;
            }
            else if (Phase == AimPhase.Dragging)
            {
                Phase = AimPhase.Locked;
            }

            return ShouldFollowPointer;
        }
    }
}
