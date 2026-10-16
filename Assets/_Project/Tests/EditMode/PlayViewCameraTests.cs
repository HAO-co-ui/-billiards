using System.IO;
using Billiards.Core;
using Billiards.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **双取景（首页观景 / 打球上帝视角）的判据族**（编号续 `T133`：`T134`–`T135`）。
    ///
    /// 背景（用户实机反馈驱动）：本卡先把**唯一**的取景从 `play` 放宽到 `home`（"要能看见球房环境"），
    /// 随后用户反馈"这个视角开始游戏打球不太舒服，**点开始游戏就切换为上帝视角**"
    /// ⇒ 房间可见性改由**首页取景**承担、**打球回到 `play`** ⇒ 于是"一套取景"变成
    /// "**两套预设 + 运行期切换**"。真源 = <see cref="CameraPresets"/>（Runtime 程序集，
    /// 因为运行期组件读不到 Editor 的 `GreyboxSceneSetup`；后者反向派生）。
    ///
    /// · **`T134`**：① **两套预设必须真的不同**（防"两套写一样"的假实现 —— 一旦有人把 `Home` 直接写成
    ///   `Play`，**首页"看得见房间"就静默失效**，而 `CAM-*` / `T95` / `SCN-*` **没有一条会红**，
    ///   因为它们只测序列化态 = `play`）；② **场景序列化态必须是 `play`**（比对的是**运行期真源**
    ///   `CameraPresets.Play*`，不是 `GreyboxSceneSetup` 的同名别名 ⇒ **非恒真**：把别名改指 `home` 即红）。
    /// · **`T135`**：切换规则的**纯函数**（`Resolve(homeOpen)`）+ 施加动作（`ApplyPreset`）逐项断言。
    ///   之所以走纯函数而不是去改 `HomeView.IsOpen`：它是 `{ get; private set; }`，**测试改不动**
    ///   —— 这也正是把切换规则抽成纯函数的原因（可测）。
    ///
    /// **它为什么不是恒绿摆设**：把 `CameraPresets.HomePosition` 改成 `PlayPosition` ⇒ `T134①` 红；
    /// **把 `HomePosition`/`HomeLookTarget` 一起改成与 `Play` 相同、只留 `HomeFovDeg=53`**（复核方的注入 (i)-1）
    /// ⇒ `T134①` **也必须红** —— 本判据按**几何**判（位置距离与注视点距离都要 > 阈值），
    /// **fov 差不再能顶替**；这正是那次 CONCERN 的修法（旧口径下该注入下 ① 仍绿、只被 ② 兜住）。
    /// 把 `GreyboxSceneSetup.CameraPosition` 改指 `HomePosition` ⇒ `T134②` 红；
    /// 把 `Resolve` 的两个分支写反 ⇒ `T135` 红；把 `ApplyPreset` 写成 no-op ⇒ `T135` 的施加断言红。
    /// </summary>
    public class PlayViewCameraTests
    {
        private const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        private const float PosTol = 1e-4f;
        private const float FovTol = 1e-3f;

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? ".";

        /// <summary>
        /// **`T134`：两套预设必须真的不同 + 场景序列化态必须是 `play`。**
        /// </summary>
        [Test]
        public void T134_TwoPresetsDiffer_AndSerializedStateIsPlay()
        {
            // ① 两套预设必须**在几何上**真的不同（**逐项**给出差在哪，便于定位）
            //    ⚠ 判定口径 = **几何**：位置距离与注视点距离**都必须** > `MinPresetSeparationMetres`。
            //      **fov 差不算数** —— 复核方注入 (i)-1（把 Home 的位置/注视点改成与 Play 相同、只留 `HomeFovDeg=53`）
            //      实测：**旧口径下本断言仍绿**（旧实现把 fov 差也当成"不同"），只被下面的 ② 兜住
            //      ⇒ ① 单独看弱于它的标题。现已**移除 fov 子句** ⇒ 注入 (i)-1 **必须让 ① 自己红**。
            float posGap = Vector3.Distance(CameraPresets.PlayPosition, CameraPresets.HomePosition);
            float lookGap = Vector3.Distance(CameraPresets.PlayLookTarget, CameraPresets.HomeLookTarget);
            Assert.IsTrue(CameraPresets.PresetsDiffer(),
                "T134①：两套相机预设**在几何上不够不同**（位置距离与注视点距离都必须 > "
                + $"{CameraPresets.MinPresetSeparationMetres:R} m）⇒ 首页那套的\"看得见房间\"会静默失效，"
                + "而现有判据一条都不会红。"
                + $"实测：位置距离={posGap:R} m，注视点距离={lookGap:R} m。"
                + $"play=({CameraPresets.PlayPosition}, {CameraPresets.PlayLookTarget}, {CameraPresets.PlayFovDeg}°)，"
                + $"home=({CameraPresets.HomePosition}, {CameraPresets.HomeLookTarget}, {CameraPresets.HomeFovDeg}°)。");

            // ② 场景序列化态 = play（比对**运行期真源**，不是 Editor 侧别名 ⇒ 非恒真）
            string full = Path.Combine(ProjectRoot, MainScenePath);
            Assert.IsTrue(File.Exists(full), "T134②：Main 场景缺失：" + MainScenePath);

            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            try
            {
                Camera cam = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    cam = root.GetComponentInChildren<Camera>(true);
                    if (cam != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(cam, "T134②：Main 场景里找不到相机。");

                float posErr = Vector3.Distance(cam.transform.position, CameraPresets.PlayPosition);
                Assert.Less(posErr, PosTol,
                    $"T134②：场景相机位置 {cam.transform.position} ≠ play 预设 {CameraPresets.PlayPosition}"
                    + $"（err={posErr:R}）⇒ 序列化态不是 play 口径（`CAM-05` 要求两场景同为玩法口径）。");

                Assert.AreEqual(CameraPresets.PlayFovDeg, cam.fieldOfView, FovTol,
                    $"T134②：场景相机 fov={cam.fieldOfView:R} ≠ play 预设 {CameraPresets.PlayFovDeg:R}。");

                Vector3 wantForward = (CameraPresets.PlayLookTarget - CameraPresets.PlayPosition).normalized;
                float angErr = Vector3.Angle(cam.transform.forward, wantForward);
                Assert.Less(angErr, 0.05f,
                    $"T134②：场景相机朝向与 play 预设的视线夹角 {angErr:R}°（应 ≈0）。");

                // 反向保证：序列化态**不是** home（否则①②会被"两个别名都改成 home"一起蒙混过去）
                float homeErr = Vector3.Distance(cam.transform.position, CameraPresets.HomePosition);
                Assert.Greater(homeErr, PosTol,
                    "T134②：场景相机位置**等于 home 预设** ⇒ 序列化态被改成了首页口径，"
                    + "`CAM-04`（打球占比）与 `T95`（HUD 不压桌）的口径前提全部失效。");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// **`T135`：切换规则（纯函数）+ 施加动作 —— 首页开 ⇒ home，关 ⇒ play。**
        /// </summary>
        [Test]
        public void T135_SwitchRule_HomeOpenAppliesHome_ClosedAppliesPlay()
        {
            // ① 纯函数：首页开 ⇒ home；首页关（= 点了 Practice 开始打球）⇒ play
            Assert.AreEqual(CameraPresets.Preset.Home, CameraPresets.Resolve(true),
                "T135①：首页打开时应取 home 预设（看得见球房）。");
            Assert.AreEqual(CameraPresets.Preset.Play, CameraPresets.Resolve(false),
                "T135①：首页关闭（开始打球）时应取 play 预设（上帝视角）。");

            // ② 取位姿：两套各自取到自己的值（防"枚举对了但取值串了"）
            CameraPresets.GetPose(CameraPresets.Preset.Home, out Vector3 hp, out Vector3 hl, out float hf);
            Assert.AreEqual(CameraPresets.HomePosition, hp, "T135②：home 位姿取错。");
            Assert.AreEqual(CameraPresets.HomeLookTarget, hl, "T135②：home 注视点取错。");
            Assert.AreEqual(CameraPresets.HomeFovDeg, hf, FovTol, "T135②：home fov 取错。");

            CameraPresets.GetPose(CameraPresets.Preset.Play, out Vector3 pp, out Vector3 pl, out float pf);
            Assert.AreEqual(CameraPresets.PlayPosition, pp, "T135②：play 位姿取错。");
            Assert.AreEqual(CameraPresets.PlayLookTarget, pl, "T135②：play 注视点取错。");
            Assert.AreEqual(CameraPresets.PlayFovDeg, pf, FovTol, "T135②：play fov 取错。");

            // ③ 施加动作：组件真的把相机搬到该预设（防 ApplyPreset 写成 no-op / 搬错方向）
            var go = new GameObject("T135PlayViewCamera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                var view = go.AddComponent<PlayViewCamera>();
                view.targetCamera = cam;   // 显式接线（本测试不依赖 Awake 的执行时机）

                view.ApplyPreset(CameraPresets.Preset.Home);
                Assert.Less(Vector3.Distance(cam.transform.position, CameraPresets.HomePosition), PosTol,
                    $"T135③：ApplyPreset(Home) 后相机位置 {cam.transform.position} ≠ {CameraPresets.HomePosition}。");
                Assert.AreEqual(CameraPresets.HomeFovDeg, cam.fieldOfView, FovTol,
                    $"T135③：ApplyPreset(Home) 后 fov={cam.fieldOfView:R} ≠ {CameraPresets.HomeFovDeg:R}。");
                Vector3 homeForward = (CameraPresets.HomeLookTarget - CameraPresets.HomePosition).normalized;
                Assert.Less(Vector3.Angle(cam.transform.forward, homeForward), 0.05f,
                    "T135③：ApplyPreset(Home) 后相机朝向不对。");

                view.ApplyPreset(CameraPresets.Preset.Play);
                Assert.Less(Vector3.Distance(cam.transform.position, CameraPresets.PlayPosition), PosTol,
                    $"T135③：ApplyPreset(Play) 后相机位置 {cam.transform.position} ≠ {CameraPresets.PlayPosition}。");
                Assert.AreEqual(CameraPresets.PlayFovDeg, cam.fieldOfView, FovTol,
                    $"T135③：ApplyPreset(Play) 后 fov={cam.fieldOfView:R} ≠ {CameraPresets.PlayFovDeg:R}。");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
