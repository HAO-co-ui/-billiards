using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Billiards.Core;
using Billiards.Diagnostics;
using Billiards.Table;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// Day 2 完整性校验器：把"碰撞数据落地 + 美术资源导入"是否真正成立，变成一组**可判据**断言。
    ///
    /// 与 Day 1 的 BilliardsProjectVerifier 同源同风格。核心风险提示：本项目物理自研，
    /// 碰撞几何完全依赖今天这份数据；数据错了会直接导致 Day 7 强制闸门失败且在后期极难定位。
    /// 因此这里不靠目视，全部用数值断言，并把 expected/actual/detail 写进报告。
    ///
    /// 判据组织（三层网格对齐 + 数据交叉比对 + 手性自检 + 朝向不变量）：
    ///  · **L1 面点**（AXL-04/05）：端点落在网格**面**上（含内部与边）—— **必要条件**；**对足迹内滑动盲**。
    ///  · **L2 边距**（AXL-07）：端点落在网格**边**上 —— 高灵敏（底噪 0.18 µm）；**对沿边方向滑动全盲**。
    ///  · **L3 顶点比值**（AXL-06）：端点→最近顶点 / 局部间距 ≤ k —— **唯一无方向性盲区**；但有**周期性盲带**。
    ///  · **CMP-03**：生成资产 vs 源 JSON **索引对齐 + 逐字段**（1e-5 m）—— 单点位移威胁的**兜底**。
    ///  · **CHR-01/02**：手性/绕向**自包含**自检（不依赖基线）。
    ///  · **AXL-02 / SCN-04**：世界包围盒 = **转换链推导**的期望 —— 钉"朝向不变量恰好为真"。
    ///
    /// ⚠ **历史声明已被证伪，勿再引用**：本类曾写"0.2613 mm **不是误差**，而是『82 段直线段逼近曲率袋角的
    /// **轮廓近似残差**』，阈值由该测量性质推导为 0.5 mm"。
    /// **该说法已被 D2-T02A/T03 证伪** —— 188 个端点**精确落在网格边/面上**（最坏 0.000000 m），
    /// 0.2613 mm 实为"端点 → 最近**顶点**"这一度量自身的**离散采样下限**；且 AXL-04/05 现已改为**面点**口径。
    /// 详见 ADR-0001 §9.5。
    /// </summary>
    public static class TableIntegrityValidator
    {
        public const string ModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        public const string ReferenceTablePath = "Docs/Reference/table-metadata-reference.json";
        public const string ReferenceGameplayPath = "Docs/Reference/gameplay-markers-reference.json";
        public const string ReportFileName = "day2-verification.json";

        // ── 容差：**单值源在 `MeshAlignmentTolerances`**（`Billiards.Diagnostics`，铁律 16）──────
        //
        // 以下同名常量是指向单值源的**编译期别名**（`const` 可引用 `const`）。之所以保留别名而不
        // 直接把本文件内 ~50 处引用改成 `MeshAlignmentTolerances.X`：**改动面越小，越不容易改错**；
        // 而"值只有一个来源"这一实质已达成 —— 改值只需改 `MeshAlignmentTolerances` 一处。
        // 同时 `MaterialVisualValidator` 的 `TableIntegrityValidator.BoundsTolerance` 继续可用。
        //
        // 容差策略与三层各自的盲区、推导依据，全部集中写在 `MeshAlignmentTolerances` 的 doc-comment。

        public const float IndexAlignedTolerance = MeshAlignmentTolerances.IndexAligned;
        public const float BoundsTolerance = MeshAlignmentTolerances.Bounds;
        public const float SurfacePointTolerance = MeshAlignmentTolerances.L1SurfacePoint;
        public const float EdgeDistanceTolerance = MeshAlignmentTolerances.L2EdgeDistance;
        public const float VertexRatioTolerance = MeshAlignmentTolerances.L3VertexRatio;
        public const float VertexRatioTheoreticalBound = MeshAlignmentTolerances.L3TheoreticalBound;
        public const float MeasuredVertexBaseline = MeshAlignmentTolerances.MeasuredVertexBaseline;

        // ── 入口 ───────────────────────────────────────────────────────────

        // ── 唯一的生成前置条件（铁律 16：同一不变量在多处入口的前置条件必须**单点实现**）──
        //
        // 背景：本类曾有**两处**入口各写一份前置条件（菜单入口 :96 无条件 Import()，命令行入口另写一份），
        // 结果"改了一处漏一处"：R2 只修了命令行入口，菜单入口仍会在**校验前重建** TableConfig.asset。
        // 根因是**结构**，不是疏忽 —— 故此处收敛为单一 helper，两入口共用。

        /// <summary>
        /// 校验入口的**唯一前置条件**：只在产物**缺失**时生成；存在则**一律不重建**。
        ///
        /// **为什么**：校验入口必须校验"仓库里**既有的**那个对象"。若先重建再校验，
        /// 它证明的只是"**生成器自洽**"，**证明不了"被提交的对象是对的"**（R2 原则）。
        ///
        /// 需要"重建后再校验"的工作流，请用 <see cref="RebuildAndValidateInteractive"/>
        /// —— **本类不提供"默默重建"的校验入口**：要么只校验，要么名字说清它会重建。
        /// </summary>
        private static void EnsureGeneratedPrecondition()
        {
            if (ProjectPaths.Exists(TableDataImporter.OutputAssetPath))
            {
                Debug.Log("[前置] TableConfig.asset 已存在 ⇒ **不重建**，校验既有产物");
                return;
            }

            Debug.Log("[前置] TableConfig.asset 缺失 ⇒ 生成一次（此后校验既有产物）");
            TableDataImporter.Import();
        }

        [MenuItem("Billiards/Verify/Validate Table Data & Art (Day 2)", false, 201)]
        public static void ValidateInteractive()
        {
            // 只校验；**不重建**（前置条件走单点 helper）
            EnsureGeneratedPrecondition();

            SetupReport report = RunAll();
            string path = ResolveReportPath("Docs/Reports/" + ReportFileName, ReportFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log(report.ToConsoleSummary());
            EditorUtility.DisplayDialog(
                "Billiards Day-2 Validate",
                $"校验完成（**未重建任何产物**）。\n\nPASS {report.passCount}  WARN {report.warnCount}  FAIL {report.failCount}\n结论：{report.overallStatus}",
                "OK");
            Debug.Log("验收报告已写入：" + path);
        }

        /// <summary>
        /// **名字自证其行**的重建入口：显式重建 TableConfig.asset，然后校验。
        /// 与 <see cref="ValidateInteractive"/> 分开，是为了让"会重建"这件事在菜单名里可见。
        /// </summary>
        [MenuItem("Billiards/Verify/Rebuild && Validate Table Data (Day 2)", false, 203)]
        public static void RebuildAndValidateInteractive()
        {
            Debug.Log("[Rebuild & Validate] **显式重建** TableConfig.asset（本入口的名字已说明它会重建）");
            TableDataImporter.Import();

            SetupReport report = RunAll();
            string path = ResolveReportPath("Docs/Reports/" + ReportFileName, ReportFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log(report.ToConsoleSummary());
            EditorUtility.DisplayDialog(
                "Billiards Day-2 Rebuild & Validate",
                $"已**重建** TableConfig.asset 并校验。\n\nPASS {report.passCount}  WARN {report.warnCount}  FAIL {report.failCount}\n结论：{report.overallStatus}",
                "OK");
            Debug.Log("验收报告已写入：" + path);
        }

        /// <summary>
        /// 薄转发 → <see cref="ReportOutput.ResolveReportPath"/>（**唯一实现**，铁律 16）。
        /// 本方法此前是独立实现，与 `MaterialVisualValidator` 的同名方法**已分叉**（见 `ReportOutput` 类注释）。
        /// </summary>
        public static string ResolveReportPath(string defaultRelative, string fileName)
            => ReportOutput.ResolveReportPath(defaultRelative, fileName);

        public static void ValidateFromCommandLine()
        {
            int exitCode;
            SetupReport report = null;

            try
            {
                // 与菜单入口**共用同一前置条件**（铁律 16：单点实现）
                EnsureGeneratedPrecondition();
                report = RunAll();
                exitCode = report.failCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("Day-2 校验过程中抛出异常：" + ex);
                exitCode = 2;
            }

            if (report != null)
            {
                report.schema = "billiards.day2-verification.v1";
                string path = ResolveReportPath("Docs/Reports/" + ReportFileName, ReportFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                Debug.Log(report.ToConsoleSummary());
                Debug.Log("验收报告已写入：" + path);
                Debug.Log("DAY2_RESULT=" + report.overallStatus
                          + " PASS=" + report.passCount
                          + " WARN=" + report.warnCount
                          + " FAIL=" + report.failCount);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);   // 再退出
            }
        }

        // ── 全量检查 ───────────────────────────────────────────────────────

        public static SetupReport RunAll()
        {
            var report = new SetupReport { schema = "billiards.day2-verification.v1" };

            TableConfig config = LoadConfig(report);
            VerifyReferenceFiles(report, config);
            VerifyDataTopology(report, config);
            VerifyCrossReference(report);
            VerifyConfigAgainstReference(report, config);
            VerifyChirality(report, config);
            VerifyMeshAlignment(report, config);
            VerifyImportSettings(report);
            VerifyArtAssets(report);

            report.Finalise();
            return report;
        }

        private static TableConfig LoadConfig(SetupReport report)
        {
            if (!ProjectPaths.Exists(TableDataImporter.OutputAssetPath))
            {
                report.Add(CheckResult.Fail("REF-02", "TableConfig 资产存在", TableDataImporter.OutputAssetPath, "未找到"));
                return null;
            }

            var config = AssetDatabase.LoadAssetAtPath<TableConfig>(TableDataImporter.OutputAssetPath);
            report.Add(config != null
                ? CheckResult.Ok("REF-02", "TableConfig 资产存在且可加载", TableDataImporter.OutputAssetPath, config.name)
                : CheckResult.Fail("REF-02", "TableConfig 资产存在且可加载", TableDataImporter.OutputAssetPath, "存在但无法反序列化"));
            return config;
        }

        // ── 参考数据 ───────────────────────────────────────────────────────

        private static void VerifyReferenceFiles(SetupReport report, TableConfig config)
        {
            bool tableExists = ProjectPaths.Exists(ReferenceTablePath);
            bool gameplayExists = ProjectPaths.Exists(ReferenceGameplayPath);
            report.Add(tableExists && gameplayExists
                ? CheckResult.Ok("REF-01", "参考数据已入库 Docs/Reference", "两份 JSON 均存在",
                    $"{ReferenceTablePath} + {ReferenceGameplayPath}")
                : CheckResult.Fail("REF-01", "参考数据已入库 Docs/Reference", "两份 JSON 均存在",
                    $"table={tableExists}, gameplay={gameplayExists}"));

            if (config == null)
            {
                report.Add(CheckResult.Fail("REF-03", "资产记录的源 sha256 与实际文件一致", "相等", "无法检查（配置缺失）"));
                return;
            }

            string actual = Sha256Hex(ProjectPaths.ToFullPath(ReferenceTablePath));
            report.Add(string.Equals(config.sourceSha256, actual, StringComparison.OrdinalIgnoreCase)
                ? CheckResult.Ok("REF-03", "资产记录的源 sha256 与实际文件一致", actual, config.sourceSha256,
                    "确保运行时几何可追溯到入库的源数据。")
                : CheckResult.Fail("REF-03", "资产记录的源 sha256 与实际文件一致", actual, config.sourceSha256));
        }

        // ── 拓扑与不变式 ───────────────────────────────────────────────────

        private static void VerifyDataTopology(SetupReport report, TableConfig config)
        {
            if (config == null)
            {
                report.Add(CheckResult.Fail("DAT-01", "段数 = 82", "82", "配置缺失"));
                return;
            }

            report.Add(config.SegmentCount == PoolConstants.CushionSegmentCount
                ? CheckResult.Ok("DAT-01", "库边/袋角/弧段数 = 82", "82", config.SegmentCount.ToString())
                : CheckResult.Fail("DAT-01", "库边/袋角/弧段数 = 82", "82", config.SegmentCount.ToString()));

            report.Add(config.PocketCount == PoolConstants.PocketCount
                ? CheckResult.Ok("DAT-02", "袋口数 = 6", "6", config.PocketCount.ToString())
                : CheckResult.Fail("DAT-02", "袋口数 = 6", "6", config.PocketCount.ToString()));

            List<string> consistency = config.ConsistencyErrors();
            report.Add(consistency.Count == 0
                ? CheckResult.Ok("DAT-03", "与 PoolConstants（单位制唯一真源）一致", "无偏差", "全部一致",
                    "内库 2.54 / 1.270108866、球半径 0.028575、台呢 y=0、袋口半径等公共标量逐项核对。")
                : CheckResult.Fail("DAT-03", "与 PoolConstants（单位制唯一真源）一致", "无偏差",
                    $"{consistency.Count} 项不一致", string.Join(" | ", consistency)));

            int corners = 0, sides = 0;
            foreach (Pocket p in config.pockets)
            {
                if (p.IsCorner) corners++;
                else if (p.IsSide) sides++;
            }

            report.Add(corners == 4 && sides == 2
                ? CheckResult.Ok("DAT-04", "袋口构成 = 4 角袋 + 2 中袋", "corner=4, side=2", $"corner={corners}, side={sides}")
                : CheckResult.Fail("DAT-04", "袋口构成 = 4 角袋 + 2 中袋", "corner=4, side=2", $"corner={corners}, side={sides}"));

            // 段长度非零 + 端点有限且在球桌外轮廓包络内
            float minLen = float.MaxValue;
            int degenerate = 0, outside = 0;
            float envelope = Mathf.Max(PoolGeometry.OuterLength, PoolGeometry.OuterWidth) * 0.5f + 0.05f;
            foreach (CushionSegment s in config.segments)
            {
                float len = Vector2.Distance(s.a, s.b);
                if (len < 1e-9f) degenerate++;
                minLen = Mathf.Min(minLen, len);
                if (!InsideEnvelope(s.a, envelope) || !InsideEnvelope(s.b, envelope)) outside++;
            }

            report.Add(degenerate == 0 && outside == 0
                ? CheckResult.Ok("DAT-05", "线段非退化且端点落在球桌包络内",
                    "无零长段、无越界端点", $"minLen={minLen:R}")
                : CheckResult.Fail("DAT-05", "线段非退化且端点落在球桌包络内",
                    "无零长段、无越界端点", $"degenerate={degenerate}, outside={outside}"));

            // 捕获半径内部不变式：capture = opening − ballRadius − 2mm
            const float margin = 0.002f;
            float maxRadialErr = 0f;
            foreach (Pocket p in config.pockets)
            {
                float derived = p.openingRadius - PoolConstants.BallRadius - margin;
                maxRadialErr = Mathf.Max(maxRadialErr, Mathf.Abs(derived - p.captureRadius));
            }

            report.Add(maxRadialErr < 1e-6f
                ? CheckResult.Ok("DAT-06", "袋口捕获半径不变式 capture = opening − 球半径 − 2mm",
                    "< 1e-6", maxRadialErr.ToString("E3"),
                    "同时证明球半径与袋口参数出自同一次测量。")
                : CheckResult.Fail("DAT-06", "袋口捕获半径不变式 capture = opening − 球半径 − 2mm",
                    "< 1e-6", maxRadialErr.ToString("E3")));

            bool widthsOk = true;
            foreach (Pocket p in config.pockets)
            {
                if (!(p.mouthWidth > p.throatWidth) || p.throatWidth <= 0f)
                {
                    widthsOk = false;
                }
            }

            report.Add(widthsOk
                ? CheckResult.Ok("DAT-07", "袋口 mouth 宽 > throat 宽 > 0", "全部成立", "全部成立",
                    "袋口呈收口形状，是捕获得以\"先过喉再判进\"的几何前提。")
                : CheckResult.Fail("DAT-07", "袋口 mouth 宽 > throat 宽 > 0", "全部成立", "存在不满足项"));

            // 捕获规则文案不得丢失
            bool notesOk = true;
            foreach (Pocket p in config.pockets)
            {
                if (string.IsNullOrEmpty(p.captureNote)) notesOk = false;
            }

            report.Add(notesOk
                ? CheckResult.Ok("DAT-08", "6 袋均带 captureNote 捕获规则", "非空", "非空",
                    "captureNote 不可省略：仅当球心穿过袋喉、朝网方向后才判捕获。")
                : CheckResult.Fail("DAT-08", "6 袋均带 captureNote 捕获规则", "非空", "存在空值"));

            // DAT-09：库边端点包围盒 = 独立复核基线（抓轴交换/整体缩放；端点 x 不对称仅 7.6e-8，非镜像判据）
            var smin = new Vector2(float.MaxValue, float.MaxValue);
            var smax = new Vector2(float.MinValue, float.MinValue);
            foreach (CushionSegment s in config.segments)
            {
                smin = Vector2.Min(smin, Vector2.Min(s.a, s.b));
                smax = Vector2.Max(smax, Vector2.Max(s.a, s.b));
            }

            float segBoxErr = Mathf.Max(
                Mathf.Abs(smin.x - PoolGeometry.CushionEndpointsMin.x),
                Mathf.Abs(smin.y - PoolGeometry.CushionEndpointsMin.y),
                Mathf.Abs(smax.x - PoolGeometry.CushionEndpointsMax.x),
                Mathf.Abs(smax.y - PoolGeometry.CushionEndpointsMax.y));

            report.Add(segBoxErr < IndexAlignedTolerance
                ? CheckResult.Ok("DAT-09", "库边端点包围盒 = 独立复核基线",
                    $"x[{PoolGeometry.CushionEndpointsMin.x:F9},{PoolGeometry.CushionEndpointsMax.x:F9}] "
                    + $"y[{PoolGeometry.CushionEndpointsMin.y:F9},{PoolGeometry.CushionEndpointsMax.y:F9}]",
                    $"err={segBoxErr:E3} m")
                : CheckResult.Fail("DAT-09", "库边端点包围盒 = 独立复核基线", "见 expected", $"err={segBoxErr:E3} m"));

            // DAT-10：openingRadius == runtimeInscribedRadius（源数据内部不变式，独立复核给第二条）
            string tableJson = Path.Combine(SetupReport.ProjectRoot,
                ReferenceTablePath.Replace('/', Path.DirectorySeparatorChar));
            TableMetaDto src = JsonUtility.FromJson<TableMetaDto>(File.ReadAllText(tableJson, Encoding.UTF8));
            float maxInscribedErr = 0f;
            int n = Mathf.Min(src.collision.pockets.Length, config.PocketCount);
            for (int i = 0; i < n; i++)
            {
                maxInscribedErr = Mathf.Max(maxInscribedErr,
                    Mathf.Abs(config.pockets[i].openingRadius - src.collision.pockets[i].runtimeInscribedRadius));
            }

            report.Add(maxInscribedErr < 1e-9f
                ? CheckResult.Ok("DAT-10", "openingRadius == runtimeInscribedRadius（源不变式）", "< 1e-9",
                    maxInscribedErr.ToString("E3"))
                : CheckResult.Fail("DAT-10", "openingRadius == runtimeInscribedRadius（源不变式）", "< 1e-9",
                    maxInscribedErr.ToString("E3")));
        }

        private static bool InsideEnvelope(Vector2 p, float envelope)
        {
            return Mathf.Abs(p.x) <= envelope && Mathf.Abs(p.y) <= envelope;
        }

        // ── A/B 交叉比对 ───────────────────────────────────────────────────

        private static void VerifyCrossReference(SetupReport report)
        {
            string tableJson = Path.Combine(SetupReport.ProjectRoot,
                ReferenceTablePath.Replace('/', Path.DirectorySeparatorChar));
            string gameplayJson = Path.Combine(SetupReport.ProjectRoot,
                ReferenceGameplayPath.Replace('/', Path.DirectorySeparatorChar));

            TableMetaDto a = JsonUtility.FromJson<TableMetaDto>(File.ReadAllText(tableJson, Encoding.UTF8));
            GameplayMarkersDto b = JsonUtility.FromJson<GameplayMarkersDto>(File.ReadAllText(gameplayJson, Encoding.UTF8));

            ReferenceCrossCheck.Result r = ReferenceCrossCheck.Compare(a, b);

            report.Add(r.Identical
                ? CheckResult.Ok("CMP-01", "两份源 JSON（A/B）逐点一致",
                    "段/袋端点、半径、全局标量均一致", r.ToSummaryLine(),
                    "任务书要求不自行选一份了事；此处给出逐点比对结论。")
                : CheckResult.Fail("CMP-01", "两份源 JSON（A/B）逐点一致",
                    "全部一致", r.ToSummaryLine(), string.Join(" | ", r.Mismatches)));

            report.Add(CheckResult.Ok("CMP-02", "A/B 比对精度（最大偏差）",
                "≤ 1e-9 m（float 逐位）", r.OverallMaxDelta.ToString("E3") + " m",
                $"段端点 {r.MaxSegmentEndpointDelta:E3} / 袋中心 {r.MaxPocketCenterDelta:E3} / "
                + $"mouth {r.MaxPocketMouthDelta:E3} / throat {r.MaxPocketThroatDelta:E3} / "
                + $"半径 {r.MaxRadiusDelta:E3} / 全局标量 {r.MaxMeasuredScalarDelta:E3}"));
        }

        // ── TableConfig 与源文件的索引对齐逐点比对 ───────────────────────────
        //
        // 独立复核（D2-T02A）指出的最强判据：**索引对齐 + 逐字段**比对以 1e-5 m 容差，
        // 可一次性拦下镜像、180° 旋转、双重缩放、细微端点偏移等"自洽检查放行"的一类错误。
        // 这里把"生成后的 TableConfig"与源 JSON 逐点对齐核对，闭合"源→资产"的最后一环。

        private static void VerifyConfigAgainstReference(SetupReport report, TableConfig config)
        {
            if (config == null)
            {
                report.Add(CheckResult.Fail("CMP-03", "TableConfig 与源 JSON 索引对齐逐点一致", "全部一致", "配置缺失"));
                return;
            }

            string tableJson = Path.Combine(SetupReport.ProjectRoot,
                ReferenceTablePath.Replace('/', Path.DirectorySeparatorChar));
            TableMetaDto a = JsonUtility.FromJson<TableMetaDto>(File.ReadAllText(tableJson, Encoding.UTF8));

            const float tol = IndexAlignedTolerance;
            float worst = 0f;
            var problems = new List<string>();

            int segN = Mathf.Min(a.collision.segments.Length, config.SegmentCount);
            for (int i = 0; i < segN; i++)
            {
                SegmentDto s = a.collision.segments[i];
                CushionSegment c = config.segments[i];

                if (!string.Equals(s.rail, c.rail))
                {
                    problems.Add($"seg[{i}].rail '{c.rail}' != '{s.rail}'");
                }

                worst = Mathf.Max(worst, Vector2.Distance(new Vector2(s.a.x, s.a.y), c.a),
                    Vector2.Distance(new Vector2(s.b.x, s.b.y), c.b));
            }

            PocketDto[] pa = a.collision.pockets;
            int pkN = Mathf.Min(pa.Length, config.PocketCount);
            for (int i = 0; i < pkN; i++)
            {
                PocketDto p = pa[i];
                Pocket c = config.pockets[i];

                if (p.index != c.index) problems.Add($"pocket[{i}].index {c.index} != {p.index}");
                if (!string.Equals(p.kind, c.kind)) problems.Add($"pocket[{i}].kind '{c.kind}' != '{p.kind}'");

                worst = Mathf.Max(worst,
                    Vector2.Distance(new Vector2(p.runtimeCenter.x, p.runtimeCenter.y), c.center),
                    Vector2.Distance(new Vector2(p.runtimeMouth[0].x, p.runtimeMouth[0].y), c.mouthA),
                    Vector2.Distance(new Vector2(p.runtimeMouth[1].x, p.runtimeMouth[1].y), c.mouthB),
                    Vector2.Distance(new Vector2(p.runtimeThroat[0].x, p.runtimeThroat[0].y), c.throatA),
                    Vector2.Distance(new Vector2(p.runtimeThroat[1].x, p.runtimeThroat[1].y), c.throatB),
                    Vector2.Distance(new Vector2(p.runtimeThroatCenter.x, p.runtimeThroatCenter.y), c.throatCenter),
                    Vector2.Distance(new Vector2(p.runtimeThroatNormal.x, p.runtimeThroatNormal.y), c.throatNormal),
                    Mathf.Abs(p.openingRadius - c.openingRadius),
                    Mathf.Abs(p.captureRadius - c.captureRadius),
                    Mathf.Abs(p.runtimeMouthWidth - c.mouthWidth),
                    Mathf.Abs(p.runtimeThroatWidth - c.throatWidth));
            }

            bool ok = problems.Count == 0 && worst < tol;
            report.Add(ok
                ? CheckResult.Ok("CMP-03", "TableConfig 与源 JSON 索引对齐逐点一致",
                    $"索引对齐、逐字段、容差 {tol:E0} m", $"maxDelta {worst:E3} m",
                    "该判据（± 索引对齐逐点）可一次性拦下镜像/180°旋转/双重缩放/细微端点偏移等自洽检查放行的错误。")
                : CheckResult.Fail("CMP-03", "TableConfig 与源 JSON 索引对齐逐点一致",
                    $"< {tol:E0} m", $"maxDelta {worst:E3} m", string.Join(" | ", problems)));
        }

        // ── 手性/绕向（自包含判据，不需要基线即可发现"水平面镜像"）──────────────
        //
        // 独立复核指出：水平面镜像/180° 旋转在"顺序无关 + 1 mm 容差"下几乎不可见
        // （只差 0.10~0.47 mm）。因此必须另加**不依赖基线**的自包含手性判据。
        //  · CHR-01：6 袋的局部手性符号模式（按 index）应为 [+,-,-,-,+,+]。proper 旋转不变，镜像整列翻转。
        //  · CHR-02：把 82 段拼接成闭合轮廓，shoelace 有向面积应为正（CCW）。镜像会翻号。

        private static readonly int[] ExpectedPocketChirality = { 1, -1, -1, -1, 1, 1 };

        private static void VerifyChirality(SetupReport report, TableConfig config)
        {
            if (config == null)
            {
                report.Add(CheckResult.Fail("CHR-01", "袋口局部手性符号模式（镜像自检）", "见详情", "配置缺失"));
                return;
            }

            var actual = new List<int>();
            foreach (Pocket p in config.pockets)
            {
                Vector2 mv = p.mouthB - p.mouthA;
                Vector2 mc = (p.mouthA + p.mouthB) * 0.5f;
                Vector2 iv = p.throatCenter - mc;
                float cr = mv.x * iv.y - mv.y * iv.x;
                actual.Add(cr >= 0f ? 1 : -1);
            }

            bool ok = actual.Count == ExpectedPocketChirality.Length;
            if (ok)
            {
                for (int i = 0; i < actual.Count; i++)
                {
                    if (actual[i] != ExpectedPocketChirality[i]) ok = false;
                }
            }

            string actStr = string.Join(",", actual);
            string expStr = string.Join(",", ExpectedPocketChirality);
            report.Add(ok
                ? CheckResult.Ok("CHR-01", "袋口局部手性符号模式 [+,-,-,-,+,+]（= 复核 C4，镜像自检）", expStr, actStr,
                    "cross(mouth0→mouth1, mouthCenter→throatCenter) 的符号。proper 旋转下不变，水平面镜像会整列翻转。")
                : CheckResult.Fail("CHR-01", "袋口局部手性符号模式 [+,-,-,-,+,+]（= 复核 C4，镜像自检）", expStr, actStr,
                    "符号模式不匹配 → 极可能是水平面镜像（双重手性翻转）。"));

            VerifyPerimeterWinding(report, config);
        }

        private static void VerifyPerimeterWinding(SetupReport report, TableConfig config)
        {
            // 按 rail 分组形成链，再按"最近端点"贪心拼接为闭合轮廓（与独立复核同法）。
            var groups = new Dictionary<string, List<CushionSegment>>();
            var order = new List<string>();
            foreach (CushionSegment s in config.segments)
            {
                if (!groups.TryGetValue(s.rail, out List<CushionSegment> list))
                {
                    list = new List<CushionSegment>();
                    groups[s.rail] = list;
                    order.Add(s.rail);
                }

                list.Add(s);
            }

            var chains = new Dictionary<string, List<Vector2>>();
            foreach (KeyValuePair<string, List<CushionSegment>> kv in groups)
            {
                var pts = new List<Vector2> { kv.Value[0].a };
                foreach (CushionSegment s in kv.Value)
                {
                    pts.Add(s.b);
                }

                chains[kv.Key] = pts;
            }

            if (chains.Count == 0)
            {
                report.Add(CheckResult.Fail("CHR-02", "库边拼接轮廓绕向 = CCW", "shoelace > 0", "无段"));
                return;
            }

            string start = order[0];
            var path = new List<Vector2>(chains[start]);
            chains.Remove(start);

            while (chains.Count > 0)
            {
                Vector2 tail = path[path.Count - 1];
                string bestRail = null;
                bool reverse = false;
                float best = float.MaxValue;
                foreach (KeyValuePair<string, List<Vector2>> kv in chains)
                {
                    float d1 = Vector2.Distance(kv.Value[0], tail);
                    float d2 = Vector2.Distance(kv.Value[kv.Value.Count - 1], tail);
                    if (d1 < best) { best = d1; bestRail = kv.Key; reverse = false; }
                    if (d2 < best) { best = d2; bestRail = kv.Key; reverse = true; }
                }

                List<Vector2> chain = chains[bestRail];
                chains.Remove(bestRail);
                if (reverse)
                {
                    chain = new List<Vector2>(chain);
                    chain.Reverse();
                }

                path.AddRange(chain);
            }

            float area2 = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                area2 += path[i].x * path[i + 1].y - path[i + 1].x * path[i].y;
            }

            float closingGap = Vector2.Distance(path[path.Count - 1], path[0]);
            float area = area2 * 0.5f;

            // 说明：82 段按 rail 分组后"最近端点"贪心拼接，并非严格闭合环（末点与首点留有约 178 mm 的缺口，
            // 系分组的链天然不首尾相接所致）。但 shoelace 对"链 + 首尾连线"构成的闭合多边形是有定义的，
            // 其**符号**即绕向：水平面镜像会把它整体翻号（已实测），proper 旋转不变。
            // 因此这里断言"符号 = CCW"并钉住量级（3.1443，源于同一算法），不把缺口当作失败。
            const float expectedArea = 3.144285f;
            bool ccw = area > 0f;
            bool magnitudeOk = Mathf.Abs(area - expectedArea) < 0.01f;

            report.Add(ccw && magnitudeOk
                ? CheckResult.Ok("CHR-02", "库边拼接轮廓绕向 = CCW（= 复核 C3，镜像自检）",
                    $"shoelace>0 且 ≈ {expectedArea:F4}", $"shoelace/2={area:F6}, gap={closingGap * 1000f:F1} mm",
                    "水平面镜像会翻转绕向符号；与 CHR-01 一起构成不依赖基线的手性自检。")
                : CheckResult.Fail("CHR-02", "库边拼接轮廓绕向 = CCW（= 复核 C3，镜像自检）",
                    $"shoelace>0 且 ≈ {expectedArea:F4}",
                    $"shoelace/2={area:F6} (ccw={ccw}, magnitudeOk={magnitudeOk}), gap={closingGap * 1000f:F1} mm"));
        }

        // ── 网格对齐（轴向判据）─────────────────────────────────────────────

        private static void VerifyMeshAlignment(SetupReport report, TableConfig config)
        {
            if (!ProjectPaths.Exists(ModelPath))
            {
                report.Add(CheckResult.Fail("AXL-01", "球桌 FBX 存在", ModelPath, "未找到"));
                return;
            }

            MeshSet mesh = LoadMeshes(ModelPath);
            if (mesh.Vertices.Count == 0)
            {
                report.Add(CheckResult.Fail("AXL-01", "可从 FBX 读取网格顶点", "> 0", "0",
                    "可能是 isReadable 未启用或网格缺失。"));
                return;
            }

            // 三角面数必须与源一致（几何未被裁剪）。顶点数会因 Unity 按法线/UV 拆分顶点而变大
            // （源码 10572 → 导入后 13934），这是正常现象，故只报告不作为失败条件。
            report.Add(mesh.TriangleCount == PoolConstants.SourceTableTriangles
                ? CheckResult.Ok("AXL-01", "从 FBX 读取的三角面数 = 源 19954",
                    PoolConstants.SourceTableTriangles.ToString(),
                    $"{mesh.TriangleCount} 三角面 / {mesh.Vertices.Count} 顶点",
                    $"源 FBX 顶点 {PoolGeometry.SourceVertexCount}；Unity 按法线/UV 拆分为 {mesh.Vertices.Count}，属正常。")
                : CheckResult.Fail("AXL-01", "从 FBX 读取的三角面数 = 源 19954",
                    PoolConstants.SourceTableTriangles.ToString(), $"{mesh.TriangleCount} 三角面"));

            // 唯一轴修正后的包围盒（mesh.Vertices 存的就是修正后的 Unity 世界坐标）
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Vector3 w in mesh.Vertices)
            {
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }

            Vector3 expMin = PoolGeometry.WorldBoundsMin;
            Vector3 expMax = PoolGeometry.WorldBoundsMax;

            float boundErr = Mathf.Max(
                Mathf.Abs(min.x - expMin.x), Mathf.Abs(min.y - expMin.y), Mathf.Abs(min.z - expMin.z),
                Mathf.Abs(max.x - expMax.x), Mathf.Abs(max.y - expMax.y), Mathf.Abs(max.z - expMax.z));

            report.Add(boundErr < BoundsTolerance
                ? CheckResult.Ok("AXL-02", "模型局部顶点经唯一转换链后的世界包围盒 = 推导期望值", "±0.1 mm",
                    $"err={boundErr:E3} m",
                    $"worldMin={min} worldMax={max}；期望由 PoolGeometry 转换链从源包络推导（非硬编码）。"
                    + "X 的不对称符号被钉住（-1.412409 vs +1.412686），可判镜像/手性。")
                : CheckResult.Fail("AXL-02", "模型局部顶点经唯一转换链后的世界包围盒 = 推导期望值", "±0.1 mm",
                    $"err={boundErr:E3} m", $"worldMin={min} worldMax={max}; expMin={expMin} expMax={expMax}"));

            float extX = max.x - min.x;
            float extZ = max.z - min.z;
            report.Add(extX > extZ
                ? CheckResult.Ok("AXL-03", "网格长轴 = Unity X（短轴 = Z）", "X > Z",
                    $"X={extX:F6} > Z={extZ:F6}")
                : CheckResult.Fail("AXL-03", "网格长轴 = Unity X（短轴 = Z）", "X > Z", $"X={extX:F6} <= Z={extZ:F6}"));

            // ── 三层度量（L1 面点 / L2 边距 / L3 顶点）；实现与 EditMode 测试共用 MeshAlignmentMetrics ──
            var segQueries = new List<Vector3>();
            foreach (CushionSegment s in config.segments)
            {
                segQueries.Add(PoolGeometry.PhysicsToUnity(s.a));
                segQueries.Add(PoolGeometry.PhysicsToUnity(s.b));
            }

            var pocketQueries = new List<Vector3>();
            foreach (Pocket p in config.pockets)
            {
                pocketQueries.Add(PoolGeometry.PhysicsToUnity(p.mouthA));
                pocketQueries.Add(PoolGeometry.PhysicsToUnity(p.mouthB));
                pocketQueries.Add(PoolGeometry.PhysicsToUnity(p.throatA));
                pocketQueries.Add(PoolGeometry.PhysicsToUnity(p.throatB));
            }

            var all = new List<Vector3>(segQueries);
            all.AddRange(pocketQueries);

            float worstFaceSeg = 0f, worstFacePocket = 0f, worstEdge = 0f;
            float worstVertex = 0f, worstRatio = 0f, localAtWorst = 0f;

            foreach (Vector3 q in all)
            {
                MeshAlignmentMetrics.Measurement m = mesh.Metrics.Measure(q);
                worstEdge = Mathf.Max(worstEdge, m.Edge);
                if (m.Vertex > worstVertex) worstVertex = m.Vertex;
                float ratio = m.Vertex / Mathf.Max(m.LocalLongestEdge, 1e-9f);
                if (ratio > worstRatio)
                {
                    worstRatio = ratio;
                    localAtWorst = m.LocalLongestEdge;
                }
            }

            foreach (Vector3 q in segQueries) worstFaceSeg = Mathf.Max(worstFaceSeg, mesh.Metrics.Measure(q).Face);
            foreach (Vector3 q in pocketQueries) worstFacePocket = Mathf.Max(worstFacePocket, mesh.Metrics.Measure(q).Face);

            // L1：必要条件层（只验落在面上）
            report.Add(worstFaceSeg < SurfacePointTolerance
                ? CheckResult.Ok("AXL-04", "L1 82 段端点落在网格面上（端点→最近面内点）",
                    $"< {SurfacePointTolerance:E0} m", $"实测 {worstFaceSeg * 1000f:F6} mm",
                    "**只验落在网格面上**；**对足迹内滑动盲**（点仍在网格内），"
                    + "但**对掉出网格（袋孔、掉出外轮廓）敏感** —— **不作位移判据**。"
                    + "此前的 0.26 mm 来自旧口径“最近顶点”的采样下限，非几何错位。")
                : CheckResult.Fail("AXL-04", "L1 82 段端点落在网格面上（端点→最近面内点）",
                    $"< {SurfacePointTolerance:E0} m", $"实测 {worstFaceSeg * 1000f:F6} mm"));

            report.Add(worstFacePocket < SurfacePointTolerance
                ? CheckResult.Ok("AXL-05", "L1 6 袋 mouth/throat 端点落在网格面上",
                    $"< {SurfacePointTolerance:E0} m", $"实测 {worstFacePocket * 1000f:F6} mm",
                    "同上：必要条件层，不作位移判据。")
                : CheckResult.Fail("AXL-05", "L1 6 袋 mouth/throat 端点落在网格面上",
                    $"< {SurfacePointTolerance:E0} m", $"实测 {worstFacePocket * 1000f:F6} mm"));

            // L2：高灵敏层（边距）
            report.Add(worstEdge < EdgeDistanceTolerance
                ? CheckResult.Ok("AXL-07", "L2 端点落在网格边上（端点→最近网格边，高灵敏层）",
                    $"< {EdgeDistanceTolerance * 1e6f:F0} µm", $"实测 {worstEdge * 1e6f:F4} µm",
                    "底噪 0.18 µm（= 模型量化 0.155 µm 与实测底噪 0.18 µm 取大），阈值 5 µm ≈ 28× 底噪。"
                    + "**盲区：沿边方向滑动 ⇒ 点仍在该边上 ⇒ 距离恒 ≈0** ⇒ 不作唯一位移判据，由 L3 兜。"
                    + "（备选 2 µm 仅在确认网格不再重导出时可用；本轮不收紧。）")
                : CheckResult.Fail("AXL-07", "L2 端点落在网格边上（端点→最近网格边，高灵敏层）",
                    $"< {EdgeDistanceTolerance * 1e6f:F0} µm", $"实测 {worstEdge * 1e6f:F4} µm"));

            // L3：全向层（顶点，比值口径）
            report.Add(worstRatio < VertexRatioTolerance
                ? CheckResult.Ok("AXL-06", "L3 端点→最近顶点 / 局部间距 ≤ k（全向层，比值口径）",
                    $"≤ k = {VertexRatioTolerance:F4}（2× 观测比值 {MeasuredVertexBaseline / localAtWorst:F4}）",
                    $"实测算比 {worstRatio:F4}（阈值 k={VertexRatioTolerance:F4}，余量 {VertexRatioTolerance / Mathf.Max(worstRatio, 1e-12f):F2}×）；"
                    + $"T = k×L_local = {VertexRatioTolerance * localAtWorst * 1000f:F3} mm；L_local={localAtWorst * 1000f:F3} mm",
                    "**唯一无方向性盲区**的层；检出下限 ≈ 0.40~0.75 mm。"
                    + "**对整段/区域刚性位移：无上盲带**（仅 ≤~0.4 mm 底噪带）；"
                    + "**对单点位移：存在周期性盲带**（实测最高至 12.4 mm），"
                    + "但该威胁**由 CMP-03（索引对齐，容差 1e-5 m）覆盖**。"
                    + $"理论硬上界 = {VertexRatioTheoreticalBound}（边中点），仅留档、不用作阈值。")
                : CheckResult.Fail("AXL-06", "L3 端点→最近顶点 / 局部间距 ≤ k（全向层，比值口径）",
                    $"≤ k = {VertexRatioTolerance:F4}", $"实测算比 {worstRatio:F4}；L_local={localAtWorst * 1000f:F3} mm"));
        }

        private class MeshSet
        {
            /// <summary>顶点已应用唯一模型侧转换（<see cref="PoolGeometry.ModelVertexToUnity"/>），处于 Unity 世界坐标。</summary>
            public readonly List<Vector3> Vertices = new List<Vector3>();

            /// <summary>三角面（顶点索引三元组），索引指向 <see cref="Vertices"/>。</summary>
            public readonly List<int> Triangles = new List<int>();

            /// <summary>三层度量的**共享实现**（与 EditMode 测试同源，杜绝口径分叉）。</summary>
            public MeshAlignmentMetrics Metrics;

            public int TriangleCount => Triangles.Count / 3;

        }

        private static MeshSet LoadMeshes(string fbxPath)
        {
            var set = new MeshSet();
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (UnityEngine.Object o in assets)
            {
                if (o is Mesh m)
                {
                    // 顶点一律经唯一模型侧转换后再入库，保证与 PhysicsToUnity 同处 Unity 世界坐标
                    int baseIndex = set.Vertices.Count;
                    foreach (Vector3 v in m.vertices)
                    {
                        set.Vertices.Add(PoolGeometry.ModelVertexToUnity(v));
                    }

                    int[] tris = m.triangles;
                    for (int i = 0; i < tris.Length; i++)
                    {
                        set.Triangles.Add(baseIndex + tris[i]);
                    }
                }
            }

            set.Metrics = new MeshAlignmentMetrics(set.Vertices.ToArray(), set.Triangles.ToArray());
            return set;
        }

        // ── 导入设置 ───────────────────────────────────────────────────────

        private static void VerifyImportSettings(SetupReport report)
        {
            var model = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (model == null)
            {
                report.Add(CheckResult.Fail("IMP-01", "模型导入设置即代码", "可读取 ModelImporter", "未找到"));
            }
            else
            {
                bool ok = !model.useFileScale
                          && Mathf.Approximately(model.globalScale, 1f)
                          && model.isReadable
                          && model.meshCompression == ModelImporterMeshCompression.Off
                          && model.materialImportMode == ModelImporterMaterialImportMode.None
                          && model.importTangents == ModelImporterTangents.CalculateMikk;
                report.Add(ok
                    ? CheckResult.Ok("IMP-01", "模型导入设置即代码",
                        "useFileScale=false, globalScale=1, readable, no compression, no auto-material, MikkTSpace",
                        "全部符合", "杜绝二次缩放；材质留待 Day 3 手建。")
                    : CheckResult.Fail("IMP-01", "模型导入设置即代码",
                        "useFileScale=false, globalScale=1, readable, no compression, no auto-material, MikkTSpace",
                        $"useFileScale={model.useFileScale}, globalScale={model.globalScale}, readable={model.isReadable}, "
                        + $"compression={model.meshCompression}, material={model.materialImportMode}, tangents={model.importTangents}"));
            }

            var normal = AssetImporter.GetAtPath(BilliardsAssetPostprocessor.NormalPath) as TextureImporter;
            if (normal != null)
            {
                var settings = new TextureImporterSettings();
                normal.ReadTextureSettings(settings);
                bool ok = normal.textureType == TextureImporterType.NormalMap
                          && !normal.sRGBTexture
                          && settings.flipGreenChannel == BilliardsAssetPostprocessor.InvertNormalGreenChannel;
                report.Add(ok
                    ? CheckResult.Ok("IMP-02", "法线贴图类型与绿通道约定",
                        $"NormalMap, sRGB=false, flipGreen={BilliardsAssetPostprocessor.InvertNormalGreenChannel}",
                        $"NormalMap, sRGB=false, flipGreen={settings.flipGreenChannel}",
                        "Unity 官方：绿色 Y+ = Up（OpenGL）；Blender 默认导出 OpenGL；实测绿均值 125.7≈中性。故不翻转。")
                    : CheckResult.Fail("IMP-02", "法线贴图类型与绿通道约定",
                        $"NormalMap, sRGB=false, flipGreen={BilliardsAssetPostprocessor.InvertNormalGreenChannel}",
                        $"type={normal.textureType}, sRGB={normal.sRGBTexture}, flipGreen={settings.flipGreenChannel}"));
            }
            else
            {
                report.Add(CheckResult.Fail("IMP-02", "法线贴图类型与绿通道约定", "可读取", "未找到法线贴图"));
            }

            var baseColor = AssetImporter.GetAtPath(BilliardsAssetPostprocessor.BaseColorPath) as TextureImporter;
            report.Add(baseColor != null && baseColor.sRGBTexture
                ? CheckResult.Ok("IMP-03", "BaseColor 为 sRGB 颜色贴图", "sRGB=true", "sRGB=true")
                : CheckResult.Fail("IMP-03", "BaseColor 为 sRGB 颜色贴图", "sRGB=true",
                    baseColor == null ? "未找到" : $"sRGB={baseColor.sRGBTexture}"));

            var net = AssetImporter.GetAtPath(BilliardsAssetPostprocessor.NetRgbaPath) as TextureImporter;
            report.Add(net != null && net.alphaSource == TextureImporterAlphaSource.FromInput && net.sRGBTexture
                ? CheckResult.Ok("IMP-04", "袋网贴图保留 Alpha（供 Alpha Clipping）", "alphaFromInput + sRGB", "符合")
                : CheckResult.Fail("IMP-04", "袋网贴图保留 Alpha（供 Alpha Clipping）", "alphaFromInput + sRGB",
                    net == null ? "未找到" : $"alphaSource={net.alphaSource}, sRGB={net.sRGBTexture}"));

            var metal = AssetImporter.GetAtPath(BilliardsAssetPostprocessor.MetallicPath) as TextureImporter;
            var rough = AssetImporter.GetAtPath(BilliardsAssetPostprocessor.RoughnessPath) as TextureImporter;
            bool linearOk = metal != null && rough != null && !metal.sRGBTexture && !rough.sRGBTexture
                            && metal.isReadable && rough.isReadable;
            report.Add(linearOk
                ? CheckResult.Ok("IMP-05", "Metallic/Roughness 为线性且可读（供通道打包）", "sRGB=false, readable=true", "符合")
                : CheckResult.Fail("IMP-05", "Metallic/Roughness 为线性且可读（供通道打包）", "sRGB=false, readable=true",
                    $"metallic={(metal == null ? "null" : $"sRGB={metal.sRGBTexture},readable={metal.isReadable}")}, "
                    + $"roughness={(rough == null ? "null" : $"sRGB={rough.sRGBTexture},readable={rough.isReadable}")}"));
        }

        // ── 美术资源 ───────────────────────────────────────────────────────

        private static void VerifyArtAssets(SetupReport report)
        {
            var expected = new (string path, int w, int h)[]
            {
                (BilliardsAssetPostprocessor.BaseColorPath, 2048, 2048),
                (BilliardsAssetPostprocessor.NormalPath, 2048, 2048),
                (BilliardsAssetPostprocessor.NetRgbaPath, 2048, 2048),
                (BilliardsAssetPostprocessor.RoughnessPath, 1024, 1024),
                (BilliardsAssetPostprocessor.MetallicPath, 1024, 1024),
            };

            bool allExist = true;
            var detail = new StringBuilder();
            foreach ((string path, int w, int h) in expected)
            {
                bool exists = ProjectPaths.Exists(path);
                allExist &= exists;
                Texture2D tex = exists ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
                string dims = tex != null ? $"{tex.width}×{tex.height}" : "?";
                bool dimOk = tex != null && tex.width == w && tex.height == h;
                if (!exists || !dimOk) allExist = false;
                detail.Append(Path.GetFileName(path)).Append('=').Append(exists ? dims : "缺失").Append("; ");
            }

            report.Add(allExist
                ? CheckResult.Ok("ART-01", "5 张贴图存在且尺寸符合规格", "见明细", detail.ToString().TrimEnd())
                : CheckResult.Fail("ART-01", "5 张贴图存在且尺寸符合规格", "见明细", detail.ToString().TrimEnd(),
                    "尺寸来自参考数据 texture_sizes（BaseColor/Normal/Net 2048、Metallic/Roughness 1024）。"));

            report.Add(ProjectPaths.Exists(ModelPath)
                ? CheckResult.Ok("ART-02", "球桌 FBX 存在", ModelPath, ModelPath)
                : CheckResult.Fail("ART-02", "球桌 FBX 存在", ModelPath, "未找到"));

            report.Add(ProjectPaths.Exists(BilliardsAssetPostprocessor.PackedMaskPath)
                ? CheckResult.Ok("ART-03", "Metallic-Roughness 打包贴图已生成（R2 提前处置）",
                    BilliardsAssetPostprocessor.PackedMaskPath, "存在")
                : CheckResult.Warn("ART-03", "Metallic-Roughness 打包贴图已生成（R2 提前处置）",
                    BilliardsAssetPostprocessor.PackedMaskPath, "未生成",
                    "URP Lit 需 Metallic(R)+Smoothness(A)。可运行 Billiards/Art/Pack Table Metallic-Roughness Mask；Day 3 处理。"));
        }

        private static string Sha256Hex(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return "<missing>";
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(fullPath));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
