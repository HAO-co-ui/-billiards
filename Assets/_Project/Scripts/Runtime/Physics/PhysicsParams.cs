namespace Billiards.Physics
{
    /// <summary>
    /// 球-库边、球-球接触与滚动阻力的物理参数。做成 <c>struct</c> 以便按值传入定长步进
    /// （避免每步读易变的引用状态，保证同一 <c>Step</c> 内参数一致）。
    ///
    /// 单位与语义（**唯一真源**，实现不得自行造值）：
    ///  · <see cref="cushionRestitution"/> = 库边恢复系数 e，[0,1]，作用于**法向**分量；
    ///  · <see cref="cushionTangentialFriction"/> = 库边切向摩擦保留系数 μ，[0,1]，
    ///    作用于**切向**分量（1 = 无摩擦，0 = 切向全耗散）；
    ///  · <see cref="ballRestitution"/> = 球-球恢复系数 e_ball，[0,1]，作用于**法向**分量；
    ///    运行时默认 **0.95**（台球实测 0.93–0.96）；单测参数化覆盖 **1.0 与 0.95 两档**
    ///    （**运行时默认 0.95 ≠ 只测 0.95**）；
    ///  · <see cref="ballTangentialFriction"/> = 球-球切向保留系数 μ_ball，[0,1]，
    ///    **本轮钉死 1.0**：切向相对速度的物理汇是自旋，而本模型**无角自由度**，
    ///    按 μ&lt;1 去衰减属**非物理耗散** ⇒ 光滑球（μ=1，切向不变）是无自旋下唯一自洽的选择
    ///    （允许 ≠1 的前提是引入自旋之后）；
    ///  · <see cref="rollingDeceleration"/> = 台呢滚动减速度 a，m/s²，物理内核取 0。
    ///
    /// 注意 e 与 μ **各自独立**作用于各自分量，不得合并为一次反射后再缩放
    /// （那会把 μ 也乘到法向分量上，见 <c>PoolSimulator.ResolveSegmentContact</c>）。
    /// **库边与球-球是两对不同接触对**，故 <see cref="cushionRestitution"/> 与
    /// <see cref="ballRestitution"/> **不得共用字段**：合并会让"只改库边手感"污染球-球行为。
    /// </summary>
    public struct PhysicsParams
    {
        /// <summary>库边恢复系数 e（法向）。</summary>
        public float cushionRestitution;

        /// <summary>库边切向摩擦保留系数 μ（切向）。</summary>
        public float cushionTangentialFriction;

        /// <summary>球-球恢复系数 e_ball（法向）。运行时默认 0.95。</summary>
        public float ballRestitution;

        /// <summary>球-球切向保留系数 μ_ball（切向）。本轮**钉死 1.0**（见类型注释）。</summary>
        public float ballTangentialFriction;

        /// <summary>台呢滚动减速度 a，m/s²。</summary>
        public float rollingDeceleration;
    }
}
