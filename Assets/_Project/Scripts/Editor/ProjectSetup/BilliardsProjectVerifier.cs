using System.Collections.Generic;
using Billiards.Core;
using Billiards.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// Day 1 工程配置验收器。只读（唯一例外：为统计 Rigidbody 会以叠加方式临时打开主场景，
    /// 随后立即关闭，不影响当前打开的场景）。所有结论都带 expected / actual，便于回溯。
    /// </summary>
    public static class BilliardsProjectVerifier
    {
        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string InputActionsPath = "Assets/_Project/Settings/Input/Billiards.inputactions";
        public const string PcRpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        public const string ProjectWideActionsConfigKey = "com.unity.input.settings.actions";

        public static SetupReport RunAll()
        {
            var report = new SetupReport();

            VerifyPlayerSettings(report);
            VerifyLayers(report);
            VerifyRendering(report);
            VerifyScenes(report);
            VerifyInput(report);
            VerifyAssemblies(report);
            VerifyBuildTarget(report);

            report.Finalise();
            return report;
        }

        // ---- 玩家设置 ---------------------------------------------------------

        private static void VerifyPlayerSettings(SetupReport report)
        {
            report.Add(PlayerSettings.colorSpace == ColorSpace.Linear
                ? CheckResult.Ok("CFG-01", "色彩空间为 Linear",
                    "Linear", PlayerSettings.colorSpace.ToString(),
                    "URP 在 Gamma 下会出现光照与贴图亮度错误，此项为硬性要求。")
                : CheckResult.Fail("CFG-01", "色彩空间为 Linear",
                    "Linear", PlayerSettings.colorSpace.ToString()));

            string res = PlayerSettings.defaultScreenWidth + "x" + PlayerSettings.defaultScreenHeight;
            report.Add(PlayerSettings.defaultScreenWidth == 1920 && PlayerSettings.defaultScreenHeight == 1080
                ? CheckResult.Ok("CFG-02", "默认窗口分辨率 1920x1080", "1920x1080", res)
                : CheckResult.Fail("CFG-02", "默认窗口分辨率 1920x1080", "1920x1080", res));

            report.Add(PlayerSettings.fullScreenMode == FullScreenMode.Windowed
                ? CheckResult.Ok("CFG-03", "默认窗口模式（开发期便于对照调参）",
                    FullScreenMode.Windowed.ToString(), PlayerSettings.fullScreenMode.ToString())
                : CheckResult.Fail("CFG-03", "默认窗口模式（开发期便于对照调参）",
                    FullScreenMode.Windowed.ToString(), PlayerSettings.fullScreenMode.ToString()));

            report.Add(PlayerSettings.resizableWindow
                ? CheckResult.Ok("CFG-04", "窗口可调整大小", "true", "true")
                : CheckResult.Fail("CFG-04", "窗口可调整大小", "true", "false"));

            report.Add(PlayerSettings.runInBackground
                ? CheckResult.Ok("CFG-05", "后台继续运行", "true", "true",
                    "开发期在编辑器外层调参时，模拟不会因失焦暂停。发布前需复核。")
                : CheckResult.Fail("CFG-05", "后台继续运行", "true", "false"));

            report.Add(PlayerSettings.productName == BilliardsProjectSetup.ProductName
                ? CheckResult.Ok("CFG-06", "产品名", BilliardsProjectSetup.ProductName, PlayerSettings.productName)
                : CheckResult.Fail("CFG-06", "产品名", BilliardsProjectSetup.ProductName, PlayerSettings.productName));
        }

        // ---- Layer 与物理隔离 -------------------------------------------------

        private static void VerifyLayers(SetupReport report)
        {
            var mismatches = new List<string>();
            for (int i = 0; i < BilliardsLayers.Indices.Length; i++)
            {
                int index = BilliardsLayers.Indices[i];
                string actualName = LayerMask.LayerToName(index);
                if (actualName != BilliardsLayers.Names[i])
                {
                    mismatches.Add(string.Format("{0}:'{1}'!='{2}'", index, actualName, BilliardsLayers.Names[i]));
                }
            }

            string expectedLayers = string.Join(", ", BilliardsLayers.Names);
            string actualLayers = string.Join(", ",
                System.Array.ConvertAll(BilliardsLayers.Indices, i => i + "=" + LayerMask.LayerToName(i)));

            report.Add(mismatches.Count == 0
                ? CheckResult.Ok("LYR-01", "玩法 Layer 6-11 命名正确", expectedLayers, actualLayers)
                : CheckResult.Fail("LYR-01", "玩法 Layer 6-11 命名正确", expectedLayers,
                    actualLayers, "不一致项：" + string.Join(" | ", mismatches)));

            var colliding = new List<string>();
            for (int i = 0; i < BilliardsLayers.Indices.Length; i++)
            {
                for (int j = i; j < BilliardsLayers.Indices.Length; j++)
                {
                    int a = BilliardsLayers.Indices[i];
                    int b = BilliardsLayers.Indices[j];
                    // 必须全限定：Runtime 新增的命名空间 Billiards.Physics 会遮蔽 UnityEngine.Physics。
                    if (!UnityEngine.Physics.GetIgnoreLayerCollision(a, b))
                    {
                        colliding.Add(a + "-" + b);
                    }
                }
            }

            report.Add(colliding.Count == 0
                ? CheckResult.Ok("LYR-02", "玩法层之间物理碰撞全部隔离",
                    "全部 IgnoreLayerCollision = true", "全部已隔离",
                    "本项目自研二维定长步进物理，不使用 PhysX。隔离用于防止误加 Rigidbody/Collider 造成隐形干扰。")
                : CheckResult.Fail("LYR-02", "玩法层之间物理碰撞全部隔离",
                    "全部 IgnoreLayerCollision = true", "仍存在碰撞对：" + string.Join(", ", colliding)));

            string[] customTags = UnityEditorInternal.InternalEditorUtility.tags;
            int customTagCount = 0;
            foreach (string t in customTags)
            {
                if (!string.IsNullOrEmpty(t) && System.Array.IndexOf(DefaultTags, t) < 0)
                {
                    customTagCount++;
                }
            }

            report.Add(customTagCount == 0
                ? CheckResult.Ok("LYR-03", "未引入自定义 Tag（刻意决策）", "0", "0",
                    "物体身份由组件表达，具备类型安全；字符串 Tag 拼错不会编译失败，不用于关键路径判定。")
                : CheckResult.Warn("LYR-03", "未引入自定义 Tag（刻意决策）", "0", customTagCount.ToString(),
                    "存在自定义 Tag：" + string.Join(", ", customTags)));
        }

        private static readonly string[] DefaultTags =
        {
            "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController",
        };

        // ---- 渲染管线 ---------------------------------------------------------

        private static void VerifyRendering(SetupReport report)
        {
            RenderPipelineAsset pcRp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PcRpAssetPath);

            report.Add(pcRp != null
                ? CheckResult.Ok("RND-01", "PC 渲染管线资产可加载", PcRpAssetPath, pcRp.name)
                : CheckResult.Fail("RND-01", "PC 渲染管线资产可加载", PcRpAssetPath, "未找到"));

            RenderPipelineAsset graphicsDefault = GraphicsSettings.defaultRenderPipeline;
            report.Add(graphicsDefault == pcRp && pcRp != null
                ? CheckResult.Ok("RND-02", "Graphics 默认渲染管线 = PC_RPAsset",
                    "PC_RPAsset", graphicsDefault == null ? "<null>" : graphicsDefault.name)
                : CheckResult.Fail("RND-02", "Graphics 默认渲染管线 = PC_RPAsset",
                    "PC_RPAsset", graphicsDefault == null ? "<null>" : graphicsDefault.name));

            int level = QualitySettings.GetQualityLevel();
            string levelName = level >= 0 && level < QualitySettings.names.Length ? QualitySettings.names[level] : "<invalid>";
            report.Add(levelName == "PC"
                ? CheckResult.Ok("RND-03", "当前画质等级为 PC", "PC", levelName)
                : CheckResult.Fail("RND-03", "当前画质等级为 PC", "PC", levelName));

            RenderPipelineAsset qualityRp = QualitySettings.renderPipeline;
            report.Add(qualityRp == null || qualityRp == pcRp
                ? CheckResult.Ok("RND-04", "PC 画质等级管线覆盖指向 PC_RPAsset",
                    "PC_RPAsset 或继承自 Graphics 默认", qualityRp == null ? "<继承默认>" : qualityRp.name)
                : CheckResult.Fail("RND-04", "PC 画质等级管线覆盖指向 PC_RPAsset",
                    "PC_RPAsset 或继承自 Graphics 默认", qualityRp.name));
        }

        // ---- 场景 -------------------------------------------------------------

        private static void VerifyScenes(SetupReport report)
        {
            SceneAsset mainScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath);
            report.Add(mainScene != null
                ? CheckResult.Ok("SCN-01", "主场景存在", MainScenePath, MainScenePath)
                : CheckResult.Fail("SCN-01", "主场景存在", MainScenePath, "未找到"));

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            bool buildListOk = scenes.Length == 1 && scenes[0].path == MainScenePath && scenes[0].enabled;
            string actualScenes = scenes.Length == 0 ? "<空>" : string.Join(", ", System.Array.ConvertAll(scenes,
                s => s.path + (s.enabled ? " [enabled]" : " [disabled]")));

            report.Add(buildListOk
                ? CheckResult.Ok("SCN-02", "构建场景列表仅含主场景", MainScenePath, actualScenes)
                : CheckResult.Fail("SCN-02", "构建场景列表仅含主场景", MainScenePath, actualScenes));

            var leftovers = new List<string>();
            foreach (string path in BilliardsProjectSetup.TemplateLeftovers)
            {
                // 用文件系统判断而非 AssetDatabase：被删脚本的遗留资产在 AssetDatabase 中
                // 会表现为 null，用它做存在性检查会产生"假通过"。
                if (ProjectPaths.Exists(path))
                {
                    leftovers.Add(path);
                }
            }

            report.Add(leftovers.Count == 0
                ? CheckResult.Ok("SCN-03", "模板残留已清理",
                    string.Join(" / ", BilliardsProjectSetup.TemplateLeftovers) + " 均不存在", "均已移除")
                : CheckResult.Fail("SCN-03", "模板残留已清理",
                    "均不存在", "仍存在：" + string.Join(", ", leftovers)));

            if (mainScene == null)
            {
                report.Add(CheckResult.Fail("SCN-04", "主场景中无 Rigidbody（自研物理守卫）",
                    "0", "无法检查（场景缺失）"));
                return;
            }

            // 单点实现：与 SCN-06 共用 PhysicsBanAudit（铁律 16），不再各写一份。
            int rigidbodyCount = PhysicsBanAudit.CountRigidbodiesInScene(MainScenePath);
            report.Add(rigidbodyCount == 0
                ? CheckResult.Ok("SCN-04", "主场景中无 Rigidbody（自研物理守卫）", "0", "0",
                    "台球运动全部由自研二维定长步进模拟器驱动，任何 Rigidbody 都意味着有人误接入了 PhysX。")
                : CheckResult.Fail("SCN-04", "主场景中无 Rigidbody（自研物理守卫）", "0", rigidbodyCount.ToString(),
                    "检测到 PhysX 刚体，将破坏确定性与手感一致性。"));
        }

        // `CountRigidbodiesInScene` 已下沉为单点实现：`Billiards.Diagnostics.PhysicsBanAudit`
        // （原为 SCN-04/SCN-06 各一份、且已分叉 —— 见该类 doc-comment 的历史说明）。

        // ---- 输入 -------------------------------------------------------------

        private static void VerifyInput(SetupReport report)
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            if (asset == null)
            {
                report.Add(CheckResult.Fail("INP-01", "台球输入映射资产可加载", InputActionsPath, "未找到"));
                report.Add(CheckResult.Fail("INP-02", "项目级输入资产引用完好", ProjectWideActionsConfigKey, "无法检查（资产缺失）"));
                return;
            }

            var missing = new List<string>();
            foreach (string mapName in BilliardsProjectSetup.RequiredActionMaps)
            {
                if (asset.FindActionMap(mapName, false) == null)
                {
                    missing.Add(mapName);
                }
            }

            report.Add(missing.Count == 0
                ? CheckResult.Ok("INP-01", "输入映射包含全部必需动作表",
                    string.Join(", ", BilliardsProjectSetup.RequiredActionMaps),
                    string.Join(", ", MapNames(asset)))
                : CheckResult.Fail("INP-01", "输入映射包含全部必需动作表",
                    string.Join(", ", BilliardsProjectSetup.RequiredActionMaps),
                    string.Join(", ", MapNames(asset)), "缺失：" + string.Join(", ", missing)));

            var gameplayMap = asset.FindActionMap("Gameplay", false);
            if (gameplayMap != null)
            {
                var missingActions = new List<string>();
                foreach (string actionName in BilliardsProjectSetup.RequiredGameplayActions)
                {
                    if (gameplayMap.FindAction(actionName, false) == null)
                    {
                        missingActions.Add(actionName);
                    }
                }

                report.Add(missingActions.Count == 0
                    ? CheckResult.Ok("INP-03", "Gameplay 映射包含全部必需动作",
                        string.Join(", ", BilliardsProjectSetup.RequiredGameplayActions),
                        ActionNames(gameplayMap))
                    : CheckResult.Fail("INP-03", "Gameplay 映射包含全部必需动作",
                        string.Join(", ", BilliardsProjectSetup.RequiredGameplayActions),
                        ActionNames(gameplayMap), "缺失：" + string.Join(", ", missingActions)));
            }
            else
            {
                report.Add(CheckResult.Fail("INP-03", "Gameplay 映射包含全部必需动作", "全部", "映射缺失"));
            }

            InputActionAsset projectWide;
            bool hasProjectWide = EditorBuildSettings.TryGetConfigObject(
                ProjectWideActionsConfigKey, out projectWide);

            report.Add(hasProjectWide && projectWide == asset
                ? CheckResult.Ok("INP-02", "项目级输入资产引用完好", InputActionsPath, asset.name,
                    "资产迁移后 GUID 保持不变，EditorBuildSettings 的 config object 仍指向它。")
                : CheckResult.Fail("INP-02", "项目级输入资产引用完好", InputActionsPath,
                    hasProjectWide ? (projectWide == null ? "<null>" : projectWide.name) : "<未配置>",
                    "项目级动作丢失会导致 Unity 6 的 InputSystem 全局动作不可用。"));
        }

        private static string[] MapNames(InputActionAsset asset)
        {
            var names = new List<string>();
            foreach (InputActionMap map in asset.actionMaps)
            {
                names.Add(map.name);
            }

            return names.ToArray();
        }

        private static string ActionNames(InputActionMap map)
        {
            var names = new List<string>();
            foreach (InputAction action in map.actions)
            {
                names.Add(action.name);
            }

            return string.Join(", ", names);
        }

        // ---- 程序集 -----------------------------------------------------------

        private static void VerifyAssemblies(SetupReport report)
        {
            report.Add(CheckAssembly("ASM-01", "Billiards.Runtime", "Assets/_Project/Scripts/Runtime/Billiards.Runtime.asmdef", report));
            report.Add(CheckAssembly("ASM-02", "Billiards.Editor", "Assets/_Project/Scripts/Editor/Billiards.Editor.asmdef", report));
            report.Add(CheckAssembly("ASM-03", "Billiards.Tests.EditMode", "Assets/_Project/Tests/EditMode/Billiards.Tests.EditMode.asmdef", report));

            // Editor 程序集能读到 Runtime 程序集的常量，即证明 asmdef 引用链正确。
            float radius = PoolConstants.BallRadius;
            report.Add(Mathf.Abs(radius - 0.028575f) < 1e-9f
                ? CheckResult.Ok("ASM-04", "Editor 程序集可访问 Runtime 常量", "0.028575", radius.ToString("R"),
                    "证明 Billiards.Editor 对 Billiards.Runtime 的引用链已生效。")
                : CheckResult.Fail("ASM-04", "Editor 程序集可访问 Runtime 常量", "0.028575", radius.ToString("R")));
        }

        private static CheckResult CheckAssembly(string id, string name, string path, SetupReport report)
        {
            Object asmdef = AssetDatabase.LoadAssetAtPath<Object>(path);
            return asmdef != null
                ? CheckResult.Ok(id, "程序集定义 " + name, path, "已加载")
                : CheckResult.Fail(id, "程序集定义 " + name, path, "未找到");
        }

        // ---- 构建目标 ---------------------------------------------------------

        private static void VerifyBuildTarget(SetupReport report)
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            report.Add(target == BuildTarget.StandaloneWindows64
                ? CheckResult.Ok("TGT-01", "活动构建目标为 Windows 桌面",
                    BuildTarget.StandaloneWindows64.ToString(), target.ToString())
                : CheckResult.Warn("TGT-01", "活动构建目标为 Windows 桌面",
                    BuildTarget.StandaloneWindows64.ToString(), target.ToString(),
                    "35-A 交付边界为 PC 桌面横屏，非 Windows 目标需确认。"));
        }
    }
}
