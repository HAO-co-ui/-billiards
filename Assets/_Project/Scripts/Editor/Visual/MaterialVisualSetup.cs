using System;
using System.Collections.Generic;
using System.IO;
using Billiards.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 材质与视觉基础的可复现构建器（配置即代码，与工程配置/数据导入同一哲学）。
    ///
    /// 产出：
    ///  · 桌体材质（URP Lit / Opaque）  · 袋网材质（URP Lit / Alpha Clipping, cutoff 0.5, 双面）
    ///  · 房间地面材质（最小布景，用 PoolConstants.TableLegBottomY 对齐桌腿底）
    ///  · 16 个球材质 + 16 个球 Prefab（含 BallPrefab 基座） · 球杆材质 + 球杆 Prefab
    ///  · Main.unity：PlayfieldRoot（**scale = `PoolConstants.PlayfieldVisualScale`**）→ 球桌 + 地面；
    ///    Room→Floor；主平行光；**玩法取景相机**（位姿 / FOV 引用 `GreyboxSceneSetup` 的既有常量）
    ///  · Main.unity 末尾再叠加**玩法层**（<see cref="GreyboxSceneSetup.AttachMainGameplayLayer"/>）
    ///
    /// 硬约束遵守：
    ///  · 唯一坐标转换点 = <see cref="PoolGeometry"/>，本类**不做任何轴/手性/缩放转换**；
    ///    球桌模型不再叠加手动旋转（模型侧修正已由导入与 PoolGeometry 承担）。
    ///  · **`PlayfieldVisualScale`（1.2）只作用于 `PlayfieldRoot` 这一个视觉根**，物理保持规范米制。
    ///    ⚠ 口径变更：本类**先前**把 `PlayfieldRoot` 留成 scale=1（"预留视觉缩放层"）。现在 `Main.unity`
    ///    是**正式游戏入口**，玩法口径就是 1.2（与灰盒一致）⇒ 本类**应用** 1.2，
    ///    且**地面必须与桌同挂该根**（否则根变 1.2 后地面不缩放、桌子浮起 0.2× 桌腿高）。
    ///    相机常量收敛到 `GreyboxSceneSetup` 一处（铁律 16：同一不变量只有一处实现）。
    ///  · 不手改 ProjectSettings/*.asset；场景/材质全部走官方 API。
    /// </summary>
    public static class MaterialVisualSetup
    {
        public const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        public const string TableModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        public const string BodyMaterialPath = "Assets/_Project/Materials/Table_Body.mat";
        public const string NetsMaterialPath = "Assets/_Project/Materials/Table_Nets.mat";
        public const string FloorMaterialPath = "Assets/_Project/Materials/Room_Floor.mat";
        public const string CueMaterialPath = "Assets/_Project/Materials/Cue_Pool.mat";

        public const string BaseColorTex = "Assets/_Project/Art/Textures/table_basecolor.jpg";
        public const string NormalTex = "Assets/_Project/Art/Textures/table_normal.png";
        public const string NetTex = "Assets/_Project/Art/Textures/table_net_rgba.png";
        public const string PackedMaskTex = "Assets/_Project/Art/Textures/table_metallic_roughness.png";

        public const string BallsModelDir = BilliardsPropsAssetPostprocessor.BallsModelDir;
        public const string BallsTexDir = BilliardsPropsAssetPostprocessor.BallsTextureDir;
        public const string CueModelPath = BilliardsPropsAssetPostprocessor.CueModelPath;
        public const string CueTexPath = BilliardsPropsAssetPostprocessor.CueTexturePath;

        public const string BallsMaterialDir = "Assets/_Project/Materials/Balls";
        public const string BallsPrefabDir = "Assets/_Project/Prefabs/Balls";
        public const string CuePrefabPath = "Assets/_Project/Prefabs/Cue/PoolCue.prefab";
        public const string BaseBallPrefabPath = "Assets/_Project/Prefabs/Balls/BallPrefab.prefab";

        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

        public const int BallCount = 16;

        // 灯光参数（MaterialVisualValidator 会读回核对）
        //
        // ⚠ **相机参数已从这里删除**（口径收敛，铁律 16）：本类先前自己持有一份
        // `CameraPosition` / `CameraTarget` / `CameraFov(=37°)`，而 `GreyboxSceneSetup` 另有一份
        // （pos `(0,2.10,−0.75)` / look `(0,0.02,0.06)` / fov `52`）—— **同一不变量两处实现**。
        // `Main.unity` 现在是**正式游戏入口**、取景必须与玩法口径一致 ⇒
        // **唯一真源 = `GreyboxSceneSetup`**（`CameraPosition` / `CameraLookTarget` / `CameraBaseVerticalFovDeg`），
        // 生成侧与验收器（`CAM-01`）都引用它。
        public const float CameraNear = 0.05f;
        public const float CameraFar = 60f;

        public static readonly Vector3 SunEuler = new Vector3(52f, -34f, 0f);
        public static readonly Color SunColor = new Color(1f, 0.957f, 0.902f);
        public const float SunIntensity = 1.40f;

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Visual/Build Materials, Scene & Prefabs", false, 400)]
        public static void ApplyInteractive()
        {
            Apply();
            Debug.Log("视觉基础构建完成。");
            EditorUtility.DisplayDialog("Billiards Material Visual Setup",
                "材质 / 场景 / Prefab 构建完成。\n\n材质：" + BodyMaterialPath + "\n" + NetsMaterialPath, "OK");
        }

        public static void Apply()
        {
            AssetDatabase.Refresh();
            BuildMaterials();
            BuildScene();
            BuildPrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// **只重建场景**（不重写材料 / 预制件）—— `Main.unity` 生成侧改动的复现与批处理入口。
        /// 与 <see cref="Apply"/> 的区别：不调 `BuildMaterials` / `BuildPrefabs`
        /// （那会重写一批与本次改动无关的资产、制造 git 噪声）。场景本身会随后接上玩法层
        /// （<see cref="GreyboxSceneSetup.AttachMainGameplayLayer"/>，见 <see cref="BuildScene"/> 末尾）。
        /// </summary>
        public static void BuildSceneFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                AssetDatabase.Refresh();
                BuildScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("MAIN_SCENE_BUILD_OK scene=" + MainScenePath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Main 场景重建失败：" + ex);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // ── 材质 ───────────────────────────────────────────────────────────

        private static Shader UrpLit()
        {
            Shader sh = Shader.Find(UrpLitShaderName);
            if (sh == null)
            {
                throw new InvalidOperationException("找不到 URP Lit 着色器：" + UrpLitShaderName);
            }

            return sh;
        }

        public static void BuildMaterials()
        {
            EnsureDir("Assets/_Project/Materials");
            EnsureDir(BallsMaterialDir);

            BuildBodyMaterial();
            BuildNetsMaterial();
            BuildFloorMaterial();
            BuildBallMaterials();
            BuildCueMaterial();

            // 球房环境材质（墙 / 地毯 / 盆栽 / 长凳 / 木器族 / 黄铜族 / 粉笔）—— 唯一实现点在 RoomEnvironmentSetup。
            RoomEnvironmentSetup.BuildMaterials();
        }

        private static void BuildBodyMaterial()
        {
            Material m = LoadOrCreate(BodyMaterialPath);
            Configure(m, "Table_Body");

            m.SetTexture("_BaseMap", LoadTexture(BaseColorTex, nameof(BaseColorTex)));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", LoadTexture(NormalTex, nameof(NormalTex)));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", LoadTexture(PackedMaskTex, nameof(PackedMaskTex)));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // 0 = Metallic Alpha
            m.DisableKeyword("_SMOOTHNESSSOURCE_ALBEDO_ALPHA");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");

            // Opaque（桌体必须不透明）
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
        }

        private static void BuildNetsMaterial()
        {
            Material m = LoadOrCreate(NetsMaterialPath);
            Configure(m, "Table_Nets");

            m.SetTexture("_BaseMap", LoadTexture(NetTex, nameof(NetTex)));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");

            // Alpha Clipping（cutoff 0.5，双面；桌体本身仍不透明）
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", BilliardsAssetPostprocessor.NetAlphaCutoff);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_Cull", 0f);                        // 0 = 双面
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
        }

        public static void BuildFloorMaterial()
        {
            Material m = LoadOrCreate(FloorMaterialPath);
            Configure(m, "Room_Floor");

            // 地面贴图与平铺口径的**唯一实现点**在 `RoomEnvironmentSetup`（可平铺木板贴图，复用本材质资产，
            // 不新增材质 ⇒ 守住 `SmokeProbe.MaxDistinctMaterialCount`）。
            RoomEnvironmentSetup.ConfigureFloorMaterial(m);
        }

        private static void BuildBallMaterials()
        {
            for (int i = 0; i < BallCount; i++)
            {
                string name = BallName(i);
                string texPath = $"{BallsTexDir}/{name}_BaseColor_1024x512.png";
                Material m = LoadOrCreate($"{BallsMaterialDir}/{name}.mat");
                Configure(m, name);
                m.SetTexture("_BaseMap", LoadTexture(texPath, name));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.85f);          // 台球是抛光树脂，偏光滑
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Geometry;
                EditorUtility.SetDirty(m);
            }
        }

        private static void BuildCueMaterial()
        {
            Material m = LoadOrCreate(CueMaterialPath);
            Configure(m, "Cue_Pool");
            m.SetTexture("_BaseMap", LoadTexture(CueTexPath, "cue"));
            m.SetColor("_BaseColor", Color.white);
            // **与资产现值同步**：P17 把球杆漆面反光调成 0.78/0.1，但那次只改了 `Cue_Pool.mat`、
            // **没改本生成器** ⇒ 重跑本方法会把球杆外观静默降回 0.55/0（"重建材质 = 观感变更"）。
            // 这里补齐单一真源；改观感只改这两行（并同步资产）。
            m.SetFloat("_Metallic", 0.1f);
            m.SetFloat("_Smoothness", 0.78f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
        }

        private static void Configure(Material m, string name)
        {
            m.name = name;
            if (m.shader == null || m.shader.name != UrpLitShaderName)
            {
                m.shader = UrpLit();
            }

            m.SetShaderPassEnabled("ShadowCaster", true);
            m.SetShaderPassEnabled("DepthOnly", true);
            m.SetShaderPassEnabled("DepthNormals", true);
        }

        // ── 场景 ───────────────────────────────────────────────────────────

        private static void BuildScene()
        {
            EnsureDir("Assets/_Project/Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1) 视觉根（**表现层 1.2**）：物理仍是规范米制，只有"看得见的东西"放大。
            //    唯一真源 = `PoolConstants.PlayfieldVisualScale`；显示↔规范映射的唯一函数是 `PlayfieldView`。
            var playfield = new GameObject("PlayfieldRoot");
            playfield.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            playfield.transform.localScale = Vector3.one * PoolConstants.PlayfieldVisualScale;

            // 球桌可视物走**唯一实现点**（与物理灰盒共用；各写一份必然分叉，见 `TableVisualBuilder` 的类注释）。
            TableVisualBuilder.AttachTable(playfield.transform);

            // 2) 最小布景：地面（与桌腿底对齐）—— 同样走唯一实现点。
            //    ⚠ **本轮修的真缺陷（"改一个数、坏另一处"的典型）**：
            //    旧实现挂 `playfield.transform.parent`，而 `playfield` 是**场景根** ⇒ 实参是 `null` ⇒
            //    `AttachRoom` 把 Room 落在**场景根**（`AttachRoom` 自己注释也这么写）。地面用的是
            //    `localPosition = (0, TableLegBottomY, 0)` ⇒ **地面世界 y = 父链 y 缩放 × TableLegBottomY**。
            //    根为 1 时看不出；**根改成 1.2 后**地面仍是 1×（−0.8635）、桌腿底是 1.2×（−1.0362）
            //    ⇒ **桌子浮起 0.1727 m**。灰盒本来就是对的（`AttachRoom(playfieldRoot)`）。
            //    ⇒ 与灰盒统一为**同一形态**：桌与地面**同挂缩放根**。该不变量由 day3 的 `SCN-07` 守
            //    （`|地面世界 y − 桌子世界 AABB min.y|`，两边都取**实测对象** ⇒ 非恒真）。
            TableVisualBuilder.AttachRoom(playfield.transform);

            // 3) 灯光
            var sunGo = new GameObject("Directional Light");
            sunGo.transform.rotation = Quaternion.Euler(SunEuler);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.44f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.28f, 0.30f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.13f, 0.14f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;

            // 4) 相机 = **玩法取景**（位姿 / FOV 引用 `GreyboxSceneSetup` 的既有常量 ⇒ 单一真源）
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            camGo.transform.position = GreyboxSceneSetup.CameraPosition;
            camGo.transform.rotation = Quaternion.LookRotation(
                GreyboxSceneSetup.CameraLookTarget - GreyboxSceneSetup.CameraPosition, Vector3.up);
            cam.fieldOfView = GreyboxSceneSetup.CameraBaseVerticalFovDeg;
            cam.nearClipPlane = CameraNear;
            cam.farClipPlane = CameraFar;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.052f, 0.06f, 1f);
            cam.allowHDR = true;

            // **叠加玩法层**：`Main.unity` 现在是**正式游戏入口**（出包入口 / build list 都指向它）⇒
            // 本生成器（基础视觉：桌 / 地面 / 相机 / 光）必须**继续产出完整 Main**，否则会把玩法层"洗掉"。
            // 接线走 `GreyboxSceneSetup.AttachMainGameplayLayer` 这个**唯一实现点**（与灰盒共用宿主构建，铁律 16）。
            GreyboxSceneSetup.AttachMainGameplayLayer(scene);

            EditorSceneManager.SaveScene(scene, MainScenePath);
        }

        /// <summary>给球桌网格按子网格分配材质：0 = 桌体（不透明），1 = 袋网（Alpha Clipping）。</summary>
        // ── Prefab ─────────────────────────────────────────────────────────

        private static void BuildPrefabs()
        {
            EnsureDir(BallsPrefabDir);
            EnsureDir("Assets/_Project/Prefabs/Cue");

            // 球杆（不加任何补偿旋转——ADR 禁止在 PoolGeometry 之外做轴变换；其局部轴向由校验器实测记录）
            CreateModelPrefab(CueModelPath, CueMaterialPath, "PoolCue", CuePrefabPath, BilliardsLayers.Cue);

            // 基座球 Prefab（默认材质的模板）
            Material baseBallMat = AssetDatabase.LoadAssetAtPath<Material>($"{BallsMaterialDir}/{BallName(0)}.mat");
            CreateModelPrefab($"{BallsModelDir}/{BallName(0)}.fbx", null, "BallPrefab", BaseBallPrefabPath,
                BilliardsLayers.Ball, baseBallMat);

            // 16 个球 Prefab（各自材质）
            for (int i = 0; i < BallCount; i++)
            {
                string name = BallName(i);
                CreateModelPrefab(
                    $"{BallsModelDir}/{name}.fbx",
                    $"{BallsMaterialDir}/{name}.mat",
                    name,
                    $"{BallsPrefabDir}/{name}.prefab",
                    BilliardsLayers.Ball);
            }
        }

        private static void CreateModelPrefab(string modelPath, string materialPath, string rootName, string prefabPath,
            int layer, Material explicitMaterial = null)
        {
            GameObject model = TableVisualBuilder.InstantiateModel(modelPath, rootName + "_Model");
            var root = new GameObject(rootName);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            TableVisualBuilder.SetLayerRecursive(root, layer);

            Material mat = explicitMaterial;
            if (mat == null && !string.IsNullOrEmpty(materialPath))
            {
                mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            }

            if (mat != null)
            {
                foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = new Material[mr.sharedMaterials.Length > 0 ? mr.sharedMaterials.Length : 1];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = mat;
                    }

                    mr.sharedMaterials = mats;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        // ── helpers ────────────────────────────────────────────────────────

        public static string BallName(int i)
        {
            return "Ball_" + i.ToString("00");
        }

        private static Material LoadOrCreate(string path)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(UrpLit());
                AssetDatabase.CreateAsset(m, path);
            }

            return m;
        }

        private static Texture LoadTexture(string path, string what)
        {
            Texture t = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (t == null)
            {
                throw new InvalidOperationException($"无法加载贴图（{what}）：{path}");
            }

            return t;
        }

        private static void EnsureDir(string assetDir)
        {
            string full = Path.Combine(SetupReport.ProjectRoot, assetDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(full);
        }
    }
}
