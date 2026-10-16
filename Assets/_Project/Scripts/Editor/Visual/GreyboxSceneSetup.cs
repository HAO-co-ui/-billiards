using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using Billiards.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 场景的**幂等**构建器（配置即代码，与工程配置/数据导入/材质视觉同一哲学）。承担**两个场景**：
    ///
    /// <list type="number">
    /// <item><b>灰盒诊断场景</b> <c>Assets/_Project/Scenes/PhysicsGreybox.unity</c>（<see cref="BuildScene"/>）：
    /// 从空场景**全量生成** —— 球桌 + 球 + 杆 + 全部玩法宿主 + 相机 + 光。保留为**诊断场景**
    /// （`Gizmo` / 调试叠加层 / 直接看内核行为），**不再是出包入口**。</item>
    /// <item><b>正式游戏入口</b> <c>Assets/_Project/Scenes/Main.unity</c>（<see cref="AttachMainGameplayLayer"/>）：
    /// 在**既有的** <c>Main.unity</c>（材质视觉场景：桌 / 地面 / 相机 / 光）之上**叠加玩法层** ——
    /// 复用既有 <c>PlayfieldRoot</c> / 相机 / 光（**不重建它们、不新增第二台相机或第二个 AudioListener**），
    /// 只补"玩得起来"所需的宿主与球可视物。</item>
    /// </list>
    ///
    /// ── 口径变更（**旧说法已作废**，勿再引用）──────────────────────────────────
    /// 本类原先写有"提交态 build list 不含本场景 + 出包入口三重保护"两条 —— 那是在
    /// **"`Main.unity` 是空壳"**这一前提下成立的旧口径。**该前提已被证伪**：`Main.unity` 现在
    /// 是带完整玩法层的**正式入口** ⇒
    ///  · 出包入口（<see cref="PerformanceSetup"/> / <see cref="BuildWindows"/>）指向 **`Main.unity`**；
    ///  · `SCN-02`（工程配置）与 `D4-BLD-01`（物理验收）断言的 build list `== [Main.unity]`
    ///    因此**变成真正正确**，不再与出包口径互斥（互斥的根因就是"Main 是空壳"，已消除）。
    /// （铁律 17：结论被证伪后必须清除**所有**引用处，含代码注释。）
    ///
    /// ── Main 与灰盒的两处**刻意不同**（登记）──────────────────────────────────
    ///  1. **可视缩放**：`Main.unity` 的 `PlayfieldRoot.localScale` 保持 **1**（材质视觉验收 `SCN-02`
    ///     钉死为 1，且 `SCN-04` 的世界包围盒按规范尺度比对）⇒ Main 里**不应用** `PlayfieldVisualScale`
    ///     （1.2），游戏以规范尺度渲染；灰盒才用 1.2。**不得**为"看起来一样"把 Main 的根缩放改成 1.2
    ///     （那会同时打红 `SCN-02` 与 `SCN-04`）。
    ///  2. **相机**：`Main.unity` **复用既有相机**（`Main Camera`，位置/FOV 由材质视觉验收 `CAM-01` 钉死），
    ///     而非灰盒那台（fov 52 的俯视相机）—— 避免出现两台相机 / 两个 `AudioListener`。
    ///
    /// 另：本文件只生成、**不校验**（生成/校验入口分离，R2）；校验见 <see cref="PhysicsValidator"/> /
    /// <see cref="MaterialVisualValidator"/>。场景内**不得出现任何碰撞体/刚体**（自研物理铁律 R5-1，
    /// 白球由 <c>GameObject.CreatePrimitive</c> 生成后立即移除其自带的碰撞体）。
    /// </summary>
    public static class GreyboxSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        public const string ScenesDir = "Assets/_Project/Scenes";

        /// <summary>**正式游戏入口场景**（材质视觉场景 + 玩法层）。出包入口与 build list 都指向它。</summary>
        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

        /// <summary>
        /// `Main.unity` 里**玩法层根**的对象名。同时充当**幂等标记**：场景里已有该对象 ⇒
        /// 认为玩法层已就位，<see cref="ApplyMainScene"/> 直接跳过（不重写、不重复添加宿主）。
        /// </summary>
        public const string MainGameplayRootName = "GameplayLayer";

        /// <summary>球堆可视球的名字前缀（`RackBall_i`）；重建时按前缀回收旧球。</summary>
        public const string RackBallObjectName = "RackBall_";
        public const string TableConfigPath = "Assets/_Project/Data/TableConfig.asset";
        public const string CueBallMaterialPath = "Assets/_Project/Materials/Balls/Ball_00.mat";

        /// <summary>
        /// 球模型（`Art/Models/Balls/Ball_NN.fbx`）：**球心为本地原点、半径 0.028575 m、单位缩放**
        /// （与 `PoolConstants.BallRadius` 逐位一致 ⇒ 实例化后**不需要再缩放**）。
        /// </summary>
        public const string BallModelPathFormat = "Assets/_Project/Art/Models/Balls/Ball_{0:00}.fbx";

        /// <summary>球材质（`Materials/Balls/Ball_NN.mat`，URP Lit，**逐号独立 1024×512 贴图**，无 atlas）。</summary>
        public const string BallMaterialPathFormat = "Assets/_Project/Materials/Balls/Ball_{0:00}.mat";

        /// <summary>
        /// 球杆模型：长轴沿 **−Y**、杆尖在局部原点、长 **1.400000 m** —— 由离线探针
        /// `Docs/Tools/fbx_axis_probe.py` 直接解析 FBX 二进制量得（绕过 Unity）：
        /// 原始局部 bbox `min=(-0.016148, -1.400000, -0.015760)`、`max=(0.015415, -0.000000, 0.016188)`
        /// ⇒ 各轴尺寸 `X=0.0316 / Y=1.4000 / Z=0.0319` m。
        ///
        /// ⚠ **美术包 README 写的是"杆身沿 +Z"** —— 那是 **Blender 源工程（Z-up）** 的口径；
        /// FBX 导出已做 Z-up → Y-up 转换 ⇒ 进 Unity 后长轴变成 **Y**，且杆身朝 **−Y**。
        /// 实测证据：判据 `T123` 首版按 README 直接断言 min.z/长 Z ⇒ 报
        /// 「杆长 0.0319 m ≠ 1.4 m、横向 1.4 m」，即**长轴不在 Z 上**。
        /// ⇒ 需要一层修正旋转把 **−Y 映到 +Z**，才能满足 <see cref="Physics.CueStickController"/>
        /// 的既有约定（杆尖=原点、杆身 +Z）。**不修改控制器**（它的坐标系是被多处证据钉住的）。
        /// </summary>
        public const string CueModelPath = "Assets/_Project/Art/Models/Cue/PoolCue_TipOrigin.fbx";

        /// <summary>杆模型实例的对象名（判据 `T123` 按名定位）。</summary>
        public const string CueModelObjectName = "CueModel";

        /// <summary>
        /// **杆根节点**对象名 —— 它是交给 <see cref="Physics.CueStickController.stickInstance"/> 的那一层，
        /// 由控制器每帧写 `position/rotation`（世界）。模型作为它的**子物体**并带一层修正旋转
        /// （<see cref="CueModelLocalRotation"/>）⇒ 控制器写父、几何约定由子提供，两者互不覆盖。
        ///
        /// 为什么必须多一层：控制器写的是 `_stick.rotation`（**绝对**旋转），
        /// 若把修正旋转直接放在 `stickInstance` 上，会被每帧覆写掉（等于没修）。
        /// </summary>
        public const string CueStickRootObjectName = "CueStickRoot";

        /// <summary>
        /// 杆模型的**修正旋转**：把网格的 **−Y**（杆身方向）映到杆根局部空间的 **+Z**。
        ///
        /// 推导（不是试出来的）：绕 X 轴转 θ 把 (0,−1,0) 映到 (0,−cosθ,−sinθ)；
        /// 要求等于 (0,0,1) ⇒ cosθ=0 且 −sinθ=1 ⇒ **θ = −90°** ⇒ `Quaternion.Euler(-90,0,0)`。
        /// 旋转后原本的 X→X、Z→Y ⇒ 横向尺寸仍是 31.6 mm / 31.9 mm（球杆尾径 ≈ 32 mm，合理）。
        /// </summary>
        public static readonly Quaternion CueModelLocalRotation = Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>
        /// 美术包给定的**号面朝向**（原型斜俯视相机的来向，见 `Balls/Editable/README.md`）。
        /// 模型自带朝向已按它摆好 ⇒ 本工程只需把这一朝向**旋到本工程相机方向**（见 <see cref="BallFaceRotation"/>）。
        /// </summary>
        public static readonly Vector3 ArtBallFaceDirection = new Vector3(3.55f, 2.65f, 3.55f);

        public const string RootObjectName = "PhysicsGreybox";
        public const string GizmoObjectName = "CushionGizmo";
        public const string DriveObjectName = "PhysicsTestDrive";
        public const string CueBallObjectName = "CueBall";

        /// <summary>
        /// **可视野缩放根**（表现层等比缩放 <see cref="PoolConstants.PlayfieldVisualScale"/>）。
        /// 球可视物挂其下；相机 / 光 / Gizmo / 驱动器**留在场景根下**（否则会被一起缩放）。
        /// </summary>
        public const string PlayfieldRootObjectName = "PlayfieldRoot";

        /// <summary>指针瞄准控制器宿主对象名（交互胶水；挂在 `PhysicsGreybox` 根下，不进缩放根）。</summary>
        public const string PointerAimControllerObjectName = "PointerAimController";
        public const string PointerPowerControllerObjectName = "PointerPowerController";
        public const string CueStickControllerObjectName = "CueStick";
        public const string PoolAudioObjectName = "PoolAudio";
        public const string PoolVfxObjectName = "PoolVfx";

        /// <summary>最小 HUD 宿主对象名（与其它表现层同层：`PhysicsGreybox` 根下，**不进缩放根**）。</summary>
        public const string PracticeHudObjectName = "PracticeHud";

        /// <summary>练习结算卡宿主对象名。</summary>
        public const string PracticeResultObjectName = "PracticeResult";

        /// <summary>**开始界面（首页）**宿主对象名（挂在 `PhysicsGreybox` 根下，不进缩放根）。</summary>
        public const string HomeObjectName = "Home";

        /// <summary>`EventSystem` 宿主对象名（结算卡按钮的射线入口）。</summary>
        public const string EventSystemObjectName = "EventSystem";

        /// <summary>瞄准辅助宿主对象名（预测线控制器 + 视图；挂在 `PhysicsGreybox` 根下，不进缩放根）。</summary>
        public const string AimAssistObjectName = "AimAssist";

        // ── 主相机取景（**HUD 布局期望值的前提**）──────────────────────────────
        // 球桌在屏幕上的投影包围盒完全由这三项 + 可视野缩放决定；`PracticeHudLayout` 的三块分区
        // 就是照着该投影的实测空余带宽摆的（见 `Docs/35-A_Day13_最小HUD_spec.md` §3.3）。
        // 提成公开常量 + `BuildCamera` 直接引用 ⇒ 相机取景与 HUD 布局**不可能各自漂移**；
        // 判据 `T95 (f)` 再从**已提交场景**回读校验一次（防"场景被手改、代码没跟上"）。
        //
        // ── 取景口径变更（登记"谁裁定 / 为什么 / 前后值 / 旧意图"）────────────────────────
        // **谁裁定**：用户（2026-10-16，经主理人转达）。
        // **为什么**：旧机位 `(0, 2.10, −0.75)` 朝 **+Z**、俯角 68.7°、离桌 2.25 m，画面只覆盖
        //   桌远库外约 **0.6 m**；而参考球房的装饰（后墙/墙裙/长凳/盆栽/球杆架/边桌/灯/相框）
        //   **全在 −Z 一侧** ⇒ 后墙整片落在相机**背后**，**12 件道具的包围盒 8 个角全部在视锥外**
        //   （离线逐角视锥投影实测，判据 `CAM-*` 与 `day3-top-view.png` 自洽）。
        //   用户裁定"**要能看见球房环境**"⇒ 相机移到桌子 **+Z 侧**并放宽取景。
        // **前后值**：pos `(0,2.10,−0.75)` → **`(0,2.10,+2.80)`**；look `(0,0.02,0.06)` → **`(0,0.05,−1.00)`**；
        //   fov `52` → **`53`**。实测（aspect 1.6 + `CameraViewportFitter` 抬后的有效 fov）：
        //   打球区横向占比 `0.900` → **`0.616`**；外框裁切 `3.9%` → `4.7%`（仍 ≤ `CAM-03` 的 8%）；
        //   房间可见件 `1/12` → **`12/12`**。
        // **旧意图**：桌子**充满屏幕**（`CAM-04` "防缩成中间一张小桌"）。
        //   ⇒ 该意图**保留**，只把量化下限随口径重定（`ClothWidthFractionMin` 0.85 → 0.50，见验收器）。
        // **代价（如实）**：桌子在画面里变小（0.900 → 0.616）且下移到 `y≤0.46` —— 这是"看得见房间"的必然取舍。
        // ⚠ 改这三项 ⇒ **`T95 (d)` 的三组派生期望值必须同步重新推导**（见 `PracticeHudTests` 的对应注释）。

        /// <summary>主相机世界位置。</summary>
        public static readonly Vector3 CameraPosition = new Vector3(0f, 2.10f, 2.80f);

        /// <summary>主相机注视点。</summary>
        public static readonly Vector3 CameraLookTarget = new Vector3(0f, 0.05f, -1.00f);

        /// <summary>基准垂直 FOV（度）；同时是场景序列化 FOV（`CameraViewportFitter` 从相机取 ⇒ 二者不可能漂移）。</summary>
        public const float CameraBaseVerticalFovDeg = 53f;

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Visual/Build Physics Greybox Scene", false, 402)]
        public static void ApplyInteractive()
        {
            Apply();
            Debug.Log("灰盒物理场景可用：" + ScenePath);
            EditorUtility.DisplayDialog("Billiards Physics Greybox Setup",
                "灰盒物理场景已就绪（幂等：已存在则不重写）：\n\n" + ScenePath
                + "\n\n注意：本场景刻意**不**注册进 Build Settings（见 spec §8 H-6）。", "OK");
        }

        [MenuItem("Billiards/Visual/Rebuild Physics Greybox Scene (force overwrite)", false, 403)]
        public static void RebuildInteractive()
        {
            Rebuild();
            Debug.Log("灰盒物理场景已强制重建：" + ScenePath);
            EditorUtility.DisplayDialog("Billiards Physics Greybox Setup",
                "灰盒物理场景已**强制重建**（覆盖）：\n\n" + ScenePath, "OK");
        }

        [MenuItem("Billiards/Visual/Attach Gameplay Layer To Main Scene", false, 404)]
        public static void ApplyMainInteractive()
        {
            ApplyMainScene();
            Debug.Log("Main 场景玩法层可用：" + MainScenePath);
            EditorUtility.DisplayDialog("Billiards Main Scene Gameplay Layer",
                "正式入口 Main.unity 的玩法层已就绪（幂等：已存在则不重写）：\n\n" + MainScenePath
                + "\n\n本场景是**出包入口**（build list 与 BuildWindows 都指向它）。", "OK");
        }

        [MenuItem("Billiards/Visual/Rebuild Main Scene Gameplay Layer (force overwrite)", false, 405)]
        public static void RebuildMainInteractive()
        {
            RebuildMainScene();
            Debug.Log("Main 场景玩法层已强制重建：" + MainScenePath);
            EditorUtility.DisplayDialog("Billiards Main Scene Gameplay Layer",
                "正式入口 Main.unity 的玩法层已**强制重建**（覆盖）：\n\n" + MainScenePath, "OK");
        }

        /// <summary>
        /// **幂等**构建：产物已存在则**不重写**（no-op）。
        ///
        /// 为什么不是"每次无条件重建"：Unity 保存场景时为运行期创建的每个对象分配**随机会话内的
        /// fileID**（`m_LocalIdentfierInFile`）并按该 ID 排序落盘 ⇒ 同一份代码重建出的 `.unity`
        /// **逐字节必然不同**（实测两次重建 sha1 不同，而结构与数值完全一致）。
        /// 因此唯一在本工程可判定的"幂等"是：**已存在即不改写**，重复运行 sha1 不变。
        /// 这也与工程标准 R5-4「产物缺失才生成，存在则只校验」一致 —— 场景是否仍然正确由
        /// <see cref="PhysicsValidator"/> 判定，不由生成器偷偷修复。
        ///
        /// 需要强制重建（改了生成逻辑时）请用 <see cref="Rebuild"/>。
        /// </summary>
        public static void Apply()
        {
            AssetDatabase.Refresh();

            if (ProjectPaths.Exists(ScenePath))
            {
                Debug.Log("DAY4_SETUP_SKIP（幂等：产物已存在，不重写）scene=" + ScenePath);
                return;
            }

            Rebuild();
        }

        /// <summary>强制重建：从空场景重新生成并覆盖。仅在生成逻辑变更时使用。</summary>
        public static void Rebuild()
        {
            AssetDatabase.Refresh();
            BuildScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>供 <c>-executeMethod</c> 使用（幂等路径）；退出码 0 = 成功。</summary>
        public static void BuildFromCommandLine()
        {
            RunFromCommandLine(Apply, ScenePath);
        }

        /// <summary>供 <c>-executeMethod</c> 使用（强制重建路径）；退出码 0 = 成功。</summary>
        public static void RebuildFromCommandLine()
        {
            RunFromCommandLine(Rebuild, ScenePath);
        }

        /// <summary>供 <c>-executeMethod</c> 使用（Main 玩法层，幂等路径）；退出码 0 = 成功。</summary>
        public static void ApplyMainFromCommandLine()
        {
            RunFromCommandLine(ApplyMainScene, MainScenePath);
        }

        /// <summary>供 <c>-executeMethod</c> 使用（Main 玩法层，强制重建路径）；退出码 0 = 成功。</summary>
        public static void RebuildMainFromCommandLine()
        {
            RunFromCommandLine(RebuildMainScene, MainScenePath);
        }

        /// <summary>
        /// **幂等**给 `Main.unity` 叠加玩法层：场景里已有玩法层根（<see cref="MainGameplayRootName"/>）⇒
        /// **早退、不写盘**（重复执行 sha1 不变 —— 本工程既有硬要求）。
        /// </summary>
        public static void ApplyMainScene()
        {
            AssetDatabase.Refresh();

            if (MainHasGameplayLayer())
            {
                Debug.Log("MAIN_SETUP_SKIP（幂等：玩法层已存在，不重写）scene=" + MainScenePath);
                return;
            }

            RebuildMainScene();
        }

        /// <summary>
        /// **强制重建** Main 的玩法层：打开既有 `Main.unity` → 回收旧的玩法层（根对象 + 球可视物）→
        /// 重新叠加 → 保存。**保留** Main 既有的桌 / 地面 / 相机 / 光（不重建它们）。
        /// </summary>
        public static void RebuildMainScene()
        {
            AssetDatabase.Refresh();

            if (!ProjectPaths.Exists(MainScenePath))
            {
                throw new InvalidOperationException(
                    "找不到 Main 场景：" + MainScenePath + "（应由 MaterialVisualSetup 生成基础视觉场景）。");
            }

            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            AttachMainGameplayLayer(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 场景文件文本里是否已含**玩法层根**（=<see cref="MainGameplayRootName"/> 的 `m_Name`）。
        /// 读盘（不是内存场景）⇒ 与 `-batchmode` 幂等判据同口径。
        /// </summary>
        public static bool MainHasGameplayLayer()
        {
            if (!ProjectPaths.Exists(MainScenePath))
            {
                return false;
            }

            string text = File.ReadAllText(ProjectPaths.ToFullPath(MainScenePath));
            return text.Contains("m_Name: " + MainGameplayRootName);
        }

        private static void RunFromCommandLine(Action build, string scenePath)
        {
            int exitCode = 0;
            try
            {
                build();
                Debug.Log("DAY4_SETUP_OK scene=" + scenePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("场景构建失败（" + scenePath + "）：" + ex);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // ── 场景 ───────────────────────────────────────────────────────────

        private static void BuildScene()
        {
            Directory.CreateDirectory(ProjectPaths.ToFullPath(ScenesDir));

            TableConfig config = AssetDatabase.LoadAssetAtPath<TableConfig>(TableConfigPath);
            if (config == null)
            {
                throw new InvalidOperationException("找不到 TableConfig 资产：" + TableConfigPath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(RootObjectName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            // 0) **可视野缩放根**（表现层 1.2）。物理仍是**规范米制**；这里只放大"看得见的东西"。
            //    唯一真相 = 该 Transform 的 localScale；显示↔规范映射的唯一函数是 `PlayfieldView`。
            //    ⚠ 相机 / 光 / Gizmo / 驱动器**不进这个根** —— 否则相机会被放大、取景全错。
            var playfieldRoot = new GameObject(PlayfieldRootObjectName);
            playfieldRoot.transform.SetParent(root.transform, false);
            playfieldRoot.transform.localPosition = Vector3.zero;
            playfieldRoot.transform.localRotation = Quaternion.identity;
            playfieldRoot.transform.localScale = Vector3.one * PoolConstants.PlayfieldVisualScale;

            // 0b) **球桌可视物 + 最小布景**（用户实机反馈"场景全黑、看不到球袋在哪"后补）。
            //     ⚠ 此前灰盒**从未建过桌模型** —— 库边全靠 `CushionGizmo` 的 `Gizmos.DrawLine`，
            //     而 **`Gizmos` 只在编辑器 Scene 视图可见**，Game 视图里就是黑底加几颗球。
            //     现在走与 `Main` 场景**同一个** `TableVisualBuilder`（唯一实现点，铁律 16）。
            //     桌与地面**都挂可视根**（×1.2）：桌腿底与地面才能同时被缩放、保持齐平；
            //     若把地面留在未缩放的根下，桌子会**浮在地面上方** 0.2× 桌腿高。
            TableVisualBuilder.AttachTable(playfieldRoot.transform);
            TableVisualBuilder.AttachRoom(playfieldRoot.transform);

            // 1) 白球（物理平面 footSpot → **规范世界**，唯一转换点 PoolGeometry）
            //    ⚠ 挂到**缩放根**下：写入的是**局部坐标**（= 规范世界坐标），世界坐标会自动 ×1.2。
            GameObject cueBall = BuildCueBall();
            cueBall.transform.SetParent(playfieldRoot.transform, false);
            cueBall.transform.localPosition = PoolGeometry.PhysicsBallToUnity(config.footSpot);

            // 2) 俯视相机（含视口适配器）+ 主平行光（灰盒可见即可；不引入任何物理组件）
            //    ⚠ **在玩法层之前建**：瞄准辅助 / 拉杆需要相机引用（与 Main 的叠加口径一致）。
            Camera cam = BuildCamera(root);
            BuildLight(root);

            // 3) **球堆可见性** —— 按 `RackPositions` 生成同数量的球体并接上 `ballVisuals`，
            //     使 Game 视图能看见散球（`D7-T02` 阻塞项 ②）；**纯可视，无任何物理组件**。
            //     球与白球同挂**缩放根**下（见 `BuildRackVisuals`）。
            Transform[] ballVisuals = BuildRackVisuals(playfieldRoot, config, cueBall);

            // 4) **玩法层**（CushionGizmo / 驱动器 / 交互 / 音频 / 特效 / HUD / 结算 / 瞄准辅助）。
            //    ⚠ 与 `Main.unity` 的叠加走**同一个** `BuildGameplayHosts`（唯一实现点，铁律 16）——
            //    两个场景各写一份宿主接线必然分叉。
            //    灰盒是**诊断场景** ⇒ 调试叠加层**开**（`showDebugOverlay = true`）。
            BuildGameplayHosts(root, cam, playfieldRoot.transform, config, cueBall, ballVisuals,
                PhysicsTestDrive.RackBallCount, debugOverlay: true);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// **玩法层宿主的唯一构建点**（`PhysicsGreybox` 与 `Main.unity` 共用）。
        ///
        /// 在 <paramref name="root"/> 下逐层创建：`CushionGizmo` → `PhysicsTestDrive`（接线 tableConfig /
        /// tuning / 球可视物）→ `PointerAimController` / `PointerPowerController` / `CueStick` →
        /// `PoolAudio` / `PoolVfx` / `PracticeHud` / `PracticeResult` / `Home` / `EventSystem` / `AimAssist`。
        ///
        /// <para><b>调用方负责</b>：<paramref name="playfieldRoot"/>（可视缩放根）、<paramref name="cueBall"/>、
        /// <paramref name="ballVisuals"/> 已经建好并挂在该根下 —— 因为两个场景的"基础可视层"来源不同
        /// （灰盒全量生成；Main 复用既有的桌 / 相机 / 光）。</para>
        ///
        /// <param name="debugOverlay">是否让驱动器默认绘制调试叠加层：灰盒 = <c>true</c>（诊断），Main = <c>false</c>（正式）。</param>
        /// </summary>
        private static void BuildGameplayHosts(GameObject root, Camera cam, Transform playfieldRoot, TableConfig config,
            GameObject cueBall, Transform[] ballVisuals, int rackBallCount, bool debugOverlay)
        {
            // ─ CushionGizmo 宿主（引用 TableConfig 绘制 82 段库边；绘制坐标过显示层）──
            var gizmoGo = new GameObject(GizmoObjectName);
            gizmoGo.transform.SetParent(root.transform, false);
            CushionGizmo gizmo = gizmoGo.AddComponent<CushionGizmo>();
            gizmo.tableConfig = config;
            // Gizmo 宿主本身不进缩放根（`Gizmos.*` 用世界坐标，父节点缩放对它无效），
            // 但**绘制坐标**必须过显示层 ⇒ 接线 `playfieldRoot` 让 CushionGizmo 走 `PlayfieldView` 映射。
            gizmo.playfieldRoot = playfieldRoot;

            // ─ PhysicsTestDrive 宿主 ──
            var driveGo = new GameObject(DriveObjectName);
            driveGo.transform.SetParent(root.transform, false);
            PhysicsTestDrive drive = driveGo.AddComponent<PhysicsTestDrive>();
            drive.tableConfig = config;
            drive.cushionGizmo = gizmo;
            drive.ballVisual = cueBall.transform;
            drive.ballVisuals = ballVisuals;
            drive.rackBallCount = rackBallCount;
            drive.showDebugOverlay = debugOverlay;

            // **把 `PhysicsTuning` 资产接进场景** —— 让"唯一可调入口"名副其实；
            // 否则运行时恒为 null ⇒ 走回落路径 ⇒ 用户不知道自己在测哪套参数（`D7-T02` 阻塞项 ①）。
            // `T41` 守这条接线（把 tuning 置空 ⇒ FAIL）。
            drive.tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(PhysicsTuningSetup.AssetPath);

            // ─ 指针瞄准控制器（读输入 → 纯状态机 → 只写 drive.aimDegrees）──
            var aimGo = new GameObject(PointerAimControllerObjectName);
            aimGo.transform.SetParent(root.transform, false);
            var aimController = aimGo.AddComponent<PointerAimController>();
            aimController.aimCamera = cam;
            aimController.playfieldRoot = playfieldRoot;
            aimController.drive = drive;

            // ─ 拉杆力度控制器（右键拖 = 蓄力 1:1、松手 = 出杆）──
            var powerGo = new GameObject(PointerPowerControllerObjectName);
            powerGo.transform.SetParent(root.transform, false);
            var powerController = powerGo.AddComponent<PointerPowerController>();
            powerController.drive = drive;
            powerController.aimCamera = cam;
            powerController.playfieldRoot = playfieldRoot;

            // ─ 球杆控制器（击球流程状态机的唯一驱动者 + 杆程序动画）──
            var cueStickGo = new GameObject(CueStickControllerObjectName);
            cueStickGo.transform.SetParent(root.transform, false);
            var cueStick = cueStickGo.AddComponent<CueStickController>();
            cueStick.drive = drive;
            cueStick.pull = powerController;
            powerController.cueStick = cueStick;

            // 杆用美术包真模型（杆尖=局部原点、杆身 +Z、长 1.4 m ⇒ 与控制器约定逐字一致）；
            // 直接挂为控制器的子物体、位姿全交给控制器；模型缺失则留空 ⇒ 控制器自建基元兜底。
            cueStick.stickInstance = BuildCueModel(cueStickGo.transform);

            // ─ 音频层（内核接触事件流 → 撞声；strike 轮询出杆计数；纯函数调制 + 限流）──
            var audioGo = new GameObject(PoolAudioObjectName);
            audioGo.transform.SetParent(root.transform, false);
            var poolAudio = audioGo.AddComponent<PoolAudio>();
            poolAudio.drive = drive;
            poolAudio.strikeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/strike.wav");
            poolAudio.hitClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/hit.wav");
            poolAudio.railClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/rail.wav");
            poolAudio.pocketClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/pocket.wav");
            poolAudio.bgmClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/BGM/late-set.wav");

            // ─ 特效层（计数增量门 + 状态推导；池化、可开关；不消费接触事件流）──
            var vfxGo = new GameObject(PoolVfxObjectName);
            vfxGo.transform.SetParent(root.transform, false);
            var vfxView = vfxGo.AddComponent<PoolVfxView>();
            vfxView.drive = drive;
            vfxView.tableConfig = config;
            vfxView.playfieldRoot = playfieldRoot;

            // ─ 最小 HUD（几何唯一来源 = `PracticeHudLayout.Resolve()`；数据全取自既有只读出口）──
            var hudGo = new GameObject(PracticeHudObjectName);
            hudGo.transform.SetParent(root.transform, false);
            var hud = hudGo.AddComponent<PracticeHudView>();
            hud.drive = drive;
            hud.cueStick = cueStick;
            hud.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudAssetBuilder.TitleFontAssetPath);
            hud.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudAssetBuilder.BodyFontAssetPath);

            // 美术资源接线：界面精灵（13 图标 + 5 形状）与 16 个球号贴片 —— **必须全部在场**，
            // 任一缺失就在这里抛（构建期拦截 > 运行期看见空白，铁律 18）；两个开关写**已存在的宿主**。
            hud.uiSprites = LoadUiSpritesOrThrow();
            hud.ballChips = LoadBallChipsOrThrow();
            hud.vfxView = vfxView;
            hud.poolAudio = poolAudio;

            // ─ 结算卡 + 模态交互所需的最小 UI 基础设施 ──
            // `EventSystem` + `StandaloneInputModule`（**旧输入**）—— 与本工程既有输入口径一致。
            var eventSystemGo = new GameObject(EventSystemObjectName);
            eventSystemGo.transform.SetParent(root.transform, false);
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            var resultGo = new GameObject(PracticeResultObjectName);
            resultGo.transform.SetParent(root.transform, false);
            var resultView = resultGo.AddComponent<PracticeResultView>();
            resultView.drive = drive;
            resultView.hud = hud;

            // ─ 首页（开始界面）──
            // 启动先显示（`HomeView.shownOnStart`），关闭后 HUD 与可玩性才可用。
            // `HomeView.IsOpen` 是**第二个**模态判据点（结算卡是第一个），三个输入控制器都读它；
            // 首页自身的显隐与 HUD 三块分区的让位都在视图里做（不在生成器里，保持生成器只负责接线）。
            var homeGo = new GameObject(HomeObjectName);
            homeGo.transform.SetParent(root.transform, false);
            var homeView = homeGo.AddComponent<HomeView>();
            homeView.hud = hud;
            homeView.vfxView = vfxView;
            homeView.poolAudio = poolAudio;

            // ─ 瞄准辅助（预测线）：控制器（胶水）+ 视图（绘制过显示层单点）──
            var assistGo = new GameObject(AimAssistObjectName);
            assistGo.transform.SetParent(root.transform, false);
            var assistController = assistGo.AddComponent<AimAssistController>();
            assistController.drive = drive;
            assistController.aimCamera = cam;
            assistController.playfieldRoot = playfieldRoot;

            var assistView = assistGo.AddComponent<AimAssistView>();
            assistView.controller = assistController;
            assistView.playfieldRoot = playfieldRoot;
        }

        // ── Main 场景：把玩法层叠加到既有视觉场景上 ──────────────────────────

        /// <summary>
        /// **把玩法层叠加到 `Main.unity`**（既有桌 / 地面 / 相机 / 光**原样复用**，不重建它们）。
        ///
        /// 调用方须已把 <paramref name="scene"/> 载入为当前活动场景（`OpenScene(Single)` 或 `NewScene`）。
        /// 本方法依次：① 复用既有 `PlayfieldRoot` 与人形相机；② 给相机**缺失才补** `AudioListener` /
        /// `CameraViewportFitter`（**绝不新增第二台相机 / 第二个监听器**）；③ 回收并重建球可视物
        /// （挂既有 `PlayfieldRoot` 下）；④ 在 <see cref="MainGameplayRootName"/> 根下建玩法宿主
        /// （调试叠加层**关**，正式入口不显示诊断文本）。
        /// </summary>
        public static void AttachMainGameplayLayer(Scene scene)
        {
            TableConfig config = AssetDatabase.LoadAssetAtPath<TableConfig>(TableConfigPath);
            if (config == null)
            {
                throw new InvalidOperationException("找不到 TableConfig 资产：" + TableConfigPath);
            }

            GameObject playfieldRootGo = FindRoot(scene, PlayfieldRootObjectName);
            if (playfieldRootGo == null)
            {
                throw new InvalidOperationException(
                    "Main 场景缺 " + PlayfieldRootObjectName + "（应由 MaterialVisualSetup 生成基础视觉场景）。");
            }

            GameObject camGo = FindRoot(scene, "Main Camera");
            Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (cam == null)
            {
                throw new InvalidOperationException("Main 场景缺主相机（名为「Main Camera」的 Camera 组件）。");
            }

            // 回收旧玩法层（幂等重建）：先删玩法层根，再删旧球可视物。**不动桌 / 地面 / 相机 / 光**。
            DestroyImmediateIfExists(FindRoot(scene, MainGameplayRootName));
            RemovePlayfieldBallChildren(playfieldRootGo.transform);

            // **场景唯一 AudioListener**：复用相机上已有的监听器；缺失才补（不得出现两个 ⇒ `T91` 同族）。
            if (camGo.GetComponent<AudioListener>() == null)
            {
                camGo.AddComponent<AudioListener>();
            }

            // **视口适配器**（与灰盒同一口径）：缺失才补；`baseVerticalFovDeg` 取**序列化 FOV** ⇒ 二者不漂移。
            CameraViewportFitter fitter = camGo.GetComponent<CameraViewportFitter>();
            if (fitter == null)
            {
                fitter = camGo.AddComponent<CameraViewportFitter>();
                fitter.targetCamera = cam;
                fitter.baseVerticalFovDeg = cam.fieldOfView;
            }

            // 白球 + 球堆可视物：挂**既有 PlayfieldRoot** 下（写入**局部规范坐标**；该根在 Main 里 scale = 1）。
            GameObject cueBall = BuildCueBall();
            cueBall.transform.SetParent(playfieldRootGo.transform, false);
            cueBall.transform.localPosition = PoolGeometry.PhysicsBallToUnity(config.footSpot);
            Transform[] ballVisuals = BuildRackVisuals(playfieldRootGo, config, cueBall);

            var root = new GameObject(MainGameplayRootName);
            MoveToScene(root, scene);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            BuildGameplayHosts(root, cam, playfieldRootGo.transform, config, cueBall, ballVisuals,
                PhysicsTestDrive.RackBallCount, debugOverlay: false);
        }

        /// <summary>在场景**根**对象里按名查对象（不做递归，Main 的桌 / 相机 / 光都是根对象）。</summary>
        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == name)
                {
                    return go;
                }
            }

            return null;
        }

        /// <summary>把对象移入指定场景（若已在其中则 no-op）。保证"叠加层落在 Main 里"而非偶然的活动场景。</summary>
        private static void MoveToScene(GameObject go, Scene scene)
        {
            if (go.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(go, scene);
            }
        }

        private static void DestroyImmediateIfExists(GameObject go)
        {
            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>回收既有 `PlayfieldRoot` 下的**球可视物**（白球 + `RackBall_*`），供重建；**不动桌 / 地面**。</summary>
        private static void RemovePlayfieldBallChildren(Transform playfieldRoot)
        {
            var doomed = new List<GameObject>();
            for (int i = playfieldRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = playfieldRoot.GetChild(i);
                if (child.name == CueBallObjectName
                    || child.name.StartsWith(RackBallObjectName, StringComparison.Ordinal))
                {
                    doomed.Add(child.gameObject);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(doomed[i]);
            }
        }

        /// <summary>白球（0 号）：美术包模型 + `Ball_00.mat`。</summary>
        private static GameObject BuildCueBall()
        {
            return BuildBallObject(CueBallObjectName, 0);
        }

        /// <summary>
        /// 载入一个界面精灵；**首次取不到就强制重导一次再取**。
        ///
        /// 为什么需要重导：PNG 是刚刚由脚本生成并落进 `Assets/` 的，
        /// 若编辑器在导入设置（`UiSpriteImportPostprocessor`）生效**之前**就已经扫过一次目录，
        /// 该文件会以默认设置（`textureType = Default`，**不是 Sprite**）入库 ⇒
        /// `LoadAssetAtPath&lt;Sprite&gt;` 恒为 null，且**不会自己变好**。
        /// 强制重导一次即可让后处理器重新决定导入设置；仍失败才是真的缺资源。
        /// </summary>
        private static Sprite LoadSpriteWithRetry(string assetPath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
            {
                return sprite;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>
        /// 按 <see cref="ClubUiSprites"/> 的清单**逐格**载入界面精灵；
        /// 任一格缺失或名字对不上就**抛异常**（构建期失败），不返回半成品数组。
        ///
        /// 为什么不"缺了就留空"：uGUI 的 `Image` 在 `sprite == null` 时画的是**白色方块**，
        /// 而白色方块比"缺图"更糟（看起来像界面坏了、又查不出是资源没生成）。
        /// 名字核对走 <see cref="ClubUiSprites.ValidateSlots"/>（与判据 `T124` **同一份实现**，
        /// 不各写一遍，铁律 16）。
        /// </summary>
        public static Sprite[] LoadUiSpritesOrThrow()
        {
            var sprites = new Sprite[ClubUiSprites.Count];
            for (int i = 0; i < sprites.Length; i++)
            {
                var kind = (ClubUiSprites.UiSprite)i;
                string path = ClubUiSprites.AssetPath(kind);
                sprites[i] = LoadSpriteWithRetry(path);
                if (sprites[i] == null)
                {
                    throw new InvalidOperationException(
                        $"界面精灵缺失或未按 Sprite 导入：{path}"
                        + "（生成：`<venv python> Docs/Tools/make_ui_sprites.py`；导入设置：`UiSpriteImportPostprocessor`）。");
                }
            }

            if (!ClubUiSprites.ValidateSlots(sprites, out string error))
            {
                throw new InvalidOperationException("界面精灵槽位校验失败：" + error);
            }

            return sprites;
        }

        /// <summary>按 <see cref="ClubBallChips"/> 的清单逐格载入 16 个球号贴片；缺失即抛（同 <see cref="LoadUiSpritesOrThrow"/>）。</summary>
        public static Sprite[] LoadBallChipsOrThrow()
        {
            var chips = new Sprite[ClubBallChips.Count];
            for (int i = 0; i < chips.Length; i++)
            {
                string path = ClubBallChips.AssetPath(i);
                chips[i] = LoadSpriteWithRetry(path);
                if (chips[i] == null)
                {
                    throw new InvalidOperationException(
                        $"迷你球贴片缺失或未按 Sprite 导入：{path}"
                        + "（生成：`<venv python> Docs/Tools/make_ball_chips.py`）。");
                }
            }

            if (!ClubBallChips.ValidateSlots(chips, out string error))
            {
                throw new InvalidOperationException("迷你球贴片槽位校验失败：" + error);
            }

            return chips;
        }

        /// <summary>
        /// 生成一颗**纯可视**球：优先用美术包模型 `Ball_NN.fbx`（球心原点、半径 0.028575 m、单位缩放），
        /// 模型缺失时退回球体基元（**兜底不得让场景没有球**）。两种路径都**立即移除碰撞体**（自研物理铁律）。
        ///
        /// **材质逐号取**（`Ball_NN.mat`）：0 号 = 白球、1–15 号 = 各自花色与号数 —— 这是用户反馈
        /// "球没弄全/全白"的直接修法（旧实现对**每一颗**都套 `Ball_00.mat`）。
        /// </summary>
        private static GameObject BuildBallObject(string name, int ballNumber)
        {
            GameObject ball = InstantiateModelOrNull(string.Format(CultureInfo.InvariantCulture,
                BallModelPathFormat, ballNumber), name);
            if (ball != null)
            {
                ball.transform.localPosition = Vector3.zero;
                ball.transform.localRotation = BallFaceRotation();
                ball.transform.localScale = Vector3.one;
            }
            else
            {
                ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = name;
                // 基元球直径 = 1 Unity 单位 ⇒ 缩放到规范球直径。
                ball.transform.localScale = Vector3.one * PoolConstants.BallDiameter;
            }

            RemoveAllColliders(ball);
            ball.layer = BilliardsLayers.Ball;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(string.Format(CultureInfo.InvariantCulture,
                BallMaterialPathFormat, ballNumber));
            if (material != null)
            {
                var renderers = ball.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].sharedMaterial = material;
                }
            }

            return ball;
        }

        /// <summary>
        /// **号面朝向修正**：美术包的球模型**自带朝向**已让号码面向原型相机方向
        /// <see cref="ArtBallFaceDirection"/>（见 `Balls/Editable/README.md`）。本工程相机是更陡的俯视
        /// （`CameraPosition` → `CameraLookTarget`）⇒ 只需把那个朝向**旋到本工程相机方向**，
        /// 号面即朝观众。**不假设模型内部的局部轴**（用 `FromToRotation` 表达"朝向差"）。
        ///
        /// 盲带：只定"面朝哪"，**不定号在面内的旋转**（号码是否水平由模型自带朝向决定）；球滚动时号随之转（设计如此，非 billboard）。
        /// </summary>
        private static Quaternion BallFaceRotation()
        {
            Vector3 viewDirection = (CameraPosition - CameraLookTarget).normalized;
            return Quaternion.FromToRotation(ArtBallFaceDirection.normalized, viewDirection);
        }

        /// <summary>实例化一个模型资产（保持与资产的 Prefab 连接，便于日后换模型）；失败返回 <c>null</c>（由调用方兜底）。</summary>
        private static GameObject InstantiateModelOrNull(string assetPath, string objectName)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[SCENE] 模型缺失，退回基元兜底：{assetPath}");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(asset);
            }

            instance.name = objectName;
            return instance;
        }

        /// <summary>移除对象及其子物体上的**所有碰撞体**（美术模型可能自带；自研物理铁律要求一律不用内置物理）。</summary>
        private static void RemoveAllColliders(GameObject target)
        {
            var colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[i]);
            }
        }

        /// <summary>
        /// 按 <see cref="PhysicsTestDrive.RackPositions"/> 生成**与内核球一一对应**的可视球体，返回 Transform 数组。
        /// 第 0 个复用已建的白球；其余为 `RackBall_i`。**只可视**：不引入任何物理组件。
        /// 全部挂 <paramref name="playfieldRoot"/>（缩放根）下，写入的是**局部规范坐标**。
        /// </summary>
        /// <summary>
        /// 建**杆模型**并挂到杆控制器下：
        /// 杆根（<see cref="CueStickRootObjectName"/>，单位位姿，交给控制器驱动）
        /// └ 模型实例（<see cref="CueModelObjectName"/>，带修正旋转 <see cref="CueModelLocalRotation"/>）
        ///
        /// 两层是**必需**的（原因见 <see cref="CueStickRootObjectName"/> 的注释）：
        /// 控制器每帧写绝对 `position/rotation`，修正旋转只能落在它的子物体上。
        /// 模型缺失时返回 <c>null</c> ⇒ 控制器自建基元兜底（不得因为没接线就没有杆）。
        /// </summary>
        private static Transform BuildCueModel(Transform parent)
        {
            GameObject cue = InstantiateModelOrNull(CueModelPath, CueModelObjectName);
            if (cue == null)
            {
                return null;
            }

            RemoveAllColliders(cue);

            var rootGo = new GameObject(CueStickRootObjectName);
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;

            cue.transform.SetParent(rootGo.transform, false);
            cue.transform.localPosition = Vector3.zero;
            cue.transform.localRotation = CueModelLocalRotation;
            cue.transform.localScale = Vector3.one;
            return rootGo.transform;
        }

        private static Transform[] BuildRackVisuals(GameObject playfieldRoot, TableConfig config, GameObject cueBall)
        {
            // ⚠ **球数必须与内核一致**：内核走 `PhysicsTestDrive.RackBallCount`（15 目标球 ⇒ 共 16 颗）。
            // 旧实现这里写死 3 ⇒ 场上只有 4 颗可视球，而驱动器里名义上有更多球 ⇒ "球没弄全"（用户反馈）。
            // 现在两处**同取一个常量**，并由判据 `T122` 断言"可视球数 == 内核球数"。
            Vector2[] positions = PhysicsTestDrive.RackPositions(config.footSpot, PhysicsTestDrive.RackBallCount);
            var visuals = new Transform[positions.Length];
            visuals[0] = cueBall.transform;

            for (int i = 1; i < positions.Length; i++)
            {
                GameObject ball = BuildBallObject(RackBallObjectName + i.ToString(), i);
                ball.transform.SetParent(playfieldRoot.transform, false);
                ball.transform.localPosition = PoolGeometry.PhysicsBallToUnity(positions[i]);
                visuals[i] = ball.transform;
            }

            return visuals;
        }

        private static Camera BuildCamera(GameObject root)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);

            Camera cam = camGo.AddComponent<Camera>();

            // **场景唯一的音频监听器**：代码生成的相机**不会**自带 AudioListener（只有编辑器"新建场景"的
            // 默认相机才带）——缺它则**全场景无声**（所有 AudioSource 不输出；`T91` 守这条）。
            camGo.AddComponent<AudioListener>();

            camGo.transform.position = CameraPosition;
            camGo.transform.rotation = Quaternion.LookRotation(
                CameraLookTarget - camGo.transform.position, Vector3.up);
            cam.fieldOfView = CameraBaseVerticalFovDeg;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.052f, 0.06f, 1f);

            // 4a) **视口适配器**（16:9 基准 + 窄窗回落，保持水平 FOV）。
            //     基准垂直 FOV **实测定为 52**（spec 假定的 40 装不下 `×1.2` 后的内库四角；见 `CameraViewportFitter`
            //     的字段注释与 `Docs/35-A_Day8_相机与指针瞄准_执行记录.md`）。**从相机取** ⇒ 二者不可能漂移（`T53` 守）。
            CameraViewportFitter fitter = camGo.AddComponent<CameraViewportFitter>();
            fitter.targetCamera = cam;
            fitter.baseVerticalFovDeg = cam.fieldOfView;

            return cam;
        }

        private static void BuildLight(GameObject root)
        {
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(52f, -34f, 0f);

            Light sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.957f, 0.902f);
            sun.intensity = 1.40f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }
    }
}
