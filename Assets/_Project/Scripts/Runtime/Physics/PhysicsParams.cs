namespace Billiards.Physics
{
    /// <summary>
    /// 球-库边接触与滚动阻力的物理参数。做成 <c>struct</c> 以便按值传入定长步进
    /// （避免每步读易变的引用状态，保证同一 <c>Step</c> 内参数一致）。
    ///
    /// 单位与语义：
    ///  · <see cref="cushionRestitution"/> = 库边恢复系数 e，[0,1]，作用于**法向**分量；
    ///  · <see cref="cushionTangentialFriction"/> = 库边切向摩擦保留系数 μ，[0,1]，
    ///    作用于**切向**分量（1 = 无摩擦，0 = 切向全耗散）；
    ///  · <see cref="rollingDeceleration"/> = 台呢滚动减速度 a，m/s²，Day 4 取 0（详见 spec）。
    ///
    /// 注意 e 与 μ **各自独立**作用于各自分量，不得合并为一次反射后再缩放
    /// （那会把 μ 也乘到法向分量上，见 PoolSimulator.ResolveContact）。
    /// </summary>
    public struct PhysicsParams
    {
        /// <summary>库边恢复系数 e（法向）。</summary>
        public float cushionRestitution;

        /// <summary>库边切向摩擦保留系数 μ（切向）。</summary>
        public float cushionTangentialFriction;

        /// <summary>台呢滚动减速度 a，m/s²。</summary>
        public float rollingDeceleration;
    }
}
