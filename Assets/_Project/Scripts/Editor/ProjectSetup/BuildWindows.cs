using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **Windows 桌面构建入口**（Day 16 任务③）：`-executeMethod Billiards.EditorTools.BuildWindows.BuildFromCommandLine`。
    ///
    /// 验收要求是"**构建包可脱离编辑器独立运行**" ⇒ 本入口必须做到三件事：
    /// 1. **先应用性能与构建配置**（<see cref="PerformanceSetup"/>，幂等），再**断言**构建场景恰为灰盒场景
    ///    （原列表指向空壳 `Main.unity` ⇒ 会打出黑屏死包，见该类的说明）；
    /// 2. 用 `BuildPipeline.BuildPlayer` 出包到 <see cref="OutputRelativePath"/>；
    /// 3. **自检产物齐备**（exe + `_Data/` + `UnityPlayer.dll`）并落一份构建报告 JSON，用**退出码**表达成败。
    ///
    /// 报告落在**构建产物目录内**（`Build/…/build-report.json`，已在 `.gitignore` 忽略）而不写进仓库：
    /// 一次构建的报告是"这一次产物"的属性，跟着产物走才不会被后人误读成当前状态。
    ///
    /// 用法：
    /// <code>
    /// Unity.exe -batchmode -projectPath &lt;工程&gt; -executeMethod Billiards.EditorTools.BuildWindows.BuildFromCommandLine -logFile &lt;log&gt;
    /// </code>
    /// 退出码：**0 = 出包且自检通过**；**1 = 构建失败**；**2 = 配置/场景断言失败**；**3 = 产物自检失败**。
    /// </summary>
    public static class BuildWindows
    {
        /// <summary>产物目录（仓库内、已在 `.gitignore` 忽略；避免把上百 MB 提交进 git）。</summary>
        public const string OutputRelativePath = "Build/Windows/Idea35PoolRoom.exe";

        /// <summary>命令行入口。</summary>
        public static void BuildFromCommandLine()
        {
            int setup = PerformanceSetup.Apply();
            if (setup != 0)
            {
                Debug.LogError($"[BUILD] 配置应用失败（退出码 {setup}）⇒ 不出包。");
                EditorApplication.Exit(2);
                return;
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != PerformanceSetup.GreyboxScenePath || !scenes[0].enabled)
            {
                Debug.LogError("[BUILD] 构建场景列表不是「恰含并启用灰盒场景」⇒ 拒绝出包"
                               + "（否则会打出没有任何逻辑的空包）。");
                EditorApplication.Exit(2);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { PerformanceSetup.GreyboxScenePath },
                locationPathName = OutputRelativePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            string exeFull = Path.GetFullPath(OutputRelativePath);
            string dataDir = Path.Combine(Path.GetDirectoryName(exeFull) ?? ".", "Idea35PoolRoom_Data");
            string unityPlayer = Path.Combine(Path.GetDirectoryName(exeFull) ?? ".", "UnityPlayer.dll");
            string managedDir = Path.Combine(dataDir, "Managed");

            bool artifactsOk = summary.result == BuildResult.Succeeded
                               && File.Exists(exeFull)
                               && new FileInfo(exeFull).Length > 0
                               && Directory.Exists(dataDir)
                               && Directory.Exists(managedDir)
                               && File.Exists(unityPlayer);

            long exeBytes = File.Exists(exeFull) ? new FileInfo(exeFull).Length : 0;

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"result\": \"").Append(summary.result).Append("\",\n");
            sb.Append("  \"target\": \"StandaloneWindows64\",\n");
            sb.Append("  \"scene\": \"").Append(PerformanceSetup.GreyboxScenePath).Append("\",\n");
            sb.Append("  \"outputPath\": \"").Append(exeFull.Replace("\\", "/")).Append("\",\n");
            sb.Append("  \"exeBytes\": ").Append(exeBytes).Append(",\n");
            sb.Append("  \"totalBytes\": ").Append(summary.totalSize).Append(",\n");
            sb.Append("  \"buildSeconds\": ")
              .Append(summary.totalTime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"errors\": ").Append(summary.totalErrors).Append(",\n");
            sb.Append("  \"warnings\": ").Append(summary.totalWarnings).Append(",\n");
            sb.Append("  \"artifactsOk\": ").Append(artifactsOk ? "true" : "false").Append("\n");
            sb.Append("}\n");

            string reportPath = Path.Combine(Path.GetDirectoryName(exeFull) ?? ".", "build-report.json");
            try
            {
                File.WriteAllText(reportPath, sb.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BUILD] 报告写盘失败：{e.Message}");
            }

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[BUILD] {0}｜耗时 {1:F1}s｜exe {2} B｜总 {3} B｜错误 {4} 警告 {5}｜产物自检 {6}｜报告 {7}",
                summary.result, summary.totalTime.TotalSeconds, exeBytes, summary.totalSize,
                summary.totalErrors, summary.totalWarnings, artifactsOk ? "OK" : "FAIL", reportPath));

            EditorApplication.Exit(artifactsOk ? 0 : (summary.result == BuildResult.Succeeded ? 3 : 1));
        }
    }
}
