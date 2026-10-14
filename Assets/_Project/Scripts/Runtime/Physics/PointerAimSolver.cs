using Billiards.Core;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **指针瞄准的纯函数核**：屏幕像素 → 台呢平面上的物理点 → 物理平面方向。
    ///
    /// 纯静态、**无 <c>Input</c>、无状态**：与内核既有纯函数（<see cref="PhysicsTestDrive.WholeSteps"/> /
    /// <see cref="PhysicsTestDrive.RackPositions"/>）同一风格 —— 测试可直接驱动，不必真的移动鼠标。
    /// 输入设备的读取属交互层（胶水），不在本类。
    ///
    /// ── 拾取口径（**唯一顺序 β**）──────────────────────────────────────────
    /// **先反缩放（射线 → 规范空间）→ 再与 <c>y = 0</c> 求交 → 再 <see cref="PoolGeometry.UnityToPhysics"/>**。
    /// 与"先投影再反缩放"（α）对**等比缩放**而言恒等（见 spec §8.2）；本工程钉 β。
    ///
    /// ── 方向口径（**唯一入口**）────────────────────────────────────────────
    /// 物理平面方向 → 世界方向**一律委托** <see cref="PoolGeometry.PhysicsDirectionToUnity"/>；
    /// 本类**不自行实现** `(−x, −y)` 映射（那样会出现第二处坐标转换点，违 ADR-0001 §7.1）。
    /// </summary>
    public static class PointerAimSolver
    {
        /// <summary>
        /// 射线方向与台呢平面法向的**近平行**阈值：`|D.y|` 小于它就拒绝。
        ///
        /// 取 **1e-4** 的两条依据：
        /// ① **量级**：规范空间里射线是**单位向量**，`|D.y| = sin(射线与台呢平面的夹角)`。
        ///    相机高约 1.75 m（规范）⇒ `|D.y| = 1e-4` 时交点约 **1.75e4 m（17.5 km）**，远超任何有效拾取
        ///    ⇒ 1e-4 以下一律视为"打不到台面"。
        /// ② **float 噪声余量**：Unity 的相机投影由 `pixelRect` 的浮点宽高比驱动，
        ///    **实测**「水平前视相机 + 屏幕中心像素」的 `|D.y| ≈ 1.35e-6`（不是精确 0）
        ///    ⇒ 阈值取 1e-6 会被自身噪声击穿（该相机本该被拒绝却通过）；1e-4 留约 **74×** 余量。
        /// </summary>
        private const float ParallelEpsilon = 1e-4f;

        /// <summary>
        /// **屏幕像素 → 物理平面点**。
        /// 顺序（β）：`cam.ScreenPointToRay` → 射线变到 `playfieldRoot` 的**规范局部空间**
        /// （`InverseTransformPoint` 处理原点与平移、`InverseTransformVector` 处理方向且**不带平移**）
        /// → 与 `y = <see cref="PoolConstants.ClothHeightY"/>` 求交 → <see cref="PoolGeometry.UnityToPhysics"/>。
        ///
        /// **拒绝域（唯一条件）**：射线与台呢平面**近平行**
        /// （`|D.y| &lt; <see cref="ParallelEpsilon"/>`）⇒ 求交参数 `t` 爆炸 ⇒ 返回 `false`，
        /// **不得**输出 NaN / Inf / 越界点。
        ///
        /// ⚠ **本检查是唯一的拒绝条件，且是「可被打死」的关键**：删掉它，近平行射线会因 `t = ±Inf`
        /// 与 `Inf × 0` 产出 NaN ⇒ 判据 `T49` 的拒绝用例 FAIL。
        /// **不得**再加一条"命中点必须落在球桌矩形内"之类的兜底 —— 那会让巨型坐标（或 NaN 的越界比较）
        /// 被二次拒绝，**掩盖掉本检查的注入**（spec §5 T49 的注入 = 删本条）。
        ///
        /// ⇒ 调用方拿到 `false` 时**不得**改瞄准角（否则 NaN 会传播到 `aimDegrees`）。
        /// </summary>
        public static bool TryPickPhysicsPoint(Camera cam, Transform playfieldRoot,
                                               Vector2 screenPosition, out Vector2 physicsPoint)
        {
            physicsPoint = default;

            if (cam == null || playfieldRoot == null)
            {
                return false;
            }

            Ray ray = cam.ScreenPointToRay(screenPosition);

            // 反缩放（唯一实现 PlayfieldView）：原点要处理平移，方向**不带**平移。
            Vector3 origin = PlayfieldView.CanonicalFromDisplay(playfieldRoot, ray.origin);
            Vector3 direction = playfieldRoot.InverseTransformVector(ray.direction);

            // 近平行 ⇒ 拒绝（唯一拒绝条件；否则 t 爆炸 → NaN 传播到 aimDegrees）。
            if (Mathf.Abs(direction.y) < ParallelEpsilon)
            {
                return false;
            }

            float t = (PoolConstants.ClothHeightY - origin.y) / direction.y;

            // 规范空间中求交 ⇒ 唯一坐标系转换点。
            Vector3 canonicalPoint = origin + t * direction;
            physicsPoint = PoolGeometry.UnityToPhysics(canonicalPoint);
            return true;
        }

        /// <summary>
        /// **白球物理位 + 指向点 → 物理平面单位方向**（白球 → 指向点）。
        /// 两点重合（退化）⇒ 返回零向量（调用方须自行判断，不得喂给 `AimDirectionUnity` 之外的映射）。
        /// </summary>
        public static Vector2 AimDirectionPhysics(Vector2 cueBallPhysics, Vector2 targetPhysics)
        {
            Vector2 delta = targetPhysics - cueBallPhysics;
            return delta.sqrMagnitude > 0f ? delta.normalized : Vector2.zero;
        }

        /// <summary>
        /// **物理平面方向 → Unity 世界方向**。**委托** <see cref="PoolGeometry.PhysicsDirectionToUnity"/>
        /// （`(−dx, 0, −dy)`），本类只负责转交，不含平移、不自行映射。
        /// </summary>
        public static Vector3 WorldDirectionFromPhysics(Vector2 aimPhysics)
        {
            return PoolGeometry.PhysicsDirectionToUnity(aimPhysics);
        }
    }
}
