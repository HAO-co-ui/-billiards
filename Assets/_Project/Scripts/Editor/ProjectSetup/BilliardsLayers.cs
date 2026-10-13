namespace Billiards.EditorTools
{
    /// <summary>
    /// 本项目的 Layer 分配。Layer 的两个用途：
    /// 1) 相机剔除与渲染分层（如 AimGuide 可整体开关，不影响其他内容）；
    /// 2) 物理碰撞隔离（本项目自研二维定长步进物理，完全不用 PhysX，
    ///    因此把玩法层两两设为不碰撞，作为对误用 Rigidbody/Collider 的防御）。
    ///
    /// 关于 Tag：本项目**刻意不使用自定义 Tag**。物体身份由组件（如 BallView、
    /// PocketMarker）表达，具备类型安全、可静态检查、可随程序集重构。
    /// Tag 是字符串，拼错不会编译失败，不适用于本项目的关键路径判定。
    /// </summary>
    public static class BilliardsLayers
    {
        public const int Table = 6;
        public const int Ball = 7;
        public const int Cue = 8;
        public const int AimGuide = 9;
        public const int Vfx = 10;
        public const int Environment = 11;

        /// <summary>玩法层索引与其期望名称，顺序即应用顺序。</summary>
        public static readonly int[] Indices = { Table, Ball, Cue, AimGuide, Vfx, Environment };

        public static readonly string[] Names = { "Table", "Ball", "Cue", "AimGuide", "VFX", "Environment" };

        /// <summary>玩法层在所有项目 Layer 中占用的索引区间（含端点）。</summary>
        public const int FirstIndex = Table;
        public const int LastIndex = Environment;
    }
}
