using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **HUD 字体资产构建器**（Day 13 任务 2「字体接入」的机械化落点）。
    ///
    /// 两件事，缺一不可：
    /// ① **导入 TMP Essential Resources**（若工程里还没有）—— 代码生成的工程从不自带 TMP 的
    ///    着色器 / 默认材质 / `TMP Settings`，缺它则 `TMP_FontAsset.CreateFontAsset` 产出的材质
    ///    **着色器为 null**、运行期 TMP 组件直接不可用；
    /// ② 由 5 个已转好的 TTF 建 **5 个 TMP FontAsset**，并把 **ASCII 可打印区（0x20–0x7E）**
    ///    的字符**灌进字符表**。
    ///
    /// ── 为什么一定要灌字符表（关键，不是可选优化）─────────────────────────────
    /// `TMP_FontAsset.HasCharacters(string, out List&lt;char&gt;)` 的判定**只查 `m_CharacterLookupDictionary`**，
    /// **不回落源字体**（实测 `TMP_FontAsset.cs` 该重载全无 source-font 分支）⇒ 一个"建好但没灌字符"的
    /// 动态字体资产会让 `T97 (c)` 对**完全正常的字体**报红。灌字符表把该判据变成对**真实覆盖**的检查。
    ///
    /// ── 幂等 ───────────────────────────────────────────────────────────────
    /// 已存在的 FontAsset **不重建**（保留其 GUID ⇒ 场景与预制体的引用不会断）。
    /// 菜单「Billiards/Visual/Rebuild HUD Font Assets (force)」才强制重建。
    /// </summary>
    public static class HudAssetBuilder
    {
        /// <summary>字体目录（与 `Docs/35-A_Day13_最小HUD_spec.md` §3.1 的一致口径）。</summary>
        public const string FontsDirectory = "Assets/_Project/UI/Fonts";

        /// <summary>TMP 资源导入完成的判据物（`Assets/TextMesh Pro/Resources/TMP Settings.asset`）。</summary>
        public const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>ASCII 可打印区（空格 0x20 起、波浪号 0x7E 止）——HUD 全部英文文案的来源字符集。</summary>
        public const string AsciiPrintable =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        /// <summary>**标题族**资产（数值大字 + `POWER` + `Shoot`）。判据 `T97` 钉死此路径。</summary>
        public const string TitleFontAssetPath = FontsDirectory + "/BarlowCondensed-SemiBold SDF.asset";

        /// <summary>**正文族**资产（三个小标签 + 状态文案）。判据 `T97` 钉死此路径。</summary>
        public const string BodyFontAssetPath = FontsDirectory + "/Inter-Regular SDF.asset";

        /// <summary>标题族的**源 TTF**（`T97` 的 cmap 覆盖检查与源引用核对都钉这个）。</summary>
        public const string TitleFontTtfPath = FontsDirectory + "/BarlowCondensed-SemiBold.ttf";

        /// <summary>正文族的**源 TTF**。</summary>
        public const string BodyFontTtfPath = FontsDirectory + "/Inter-Regular.ttf";

        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasWidth = 1024;
        private const int AtlasHeight = 1024;

        /// <summary>TTF 文件名 → FontAsset 文件名（**两段名字都不是自由命名**：判据 `T97` 逐个钉死这些路径）。</summary>
        public static readonly (string Ttf, string Asset)[] FontTable =
        {
            ("BarlowCondensed-Medium.ttf", "BarlowCondensed-Medium SDF.asset"),
            ("BarlowCondensed-SemiBold.ttf", "BarlowCondensed-SemiBold SDF.asset"),
            ("BarlowCondensed-Bold.ttf", "BarlowCondensed-Bold SDF.asset"),
            ("Inter-Regular.ttf", "Inter-Regular SDF.asset"),
            ("Inter-SemiBold.ttf", "Inter-SemiBold SDF.asset"),
        };

        [MenuItem("Billiards/Visual/Build HUD Font Assets", false, 404)]
        public static void BuildInteractive()
        {
            Build(false);
        }

        [MenuItem("Billiards/Visual/Rebuild HUD Font Assets (force)", false, 405)]
        public static void RebuildInteractive()
        {
            Build(true);
        }

        /// <summary>供 `-executeMethod` 使用；退出码 0 = 全成功，1 = 有失败项（异常会让 Unity 直接非 0 退出）。</summary>
        public static void BuildFromCommandLine()
        {
            bool ok = Build(false);
            if (!ok)
            {
                throw new System.InvalidOperationException("HUD 字体资产构建存在失败项（详见上方日志）。");
            }
        }

        /// <summary>供 `-executeMethod` 使用：**只读探针**，把每个资产的加载后状态打到日志（诊断用，不改任何东西）。</summary>
        public static void ProbeFromCommandLine()
        {
            foreach ((string ttf, string asset) in FontTable)
            {
                string assetPath = FontsDirectory + "/" + asset;
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                if (fontAsset == null)
                {
                    Debug.Log($"[HudAssetProbe] {asset}：载入失败");
                    continue;
                }

                int characters = fontAsset.characterTable != null ? fontAsset.characterTable.Count : -1;
                int glyphs = fontAsset.glyphTable != null ? fontAsset.glyphTable.Count : -1;
                bool hasA = fontAsset.HasCharacters("A", out List<char> missing);

                bool diskOk = AssetFileHasPersistedTables(assetPath, out int diskCharacters, out int diskGlyphs, out string error);
                Debug.Log($"[HudAssetProbe] {asset}：内存 character={characters} glyph={glyphs}；"
                          + $"磁盘 character={(diskOk ? diskCharacters : -1)} glyph={(diskOk ? diskGlyphs : -1)}"
                          + $"{(diskOk ? string.Empty : "（" + error + "）")}；HasCharacters(\"A\")={hasA}"
                          + $" missing={(missing == null ? "null" : missing.Count.ToString())}；"
                          + $"atlas={(fontAsset.atlasTexture != null ? fontAsset.atlasTexture.width + "x" + fontAsset.atlasTexture.height : "null")}");
            }
        }

        /// <summary>供 `-executeMethod` 使用：**强制重建**（已存在的资产也重写）。</summary>
        public static void RebuildFromCommandLine()
        {
            bool ok = Build(true);
            if (!ok)
            {
                throw new System.InvalidOperationException("HUD 字体资产强制重建存在失败项（详见上方日志）。");
            }
        }

        public static bool Build(bool force)
        {
            bool allOk = true;
            allOk &= EnsureTmpEssentials();

            AssetDatabase.Refresh();

            foreach ((string ttf, string asset) in FontTable)
            {
                string ttfPath = FontsDirectory + "/" + ttf;
                string assetPath = FontsDirectory + "/" + asset;

                var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
                if (sourceFont == null)
                {
                    Debug.LogError($"[HudAssetBuilder] 源字体缺失：{ttfPath}");
                    allOk = false;
                    continue;
                }

                if (!force && AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
                {
                    Debug.Log($"[HudAssetBuilder] 已存在，跳过：{assetPath}");
                    continue;
                }

                if (!CreateFontAsset(sourceFont, assetPath, out string error))
                {
                    Debug.LogError($"[HudAssetBuilder] 建资产失败 {assetPath}：{error}");
                    allOk = false;
                    continue;
                }

                Debug.Log($"[HudAssetBuilder] 已建 {assetPath}（源 {ttf}）");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return allOk;
        }

        /// <summary>
        /// 缺则导入 TMP Essential Resources。返回是否"导入后可用"。
        ///
        /// ⚠ **实测结论（2026-10-15）：`AssetDatabase.ImportPackage` 在 `-batchmode` 下静默失效** ——
        /// 返回后既不报错也不落盘（日志只有一条 `Asset Pipeline Refresh`，`Assets/TextMesh Pro/` 根本没生成），
        /// 随后 `TMP_FontAsset.CreateFontAsset` 会在 `TMP_Settings.get_clearDynamicDataOnBuild` 抛
        /// `NullReferenceException`。该 API 依赖编辑器交互流程，**批处理不可用**。
        /// ⇒ 批处理路径改走确定性解包工具 `Docs/Tools/extract_tmp_essentials.py`
        /// （`.unitypackage` = gzip tar，逐条还原并保留包内 `.meta` 的 GUID）。
        /// 交互式菜单仍保留 `ImportPackage` 兜底。
        /// </summary>
        public static bool EnsureTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath))
            {
                Debug.Log($"[HudAssetBuilder] TMP 资源已在位：{TmpSettingsPath}");
                return true;
            }

            if (!Application.isBatchMode)
            {
                string interactive = ResolveEssentialsPackagePath();
                if (interactive != null)
                {
                    Debug.Log($"[HudAssetBuilder] 交互模式：导入 {interactive}");
                    AssetDatabase.ImportPackage(interactive, false);
                }
            }

            AssetDatabase.Refresh();

            if (File.Exists(TmpSettingsPath))
            {
                Debug.Log($"[HudAssetBuilder] TMP 资源导入完成：{TmpSettingsPath}");
                return true;
            }

            Debug.LogError(
                "[HudAssetBuilder] TMP 资源缺失，且本路径无法导入。请先跑："
                + "`<venv>/python Docs/Tools/extract_tmp_essentials.py`（批处理下 "
                + "`AssetDatabase.ImportPackage` 静默失效，见本方法注释）。");
            return false;
        }

        /// <summary>
        /// 定位资源包。**先走包信息 API**（版本无关），失败再按目录名兜底 ——
        /// 不把 `com.unity.ugui@&lt;hash&gt;` 的哈希写死（那是缓存目录名，升级即失效）。
        /// </summary>
        public static string ResolveEssentialsPackagePath()
        {
            const string relative = "Package Resources/TMP Essential Resources.unitypackage";

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui/package.json");
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            {
                string candidate = Path.Combine(info.resolvedPath, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string root = Path.GetDirectoryName(Application.dataPath);
            string cache = Path.Combine(root ?? ".", "Library/PackageCache");
            if (Directory.Exists(cache))
            {
                foreach (string dir in Directory.GetDirectories(cache, "com.unity.ugui*"))
                {
                    string candidate = Path.Combine(dir, relative);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static bool CreateFontAsset(Font sourceFont, string assetPath, out string error)
        {
            error = null;

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasWidth,
                AtlasHeight,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                error = "CreateFontAsset 返回 null（TMP 资源缺失或着色器不可用）。";
                return false;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);

            // ⚠ **顺序是硬约束**：必须先 `CreateAsset` 让资产成为**持久对象**，再灌字符。
            // 依据：`TMP_EditorResourceManager.RegisterResourceForUpdate` 的第一句就是
            // `if (!EditorUtility.IsPersistent(obj)) return;` —— 在 `CreateAsset` 之前灌字符，
            // 那时资产还不是持久对象 ⇒ TMP 的持久化注册被**整段跳过**，`TryAddCharacters` 的结果
            // 只留在内存里，`SaveAssets` 也写不进 `.asset`（实测：文件停在 6.4 KB、`m_CharacterTable: []`）。
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // 材质与图集是字体资产的**子资产**，必须显式挂上，否则重启编辑器后引用会丢。
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    Texture2D texture = fontAsset.atlasTextures[i];
                    if (texture == null)
                    {
                        continue;
                    }

                    texture.name = fontAsset.name + (i == 0 ? " Atlas" : " Atlas " + i);
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }
            }

            // 现在资产已持久 ⇒ 灌字符（ASCII 可打印区），TMP 的持久化注册才生效。
            fontAsset.TryAddCharacters(AsciiPrintable, out string missing, false);
            Debug.Log($"[HudAssetBuilder] {fontAsset.name} 灌字符后："
                      + $"characterTable={fontAsset.characterTable?.Count}, glyphTable={fontAsset.glyphTable?.Count}, "
                      + $"missingLength={missing?.Length ?? -1}");
            if (!string.IsNullOrEmpty(missing))
            {
                error = $"ASCII 字符集有缺失（{missing.Length} 个：{Describe(missing)}）—— 该字体不能用于 HUD。";
                return false;
            }

            // ⚠ **预热是尽力而为，不是成功条件**（实测结论）：`TryAddCharacters` 对
            // `AtlasPopulationMode.Dynamic` 的资产只是**预热优化** —— 运行期 TMP 会按需从
            // `sourceFontFile` 补字形。而且这次预热的结果**无法跨进程保留**（实测：本进程存盘后
            // 磁盘确有 95 条，下一次 `-runTests` 进程跑完文件又回到 6.4 KB / 空表 / 图集 1×1）。
            // ⇒ 资产是否可用**不取决于**持久化的字符表，而取决于 ①资产在场 ②`sourceFontFile` 有效
            //    ③源 TTF 的 cmap 覆盖所需字符（后两条由判据 `T97` 直接验）。
            // 故这里**只记录**预热结果，不让它决定成败。
            int prewarmedCharacters = fontAsset.characterTable != null ? fontAsset.characterTable.Count : 0;
            int prewarmedGlyphs = fontAsset.glyphTable != null ? fontAsset.glyphTable.Count : 0;

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            if (!AssetFileHasPersistedTables(assetPath, out int diskCharacters, out int diskGlyphs, out string readError))
            {
                Debug.LogWarning($"[HudAssetBuilder] {fontAsset.name} 磁盘回读失败（{readError}）—— 不影响 Dynamic 资产可用性。");
            }
            else if (diskCharacters == 0 || diskGlyphs == 0)
            {
                Debug.Log($"[HudAssetBuilder] {fontAsset.name} 预热未持久化（内存 {prewarmedCharacters}/{prewarmedGlyphs}，"
                          + $"磁盘 {diskCharacters}/{diskGlyphs}）—— Dynamic 资产运行期按需补字形，属预期。");
            }

            return true;
        }

        private static string Describe(string missing)
        {
            var parts = new List<string>();
            foreach (char c in missing)
            {
                parts.Add("0x" + ((int)c).ToString("X2"));
            }

            return string.Join(",", parts);
        }

        /// <summary>
        /// **读磁盘**统计 `.asset` 文本里 `m_CharacterTable` / `m_GlyphTable` 的条目数。
        ///
        /// 为什么必须读文件：内存对象与 `AssetDatabase.LoadAssetAtPath` 的即时回读**都会给出未持久化前的 95**
        /// （实测踩过 —— 文件停在 6.4 KB、`m_CharacterTable: []`，而回读报 95 ⇒ **假 PASS**）。
        /// 守卫强度 = 守卫读取的东西（铁律 10）。
        /// </summary>
        public static bool AssetFileHasPersistedTables(string assetPath, out int characters, out int glyphs, out string error)
        {
            characters = 0;
            glyphs = 0;
            error = null;

            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", assetPath);
            if (!File.Exists(full))
            {
                error = full + " 不存在";
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (System.Exception exception)
            {
                error = exception.Message;
                return false;
            }

            characters = CountEntries(text, "m_CharacterTable");
            glyphs = CountEntries(text, "m_GlyphTable");
            return true;
        }

        /// <summary>
        /// 数某个数组字段的元素个数。
        ///
        /// ⚠ **Unity 的 YAML 对空/非空数组写法不同**：
        /// 空 ⇒ `m_CharacterTable: []`（有方括号）；非空 ⇒ `m_CharacterTable:` 换行后逐行 `  - …`（**没有方括号**）。
        /// 只匹配 `: [` 会把**已填满**的数组也数成 0 ⇒ **假阴性**（本守卫第一版就栽在这里）。
        /// 故：先看是否为 `: []` 空形态，否则数块内行首恰好两空格 + `- ` 的条目
        /// （嵌套字段缩进更深，不会误计）。块右界 = 下一个同级字段（行首两空格 + `m_`）。
        /// </summary>
        private static int CountEntries(string text, string field)
        {
            string marker = "\n  " + field + ":";
            int markerIndex = text.IndexOf(marker, System.StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return 0;
            }

            int start = markerIndex + 1;
            int next = text.IndexOf("\n  m_", start + marker.Length, System.StringComparison.Ordinal);
            string block = next < 0 ? text.Substring(start) : text.Substring(start, next - start);

            if (block.StartsWith("  " + field + ": []", System.StringComparison.Ordinal))
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            while ((index = block.IndexOf("\n  - ", index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += 4;
            }

            return count;
        }
    }
}
