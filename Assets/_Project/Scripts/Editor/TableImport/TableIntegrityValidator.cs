using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Billiards.Core;
using Billiards.Reference;
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
    /// 三类判据（对应任务书第 2 条硬约束"轴向转换只允许做一次、且要可判据"）：
    ///  1) 包围盒判据：导入网格经唯一轴修正后的包围盒 = 源数据 bounds_runtime，
    ///     且 X 的不对称符号被钉住（用于判定没有发生镜像/手性翻转）。
    ///  2) 最近顶点判据：82 段端点 + 6 袋的 mouth/throat 端点，到网格顶点的最近距离 < 1 mm。
    ///     （离线实测：段端点 ≤0.26 mm，袋口 mouth = 0，throat ≤0.22 mm。）
    ///     这条同时证明"轴向映射正确"+"无二次缩放"+"物理轮廓与渲染网格对齐"。
    ///  3) 交叉比对判据：A/B 两份源文件逐点一致。
    /// </summary>
    public static class TableIntegrityValidator
    {
        public const string ModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        public const string ReferenceTablePath = "Docs/Reference/table-metadata-reference.json";
        public const string ReferenceGameplayPath = "Docs/Reference/gameplay-markers-reference.json";
        public const string ReportFileName = "day2-verification.json";

        // ── 容差策略（采纳独立复核 D2-T02A 的实测结论）─────────────────────────
        //
        // 复核实测：**顺序无关**（集合/最近邻）比对下，全量镜像/180°旋转与真值只差
        //   镜像 x→−x = 4.74e-4 m、镜像 y = 1.04e-4 m、180°绕竖轴 = 4.73e-4 m。
        // 故 1 mm 容差会被这些错误"骗过去"。
        //
        // 因此本校验器的分层策略是：
        //  ① 主判据（决定成败）= **索引对齐 + 逐字段**比对，容差 1e-5 m（CMP-03）；
        //     镜像/180°在此口径下误差为 0.27~2.6 m，一抓即中。
        //  ② 手性/绕向自检（不依赖基线）= CHR-01“= C4 袋口局部手性符号”、CHR-02“= C3 库边链绕向”。
        //  ③ AXL-02 包围盒用**网格外轮廓**的 x 向不对称（0.277 mm）钉手性，容差 1e-4 m。
        //  ④ AXL-04/05（最近顶点，容差 ≤ 0.5 mm）属**“物理数据 ↔ 渲染网格”对应关系**这一类，
        //     与上述“数据集之间顺序无关比对”不是同一类：网格被单侧镜像时其误差达米级（≈2.6 m）。
        //     其 0.2613 mm **不是误差**，而是“82 段直线段逼近曲率袋角”的**轮廓近似残差**；
        //     阈值由该测量性质推导为 0.5 mm（约 1.9× 余量），并**必须输出实测值**以看见渐变漂移。
        //     **不作为镜像判据**，仅作网格对齐的补充证据。

        /// <summary>① 主判据容差：索引对齐逐字段比对，1e-5 m（独立复核建议值）。</summary>
        public const float IndexAlignedTolerance = 1e-5f;

        /// <summary>③ 网格外轮廓包围盒容差：0.1 mm。缩放/旋转错误会远超此值。</summary>
        public const float BoundsTolerance = 1e-4f;

        /// <summary>
        /// ④ 物理数据 ↔ 渲染网格 的“最近顶点”对应容差 = **≤ 0.5 mm**（由轮廓近似残差 0.2613 mm 推导，约 1.9× 余量）。
        /// 该阈值只用于拦“大错”；**渐变漂移由输出的实测值观察**（阈值只能拦大错，记录实测值才能看见渐变）。
        /// 属数据↔网格对应类，**非镜像判据**。
        /// </summary>
        public const float VertexAlignmentTolerance = 0.0005f;

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Verify/Validate Table Data & Art (Day 2)", false, 201)]
        public static void ValidateInteractive()
        {
            TableDataImporter.Import();
            SetupReport report = RunAll();
            string path = report.WriteToDisk(ReportFileName);
            Debug.Log(report.ToConsoleSummary());
            EditorUtility.DisplayDialog(
                "Billiards Day-2 Validate",
                $"校验完成。\n\nPASS {report.passCount}  WARN {report.warnCount}  FAIL {report.failCount}\n结论：{report.overallStatus}",
                "OK");
            Debug.Log("验收报告已写入：" + path);
        }

        public static void ValidateFromCommandLine()
        {
            int exitCode;
            SetupReport report = null;

            try
            {
                TableDataImporter.Import();   // 幂等；确保 TableConfig 为最新
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
                string path = report.WriteToDisk(ReportFileName);   // 先落盘
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

            Vector3 expMin = PoolGeometry.OuterBoundsMin;
            Vector3 expMax = PoolGeometry.OuterBoundsMax;

            float boundErr = Mathf.Max(
                Mathf.Abs(min.x - expMin.x), Mathf.Abs(min.y - expMin.y), Mathf.Abs(min.z - expMin.z),
                Mathf.Abs(max.x - expMax.x), Mathf.Abs(max.y - expMax.y), Mathf.Abs(max.z - expMax.z));

            report.Add(boundErr < BoundsTolerance
                ? CheckResult.Ok("AXL-02", "轴修正后网格包围盒 = 源 bounds_runtime", "±0.1 mm",
                    $"err={boundErr:E3} m",
                    $"worldMin={min} worldMax={max}；X 不对称符号被钉住（-1.412686 vs +1.412409），可判手性未翻转。")
                : CheckResult.Fail("AXL-02", "轴修正后网格包围盒 = 源 bounds_runtime", "±0.1 mm",
                    $"err={boundErr:E3} m", $"worldMin={min} worldMax={max}; expMin={expMin} expMax={expMax}"));

            float extX = max.x - min.x;
            float extZ = max.z - min.z;
            report.Add(extX > extZ
                ? CheckResult.Ok("AXL-03", "网格长轴 = Unity X（短轴 = Z）", "X > Z",
                    $"X={extX:F6} > Z={extZ:F6}")
                : CheckResult.Fail("AXL-03", "网格长轴 = Unity X（短轴 = Z）", "X > Z", $"X={extX:F6} <= Z={extZ:F6}"));

            // 最近顶点判据：段端点（在水平面 XZ 比较——物理是二维的，竖直高度不参与）
            float worstSeg = 0f;
            foreach (CushionSegment s in config.segments)
            {
                worstSeg = Mathf.Max(worstSeg, mesh.NearestHorizontalDistance(PoolGeometry.PhysicsToUnity(s.a)));
                worstSeg = Mathf.Max(worstSeg, mesh.NearestHorizontalDistance(PoolGeometry.PhysicsToUnity(s.b)));
            }

            report.Add(worstSeg < VertexAlignmentTolerance
                ? CheckResult.Ok("AXL-04", "82 段端点与网格顶点对齐（水平面）",
                    $"实测值（阈值 {VertexAlignmentTolerance * 1000f:F2} mm）", $"实测 {worstSeg * 1000f:F4} mm",
                    $"余量 {VertexAlignmentTolerance / Mathf.Max(worstSeg, 1e-9f):F2}×；"
                    + "该实测值为“82 段直线段逼近曲率袋角”的轮廓近似残差（非误差）。"
                    + "本检查只验证**水平面端点对齐**；它**不能**单独证明手性/未镜像 —— "
                    + "最优轴排列内 8 个符号变体的误差跨度仅约 0.27 µm，镜像变体甚至略优。"
                    + "手性由 AXL-02（包围盒不对称：X 端 -1.412686/+1.412409、Y 端腿底 -0.8635/库顶 +0.0610）"
                    + "与“模型修正必须是纯旋转 det=+1”共同钉死。")
                : CheckResult.Fail("AXL-04", "82 段端点与网格顶点对齐（水平面）",
                    $"实测值（阈值 {VertexAlignmentTolerance * 1000f:F2} mm）", $"实测 {worstSeg * 1000f:F4} mm"));

            // 最近顶点判据：袋口 mouth/throat（袋心是形心，非顶点，故不参与）
            float worstPocket = 0f;
            foreach (Pocket p in config.pockets)
            {
                worstPocket = Mathf.Max(worstPocket, mesh.NearestHorizontalDistance(PoolGeometry.PhysicsToUnity(p.mouthA)));
                worstPocket = Mathf.Max(worstPocket, mesh.NearestHorizontalDistance(PoolGeometry.PhysicsToUnity(p.mouthB)));
                worstPocket = Mathf.Max(worstPocket, mesh.NearestHorizontalDistance(PoolGeometry.PhysicsToUnity(p.throatA)));
                worstPocket = Mathf.Max(worstPocket, mesh.NearestHorizontalDistance(PoolGeometry.PhysicsToUnity(p.throatB)));
            }

            report.Add(worstPocket < VertexAlignmentTolerance
                ? CheckResult.Ok("AXL-05", "6 袋 mouth/throat 端点与网格顶点对齐（水平面）",
                    $"实测值（阈值 {VertexAlignmentTolerance * 1000f:F2} mm）", $"实测 {worstPocket * 1000f:F4} mm",
                    $"余量 {VertexAlignmentTolerance / Mathf.Max(worstPocket, 1e-9f):F2}×；"
                    + "离线实测 mouth = 0、throat ≤ 0.22 mm；本条是\"袋口落在网格袋口上\"的可判据版本。")
                : CheckResult.Fail("AXL-05", "6 袋 mouth/throat 端点与网格顶点对齐（水平面）",
                    $"实测值（阈值 {VertexAlignmentTolerance * 1000f:F2} mm）", $"实测 {worstPocket * 1000f:F4} mm"));
        }

        private class MeshSet
        {
            /// <summary>顶点已应用唯一轴修正，处于 Unity 世界坐标（可直接与 PhysicsToUnity 的结果比较）。</summary>
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public int TriangleCount;

            /// <summary>查询点到最近网格顶点在**水平面 XZ** 上的距离。
            /// 物理是二维的，竖直高度不参与对齐判据（库鼻高度 44.98 mm 与台呢高度本就是不同平面）。</summary>
            public float NearestHorizontalDistance(Vector3 query)
            {
                float best = float.MaxValue;
                for (int i = 0; i < Vertices.Count; i++)
                {
                    float dx = Vertices[i].x - query.x;
                    float dz = Vertices[i].z - query.z;
                    float d = dx * dx + dz * dz;
                    if (d < best) best = d;
                }

                return Mathf.Sqrt(best);
            }
        }

        private static MeshSet LoadMeshes(string fbxPath)
        {
            var set = new MeshSet();
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (UnityEngine.Object o in assets)
            {
                if (o is Mesh m)
                {
                    // 顶点一律经唯一轴修正后再入库，保证后续包围盒/最近顶点比较口径一致。
                    foreach (Vector3 v in m.vertices)
                    {
                        set.Vertices.Add(PoolGeometry.ModelVertexToUnity(v));
                    }

                    set.TriangleCount += m.triangles.Length / 3;
                }
            }

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
