using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// **相机取景的两套预设 + 选择规则（纯函数）** —— 本工程"相机口径"的**唯一真源**。
    ///
    /// ── 为什么真源在 `Billiards.Runtime` 而不是 `GreyboxSceneSetup` ──────────────────────
    /// 用户反馈"开始游戏打球不舒服，**点开始游戏就切换为上帝视角**" ⇒ 需要**运行期**按 UI 状态切相机。
    /// 运行期组件在 `Billiards.Runtime`，而 `GreyboxSceneSetup` 在 `Billiards.Editor`
    /// （`Billiards.Runtime.asmdef` **不引用** `Billiards.Editor`，方向不可逆）。
    /// ⇒ 数值只能放 **Runtime**，再由 `GreyboxSceneSetup` **反向派生**它的既有公开名
    /// （`CameraPosition` / `CameraLookTarget` / `CameraBaseVerticalFovDeg`）。
    /// 这样**仍然只有一处写数字**，且 Editor / 测试 / 验收器的既有引用**一个都不用改**。
    ///
    /// ── 两套预设 ────────────────────────────────────────────────────────────
    /// · **`Play`（上帝视角，打球用）**：`(0, 2.10, −0.75)` 注视 `(0, 0.02, 0.06)`、fov **52**。
    ///   这是本卡之前一直在用、也是用户此前实机打过的那套（**场景序列化态 = 本套**）。
    ///   `CAM-01..05` 与 `T95` 全部按本套度量（灰盒是**玩法**诊断场景，本就该是 play 口径）。
    /// · **`Home`（首页观景用）**：`(0, 2.10, +2.80)` 注视 `(0, 0.05, −1.00)`、fov **53**。
    ///   这是本卡为"**要能看见球房环境**"引入的那套（桌子占 61.6% 宽、房间 12/12 件入画）。
    ///
    /// ── 口径漂移史（**如实登记**，供后人判断"这两个数为什么长这样"）────────────────
    /// ① 本卡先是把**唯一**的取景从 play 放宽到 home（用户："要能看见球房环境"）；
    /// ② 随后用户实机反馈"打球时这个视角不舒服，**点开始就切上帝视角**" ⇒ 房间可见性改由
    ///    **首页取景**承担，**打球回到 play**；
    /// ⇒ 于是"一套取景"变成"两套 + 运行期切换"（见 <see cref="Billiards.UI.PlayViewCamera"/>）。
    ///   **结论**：两套都保留，各司其职 —— 不是"改错了又改回来"。
    /// </summary>
    public static class CameraPresets
    {
        /// <summary>打球的**基准垂直 FOV（度）**；同时是场景序列化 FOV。</summary>
        public const float PlayFovDeg = 52f;

        /// <summary>首页的**基准垂直 FOV（度）**。</summary>
        public const float HomeFovDeg = 53f;

        /// <summary>打球（上帝视角）：相机世界位置。</summary>
        public static readonly Vector3 PlayPosition = new Vector3(0f, 2.10f, -0.75f);

        /// <summary>打球（上帝视角）：注视点。</summary>
        public static readonly Vector3 PlayLookTarget = new Vector3(0f, 0.02f, 0.06f);

        /// <summary>首页：相机世界位置。</summary>
        public static readonly Vector3 HomePosition = new Vector3(0f, 2.10f, 2.80f);

        /// <summary>首页：注视点。</summary>
        public static readonly Vector3 HomeLookTarget = new Vector3(0f, 0.05f, -1.00f);

        /// <summary>
        /// **两套预设"算不同"的最小几何间隔（米）** —— `T134①` 的判据阈值。
        ///
        /// 取 **0.25 m**：远大于浮点噪声与"手滑改一位小数"的量级，又远小于实际间隔
        /// （实测位置距离 **3.550 m**、注视点距离 **1.061 m** ⇒ 余量分别 **14×** 与 **4.2×**）。
        /// 语义 = "**相机确实换了地方/换了朝向**"，而不是"某个数值字段不相等"。
        /// </summary>
        public const float MinPresetSeparationMetres = 0.25f;

        /// <summary>预设标识。**显式枚举**（不用 bool/魔法字符串）⇒ 切换判据可读可测。</summary>
        public enum Preset
        {
            /// <summary>上帝视角，打球用。</summary>
            Play = 0,

            /// <summary>首页观景用。</summary>
            Home = 1,
        }

        /// <summary>
        /// **纯函数**：给定"首页是否打开"⇒ 该用哪套预设。
        ///
        /// 抽成纯函数是为了让"切换行为"**可在 EditMode 直接断言**（不必起场景、不必跑帧）——
        /// 否则"两套写一样"或"切反了"这类**假实现**只能靠肉眼在实机上发现。
        /// </summary>
        public static Preset Resolve(bool homeOpen)
        {
            return homeOpen ? Preset.Home : Preset.Play;
        }

        /// <summary>取某套预设的位姿（位置 / 注视点 / 垂直 FOV）。</summary>
        public static void GetPose(Preset preset, out Vector3 position, out Vector3 lookTarget, out float fovDeg)
        {
            if (preset == Preset.Home)
            {
                position = HomePosition;
                lookTarget = HomeLookTarget;
                fovDeg = HomeFovDeg;
                return;
            }

            position = PlayPosition;
            lookTarget = PlayLookTarget;
            fovDeg = PlayFovDeg;
        }

        /// <summary>
        /// **把某套预设施加到一台相机上** —— 运行期"施加预设"的实现点。
        ///
        /// ── ⚠ **已知重复（如实登记，不抽共享函数）** ────────────────────────────────────
        /// 构建期的两个渲染工具**不调用**本方法，而是各自有一份**等价实现**：
        /// · `MaterialVisualValidator.SetBaseFov/RestoreBaseFov`（证据图渲染路径）
        /// · `RoomEnvironmentSetup.SetBaseFov`（房间预览渲染工具）
        /// （本方法只被运行期组件 <see cref="Billiards.UI.PlayViewCamera"/> 调用。）
        /// **三处同遵循同一不变量**：**先改 `fitter.baseVerticalFovDeg`，再 `Apply()`**。
        /// **为什么不抽共享函数**：那要同时动 Runtime 与 Editor 两条链，且必须**重跑 `validators`
        /// 证明渲染输出逐字节未变**（等于再刷一次入库证据）—— 消一处重复的收益不值这个代价。
        /// ⇒ **将来若再改这条链路，必须三处同改**（规格 §12.7 已登记）。
        ///
        /// ⚠ **必须同时更新 `CameraViewportFitter` 的基准 fov**：适配器只在 `OnEnable` 与**宽高比变化**时
        /// 写 `Camera.fieldOfView`，取值由它自己的 `baseVerticalFovDeg` 决定。若只改相机 fov、不改适配器基准，
        /// 那么窗口比例一变，适配器就会用**旧基准**把 fov 覆写回去（"看着像生效了、换个窗口就弹回"）。
        /// </summary>
        public static void ApplyTo(Camera camera, Preset preset)
        {
            if (camera == null)
            {
                return;
            }

            GetPose(preset, out Vector3 position, out Vector3 lookTarget, out float fovDeg);

            Transform t = camera.transform;
            t.position = position;
            t.rotation = Quaternion.LookRotation(lookTarget - position, Vector3.up);

            var fitter = camera.GetComponent<Billiards.Physics.CameraViewportFitter>();
            if (fitter != null)
            {
                fitter.baseVerticalFovDeg = fovDeg;   // 先改基准，再让适配器按当前宽高比落 fov
                fitter.Apply();
            }
            else
            {
                camera.fieldOfView = fovDeg;
            }
        }

        /// <summary>
        /// **两套预设"在几何上"是否真的不同**（`T134①` 的判据实现，抽出来供测试与运行期共用）。
        ///
        /// ── 判定口径 = **几何**（复核方 CONCERN 后加严）─────────────────────────────────
        /// `Play` 与 `Home` 的**位置距离**与**注视点距离****都必须 > <see cref="MinPresetSeparationMetres"/>**。
        /// ⚠ **fov 差不算数**：fov 单独不同**不能**让两套预设成为"两个取景"。
        ///   复核方注入 **(i)-1**（把 `HomePosition`/`HomeLookTarget` 改成与 `Play` 相同、只留
        ///   `HomeFovDeg = 53`）实测：**旧口径下本判据仍绿**（因为旧实现把 fov 差也算作"不同"），
        ///   只是**被 `T134②`（场景序列化态 ≠ home）兜住** ⇒ ① 单独看**弱于它的标题**。
        ///   ⇒ 本实现**移除 fov 子句**：改坏位置/注视点，① **必须自己红**，不再依赖 ② 兜底。
        ///
        /// 为什么需要这条：若有一天有人图省事把 home 直接写成 play，**首页"看得见房间"就静默失效**，
        /// 而 `CAM-*` / `T95` / `SCN-*` **没有一条会红**（它们只测序列化态 = play）。
        /// </summary>
        public static bool PresetsDiffer()
        {
            return Vector3.Distance(PlayPosition, HomePosition) > MinPresetSeparationMetres
                   && Vector3.Distance(PlayLookTarget, HomeLookTarget) > MinPresetSeparationMetres;
        }
    }
}
