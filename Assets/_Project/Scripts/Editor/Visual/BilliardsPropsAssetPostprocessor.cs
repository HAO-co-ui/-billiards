using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 球与球杆的导入设置即代码。
    ///
    /// 为什么不复用球桌的 <see cref="BilliardsAssetPostprocessor"/>：那个类按**精确路径**只处理球桌，
    /// 且它已是复核后的冻结件。新增道具单独成类，边界清晰、互不干扰（两个后处理器都按路径前缀守卫）。
    ///
    /// 关键决策与球桌同源：
    ///  · `useFileScale=false + globalScale=1` → 顶点按米直接用。实测球 FBX 顶点包围盒正是 ±0.028575 m
    ///    （== `PoolConstants.BallRadius`），即文件内已是米制，禁用文件缩放可杜绝 100×/0.01× 的尺度事故。
    ///  · `materialImportMode=None` → 不自动建材质，由材质视觉方案统一手建 URP Lit（球各自一张 BaseColor）。
    ///  · `isReadable=true` → 供校验器逐顶点核对球半径与球杆杆尖/杆向（材质视觉验收需要）。
    /// </summary>
    public class BilliardsPropsAssetPostprocessor : AssetPostprocessor
    {
        public const string BallsModelDir = "Assets/_Project/Art/Models/Balls/";
        public const string CueModelPath = "Assets/_Project/Art/Models/Cue/PoolCue_TipOrigin.fbx";
        public const string BallsTextureDir = "Assets/_Project/Art/Textures/Balls/";
        public const string CueTexturePath = "Assets/_Project/Art/Textures/Cue/pool_cue_3d_model_basecolor.jpg";

        private void OnPreprocessModel()
        {
            bool isBall = assetPath.StartsWith(BallsModelDir) && assetPath.EndsWith(".fbx");
            bool isCue = assetPath == CueModelPath;
            if (!isBall && !isCue)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            importer.useFileScale = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = false;   // 轴向转换只允许在 PoolGeometry 一处

            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importConstraints = false;

            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.None;
        }

        private void OnPreprocessTexture()
        {
            bool isBallTex = assetPath.StartsWith(BallsTextureDir);
            bool isCueTex = assetPath == CueTexturePath;
            if (!isBallTex && !isCueTex)
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;                 // 颜色贴图
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
