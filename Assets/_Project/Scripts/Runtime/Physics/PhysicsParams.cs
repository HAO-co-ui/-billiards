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
    ///  · 台呢滚阻改为**两段模型**，由三个字段承载（见下）—— 台呢减速度是**速度的函数**
    ///    `a(v) = a_roll + (a_slide − a_roll)·clamp01(v / v_transition)`，m/s²：
    ///      · <see cref="rollingDecelerationSlide"/> = **滑动段**减速度 a_slide（高速段取值，量级 1–3）；
    ///      · <see cref="rollingDecelerationRoll"/> = **滚动段**减速度 a_roll（低速段取值，量级 0.10–0.30）；
    ///      · <see cref="rollingTransitionSpeed"/> = 两段过渡速度 v_transition，m/s（量级 1–3）。
    ///
    /// ── 沿革 / 映射墓碑（旧单值滚阻字段**已删**）────────────────────────────
    /// 本类型此前只有一个「**恒定减速度**」标量字段（本次删除）。映射关系：
    ///   **旧单值 `a = X`  ⇔  `rollingDecelerationSlide = rollingDecelerationRoll = X`**
    ///   （`v_transition` 取任意值均可 —— 退化档下 `(a_slide − a_roll) = +0` ⇒ `a(v) ≡ X` 逐位等价）。
    /// 沿革口径：内核阶段旧单值曾取 `0`（"单一口径"，台呢滚阻恒 0）→ 调参阶段定为 `0.9` →
    /// 本次起由上述三字段取代。**删旧字段的理由**：退化档已由"两段取同值"完整表达，
    /// 旧字段是**同一个量的第二种写法**（必然分叉）；保留它会再造"看着能调、实际被覆盖"的字段。
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

        /// <summary>台呢**滑动段**减速度 a_slide，m/s²（高速段取值；量级 1–3）。</summary>
        public float rollingDecelerationSlide;

        /// <summary>台呢**滚动段**减速度 a_roll，m/s²（低速段取值；量级 0.10–0.30）。</summary>
        public float rollingDecelerationRoll;

        /// <summary>
        /// 两段过渡速度 v_transition，m/s。边界：`≤ 0` ⇒ `a(v) = a_slide`（不分区）；
        /// `v ≤ 0` ⇒ `a(v) = a_roll`（两段相等时二者同值）。
        /// </summary>
        public float rollingTransitionSpeed;

        /// <summary>
        /// **安全默认值 —— 必须用它，不要用 `default(PhysicsParams)`。**
        ///
        /// ⚠ **语言事实**：`default(T)`（T 为 struct）由 C# **定义为"字段全部清零"**，
        /// 与 C# 10+ 的"字段初始化器 / 显式无参构造"**无关** ⇒ 给字段写初始化器**改不了** `default` 的值。
        /// 因此"让 `default(PhysicsParams)` 成为安全值"这条要求**在 struct 上不可达**。
        ///
        /// 而全零对物理参数是**危险值**：`ballTangentialFriction = 0` 会命中
        /// <c>PoolSimulator</c> 的 `μ &lt; 1` 分支、把切向分量**整个删除**，
        /// 那正是明令禁止的**非物理耗散**；`ballRestitution = 0` 则让球-球碰撞**完全非弹性**。
        ///
        /// ⇒ 可取的做法是**两条防线**：① 调用点一律用本属性显式取值；
        /// ② 判据断言"内核**实际生效**的参数"是安全值（见 `PoolSimulator.Params`）。
        /// </summary>
        public static PhysicsParams SafeDefault
        {
            get
            {
                return new PhysicsParams
                {
                    cushionRestitution = 1f,
                    cushionTangentialFriction = 1f,
                    ballRestitution = 1f,
                    ballTangentialFriction = 1f,
                    rollingDecelerationSlide = 0f,
                    rollingDecelerationRoll = 0f,
                    rollingTransitionSpeed = 0f,
                };
            }
        }
    }
}
