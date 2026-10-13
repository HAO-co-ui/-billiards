using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Billiards.Diagnostics;
using Billiards.Physics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// Day 4 验收门（**只校验，不生成**）。
    ///
    /// R2 铁律：**本类绝不调用 <see cref="GreyboxSceneSetup"/>** —— 校验器一旦重建被校验对象，
    /// 就只能证明"生成器自洽"，不能证明"仓库里那份场景是对的"。需要重建请单独跑
    /// `Billiards/Visual/Build Day-4 Physics Greybox Scene`。
    ///
    /// 产出 <c>Docs/Reports/d4-verification.json</c>（可用 <c>-outputDir</c> 重定向，走
    /// <see cref="ReportOutput.ResolveReportPath"/> 单点），沿用
    /// <see cref="SetupReport"/> / <see cref="CheckResult"/> 模型。
    ///
    /// 覆盖断言（spec §10.3 R-J + 团队分工）：
    ///  · <c>D4-SCN-01</c> 灰盒场景文件存在
    ///  · <c>D4-SCN-02</c> 场景内 <c>CushionGizmo</c> 与 <c>PhysicsTestDrive</c> 各恰有 1 个（含非空转证据）
    ///  · <c>D4-PHY-01</c> <c>Runtime/Physics/**.cs</c> 无刚体/碰撞体类型引用
    ///  · <c>D4-BAN-01</c> Scenes + Prefabs 全量扫描无 Rigidbody/Collider（复用
    ///    <see cref="PhysicsBanAudit"/> 单点实现，铁律 16）
    ///  · <c>D4-BLD-01</c> build list 场景数 == 1（且为 <c>Main.unity</c>）—— R-J 断言，
    ///    防止灰盒场景被误注册而作废 Day 1 的 <c>SCN-02</c> 证据
    /// </summary>
    public static class PhysicsValidator
    {
        public const string ReportFileName = "d4-verification.json";
        public const string SchemaId = "billiards.day4-verification.v1";

        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string PhysicsSourceDir = "Assets/_Project/Scripts/Runtime/Physics";

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Verify/Validate Day-4 Physics Greybox", false, 403)]
        public static void ValidateInteractive()
        {
            int exitCode = RunAndWrite();
            Debug.Log("Day-4 验收退出码：" + exitCode);
        }

        /// <summary>供 <c>-executeMethod</c> 使用；退出码 0 = 全过，1 = 有 FAIL，2 = 抛异常。</summary>
        public static void ValidateFromCommandLine()
        {
            int exitCode = RunAndWrite();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static int RunAndWrite()
        {
            SetupReport report;
            int exitCode;
            try
            {
                report = RunAll();
                exitCode = report.failCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("Day-4 校验过程中抛出异常：" + ex);
                return 2;
            }

            report.schema = SchemaId;
            // 报告路径走单点实现（ReportOutput.ResolveReportPath），支持 -outputDir。
            string path = report.WriteToDisk(ReportFileName);
            Debug.Log(report.ToConsoleSummary());
            Debug.Log("验收报告已写入：" + path);
            Debug.Log("DAY4_RESULT=" + report.overallStatus
                      + " PASS=" + report.passCount + " WARN=" + report.warnCount + " FAIL=" + report.failCount);
            return exitCode;
        }

        // ── 全量检查 ───────────────────────────────────────────────────────

        public static SetupReport RunAll()
        {
            var report = new SetupReport { schema = SchemaId };

            VerifyGreyboxScene(report);
            VerifyPhysicsSourcesArePhysXFree(report);
            VerifyPhysicsBanScan(report);
            VerifyBuildSettings(report);

            report.Finalise();
            return report;
        }

        // ── 灰盒场景 ───────────────────────────────────────────────────────

        /// <summary>
        /// 灰盒场景结构扫描结果（**本工程唯一实现**，验收器与 EditMode 测试共用 —— 铁律 16）。
        /// </summary>
        public struct GreyboxScan
        {
            public bool SceneFileExists;
            public int RootObjectCount;
            public int GizmoCount;
            public int DriveCount;
            public string Detail;
        }

        /// <summary>
        /// 以**只读**方式打开灰盒场景并统计关键组件数。不修改、不保存任何资产；
        /// 若场景已在内存中加载则直接复用（不重复打开、也不关闭调用方已打开的场景）。
        /// </summary>
        public static GreyboxScan ScanGreyboxScene()
        {
            var scan = new GreyboxScan { Detail = string.Empty };
            scan.SceneFileExists = ProjectPaths.Exists(GreyboxSceneSetup.ScenePath);
            if (!scan.SceneFileExists)
            {
                scan.Detail = "场景文件不存在：" + GreyboxSceneSetup.ScenePath;
                return scan;
            }

            string full = ProjectPaths.ToFullPath(GreyboxSceneSetup.ScenePath);
            bool opened = false;

            Scene scene = SceneManager.GetSceneByPath(GreyboxSceneSetup.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    GreyboxSceneSetup.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                scan.RootObjectCount = roots.Length;
                foreach (GameObject root in roots)
                {
                    scan.GizmoCount += root.GetComponentsInChildren<CushionGizmo>(true).Length;
                    scan.DriveCount += root.GetComponentsInChildren<PhysicsTestDrive>(true).Length;
                }
            }
            finally
            {
                if (opened)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }

            scan.Detail = $"{Path.GetFileName(full)}：rootObjects={scan.RootObjectCount} "
                          + $"CushionGizmo={scan.GizmoCount} PhysicsTestDrive={scan.DriveCount}";
            return scan;
        }

        private static void VerifyGreyboxScene(SetupReport report)
        {
            GreyboxScan scan = ScanGreyboxScene();

            report.Add(scan.SceneFileExists
                ? CheckResult.Ok("D4-SCN-01", "灰盒场景 PhysicsGreybox.unity 存在",
                    GreyboxSceneSetup.ScenePath, GreyboxSceneSetup.ScenePath)
                : CheckResult.Fail("D4-SCN-01", "灰盒场景 PhysicsGreybox.unity 存在",
                    GreyboxSceneSetup.ScenePath, "未找到"));

            if (!scan.SceneFileExists)
            {
                report.Add(CheckResult.Fail("D4-SCN-02", "场景内 CushionGizmo / PhysicsTestDrive 各恰有 1 个",
                    "1 / 1", "无法检查（场景缺失）"));
                return;
            }

            bool nonVacuous = scan.RootObjectCount > 0;
            bool ok = nonVacuous && scan.GizmoCount == 1 && scan.DriveCount == 1;

            report.Add(ok
                ? CheckResult.Ok("D4-SCN-02", "场景内 CushionGizmo / PhysicsTestDrive 各恰有 1 个",
                    "1 / 1（且根对象数 > 0）", scan.Detail)
                : CheckResult.Fail("D4-SCN-02", "场景内 CushionGizmo / PhysicsTestDrive 各恰有 1 个",
                    "1 / 1（且根对象数 > 0）",
                    scan.Detail + (nonVacuous ? "" : "；**扫描空转**（0 个根对象）")));
        }

        // ── 源码级：Physics/ 下不得出现 PhysX 组件类型 ──────────────────────

        /// <summary>
        /// 定点检查 <c>Runtime/Physics/**.cs</c> 的**源码文本**里不出现 <c>Rigidbody</c> / <c>Collider</c> 标识符。
        /// **只跳过注释行**（行首空白之后以 <c>//</c> 起头，含 <c>///</c>），**不跳过字符串字面量**。
        ///
        /// <para>
        /// **为什么必须保留字符串**：本检查的**唯一独有覆盖**是「字符串编码的 PhysX 类型名」——
        /// <c>Type.GetType("UnityEngine.Rigidbody, UnityEngine.PhysicsModule")</c> 这类写法
        /// **不产生任何编译期引用**（程序集级检查看不见它），但文本里出现了该名字 ⇒ 只有本检查抓得到。
        /// **跳过字符串 = 等于把这个检查删掉。**
        /// </para>
        /// <para>
        /// 本检查**接受**「字符串里出现这两个词」的假阳性 —— **那是本检查的覆盖来源，不是缺陷**。
        /// **不得**与 <c>PhysicsBanGuardTests.RuntimeAssembly_DoesNotReferencePhysXModule</c> 合并：
        /// 后者抓编译期类型引用（抓不到字符串形态），本检查抓文本名字（抓不到纯 <c>Physics.</c> 调用）——
        /// **两者两向互补**。若真要合并，合并后的实现必须**同时**扫类型引用与字符串名字。
        /// </para>
        /// <para>
        /// 刻意**不**剥离行尾注释：那需要真正的词法分析（字符串里可能含 <c>"//"</c>，例如 URL），
        /// 朴素切割会误删字符串内容 —— 而字符串恰是本检查的覆盖来源。故只跳**整行注释**。
        /// </para>
        /// </summary>
        private static void VerifyPhysicsSourcesArePhysXFree(SetupReport report)
        {
            string dir = ProjectPaths.ToFullPath(PhysicsSourceDir);
            var hits = new List<string>();
            int scanned = 0;
            int commentLinesSkipped = 0;

            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);   // 确定性顺序

                var pattern = new Regex(@"\b(Rigidbody|Collider)\b");
                foreach (string file in files)
                {
                    scanned++;
                    int line = 0;
                    foreach (string text in File.ReadLines(file))
                    {
                        line++;

                        // 只跳过**整行注释**（见上方 doc 注释：不剥离行尾注释）。
                        if (text.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        {
                            commentLinesSkipped++;
                            continue;
                        }

                        if (pattern.IsMatch(text))
                        {
                            hits.Add(Path.GetFileName(file) + ":" + line);
                        }
                    }
                }
            }

            bool ok = scanned > 0 && hits.Count == 0;
            string actual = $"scanned={scanned} file(s), commentLinesSkipped={commentLinesSkipped} → hits={hits.Count}"
                            + (hits.Count > 0 ? " [" + string.Join(", ", hits) + "]" : string.Empty);

            report.Add(ok
                ? CheckResult.Ok("D4-PHY-01", "Runtime/Physics/ 源码无 Rigidbody/Collider 名字（含字符串字面量，只跳注释行）",
                    "scanned > 0 且 hits == 0", actual)
                : CheckResult.Fail("D4-PHY-01", "Runtime/Physics/ 源码无 Rigidbody/Collider 名字（含字符串字面量，只跳注释行）",
                    "scanned > 0 且 hits == 0", actual));
        }

        // ── 场景 + prefab 全量守卫（单点实现复用）──────────────────────────

        private static void VerifyPhysicsBanScan(SetupReport report)
        {
            PhysicsBanAudit.Scan scan = PhysicsBanAudit.ScanAll();
            bool ok = scan.RigidbodyCount == 0 && scan.ColliderCount == 0 && scan.CoverageIsNonVacuous;
            string actual =
                $"scene={scan.SceneCount} prefab={scan.PrefabCount} → Rigidbody={scan.RigidbodyCount} Collider={scan.ColliderCount}";

            report.Add(ok
                ? CheckResult.Ok("D4-BAN-01", "Scenes + Prefabs 无 Rigidbody/Collider（含新灰盒场景）",
                    "0 / 0，且扫描非空转", actual,
                    "复用 `PhysicsBanAudit` 单点实现（铁律 16）；扫描范围含本切片新增的 PhysicsGreybox.unity。")
                : CheckResult.Fail("D4-BAN-01", "Scenes + Prefabs 无 Rigidbody/Collider（含新灰盒场景）",
                    "0 / 0，且扫描非空转",
                    actual + (scan.Offenders.Count > 0 ? "；违规：" + string.Join(" | ", scan.Offenders) : "（扫描空转！）")));
        }

        // ── build list（R-J）───────────────────────────────────────────────

        private static void VerifyBuildSettings(SetupReport report)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            bool ok = scenes.Length == 1 && scenes[0].path == MainScenePath && scenes[0].enabled;

            string actual = scenes.Length == 0
                ? "<空>"
                : string.Join(", ", Array.ConvertAll(scenes,
                    s => s.path + (s.enabled ? " [enabled]" : " [disabled]")));

            report.Add(ok
                ? CheckResult.Ok("D4-BLD-01", "build list 场景数 == 1（灰盒场景未被注册）",
                    "仅 " + MainScenePath, actual,
                    "R-J：灰盒场景**刻意不注册**。若注册，Day 1 的 SCN-02（build list 恰含 Main.unity）会失败、其 26/26 证据作废。")
                : CheckResult.Fail("D4-BLD-01", "build list 场景数 == 1（灰盒场景未被注册）",
                    "仅 " + MainScenePath, actual));
        }
    }
}
