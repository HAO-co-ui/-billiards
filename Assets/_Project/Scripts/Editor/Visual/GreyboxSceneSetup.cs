using System;
using System.IO;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using Billiards.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 灰盒物理场景的**幂等**构建器（配置即代码，与工程配置/数据导入/材质视觉同一哲学）。
    ///
    /// 产出 <c>Assets/_Project/Scenes/PhysicsGreybox.unity</c>：
    ///  · <c>CushionGizmo</c> 宿主（引用 <c>TableConfig.asset</c>，绘制 82 段库边）
    ///  · <c>PhysicsTestDrive</c> 宿主（累加器驱动 <c>PoolSimulator</c> + 键盘冒烟发射）
    ///  · 白球（放在 <c>TableConfig.footSpot</c>）
    ///  · 俯视相机 + 主平行光（便于肉眼查看灰盒）
    ///
    /// ── 两条明文禁令（spec §8 H-6 / §10.3）─────────────────────────────────
    ///  1. **严禁**把本场景写入 <c>EditorBuildSettings</c>。
    ///     理由：工程配置验收项 <c>SCN-02</c> 断言 build list 中**恰好**只有 <c>Main.unity</c>；
    ///     一旦注册灰盒场景，工程配置验收的 26/26 会变成 25/26、其验收证据立即作废。
    ///     灰盒场景**用编辑器直接双击打开**即可，不需要进 build list。
    ///     若确需注册，必须作为独立变更同时修订工程配置验收口径，不得夹带在物理场景变更内。
    ///  2. **严禁**修改 <c>Assets/_Project/Scenes/Main.unity</c>（已被材质视觉验收 22/22 冻结）。
    ///
    /// 另：本文件只生成、**不校验**（生成/校验入口分离，R2）；校验见 <see cref="PhysicsValidator"/>。
    /// 场景内**不得出现任何碰撞体/刚体**（自研物理铁律 R5-1，场景内白球由
    /// <c>GameObject.CreatePrimitive</c> 生成后立即移除其自带的碰撞体）。
    /// </summary>
    public static class GreyboxSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        public const string ScenesDir = "Assets/_Project/Scenes";
        public const string TableConfigPath = "Assets/_Project/Data/TableConfig.asset";
        public const string CueBallMaterialPath = "Assets/_Project/Materials/Balls/Ball_00.mat";

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

        /// <summary>瞄准辅助宿主对象名（预测线控制器 + 视图；挂在 `PhysicsGreybox` 根下，不进缩放根）。</summary>
        public const string AimAssistObjectName = "AimAssist";

        // ── 主相机取景（**HUD 布局期望值的前提**）──────────────────────────────
        // 球桌在屏幕上的投影包围盒完全由这三项 + 可视野缩放决定；`PracticeHudLayout` 的三块分区
        // 就是照着该投影的实测空余带宽摆的（见 `Docs/35-A_Day13_最小HUD_spec.md` §3.3）。
        // 提成公开常量 + `BuildCamera` 直接引用 ⇒ 相机取景与 HUD 布局**不可能各自漂移**；
        // 判据 `T95 (f)` 再从**已提交场景**回读校验一次（防"场景被手改、代码没跟上"）。

        /// <summary>主相机世界位置。</summary>
        public static readonly Vector3 CameraPosition = new Vector3(0f, 2.10f, -0.75f);

        /// <summary>主相机注视点。</summary>
        public static readonly Vector3 CameraLookTarget = new Vector3(0f, 0.02f, 0.06f);

        /// <summary>基准垂直 FOV（度）；同时是场景序列化 FOV（`CameraViewportFitter` 从相机取 ⇒ 二者不可能漂移）。</summary>
        public const float CameraBaseVerticalFovDeg = 52f;

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
            RunFromCommandLine(Apply);
        }

        /// <summary>供 <c>-executeMethod</c> 使用（强制重建路径）；退出码 0 = 成功。</summary>
        public static void RebuildFromCommandLine()
        {
            RunFromCommandLine(Rebuild);
        }

        private static void RunFromCommandLine(Action build)
        {
            int exitCode = 0;
            try
            {
                build();
                Debug.Log("DAY4_SETUP_OK scene=" + ScenePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("灰盒场景构建失败：" + ex);
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

            // 1) 白球（物理平面 footSpot → **规范世界**，唯一转换点 PoolGeometry）
            //    ⚠ 挂到**缩放根**下：写入的是**局部坐标**（= 规范世界坐标），世界坐标会自动 ×1.2。
            GameObject cueBall = BuildCueBall();
            cueBall.transform.SetParent(playfieldRoot.transform, false);
            cueBall.transform.localPosition = PoolGeometry.PhysicsBallToUnity(config.footSpot);

            // 2) CushionGizmo 宿主
            var gizmoGo = new GameObject(GizmoObjectName);
            gizmoGo.transform.SetParent(root.transform, false);
            CushionGizmo gizmo = gizmoGo.AddComponent<CushionGizmo>();
            gizmo.tableConfig = config;
            // Gizmo 宿主本身不进缩放根（`Gizmos.*` 用世界坐标，父节点缩放对它无效），
            // 但**绘制坐标**必须过显示层 ⇒ 接线 `playfieldRoot` 让 CushionGizmo 走 `PlayfieldView` 映射。
            gizmo.playfieldRoot = playfieldRoot.transform;

            // 3) PhysicsTestDrive 宿主
            var driveGo = new GameObject(DriveObjectName);
            driveGo.transform.SetParent(root.transform, false);
            PhysicsTestDrive drive = driveGo.AddComponent<PhysicsTestDrive>();
            drive.tableConfig = config;
            drive.cushionGizmo = gizmo;
            drive.ballVisual = cueBall.transform;

            // 3a) **把 `PhysicsTuning` 资产接进场景** —— 让"唯一可调入口"名副其实；
            //     否则运行时恒为 null ⇒ 灰盒走回落路径 ⇒ 用户不知道自己在测哪套参数（`D7-T02` 阻塞项 ①）。
            //     `T41` 守这条接线（把 tuning 置空 ⇒ FAIL）。
            drive.tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(PhysicsTuningSetup.AssetPath);

            // 3b) **球堆可见性** —— 按 `RackPositions` 生成同数量的球体并接上 `ballVisuals`，
            //     使 Game 视图能看见散球（`D7-T02` 阻塞项 ②）；**纯可视，无任何物理组件**。
            //     球与白球同挂**缩放根**下（见 `BuildRackVisuals`）。
            drive.ballVisuals = BuildRackVisuals(playfieldRoot, config, cueBall);

            // 4) 俯视相机（含视口适配器）+ 主平行光（灰盒可见即可；不引入任何物理组件）
            Camera cam = BuildCamera(root);
            BuildLight(root);

            // 5) **指针瞄准控制器** —— 交互胶水：读输入 → 纯状态机 → **只写** `drive.aimDegrees`。
            //    与驱动器同层（`PhysicsGreybox` 根下，不进缩放根）；不引入任何物理组件。
            var aimGo = new GameObject(PointerAimControllerObjectName);
            aimGo.transform.SetParent(root.transform, false);
            var aimController = aimGo.AddComponent<PointerAimController>();
            aimController.aimCamera = cam;
            aimController.playfieldRoot = playfieldRoot.transform;
            aimController.drive = drive;

            // 5b) **拉杆力度控制器** —— 交互胶水：按住右键拖 = 蓄力（拖距与力度 1:1、无惯性）、松手 = 出杆。
            //     与驱动器同层（根下、不进缩放根）；右键原"取消瞄准"职责已移交 Esc。
            var powerGo = new GameObject(PointerPowerControllerObjectName);
            powerGo.transform.SetParent(root.transform, false);
            var powerController = powerGo.AddComponent<PointerPowerController>();
            powerController.drive = drive;
            powerController.aimCamera = cam;
            powerController.playfieldRoot = playfieldRoot.transform;

            // 5c) **球杆控制器** —— 击球流程状态机的唯一驱动者 + 杆程序动画（前推到位帧击球恰一次）。
            //     拉杆松手 ⇒ 事件锁存 ⇒ 球杆前推 ⇒ 命中帧出杆；与指示物同层（根下、不进缩放根）。
            var cueStickGo = new GameObject(CueStickControllerObjectName);
            cueStickGo.transform.SetParent(root.transform, false);
            var cueStick = cueStickGo.AddComponent<CueStickController>();
            cueStick.drive = drive;
            cueStick.pull = powerController;
            powerController.cueStick = cueStick;

            // 5d) **音频层** —— 内核接触事件流 → 撞声；strike 轮询出杆计数；纯函数调制 + 限流。
            var audioGo = new GameObject(PoolAudioObjectName);
            audioGo.transform.SetParent(root.transform, false);
            var poolAudio = audioGo.AddComponent<PoolAudio>();
            poolAudio.drive = drive;
            poolAudio.strikeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/strike.wav");
            poolAudio.hitClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/hit.wav");
            poolAudio.railClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/rail.wav");
            poolAudio.pocketClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/pocket.wav");
            poolAudio.bgmClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/BGM/late-set.wav");

            // 5e) **特效层** —— 计数增量门 + 状态推导生成 4 类程序化特效（池化、可开关；不消费接触事件流）。
            var vfxGo = new GameObject(PoolVfxObjectName);
            vfxGo.transform.SetParent(root.transform, false);
            var vfxView = vfxGo.AddComponent<PoolVfxView>();
            vfxView.drive = drive;
            vfxView.tableConfig = config;
            vfxView.playfieldRoot = playfieldRoot.transform;

            // 5f) **最小 HUD** —— 三块（计数组 / 状态条 / 力度 + Shoot）。
            //     几何唯一来源 = `PracticeHudLayout.Resolve()`（落成 uGUI 锚点比例 ⇒ 与判据同源、无第二份数字）；
            //     数据全部取自既有只读出口（`CueStickController.Flow` / `drive.power` / `drive.LaunchCount` / 内核球表）。
            //     字体按分工指派：标题族 = Barlow Condensed SemiBold、正文族 = Inter Regular（`T97` 钉住这两个资产路径）。
            var hudGo = new GameObject(PracticeHudObjectName);
            hudGo.transform.SetParent(root.transform, false);
            var hud = hudGo.AddComponent<PracticeHudView>();
            hud.drive = drive;
            hud.cueStick = cueStick;
            hud.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudAssetBuilder.TitleFontAssetPath);
            hud.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudAssetBuilder.BodyFontAssetPath);

            // 6) **瞄准辅助**（预测线）：控制器（胶水：只读内核 + 读驱动器开关）+ 视图（绘制：过显示层单点）。
            //    与驱动器同层（`PhysicsGreybox` 根下，不进缩放根）；不引入任何物理组件。
            var assistGo = new GameObject(AimAssistObjectName);
            assistGo.transform.SetParent(root.transform, false);
            var assistController = assistGo.AddComponent<AimAssistController>();
            assistController.drive = drive;
            assistController.aimCamera = cam;
            assistController.playfieldRoot = playfieldRoot.transform;

            var assistView = assistGo.AddComponent<AimAssistView>();
            assistView.controller = assistController;
            assistView.playfieldRoot = playfieldRoot.transform;

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>白球：球体基元 + 材质；**立即移除基元自带的碰撞体**（自研物理铁律）。</summary>
        private static GameObject BuildCueBall()
        {
            return BuildBallObject(CueBallObjectName);
        }

        /// <summary>生成一颗**纯可视**球（球体基元 + 球材质，立即移除自带碰撞体）。</summary>
        private static GameObject BuildBallObject(string name)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = name;

            Collider primitiveCollider = ball.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(primitiveCollider);
            }

            // 基元球直径 = 1 Unity 单位 ⇒ 缩放到规范球直径。
            ball.transform.localScale = Vector3.one * PoolConstants.BallDiameter;
            ball.layer = BilliardsLayers.Ball;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(CueBallMaterialPath);
            var renderer = ball.GetComponent<MeshRenderer>();
            if (material != null && renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            return ball;
        }

        /// <summary>
        /// 按 <see cref="PhysicsTestDrive.RackPositions"/> 生成**与内核球一一对应**的可视球体，返回 Transform 数组。
        /// 第 0 个复用已建的白球；其余为 `RackBall_i`。**只可视**：不引入任何物理组件。
        /// 全部挂 <paramref name="playfieldRoot"/>（缩放根）下，写入的是**局部规范坐标**。
        /// </summary>
        private static Transform[] BuildRackVisuals(GameObject playfieldRoot, TableConfig config, GameObject cueBall)
        {
            Vector2[] positions = PhysicsTestDrive.RackPositions(config.footSpot, 3);
            var visuals = new Transform[positions.Length];
            visuals[0] = cueBall.transform;

            for (int i = 1; i < positions.Length; i++)
            {
                GameObject ball = BuildBallObject("RackBall_" + i.ToString());
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
