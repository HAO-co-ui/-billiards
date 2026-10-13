using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// Day 3 验收器：材质 / 场景 / Prefab / 灯光 / 相机 的可判据检查 + 渲染证据（俯视图、法线 A/B 对比图）。
    ///
    /// 与 Day 1/2 同源同风格：只读为主、全部结论带 expected/actual、报告先落盘再退出。
    /// 渲染在批处理下用 RenderTexture + EncodeToPNG 落盘到 `Docs/Reports/`。
    /// </summary>
    public static class Day3Validator
    {
        public const string ReportFileName = "day3-verification.json";
        public const string AbDir = "Docs/Reports/day3-normal-ab";

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Verify/Validate Day-3 Visuals (materials/scene/camera)", false, 401)]
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
                Day3VisualSetup.Apply();
                RenderAll();
            }
            catch (Exception ex)
            {
                Debug.LogError("Day-3 渲染失败：" + ex);
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
                // ⚠ R2：**不调用 Day3VisualSetup.Apply()**。校验器一旦重建被校验对象，
                // 就只能证明“生成器自洽”，不能证明“被提交的场景是对的”。
                // 需要重建场景时，请单独运行 Billiards/Visual/Build Day-3 Materials, Scene & Prefabs。
                report = RunAll();
                exitCode = report.failCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("Day-3 校验过程中抛出异常：" + ex);
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
            Material body = LoadMat(Day3VisualSetup.BodyMaterialPath);
            bool bodyOk = body != null
                          && body.shader != null && body.shader.name == Day3VisualSetup.UrpLitShaderName
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

            Material nets = LoadMat(Day3VisualSetup.NetsMaterialPath);
            bool netsOk = nets != null
                          && nets.shader != null && nets.shader.name == Day3VisualSetup.UrpLitShaderName
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
            for (int i = 0; i < Day3VisualSetup.BallCount; i++)
            {
                Material bm = LoadMat($"{Day3VisualSetup.BallsMaterialDir}/{Day3VisualSetup.BallName(i)}.mat");
                if (bm != null && bm.shader != null && bm.shader.name == Day3VisualSetup.UrpLitShaderName
                    && bm.GetTexture("_BaseMap") != null && !bm.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    ballsOk++;
                }
            }

            report.Add(ballsOk == Day3VisualSetup.BallCount
                ? CheckResult.Ok("MAT-03", $"16 个球材质齐备且指向各自 BaseColor", "16/16", ballsOk + "/16")
                : CheckResult.Fail("MAT-03", $"16 个球材质齐备且指向各自 BaseColor", "16/16", ballsOk + "/16"));

            Material cue = LoadMat(Day3VisualSetup.CueMaterialPath);
            report.Add(cue != null && cue.GetTexture("_BaseMap") != null
                ? CheckResult.Ok("MAT-04", "球杆材质存在且含 BaseColor", "存在", "存在")
                : CheckResult.Fail("MAT-04", "球杆材质存在且含 BaseColor", "存在", DescribeMat(cue)));

            Material floor = LoadMat(Day3VisualSetup.FloorMaterialPath);
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
            if (!ProjectPaths.Exists(Day3VisualSetup.MainScenePath))
            {
                report.Add(CheckResult.Fail("SCN-01", "Main.unity 存在", Day3VisualSetup.MainScenePath, "未找到"));
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != Day3VisualSetup.MainScenePath)
            {
                scene = EditorSceneManager.OpenScene(Day3VisualSetup.MainScenePath, OpenSceneMode.Single);
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
                report.Add(CheckResult.Fail("SCN-02", "PlayfieldRoot 缩放 = 1（1.2 未误用）", "1", "无法检查"));
                return;
            }

            Vector3 s = playfield.transform.localScale;
            bool scaleOne = Mathf.Approximately(s.x, 1f) && Mathf.Approximately(s.y, 1f) && Mathf.Approximately(s.z, 1f);
            report.Add(scaleOne
                ? CheckResult.Ok("SCN-02", "PlayfieldRoot 缩放 = 1（1.2 未误用，物理保持规范米制）", "1", s.ToString())
                : CheckResult.Fail("SCN-02", "PlayfieldRoot 缩放 = 1（1.2 未误用）", "1", s.ToString()));

            report.Add(table.transform.IsChildOf(playfield.transform)
                ? CheckResult.Ok("SCN-03", "球桌挂在 PlayfieldRoot 之下（视觉根且零旋转）", "是", "是",
                    "球桌不得再叠加手动旋转：唯一模型侧修正在 PoolGeometry。")
                : CheckResult.Fail("SCN-03", "球桌挂在 PlayfieldRoot 之下", "是", "否"));

            // SCN-04：实例化球桌的**世界**几何 vs 转换链期望 —— ADR-0001 §9 修正后的核心不变量。
            // 转换链（PoolGeometry）已把模型自带朝向 ModelOrientation(Rx≈270°) 纳入，
            // 故 `PhysicsToUnity` 与实例化网格的世界位置**恰好重合**（不再是"在某对称性下近似重合"）。
            Bounds wb = ComputeWorldBounds(table);
            float worldErr = Mathf.Max(
                Mathf.Abs(wb.min.x - PoolGeometry.WorldBoundsMin.x),
                Mathf.Abs(wb.min.y - PoolGeometry.WorldBoundsMin.y),
                Mathf.Abs(wb.min.z - PoolGeometry.WorldBoundsMin.z),
                Mathf.Abs(wb.max.x - PoolGeometry.WorldBoundsMax.x),
                Mathf.Abs(wb.max.y - PoolGeometry.WorldBoundsMax.y),
                Mathf.Abs(wb.max.z - PoolGeometry.WorldBoundsMax.z));

            table.transform.GetPositionAndRotation(out Vector3 wp, out Quaternion wr);
            string wt = $"worldMin=({wb.min.x:F6},{wb.min.y:F6},{wb.min.z:F6}) "
                      + $"worldMax=({wb.max.x:F6},{wb.max.y:F6},{wb.max.z:F6}); "
                      + $"expectedMin={PoolGeometry.WorldBoundsMin} expectedMax={PoolGeometry.WorldBoundsMax}; "
                      + $"rootRot={wr.eulerAngles}, rootPos={wp}, lossyScale={table.transform.lossyScale}";

            report.Add(worldErr < TableIntegrityValidator.BoundsTolerance
                ? CheckResult.Ok("SCN-04", "实例化球桌世界几何 = 转换链期望（不变量恰好为真）",
                    $"±{TableIntegrityValidator.BoundsTolerance:E0} m", $"err={worldErr:E3} m",
                    wt + "；本条可被**证伪** —— 翻转 PoolGeometry.ModelOrientation 会使 err 跳到 2.77e-4 m"
                    + "（= 角袋 x 向不对称）而 FAIL（由 ModelOrientationTests 断言）。"
                    + "根节点自带 Rx≈270° 属模型姿态（ModelOrientation 常量），场景未叠加任何手动旋转（守 ADR §7.2）。")
                : CheckResult.Fail("SCN-04", "实例化球桌世界几何 = 转换链期望（不变量恰好为真）",
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
                    Material want = i == 1 ? LoadMat(Day3VisualSetup.NetsMaterialPath) : LoadMat(Day3VisualSetup.BodyMaterialPath);
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
            // ① 扫描范围由「仅当前场景」扩到「Scenes + Prefabs 全量」：Day 4/5 正是从这 18 个 prefab 实例化球，
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
        }

        // ── Prefab ─────────────────────────────────────────────────────────

        private static void VerifyPrefabs(SetupReport report)
        {
            int found = 0;
            for (int i = 0; i < Day3VisualSetup.BallCount; i++)
            {
                string p = $"{Day3VisualSetup.BallsPrefabDir}/{Day3VisualSetup.BallName(i)}.prefab";
                if (ProjectPaths.Exists(p) && AssetDatabase.LoadAssetAtPath<GameObject>(p) != null)
                {
                    found++;
                }
            }

            report.Add(found == Day3VisualSetup.BallCount
                ? CheckResult.Ok("PRB-01", "16 个球 Prefab 齐备", "16/16", found + "/16")
                : CheckResult.Fail("PRB-01", "16 个球 Prefab 齐备", "16/16", found + "/16"));

            bool baseOk = ProjectPaths.Exists(Day3VisualSetup.BaseBallPrefabPath)
                          && AssetDatabase.LoadAssetAtPath<GameObject>(Day3VisualSetup.BaseBallPrefabPath) != null;
            bool cueOk = ProjectPaths.Exists(Day3VisualSetup.CuePrefabPath)
                         && AssetDatabase.LoadAssetAtPath<GameObject>(Day3VisualSetup.CuePrefabPath) != null;
            report.Add(baseOk && cueOk
                ? CheckResult.Ok("PRB-02", "BallPrefab 基座 + 球杆 Prefab 存在", "均存在", "均存在")
                : CheckResult.Fail("PRB-02", "BallPrefab 基座 + 球杆 Prefab 存在", "均存在",
                    $"base={baseOk}, cue={cueOk}"));

            GameObject ball0 = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{Day3VisualSetup.BallsPrefabDir}/{Day3VisualSetup.BallName(0)}.prefab");
            Material want0 = LoadMat($"{Day3VisualSetup.BallsMaterialDir}/{Day3VisualSetup.BallName(0)}.mat");
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
            Mesh ballMesh = FirstMesh($"{Day3VisualSetup.BallsModelDir}/{Day3VisualSetup.BallName(0)}.fbx");
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

            Mesh cueMesh = FirstMesh(Day3VisualSetup.CueModelPath);
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
                    $"Unity 导入后网格本地长轴 = {cueAxis}；文档声称杆身 +Z，**实测为准**，Day 10 球杆动画需据此定向。")
                : CheckResult.Fail("GEO-02", "球杆网格长 = PoolConstants.CueLength", PoolConstants.CueLength.ToString("R"),
                    $"{cueLen:F4} m（长轴 = {cueAxis}）"));

            int fbxCount = 0;
            for (int i = 0; i < Day3VisualSetup.BallCount; i++)
            {
                if (ProjectPaths.Exists($"{Day3VisualSetup.BallsModelDir}/{Day3VisualSetup.BallName(i)}.fbx"))
                {
                    fbxCount++;
                }
            }

            report.Add(fbxCount == Day3VisualSetup.BallCount
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

            float posErr = Vector3.Distance(cam.transform.position, Day3VisualSetup.CameraPosition);
            bool paramsOk = posErr < 1e-3f
                            && Mathf.Abs(cam.fieldOfView - Day3VisualSetup.CameraFov) < 0.01f
                            && cam.nearClipPlane > 0f;

            report.Add(paramsOk
                ? CheckResult.Ok("CAM-01", "主相机参数符合设定", $"pos={Day3VisualSetup.CameraPosition}, fov={Day3VisualSetup.CameraFov}",
                    $"pos={cam.transform.position}, fov={cam.fieldOfView}", "俯视带俯角；俯视截图即由该相机渲染。")
                : CheckResult.Fail("CAM-01", "主相机参数符合设定", $"pos={Day3VisualSetup.CameraPosition}, fov={Day3VisualSetup.CameraFov}",
                    $"pos={cam.transform.position}, fov={cam.fieldOfView}"));

            // 球桌是否完整落在视锥内（用世界包围盒 8 角投影到视口）
            GameObject table = FindChild(scene, "PoolTable");
            if (table != null)
            {
                Bounds b = ComputeWorldBounds(table);
                cam.aspect = 1440f / 900f;
                var corners = new Vector3[8];
                int k = 0;
                for (int xi = 0; xi < 2; xi++)
                for (int yi = 0; yi < 2; yi++)
                for (int zi = 0; zi < 2; zi++)
                {
                    corners[k++] = new Vector3(
                        xi == 0 ? b.min.x : b.max.x,
                        yi == 0 ? b.min.y : b.max.y,
                        zi == 0 ? b.min.z : b.max.z);
                }

                float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
                bool behind = false;
                foreach (Vector3 c in corners)
                {
                    Vector3 vp = cam.WorldToViewportPoint(c);
                    if (vp.z <= 0f) behind = true;
                    minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
                    minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
                }

                bool inFrame = !behind && minX >= -0.02f && maxX <= 1.02f && minY >= -0.02f && maxY <= 1.02f;
                report.Add(inFrame
                    ? CheckResult.Ok("CAM-02", "球桌完整落在相机视锥内", "视口 [0,1]",
                        $"x[{minX:F3},{maxX:F3}] y[{minY:F3},{maxY:F3}]")
                    : CheckResult.Fail("CAM-02", "球桌完整落在相机视锥内", "视口 [0,1]",
                        $"x[{minX:F3},{maxX:F3}] y[{minY:F3},{maxY:F3}] behind={behind}"));
            }

            Light sun = RenderSettings.sun;
            bool lightOk = sun != null && sun.type == LightType.Directional
                           && Mathf.Abs(sun.intensity - Day3VisualSetup.SunIntensity) < 1e-3f
                           && RenderSettings.ambientMode != AmbientMode.Skybox;
            report.Add(lightOk
                ? CheckResult.Ok("LGT-01", "主光 + 环境光已配置", $"dir light, intensity={Day3VisualSetup.SunIntensity}",
                    $"intensity={sun.intensity}, ambient={RenderSettings.ambientMode}, color={sun.color}")
                : CheckResult.Fail("LGT-01", "主光 + 环境光已配置", $"dir light, intensity={Day3VisualSetup.SunIntensity}",
                    sun == null ? "<无 RenderSettings.sun>" : $"intensity={sun.intensity}, ambient={RenderSettings.ambientMode}"));
        }

        // ── 渲染 ───────────────────────────────────────────────────────────

        private static void RenderAndVerify(SetupReport report)
        {
            List<string> produced;
            try
            {
                produced = RenderAll();
            }
            catch (Exception ex)
            {
                report.Add(CheckResult.Fail("RND-01", "渲染证据图落盘", "3 张", "异常：" + ex.Message));
                return;
            }

            report.Add(produced.Count >= 4
                ? CheckResult.Ok("RND-01", "渲染证据图落盘", "俯视图 + 透视 + 法线 A/B（4 张）",
                    string.Join("; ", produced))
                : CheckResult.Fail("RND-01", "渲染证据图落盘", "4 张", produced.Count + " 张"));

            float meanAsIs = 0f, meanFlip = 0f;
            try
            {
                meanAsIs = MeanLuminance(TestRootPath(AbAsIsName));
                meanFlip = MeanLuminance(TestRootPath(AbFlipName));
            }
            catch
            {
                // 忽略：统计失败不影响落盘事实
            }

            report.Add(CheckResult.Ok("NAB-01", "法线方向 A/B 已渲染（结论见执行记录）",
                "两版受光对比图", $"{AbAsIsName} vs {AbFlipName}",
                $"掠射光 Euler(12,-118,0)、低角度视图、`_BumpScale={AbBumpScale}`（仅对比图放大，正式材质为 1）；"
                + $"两版均值亮度 asIs={meanAsIs:F4} flipped={meanFlip:F4}。"
                + "方向判定为**视觉判据**（依据：受力特征的明暗侧是否随绿色通道翻转而交换），"
                + "结论与逐条依据写入 `Docs/35-A_Day3_材质与视觉基础_执行记录.md`。"));
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
            Vector3 prevPos = cam.transform.position;
            Quaternion prevRot = cam.transform.rotation;
            float prevFov = cam.fieldOfView;

            cam.transform.position = new Vector3(0.15f, 1.15f, -2.15f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.02f, 0.05f) - cam.transform.position, Vector3.up);
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

            byte[] png = File.ReadAllBytes(ProjectPaths.ToFullPath(Day3VisualSetup.NormalTex));

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
