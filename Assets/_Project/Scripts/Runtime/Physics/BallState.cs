using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 单个球的运动状态，位于**规范二维物理平面**（x 长轴、y 短轴，单位米，见 ADR-0001）。
    ///
    /// 刻意做成 <c>struct</c>：定长步进内核每步要复制/写回整组球状态，
    /// 值语义可避免别名与堆分配，也让 <see cref="PoolSimulator.LoadBalls"/> 的
    /// 拷贝语义清晰（外部传入的状态不会被内核反向改动）。
    /// </summary>
    public struct BallState
    {
        /// <summary>球心位置，米。</summary>
        public Vector2 position;

        /// <summary>球心速度，米/秒。</summary>
        public Vector2 velocity;
    }
}
