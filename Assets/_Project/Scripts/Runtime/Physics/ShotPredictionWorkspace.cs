using System.Collections.Generic;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **击球预测的可复用工作区**（消除每帧 `Predict` 的分配）。
    ///
    /// <para>一次 <see cref="ShotPredictor.Predict(TableConfig, PoolSimulator, Vector2, int)"/> 过去会做三次分配：
    /// ① `new PoolSimulator(...)`（构造 82 段 + 6 袋的几何数组）；
    /// ② `new List&lt;BallState&gt;` 快照；
    /// ③ `new List&lt;Vector2&gt;` 路径。拖动瞄准时每帧跑一次 ⇒ 每帧数十 KB 的 GC 垃圾。
    /// 本类把这三者**持有并复用**（`Clear()` 而非新建），供**同一条热路径**（<c>AimAssistController</c>）反复使用。</para>
    ///
    /// <para>── 正确性契约（复用不得改变预测结果）────────────────────────────────
    /// 探测内核复用的**充分条件**是：复用后的状态与"新建 + 装载"逐位一致。
    /// 实测依据（`PoolSimulator`）：构造只写**与球无关的常量场**（参数 / 段 / 袋 / 子步数）并调一次
    /// <c>ResetDiagnostics()</c>；而 <c>LoadBalls()</c> **不清每杆计数** ⇒ 复用路径必须**显式补一次**
    /// <c>ResetDiagnostics()</c>（本类的使用方即 <see cref="ShotPredictor"/> 已补），补后状态与新建路径等价。
    /// 若参数 / 步长 / 终止条件 / 袋口乘子任一变化 ⇒ **重建**探测内核（缓存键见 <see cref="ShotPredictor"/>）。</para>
    ///
    /// <para>── 别名语义（调用方须知道）──────────────────────────────────────────
    /// <see cref="Path"/> 会被 <see cref="ShotPrediction.CueWorldPath"/> **直接引用**（不复制，避免分配）
    /// ⇒ 上一次 <c>Predict</c> 返回的结果里那条路径，会在**下一次对同一工作区调用 `Predict`** 时被改写。
    /// 单消费者场景（控制器只保留"当前"一次预测）下这是期望行为；需要长期持有路径者请自行复制。</para>
    /// </summary>
    public sealed class ShotPredictionWorkspace
    {
        /// <summary>复用的探测内核（`null` = 尚未建立）。</summary>
        internal PoolSimulator Probe;

        // 缓存键：任一不符 ⇒ 重建探测内核（保证与新建路径等价）。
        internal TableConfig ProbeTable;
        internal PhysicsParams ProbeParams;
        internal float ProbePocketScale;
        internal float ProbeRestSpeed;
        internal int ProbeRestFrames;

        /// <summary>
        /// **复用的接触事件缓冲**（给 <see cref="ShotPredictor"/> 取"首个接触事件"，避免每帧分配）。
        /// ⚠ 探测内核是**复用**的 ⇒ 事件缓冲可能残留**上一次预测**的事件 ⇒ 预测前必须先 drain 清空（见 `ShotPredictor`）。
        /// </summary>
        internal readonly List<ContactEvent> ContactEvents = new List<ContactEvent>(64);

        /// <summary>复用的在场上球快照缓冲。</summary>
        internal readonly List<BallState> Snapshot = new List<BallState>(20);

        /// <summary>复用的白球路径折线缓冲。</summary>
        internal readonly List<Vector2> Path = new List<Vector2>(128);
    }
}
