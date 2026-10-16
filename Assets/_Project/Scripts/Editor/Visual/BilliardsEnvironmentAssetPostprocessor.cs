using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 「球房环境」美术资产的导入设置即代码。
    ///
    /// 为什么不复用 <see cref="BilliardsPropsAssetPostprocessor"/>：那一个按**精确路径**只处理球与球杆；
    /// 本类按目录前缀处理 `Art/Models/Environment/` 与 `Art/Textures/Environment/`，边界清晰。
    /// （与球桌 / 球 / 球杆的既有分工一致：一个资产族一个后处理器。）
    ///
    /// ── 关键设置与依据（**实测**，不是照文档抄）────────────────────────────
    /// 用离线 FBX 二进制探针（`Docs/Tools/fbx_mesh_probe.py` 的解析器）直读源 FBX 得到的事实：
    ///  · 全部环境 FBX（含球桌）`GlobalSettings` 一致：`UpAxis=Y / UpAxisSign=1`、`UnitScaleFactor=100`；
    ///    且每个 `Model` 节点都带 `Lcl Rotation = (-90.000009°, 0, 0)`。
    ///    ⇒ 该**节点旋转**就是把源顶点（Z-up、底面在 Z=0）扳成 Y-up 立姿的那一步，
    ///      与球桌 FBX 同源（球桌 `SCN-04` 实测 rootRot=(270.02,0,0) 即同一个 −90°X）。
    ///    ⇒ 因此**不得**再叠加任何手动旋转，也不得烘焙轴转换。
    ///  · 源顶点数值已是**米制**（例：Bench 局部包围盒 1.60×0.60×0.531 m，与 asset-metadata 的
    ///    `dimensions_runtime_xyz` 逐项吻合）⇒ `useFileScale=false + globalScale=1`（与球桌/球/杆同一口径）。
    ///  · 每个道具 `Model` 节点的平移**等于**其在参考房间里的**底面中心**（例：Bench 节点 T=(0.5,−0.78,−1.9)
    ///    == asset-metadata 的 `bottom_center_runtime`）⇒ 导入后自带"房间坐标摆放"。
    ///    但本工程**不采用**该自带平移（`RoomEnvironmentSetup` 按自己的摆位表重新落位），故 `isReadable=true` 以便
    ///    构建期实测包围盒、并供"地板/墙裙"类网格做 UV/合并处理。
    /// </summary>
    public class BilliardsEnvironmentAssetPostprocessor : AssetPostprocessor
    {
        public const string EnvModelDir = "Assets/_Project/Art/Models/Environment/";
        public const string EnvTextureDir = "Assets/_Project/Art/Textures/Environment/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(EnvModelDir) || !assetPath.EndsWith(".fbx"))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            // 单位：源顶点已是米制。与球桌/球/球杆同口径（1 Unity 单位 = 1 米）。
            importer.useFileScale = false;
            importer.globalScale = 1f;

            // 轴向：**不烘焙**。立姿由源 FBX 的 Model 节点旋转（−90°X）承担，与球桌一致。
            importer.bakeAxisConversion = false;

            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importConstraints = false;

            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;                    // 构建期实测包围盒 + 墙面网格合并都要求可读
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;

            // 不自动建材质：材质由 `RoomEnvironmentSetup` 统一手建 URP Lit（含"图集/同族复用"）。
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            importer.animationType = ModelImporterAnimationType.None;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(EnvTextureDir))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;                   // 全部是 BaseColor（颜色数据）
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;    // 地板贴图需要平铺
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
