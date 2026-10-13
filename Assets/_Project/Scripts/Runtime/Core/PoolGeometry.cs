using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// 三个坐标系之间**唯一**的转换点。本项目对轴/手性转换有一条硬约束
    /// （美术交付文档原话）："不要同时应用导入器转换和二次手动转换。"
    /// 因此这里把每条转换规则显式写成常量与方法，作为全工程唯一的转换入口，
    /// 并由 TableIntegrityValidator 用可判据断言（不是目视）锁死。
    ///
    /// ── 坐标系定义（全部按实测如实标注）──────────────────────────────────
    ///
    /// 1) 规范二维物理平面 (physics2d)
    ///    {x, y}，单位米。x = 球桌长轴（±1.27 内库半长），y = 球桌短轴（±0.635054433 内库半宽）。
    ///    该平面**直接**取自 table-metadata-reference.json 的 collision.segments / pockets，
    ///    是本项目物理模拟器（Day 4+）唯一使用的坐标。**不做任何转换。**
    ///
    /// 2) 源运行时三维 (runtime3d) —— three.js / glTF
    ///    +X 长轴，+Y 竖直向上，+Z 短轴，右手系。
    ///    JSON 原文（gameplay-markers-reference.json → coordinates.physics2d_to_runtime3d）：
    ///      "Ball {x,y} -> mesh [x, BALL_RADIUS, y]; collider/pocket {x,y} -> [x, 0, y]"
    ///    即 physics2d.(x,y) 就是 runtime3d.(x, z)。
    ///
    /// 3) Unity 世界
    ///    +X 长轴，+Y 竖直向上，+Z 短轴。物理平面被嵌入 XZ 水平面。
    ///
    /// ── 关键实测事实（Day 2，非推断，由 AxisProbe 穷举 48 种候选后确定）────────
    ///
    /// 事实 1：PoolTable_Normalized.fbx 的 GlobalSettings **声明 UpAxis=Y**，
    ///   但顶点数据**实际是 Blender 原生 Z-up**（独立解析 FBX 二进制核验：bbox Z∈[-0.8635,0.0610]
    ///   正是"桌面到桌腿底"的竖直范围；6 袋 72 个 runtimeInnerRim 点到 FBX 顶点的最近距离 = 0.000000 m）。
    ///   这是 Blender FBX 导出器已知的元数据/数据不一致坑。
    ///
    /// 事实 2：Unity 导入该 FBX 时**把 X 轴取反**（右手→左手的 handedness 转换），
    ///   其余轴不变。实测 Unity 内网格顶点局部包围盒
    ///   = X[-1.412409, 1.412686] / Y[-0.797634, 0.797634] / Z[-0.863517, 0.061022]，
    ///   与源 FBX 的 (px, py, up) 相比恰好是 (-px, py, up)。
    ///
    /// 事实 3：因此**唯一的模型轴修正**是 (m.x, m.y, m.z) → (−m.x, m.z, m.y)：
    ///   它把 Unity 局部 (=−px, py, up) 还原成 world (=px, up, py)。
    ///   AxisProbe 实测该修正下 82 段端点与网格顶点的水平面最近距离最坏 0.261 mm（次优候选 640 mm）。
    ///   该变换是一个**纯旋转**（绕轴 (0,1,1)/√2 转 180°，行列式 +1），无镜像、无负缩放，
    ///   因此不会翻转面法线/绕序。
    ///
    /// 结论（三式互相自洽，Validator 用包围盒 + 最近顶点双重可判据断言）：
    ///   Unity 局部顶点 = (−px, py, up)
    ///   模型轴修正      = (−m.x, m.z, m.y)          [唯一的模型侧转换]
    ///   PhysicsToUnity  = (px, h, py)
    /// 注意 PhysicsToUnity 正是任务书/记忆里的规范约定 "(x, 0, y) 映射到三维"——X、Y 均无符号翻转。
    /// </summary>
    public static class PoolGeometry
    {
        // ── 唯一转换入口 ────────────────────────────────────────────────────

        /// <summary>
        /// 规范二维物理平面 → Unity 世界坐标（水平高度由参数给定）。
        /// **全工程唯一允许的 physics→world 映射**：Unity = (physics.x, height, physics.y)。
        /// 依据：模型轴修正为 (−m.x, m.z, m.y)，实测 82 段端点对齐最坏 0.261 mm。
        /// </summary>
        public static Vector3 PhysicsToUnity(Vector2 physics, float height)
        {
            return new Vector3(physics.x, height, physics.y);
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

        /// <summary>Unity 世界坐标 → 规范二维物理平面（指针拾取、调试回读用）。</summary>
        public static Vector2 UnityToPhysics(Vector3 world)
        {
            return new Vector2(world.x, world.z);
        }

        /// <summary>
        /// 唯一的模型轴修正：把 Unity 导入后的 FBX 局部顶点转换到 Unity 世界。
        /// (m.x, m.y, m.z) → (−m.x, m.z, m.y)。
        /// **这是唯一的模型侧转换**，不得在场景里再叠一次手动旋转。
        /// 等价于绕轴 (0,1,1)/√2 旋转 180°（见 <see cref="ModelAxisCorrection"/>）。
        /// </summary>
        public static Vector3 ModelVertexToUnity(Vector3 modelLocal)
        {
            return new Vector3(-modelLocal.x, modelLocal.z, modelLocal.y);
        }

        /// <summary>
        /// <see cref="ModelVertexToUnity"/> 的等价四元数形式（绕 (0,1,1)/√2 转 180°）。
        /// 供需要 Transform.rotation 的场合使用；与分量式函数结果一致（由测试断言）。
        /// </summary>
        public static readonly Quaternion ModelAxisCorrection =
            Quaternion.AngleAxis(180f, new Vector3(0f, 1f, 1f).normalized);

        // ── 参考常量（供 Validator 与测试做可判据断言）────────────────────────

        /// <summary>
        /// 规范球桌**外轮廓**包围盒（含库边外沿与桌腿），单位米，runtime3d 坐标系
        /// (X 长轴, Y 上, Z 短轴)。直接取自 table-metadata-reference.json 的 bounds_runtime。
        /// 用于校验导入网格未发生二次缩放/意外旋转。经唯一轴修正后，Unity 世界包围盒应与之相等。
        /// </summary>
        public static readonly Vector3 OuterBoundsMin = new Vector3(-1.412685751914978f, -0.8635167479515076f, -0.7976343631744385f);

        /// <summary>规范球桌外轮廓包围盒上界（见 OuterBoundsMin 说明）。</summary>
        public static readonly Vector3 OuterBoundsMax = new Vector3(1.412408709526062f, 0.06102215126156807f, 0.7976343631744385f);

        /// <summary>规范球桌外轮廓在长轴上的总长度，2.82509446144104 m。</summary>
        public const float OuterLength = 2.82509446144104f;

        /// <summary>规范球桌外轮廓在短轴上的总宽度，1.595268726348877 m。</summary>
        public const float OuterWidth = 1.595268726348877f;

        /// <summary>规范球桌外轮廓竖直总高度（桌腿底到库顶），0.9245388992130757 m。</summary>
        public const float OuterHeight = 0.9245388992130757f;

        /// <summary>
        /// 82 段库边**端点**的包围盒下界（物理平面 x/y，米）。注意这与 <see cref="OuterBoundsMin"/> 不同：
        /// 后者是含库边外沿与桌腿的网格外轮廓，前者只覆盖库边线段端点（不含袋口外的库边外沿）。
        /// 来源：table-metadata-reference.json 的 collision.segments 端点实测；
        /// 与独立复核 D2-T02A 给出的基线逐位一致。用于交叉核对轴向/缩放（非镜像判据：
        /// 端点 x 的左右不对称仅 7.6e-8 m，低于任何可用容差）。
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
