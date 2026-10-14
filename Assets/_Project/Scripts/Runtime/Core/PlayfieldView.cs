using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// 「显示世界 ↔ 规范世界」的**唯一**映射点（表现层等比缩放）。
    ///
    /// ── 为什么单独开一个点（与 <see cref="PoolGeometry"/> 的关系）────────────
    /// 本工程把两件**正交**的事分成**两个单点**：
    ///  1. <see cref="PoolGeometry"/> = **坐标系（轴 / 手性 / 单位）**转换点。点映射 `(−px, h, −py)`、
    ///     方向映射 `(−dx, 0, −dy)` —— 这是已冻结的契约。
    ///  2. 本类 = **表现层等比缩放**（"桌面扩大 20%"，`PoolConstants.PlayfieldVisualScale`）。
    ///
    /// **边界（不得复合）**：本类位于 <see cref="PoolGeometry"/> 的**下游** ——
    /// 正向：先 `PoolGeometry.PhysicsToUnity` 得**规范世界**坐标，再经本类放大到**显示世界**；
    /// 反向：先本类收回**规范世界**，再 `PoolGeometry.UnityToPhysics`。
    /// 规范世界坐标**不得**再被放进带缩放的根节点下（那会变成 1.2² = 1.44 的双重缩放）。
    ///
    /// ── 实现口径 ───────────────────────────────────────────────────────────
    /// 只用 `Transform.TransformPoint` / `Transform.InverseTransformPoint`（**通用仿射逆变换**），
    /// 因此天然含"去平移 / 去旋转 / 去缩放"，且**不假设**根节点的缩放值 ——
    /// 缩放真相只有一个：`playfieldRoot.localScale`。**禁止**在这里手写 `× 1.2` / `× 1/1.2`
    /// （那会在根节点带平移或旋转时静默算错）。
    /// </summary>
    public static class PlayfieldView
    {
        /// <summary>
        /// **规范世界 → 显示世界**（= 挂在缩放根下的子节点"局部坐标 → 世界"）。
        /// 规范世界坐标进 `<paramref name="playfieldRoot"/>` 的**局部空间**；
        /// 显示世界 = `playfieldRoot.TransformPoint(规范局部)`。
        /// </summary>
        public static Vector3 DisplayFromCanonical(Transform playfieldRoot, Vector3 canonicalWorld)
        {
            return playfieldRoot.TransformPoint(canonicalWorld);
        }

        /// <summary>
        /// **显示世界 → 规范世界**（= `playfieldRoot.InverseTransformPoint`）。
        /// 指针拾取的**反缩放**步骤走这里（唯一实现）。
        /// </summary>
        public static Vector3 CanonicalFromDisplay(Transform playfieldRoot, Vector3 displayWorld)
        {
            return playfieldRoot.InverseTransformPoint(displayWorld);
        }

        /// <summary>
        /// 断言用：`playfieldRoot` 的**本地等比缩放**是否 == <see cref="PoolConstants.PlayfieldVisualScale"/>
        /// （逐分量、容差 1e-6）。<c>null</c> 或非等比 / 值不符 ⇒ <c>false</c>。
        /// </summary>
        public static bool HasExpectedScale(Transform playfieldRoot)
        {
            if (playfieldRoot == null)
            {
                return false;
            }

            const float tolerance = 1e-6f;
            Vector3 scale = playfieldRoot.localScale;
            float expected = PoolConstants.PlayfieldVisualScale;
            return Mathf.Abs(scale.x - expected) <= tolerance
                && Mathf.Abs(scale.y - expected) <= tolerance
                && Mathf.Abs(scale.z - expected) <= tolerance;
        }
    }
}
