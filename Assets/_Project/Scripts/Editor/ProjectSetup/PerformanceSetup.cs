using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **性能与构建配置的唯一落地入口**。
    ///
    /// 走**官方 API / `SerializedObject`**，**绝不手改** `ProjectSettings/*.asset` 或资产 YAML
    /// （手改会被编辑器静默覆盖 —— 本工程既有纪律）。幂等：重复执行结果一致。
    ///
    /// 两项配置（都来自实测诊断，见 `Docs/35-A_Day16_性能与打包_spec.md` §3）：
    /// 1. **URP 阴影收敛**：`ShadowDistance 50 → 6 m`、`CascadeCount 4 → 1`、阴影图保持 2048²。
    ///    球桌内库只有 2.54 × 1.27 m 且场景仅 1 盏平行光 ⇒ 原来是在用 4 次 2048² 的阴影 pass
    ///    去覆盖 50 m 的世界，桌上那 2.5 m 反而被摊薄。
    /// 2. **构建场景列表**：设为**正式游戏入口** <c>Main.unity</c>。
    ///    ⚠ **口径变更（旧说法已作废）**：本项原先把列表指向灰盒场景 —— 那是在"`Main.unity` 是空壳"这一前提下
    ///    为了"不打出黑屏死包"做的临时处置，代价是**与 `SCN-02` / `D4-BLD-01`（都断言列表恰为 `Main.unity`）互斥**。
    ///    `Main.unity` 现已是带完整玩法层的正式入口 ⇒ 列表指回 `Main.unity` ⇒ **两条口径不再互斥**
    ///    （`SCN-02` / `D4-BLD-01` 因此变成真正正确）。
    /// </summary>
    public static class PerformanceSetup
    {
        /// <summary>**出包的唯一场景 = 正式游戏入口**（材质视觉场景 + 玩法层，含全部宿主 ⇒ 不是空壳）。</summary>
        public const string BuildScenePath = "Assets/_Project/Scenes/Main.unity";

        /// <summary>URP PC 管线资产（阴影设置的真源；`QualitySettings` 的同类字段在 URP 下惰性）。</summary>
        public const string RenderPipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";

        /// <summary>阴影距离（米）：桌面最大尺寸 2.54 m 的约 2.4 倍，含桌腿落地与地面投影余量。</summary>
        public const float ShadowDistanceMeters = 6f;

        /// <summary>阴影级联数：6 m 范围内 1 级即可（2048² ⇒ 约 2.9 mm/texel）。</summary>
        public const int ShadowCascadeCount = 1;

        /// <summary>主光阴影图分辨率（保持 2048²：6 m 覆盖下分辨率充裕）。</summary>
        public const int MainLightShadowmapResolution = 2048;

        /// <summary>命令行入口（`-executeMethod`）：应用配置后退出，退出码 0 = 成功。</summary>
        public static void ApplyFromCommandLine()
        {
            EditorApplication.Exit(Apply());
        }

        /// <summary>应用配置并**回读校验**；返回 0 = 成功，非 0 = 失败（供退出码直接用）。</summary>
        public static int Apply()
        {
            var rp = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RenderPipelineAssetPath);
            if (rp == null)
            {
                Debug.LogError($"[PERF] 找不到 URP 管线资产：{RenderPipelineAssetPath}");
                return 1;
            }

            var so = new SerializedObject(rp);
            SerializedProperty distance = so.FindProperty("m_ShadowDistance");
            SerializedProperty cascades = so.FindProperty("m_ShadowCascadeCount");
            SerializedProperty resolution = so.FindProperty("m_MainLightShadowmapResolution");
            if (distance == null || cascades == null || resolution == null)
            {
                Debug.LogError("[PERF] URP 资产字段名不匹配（m_ShadowDistance / m_ShadowCascadeCount / "
                               + "m_MainLightShadowmapResolution 之一缺失）—— URP 版本变了？未做任何修改。");
                return 2;
            }

            distance.floatValue = ShadowDistanceMeters;
            cascades.intValue = ShadowCascadeCount;
            resolution.intValue = MainLightShadowmapResolution;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BuildScenePath, true),
            };

            AssetDatabase.SaveAssets();

            // ── 回读校验（不信任写入即成功）────────────────────────────────
            so.Update();
            float backDistance = so.FindProperty("m_ShadowDistance").floatValue;
            int backCascades = so.FindProperty("m_ShadowCascadeCount").intValue;
            int backResolution = so.FindProperty("m_MainLightShadowmapResolution").intValue;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            bool ok = Mathf.Abs(backDistance - ShadowDistanceMeters) < 1e-4f
                      && backCascades == ShadowCascadeCount
                      && backResolution == MainLightShadowmapResolution
                      && scenes.Length == 1
                      && scenes[0].path == BuildScenePath
                      && scenes[0].enabled;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[PERF] 阴影距离 {0} m / 级联 {1} / 阴影图 {2}²；构建场景 [{3}] ⇒ {4}",
                backDistance, backCascades, backResolution, scenes.Length > 0 ? scenes[0].path : "空",
                ok ? "OK" : "校验失败"));

            return ok ? 0 : 3;
        }
    }
}
