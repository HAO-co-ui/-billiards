using System.Collections.Generic;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **一次击球预测的结果**（值类型；由 <see cref="ShotPredictor.Predict"/> 产出）。
    ///
    /// <para>本类型**只承载数据**，不含算法、不含 `Input`/`Time`/`Random`（预测侧不得读时间与随机，
    /// 否则破坏确定性）。所有几何量都在**规范物理平面**（米）：显示映射由表现层经唯一映射点完成，
    /// 本类型**不做**任何坐标转换。</para>
    ///
    /// <para>**未发生首次球-球接触**时（<see cref="HasFirstContact"/> 为 <c>false</c>）：
    /// <see cref="GhostBall"/> / <see cref="TargetBallCenter"/> / 两个出射方向 / <see cref="TimeToFirstContactSeconds"/>
    /// 均无意义；<see cref="CueWorldPath"/> 仍有效（= 白球在步数上限内的路径）。</para>
    /// </summary>
    public struct ShotPrediction
    {
        /// <summary>在步数上限内**是否发生了首次球-球接触**。</summary>
        public bool HasFirstContact;

        /// <summary>白球预测起点（规范坐标，米）：白球在场上时 = 它的当前位置；否则 = `footSpot`（与出杆口径一致）。</summary>
        public Vector2 CueStart;

        /// <summary>
        /// **幽灵球** = 首次球-球接触、**冲量施加前**的白球位置（规范坐标，米）。
        /// 与 <see cref="TargetBallCenter"/> 的距离**恰为 `2r`**（内核在 TOI 处解析推进，
        /// float 残差 ~1e-7 m）。⚠ 不得用"`Step()` 末端采样"或"球心连线的解析交点"顶替。
        /// </summary>
        public Vector2 GhostBall;

        /// <summary>被撞目标球在接触瞬间的球心位置（规范坐标，米）。</summary>
        public Vector2 TargetBallCenter;

        /// <summary>目标球的**出射方向**（单位向量，规范平面）：= 解算后目标球速度方向；退化时回落到"幽灵球→目标球心"连线。</summary>
        public Vector2 TargetOutgoingDirection;

        /// <summary>白球**撞后**方向（单位向量，规范平面）：= 解算后白球速度方向；退化（≈0）时为 <c>Vector2.zero</c>。</summary>
        public Vector2 CueOutgoingDirection;

        /// <summary>自预测起点到首次接触的仿真时间（秒）。</summary>
        public float TimeToFirstContactSeconds;

        /// <summary>实际推进的 <see cref="PoolSimulator.Step"/> 次数（**预测确实运行过**的非空转证据）。</summary>
        public int StepsSimulated;

        /// <summary>首次接触**之前**白球与库边（含袋角弧段）发生的接触次数（Step 粒度；见 <see cref="ShotPredictor"/> 的盲带说明）。</summary>
        public int CushionBounceCountBeforeContact;

        /// <summary>白球路径折线（规范坐标）：`[CueStart, …每步白球位置…, GhostBall]`。末点 == <see cref="GhostBall"/>（有接触时）。</summary>
        public IReadOnlyList<Vector2> CueWorldPath;

        /// <summary>**只读诊断出口**：预测结束时探测内核的 `BallBallContactCount`（供成本上界断言；测试不需够到 `Predict` 的局部实例）。</summary>
        public int BallBallContactCount;

        /// <summary>**只读诊断出口**：预测结束时探测内核的 `TotalContactCount`。</summary>
        public int TotalContactCount;

        /// <summary>
        /// **只读诊断出口**：探测内核**实际装载**的球数（= 快照里在场上球数，白球离台时另含一颗补在 `footSpot` 的白球）。
        /// 用途：让"快照只装在场上球"这条语义**可被外部断言**（否则该语义不可观测）。
        /// </summary>
        public int ProbeBallCount;
    }
}
