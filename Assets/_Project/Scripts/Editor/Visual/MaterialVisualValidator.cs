using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.Diagnostics;
using Billiards.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 材质视觉验收器：材质 / 场景 / Prefab / 灯光 / 相机 的可判据检查 + 渲染证据（俯视图、法线 A/B 对比图）。
    ///
    /// 与工程配置/数据完整性验收器同源同风格：只读为主、全部结论带 expected/actual、报告先落盘再退出。
    /// 渲染在批处理下用 RenderTexture + EncodeToPNG 落盘到 `Docs/Reports/`。
    /// </summary>
    public static class MaterialVisualValidator
    {
        public const string ReportFileName = "day3-verification.json";
        public const string AbDir = "Docs/Reports/day3-normal-ab";

        /// <summary>`SCN-07` 的"地面与桌腿底齐平"容差（米）：浮点噪声量级 ⇒ 1e-3 足够严、且不会假红。</summary>
        public const float GroundFlushTolerance = 1e-3f;

        /// <summary>
        /// `CAM-03` 的**外框裁切 WARN 阈值**（视口比例）。**8%** = 实测固有裁切 **3.9%** 的约 **2 倍余量**。
        /// 语义 = "**裁切不得继续变大**"（不是"必须不裁"）；超阈值只 **WARN**（外框可见性不在取景契约内）。
        /// </summary>
        public const float OuterCropWarnThreshold = 0.08f;

        /// <summary>
        /// `CAM-04` 的打球区**横向占比下界**。
        ///
        /// ── 口径重定（登记"谁裁定 / 为什么 / 前后值 / 旧意图"）──────────────────────────
        /// **谁裁定**：用户（2026-10-16，经主理人转达）。
        /// **为什么**：旧下限 **0.85** 是在"相机把打球区充满屏幕"的口径下定的（旧实测 0.900）。
        ///   用户裁定"**要能看见球房环境**"⇒ 相机移到桌子 +Z 侧并放宽取景（见
        ///   `GreyboxSceneSetup.CameraPosition` 的口径变更注释）。**"看得见房间"与"占比 ≥0.85"数学上互斥**：
        ///   任何能把房间收进画面的取景都会把该占比压到 0.45~0.62。
        /// **前后值**：**0.85 → 0.50**。实测 A 档 **0.616** ⇒ **余量 0.116**（不贴实测值定）。
        /// **旧意图**：`CAM-04` 标题即"**防'缩成中间一张小桌'**"。该意图**不变** ——
        ///   新下限 0.50 仍能抓住"缩成小桌"（`ClothHalfLength` 全宽 1.27×2×1.2 = 3.048 m 在画面里
        ///   占比掉到 0.50 以下，意味着视锥水平半宽 > 3.05 m，那已不是"看球桌"的取景）。
        ///   ⚠ 本阈值**只随取景口径变更**而变；**不得**为了让某次取景过而单独下调。
        /// </summary>
        public const float ClothWidthFractionMin = 0.50f;

        /// <summary>`CAM-05` 取景一致性容差：相机位置（米）。</summary>
        public const float FramingPoseTolerance = 1e-3f;

        /// <summary>`CAM-05` 取景一致性容差：**序列化**垂直 FOV（度）。</summary>
        public const float FramingFovTolerance = 1e-3f;

        /// <summary>`CAM-05` 取景一致性容差：打球区投影包围盒（视口分量）。</summary>
        public const float FramingBoxTolerance = 2e-3f;

        /// <summary>`CAM-05` 取景一致性容差：相机旋转（度）。</summary>
        public const float FramingRotationToleranceDeg = 0.01f;

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Verify/Validate Visuals (materials/scene/camera)", false, 401)]
        public static void ValidateInteractive()
        {
            // ⚠ R2：**不重建被校验对象** —— 校验“仓库里既有的 Main.unity”，而不是自己刚造的。
            SetupReport report = RunAll();
            string path = ResolveReportPath("Docs/Reports/" + ReportFileName, ReportFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log(report.ToConsoleSummary());
            Debug.Log("验收报告已写入：" + path);
        }

        /// <summary>只构建 + 渲染（用于迭代 A/B 取景），不写报告。</summary>
        public static void RenderOnlyFromCommandLine()
        {
            _outputDirOverride = CommandLineArgs.Get("-outputDir");
            int exit = 0;
            try
            {
                // 本入口**故意**保留 Apply()：它是“构建+渲染”的迭代工具，**不是校验入口**。
                MaterialVisualSetup.Apply();
                RenderAll();
            }
            catch (Exception ex)
            {
                Debug.LogError("渲染失败：" + ex);
                exit = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exit);
            }
        }

        public static void ValidateFromCommandLine()
        {
            _outputDirOverride = CommandLineArgs.Get("-outputDir");
            int exitCode;
            SetupReport report = null;
            try
            {
                // ⚠ R2：**不调用 MaterialVisualSetup.Apply()**。校验器一旦重建被校验对象，
                // 就只能证明“生成器自洽”，不能证明“被提交的场景是对的”。
                // 需要重建场景时，请单独运行 MaterialVisualSetup 的构建入口（Visual 菜单）。
                report = RunAll();
                exitCode = report.failCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("材质视觉校验过程中抛出异常：" + ex);
                exitCode = 2;
            }

            if (report != null)
            {
                report.schema = "billiards.day3-verification.v1";
                string path = ResolveReportPath("Docs/Reports/" + ReportFileName, ReportFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                Debug.Log(report.ToConsoleSummary());
                Debug.Log("验收报告已写入：" + path);
                Debug.Log("DAY3_RESULT=" + report.overallStatus
                          + " PASS=" + report.passCount + " WARN=" + report.warnCount + " FAIL=" + report.failCount);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);   // 再退出
            }
        }

        // ── 全量检查 ───────────────────────────────────────────────────────

        public static SetupReport RunAll()
        {
            var report = new SetupReport { schema = "billiards.day3-verification.v1" };

            VerifyMaterials(report);
            VerifyScene(report);
            VerifyPrefabs(report);
            VerifyPropGeometry(report);
            VerifyCameraAndLighting(report);
            RenderAndVerify(report);

            report.Finalise();
            return report;
        }

        // ── 材质 ───────────────────────────────────────────────────────────

        private static void VerifyMaterials(SetupReport report)
        {
            Material body = LoadMat(MaterialVisualSetup.BodyMaterialPath);
            bool bodyOk = body != null
                          && body.shader != null && body.shader.name == MaterialVisualSetup.UrpLitShaderName
                          && body.GetTexture("_BaseMap") != null
                          && body.GetTexture("_BumpMap") != null
                          && body.GetTexture("_MetallicGlossMap") != null
                          && body.IsKeywordEnabled("_NORMALMAP")
                          && body.IsKeywordEnabled("_METALLICSPECGLOSSMAP")
                          && !body.IsKeywordEnabled("_ALPHATEST_ON")
                          && Mathf.Approximately(body.GetFloat("_SmoothnessTextureChannel"), 0f)
                          && Mathf.Approximately(body.GetFloat("_Surface"), 0f);

            report.Add(bodyOk
                ? CheckResult.Ok("MAT-01", "桌体材质：URP Lit / Opaque / 三图齐备 / Smoothness=MetallicAlpha",
                    "URP Lit, _BaseMap+_BumpMap+_MetallicGlossMap, 非透明",
                    $"shader={body.shader.name}, queue={body.renderQueue}, smoothnessChannel={body.GetFloat("_SmoothnessTextureChannel")}")
                : CheckResult.Fail("MAT-01", "桌体材质：URP Lit / Opaque / 三图齐备 / Smoothness=MetallicAlpha",
                    "URP Lit, 三图齐备, 非透明",
                    body == null ? "<缺失>" :
                    $"shader={(body.shader == null ? "<null>" : body.shader.name)}, "
                    + $"base={body.GetTexture("_BaseMap") != null}, bump={body.GetTexture("_BumpMap") != null}, "
                    + $"mask={body.GetTexture("_MetallicGlossMap") != null}, "
                    + $"kwNormal={body.IsKeywordEnabled("_NORMALMAP")}, kwMetallic={body.IsKeywordEnabled("_METALLICSPECGLOSSMAP")}, "
                    + $"kwAlphaTest={body.IsKeywordEnabled("_ALPHATEST_ON")}, "
                    + $"smoothChannel={body.GetFloat("_SmoothnessTextureChannel")}, surface={body.GetFloat("_Surface")}"));

            Material nets = LoadMat(MaterialVisualSetup.NetsMaterialPath);
            bool netsOk = nets != null
                          && nets.shader != null && nets.shader.name == MaterialVisualSetup.UrpLitShaderName
                          && Mathf.Approximately(nets.GetFloat("_AlphaClip"), 1f)
                          && Mathf.Approximately(nets.GetFloat("_Cutoff"), BilliardsAssetPostprocessor.NetAlphaCutoff)
                          && Mathf.Approximately(nets.GetFloat("_Cull"), 0f)
                          && nets.IsKeywordEnabled("_ALPHATEST_ON")
                          && nets.GetTexture("_BaseMap") != null;

            report.Add(netsOk
                ? CheckResult.Ok("MAT-02", "袋网材质：URP Lit / Alpha Clipping / cutoff 0.5 / 双面",
                    "alphaClip=1, cutoff=0.5, cull=0", DescribeMat(nets))
                : CheckResult.Fail("MAT-02", "袋网材质：URP Lit / Alpha Clipping / cutoff 0.5 / 双面",
                    "alphaClip=1, cutoff=0.5, cull=0", DescribeMat(nets)));

            int ballsOk = 0;
            for (int i = 0; i < MaterialVisualSetup.BallCount; i++)
            {
                Material bm = LoadMat($"{MaterialVisualSetup.BallsMaterialDir}/{MaterialVisualSetup.BallName(i)}.mat");
                if (bm != null && bm.shader != null && bm.shader.name == MaterialVisualSetup.UrpLitShaderName
                    && bm.GetTexture("_BaseMap") != null && !bm.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    ballsOk++;
                }
            }

            report.Add(ballsOk == MaterialVisualSetup.BallCount
                ? CheckResult.Ok("MAT-03", $"16 个球材质齐备且指向各自 BaseColor", "16/16", ballsOk + "/16")
                : CheckResult.Fail("MAT-03", $"16 个球材质齐备且指向各自 BaseColor", "16/16", ballsOk + "/16"));

            Material cue = LoadMat(MaterialVisualSetup.CueMaterialPath);
            report.Add(cue != null && cue.GetTexture("_BaseMap") != null
                ? CheckResult.Ok("MAT-04", "球杆材质存在且含 BaseColor", "存在", "存在")
                : CheckResult.Fail("MAT-04", "球杆材质存在且含 BaseColor", "存在", DescribeMat(cue)));

            Material floor = LoadMat(MaterialVisualSetup.FloorMaterialPath);
            report.Add(floor != null
                ? CheckResult.Ok("MAT-05", "地面材质存在（最小布景）", "存在", "存在")
                : CheckResult.Fail("MAT-05", "地面材质存在（最小布景）", "存在", "缺失"));
        }

        private static Material LoadMat(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static string DescribeMat(Material m)
        {
            if (m == null)
            {
                return "<缺失>";
            }

            return $"shader={(m.shader == null ? "<null>" : m.shader.name)}, alphaClip={m.GetFloat("_AlphaClip")}, "
                 + $"cutoff={m.GetFloat("_Cutoff")}, cull={m.GetFloat("_Cull")}, queue={m.renderQueue}";
        }

        // ── 场景 ───────────────────────────────────────────────────────────

        private static void VerifyScene(SetupReport report)
        {
            if (!ProjectPaths.Exists(MaterialVisualSetup.MainScenePath))
            {
                report.Add(CheckResult.Fail("SCN-01", "Main.unity 存在", MaterialVisualSetup.MainScenePath, "未找到"));
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MaterialVisualSetup.MainScenePath)
            {
                scene = EditorSceneManager.OpenScene(MaterialVisualSetup.MainScenePath, OpenSceneMode.Single);
            }

            GameObject playfield = FindRoot(scene, "PlayfieldRoot");
            GameObject table = playfield != null ? FindChild(playfield.transform, "PoolTable") : FindRoot(scene, "PoolTable");
            GameObject floor = FindChild(scene, "Floor");
            GameObject camera = FindRoot(scene, "Main Camera");

            bool requiredPresent = playfield != null && table != null && floor != null && camera != null;
            report.Add(requiredPresent
                ? CheckResult.Ok("SCN-01", "场景层级齐备（PlayfieldRoot / PoolTable / Floor / Main Camera）",
                    "四者齐备", "四者齐备")
                : CheckResult.Fail("SCN-01", "场景层级齐备", "四者齐备",
                    $"playfield={playfield != null}, table={table != null}, floor={floor != null}, camera={camera != null}"));

            if (playfield == null || table == null)
            {
                report.Add(CheckResult.Fail("SCN-02", "PlayfieldRoot 缩放 = 契约常量", PoolConstants.PlayfieldVisualScale.ToString("R"), "无法检查"));
                return;
            }

            // ── 口径重定位（登记"为什么改"，**不是放宽**）────────────────────────────
            // 旧期望 = `1`。它的**语义是"1.2 未被误用"**，前提是"`Main.unity` 是材质**检视台**"。
            // `Main.unity` 现在是**正式游戏入口**、其玩法口径**就是** 1.2（与灰盒一致）⇒ 1.2 是**有意**的。
            // ⇒ 本条改为钉"缩放**恰为那一个契约常量**"：仍是**单值等式**、仍可证伪
            //   （注入 1.0 / 1.44 都红，见规格 §注入）。
            // ⚠ "物理保持规范米制"的担保**不在本条** —— 在 `SCN-04`（它查桌子的世界几何）。
            float expectedScale = PoolConstants.PlayfieldVisualScale;
            Vector3 s = playfield.transform.localScale;
            bool scaleOk = Mathf.Abs(s.x - expectedScale) <= 1e-6f
                           && Mathf.Abs(s.y - expectedScale) <= 1e-6f
                           && Mathf.Abs(s.z - expectedScale) <= 1e-6f;
            report.Add(scaleOk
                ? CheckResult.Ok("SCN-02", "PlayfieldRoot 缩放 = PoolConstants.PlayfieldVisualScale（视觉缩放有意应用）",
                    expectedScale.ToString("R"), s.ToString())
                : CheckResult.Fail("SCN-02", "PlayfieldRoot 缩放 = PoolConstants.PlayfieldVisualScale（视觉缩放有意应用）",
                    expectedScale.ToString("R"), s.ToString()));

            report.Add(table.transform.IsChildOf(playfield.transform)
                ? CheckResult.Ok("SCN-03", "球桌挂在 PlayfieldRoot 之下（视觉根且零旋转）", "是", "是",
                    "球桌不得再叠加手动旋转：唯一模型侧修正在 PoolGeometry。")
                : CheckResult.Fail("SCN-03", "球桌挂在 PlayfieldRoot 之下", "是", "否"));

            // SCN-04：实例化球桌的**世界**几何 vs 转换链期望 —— ADR-0001 §9 修正后的核心不变量。
            // 转换链（PoolGeometry）已把模型自带朝向 ModelOrientation(Rx≈270°) 纳入，
            // 故 `PhysicsToUnity` 与实例化网格的**规范**位置**恰好重合**（不再是"在某对称性下近似重合"）；
            // `Main.unity` 的 `PlayfieldRoot` 再乘**唯一**视觉缩放常量（见下），故世界几何 = 规范 × 常数。
            // SCN-04：实例化球桌的**世界**几何 vs 转换链期望 —— ADR-0001 §9 修正后的核心不变量。
            //
            // ── 期望值口径（**必须独立，不得由被测 Transform 导出**）──────────────────
            // 期望 = **独立常量积** `PoolGeometry.WorldBounds* × PoolConstants.PlayfieldVisualScale`
            // （根为 pos=0 / rot=id ⇒ 世界 = 缩放 × 规范）。
            // ⚠ **不得**写成 `PlayfieldView.DisplayFromCanonical(root, WorldBounds*)`：那由被测对象**所在的
            //   同一个 Transform** 导出 ⇒ 根缩放取 1.0 / 1.2 / 1.44 **全部自洽、err 恒为 0** ⇒ 恒真
            //   （本工程已登记的错误模式 #8）。这里期望**不读场景**，故对根缩放/平移/旋转都可证伪。
            float visualScale = PoolConstants.PlayfieldVisualScale;
            Vector3 expMin = PoolGeometry.WorldBoundsMin * visualScale;
            Vector3 expMax = PoolGeometry.WorldBoundsMax * visualScale;

            Bounds wb = ComputeWorldBounds(table);
            float worldErr = Mathf.Max(
                Mathf.Abs(wb.min.x - expMin.x),
                Mathf.Abs(wb.min.y - expMin.y),
                Mathf.Abs(wb.min.z - expMin.z),
                Mathf.Abs(wb.max.x - expMax.x),
                Mathf.Abs(wb.max.y - expMax.y),
                Mathf.Abs(wb.max.z - expMax.z));

            table.transform.GetPositionAndRotation(out Vector3 wp, out Quaternion wr);
            string wt = $"worldMin=({wb.min.x:F6},{wb.min.y:F6},{wb.min.z:F6}) "
                      + $"worldMax=({wb.max.x:F6},{wb.max.y:F6},{wb.max.z:F6}); "
                      + $"expectedMin={expMin} expectedMax={expMax}（= 规范 ×{visualScale:R}）; "
                      + $"rootRot={wr.eulerAngles}, rootPos={wp}, lossyScale={table.transform.lossyScale}";

            report.Add(worldErr < TableIntegrityValidator.BoundsTolerance
                ? CheckResult.Ok("SCN-04", "实例化球桌世界几何 = 规范几何 × 视觉缩放（期望为独立常量积）",
                    $"±{TableIntegrityValidator.BoundsTolerance:E0} m", $"err={worldErr:E3} m",
                    wt + "；本条可被**证伪** —— ① 根缩放写 1.0 / 1.44 ⇒ err = 0.2× / 0.24× 桌面尺寸（实测 2.825E-001 m @ 1.0）；"
                    + "② 翻转 PoolGeometry.ModelOrientation 会使 err 跳到**角袋 x 向不对称**量级 —— "
                    + "该量**有定义、可离线复算**：`|OuterBoundsMax.x| − |OuterBoundsMin.x|` = "
                    + "`|1.412408709526062| − |1.412685751914978|` = **2.7704E-004 m**（规范尺度，"
                    + "= `ModelOrientationTests.CornerAsymmetry = 2.77e-4f` 所钉的同一个量）⇒ 本条按 **×1.2** 比对 ⇒ "
                    + "**3.32E-004 m**。⚠ **它与 Day3 归档里的 `AXL-04 = 0.000337 mm` 不是同一个量**"
                    + "（后者是**端点→最近面内点的 L1 残差**、单位是 mm、口径完全不同，且该记录随后已修至 0.000000 mm）——"
                    + "**两者只是数字巧合**，勿互相比对。"
                    + "③ 根平移 / 旋转 ⇒ 世界 AABB 直接偏。"
                    + "根节点自带 Rx≈270° 属模型姿态（ModelOrientation 常量），场景未叠加任何手动旋转（守 ADR §7.2）。")
                : CheckResult.Fail("SCN-04", "实例化球桌世界几何 = 规范几何 × 视觉缩放（期望为独立常量积）",
                    $"±{TableIntegrityValidator.BoundsTolerance:E0} m", $"err={worldErr:E3} m", wt));

            // 子网格材质分配
            MeshRenderer[] renderers = table.GetComponentsInChildren<MeshRenderer>(true);
            int subMeshes = 0;
            bool materialOk = renderers.Length > 0;
            foreach (MeshRenderer mr in renderers)
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                int sub = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.subMeshCount : 0;
                subMeshes += sub;
                for (int i = 0; i < sub; i++)
                {
                    Material assigned = i < mr.sharedMaterials.Length ? mr.sharedMaterials[i] : null;
                    Material want = i == 1 ? LoadMat(MaterialVisualSetup.NetsMaterialPath) : LoadMat(MaterialVisualSetup.BodyMaterialPath);
                    if (assigned != want)
                    {
                        materialOk = false;
                    }
                }
            }

            report.Add(subMeshes >= 2 && materialOk
                ? CheckResult.Ok("SCN-05", "球桌子网格材质：0=桌体(不透明) 1=袋网(AlphaClipping)",
                    "子网格=2 且材质正确", $"子网格={subMeshes}，材质正确")
                : CheckResult.Fail("SCN-05", "球桌子网格材质分配", "子网格=2 且材质正确",
                    $"子网格={subMeshes}，materialOk={materialOk}"));

            // 自研物理铁律守卫 —— **单点实现**（`PhysicsBanAudit`），与 SCN-04 共用（铁律 16）。
            // ① 扫描范围由「仅当前场景」扩到「Scenes + Prefabs 全量」：物理与玩法层正是从这 18 个 prefab 实例化球，
            //    一个 Rigidbody 挂在球 prefab 上过去**不会被任何检查发现**。
            // ② 改用**固定路径**（`ScanAll()` 内部用文件系统枚举），**不再用 `SceneManager.GetActiveScene()`** ——
            //    旧写法一旦当前活动场景不是预期场景就会扫错对象，过去的安全只是调用顺序的巧合。
            // ③ 同时断言 `CoverageIsNonVacuous`：证明"0"不是因为**一个文件都没扫到**（防恒真）。
            PhysicsBanAudit.Scan banScan = PhysicsBanAudit.ScanAll();
            bool banOk = banScan.RigidbodyCount == 0 && banScan.ColliderCount == 0 && banScan.CoverageIsNonVacuous;
            string banActual =
                $"scene={banScan.SceneCount} prefab={banScan.PrefabCount} → Rigidbody={banScan.RigidbodyCount} Collider={banScan.ColliderCount}";

            report.Add(banOk
                ? CheckResult.Ok("SCN-06", "自研物理守卫：Scenes + Prefabs 无 Rigidbody/Collider",
                    "0 / 0，且扫描非空转", banActual,
                    "扫描范围 = Scenes + Prefabs 全量（含 18 个 prefab）；非空转证据 = 扫到的文件数 > 0。"
                    + "单点实现：与 SCN-04 共用 `PhysicsBanAudit`。")
                : CheckResult.Fail("SCN-06", "自研物理守卫：Scenes + Prefabs 无 Rigidbody/Collider",
                    "0 / 0，且扫描非空转",
                    banActual + (banScan.Offenders.Count > 0 ? "；违规：" + string.Join(" | ", banScan.Offenders) : "（扫描空转！）")));

            // SCN-07：**地面与桌腿底齐平**（"桌不悬空"的视觉布景不变量）—— **本轮新增**。
            //
            // 为什么必须新增：`Main.unity` 先前把 Room 挂在**场景根**（`AttachRoom(playfield.transform.parent)`
            // 的实参是 `null`），而地面用 `localPosition = (0, TableLegBottomY, 0)` ⇒ 地面世界 y =
            // **父链 y 缩放** × TableLegBottomY。根为 1 时看不出；根改成 1.2 后地面 1×、桌腿底 1.2×
            // ⇒ 桌子**浮起 0.1727 m**。**当时没有任何判据守这条**（SCN-02 只钉根缩放值、SCN-04 只钉桌子的
            // 世界几何）⇒ 必须补，否则下次还会踩。
            //
            // 期望值**独立**：两边都取**实测对象**（地面 Transform 的世界 y、桌子网格的世界 AABB min.y），
            // 不读根缩放、不读配置常量 ⇒ **不是恒真**。
            //
            // 判别边界（如实登记）：本条职责 = **Room 的挂点**（是否与桌**同挂缩放根**）。
            //   · 把 Room 摘出缩放根（注入 C）⇒ 地面 −0.8635 vs 桌底 −1.0362 ⇒ **红（Δ=0.1727 m）** ✓；
            //   · 根缩放值写错（注入 A/B）⇒ 两边**同步**变 ⇒ 本条**绿**，那是 `SCN-02`/`SCN-04` 的职责。
            //   ⇒ **不得**把本条当作"根缩放"的守卫。
            // 盲带：若将来换桌模型使"最低点不再是桌腿"，"桌腿底"语义需按新模型重钉。
            // 覆盖：两场景都跑（灰盒本来就是对的 ⇒ 本条在灰盒上恰好绿，可自证非空转）。
            if (floor != null && table != null)
            {
                float floorWorldY = floor.transform.position.y;
                float tableBottomWorldY = ComputeWorldBounds(table).min.y;
                float gap = Mathf.Abs(floorWorldY - tableBottomWorldY);
                string detail = $"floor.worldY={floorWorldY:F6} table.min.y={tableBottomWorldY:F6} |Δ|={gap:F6} m";

                report.Add(gap < GroundFlushTolerance
                    ? CheckResult.Ok("SCN-07", "地面与桌腿底齐平（Room 与桌同挂缩放根）",
                        $"|Δ| < {GroundFlushTolerance:E0} m", detail)
                    : CheckResult.Fail("SCN-07", "地面与桌腿底齐平（Room 与桌同挂缩放根）",
                        $"|Δ| < {GroundFlushTolerance:E0} m",
                        detail + "；Room 摘出缩放根会让地面不随桌缩放 ⇒ 桌子浮起（×1.2 时 0.1727 m）。"));
            }
        }

        // ── Prefab ─────────────────────────────────────────────────────────

        private static void VerifyPrefabs(SetupReport report)
        {
            int found = 0;
            for (int i = 0; i < MaterialVisualSetup.BallCount; i++)
            {
                string p = $"{MaterialVisualSetup.BallsPrefabDir}/{MaterialVisualSetup.BallName(i)}.prefab";
                if (ProjectPaths.Exists(p) && AssetDatabase.LoadAssetAtPath<GameObject>(p) != null)
                {
                    found++;
                }
            }

            report.Add(found == MaterialVisualSetup.BallCount
                ? CheckResult.Ok("PRB-01", "16 个球 Prefab 齐备", "16/16", found + "/16")
                : CheckResult.Fail("PRB-01", "16 个球 Prefab 齐备", "16/16", found + "/16"));

            bool baseOk = ProjectPaths.Exists(MaterialVisualSetup.BaseBallPrefabPath)
                          && AssetDatabase.LoadAssetAtPath<GameObject>(MaterialVisualSetup.BaseBallPrefabPath) != null;
            bool cueOk = ProjectPaths.Exists(MaterialVisualSetup.CuePrefabPath)
                         && AssetDatabase.LoadAssetAtPath<GameObject>(MaterialVisualSetup.CuePrefabPath) != null;
            report.Add(baseOk && cueOk
                ? CheckResult.Ok("PRB-02", "BallPrefab 基座 + 球杆 Prefab 存在", "均存在", "均存在")
                : CheckResult.Fail("PRB-02", "BallPrefab 基座 + 球杆 Prefab 存在", "均存在",
                    $"base={baseOk}, cue={cueOk}"));

            GameObject ball0 = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{MaterialVisualSetup.BallsPrefabDir}/{MaterialVisualSetup.BallName(0)}.prefab");
            Material want0 = LoadMat($"{MaterialVisualSetup.BallsMaterialDir}/{MaterialVisualSetup.BallName(0)}.mat");
            bool ballMatOk = false;
            bool layerOk = false;
            if (ball0 != null)
            {
                layerOk = ball0.layer == BilliardsLayers.Ball;
                foreach (MeshRenderer mr in ball0.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.sharedMaterial == want0)
                    {
                        ballMatOk = true;
                    }
                }
            }

            report.Add(ballMatOk && layerOk
                ? CheckResult.Ok("PRB-03", "球 Prefab 材质与层正确", "Ball_00 材质=Ball_00.mat, layer=Ball",
                    "符合")
                : CheckResult.Fail("PRB-03", "球 Prefab 材质与层正确", "Ball_00 材质=Ball_00.mat, layer=Ball",
                    $"materialOk={ballMatOk}, layerOk={layerOk}"));
        }

        // ── 道具几何（实测）─────────────────────────────────────────────────

        private static void VerifyPropGeometry(SetupReport report)
        {
            // 球半径 == PoolConstants.BallRadius
            Mesh ballMesh = FirstMesh($"{MaterialVisualSetup.BallsModelDir}/{MaterialVisualSetup.BallName(0)}.fbx");
            float ballRadius = 0f;
            if (ballMesh != null)
            {
                Bounds b = ballMesh.bounds;
                ballRadius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            }

            report.Add(ballMesh != null && Mathf.Abs(ballRadius - PoolConstants.BallRadius) < 1e-5f
                ? CheckResult.Ok("GEO-01", "球网格半径 = PoolConstants.BallRadius", PoolConstants.BallRadius.ToString("R"),
                    ballRadius.ToString("R"), "实测导入后网格半径，单位米。")
                : CheckResult.Fail("GEO-01", "球网格半径 = PoolConstants.BallRadius", PoolConstants.BallRadius.ToString("R"),
                    ballRadius.ToString("R")));

            Mesh cueMesh = FirstMesh(MaterialVisualSetup.CueModelPath);
            string cueAxis = "<none>";
            float cueLen = 0f;
            if (cueMesh != null)
            {
                Bounds b = cueMesh.bounds;
                if (b.size.x >= b.size.y && b.size.x >= b.size.z) cueAxis = "X";
                else if (b.size.y >= b.size.z) cueAxis = "Y";
                else cueAxis = "Z";
                cueLen = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            }

            report.Add(cueMesh != null && Mathf.Abs(cueLen - PoolConstants.CueLength) < 1e-3f
                ? CheckResult.Ok("GEO-02", "球杆网格长 = PoolConstants.CueLength（轴向已实测记录）",
                    PoolConstants.CueLength.ToString("R"), $"{cueLen:F4} m（长轴 = {cueAxis}）",
                    $"Unity 导入后网格本地长轴 = {cueAxis}；文档声称杆身 +Z，**实测为准**，球杆动画（交互层）需据此定向。")
                : CheckResult.Fail("GEO-02", "球杆网格长 = PoolConstants.CueLength", PoolConstants.CueLength.ToString("R"),
                    $"{cueLen:F4} m（长轴 = {cueAxis}）"));

            int fbxCount = 0;
            for (int i = 0; i < MaterialVisualSetup.BallCount; i++)
            {
                if (ProjectPaths.Exists($"{MaterialVisualSetup.BallsModelDir}/{MaterialVisualSetup.BallName(i)}.fbx"))
                {
                    fbxCount++;
                }
            }

            report.Add(fbxCount == MaterialVisualSetup.BallCount
                ? CheckResult.Ok("GEO-03", "16 个球 FBX 已导入", "16/16", fbxCount + "/16")
                : CheckResult.Fail("GEO-03", "16 个球 FBX 已导入", "16/16", fbxCount + "/16"));
        }

        private static Mesh FirstMesh(string fbxPath)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (o is Mesh m)
                {
                    return m;
                }
            }

            return null;
        }

        // ── 相机与灯光 ─────────────────────────────────────────────────────

        private static void VerifyCameraAndLighting(SetupReport report)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject camGo = FindRoot(scene, "Main Camera");
            Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;

            if (cam == null)
            {
                report.Add(CheckResult.Fail("CAM-01", "主相机存在且参数符合设定", "存在", "缺失"));
                return;
            }

            // ── 两个 fov 不是同一个量（写在这里，防后人误判"相机被人改错了"）──────────────
            // 本项（`CAM-01`）断言的是**序列化** fov（**52.000** = 谁手写在相机上的值）；
            // 而 `CAM-02`/`CAM-03`/`CAM-04`/`CAM-05` 用的是 `CameraViewportFitter` 作用后的**有效** fov
            // （**56.909 @ aspect 1.6** —— 窄窗时 fitter 抬高垂直 fov 以保住**水平**覆盖）。
            // **不矛盾，是两个量**：本项守**输入**（改没改常量）、投影类守**输出**（16:10 窗口里实际看到的取景）；
            // 有效值由 fitter **纯函数**从序列化值推出，而 fitter 规则本身另由 `T52`/`T53` 守。
            // ⇒ 看到 `52` 与 `56.909` 并存时**不要去"对齐"它们**。
            //
            // ── 口径重定位（登记"为什么改"）────────────────────────────────────────
            // 期望值由 `MaterialVisualSetup` 的旧常量（pos (0,3.05,−0.88) / fov 37）改为**共用玩法常量**
            // （`GreyboxSceneSetup.CameraPosition` / `CameraBaseVerticalFovDeg = 52`）。
            // 理由：`Main.unity` 从"材质检视台"变为**正式游戏入口** ⇒ 取景必须与玩法口径一致；
            // 且相机参数**只能有一处真源**（铁律 16）—— `MaterialVisualSetup` 那份已删除。
            Vector3 expectedCamPos = GreyboxSceneSetup.CameraPosition;
            float expectedFov = GreyboxSceneSetup.CameraBaseVerticalFovDeg;
            float posErr = Vector3.Distance(cam.transform.position, expectedCamPos);
            bool paramsOk = posErr < 1e-3f
                            && Mathf.Abs(cam.fieldOfView - expectedFov) < 0.01f
                            && cam.nearClipPlane > 0f;

            report.Add(paramsOk
                ? CheckResult.Ok("CAM-01", "主相机参数 = 玩法取景（唯一真源 = GreyboxSceneSetup）",
                    $"pos={expectedCamPos}, fov={expectedFov}",
                    $"pos={cam.transform.position}, fov={cam.fieldOfView}", "俯视带俯角；俯视截图即由该相机渲染。")
                : CheckResult.Fail("CAM-01", "主相机参数 = 玩法取景（唯一真源 = GreyboxSceneSetup）",
                    $"pos={expectedCamPos}, fov={expectedFov}",
                    $"pos={cam.transform.position}, fov={cam.fieldOfView}"));

            // 球桌是否完整落在视锥内（用世界包围盒投影到视口）
            GameObject table = FindChild(scene, "PoolTable");
            if (table != null)
            {
                // ── 口径修正（登记"为什么改"）────────────────────────────────────
                // 旧实现 `cam.aspect = 1440/900` 后**拿序列化 FOV 硬投**，对象是桌子的**外框** AABB。
                // 本轮实测（fov 52 + 根缩放 1.2）暴露出两件事：
                //  ① 不施加运行时 `CameraViewportFitter`（窄窗抬垂直 FOV 保水平覆盖）⇒ 假红；
                //  ② 对象的**取错**：取景由 `CameraViewportFitter` 按**内库四角**推导（见其字段注释），
                //     **外框从来不在契约内**；旧写法在 fov37+scale1 下"恰好过"，掩盖了这一点。
                // ⇒ CAM-02 改钉**可玩区（内库）× 视觉缩放**（真契约，仍可证伪：fov/取景写小 ⇒ 红）；
                //    **外框**的可见性另立 `CAM-03` 并**如实记为 WARN**（已知观感取舍，见其 note）。
                const float checkAspect = 1440f / 900f;   // 既有口径（比 16:9 更窄 ⇒ 更严）
                float prevAspect = cam.aspect;
                float prevFov = cam.fieldOfView;
                var fitter = cam.GetComponent<CameraViewportFitter>();
                cam.aspect = checkAspect;
                cam.fieldOfView = fitter != null
                    ? CameraViewportFitter.ResolveVerticalFovDeg(checkAspect, fitter.baseAspect,
                        fitter.baseVerticalFovDeg, fitter.maxVerticalFovDeg)
                    : cam.fieldOfView;
                float usedFov = cam.fieldOfView;
                string fitterTag = fitter != null ? "有" : "无";

                Bounds outer = ComputeWorldBounds(table);
                float vs = PoolConstants.PlayfieldVisualScale;
                var cloth = new Bounds(Vector3.zero, new Vector3(
                    PoolConstants.ClothHalfLength * vs * 2f, 0.0001f, PoolConstants.ClothHalfWidth * vs * 2f));

                Vector4 cBox = ProjectViewportBox(cloth, cam, out bool cBehind);
                Vector4 oBox = ProjectViewportBox(outer, cam, out bool oBehind);

                cam.aspect = prevAspect;
                cam.fieldOfView = prevFov;

                string aspectTag = $"aspect {checkAspect:F3} 下 fov={usedFov:F2}，fitter={fitterTag}";
                bool clothInFrame = !cBehind && cBox.x >= -0.02f && cBox.y <= 1.02f && cBox.z >= -0.02f && cBox.w <= 1.02f;
                // 外框覆盖**仅记录、不判定**：用户已裁定「打球区充满屏幕、整桌**外轮廓**有意裁掉一点」
                // 是本取景的**设计意图** ⇒ 外框溢出**不是缺陷**，故不做 WARN/FAIL（避免"喊狼来了"）。
                // 数字仍每次写进 `actual`（铁律 4：把实测值写进输出，而不是只报 PASS/FAIL）。
                float outerOverflowX = Mathf.Max(0f, -oBox.x, oBox.y - 1f);
                float outerOverflowY = Mathf.Max(0f, -oBox.z, oBox.w - 1f);
                string clothActual = $"cloth x[{cBox.x:F3},{cBox.y:F3}] y[{cBox.z:F3},{cBox.w:F3}]"
                                     + (cBehind ? " behind=true" : string.Empty)
                                     + $"；外框(仅记录) x[{oBox.x:F3},{oBox.y:F3}] y[{oBox.z:F3},{oBox.w:F3}]"
                                     + $" 超量 x={outerOverflowX:F3} y={outerOverflowY:F3}（{aspectTag}）";
                report.Add(clothInFrame
                    ? CheckResult.Ok("CAM-02", "打球区（内库）× 视觉缩放完整落在相机视锥内", "视口 [0,1]", clothActual,
                        "**对象口径（用户拍板）**：只守**打球区**（`ClothInnerLength/Width` × 视觉缩放）。"
                        + "灰盒的 fov52 本就按内库四角（±1.27, ±0.635）×1.2 定（`CameraViewportFitter` 字段注释）"
                        + "⇒「打球区充满屏幕、整桌**外轮廓**有意裁掉一点」是**设计意图**。"
                        + "⚠ **判别边界**：本项**不再守**「整桌外轮廓在框内」；若将来要把整桌收进画面，那是**设计变更**，"
                        + "需同时改本判据 —— **不得**靠放宽容差 `±0.02` 蒙混。")
                    : CheckResult.Fail("CAM-02", "打球区（内库）× 视觉缩放完整落在相机视锥内", "视口 [0,1]", clothActual));

                // ── CAM-03：**外框裁切有界**（"裁切不得继续变大"）──────────────────────────────
                // 用户裁定：外框被裁 **3.9%** 是**取景的固有取舍**（灰盒同样裁 —— 它本就按**内库四角**推导）
                // ⇒ **不是缺陷**。本项**不判"是否被裁"**，只判"**裁切量不得失控**"：
                //   · 阈值 **8%** = 实测 3.9% 的约 **2 倍余量**（留出正常抖动 / 取景微调的空间）；
                //   · ≤ 8% ⇒ PASS（**日常不挂黄** —— 长期恒 WARN 会被当背景噪声，真漂移时反而没人看）；
                //   · > 8% ⇒ **WARN**（**有界可见性**，不是 FAIL：外框可见性本就**不**在取景契约内）。
                // 判别力自证（实测）：注入 B（根缩放 1.44）超量 **0.178**、注入 D（fov 40）超量 **0.222**
                //   ⇒ 两者 **> 0.08 ⇒ WARN**；稳态（配置 B）**0.039 ⇒ PASS**。
                //
                // ── **它为什么不是冗余**（"模型侧守卫"，**带限定**）──────────────────────
                // 针对"**换桌模型使木库外框变厚变宽、而位姿与打球区不变**"这种漂移，其余判据**都不会触发**：
                //   `CAM-01`（只比相机 pos / **序列化** fov）、`CAM-02`/`CAM-04`（对象是**常量基盒**
                //   `ClothHalfLength/Width × lossyScale`，**不读桌子网格**）、`CAM-05`（模型变会**同等**影响
                //   两场景 ⇒ Δ 恒 0）、`SCN-02`（只比根缩放常量）⇒ **本项抓得住**。
                // ⚠ **准确写法（限定，勿写成更强的说法）**：「**在标准管线（模型与包络常量同步更新）下**，
                //   本项是这类漂移的**唯一**守卫」。**不得**写成"任何换模型的唯一防线" —— 若有人换了模型
                //   却**没更新 `PoolGeometry` 的包络常量**，则 **`SCN-04` 会先红**（那是它该做的），
                //   此时本项**不是**唯一；也不得因此以为 `SCN-04` 冗余（它是那种情形下的兜底）。
                float outerCrop = Mathf.Max(outerOverflowX, outerOverflowY);
                string outerCropActual = $"外框超量 {outerCrop:P1}（x={outerOverflowX:F3} y={outerOverflowY:F3}）";
                report.Add(outerCrop <= OuterCropWarnThreshold
                    ? CheckResult.Ok("CAM-03", "外框裁切有界（裁切本身是取景固有取舍；此条只拦失控）",
                        $"≤ {OuterCropWarnThreshold:P0}", outerCropActual)
                    : CheckResult.Warn("CAM-03", "外框裁切有界（裁切本身是取景固有取舍；此条只拦失控）",
                        $"≤ {OuterCropWarnThreshold:P0}", outerCropActual,
                        "⇒ 裁切量明显变大（取景被改小 / 后撤）。**外框可见性不在取景契约内**"
                        + "（`CAM-02` 只守打球区）⇒ 仅 WARN 不 FAIL；"
                        + $"阈值 {OuterCropWarnThreshold:P0} = 实测 3.9% 的约 2 倍余量。"));

                // ── CAM-04：**取景占比**（守"桌子不至于缩成画面中间一小块"，防"缩成一张小桌"）────────────
                // 参照值来源 = **灰盒**（用户认可的那套观感）。实测（aspect 1.6）：
                //   · 灰盒 / 配置 B：打球区**横向占比 0.900**（0.950−0.050）、纵向 0.604；
                //   · 配置 A（**保留 Main 高机位 (0,3.05,−0.88) + fov52 + aspect 1.6**）：横向占比 **0.593**
                //     —— **实测值，来源 = 注入 E**（`Docs/Tools/batchA_injections.py inject-configA`，见规格 §8.4/§9.4）。
                // ⚠ 这里**曾经**写过一个 "≈0.77" —— 那是**转述的粗估、不是任何一次实测**（后经复算，该配置各种解释最高只到 0.659）。
                //   **已废弃**，改为上表的实测 0.593。⇒ 立规：**注释/规格/证据里引用实测值，必须可复现并注明来源**
                //   （规格 §9.6）。
                // ⇒ 阈值见**上方 `ClothWidthFractionMin` 的口径重定推导（0.85 → 0.50）**。
                //   ⚠ 本处**故意不再重复数值**：旧版把这句写成"取 0.85 / 参照 0.900"，口径重定后就成了
                //   **注释与实现自相矛盾**的陈旧行（复核方抓到的那条）⇒ 数值只留常量那一处，避免两处各写一份而漂移。
                float clothWidthFraction = cBox.y - cBox.x;
                float clothHeightFraction = cBox.w - cBox.z;
                const string clothWidthExpect = "≥ 0.50（口径重定后的新下限，实测 A 档 0.616 ⇒ 余量 0.116）";
                report.Add(clothWidthFraction >= ClothWidthFractionMin
                    ? CheckResult.Ok("CAM-04", "打球区横向占比 ≥ 参照（防'缩成中间一张小桌'）",
                        clothWidthExpect,
                        $"宽占比 {clothWidthFraction:F3}（高占比 {clothHeightFraction:F3}）")
                    : CheckResult.Fail("CAM-04", "打球区横向占比 ≥ 参照（防'缩成中间一张小桌'）",
                        clothWidthExpect,
                        $"宽占比 {clothWidthFraction:F3}（高占比 {clothHeightFraction:F3}）"
                        + " ⇒ 桌子在屏幕上的占比明显偏小（取景被改小 / 相机后撤）。"));

                // ── CAM-05：**Main 与灰盒 取景逐项一致**（把用户的原话变成可证伪的不变量）──────────
                // 用户要求就是「Main 的观感 = 我在灰盒里调的观感」⇒ 逐项比对：相机位置 / 旋转 /
                // **序列化** fov /（同宽高比、各取本方根缩放下）打球区投影包围盒。
                // 判别力：**只改其中一边**的相机 / fov / 根缩放 ⇒ 必红（不依赖 `CAM-01` 的常量表）。
                CheckFramingConsistency(report, scene, checkAspect);
            }

            Light sun = RenderSettings.sun;
            bool lightOk = sun != null && sun.type == LightType.Directional
                           && Mathf.Abs(sun.intensity - MaterialVisualSetup.SunIntensity) < 1e-3f
                           && RenderSettings.ambientMode != AmbientMode.Skybox;
            report.Add(lightOk
                ? CheckResult.Ok("LGT-01", "主光 + 环境光已配置", $"dir light, intensity={MaterialVisualSetup.SunIntensity}",
                    $"intensity={sun.intensity}, ambient={RenderSettings.ambientMode}, color={sun.color}")
                : CheckResult.Fail("LGT-01", "主光 + 环境光已配置", $"dir light, intensity={MaterialVisualSetup.SunIntensity}",
                    sun == null ? "<无 RenderSettings.sun>" : $"intensity={sun.intensity}, ambient={RenderSettings.ambientMode}"));
        }

        // ── 渲染 ───────────────────────────────────────────────────────────

        /// <summary>
        /// **渲染证据判定**（`RND-01` 非退化 + `NAB-01` 可失败）。
        ///
        /// ── 本工程实测事故（2026-10-16，本方法存在的理由）────────────────────────────
        /// `-batchmode -nographics` **没有 GPU** ⇒ `cam.Render()` / `ReadPixels` 拿不到内容 ⇒ 产出**纯灰空图**：
        /// day3 的四张证据**逐字节完全相同、`distinctColors = 1`**。而当时
        ///  · `RND-01` **只查"4 张落盘"**（存在即通过）；
        ///  · `NAB-01` **硬编码 `CheckResult.Ok`**（永不失败）
        /// ⇒ **两条判据在空证据上照样报绿** —— "**绿而空**"（红灯有人查，空证据上的绿灯没人查）。
        ///
        /// ── 现在（口径**与离线复核工具 `Docs/Tools/qa_render_evidence_check.py` 同判**）──
        ///  · `RND-01`：4 张**存在** + **文件 ≥ 100 B**（挡 Git-LFS 指针/空文件）+ **可解码** + **`distinctColors > 1`**（非单一底色）；
        ///  · `NAB-01`：`ab-asis` 与 `ab-flipgreen` **逐像素必须有差异**（`diff > 0`）——
        ///    绿通道翻转若真的生效，受力面明暗侧应交换 ⇒ 必有像素不同；**两版相同 ⇒ 证据无信息 ⇒ FAIL**。
        /// ⚠ 两边**不得出现"一个红一个绿"**。
        /// </summary>
        private static void RenderAndVerify(SetupReport report)
        {
            List<string> produced;
            try
            {
                produced = RenderAll();
            }
            catch (Exception ex)
            {
                report.Add(CheckResult.Fail("RND-01", "渲染证据图落盘且非退化", "4 张且 distinctColors > 1",
                    "异常：" + ex.Message));
                return;
            }

            // ── RND-01：落盘 **且非退化** ──
            // ⚠ **印"实际落点（全路径）"，不印写死的 `Docs/Reports/...`**：`TestRootPath` 会随 `-outputDir`
            //    重定向 ⇒ 固定印相对路径会在重定向跑批时**误导**（让人以为证据进了仓库，其实进了临时目录）。
            var problems = new List<string>();
            var counts = new List<string>();
            foreach (string rel in new[] { TopViewName, PerspectiveName, AbAsIsName, AbFlipName })
            {
                string full = TestRootPath(rel);
                if (!File.Exists(full))
                {
                    problems.Add(full + "：不存在");
                    counts.Add(full + "：<缺失>");
                    continue;
                }

                long size = new FileInfo(full).Length;
                if (size < 100)
                {
                    problems.Add($"{full}：文件过小（{size} B，疑似 Git-LFS 指针或空文件 —— 指针不能当证据）");
                    counts.Add($"{full}：{size} B");
                    continue;
                }

                int ncol = DistinctColors(full);
                counts.Add($"{full}：{ncol} 色 sha={Sha12(full)}");
                if (ncol <= 1)
                {
                    problems.Add($"{full}：**退化图**（distinctColors={ncol} ⇒ 单一底色 = 什么都没渲染；"
                                 + "`-batchmode -nographics` 无 GPU 时会这样）");
                }
            }

            bool rndOk = problems.Count == 0 && produced.Count >= 4;
            report.Add(rndOk
                ? CheckResult.Ok("RND-01", "渲染证据图落盘且**非退化**（与 `qa_render_evidence_check.py` 同判）",
                    "4 张且 distinctColors > 1", string.Join("；", counts),
                    "**只查\"落盘\"不够**：无 GPU 的批处理下 `cam.Render()` 拿不到内容 ⇒ 纯灰空图。"
                    + "本项与离线工具 `qa_render_evidence_check.py` **同判**（`distinctColors > 1`）。")
                : CheckResult.Fail("RND-01", "渲染证据图落盘且**非退化**（与 `qa_render_evidence_check.py` 同判）",
                    "4 张且 distinctColors > 1",
                    string.Join("；", counts) + "｜违规：" + string.Join("；", problems)));

            // ── NAB-01：**可失败**（旧版硬编码 Ok ⇒ 永不红）──
            float diff = 0f;
            int pixelsA = 0;
            int pixelsB = 0;
            try
            {
                diff = DiffFraction(TestRootPath(AbAsIsName), TestRootPath(AbFlipName), out pixelsA, out pixelsB);
            }
            catch (Exception ex)
            {
                problems.Add("A/B 比较异常：" + ex.Message);
            }

            float meanAsIs = 0f, meanFlip = 0f;
            try
            {
                meanAsIs = MeanLuminance(TestRootPath(AbAsIsName));
                meanFlip = MeanLuminance(TestRootPath(AbFlipName));
            }
            catch
            {
                // 均值只是补充信息；失败不影响判定
            }

            bool abOk = pixelsA > 0 && pixelsA == pixelsB && diff > 0f;
            string abActual = $"A/B 差异 {diff * 100f:F2}% 像素不同（{pixelsA} vs {pixelsB} 像素）；"
                              + $"均值亮度 asIs={meanAsIs:F4} flipped={meanFlip:F4}";
            report.Add(abOk
                ? CheckResult.Ok("NAB-01", "法线 A/B 两版**必须有差异**（否则证据无信息）",
                    "差异像素占比 > 0", abActual,
                    $"掠射光 Euler(12,-118,0)、低角度视图、`_BumpScale={AbBumpScale}`（仅对比图放大，正式材质为 1）。"
                    + "方向判定仍是**视觉判据**（依据：受力特征明暗侧是否随绿色通道翻转而交换），"
                    + "结论与逐条依据写入 `Docs/35-A_Day3_材质与视觉基础_执行记录.md`；"
                    + "**本项只保证\"该证据不是空的\"**（两版相同 ⇒ FAIL）。")
                : CheckResult.Fail("NAB-01", "法线 A/B 两版**必须有差异**（否则证据无信息）",
                    "差异像素占比 > 0", abActual
                    + "｜**两版逐像素完全相同 ⇒ A/B 证据失效**（绿通道翻转没有产生任何可观测差异）。"));
        }

        /// <summary>解码 PNG 并统计**不同颜色数**（`> 1` = 非单一底色）；失败返回 <c>-1</c>。口径同离线工具。</summary>
        private static int DistinctColors(string fullPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(fullPath)))
                {
                    return -1;
                }

                Color32[] px = tex.GetPixels32();
                var set = new HashSet<int>();
                for (int i = 0; i < px.Length; i++)
                {
                    set.Add((px[i].r << 16) | (px[i].g << 8) | px[i].b);
                }

                return set.Count;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>文件的 SHA-256 前 12 个十六进制字符（与离线工具打印的 `sha` **逐字对齐**，便于交叉核对）。</summary>
        private static string Sha12(string fullPath)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(File.ReadAllBytes(fullPath));
                var sb = new System.Text.StringBuilder(12);
                for (int i = 0; i < 6; i++)
                {
                    sb.Append(h[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        /// <summary>逐像素比较两张同尺寸 PNG，返回**不同像素占比**（尺寸不同或解码失败 ⇒ <c>1.0</c>）。口径同离线工具。</summary>
        private static float DiffFraction(string fullA, string fullB, out int pixelsA, out int pixelsB)
        {
            Color32[] a = ReadPixels32(fullA, out pixelsA);
            Color32[] b = ReadPixels32(fullB, out pixelsB);
            if (a == null || b == null || a.Length != b.Length || a.Length == 0)
            {
                return 1f;
            }

            int differing = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b)
                {
                    differing++;
                }
            }

            return (float)differing / a.Length;
        }

        private static Color32[] ReadPixels32(string fullPath, out int pixels)
        {
            pixels = 0;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(fullPath)))
                {
                    return null;
                }

                Color32[] px = tex.GetPixels32();
                pixels = px.Length;
                return px;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        public const string AbAsIsName = "Docs/Reports/day3-normal-ab/ab-asis.png";
        public const string AbFlipName = "Docs/Reports/day3-normal-ab/ab-flipgreen.png";
        public const string TopViewName = "Docs/Reports/day3-top-view.png";
        public const string PerspectiveName = "Docs/Reports/day3-perspective.png";

        /// <summary>
        /// A/B 判读专用的法线强度放大（仅用于对比图，**不写入正式材质**）。
        /// 台呢过于光滑、法线细节极弱，1× 时两版差异肉眼不可辨；放大只改变幅度、不改变符号，
        /// 故可用于判别"方向是否被翻转"。正式材质仍是 `_BumpScale = 1`。
        /// </summary>
        public const float AbBumpScale = 4f;

        public static List<string> RenderAll()
        {
            var produced = new List<string>();

            Scene scene = SceneManager.GetActiveScene();
            GameObject camGo = FindRoot(scene, "Main Camera");
            Camera cam = camGo.GetComponent<Camera>();
            GameObject table = FindChild(scene, "PoolTable");

            // 1) 俯视图（用主相机设定）
            RenderToPng(cam, 1440, 900, TestRootPath(TopViewName));
            produced.Add(TopViewName);

            // 2) 透视（同一材质，低俯角，用于判断"是不是台球桌"）
            //
            // ── 机位口径变更（登记"谁裁定 / 为什么 / 前后值"）────────────────────────────
            // **谁裁定**：主理人（2026-10-16），依据是本人上报的发现。
            // **为什么**：本机位原为 `(0.15, 1.15, −2.15)`，是按**旧玩法取景（相机在 −Z 侧朝 +Z 看）**
            //   调的。本轮用户裁定放宽取景后，主相机搬到 **+Z 侧**（`GreyboxSceneSetup.CameraPosition`
            //   = `(0, 2.10, +2.80)`）⇒ 本机位变成"**从后墙那一侧看桌子**"：
            //   它仍能判"是不是台球桌"（`RND-01` 照常 PASS），但**它自称"透视证据"、实际已不代表玩家观感**
            //   —— 属"**证据与它的声称脱节**"，正是本卡在清的那类问题。
            // **前后值**：pos `(0.15, 1.15, −2.15)` → **`(0.15, 1.15, +2.15)`**；
            //   look `(0, 0.02, 0.05)` → **`(0, 0.02, −0.05)`**（即**按 z=0 镜像**，其余不变）。
            // **为什么用"镜像"而不是"改成主相机机位"**：本项的价值在**独立机位** ——
            //   独立机位能看到主相机看不到的侧面/入射角，是它对"材质/法线"的判别力来源；
            //   改成主相机机位就退化成 `day3-top-view.png` 的重复（主理人明确**不批**这种顶替）。
            //   镜像后仍是**低机位、离轴、贴近台面**的独立视角，只是换到了房间的**开口那一侧**，
            //   顺带把后墙/摆件纳入背景（背景不再是一面挡在相机与桌子之间的墙）。
            Vector3 prevPos = cam.transform.position;
            Quaternion prevRot = cam.transform.rotation;
            float prevFov = cam.fieldOfView;

            cam.transform.position = new Vector3(0.15f, 1.15f, 2.15f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.02f, -0.05f) - cam.transform.position, Vector3.up);
            cam.fieldOfView = 38f;
            RenderToPng(cam, 1440, 900, TestRootPath(PerspectiveName));
            produced.Add(PerspectiveName);

            // 3) 法线 A/B：掠射光 + 低角度视图，让法线扰动可见。
            //    绿通道的贡献只在"光照沿贴图 V 轴掠射"时显著，故光源取低仰角；
            //    台呢过于光滑（法线细节极弱），因此把 _BumpScale 放大到 4× 仅用于**判读符号**（已在证据与记录中注明）。
            Light sun = RenderSettings.sun;
            Vector3 prevSunRot = sun != null ? sun.transform.eulerAngles : Vector3.zero;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(12f, -118f, 0f);
            }

            cam.transform.position = new Vector3(0.95f, 1.02f, -1.92f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(-0.05f, 0.03f, 0.12f) - cam.transform.position, Vector3.up);
            cam.fieldOfView = 40f;

            MeshRenderer bodyRenderer = FindBodyRenderer(table);
            Material[] originalMats = bodyRenderer.sharedMaterials;
            int bodyIndex = 0;
            Material original = originalMats[bodyIndex];
            var abMaterial = new Material(original);

            byte[] png = File.ReadAllBytes(ProjectPaths.ToFullPath(MaterialVisualSetup.NormalTex));

            Texture2D asIs = MakeNormalVariant(png, false);
            Texture2D flipped = MakeNormalVariant(png, true);

            var withAb = (Material[])originalMats.Clone();
            withAb[bodyIndex] = abMaterial;
            bodyRenderer.sharedMaterials = withAb;

            abMaterial.SetFloat("_BumpScale", AbBumpScale);
            abMaterial.SetTexture("_BumpMap", asIs);
            RenderToPng(cam, 1440, 900, TestRootPath(AbAsIsName));
            produced.Add(AbAsIsName);

            abMaterial.SetTexture("_BumpMap", flipped);
            RenderToPng(cam, 1440, 900, TestRootPath(AbFlipName));
            produced.Add(AbFlipName);

            // 还原（不落盘任何临时资产）
            bodyRenderer.sharedMaterials = originalMats;
            UnityEngine.Object.DestroyImmediate(abMaterial);
            UnityEngine.Object.DestroyImmediate(asIs);
            UnityEngine.Object.DestroyImmediate(flipped);

            cam.transform.position = prevPos;
            cam.transform.rotation = prevRot;
            cam.fieldOfView = prevFov;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(prevSunRot);
            }

            return produced;
        }

        private static Texture2D MakeNormalVariant(byte[] pngBytes, bool flipGreen)
        {
            // linear:true —— 法线是线性数据，不能走 sRGB 采样
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(pngBytes);
            if (flipGreen)
            {
                Color[] px = tex.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    px[i].g = 1f - px[i].g;
                }

                tex.SetPixels(px);
                tex.Apply(false, false);
            }

            return tex;
        }

        private static MeshRenderer FindBodyRenderer(GameObject table)
        {
            foreach (MeshRenderer mr in table.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.subMeshCount >= 1)
                {
                    return mr;
                }
            }

            throw new InvalidOperationException("找不到球桌网格渲染器。");
        }

        private static void RenderToPng(Camera cam, int w, int h, string fullPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            rt.Create();

            RenderTexture prev = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;
            float prevAspect = cam.aspect;

            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            byte[] encoded = tex.EncodeToPNG();

            UnityEngine.Object.DestroyImmediate(tex);
            cam.targetTexture = prevTarget;
            cam.aspect = prevAspect;
            RenderTexture.active = prev;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            File.WriteAllBytes(fullPath, encoded);
            Debug.Log($"渲染已落盘：{fullPath}（{w}×{h}，{encoded.Length} 字节）");
        }

        private static float MeanLuminance(string fullPath)
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(bytes);
            Color32[] px = tex.GetPixels32();
            double sum = 0;
            for (int i = 0; i < px.Length; i++)
            {
                sum += (0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b) / 255.0;
            }

            float mean = (float)(sum / px.Length);
            UnityEngine.Object.DestroyImmediate(tex);
            return mean;
        }

        /// <summary>由 `-outputDir` 指定的输出根；空 = 写默认入库路径。</summary>
        private static string _outputDirOverride;

        private static string TestRootPath(string relative)
        {
            if (!string.IsNullOrEmpty(_outputDirOverride))
            {
                string rel = relative.StartsWith("Docs/Reports/")
                    ? relative.Substring("Docs/Reports/".Length)
                    : Path.GetFileName(relative);
                string full = Path.Combine(_outputDirOverride, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                return full;
            }

            return Path.Combine(SetupReport.ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// 薄转发 → <see cref="ReportOutput.ResolveReportPath"/>（**唯一实现**，铁律 16）。
        /// 本方法此前是独立实现，**缺 `Directory.CreateDirectory`**，与 `TableIntegrityValidator`
        /// 的同名方法已分叉（`-outputDir` 指向不存在目录时本版会失败）。
        /// </summary>
        public static string ResolveReportPath(string defaultRelative, string fileName)
            => ReportOutput.ResolveReportPath(defaultRelative, fileName);

        // ── helpers ────────────────────────────────────────────────────────

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == name)
                {
                    return go;
                }
            }

            return null;
        }

        private static GameObject FindChild(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }

                Transform t = FindChildRecursive(root.transform, name);
                if (t != null)
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            Transform t = FindChildRecursive(parent, name);
            return t != null ? t.gameObject : null;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform c in parent)
            {
                if (c.name == name)
                {
                    return c;
                }

                Transform r = FindChildRecursive(c, name);
                if (r != null)
                {
                    return r;
                }
            }

            return null;
        }

        /// <summary>
        /// 把一个世界包围盒的 **8 角**投影到 <paramref name="cam"/> 的视口，返回 `(minX, maxX, minY, maxY)`；
        /// <paramref name="behind"/> = 有角落在相机后方（`viewport.z ≤ 0`）。
        /// 供 `CAM-02`（可玩区）与 `CAM-03`（外框）**共用同一实现**（铁律 16：同一度量只此一处）。
        /// </summary>
        private static Vector4 ProjectViewportBox(Bounds b, Camera cam, out bool behind)
        {
            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            behind = false;
            for (int xi = 0; xi < 2; xi++)
            for (int yi = 0; yi < 2; yi++)
            for (int zi = 0; zi < 2; zi++)
            {
                var c = new Vector3(
                    xi == 0 ? b.min.x : b.max.x,
                    yi == 0 ? b.min.y : b.max.y,
                    zi == 0 ? b.min.z : b.max.z);
                Vector3 vp = cam.WorldToViewportPoint(c);
                if (vp.z <= 0f)
                {
                    behind = true;
                }

                minX = Mathf.Min(minX, vp.x);
                maxX = Mathf.Max(maxX, vp.x);
                minY = Mathf.Min(minY, vp.y);
                maxY = Mathf.Max(maxY, vp.y);
            }

            return new Vector4(minX, maxX, minY, maxY);
        }

        /// <summary>
        /// **`CAM-05`：Main 与本工程诊断场景（灰盒）的取景逐项比对**（位姿 / 序列化 fov / 打球区投影包围盒）。
        ///
        /// 用户要求 = 「Main 的观感 = 我在灰盒里调的观感」 ⇒ 把口头约定变成**可证伪的不变量**：
        /// **只改其中一边**的相机 / fov / 根缩放 ⇒ 必红（本项因此**不依赖** `CAM-01` 的常量表）。
        /// 采样用**同一实现**（<see cref="TrySampleFraming"/>），各取**本方**根缩放下的打球区（铁律 16）。
        /// </summary>
        private static void CheckFramingConsistency(SetupReport report, Scene mainScene, float aspect)
        {
            const string title = "Main 与灰盒 取景逐项一致（位姿 / fov / 打球区投影包围盒）";
            const string expected = "Δpos ≤ 1e-3 m / Δrot ≤ 0.01° / Δfov ≤ 1e-3° / Δbox ≤ 2e-3";

            if (!ProjectPaths.Exists(GreyboxSceneSetup.ScenePath))
            {
                report.Add(CheckResult.Fail("CAM-05", title, expected,
                    "灰盒场景缺失（无法比对）：" + GreyboxSceneSetup.ScenePath));
                return;
            }

            if (!TrySampleFraming(mainScene, aspect, out Vector3 mainPos, out Quaternion mainRot, out float mainFov,
                    out Vector4 mainBox, out string mainDetail))
            {
                report.Add(CheckResult.Fail("CAM-05", title, expected, "Main 取景采样失败：" + mainDetail));
                return;
            }

            Scene greybox = SceneManager.GetSceneByPath(GreyboxSceneSetup.ScenePath);
            bool opened = !greybox.IsValid() || !greybox.isLoaded;
            if (opened)
            {
                greybox = EditorSceneManager.OpenScene(GreyboxSceneSetup.ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                if (!TrySampleFraming(greybox, aspect, out Vector3 gPos, out Quaternion gRot, out float gFov,
                        out Vector4 gBox, out string gDetail))
                {
                    report.Add(CheckResult.Fail("CAM-05", title, expected, "灰盒取景采样失败：" + gDetail));
                    return;
                }

                float posDelta = Vector3.Distance(mainPos, gPos);
                float rotDelta = Quaternion.Angle(mainRot, gRot);
                float fovDelta = Mathf.Abs(mainFov - gFov);
                float boxDelta = Mathf.Max(
                    Mathf.Max(Mathf.Abs(mainBox.x - gBox.x), Mathf.Abs(mainBox.y - gBox.y)),
                    Mathf.Max(Mathf.Abs(mainBox.z - gBox.z), Mathf.Abs(mainBox.w - gBox.w)));

                bool ok = posDelta <= FramingPoseTolerance
                          && rotDelta <= FramingRotationToleranceDeg
                          && fovDelta <= FramingFovTolerance
                          && boxDelta <= FramingBoxTolerance;
                string actual = $"Δpos={posDelta:E3} m，Δrot={rotDelta:E3}°，Δfov={fovDelta:E3}°，Δbox={boxDelta:E3}；"
                                + $"main[{mainDetail}] greybox[{gDetail}]";

                report.Add(ok
                    ? CheckResult.Ok("CAM-05", title, expected, actual,
                        "用户要求：Main 的观感 = 灰盒里调的那套观感 ⇒ 把口头约定变成可证伪不变量（只改一边必红）。")
                    : CheckResult.Fail("CAM-05", title, expected,
                        actual + " ⇒ 两场景取景不一致（有人只改了其中一边）。"));
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(greybox, true);
                }
            }
        }

        /// <summary>
        /// 在一个**已加载**的场景里采样"相机位姿 / **序列化** fov / 该场景**本方**根缩放下的打球区投影包围盒"。
        /// `CAM-05` 对 Main 与灰盒**用同一实现**采样（铁律 16）。
        /// </summary>
        private static bool TrySampleFraming(Scene scene, float aspect, out Vector3 camPos, out Quaternion camRot,
            out float serializedFov, out Vector4 clothBox, out string detail)
        {
            camPos = default;
            camRot = default;
            serializedFov = 0f;
            clothBox = default;
            detail = string.Empty;

            // ⚠ 两个场景的层级**不同**：Main 的 `PlayfieldRoot` 是**场景根**；灰盒的在 `PhysicsGreybox` 根**之下**
            // ⇒ 必须**递归**查找（按名字），否则灰盒侧永远采样失败（首次实现即栽在这里，实测报 `playfieldRoot=False`）。
            GameObject playfieldRoot = FindChild(scene, GreyboxSceneSetup.PlayfieldRootObjectName);
            Camera cam = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                cam = root.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    break;
                }
            }

            if (playfieldRoot == null || cam == null)
            {
                detail = $"playfieldRoot={playfieldRoot != null} cam={cam != null}";
                return false;
            }

            camPos = cam.transform.position;
            camRot = cam.transform.rotation;
            serializedFov = cam.fieldOfView;   // 取**改前**的值 = 授权值

            float prevAspect = cam.aspect;
            float prevFov = cam.fieldOfView;
            var fitter = cam.GetComponent<CameraViewportFitter>();
            cam.aspect = aspect;
            cam.fieldOfView = fitter != null
                ? CameraViewportFitter.ResolveVerticalFovDeg(aspect, fitter.baseAspect, fitter.baseVerticalFovDeg,
                    fitter.maxVerticalFovDeg)
                : cam.fieldOfView;

            float vs = playfieldRoot.transform.lossyScale.x;
            var cloth = new Bounds(Vector3.zero, new Vector3(
                PoolConstants.ClothHalfLength * vs * 2f, 0.0001f, PoolConstants.ClothHalfWidth * vs * 2f));
            clothBox = ProjectViewportBox(cloth, cam, out _);

            cam.aspect = prevAspect;
            cam.fieldOfView = prevFov;

            detail = $"pos={camPos} fov={serializedFov:F2} "
                     + $"cloth x[{clothBox.x:F3},{clothBox.y:F3}] y[{clothBox.z:F3},{clothBox.w:F3}]";
            return true;
        }

        private static Bounds ComputeWorldBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.one);
            }

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }

            return b;
        }
    }
}
