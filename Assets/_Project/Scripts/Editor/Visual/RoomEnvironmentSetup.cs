using System;
using System.Collections.Generic;
using Billiards.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **球房环境（房间布景）的唯一构建点** —— 把美术包的真 FBX（不是图元）摆到球桌周围。
    ///
    /// 落点 = `Room`（由 <see cref="TableVisualBuilder.AttachRoom"/> 创建、挂在 `PlayfieldRoot` 缩放根之下）。
    /// 因此本类**不新建地面**、**不移动 `Room`**：`SCN-07`（地面世界 y == 桌子 AABB min.y）与
    /// `SCN-01`（`PlayfieldRoot`/`PoolTable`/`Floor`/`Main Camera` 四者齐备）的语义都不受影响。
    ///
    /// ── 摆位真源（**机器可读**，非看文档猜）──────────────────────────────
    /// `F:\工作文件\idea35_台球\01_Scenes_场景\Runtime_Reference\asset-metadata.json` 的
    /// `room.placements`（37 条：24 块地板 + 13 件道具），每条含 `bottom_center_runtime` 与
    /// `dimensions_runtime_xyz`。本类取其 13 件道具的**底面中心**作为摆位表。
    /// 该 JSON 同时声明 `runtime_coordinates = { x: 桌长轴, y: 上, z: 桌短轴, floor_y: -0.78 }`。
    ///
    /// ── 一个必须处置的口径差（**不是笔误**）──────────────────────────────
    /// 参考房间地面 `floor_y = -0.78`，而**本工程球桌的桌腿底** `PoolConstants.TableLegBottomY = -0.8635167…`。
    /// 若照 −0.78 摆，13 件道具会整体**浮空 8.35 cm**。⇒ 对**整组环境**（墙 + 全部道具）统一施加
    /// <see cref="FloorOffsetY"/> = `TableLegBottomY − (−0.78)`，使参考房间的地面线对齐本工程地板。
    /// 这是**整组同移**，不改变任何相对关系。
    ///
    /// ── 姿态（**实测口径**，见后处理器注释）──────────────────────────────
    /// 环境 FBX 的源顶点是 Z-up、底面在 Z=0，立姿由源文件 `Model` 节点的 `Lcl Rotation=(−90°,0,0)` 承担
    /// （与球桌 FBX 同源）⇒ **本类不叠加任何手动旋转**。落地姿态用**实测包围盒**复核（`Debug.Log`）。
    ///
    /// ── 预算（`SmokeProbe.MaxRendererCount=60` / `MaxDistinctMaterialCount=30`）────────
    /// 材质**按"能不能被看见"分级**（不是一刀切共享）：
    ///  · **独立材质**（参考图里清晰可见、必须各自像）：墙 / 地毯 / 盆栽 / 长凳 / 墙裙 / 边桌 / 相框 = **7**；
    ///  · **共享材质**（次要或小件）：`Room_WoodSmall` =球杆架+三角架+粉笔、`Room_Metal` =落地灯+两壁灯 = **2**；
    ///  · 地面**复用既有** `Room_Floor.mat`（只换贴图，不新增）⇒ **新增材质 = 9**（21+9 = 30，压线）。
    /// 渲染器：墙 `Reference_Existing_Walls.fbx`（17 个 Wall_Box 子网格）`CombineMeshes` 合成 **1**，
    /// 12 件道具各 1 ⇒ **新增渲染器 = 13**（实测峰值 55/60）。
    ///
    /// ── 两处**对权威摆位的偏离**（都登记了理由，见对应常量注释）──────────────────
    ///  ① 整组下移 `FloorOffsetY`（参考房间地面 −0.78 → 本工程桌腿底 −0.8635）；
    ///  ② `Bench` 的 z：−1.90 → −1.85（消掉与墙裙板 5.2 cm 的互穿）。
    /// 另：`Ceiling_Light.fbx` **有意未纳入**（美术包无天花板 ⇒ 无处可挂）。
    /// </summary>
    public static class RoomEnvironmentSetup
    {
        public const string EnvModelDir = BilliardsEnvironmentAssetPostprocessor.EnvModelDir;      // 带尾斜杠
        public const string EnvTexDir = BilliardsEnvironmentAssetPostprocessor.EnvTextureDir;      // 带尾斜杠
        public const string EnvMaterialDir = "Assets/_Project/Materials/Room";

        public const string WallsModelPath = EnvModelDir + "Room_Walls.fbx";
        public const string WallsCombinedMeshPath = EnvModelDir + "Room_Walls_Combined.asset";
        public const string FloorPlanksTexPath = EnvTexDir + "room_floor_planks.png";

        // ── 材质资产（**按"能不能被看见"分级**，见类注释的预算口径）──────────────────
        //   独立材质 = 参考图里清晰可见、必须各自像：墙 / 地毯 / 盆栽 / 长凳 / 墙裙 / 边桌 / 相框；
        //   共享材质 = 次要或小件：球杆架 + 三角架 + 粉笔（同木色）、落地灯 + 两壁灯（同金属）。
        //   地面**复用既有** `Room_Floor.mat`（只换贴图）⇒ 新增 **9** 个。
        public const string WallsMatPath = EnvMaterialDir + "/Room_Walls.mat";
        public const string RugMatPath = EnvMaterialDir + "/Room_Rug.mat";
        public const string PlantMatPath = EnvMaterialDir + "/Room_Plant.mat";
        public const string BenchMatPath = EnvMaterialDir + "/Room_Bench.mat";
        public const string WallTrimMatPath = EnvMaterialDir + "/Room_WallTrim.mat";
        public const string SideTableMatPath = EnvMaterialDir + "/Room_SideTable.mat";
        public const string PaintingMatPath = EnvMaterialDir + "/Room_Painting.mat";
        public const string WoodSmallMatPath = EnvMaterialDir + "/Room_WoodSmall.mat";
        public const string MetalMatPath = EnvMaterialDir + "/Room_Metal.mat";

        /// <summary>已废弃的旧材质（初版"一刀切共享"留下的），构建时清掉，避免留孤儿资产。</summary>
        private static readonly string[] ObsoleteMaterialPaths =
        {
            EnvMaterialDir + "/Room_Wood.mat",
            EnvMaterialDir + "/Room_Brass.mat",
            EnvMaterialDir + "/Room_Chalk.mat",
        };

        /// <summary>参考房间地面高度（`asset-metadata.runtime_coordinates.floor_y`）。</summary>
        public const float ReferenceFloorY = -0.78f;

        /// <summary>地板贴图在世界空间的重复周期（米/次）：贴图 1024² 视为 1.5 m 见方。</summary>
        public const float FloorTileMetres = 1.5f;

        /// <summary>参考房间地面 → 本工程地面（桌腿底）的**整组**下移量（见类注释）。</summary>
        public static float FloorOffsetY
        {
            get { return PoolConstants.TableLegBottomY - ReferenceFloorY; }
        }

        private struct PropSpec
        {
            public readonly string Name;
            public readonly string FbxFile;
            public readonly Vector3 BottomCenter;   // 参考房间坐标（Y-up）
            public readonly Vector3 ExpectedSize;   // 参考房间坐标下的包围盒尺寸（== asset-metadata.dimensions_runtime_xyz）
            public readonly string MaterialPath;

            public PropSpec(string name, string fbx, Vector3 bottomCenter, Vector3 expectedSize, string materialPath)
            {
                Name = name;
                FbxFile = fbx;
                BottomCenter = bottomCenter;
                ExpectedSize = expectedSize;
                MaterialPath = materialPath;
            }
        }

        /// <summary>
        /// 13 件道具的**权威摆位 + 权威尺寸**（逐条来自 `asset-metadata.json` 的 `bottom_center_runtime`
        /// 与 `dimensions_runtime_xyz`，原样抄录）。命名按可读性重排（去掉原 `source_*` 前缀）。
        ///
        /// `ExpectedSize` 不是装饰：它是**朝向的客观判据**——若某件被摆成"躺倒/贴反"，
        /// 实测包围盒的 Y（高度）会与这里对不上（例如长凳躺倒时 Y 会变成 0.6 而不是 0.53）。
        /// </summary>
        private static readonly PropSpec[] Props =
        {
            new PropSpec("Rug",          "Rug.fbx",           new Vector3( 0.00f, -0.779f,  0.00f), new Vector3(3.650f, 0.025f, 2.650f), RugMatPath),
            // ⚠ **对权威摆位的偏离（唯一一处，登记理由）**：z 由 `asset-metadata` 的 −1.90 改为 **−1.85**。
            //    原因：原值使长凳背面与墙上墙裙板在 z 向互穿 **5.2 cm**（离线 AABB 实测）。互穿是缺陷类别，
            //    不该因"参考数据自带"而放行；5 cm 的水平位移在观感上不可见，但"长凳嵌进墙裙"看得见。
            new PropSpec("Bench",        "Bench.fbx",         new Vector3( 0.50f, -0.780f, -1.85f), new Vector3(1.600f, 0.530f, 0.600f), BenchMatPath),
            new PropSpec("CueRack",      "Cue_Rack.fbx",      new Vector3(-2.20f, -0.780f, -2.02f), new Vector3(0.880f, 1.520f, 0.270f), WoodSmallMatPath),
            new PropSpec("PottedPlant",  "Potted_Plant.fbx",  new Vector3(-1.47f, -0.780f, -1.77f), new Vector3(0.730f, 1.500f, 0.800f), PlantMatPath),
            new PropSpec("SideTable",    "Side_Table.fbx",    new Vector3( 2.18f, -0.780f, -1.53f), new Vector3(0.800f, 0.720f, 0.800f), SideTableMatPath),
            new PropSpec("RackTriangle", "Rack_Triangle.fbx", new Vector3( 2.18f, -0.054f, -1.53f), new Vector3(0.430f, 0.053f, 0.430f), WoodSmallMatPath),
            new PropSpec("Chalk",        "Chalk.fbx",         new Vector3( 1.94f, -0.047f, -1.54f), new Vector3(0.063f, 0.063f, 0.063f), WoodSmallMatPath),
            new PropSpec("FloorLamp",    "Floor_Lamp.fbx",    new Vector3( 2.65f, -0.780f, -2.05f), new Vector3(0.360f, 1.520f, 0.360f), MetalMatPath),
            new PropSpec("WallLampLeft", "Wall_Lamp.fbx",     new Vector3(-1.02f,  0.560f, -2.05f), new Vector3(0.430f, 0.530f, 0.360f), MetalMatPath),
            new PropSpec("WallLampRight","Wall_Lamp.fbx",     new Vector3( 1.84f,  0.560f, -2.05f), new Vector3(0.430f, 0.530f, 0.360f), MetalMatPath),
            new PropSpec("Painting",     "Painting.fbx",      new Vector3( 0.54f,  0.390f, -2.19f), new Vector3(0.890f, 0.760f, 0.102f), PaintingMatPath),
            new PropSpec("WallTrim",     "Wall_Trim.fbx",     new Vector3(-0.03f, -0.690f, -2.20f), new Vector3(1.220f, 0.720f, 0.104f), WallTrimMatPath),
            // ⚠ **`Ceiling_Light.fbx` 有意未纳入（登记理由）**：美术包**没有天花板网格**（17 块墙里无顶），
            //    该件底面在整个房间的上沿、顶面高出墙顶 0.70 m ⇒ 它**物理上无处可挂**，摆出来是"悬在开口上方的灯"。
            //    另实测它在**打法取景**里整件落在画外（13 件里唯一一件"框外 + 无法解释"的），故去掉以免留一个说不通的悬浮物。
            //    ⇒ 将来若补天花板/顶棚，把它（`Ceiling_Light.fbx` 已在库内）按 `asset-metadata` 的
            //       bottom_center (0, 1.42, −1.58) 加回即可。
        };

        // ── 入口 ───────────────────────────────────────────────────────────

        /// <summary>把墙 + 13 件道具挂到 <paramref name="room"/> 之下（由 `TableVisualBuilder.AttachRoom` 调用）。</summary>
        public static void Attach(Transform room)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            Vector3 offset = new Vector3(0f, FloorOffsetY, 0f);

            BuildWalls(room, offset);

            for (int i = 0; i < Props.Length; i++)
            {
                PropSpec spec = Props[i];
                GameObject go = TableVisualBuilder.InstantiateModel(EnvModelDir + spec.FbxFile, spec.Name);
                go.transform.SetParent(room, false);     // worldPositionStays=false ⇒ 保留导入姿态（含 −90°X 立姿）

                TableVisualBuilder.SetLayerRecursive(go, BilliardsLayers.Environment);
                StripPhysics(go);
                AssignMaterial(go, spec.MaterialPath);
                SetShadowCasting(go, ShadowCastingMode.On);
                PlaceBottomCenter(go, room, spec.BottomCenter + offset);

                LogPlacement(spec.Name, room, spec.BottomCenter + offset, spec.ExpectedSize);
            }
        }

        /// <summary>构建全部环境材质（含"地面换成可平铺木板"）。由 <see cref="MaterialVisualSetup.BuildMaterials"/> 调用。</summary>
        public static void BuildMaterials()
        {
            EnsureDir(EnvMaterialDir);

            // 初版"一刀切共享"留下的孤儿材质（边桌/墙裙被并进 Room_Wood、三盏灯并进 Room_Brass、粉笔单列）：
            // 清掉，免留孤儿资产（口径变更后它们不再被任何渲染器引用）。
            for (int i = 0; i < ObsoleteMaterialPaths.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<Material>(ObsoleteMaterialPaths[i]) != null)
                {
                    AssetDatabase.DeleteAsset(ObsoleteMaterialPaths[i]);
                }
            }

            BuildWallsMaterial();

            // **独立材质**：参考图里清晰可见、必须各自像。
            BuildTexturedMaterial(RugMatPath, "Room_Rug", EnvTexDir + "ornate_rug_3d_model_basecolor.jpg", 0.10f);
            BuildTexturedMaterial(PlantMatPath, "Room_Plant", EnvTexDir + "potted_plant_3d_model_basecolor.jpg", 0.25f);
            BuildTexturedMaterial(BenchMatPath, "Room_Bench", EnvTexDir + "tufted_leather_ottoman_3d_model_basecolor.jpg", 0.45f);
            BuildTexturedMaterial(SideTableMatPath, "Room_SideTable", EnvTexDir + "wooden_round_table_3d_model_basecolor.jpg", 0.30f);
            BuildTexturedMaterial(WallTrimMatPath, "Room_WallTrim", EnvTexDir + "wooden_chalkboard_3d_model_basecolor.jpg", 0.30f);
            BuildTexturedMaterial(PaintingMatPath, "Room_Painting", EnvTexDir + "framed_landscape_painting_3d_model_basecolor.jpg", 0.35f);

            // **共享材质**：次要 / 小件 / 参考构图里不承担识别度。
            BuildTexturedMaterial(WoodSmallMatPath, "Room_WoodSmall",
                EnvTexDir + "wooden_triangle_frame_3d_model_basecolor.jpg", 0.30f);   // 球杆架 + 三角架 + 粉笔
            BuildTexturedMaterial(MetalMatPath, "Room_Metal",
                EnvTexDir + "brass_wall_lamp_3d_model_basecolor.jpg", 0.55f);         // 落地灯 + 两壁灯
        }

        /// <summary>把既有地面材质 `Room_Floor.mat` 改成**可平铺的木板**（复用材质资产 ⇒ 不新增材质）。</summary>
        public static void ConfigureFloorMaterial(Material m)
        {
            if (m == null)
            {
                throw new ArgumentNullException(nameof(m));
            }

            Texture planks = AssetDatabase.LoadAssetAtPath<Texture>(FloorPlanksTexPath);
            if (planks == null)
            {
                throw new InvalidOperationException("找不到地板贴图：" + FloorPlanksTexPath);
            }

            // 地面渲染器是一个 30×30 的 Plane（UV 0..1）⇒ 靠材质平铺把贴图铺满。
            // 物理平面尺寸 30 m（= 10 × localScale 3）× 视觉缩放 1.2 ⇒ 世界 36 m；按 1.5 m/次 ⇒ 24 次。
            const float worldPlaneMetres = 30f * PoolConstants.PlayfieldVisualScale;
            float tiling = worldPlaneMetres / FloorTileMetres;
            m.SetTexture("_BaseMap", planks);
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
        }

        /// <summary>批处理入口：只重建**环境材质 + 地面材质**（不重写桌/球/杆材质，避免 git 噪声）。</summary>
        public static void BuildMaterialsFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                AssetDatabase.Refresh();

                // ⚠ **强制重导一次**：这批 FBX/贴图是本轮新拷进 `Assets/` 的，可能在后处理器
                //    (`BilliardsEnvironmentAssetPostprocessor`) 编译生效**之前**就被扫过一遍，
                //    那样 meta 里就没有 `useFileScale=false / isReadable=true / materialImportMode=None`。
                //    不重导 ⇒ 顶点单位、可读性、材质自动生成全都不对，而且**不会报错**。
                AssetDatabase.ImportAsset(EnvModelDir, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(EnvTexDir, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();

                BuildMaterials();

                Material floor = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.FloorMaterialPath);
                if (floor == null)
                {
                    throw new InvalidOperationException("找不到地面材质：" + MaterialVisualSetup.FloorMaterialPath
                        + "（请先跑视觉基础构建）");
                }

                ConfigureFloorMaterial(floor);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("ROOM_ENV_MATERIALS_OK dir=" + EnvMaterialDir);
            }
            catch (Exception ex)
            {
                Debug.LogError("环境材质构建失败：" + ex);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // ── 房间尺度预览渲染（**只写仓库外**，不碰 `Docs/Reports/` 的入库证据）──────────

        /// <summary>
        /// 从**房间尺度**取景渲染一张预览图（复用场景里的主相机、渲染完**原样还原位姿**，
        /// ⇒ **不往场景里加第二台相机**，也不违反"恰 1 台相机"的约束）。
        ///
        /// 为什么需要：玩法取景被 `CAM-01..05` 钉死在桌上（俯角 68.7°、离桌 2.25 m），
        /// 画面只覆盖桌远库外约 0.6 m ⇒ 球房环境**整片落在画外**。要"看得见房间"必须换一台更远更高的取景，
        /// 而那属于设计变更（需重定 `CAM-04`）。本入口把这件事与"改玩法取景"解耦：
        /// **只渲染、不改场景**，用于(a)证明房间确实建对；(b)预演候选取景的效果。
        ///
        /// 环境变量：
        ///   · `BILLIARDS_ROOM_PREVIEW_OUT`  输出 PNG（默认 `%TEMP%/billiards_batch/room-preview.png`）
        ///   · `BILLIARDS_ROOM_PREVIEW_CAM`  `game`（主相机原样 = 场景序列化态，即 **play 打球视角**）
        ///     | `home`（**首页观景预设**，取自真源 `CameraPresets.Home`；旧别名 `room` 亦可）| `custom`
        ///   · `custom` 时另需 `..._POS` / `..._LOOK` / `..._FOV`，格式 `x,y,z`
        /// </summary>
        public static void RenderRoomPreviewFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                string outPath = Environment.GetEnvironmentVariable("BILLIARDS_ROOM_PREVIEW_OUT");
                if (string.IsNullOrEmpty(outPath))
                {
                    outPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "billiards_batch", "room-preview.png");
                }

                string mode = Environment.GetEnvironmentVariable("BILLIARDS_ROOM_PREVIEW_CAM");
                if (string.IsNullOrEmpty(mode))
                {
                    mode = "room";
                }

                Scene scene = EditorSceneManager.OpenScene(MaterialVisualSetup.MainScenePath, OpenSceneMode.Single);
                GameObject camGo = FindRoot(scene, "Main Camera");
                if (camGo == null)
                {
                    throw new InvalidOperationException("场景里找不到 Main Camera：" + MaterialVisualSetup.MainScenePath);
                }

                Camera cam = camGo.GetComponent<Camera>();
                Vector3 prevPos = camGo.transform.position;
                Quaternion prevRot = camGo.transform.rotation;
                float prevFov = cam.fieldOfView;
                var fitter0 = camGo.GetComponent<Billiards.Physics.CameraViewportFitter>();
                float prevBaseFov = fitter0 != null ? fitter0.baseVerticalFovDeg : prevFov;

                if (string.Equals(mode, "home", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "room", StringComparison.OrdinalIgnoreCase))
                {
                    // ⚠ 用**真源** `CameraPresets.Home`（而不是在这里硬编码坐标）——
                    //   否则"首页观景"会有两份数：运行期一套、渲染工具一套，迟早漂移。
                    CameraPresets.ApplyTo(cam, CameraPresets.Preset.Home);
                }
                else if (string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase))
                {
                    Vector3 p = ParseVec(Environment.GetEnvironmentVariable("BILLIARDS_ROOM_PREVIEW_POS"));
                    Vector3 l = ParseVec(Environment.GetEnvironmentVariable("BILLIARDS_ROOM_PREVIEW_LOOK"));
                    camGo.transform.position = p;
                    camGo.transform.rotation = Quaternion.LookRotation(l - p, Vector3.up);
                    float fov = 50f;
                    float.TryParse(Environment.GetEnvironmentVariable("BILLIARDS_ROOM_PREVIEW_FOV"),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fov);
                    SetBaseFov(cam, fov);
                }

                RenderToPng(cam, 1440, 900, outPath);

                camGo.transform.position = prevPos;
                camGo.transform.rotation = prevRot;
                cam.fieldOfView = prevFov;
                if (fitter0 != null)
                {
                    fitter0.baseVerticalFovDeg = prevBaseFov;
                }

                Debug.Log("ROOM_PREVIEW_OK mode=" + mode + " out=" + outPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("房间预览渲染失败：" + ex);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static Vector3 ParseVec(string csv)        {
            if (string.IsNullOrEmpty(csv))
            {
                throw new InvalidOperationException("缺少向量参数（格式 x,y,z）");
            }

            string[] parts = csv.Split(',');
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("向量参数格式错误（应为 x,y,z）：" + csv);
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(
                float.Parse(parts[0], inv),
                float.Parse(parts[1], inv),
                float.Parse(parts[2], inv));
        }

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

        /// <summary>
        /// 设定"基准垂直 fov"：**有 `CameraViewportFitter` 就改它的基准**，没有才直接写相机 fov。
        /// 为什么：适配器是**渲染期 fov 的唯一权威**（它按当前宽高比从基准解析）——
        /// 若绕过它直接写 `cam.fieldOfView`，一旦宽高比变化（本工具渲染时会设成 16:10）就会被它覆写回去。
        /// </summary>
        private static void SetBaseFov(Camera cam, float fovDeg)
        {
            var fitter = cam.GetComponent<Billiards.Physics.CameraViewportFitter>();
            if (fitter != null)
            {
                fitter.baseVerticalFovDeg = fovDeg;
                fitter.Apply();
                return;
            }

            cam.fieldOfView = fovDeg;
        }

        private static void RenderToPng(Camera cam, int w, int h, string fullPath)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            rt.Create();

            RenderTexture prev = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;
            float prevAspect = cam.aspect;

            cam.targetTexture = rt;
            cam.aspect = (float)w / h;

            // ⚠ **宽高比刚变了** ⇒ 必须让适配器按**新**比例重新解析 fov。
            //   不这么做的话，渲染用的是"按旧比例算出的 fov"（本工具实测踩过：`home` 模式渲染出的
            //   取景与真源预设不一致，且不报任何错）。
            var fitter = cam.GetComponent<Billiards.Physics.CameraViewportFitter>();
            if (fitter != null)
            {
                fitter.Apply();
            }

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

            System.IO.File.WriteAllBytes(fullPath, encoded);
            Debug.Log($"房间预览已落盘：{fullPath}（{w}×{h}，{encoded.Length} 字节）");
        }

        // ── 墙 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 把 `Room_Walls.fbx` 的 **17 个 Wall_Box 子网格合并成 1 个渲染器**（否则 +17 渲染器直接吃光预算），
        /// 合并网格存成 `.asset`（场景不能序列化运行时 `new Mesh()`）。
        /// </summary>
        private static void BuildWalls(Transform room, Vector3 offset)
        {
            GameObject src = TableVisualBuilder.InstantiateModel(WallsModelPath, "Walls_Source");
            src.transform.SetParent(room, false);

            var parts = new List<CombineInstance>();
            foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                {
                    continue;
                }

                parts.Add(new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix,
                });
            }

            if (parts.Count == 0)
            {
                UnityEngine.Object.DestroyImmediate(src);
                throw new InvalidOperationException("墙体 FBX 里没有可合并的网格：" + WallsModelPath);
            }

            Mesh combined = SaveOrUpdateCombinedMesh(WallsCombinedMeshPath, parts);

            var walls = new GameObject("Walls");
            walls.transform.SetParent(room, false);
            walls.transform.localPosition = offset;      // 与道具同一次整组下移
            walls.layer = BilliardsLayers.Environment;

            walls.AddComponent<MeshFilter>().sharedMesh = combined;
            MeshRenderer mr = walls.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LoadMaterialOrThrow(WallsMatPath);
            mr.shadowCastingMode = ShadowCastingMode.Off;   // 房间外壳：不投影（避免整片地板被墙影盖住）

            UnityEngine.Object.DestroyImmediate(src);
            Debug.Log($"[RoomEnv] Walls: 合并 {parts.Count} 个子网格 → 1 渲染器；顶点 {combined.vertexCount}");
        }

        private static Mesh SaveOrUpdateCombinedMesh(string assetPath, List<CombineInstance> parts)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Room_Walls_Combined" };
                AssetDatabase.CreateAsset(mesh, assetPath);
            }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ── 材质 ───────────────────────────────────────────────────────────

        private static void BuildWallsMaterial()
        {
            Material m = LoadOrCreateMaterial(WallsMatPath, "Room_Walls");
            m.SetColor("_BaseColor", new Color(0.135f, 0.115f, 0.103f));   // 深色墙（暖灰）
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
        }

        private static void BuildTexturedMaterial(string path, string name, string texPath, float smoothness)
        {
            Material m = LoadOrCreateMaterial(path, name);
            Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
            if (tex == null)
            {
                throw new InvalidOperationException("找不到环境贴图：" + texPath);
            }

            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
        }

        private static Material LoadOrCreateMaterial(string path, string name)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader shader = Shader.Find(MaterialVisualSetup.UrpLitShaderName);
                if (shader == null)
                {
                    throw new InvalidOperationException("找不到 URP Lit 着色器：" + MaterialVisualSetup.UrpLitShaderName);
                }

                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            m.name = name;
            if (m.shader == null || m.shader.name != MaterialVisualSetup.UrpLitShaderName)
            {
                m.shader = Shader.Find(MaterialVisualSetup.UrpLitShaderName);
            }

            m.SetShaderPassEnabled("ShadowCaster", true);
            m.SetShaderPassEnabled("DepthOnly", true);
            m.SetShaderPassEnabled("DepthNormals", true);
            return m;
        }

        private static Material LoadMaterialOrThrow(string path)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                throw new InvalidOperationException("环境材质缺失：" + path
                    + "（先跑 RoomEnvironmentSetup.BuildMaterialsFromCommandLine）");
            }

            return m;
        }

        // ── 摆放 / 处置 ────────────────────────────────────────────────────

        /// <summary>
        /// 按**实测包围盒**把物件摆到目标底面中心：先按当前层级量出世界 AABB，再把根整体平移，
        /// 使 AABB 的 `(center.x, min.y, center.z)` 落到目标点。与层级形状无关（FBX 自带平移/旋转都能吸收）。
        /// </summary>
        private static void PlaceBottomCenter(GameObject go, Transform roomSpace, Vector3 targetLocal)
        {
            if (!TryWorldBounds(go, out Bounds bounds))
            {
                Debug.LogWarning($"[RoomEnv] {go.name}: 没有可测的渲染器，跳过落位。");
                return;
            }

            Vector3 want = roomSpace.TransformPoint(targetLocal);
            Vector3 delta = new Vector3(want.x - bounds.center.x, want.y - bounds.min.y, want.z - bounds.center.z);
            go.transform.position += delta;
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            bounds = new Bounds();
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderers[i].bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return any;
        }

        /// <summary>
        /// 逐件打印**实测落位与实测尺寸**（朝向的客观判据）：尺寸按"世界尺寸 ÷ 父链缩放"折算回参考房间尺度，
        /// 与 `asset-metadata.dimensions_runtime_xyz` 逐轴比对。任一轴对不上 ⇒ 该件**姿态不对**
        /// （躺倒 / 贴反 / 缩放错），而不是"看起来还行"。
        /// </summary>
        private static void LogPlacement(string name, Transform roomSpace, Vector3 targetLocal, Vector3 expectedSize)
        {
            Transform child = roomSpace.Find(name);
            if (child == null || !TryWorldBounds(child.gameObject, out Bounds b))
            {
                return;
            }

            Vector3 wantWorld = roomSpace.TransformPoint(targetLocal);
            Vector3 measured = new Vector3(b.center.x, b.min.y, b.center.z);

            Vector3 scale = roomSpace.lossyScale;
            Vector3 canonSize = new Vector3(
                b.size.x / Mathf.Max(1e-6f, scale.x),
                b.size.y / Mathf.Max(1e-6f, scale.y),
                b.size.z / Mathf.Max(1e-6f, scale.z));
            Vector3 sizeErr = canonSize - expectedSize;

            Debug.Log($"[RoomEnv] {name}: Δ落位={V(measured - wantWorld)} " +
                      $"实测尺寸(规范)={V(canonSize)} 期望={V(expectedSize)} Δ尺寸={V(sizeErr)}");
        }

        private static string V(Vector3 v)
        {
            return $"({v.x:F3},{v.y:F3},{v.z:F3})";
        }

        private static void StripPhysics(GameObject go)
        {
            // 自研物理铁律（`SCN-06`：Scenes + Prefabs 全量 Rigidbody == 0 且 Collider == 0）。
            // 美术模型常见自带 MeshCollider/BoxCollider ⇒ 逐件剥掉（照抄 AttachRoom 对地面的做法）。
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(rb);
            }
        }

        private static void AssignMaterial(GameObject go, string materialPath)
        {
            Material mat = LoadMaterialOrThrow(materialPath);
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                int count = (mf != null && mf.sharedMesh != null)
                    ? Mathf.Max(1, mf.sharedMesh.subMeshCount)
                    : 1;
                var mats = new Material[count];
                for (int i = 0; i < count; i++)
                {
                    mats[i] = mat;
                }

                mr.sharedMaterials = mats;
            }
        }

        private static void SetShadowCasting(GameObject go, ShadowCastingMode mode)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = mode;
            }
        }

        private static void EnsureDir(string assetDir)
        {
            string full = System.IO.Path.Combine(SetupReport.ProjectRoot, assetDir.Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(full);
        }
    }
}
