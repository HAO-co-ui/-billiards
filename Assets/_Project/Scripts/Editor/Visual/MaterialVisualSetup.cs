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
    ///  · Main.unity：PlayfieldRoot（scale=1）→ 球桌；Room→Floor；主平行光；俯视带俯角相机
    ///
    /// 硬约束遵守：
    ///  · 唯一坐标转换点 = <see cref="PoolGeometry"/>，本类**不做任何轴/手性/缩放转换**；
    ///    球桌模型不再叠加手动旋转（模型侧修正已由导入与 PoolGeometry 承担）。
    ///  · `PlayfieldVisualScale`（1.2）**本类不应用**，仅预留 scale=1 的 PlayfieldRoot 供后续视觉缩放层挂载；
    ///    应用时只能作用于该视觉 root，物理保持规范米制。
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

        // 相机与灯光参数（MaterialVisualValidator 会读回核对）
        public static readonly Vector3 CameraPosition = new Vector3(0f, 3.05f, -0.88f);
        public static readonly Vector3 CameraTarget = new Vector3(0f, 0f, 0f);
        public const float CameraFov = 37f;
        public const float CameraNear = 0.05f;
        public const float CameraFar = 60f;

        public static readonly Vector3 SunEuler = new Vector3(52f, -34f, 0f);
        public static readonly Color SunColor = new Color(1f, 0.957f, 0.902f);
        public const float SunIntensity = 1.40f;

        // ── 入口 ───────────────────────────────────────────────────────────

        [MenuItem("Billiards/Visual/Build Day-3 Materials, Scene & Prefabs", false, 400)]
        public static void ApplyInteractive()
        {
            Apply();
            Debug.Log("Day-3 视觉基础构建完成。");
            EditorUtility.DisplayDialog("Billiards Day-3 Setup",
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

        private static void BuildFloorMaterial()
        {
            Material m = LoadOrCreate(FloorMaterialPath);
            Configure(m, "Room_Floor");
            m.SetColor("_BaseColor", new Color(0.115f, 0.098f, 0.082f));
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
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
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.55f);
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

            // 1) 视觉根（scale=1 预留；后续才把 1.2 挂到这里，且只影响视觉）
            var playfield = new GameObject("PlayfieldRoot");
            playfield.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            playfield.transform.localScale = Vector3.one;

            GameObject tableInstance = InstantiateModel(TableModelPath, "PoolTable");
            tableInstance.transform.SetParent(playfield.transform, false);
            SetLayerRecursive(tableInstance, BilliardsLayers.Table);
            AssignTableMaterials(tableInstance);

            // 2) 最小布景：地面（与桌腿底对齐）
            var room = new GameObject("Room");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            Collider floorCollider = floor.GetComponent<Collider>();
            if (floorCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(floorCollider);   // 自研物理：场景不放碰撞体
            }

            floor.transform.SetParent(room.transform, false);
            floor.transform.position = new Vector3(0f, PoolConstants.TableLegBottomY, 0f);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);      // 默认平面 10×10 → 30×30 m
            SetLayerRecursive(floor, BilliardsLayers.Environment);
            var floorRenderer = floor.GetComponent<MeshRenderer>();
            if (floorRenderer != null)
            {
                floorRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
                floorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }

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

            // 4) 相机（俯视带俯角）
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            camGo.transform.position = CameraPosition;
            camGo.transform.rotation = Quaternion.LookRotation(CameraTarget - CameraPosition, Vector3.up);
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = CameraNear;
            cam.farClipPlane = CameraFar;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.052f, 0.06f, 1f);
            cam.allowHDR = true;

            EditorSceneManager.SaveScene(scene, MainScenePath);
        }

        /// <summary>给球桌网格按子网格分配材质：0 = 桌体（不透明），1 = 袋网（Alpha Clipping）。</summary>
        private static void AssignTableMaterials(GameObject root)
        {
            Material body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
            Material nets = AssetDatabase.LoadAssetAtPath<Material>(NetsMaterialPath);

            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                int subMeshes = (mf != null && mf.sharedMesh != null) ? Mathf.Max(1, mf.sharedMesh.subMeshCount) : 1;
                var mats = new Material[subMeshes];
                for (int i = 0; i < subMeshes; i++)
                {
                    mats[i] = (i == 1) ? nets : body;
                }

                mr.sharedMaterials = mats;
            }
        }

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
            GameObject model = InstantiateModel(modelPath, rootName + "_Model");
            var root = new GameObject(rootName);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            SetLayerRecursive(root, layer);

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

        private static GameObject InstantiateModel(string modelPath, string name)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                throw new InvalidOperationException("无法加载模型：" + modelPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = name;
            return instance;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
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
