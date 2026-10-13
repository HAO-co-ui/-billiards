using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Billiards.EditorTools
{
    public enum CheckStatus
    {
        Pass,
        Warn,
        Fail,
    }

    [Serializable]
    public class CheckResult
    {
        public string id;
        public string title;
        public string expected;
        public string actual;
        public string status;
        public string detail;

        public static CheckResult Ok(string id, string title, string expected, string actual, string detail = "")
        {
            return Make(id, title, expected, actual, CheckStatus.Pass, detail);
        }

        public static CheckResult Warn(string id, string title, string expected, string actual, string detail)
        {
            return Make(id, title, expected, actual, CheckStatus.Warn, detail);
        }

        public static CheckResult Fail(string id, string title, string expected, string actual, string detail = "")
        {
            return Make(id, title, expected, actual, CheckStatus.Fail, detail);
        }

        private static CheckResult Make(string id, string title, string expected, string actual, CheckStatus status, string detail)
        {
            return new CheckResult
            {
                id = id,
                title = title,
                expected = expected,
                actual = actual,
                status = status.ToString().ToUpperInvariant(),
                detail = detail ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// 资产路径探测工具。
    ///
    /// 存在的理由：不要用 AssetDatabase.LoadAssetAtPath&lt;Object&gt;() != null 判断资产是否存在。
    /// 当某资产所依赖的脚本先被删除时（例如模板的 Readme.asset 依赖 TutorialInfo 里的 Readme.cs），
    /// 该资产会进入"无法反序列化"状态，LoadAssetAtPath 返回 null —— 于是清理逻辑跳过它、
    /// 验收逻辑也报告它不存在，**两边同时失效并产生假通过**。
    /// 这里直接查文件系统，绕开该陷阱。
    /// </summary>
    public static class ProjectPaths
    {
        public static string ToFullPath(string assetPath)
        {
            return Path.Combine(
                SetupReport.ProjectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static bool Exists(string assetPath)
        {
            string full = ToFullPath(assetPath);
            return File.Exists(full) || Directory.Exists(full);
        }

        /// <summary>直接删除磁盘上的资产及其 .meta，用于 AssetDatabase.DeleteAsset 拒绝处理的情形。</summary>
        public static void DeleteOnDisk(string assetPath)
        {
            string full = ToFullPath(assetPath);

            if (Directory.Exists(full))
            {
                Directory.Delete(full, true);
            }
            else if (File.Exists(full))
            {
                File.Delete(full);
            }

            string meta = full + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }
    }

    [Serializable]
    public class SetupReport
    {
        public string schema = "billiards.day1-verification.v1";
        public string generatedAtUtc;
        public string unityVersion;
        public string activeBuildTarget;
        public string projectRoot;
        public bool isBatchMode;
        public int passCount;
        public int warnCount;
        public int failCount;
        public string overallStatus;
        public List<CheckResult> checks = new List<CheckResult>();

        public static string ProjectRoot
        {
            get
            {
                // Application.dataPath = <root>/Assets
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent == null ? Application.dataPath : parent.FullName;
            }
        }

        public static string ReportsDirectory
        {
            get { return Path.Combine(ProjectRoot, "Docs", "Reports"); }
        }

        public void Add(CheckResult result)
        {
            checks.Add(result);
        }

        public void Finalise()
        {
            passCount = 0;
            warnCount = 0;
            failCount = 0;

            foreach (CheckResult c in checks)
            {
                switch (c.status)
                {
                    case "PASS":
                        passCount++;
                        break;
                    case "WARN":
                        warnCount++;
                        break;
                    default:
                        failCount++;
                        break;
                }
            }

            overallStatus = failCount > 0 ? "FAIL" : (warnCount > 0 ? "PASS_WITH_WARNINGS" : "PASS");
            generatedAtUtc = DateTime.UtcNow.ToString("o");
            unityVersion = Application.unityVersion;
            activeBuildTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
            projectRoot = ProjectRoot;
            isBatchMode = Application.isBatchMode;
        }

        /// <summary>
        /// 写验收报告。**支持 `-outputDir`**（D4-FIX-03 E1）——
        /// 此前写死 `Docs/Reports/`，于是 Day1 的 CLI 入口跑一次就**覆写已入库证据**，
        /// 复核方无法独立复跑 Day1（只能靠自报）。现走 <see cref="ReportOutput.ResolveReportPath"/> 单点。
        /// </summary>
        public string WriteToDisk(string fileName = "day1-verification.json")
        {
            string path = ReportOutput.ResolveReportPath("Docs/Reports/" + fileName, fileName);
            File.WriteAllText(path, JsonUtility.ToJson(this, true), new System.Text.UTF8Encoding(false));
            return path;
        }

        public string ToConsoleSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("================ Billiards Day-1 工程配置验收 ================");
            sb.AppendLine("Unity " + unityVersion + "  |  target " + activeBuildTarget + "  |  batch " + isBatchMode);
            sb.AppendLine("--------------------------------------------------------------");

            foreach (CheckResult c in checks)
            {
                sb.AppendLine(string.Format("[{0}] {1}", c.status, c.title));
                sb.AppendLine(string.Format("        expected: {0}", c.expected));
                sb.AppendLine(string.Format("        actual  : {0}", c.actual));
                if (!string.IsNullOrEmpty(c.detail))
                {
                    sb.AppendLine(string.Format("        detail  : {0}", c.detail));
                }
            }

            sb.AppendLine("--------------------------------------------------------------");
            sb.AppendLine(string.Format("PASS {0}   WARN {1}   FAIL {2}   =>  {3}", passCount, warnCount, failCount, overallStatus));
            sb.AppendLine("==============================================================");
            return sb.ToString();
        }
    }
}
