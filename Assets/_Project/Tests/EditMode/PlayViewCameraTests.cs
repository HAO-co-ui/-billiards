using System.IO;
using Billiards.Core;
using Billiards.Physics;
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
    /// · **`T136`**：转场 ——
    ///   ① **纯函数**：权重 `w(t)`（`w(0)=0`/`w(1)=1`/`w(0.5)=0.5`/单调不减/∈[0,1]/两端速度为 0）；
    ///   ② **纯函数**：`Interpolate` 在 `t=0` 逐位等于起点、`t=1` **逐位等于目标预设**（不是容差）、
    ///      `t∈(0,1)` 单调不回头且不越出两端点的包围盒；
    ///   ③ **纯函数**：`ShouldAnimate` —— **首帧不做动画**、**状态未变不做动画**（二者合起来 ⇒ **冒烟全程不做过渡**）、
    ///      状态真变了才做；
    ///   ④ **状态机**：真组件上"首帧硬切 / 状态变化起转场 / 推进到时长后**逐位**落到目标预设"。
    ///
    /// **它们为什么不是恒绿摆设**：
    /// · `T134①`：把 `HomePosition` 改成 `PlayPosition` ⇒ 红；**把 `HomePosition`/`HomeLookTarget` 一起改成与
    ///   `Play` 相同、只留 `HomeFovDeg=53`**（复核方的注入 (i)-1）⇒ **也必须红** —— 本判据按**几何**判
    ///   （位置距离与注视点距离都要 > 阈值），**fov 差不再能顶替**；这正是那次 CONCERN 的修法。
    /// · `T134②`：把 `GreyboxSceneSetup.CameraPosition` 改指 `HomePosition` ⇒ 红。
    /// · `T135`：把 `Resolve` 的两个分支写反 ⇒ 红；把 `ApplyPreset` 写成 no-op ⇒ 施加断言红。
    /// · `T136`：删掉 `Interpolate` 的 `t ≥ 1` 分支（改成一直插值）⇒ ② 红（终态有残差）；
    ///   `Weight` 改成线性（`return t`）⇒ ① 的两端速度断言红；`ShouldAnimate` 去掉 `hasApplied` 判断
    ///   ⇒ ③ 的首帧断言红；把 `Tick` 的终态改成"用插值结果"⇒ ④ 的逐位断言红。
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

        /// <summary>
        /// **`T136`：转场（用户反馈"视角转场要丝滑"）** —— 纯函数三层 + 状态机一层。
        /// 断言清单见类注释；**关键是 `t = 1` 必须逐位落到目标预设**（不许插值到 99% 就停，
        /// 因为 `CAM-01..05` 判的是**稳态**）。
        /// </summary>
        [Test]
        public void T136_Transition_Smoothstep_ExactEndpoints_NoAnimationWhenUnchanged()
        {
            // ── ① 权重（smoothstep）：端点、中点、单调、值域、两端速度为 0 ──
            Assert.AreEqual(0f, CameraTransition.Weight(0f), "T136①：w(0) 应为 0。");
            Assert.AreEqual(1f, CameraTransition.Weight(1f), "T136①：w(1) 应为 1。");
            Assert.AreEqual(0.5f, CameraTransition.Weight(0.5f), 1e-6f, "T136①：w(0.5) 应为 0.5（smoothstep 对称）。");
            Assert.AreEqual(0f, CameraTransition.Weight(-3f), "T136①：越界 t 应被 clamp 到 0。");
            Assert.AreEqual(1f, CameraTransition.Weight(7f), "T136①：越界 t 应被 clamp 到 1。");

            float[] w = CameraTransition.SampleWeights(101);
            for (int i = 0; i < w.Length; i++)
            {
                Assert.GreaterOrEqual(w[i], 0f, $"T136①：w[{i}] 越下界。");
                Assert.LessOrEqual(w[i], 1f, $"T136①：w[{i}] 越上界。");
                if (i > 0)
                {
                    Assert.GreaterOrEqual(w[i] + 1e-7f, w[i - 1],
                        $"T136①：权重非单调（w[{i - 1}]={w[i - 1]:R} > w[{i}]={w[i]:R}）⇒ 插值会「回头」。");
                }
            }

            // 两端速度为 0（smoothstep 的定义性质；线性缓动在这里会红）
            const float h = 1e-3f;
            float slopeStart = (CameraTransition.Weight(h) - CameraTransition.Weight(0f)) / h;
            float slopeEnd = (CameraTransition.Weight(1f) - CameraTransition.Weight(1f - h)) / h;
            Assert.Less(Mathf.Abs(slopeStart), 0.02f, $"T136①：起点速度应 ≈0（实测 {slopeStart:R}）⇒ 起步不应突兀。");
            Assert.Less(Mathf.Abs(slopeEnd), 0.02f, $"T136①：终点速度应 ≈0（实测 {slopeEnd:R}）⇒ 收尾不应突兀。");

            // ── ② 插值：端点**逐位**、中途单调不回头、不越界 ──
            Vector3 fromPos = CameraPresets.HomePosition;
            Vector3 fromLook = CameraPresets.HomeLookTarget;
            float fromFov = CameraPresets.HomeFovDeg;
            Vector3 toPos = CameraPresets.PlayPosition;
            Vector3 toLook = CameraPresets.PlayLookTarget;
            float toFov = CameraPresets.PlayFovDeg;

            CameraTransition.Interpolate(fromPos, fromLook, fromFov, toPos, toLook, toFov, 0f,
                out Vector3 p0, out Vector3 l0, out float f0);
            Assert.IsTrue(Exact(p0, fromPos) && Exact(l0, fromLook) && f0.ToString("R") == fromFov.ToString("R"),
                "T136②：t=0 必须**逐位**等于起点。");

            CameraTransition.Interpolate(fromPos, fromLook, fromFov, toPos, toLook, toFov, 1f,
                out Vector3 p1, out Vector3 l1, out float f1);
            Assert.IsTrue(Exact(p1, toPos) && Exact(l1, toLook) && f1.ToString("R") == toFov.ToString("R"),
                $"T136②：t=1 必须**逐位**等于目标预设（实测 pos={p1}, fov={f1:R}）"
                + " —— 不许「插值到 99% 就停」（`CAM-01..05` 判的是稳态）。");

            // 中途：单调靠近目标 + 不越出两端点的包围盒
            float prevDist = float.MaxValue;
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                CameraTransition.Interpolate(fromPos, fromLook, fromFov, toPos, toLook, toFov, t,
                    out Vector3 p, out _, out float fov);
                float dist = Vector3.Distance(p, toPos);
                Assert.LessOrEqual(dist, prevDist + 1e-5f,
                    $"T136②：t={t:R} 处距目标反而变远（{dist:R} > {prevDist:R}）⇒ 插值回头了。");
                prevDist = dist;

                Assert.IsTrue(InBox(p, fromPos, toPos), $"T136②：t={t:R} 处位置越出两端点包围盒：{p}。");
                Assert.IsTrue(fov >= Mathf.Min(fromFov, toFov) - 1e-4f && fov <= Mathf.Max(fromFov, toFov) + 1e-4f,
                    $"T136②：t={t:R} 处 fov {fov:R} 越出 [{Mathf.Min(fromFov, toFov):R},{Mathf.Max(fromFov, toFov):R}]。");
            }

            // ── ③ ShouldAnimate：首帧不动画 / 状态未变不动画（⇒ 冒烟全程不做过渡）/ 真变了才动 ──
            Assert.IsFalse(CameraTransition.ShouldAnimate(false, CameraPresets.Preset.Play, CameraPresets.Preset.Home),
                "T136③：**首帧**（尚未施放过）必须硬切 —— 从「场景序列化态」插值会让开机第一帧像「相机飞过来」。");
            Assert.IsFalse(CameraTransition.ShouldAnimate(true, CameraPresets.Preset.Play, CameraPresets.Preset.Play),
                "T136③：**状态未变**必须硬切（不做无谓动画）。");
            Assert.IsFalse(CameraTransition.ShouldAnimate(true, CameraPresets.Preset.Home, CameraPresets.Preset.Home),
                "T136③：状态未变（home 侧同理）必须硬切。");
            Assert.IsTrue(CameraTransition.ShouldAnimate(true, CameraPresets.Preset.Home, CameraPresets.Preset.Play),
                "T136③：首页→打球必须走转场（本卡唯一要接的方向）。");
            // ⇒ 冒烟不做过渡的**证明**：冒烟全程无人操作 + 首页被抑制（homeStartup=suppressed）
            //   ⇒ `HomeView.IsOpen` 恒为 false ⇒ 解出预设恒为 play ⇒ `from == to` ⇒ ③ 这条恒 false。

            // ── ④ 状态机（真组件）：首帧硬切 / 状态变化起转场 / 推进满时长后逐位落到目标 ──
            var go = new GameObject("T136PlayViewCamera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                var view = go.AddComponent<PlayViewCamera>();
                view.targetCamera = cam;
                view.transitionSeconds = CameraTransition.DefaultSeconds;

                bool homeOpen = false;
                view.homeOpenSource = () => homeOpen;

                // 首帧：硬切到 play（此时 homeOpen=false），且**不在**转场中
                view.Apply(0f);
                Assert.IsFalse(view.IsTransitioning, "T136④：首帧必须硬切（不得进入转场）。");
                Assert.IsTrue(Exact(cam.transform.position, CameraPresets.PlayPosition),
                    $"T136④：首帧后相机应在 play 位（实测 {cam.transform.position}）。");

                // 切到 home（硬切，用于把起点摆到 home）
                view.ApplyPreset(CameraPresets.Preset.Home);
                Assert.IsTrue(Exact(cam.transform.position, CameraPresets.HomePosition),
                    "T136④：ApplyPreset(Home) 应硬切到 home。");

                // 状态变化 ⇒ 起转场
                homeOpen = false;
                view.Apply(0f);
                Assert.IsTrue(view.IsTransitioning, "T136④：home→play 应**起一段转场**（而不是硬切）。");

                // 推进：中途应在两端点之间（不在任一端点上必然成立的"贴哪边"）
                view.Apply(CameraTransition.DefaultSeconds * 0.5f);
                Assert.IsTrue(view.IsTransitioning, "T136④：半程时应仍在转场中。");
                float halfDist = Vector3.Distance(cam.transform.position, CameraPresets.PlayPosition);
                Assert.Greater(halfDist, 1e-3f, "T136④：半程不应已经贴到终点（否则等于硬切）。");

                // 推进满 ⇒ **逐位**落到 play，且转场结束
                view.Apply(CameraTransition.DefaultSeconds);
                Assert.IsFalse(view.IsTransitioning, "T136④：推进满时长后转场应结束。");
                Assert.IsTrue(Exact(cam.transform.position, CameraPresets.PlayPosition),
                    $"T136④：转场终态必须**逐位**等于 play 预设（实测 {cam.transform.position}）。");
                Assert.AreEqual(CameraPresets.PlayFovDeg.ToString("R"), cam.fieldOfView.ToString("R"),
                    "T136④：转场终态 fov 必须逐位等于 play 预设。");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// **`T137`：转场期间不许出现瞄准表现（瞄准线 / 幽灵球 / 球杆）** —— 用户实机反馈"转场过程中瞄准线会出来乱转"。
        ///
        /// 根因：瞄准线/幽灵球/球杆都是**相机相关的投影**，而转场期间相机在扫过一段路径
        /// ⇒ 只要不门控，它们就会跟着扫。三处**同一个根因** ⇒ 走**同一个**门
        /// （`Billiards.Core.CameraTransition.IsActive`，由相机侧 `PlayViewCamera` 发布、单向）。
        ///
        /// 断言四组：
        ///  ① **瞄准门**（纯函数）：转场中 ⇒ 不显示；转场结束 ⇒ 恢复；
        ///  ② **球杆门**（纯函数，同形）：同上，且原有两条（流程口径 / 白球在场上）不受影响；
        ///  ③ **生命周期**：起转场 ⇒ 门**立刻**关上；推进到**半程**（`t < 1`）**仍关着**
        ///     （"没到 `t ≥ 1` 不许恢复"，否则最后几帧又会闪一下）；推满 ⇒ 放开**且**相机逐位落在预设；
        ///  ④ **不误伤稳态 + 兜底真的被考到**：首帧硬切后门是**开**的（否则正常打球时瞄准线会一直不显示）；
        ///     并且**在"门还开着"的时刻**考 `OnDisable` 的兜底 —— 分两段：
        ///     **① 功能**：起转场到半程（门确实关着）后调 `ReleaseTransitionGate()` ⇒ 门必须放开；
        ///     **② 接线**（白盒文本断言，与 `T72b` 读内核源码同法）：`OnDisable` 体里**必须出现该调用**。
        ///     ⚠ **修法登记（复核方查出旧写法恒真）**：旧写法在 `Apply(DefaultSeconds)` **之后**销毁，
        ///     而那一刻门**已被 `ApplyPreset` 放开** ⇒ 断言 `IsActive == false` **必然成立**、**与兜底无关**
        ///     ⇒ 删掉 `OnDisable` 里那行**也不会红**（"绿而空"的无判据守卫）。
        ///     ⚠ **实测坑（本判据前两版都踩了）**：**EditMode 不触发 `OnDisable`** ——
        ///     `DestroyImmediate(GameObject)` 与 `enabled = false` **两种方式都不触发**
        ///     （甚至**带着正确兜底也红**：`IsTransitioning` 仍为 true）⇒ **纯运行期断言在本环境守不住 `OnDisable`**。
        ///     ⇒ 故"接线"这一半必须用**白盒文本断言**（这是**环境限制**，不是放松：
        ///     删掉那行调用，②**必红**）。
        ///
        /// **怎么打死它**：删掉 `ShouldShowAimAssist` 的 `!transitionActive` 项 ⇒ ① 红；
        /// 删掉 `ShouldShowStick` 的同项 ⇒ ② 红；把 `PlayViewCamera` 的 `Publish(false)` 从落终态挪到半程
        /// ⇒ ③ 的"半程仍关着"红；把 `Publish(true)` 去掉 ⇒ ③ 的"起转场立刻关上"红；
        /// 把 `ReleaseTransitionGate()` 的**实现**改成 no-op ⇒ ④① 红；
        /// **删掉 `OnDisable` 里的 `ReleaseTransitionGate()` 调用 ⇒ ④② 红**（待按此**实测注入**验证）。
        /// </summary>
        [Test]
        public void T137_Transition_HidesAimVisuals_UntilLanded()
        {
            // ── ① 瞄准门（纯函数）──
            Assert.IsFalse(AimAssistController.ShouldShowAimAssist(true, true, true, false, true),
                "T137①：**转场中**必须不显示瞄准辅助（否则线会跟着相机扫 —— 用户实机反馈的\"乱转\"）。");
            Assert.IsTrue(AimAssistController.ShouldShowAimAssist(true, true, true, false, false),
                "T137①：**转场结束**（门放开）后应恢复显示（四条原本都成立）。");
            Assert.IsFalse(AimAssistController.ShouldShowAimAssist(false, true, true, false, false),
                "T137①：转场门前**原有的四条**不得被削弱（预测开关关闭仍应为 false）。");
            Assert.IsFalse(AimAssistController.ShouldShowAimAssist(true, true, true, true, false),
                "T137①：模态让位那条也不得被削弱（结算卡/首页打开仍应为 false）。");

            // ── ② 球杆门（纯函数，与瞄准门同形）──
            Assert.IsFalse(CueStickController.ShouldShowStick(true, true, true),
                "T137②：**转场中**球杆也必须隐藏（同一个根因，不能只堵瞄准线一个洞）。");
            Assert.IsTrue(CueStickController.ShouldShowStick(true, true, false),
                "T137②：转场结束后球杆恢复（流程口径与白球在场两条都成立）。");
            Assert.IsFalse(CueStickController.ShouldShowStick(false, true, false),
                "T137②：流程口径（Moving/Settle 隐藏）不得被削弱。");
            Assert.IsFalse(CueStickController.ShouldShowStick(true, false, false),
                "T137②：白球不在场上 ⇒ 杆仍须隐藏（否则会停在袋口残留位置）。");

            // ── ③ / ④ 生命周期（真组件）──
            var go = new GameObject("T137PlayViewCamera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                var view = go.AddComponent<PlayViewCamera>();
                view.targetCamera = cam;
                view.transitionSeconds = CameraTransition.DefaultSeconds;

                bool homeOpen = true;                 // 从首页开始（相机在 home）
                view.homeOpenSource = () => homeOpen;

                view.Apply(0f);                        // 首帧：硬切到 home
                Assert.IsFalse(CameraTransition.IsActive,
                    "T137④：首帧硬切**不是**转场 ⇒ 门必须是开的（否则正常打球时瞄准线一直不显示）。");

                homeOpen = false;                      // 点开始 ⇒ 开始转场
                view.Apply(0f);
                Assert.IsTrue(CameraTransition.IsActive,
                    "T137③：起转场必须**立刻**把关上（否则转场最初几帧线还在画）。");

                // 半程：仍必须**关着**（"没到 t≥1 不许恢复"）
                view.Apply(CameraTransition.DefaultSeconds * 0.5f);
                Assert.IsTrue(view.IsTransitioning, "T137③：半程应仍在转场中。");
                Assert.IsTrue(CameraTransition.IsActive,
                    "T137③：**半程仍须关着** —— 提前恢复会让最后几帧又闪一下（这是这类门最典型的漏法）。");

                // 推满：放开 + 相机逐位落在预设
                view.Apply(CameraTransition.DefaultSeconds);
                Assert.IsFalse(view.IsTransitioning, "T137③：推满时长后转场应结束。");
                Assert.IsFalse(CameraTransition.IsActive,
                    "T137③：**落终态那一刻**才放松门（`PlayViewCamera.ApplyPreset` 里 `Publish(false)`）。");
                Assert.IsTrue(Exact(cam.transform.position, CameraPresets.PlayPosition),
                    $"T137③：落终态后相机应逐位等于 play 预设（实测 {cam.transform.position}）。");

                // ── ④ 兜底（`OnDisable` 那条）**必须有人守**，但 EditMode **够不到它** ─────────────
                // ⚠ **修法登记（复核方查出旧写法恒真）**：旧写法在 `Apply(DefaultSeconds)` **之后**销毁，
                //   而那一刻门**已被 `ApplyPreset` 放开** ⇒ 断言 `IsActive == false` **必然成立**、**与兜底无关**
                //   ⇒ 删掉兜底那行**也不会红**（"绿而空"的无判据守卫）。
                // ⚠ **实测坑（本判据前两版都因此红）**：EditMode **不触发 `OnDisable`** ——
                //   `DestroyImmediate(GameObject)` 与 `enabled = false` **两种方式都不触发**
                //   （甚至**带着正确兜底也红**）⇒ **纯运行期断言在本环境不可能守住 `OnDisable`**。
                // ⇒ 最终改法 = **① 功能断言**（直接调 `ReleaseTransitionGate()`，且**门此刻确实关着** ⇒ 不恒真）
                //   **+ ② 白盒文本断言**（`OnDisable` 的方法体里必须出现该调用；与 `T72b` 读内核源码同法）
                //   ⇒ **删掉那行调用，本条必红**（这正是复核方注入 (ii) 要打的那一行）。
                var go2 = new GameObject("T137PlayViewCameraTeardown");
                try
                {
                    Camera cam2 = go2.AddComponent<Camera>();
                    var view2 = go2.AddComponent<PlayViewCamera>();
                    view2.targetCamera = cam2;
                    view2.transitionSeconds = CameraTransition.DefaultSeconds;

                    bool open2 = true;
                    view2.homeOpenSource = () => open2;

                    view2.Apply(0f);                                        // 首帧硬切到 home（门是开的）
                    open2 = false;
                    view2.Apply(0f);                                        // 起转场 ⇒ 门关上
                    view2.Apply(CameraTransition.DefaultSeconds * 0.5f);    // 半程 ⇒ 门**仍关着**
                    Assert.IsTrue(view2.IsTransitioning, "T137④：前提 —— 应仍在转场中。");
                    Assert.IsTrue(CameraTransition.IsActive,
                        "T137④：前提 —— 此时门应仍**关着**（否则下面的断言又会变成恒真）。");

                    view2.ReleaseTransitionGate();                          // ① 功能：兜底动作必须开门
                    Assert.IsFalse(CameraTransition.IsActive,
                        "T137④①：`ReleaseTransitionGate()` 必须把门放开 —— 否则静态状态会漏到下一个场景、"
                        + "瞄准线从此再也不显示。");
                }
                finally
                {
                    if (go2 != null)
                    {
                        Object.DestroyImmediate(go2);
                    }
                }

                // ② 接线（白盒文本断言，与 `T72b` 读内核源码同法）：`OnDisable` 必须调用兜底
                string viewSrc = File.ReadAllText(Path.Combine(ProjectRoot, PlayViewCameraSourcePath));
                string stripped = StripLineComments(viewSrc);
                int odIdx = stripped.IndexOf("private void OnDisable()");
                Assert.Greater(odIdx, 0, "T137④②：`PlayViewCamera` 里找不到 `OnDisable` 方法。");
                int ob = stripped.IndexOf('{', odIdx);
                int oe = stripped.IndexOf('}', ob);
                Assert.Greater(oe, ob, "T137④②：`OnDisable` 方法体解析失败。");
                string odBody = stripped.Substring(ob, oe - ob + 1);
                string odOneLine = odBody.Replace("\r", " ").Replace("\n", " ").Trim();
                Assert.IsTrue(odBody.Contains("ReleaseTransitionGate()"),
                    "T137④②：`OnDisable` **必须**调用 `ReleaseTransitionGate()` —— 删掉它 ⇒ 兜底无人守 ⇒ 本条必红。"
                    + $"（实测 `OnDisable` 体：{odOneLine}）");
            }
            finally
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        /// <summary>`PlayViewCamera` 的源码路径（`T137④②` 白盒文本断言用）。</summary>
        private const string PlayViewCameraSourcePath = "Assets/_Project/Scripts/Runtime/UI/PlayViewCamera.cs";

        /// <summary>逐行剥掉 `//` 之后的注释（与 `T72b` 的"读源码剔注释"同法）。</summary>
        private static string StripLineComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>逐位相等（浮点用 `R` 往返串比较，避免 `Vector3.==` 的近似语义）。</summary>
        private static bool Exact(Vector3 a, Vector3 b)
        {
            return a.x.ToString("R") == b.x.ToString("R")
                   && a.y.ToString("R") == b.y.ToString("R")
                   && a.z.ToString("R") == b.z.ToString("R");
        }

        /// <summary>`p` 是否落在 `a`/`b` 两个端点构成的轴对齐包围盒内（含容差）。</summary>
        private static bool InBox(Vector3 p, Vector3 a, Vector3 b)
        {
            const float tol = 1e-4f;
            return p.x >= Mathf.Min(a.x, b.x) - tol && p.x <= Mathf.Max(a.x, b.x) + tol
                   && p.y >= Mathf.Min(a.y, b.y) - tol && p.y <= Mathf.Max(a.y, b.y) + tol
                   && p.z >= Mathf.Min(a.z, b.z) - tol && p.z <= Mathf.Max(a.z, b.z) + tol;
        }
    }
}
