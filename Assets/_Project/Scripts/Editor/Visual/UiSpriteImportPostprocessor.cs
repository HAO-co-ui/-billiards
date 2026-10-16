using System.IO;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **界面精灵（图标 / 形状 / 迷你球贴片）的导入设置即代码**。
    ///
    /// 为什么必须有一个后处理器（不是可选项）：本工程是 **3D 模板工程** ⇒
    /// 新导入的 PNG 默认 `textureType = Default`（当贴图用，**不是 Sprite**）⇒
    /// `AssetDatabase.LoadAssetAtPath&lt;Sprite&gt;` 恒为 `null`，界面图标会**静默全空**。
    /// 把口径写成代码（而不是在 Inspector 里手点），才可能被复核与复现。
    ///
    /// 关键设置与理由：
    ///  · `spriteImportMode = Single` + `textureType = Sprite` ⇒ 一张 PNG = 一个精灵，消费方按清单序号取。
    ///  · `spriteBorder` = 4/4/4/4 **只对 `ui-panel-*` 设** ⇒ 圆角九宫格；其余留 0（拉伸即变形）。
    ///  · `mipmapEnabled = false` ⇒ UI 贴图永远在 1:1 附近采样，mip 只会让它更糊。
    ///  · `alphaIsTransparency = true` + `sRGBTexture = true` ⇒ 透明边缘不出现黑边（预乘问题）。
    ///  · `filterMode = Bilinear` ⇒ 抗锯齿边缘平滑。
    ///  · **压缩关闭**：64×64 的线性图标在 BC 压缩下笔画会断裂；整批不到 60 KB，不值当。
    ///  · `wrapMode = Clamp` ⇒ 九宫格拉伸时边缘不外卷。
    ///  · `isReadable = true` **只给迷你球贴片**：判据要逐像素核对"顶部条带是暖白（花色）还是球色（纯色）"，
    ///    以及"16 个贴片互不相同"。图标不需要读回，省一份运行时内存副本。
    /// </summary>
    public class UiSpriteImportPostprocessor : AssetPostprocessor
    {
        /// <summary>图标与形状目录（与 <c>ClubUiSprites.Directory</c> 同值）。</summary>
        public const string IconsDirectory = "Assets/_Project/UI/Icons/";

        /// <summary>迷你球贴片目录（与 <c>ClubBallChips.Directory</c> 同值）。</summary>
        public const string BallChipsDirectory = "Assets/_Project/UI/Balls/";

        /// <summary>九宫格边框（与 <c>ClubUiSprites.PanelSliceBorder</c> 同值）。</summary>
        public const int PanelBorder = 4;

        /// <summary>
        /// 图标与球贴片的**最大边长**。
        /// 设计域里图标是 15–27 px，`Icons/` 的生成物本身就是 64×64 ⇒ 128 已足够（且更省内存）。
        /// </summary>
        public const int IconMaxTextureSize = 128;

        /// <summary>
        /// **品牌位图（俱乐部徽章）的最大边长**。它**不是**程序化生成的 64×64 图标，而是美术包的
        /// 彩色成品位图；首页上按 88 设计 px 呈现，在 4K（画布缩放 2×）下要按 176 px 采样
        /// ⇒ 128 会明显糊。取 512 留足余量（原始图远大于 512，仍会被降采样，但**不是被砍到 128**）。
        /// </summary>
        public const int BrandBitmapMaxTextureSize = 512;

        /// <summary>品牌位图（俱乐部徽章）的文件名——与 <c>ClubUiSprites.BaseName(ClubCrest)</c> 同值。</summary>
        public const string ClubCrestFileName = "ui-club-crest";

        private void OnPreprocessTexture()
        {
            string extension = Path.GetExtension(assetPath);
            if (!string.Equals(extension, ".png", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool isIcon = assetPath.StartsWith(IconsDirectory);
            bool isBallChip = assetPath.StartsWith(BallChipsDirectory);
            if (!isIcon && !isBallChip)
            {
                return;
            }

            bool isPanel = isIcon && Path.GetFileNameWithoutExtension(assetPath).StartsWith("ui-panel-");
            bool isBrandBitmap = isIcon
                && string.Equals(Path.GetFileNameWithoutExtension(assetPath), ClubCrestFileName, System.StringComparison.Ordinal);

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;                  // == Canvas 的 referencePixelsPerUnit
            importer.spriteBorder = isPanel
                ? new Vector4(PanelBorder, PanelBorder, PanelBorder, PanelBorder)
                : Vector4.zero;
            importer.spritePivot = new Vector2(0.5f, 0.5f);

            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = isBrandBitmap ? BrandBitmapMaxTextureSize : IconMaxTextureSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = isBallChip;
        }
    }
}
