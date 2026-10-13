using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 把**分离的** Metallic / Roughness 单通道灰度图打包成 URP Lit 需要的
    /// <c>_MetallicGlossMap</c>：R = Metallic，A = Smoothness = 1 − Roughness。
    ///
    /// 这是排期风险登记册 R2 的核心问题：美术包给的是分离贴图（Metallic 1024²、
    /// Roughness 1024²），而 URP Lit 的金属度贴图要求 R 通道存金属度、A 通道存光滑度。
    /// 直接分别接两张图是接不上的（URP Lit 没有独立的 roughness 输入）。
    ///
    /// 为什么要代码化：这一步是"贴图数据变换"，手点 PS 不可复算、不可评审。
    /// 生成物 <c>table_metallic_roughness.png</c> 入库，可回溯到两张源图。
    /// 使用材质时把 URP Lit 的 Smoothness Source 设为 "Metallic Alpha" 即可。
    ///
    /// 幂等：产物已存在且尺寸正确时跳过，除非 force=true。
    /// </summary>
    public static class TableTextureChannelPacker
    {
        private static readonly string Metallic = BilliardsAssetPostprocessor.MetallicPath;
        private static readonly string Roughness = BilliardsAssetPostprocessor.RoughnessPath;
        private static readonly string Output = BilliardsAssetPostprocessor.PackedMaskPath;

        [MenuItem("Billiards/Art/Pack Table Metallic-Roughness Mask", false, 300)]
        public static void PackInteractive()
        {
            string message = Pack(force: true);
            Debug.Log(message);
            EditorUtility.DisplayDialog("Billiards Channel Packer", message, "OK");
        }

        /// <summary>命令行入口：打包 → 打印 → 退出码。报告（控制台）先输出再退出。</summary>
        public static void PackFromCommandLine()
        {
            int exit = 0;
            try
            {
                Debug.Log("PACK_RESULT=" + Pack(force: true));
            }
            catch (Exception ex)
            {
                Debug.LogError("通道打包失败：" + ex);
                exit = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exit);
            }
        }

        /// <summary>返回人类可读的结果摘要。异常在内部转成抛出的 InvalidOperationException。</summary>
        public static string Pack(bool force)
        {
            if (!force && ProjectPaths.Exists(Output))
            {
                var probe = AssetDatabase.LoadAssetAtPath<Texture2D>(Output);
                if (probe != null && probe.width > 0)
                {
                    return $"已存在打包产物（{probe.width}×{probe.height}），跳过。force=true 可强制重生成。";
                }
            }

            if (!ProjectPaths.Exists(Metallic) || !ProjectPaths.Exists(Roughness))
            {
                throw new InvalidOperationException(
                    $"源贴图缺失：metallic={ProjectPaths.Exists(Metallic)} roughness={ProjectPaths.Exists(Roughness)}");
            }

            // 确保源贴图已按 readable 导入（Postprocessor 会设，但首次导入顺序不保证；这里兜底）。
            EnsureReadable(Metallic);
            EnsureReadable(Roughness);

            var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(Metallic);
            var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(Roughness);
            if (metal == null || rough == null)
            {
                throw new InvalidOperationException("无法加载 metallic/roughness 贴图为 Texture2D。");
            }

            if (metal.width != rough.width || metal.height != rough.height)
            {
                throw new InvalidOperationException(
                    $"metallic {metal.width}×{metal.height} 与 roughness {rough.width}×{rough.height} 尺寸不一致，无法逐像素打包。");
            }

            int w = metal.width;
            int h = metal.height;

            Color[] mc = metal.GetPixels();
            Color[] rc = rough.GetPixels();
            var outPixels = new Color[w * h];

            for (int i = 0; i < outPixels.Length; i++)
            {
                float metallicValue = mc[i].r;                 // 灰度图：R 即数值
                float smoothness = 1f - rc[i].r;               // roughness → smoothness
                outPixels[i] = new Color(metallicValue, 0f, 0f, smoothness);
            }

            var packed = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: true);
            packed.SetPixels(outPixels);
            packed.Apply(false, false);

            byte[] png = packed.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(packed);

            string fullOut = ProjectPaths.ToFullPath(Output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
            File.WriteAllBytes(fullOut, png);
            AssetDatabase.ImportAsset(Output, ImportAssetOptions.ForceUpdate);

            var verify = AssetDatabase.LoadAssetAtPath<Texture2D>(Output);
            string size = verify != null ? $"{verify.width}×{verify.height}" : "<未导入>";

            return $"已生成 {Output}（{size}）。R=Metallic，A=Smoothness=1−Roughness。"
                 + " URP Lit 使用时将 Smoothness Source 设为 Metallic Alpha。";
        }

        private static void EnsureReadable(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }
    }
}
