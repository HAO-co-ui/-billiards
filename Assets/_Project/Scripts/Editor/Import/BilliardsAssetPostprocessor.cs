using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 导入设置即代码 —— 与 Day 1 同一哲学（配置即代码、可复现、可评审、可 diff）。
    ///
    /// 为什么不用 Inspector 手点：手点的导入设置无法评审、无法回滚、新机器 clone 后丢失，
    /// 且"某个贴图忘了改 sRGB"这类缺陷不会编译失败、只会在渲染时静默出错。
    /// 这里把球桌模型与 5 张贴图的每一项导入设置都固化。
    ///
    /// 关键决策（详见 Day 2 报告）：
    ///  · 模型 useFileScale=false + globalScale=1 → 顶点按米直接用，杜绝二次缩放。
    ///    源 FBX 顶点已是米制（bbox 长 2.825 m），由 Validator 断言。
    ///  · 模型不自动建材质（materialImportMode=None）：Day 3 按通道方案（Metallic R + Smoothness A）
    ///    手工建 URP Lit 材质，避免生成半错材质误导。
    ///  · 法线贴图**不翻转绿通道**：依据 Unity 官方文档（绿色通道 Y+ = Up，即 OpenGL 约定）
    ///    与 Blender 默认导出（OpenGL 约定）一致；实测该贴图绿通道均值 125.7 ≈ 中性 128，自洽。
    ///  · Metallic/Roughness 是分离的单通道灰度图，URP Lit 需要打包过的
    ///    Metallic(R)+Smoothness(A)；由 TableTextureChannelPacker 生成，属 Day 3 风险 R2 的提前处置。
    /// </summary>
    public class BilliardsAssetPostprocessor : AssetPostprocessor
    {
        public const string TableModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";

        public const string BaseColorPath = "Assets/_Project/Art/Textures/table_basecolor.jpg";
        public const string NormalPath = "Assets/_Project/Art/Textures/table_normal.png";
        public const string NetRgbaPath = "Assets/_Project/Art/Textures/table_net_rgba.png";
        public const string RoughnessPath = "Assets/_Project/Art/Textures/table_roughness.png";
        public const string MetallicPath = "Assets/_Project/Art/Textures/table_metallic.png";

        /// <summary>打包产物：R=Metallic, A=Smoothness(=1−Roughness)。</summary>
        public const string PackedMaskPath = "Assets/_Project/Art/Textures/table_metallic_roughness.png";

        /// <summary>绿通道是否翻转。OpenGL 约定 + Blender 导出 + Unity 官方文档 → 不翻转。</summary>
        public const bool InvertNormalGreenChannel = false;

        /// <summary>袋网 Alpha Clipping 的参考阈值（源数据 materials.net_mask_cutoff = 0.5）。</summary>
        public const float NetAlphaCutoff = 0.5f;

        // ── 模型 ───────────────────────────────────────────────────────────

        private void OnPreprocessModel()
        {
            if (assetPath != TableModelPath)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            // 单位：源顶点已是米制。禁用文件缩放并锁定 globalScale=1，
            // 使"1 Unity 单位 = 1 米"由本设置 + PoolConstants 双击钉死。
            importer.useFileScale = false;
            importer.globalScale = 1f;

            // 不烘焙额外的轴转换：模型轴向由 PoolGeometry.ModelAxisCorrection 单点处理（只做一次）。
            importer.bakeAxisConversion = false;

            // 只导入网格，其余一律关闭，避免生成无用资产。
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importConstraints = false;

            // 网格精度：碰撞轮廓必须与渲染网格严格对齐，关闭压缩。
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;                  // 供 Validator 逐顶点核对（Day 2 必需）
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk; // 法线贴图需要正确切线
            importer.generateSecondaryUV = false;

            // 材质：Day 3 手工建 URP Lit。今天不自动生成，避免半错材质。
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            importer.animationType = ModelImporterAnimationType.None;
        }

        // ── 贴图 ───────────────────────────────────────────────────────────

        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;

            switch (assetPath)
            {
                case BaseColorPath:
                    ConfigureBaseColor(importer);
                    break;
                case NormalPath:
                    ConfigureNormal(importer);
                    break;
                case NetRgbaPath:
                    ConfigureNet(importer);
                    break;
                case RoughnessPath:
                case MetallicPath:
                    ConfigureLinearSingleChannel(importer);
                    break;
                case PackedMaskPath:
                    ConfigurePackedMask(importer);
                    break;
            }
        }

        private static void ConfigureBaseColor(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;                 // 颜色数据
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        private static void ConfigureNormal(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.NormalMap; // 关键：否则会被当颜色贴图
            importer.sRGBTexture = false;                          // 法线是线性数据
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            // 绿通道约定：显式写死，避免"谁在 Inspector 里手点过"这种不可追溯的漂移。
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.flipGreenChannel = InvertNormalGreenChannel;
            importer.SetTextureSettings(settings);
        }

        private static void ConfigureNet(TextureImporter importer)
        {
            // 袋网用 Alpha Clipping（cutoff 0.5，双面 —— 双面在材质侧设置）。
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        private static void ConfigureLinearSingleChannel(TextureImporter importer)
        {
            // Metallic / Roughness 是线性单通道灰度图，必须 readable 才能被 ChannelPacker 读取。
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 1024;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        private static void ConfigurePackedMask(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;                // R=metallic、A=smoothness 均为线性
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
