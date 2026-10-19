using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **WebGL 构建入口**：`-executeMethod Billiards.EditorTools.BuildWebGL.BuildFromCommandLine`。
    ///
    /// 与 <see cref="BuildWindows"/> **同一套纪律**（先应用配置 → 断言构建场景 → 出包 → 自检产物 → 退出码表达成败），
    /// 差别只有三处：
    /// 1. 目标平台 `WebGL`，产物是**目录**（`Build/Web/index.html` + `Build/*.wasm/data/framework.js`）；
    /// 2. **压缩关掉**（`WebGLCompressionFormat.Disabled`）—— 目的是能直接用最朴素的静态服务器打开验证；
    ///    正式发布再改 Brotli 并在服务器加 `Content-Encoding` 头（否则浏览器拿到压缩流会解析失败）；
    /// 3. 自检项换成 WebGL 的五个必备产物。
    ///
    /// 用法：
    /// <code>
    /// Unity.exe -batchmode -projectPath &lt;工程&gt; -executeMethod Billiards.EditorTools.BuildWebGL.BuildFromCommandLine -logFile &lt;log&gt;
    /// </code>
    /// 退出码：**0 = 出包且自检通过**；**1 = 构建失败**；**2 = 配置/场景断言失败**；**3 = 产物自检失败**。
    /// </summary>
    public static class BuildWebGL
    {
        /// <summary>产物目录（仓库内、已被 `.gitignore` 忽略）。</summary>
        public const string OutputRelativePath = "Build/Web";

        /// <summary>命令行入口。</summary>
        public static void BuildFromCommandLine()
        {
            int setup = PerformanceSetup.Apply();
            if (setup != 0)
            {
                Debug.LogError($"[BUILD-WEB] 配置应用失败（退出码 {setup}）⇒ 不出包。");
                EditorApplication.Exit(2);
                return;
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != PerformanceSetup.BuildScenePath || !scenes[0].enabled)
            {
                Debug.LogError("[BUILD-WEB] 构建场景列表不是「恰含并启用正式入口 Main.unity」⇒ 拒绝出包。");
                EditorApplication.Exit(2);
                return;
            }

            // 联机验证用：关压缩，让 `python -m http.server` 之类的静态服务器能直接跑。
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = true;
            // 画布自适应（PC 与手机浏览器都要铺满）。
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            // **自建模板**（`Assets/WebGLTemplates/Responsive`）：默认模板在"桌面样式"下把画布写死 1280×720，
            // 浏览器窗口小于该尺寸时会被**裁切**（实测 844×390 下菜单被切到屏幕外）。
            // 自建模板让画布始终 100% 铺满可视区，PC 缩放窗口 / 手机 / 平板都能正确显示。
            PlayerSettings.WebGL.template = "PROJECT:Responsive";

            Directory.CreateDirectory(OutputRelativePath);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { PerformanceSetup.BuildScenePath },
                locationPathName = OutputRelativePath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            string dir = Path.GetFullPath(OutputRelativePath);
            string indexHtml = Path.Combine(dir, "index.html");
            string buildDir = Path.Combine(dir, "Build");
            string wasm = Directory.Exists(buildDir)
                ? Directory.GetFiles(buildDir, "*.wasm").Length > 0 ? Directory.GetFiles(buildDir, "*.wasm")[0] : null
                : null;
            string data = Directory.Exists(buildDir) ? Directory.GetFiles(buildDir, "*.data").FirstOrDefault() : null;
            string framework = Directory.Exists(buildDir)
                ? Directory.GetFiles(buildDir, "*.framework.js").FirstOrDefault()
                : null;
            string loader = Directory.Exists(buildDir) ? Directory.GetFiles(buildDir, "*.loader.js").FirstOrDefault() : null;

            bool artifactsOk = summary.result == BuildResult.Succeeded
                               && File.Exists(indexHtml)
                               && wasm != null && File.Exists(wasm)
                               && data != null && File.Exists(data)
                               && framework != null && File.Exists(framework);

            long wasmBytes = wasm != null ? new FileInfo(wasm).Length : 0;

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"result\": \"").Append(summary.result).Append("\",\n");
            sb.Append("  \"target\": \"WebGL\",\n");
            sb.Append("  \"outputPath\": \"").Append(dir.Replace("\\", "\\\\")).Append("\",\n");
            sb.Append("  \"indexHtml\": ").Append(File.Exists(indexHtml) ? "true" : "false").Append(",\n");
            sb.Append("  \"wasmBytes\": ").Append(wasmBytes).Append(",\n");
            sb.Append("  \"dataFile\": ").Append(data != null ? "\"" + Path.GetFileName(data) + "\"" : "null").Append(",\n");
            sb.Append("  \"frameworkFile\": ").Append(framework != null ? "\"" + Path.GetFileName(framework) + "\"" : "null").Append(",\n");
            sb.Append("  \"loaderFile\": ").Append(loader != null ? "\"" + Path.GetFileName(loader) + "\"" : "null").Append(",\n");
            sb.Append("  \"totalBytes\": ").Append(summary.totalSize).Append(",\n");
            sb.Append("  \"errors\": ").Append(summary.totalErrors).Append(",\n");
            sb.Append("  \"warnings\": ").Append(summary.totalWarnings).Append(",\n");
            sb.Append("  \"artifactsOk\": ").Append(artifactsOk ? "true" : "false").Append('\n');
            sb.Append("}\n");

            string reportPath = Path.Combine(dir, "build-report.json");
            File.WriteAllText(reportPath, sb.ToString());
            Debug.Log($"[BUILD-WEB] {summary.result} | errors={summary.totalErrors} | artifactsOk={artifactsOk} | wasm={wasmBytes}B");
            Debug.Log($"[BUILD-WEB] report: {reportPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(artifactsOk ? 0 : 3);
        }
    }
}
