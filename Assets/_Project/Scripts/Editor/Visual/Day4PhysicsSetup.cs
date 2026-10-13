using System;
using System.IO;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// Day 4 灰盒物理场景的**幂等**构建器（配置即代码，与 Day 1/2/3 同一哲学）。
    ///
    /// 产出 <c>Assets/_Project/Scenes/PhysicsGreybox.unity</c>：
    ///  · <c>CushionGizmo</c> 宿主（引用 <c>TableConfig.asset</c>，绘制 82 段库边）
    ///  · <c>PhysicsTestDrive</c> 宿主（累加器驱动 <c>PoolSimulator</c> + 键盘冒烟发射）
    ///  · 白球（放在 <c>TableConfig.footSpot</c>）
    ///  · 俯视相机 + 主平行光（便于肉眼查看灰盒）
    ///
    /// ── 两条明文禁令（spec §8 H-6 / §10.3）─────────────────────────────────
    ///  1. **严禁**把本场景写入 <c>EditorBuildSettings</c>。
    ///     理由：Day 1 验收项 <c>SCN-02</c> 断言 build list 中**恰好**只有 <c>Main.unity</c>；
    ///     一旦注册灰盒场景，Day 1 的 26/26 会变成 25/26、其验收证据立即作废。
    ///     灰盒场景**用编辑器直接双击打开**即可，不需要进 build list。
    ///     将来若确需注册，必须作为独立变更同时修订 Day 1 验收口径，不得夹带在 Day 4 内。
    ///  2. **严禁**修改 <c>Assets/_Project/Scenes/Main.unity</c>（已被 Day 3 验收 22/22 冻结）。
    ///
    /// 另：本文件只生成、**不校验**（生成/校验入口分离，R2）；校验见 <see cref="Day4Validator"/>。
    /// 场景内**不得出现任何碰撞体/刚体**（自研物理铁律 R5-1，场景内白球由
    /// <c>GameObject.CreatePrimitive</c> 生成后立即移除其自带的碰撞体）。
    /// </summary>
    public static class Day4PhysicsSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        public const string ScenesDir = "Assets/_Project/Scenes";
        public const string TableConfigPath = "Assets/_Project/Data/TableConfig.asset";
        public const string CueBallMaterialPath = "Assets/_Project/Materials/Balls/Ball_00.mat";

        public const string RootObjectName = "PhysicsGreybox";
        public const string GizmoObjectName = "CushionGizmo";
        public const string DriveObjectName = "PhysicsTestDrive";
        public const string CueBallObjectName = "CueBall";

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Visual/Build Day-4 Physics Greybox Scene", false, 402)]
        public static void ApplyInteractive()
        {
            Apply();
            Debug.Log("Day-4 灰盒物理场景可用：" + ScenePath);
            EditorUtility.DisplayDialog("Billiards Day-4 Setup",
                "灰盒物理场景已就绪（幂等：已存在则不重写）：\n\n" + ScenePath
                + "\n\n注意：本场景刻意**不**注册进 Build Settings（见 spec §8 H-6）。", "OK");
        }

        [MenuItem("Billiards/Visual/Rebuild Day-4 Physics Greybox Scene (force overwrite)", false, 403)]
        public static void RebuildInteractive()
        {
            Rebuild();
            Debug.Log("Day-4 灰盒物理场景已强制重建：" + ScenePath);
            EditorUtility.DisplayDialog("Billiards Day-4 Setup",
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
        /// <see cref="Day4Validator"/> 判定，不由生成器偷偷修复。
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
                Debug.LogError("Day-4 灰盒场景构建失败：" + ex);
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

            // 1) 白球（物理平面 footSpot → Unity 世界，唯一转换点 PoolGeometry）
            GameObject cueBall = BuildCueBall();
            cueBall.transform.SetParent(root.transform, false);
            cueBall.transform.position = PoolGeometry.PhysicsBallToUnity(config.footSpot);

            // 2) CushionGizmo 宿主
            var gizmoGo = new GameObject(GizmoObjectName);
            gizmoGo.transform.SetParent(root.transform, false);
            CushionGizmo gizmo = gizmoGo.AddComponent<CushionGizmo>();
            gizmo.tableConfig = config;

            // 3) PhysicsTestDrive 宿主
            var driveGo = new GameObject(DriveObjectName);
            driveGo.transform.SetParent(root.transform, false);
            PhysicsTestDrive drive = driveGo.AddComponent<PhysicsTestDrive>();
            drive.tableConfig = config;
            drive.cushionGizmo = gizmo;
            drive.ballVisual = cueBall.transform;

            // 4) 俯视相机 + 主平行光（灰盒可见即可；不引入任何物理组件）
            BuildCamera(root);
            BuildLight(root);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>白球：球体基元 + 材质；**立即移除基元自带的碰撞体**（自研物理铁律）。</summary>
        private static GameObject BuildCueBall()
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = CueBallObjectName;

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

        private static void BuildCamera(GameObject root)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);

            Camera cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0f, 2.10f, -0.75f);
            camGo.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 0.02f, 0.06f) - camGo.transform.position, Vector3.up);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.052f, 0.06f, 1f);
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
