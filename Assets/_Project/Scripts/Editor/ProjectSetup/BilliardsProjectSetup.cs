using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 工程基建与决策固化的可执行落地方案。
    ///
    /// 为什么用脚本而不是直接改 ProjectSettings/*.asset：
    /// Unity 编辑器把工程设置常驻在内存，编辑器运行期间手改磁盘上的 YAML 会在
    /// 编辑器下次写回时被静默覆盖。通过官方 API 修改是唯一可靠且可复现的方式，
    /// 同时本脚本本身也是这套配置的文档与回归入口。
    ///
    /// 幂等：任意次数执行结果一致。
    /// 命令行入口：-executeMethod Billiards.EditorTools.BilliardsProjectSetup.ApplyProjectConfigFromCommandLine
    /// </summary>
    public static class BilliardsProjectSetup
    {
        public const string ProductName = "Idea35 Pool Room";
        public const string MainScenePath = BilliardsProjectVerifier.MainScenePath;
        public const string InputActionsSourcePath = "Assets/InputSystem_Actions.inputactions";
        public const string InputActionsTargetPath = BilliardsProjectVerifier.InputActionsPath;
        public const string PcRpAssetPath = BilliardsProjectVerifier.PcRpAssetPath;

        public static readonly string[] RequiredActionMaps = { "Gameplay", "UI" };

        public static readonly string[] RequiredGameplayActions =
        {
            "PointerPosition", "AimLock", "AimNudge", "PowerAdjust", "Shoot", "CancelAim", "RestartRack", "ToggleAimGuide",
        };

        /// <summary>
        /// Unity URP 模板残留，必须清理。
        /// 顺序有意义：Readme.asset 依赖 TutorialInfo/Scripts/Readme.cs，
        /// 必须在该脚本被删除之前先删掉资产，否则资产会进入无法反序列化的状态。
        /// </summary>
        public static readonly string[] TemplateLeftovers =
        {
            "Assets/Readme.asset",
            "Assets/Scenes/SampleScene.unity",
            "Assets/Scenes",
            "Assets/TutorialInfo",
        };

        [MenuItem("Billiards/Setup/Apply Project Configuration", false, 100)]
        public static void ApplyProjectConfigInteractive()
        {
            SetupReport report = ApplyProjectConfig(interactive: true);
            Debug.Log(report.ToConsoleSummary());
            EditorUtility.DisplayDialog(
                "Billiards Project Setup",
                string.Format("配置已应用并验收。\n\nPASS {0}  WARN {1}  FAIL {2}\n结论：{3}\n\n报告：{4}",
                    report.passCount, report.warnCount, report.failCount, report.overallStatus,
                    System.IO.Path.Combine(SetupReport.ReportsDirectory, "day1-verification.json")),
                "OK");
        }

        [MenuItem("Billiards/Setup/Verify Project Configuration", false, 101)]
        public static void VerifyProjectConfigInteractive()
        {
            SetupReport report = BilliardsProjectVerifier.RunAll();
            string path = report.WriteToDisk();
            Debug.Log(report.ToConsoleSummary());
            Debug.Log("验收报告已写入：" + path);
            EditorUtility.DisplayDialog(
                "Billiards Project Verify",
                string.Format("验收完成。\n\nPASS {0}  WARN {1}  FAIL {2}\n结论：{3}",
                    report.passCount, report.warnCount, report.failCount, report.overallStatus),
                "OK");
        }

        /// <summary>命令行入口：应用配置 → 验收 → 写报告 → 按结果设置退出码。</summary>
        public static void ApplyProjectConfigFromCommandLine()
        {
            SetupReport report;
            int exitCode;

            try
            {
                report = ApplyProjectConfig(interactive: false);
                Debug.Log(report.ToConsoleSummary());
                exitCode = report.failCount > 0 ? 1 : 0;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("工程配置应用过程中抛出异常：" + ex);
                exitCode = 2;
                report = null;
            }

            if (report != null)
            {
                string path = report.WriteToDisk();
                Debug.Log("验收报告已写入：" + path);
                Debug.Log("DAY1_RESULT=" + report.overallStatus
                          + " PASS=" + report.passCount
                          + " WARN=" + report.warnCount
                          + " FAIL=" + report.failCount);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        public static SetupReport ApplyProjectConfig(bool interactive)
        {
            AssetDatabase.Refresh();

            ApplyPlayerSettings();
            ApplyLayers();
            ApplyPhysicsIsolation();
            ApplyRenderingSettings();

            MigrateInputActions();
            EnsureMainScene(interactive);
            CleanupTemplateLeftovers(interactive);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SetupReport report = BilliardsProjectVerifier.RunAll();
            report.Add(CheckResult.Ok("RUN-01", "工程配置应用流程执行完毕",
                "无异常抛出", "无异常", "各步骤均为幂等操作，可重复执行。"));
            report.Finalise();
            return report;
        }

        // ---- 玩家设置 ---------------------------------------------------------

        private static void ApplyPlayerSettings()
        {
            if (PlayerSettings.productName != ProductName)
            {
                PlayerSettings.productName = ProductName;
            }

            // 色彩空间切换会触发全量 Shader 重编译，因此仅在确有差异时才写。
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.runInBackground = true;
        }

        // ---- Layer 与物理隔离 -------------------------------------------------

        private static void ApplyLayers()
        {
            Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            {
                throw new System.InvalidOperationException("无法加载 ProjectSettings/TagManager.asset。");
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null)
            {
                throw new System.InvalidOperationException("TagManager 中未找到 layers 属性。");
            }

            for (int i = 0; i < BilliardsLayers.Indices.Length; i++)
            {
                int index = BilliardsLayers.Indices[i];
                string desired = BilliardsLayers.Names[i];

                while (layers.arraySize <= index)
                {
                    layers.InsertArrayElementAtIndex(layers.arraySize);
                    layers.GetArrayElementAtIndex(layers.arraySize - 1).stringValue = string.Empty;
                }

                SerializedProperty element = layers.GetArrayElementAtIndex(index);
                if (element.stringValue != desired)
                {
                    if (!string.IsNullOrEmpty(element.stringValue))
                    {
                        Debug.LogWarning(string.Format(
                            "Layer {0} 原本为 '{1}'，已被本项目的 '{2}' 覆盖。请确认无其他系统依赖该层。",
                            index, element.stringValue, desired));
                    }

                    element.stringValue = desired;
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyPhysicsIsolation()
        {
            // 本项目不使用 PhysX 做台球物理。把玩法层两两隔离，任何误加的
            // Rigidbody/Collider 都无法产生隐形接触力，从而保护自研模拟器的确定性。
            for (int i = 0; i < BilliardsLayers.Indices.Length; i++)
            {
                for (int j = i; j < BilliardsLayers.Indices.Length; j++)
                {
                    // 必须全限定：Runtime 新增的命名空间 Billiards.Physics 会遮蔽 UnityEngine.Physics
                    // （在 Billiards.* 命名空间层级内，标识符 Physics 先解析到 Billiards.Physics）。
                    UnityEngine.Physics.IgnoreLayerCollision(
                        BilliardsLayers.Indices[i],
                        BilliardsLayers.Indices[j],
                        true);
                }
            }
        }

        // ---- 渲染 -------------------------------------------------------------

        private static void ApplyRenderingSettings()
        {
            RenderPipelineAsset pcRp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PcRpAssetPath);
            if (pcRp == null)
            {
                Debug.LogWarning("未找到 " + PcRpAssetPath + "，跳过渲染管线设置。");
                return;
            }

            if (GraphicsSettings.defaultRenderPipeline != pcRp)
            {
                GraphicsSettings.defaultRenderPipeline = pcRp;
            }

            // 选择名为 PC 的画质等级（移动端等级在模板中已排除 Standalone 平台）。
            int pcLevel = -1;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                if (QualitySettings.names[i] == "PC")
                {
                    pcLevel = i;
                    break;
                }
            }

            if (pcLevel >= 0 && QualitySettings.GetQualityLevel() != pcLevel)
            {
                QualitySettings.SetQualityLevel(pcLevel, applyExpensiveChanges: true);
            }
        }

        // ---- 输入资产迁移 -----------------------------------------------------

        private static void MigrateInputActions()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(InputActionsTargetPath) != null)
            {
                return; // 已迁移
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(InputActionsSourcePath) == null)
            {
                Debug.LogWarning("未找到待迁移的输入资产 " + InputActionsSourcePath + "，跳过。");
                return;
            }

            // MoveAsset 保留 GUID，因此 EditorBuildSettings 中指向该项目级输入资产的
            // config object 会自动跟随，不会失效。
            string error = AssetDatabase.MoveAsset(InputActionsSourcePath, InputActionsTargetPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("输入资产迁移失败：" + error);
            }
        }

        // ---- 场景 -------------------------------------------------------------

        private static void EnsureMainScene(bool interactive)
        {
            // 批处理模式下 Unity 起始于一个"未命名且未保存"的空场景，
            // 此状态下 NewSceneMode.Additive 会被拒绝：
            //   InvalidOperationException: Cannot create a new scene additively with an untitled scene unsaved.
            // Single 模式会用新场景替换掉那个空场景，在批处理与交互两种模式下都成立。
            if (!Application.isBatchMode && interactive
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return; // 用户取消了保存确认，放弃切换场景
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) == null)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, MainScenePath))
                {
                    throw new System.InvalidOperationException("主场景保存失败：" + MainScenePath);
                }
            }
            else if (!Application.isBatchMode)
            {
                // 主场景将成为唯一构建场景，先把编辑器切过去，才能删除旧的 SampleScene。
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }

        private static void CleanupTemplateLeftovers(bool interactive)
        {
            foreach (string path in TemplateLeftovers)
            {
                if (!ProjectPaths.Exists(path))
                {
                    continue;
                }

                if (!AssetDatabase.DeleteAsset(path))
                {
                    // Readme.asset 依赖 TutorialInfo 中的 Readme.cs；若脚本被先行删除，
                    // 该资产会进入无法反序列化状态，AssetDatabase.DeleteAsset 可能拒绝处理。
                    // 退化为直接删除文件与其 .meta，确保结果一致。
                    ProjectPaths.DeleteOnDisk(path);
                }
            }

            AssetDatabase.Refresh();
        }
    }
}
