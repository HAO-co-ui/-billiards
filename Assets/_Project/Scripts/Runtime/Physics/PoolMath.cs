using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 物理内核中**开方 / 三角函数**的唯一出口（工程标准 R5-3）。
    ///
    /// 为什么必须单点：跨平台浮点确定性要求全部超越函数走同一口径。
    /// 若内核里混用 <c>Mathf.Sqrt</c> 与 <c>(float)Math.Sqrt</c>（double 版），
    /// 会在不同 IL2CPP / Mono 后端产生最后一位的差异，破坏定长步进的逐帧可复现性。
    /// 因此内核代码**只允许**经本类调用，不得直接使用 <c>Mathf</c> 的超越函数。
    ///
    /// 说明：本类刻意保持 <c>public</c>（而非 <c>internal</c>）。
    /// 测试程序集 <c>Billiards.Tests.EditMode</c> 设了 <c>overrideReferences=true</c>，
    /// <c>internal</c> 成员对它不可见。
    /// </summary>
    public static class PoolMath
    {
        /// <summary>单精度平方根。等价于 <c>(float)Math.Sqrt(x)</c>，但走 Unity 的单一实现。</summary>
        public static float Sqrt(float x)
        {
            return Mathf.Sqrt(x);
        }

        /// <summary>单精度反正切（y/x，返回弧度，范围 (-π, π]）。</summary>
        public static float Atan2(float y, float x)
        {
            return Mathf.Atan2(y, x);
        }
    }
}
