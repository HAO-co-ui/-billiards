using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// 三个坐标系之间**唯一**的转换点。本项目对轴/手性转换有一条硬约束
    /// （美术交付文档原话）："不要同时应用导入器转换和二次手动转换。"
    /// 因此这里把每条转换规则显式写成常量与方法，作为全工程唯一的转换入口，
    /// 并由 TableIntegrityValidator 用可判据断言（不是目视）锁死。
    ///
    /// ── 三个坐标系（按实测如实标注）──────────────────────────────────────
    ///
    /// 1) 规范二维物理平面 (physics2d)：{x, y}，单位米，直接取自 JSON 的 `collision.*`，**不做任何转换**。
    /// 2) 源运行时三维 (runtime3d，three.js/glTF)：+X 长轴、+Y 上、+Z 短轴；JSON 自述 `physics2d.(x,y) == runtime3d.(x,z)`。
    /// 3) Unity 世界：物理平面被嵌入 XZ 水平面。
    ///
    /// ── 转换链（**只此一处**，三段式，可逐步追）──────────────────────────
    ///
    ///     physics2d ──[PhysicsToModelLocal]──▶ 模型局部 ──[ModelOrientation]──▶ Unity 世界
    ///
    /// 实测事实（AxisProbe 48 候选穷举 + 场景实例化实测）：
    ///  · Unity 导入 FBX 后，**网格局部顶点 = (−px, py, up)** —— Unity 把 X 取反（右手→左手）。
    ///  · 该 FBX 的根节点还自带 **Rx ≈ 270°（即 −90°）** 的旋转（来自 FBX 节点变换），
    ///    即 <see cref="ModelOrientation"/>；它把源 Z-up 几何摆正到 Unity 的 Y-up。
    ///    实测根节点 `rot = (270.02°, 0, 0)`，位置 0、缩放 1。
    ///
    /// ⇒ 由此推出（场景实例化后实测校核）：
    ///      PhysicsToModelLocal(px,py,h) = (−px, py, h)
    ///      PhysicsToUnity(px,py,h)      = ModelOrientation · PhysicsToModelLocal = (−px, h, −py)
    ///    与"实例化网格的世界坐标"**恰好重合**（不再是"在某对称性下近似重合"）。
    ///
    /// ── ⚠ 与早期版本的差异（重要，勿回改）────────────────────────────────
    /// 早期版本曾把 `PhysicsToUnity` 定为 `(px, h, py)`（"X、Y 均无符号翻转"）。
    /// 那个式子**只与网格局部顶点比对过**，从未与"实例化后的世界坐标"比对，
    /// 因而漏掉了模型根节点自带的 <see cref="ModelOrientation"/>，使"物理帧 ↔ 世界"**相差绕 Y 的 180°**。
    /// 实例化球桌后实测暴露：世界包围盒 X 端被镜像（差 0.277 mm，= 角袋 x 向不对称）。
    /// 现按"让不变量**恰好为真**"修正为上面的三段式（决策见 ADR-0001）。
    /// </summary>
    public static class PoolGeometry
    {
        // ── 模型自带朝向（唯一模型侧朝向常量）──────────────────────────────

        /// <summary>
        /// 导入模型根节点自带的朝向：**Rx ≈ 270°（= −90°）**。
        ///
        /// 来源：`PoolTable_Normalized.fbx` 的 FBX 节点变换。Unity 会把它写进导入根节点的 rotation。
        /// 它的作用是**把源 Z-up 几何摆正到 Unity Y-up**，因此是模型姿态的一部分，必须被转换链纳入。
        ///
        /// 注意：它 **不是**可以"在场景里再叠加一次"的东西 —— 场景里不要对球桌实例做任何手动旋转；
        /// 姿态知识只活在 <see cref="PhysicsToUnity"/> / <see cref="ModelVertexToUnity"/> 这条链上。
        /// </summary>
        public static readonly Quaternion ModelOrientation = Quaternion.Euler(270f, 0f, 0f);

        // ── 唯一转换入口 ────────────────────────────────────────────────────

        /// <summary>
        /// 物理平面点 → **模型局部空间**。
        /// 依据：实测 Unity 导入后网格局部顶点 = `(−px, py, up)`（Unity 把 X 取反）。
        /// </summary>
        public static Vector3 PhysicsToModelLocal(Vector2 physics, float height)
        {
            return new Vector3(-physics.x, physics.y, height);
        }

        /// <summary>
        /// 物理平面 → Unity 世界坐标。
        /// `= ModelOrientation · PhysicsToModelLocal`，即唯一转换链的合成结果。
        /// 与"实例化球桌网格的世界位置"**恰好重合**（由 TableIntegrityValidator 的 WLD-01 断言）。
        /// </summary>
        public static Vector3 PhysicsToUnity(Vector2 physics, float height)
        {
            return ModelOrientation * PhysicsToModelLocal(physics, height);
        }

        /// <summary>物理平面 → 台呢平面(y=0)上的 Unity 点。</summary>
        public static Vector3 PhysicsToUnity(Vector2 physics)
        {
            return PhysicsToUnity(physics, PoolConstants.ClothHeightY);
        }

        /// <summary>物理平面 → 球心所在高度的 Unity 点（球心默认离台呢一个球半径）。</summary>
        public static Vector3 PhysicsBallToUnity(Vector2 physics)
        {
            return PhysicsToUnity(physics, PoolConstants.BallRadius);
        }

        /// <summary>Unity 世界坐标 → 规范二维物理平面（指针拾取、调试回读用）。见 <see cref="PhysicsToModelLocal"/> 的逆。</summary>
        public static Vector2 UnityToPhysics(Vector3 world)
        {
            Vector3 local = Quaternion.Inverse(ModelOrientation) * world;
            return new Vector2(-local.x, local.y);
        }

        /// <summary>
        /// 模型**局部**顶点 → Unity 世界。
        /// 即把 <see cref="ModelOrientation"/> 作用到局部顶点上；**这是唯一的模型侧转换**。
        /// 场景里不得对球桌实例再叠加任何手动旋转（否则会与这里的合成重复）。
        /// </summary>
        public static Vector3 ModelVertexToUnity(Vector3 modelLocal)
        {
            return ModelOrientation * modelLocal;
        }

        // ── 重建包络（由转换链推导，非硬编码）──────────────────────────────

        /// <summary>
        /// 源（runtime3d 坐标系）球桌**外轮廓**包围盒下界，米。
        /// 直接取自 `table-metadata-reference.json` 的 `bounds_runtime`（X 长轴 / Y 上 / Z 短轴）。
        /// </summary>
        public static readonly Vector3 OuterBoundsMin = new Vector3(-1.412685751914978f, -0.8635167479515076f, -0.7976343631744385f);

        /// <summary>源球桌外轮廓包围盒上界（见 <see cref="OuterBoundsMin"/>）。</summary>
        public static readonly Vector3 OuterBoundsMax = new Vector3(1.412408709526062f, 0.06102215126156807f, 0.7976343631744385f);

        /// <summary>
        /// **Unity 世界**空间里球桌外轮廓包围盒下界 = 源包络经唯一转换链后的 AABB。
        /// 由 <see cref="OuterBoundsMin"/> 推导而非硬编码：避免"结论与推导各写一份"。
        /// </summary>
        public static Vector3 WorldBoundsMin => BoundCorner(false, false, false);

        /// <summary>Unity 世界空间里球桌外轮廓包围盒上界（见 <see cref="WorldBoundsMin"/>）。</summary>
        public static Vector3 WorldBoundsMax => BoundCorner(true, true, true);

        private static Vector3 BoundCorner(bool maxX, bool maxY, bool maxZ)
        {
            // 源包络的 8 个角点经转换链映射，再取 AABB
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int xi = 0; xi < 2; xi++)
            for (int yi = 0; yi < 2; yi++)
            for (int zi = 0; zi < 2; zi++)
            {
                // 源坐标 (x, up, z) = (px, height, py)
                float px = xi == 0 ? OuterBoundsMin.x : OuterBoundsMax.x;
                float up = yi == 0 ? OuterBoundsMin.y : OuterBoundsMax.y;
                float py = zi == 0 ? OuterBoundsMin.z : OuterBoundsMax.z;
                Vector3 w = PhysicsToUnity(new Vector2(px, py), up);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }

            return new Vector3(maxX ? max.x : min.x, maxY ? max.y : min.y, maxZ ? max.z : min.z);
        }

        // ── 参考常量（供 Validator 与测试做可判据断言）────────────────────────

        /// <summary>规范球桌外轮廓在长轴上的总长度，2.82509446144104 m。</summary>
        public const float OuterLength = 2.82509446144104f;

        /// <summary>规范球桌外轮廓在短轴上的总宽度，1.595268726348877 m。</summary>
        public const float OuterWidth = 1.595268726348877f;

        /// <summary>规范球桌外轮廓竖直总高度（桌腿底到库顶），0.9245388992130757 m。</summary>
        public const float OuterHeight = 0.9245388992130757f;

        /// <summary>
        /// 82 段库边**端点**的包围盒下界（物理平面 x/y，米）。与 <see cref="OuterBoundsMin"/> 不同：
        /// 后者是含库边外沿与桌腿的网格外轮廓。来源：`collision.segments` 端点实测；
        /// 与独立复核基线逐位一致。用于交叉核对轴向/缩放（端点 x 左右不对称仅 7.6e-8 m，**非镜像判据**）。
        /// </summary>
        public static readonly Vector2 CushionEndpointsMin = new Vector2(-1.334342835f, -0.699095331f);

        /// <summary>82 段库边端点的包围盒上界（见 <see cref="CushionEndpointsMin"/>）。</summary>
        public static readonly Vector2 CushionEndpointsMax = new Vector2(1.334342911f, 0.699095331f);

        /// <summary>源网格顶点数（FBX 原始），用于导入完整性核对。</summary>
        public const int SourceVertexCount = 10572;

        /// <summary>FBX GlobalSettings 声明的 UpAxis（1=Y）—— 与顶点实际轴向不符，记录在案。</summary>
        public const int FbxDeclaredUpAxisY = 1;
    }
}
