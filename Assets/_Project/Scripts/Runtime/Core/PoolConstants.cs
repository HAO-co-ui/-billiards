using System;

namespace Billiards.Core
{
    /// <summary>
    /// 台球项目的单位制与几何硬指标。所有数值均来自美术交付包的实测数据，
    /// 数据源：F:\工作文件\idea35_台球\08_Gameplay_Markers\table-metadata-reference.json
    /// （该文件由美术侧对 D:\BaiduNetdiskDownload\Pool Table.fbx 做几何测量生成，
    ///   源文件 SHA256 = abc5b634683b7770d8830ae56acdc5cb430a3e7e1bbf10c67e8050073162f119）。
    ///
    /// 单位约定：1 Unity unit = 1 米。Unity 没有显式的单位设置，该约定由本类的常量
    /// 与 FBX 导入缩放（1:1）共同保证，并由 PoolConstantsTests 与
    /// TableIntegrityValidator 断言。
    ///
    /// 精度说明：统一使用 float。在米制尺度下 float 的相对精度约 1e-7，
    /// 即 1.27 m 处的绝对分辨率约 0.12 微米，远低于球半径 28.575 毫米所要求的精度。
    /// 因此 float 不会成为物理手感或几何对齐的瓶颈。
    /// （跨平台浮点确定性若在未来成为需求，再评估切换到 double 定长积分。）
    /// </summary>
    public static class PoolConstants
    {
        /// <summary>单位换算：1 个 Unity 单位等于 1 米。</summary>
        public const float MetresPerUnit = 1f;

        // ---- 硬指标：球 --------------------------------------------------------

        /// <summary>球半径，米。美式 8 球标准球直径 57.15 mm。</summary>
        public const float BallRadius = 0.028575f;

        /// <summary>球直径，米。</summary>
        public const float BallDiameter = BallRadius * 2f;

        /// <summary>
        /// 母球/目标球初速上界，8 m/s。依据策划卡「初速 2–8 m/s」的**上界**。
        /// **该值未经策划确认**：策划卡只给出区间，8 为取其上界作为物理内核的
        /// 子步数推导输入（见 PoolSimulator.DeriveSubSteps）。若策划最终确认另一下界，
        /// 只需改此常量，子步数会随之重新推导。
        /// </summary>
        public const float MaxBallSpeed = 8f;

        // ---- 硬指标：台面（内库内沿之间）---------------------------------------

        /// <summary>台面长轴内沿间距，2.54 m（9 尺台 100 英寸）。</summary>
        public const float ClothInnerLength = 2.54f;

        /// <summary>
        /// 台面短轴内沿间距，1.270108866 m。
        /// 注意：9 尺台标称短轴为 50 英寸 = 1.27 m，本值为美术源网格实测结果，
        /// 比标称值长 0.000108866 m（约 0.109 mm，相对偏差 0.0086%）。
        /// 采用实测值而非标称值，以保证物理碰撞轮廓与渲染网格严格对齐；
        /// 该偏差已在 PoolConstantsTests 中以显式容差断言记录。
        /// </summary>
        public const float ClothInnerWidth = 1.270108866f;

        /// <summary>台面长轴半长，1.27 m。</summary>
        public const float ClothHalfLength = 1.27f;

        /// <summary>台面短轴半宽，0.6350544330872985 m（= ClothInnerWidth / 2）。</summary>
        public const float ClothHalfWidth = 0.6350544330872985f;

        /// <summary>台呢所在平面高度，y = 0。全局几何与物理的基准平面。</summary>
        public const float ClothHeightY = 0f;

        /// <summary>库鼻相对台呢的高度，0.044982 m。仅用于渲染参考，物理取水平投影。</summary>
        public const float CushionNoseHeight = 0.04498204256884025f;

        /// <summary>桌腿最低点 y，-0.8635167479515076 m。房间地面需与此高度对齐。</summary>
        public const float TableLegBottomY = -0.8635167479515076f;

        // ---- 硬指标：库边与袋口 ------------------------------------------------

        /// <summary>网格派生的库边/袋角/弧段总数。</summary>
        public const int CushionSegmentCount = 82;

        /// <summary>球袋数量（4 角袋 + 2 中袋）。</summary>
        public const int PocketCount = 6;

        /// <summary>
        /// 袋口捕获判定圆半径，0.030741635767169012 m。
        /// 该值 = 袋口内投影片刻半径 - 球半径 - 2 mm 余量。
        /// 关键规则（不可省略）：仅当球心穿过袋喉、朝网方向运动后才判定捕获；
        /// 半径本身不足以判定进袋。
        /// </summary>
        public const float PocketCaptureRadius = 0.030741635767169012f;

        /// <summary>袋口内投影半径（未扣球半径与余量），0.06131663576716901 m。</summary>
        public const float PocketOpeningRadius = 0.06131663576716901f;

        // ---- 硬指标：球杆 -----------------------------------------------------

        /// <summary>球杆长度，1.4 m。模型 PoolCue_TipOrigin.fbx 的杆尖为局部原点，杆身沿 +Z。</summary>
        public const float CueLength = 1.4f;

        // ---- 表现层约定 -------------------------------------------------------

        /// <summary>
        /// "桌面扩大 20%" 的视觉缩放系数，仅作用于 PlayfieldRoot 的 Transform，
        /// 不改变物理运算的规范米制坐标。指针拾取需反向映射回物理坐标。
        /// </summary>
        public const float PlayfieldVisualScale = 1.2f;

        // ---- 溯源标识 ---------------------------------------------------------

        /// <summary>规范球桌网格三角面数，用于表导入校验（TableIntegrityValidator）。</summary>
        public const int SourceTableTriangles = 19954;

        /// <summary>规范球桌 FBX 的均匀缩放系数（导出时已应用）。</summary>
        public const float SourceTableUniformScale = 2.550047226954685f;
    }
}
