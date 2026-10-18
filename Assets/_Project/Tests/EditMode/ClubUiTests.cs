using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.Rules;
using Billiards.Table;
using Billiards.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **对局界面与反馈的判据族**（编号续 `T123`：`T124`–`T126`）。
    ///
    /// 起因（用户实机反馈第三条）：**「这个 UI，太丑了，根据 `04_UI` 里面的 UI 做」**。
    /// 实测根因：界面**没有设计来源** —— 三块 HUD 与结算卡各自内联自己的色值，
    /// 既没有设计稿的品牌元素（徽章 / 记分牌 / 球行 / 力度滑块 / 图标），
    /// 也没有设计稿的语义状态（犯规 / 自由球 / 指定袋 / 结算）。
    ///
    /// 本族把三件事钉死：
    /// ① `T124` 令牌与精灵**只有一个来源**（且视图里一个颜色字面量都不许有）；
    /// ② `T125` 状态语义与设计契约 `data-state` **八态一一映射**，且**酒红/亮青不得外借**；
    /// ③ `T126` 补的两类反馈（犯规环 / 结算环）**时长与配色合设计**、槽预算不溢出。
    ///
    /// **盲带（如实）**：本族全部是**结构 / 数值 / 源码级**判据 —— `-nographics` 无渲染，
    /// "看起来好不好看""文字有没有溢出块"**不可判定**，只能由用户实机截图验收。
    /// </summary>
    public class ClubUiTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        // ════════════════ T124 设计令牌与精灵单一真源 ══════════════════════════

        /// <summary>
        /// **`T124`：设计令牌唯一来源 + 视图零颜色字面量 + 精灵槽位完整**。
        ///
        /// 断言：
        /// ① 五个主色与设计说明逐字节一致（黄铜 `#d4b46c`、炭黑 `#102025`、桌布绿 `#174e46`、
        ///    静音灰 `#a9b9b4`、暖白 `#f2efe4`）—— 以**文档**为准（原型取色器读到 `#d2b46c`，
        ///    差 1 个最低位，属显示器实现差异）；
        /// ② 圆角 ∈ [1,3]（设计说明 §一"控件边角保持 1—3 像素"），且 `PanelForRadius` 越界会**夹取**而不是
        ///    生成大圆角；
        /// ③ `TryParseHex` 能解析并能拒绝坏输入（证明"文档字符串 ↔ 实现取值"这条链**真的在跑**，
        ///    而不是把两处字面量并排比对）；
        /// ④ **视图源码里零颜色构造**（`new Color(` / `new Color32(` / 字符串十六进制）——
        ///    这是"单一真源"唯一可机械化核对的形态；
        /// ⑤ 场景里 HUD 的精灵槽位**逐格**对得上清单（错位会让状态显示成别的图标）；
        /// ⑥ 16 个球号贴片**互不相同**、且**花色球的顶部条带是暖白**（纯色球不是）——
        ///    这条不能靠"色表对了"碰巧通过：它读的是**真实像素**。
        ///
        /// **注入负对照**：把 `ClubUiTokens.Accent` 改成 `#d2b46c` ⇒ ① 红；
        /// 在 `PracticeHudView` 里写一个 `new Color(1,0,0,1)` ⇒ ④ 红；
        /// 把 9 号贴片换成 1 号的文件 ⇒ ⑥ 红（纯色球的顶部条带不是暖白）。
        /// </summary>
        [Test]
        public void T124_DesignTokens_AndSprites_SingleSource()
        {
            var failures = new List<string>();

            // ① 主色逐字节
            var expected = new (string Name, string Hex, Color32 Actual)[]
            {
                ("黄铜 --accent", "#d4b46c", ClubUiTokens.Accent),
                ("炭黑 --ink", "#102025", ClubUiTokens.Ink),
                ("桌布绿 --cloth", "#174e46", ClubUiTokens.Cloth),
                ("静音灰 --muted", "#a9b9b4", ClubUiTokens.Muted),
                ("暖白正文", "#f2efe4", ClubUiTokens.Text),
            };

            foreach ((string name, string hex, Color32 actual) in expected)
            {
                if (!ClubUiTokens.TryParseHex(hex, out Color32 want))
                {
                    failures.Add($"③ 自带解析器读不出设计文档里的 {hex}（{name}）——解析链本身有问题。");
                    continue;
                }

                if (want.r != actual.r || want.g != actual.g || want.b != actual.b || want.a != actual.a)
                {
                    failures.Add($"① {name} 应为 {hex}，实际 {ClubUiTokens.ToHexRgba(actual)}。");
                }
            }

            // ③ 坏输入必须被拒（否则"解析器"只是永远返回 true 的空壳）
            var badInputs = new[] { "", "  ", "#12345", "#1234567", "#gggggg", "d4b46c00ff", "0x102025" };
            foreach (string bad in badInputs)
            {
                if (ClubUiTokens.TryParseHex(bad, out _))
                {
                    failures.Add($"③ 坏输入 \"{bad}\" 被解析成合法色值 ⇒ 解析器没有真正的校验。");
                }
            }

            if (!ClubUiTokens.TryParseHex("#d4b46c45", out Color32 withAlpha) || withAlpha.a != 0x45)
            {
                failures.Add("③ 带 alpha 的八位十六进制解析失败（边线族全靠它）。");
            }

            // ② 圆角区间
            if (!Mathf.Approximately(ClubUiTokens.RadiusMin, 1f) || !Mathf.Approximately(ClubUiTokens.RadiusMax, 3f))
            {
                failures.Add($"② 圆角区间应为 [1,3]（设计说明 §一），实际 [{ClubUiTokens.RadiusMin},{ClubUiTokens.RadiusMax}]。");
            }

            float[] declaredRadii =
            {
                ClubUiTokens.RadiusDialog, ClubUiTokens.RadiusPanel, ClubUiTokens.RadiusButton,
                ClubUiTokens.RadiusSegment, ClubUiTokens.RadiusSegmentInner, ClubUiTokens.RadiusTrack,
            };
            foreach (float radius in declaredRadii)
            {
                if (radius < ClubUiTokens.RadiusMin || radius > ClubUiTokens.RadiusMax)
                {
                    failures.Add($"② 圆角 {radius:R} 越出设计区间 [1,3] ⇒ 会出现设计说明禁掉的\"现代 SaaS 式圆角\"。");
                }
            }

            // ② 夹取：越界值必须落到区间内，且 1/2/3 各映射到对应槽位
            if (ClubUiSprites.PanelForRadius(1f) != ClubUiSprites.UiSprite.Panel1
                || ClubUiSprites.PanelForRadius(2f) != ClubUiSprites.UiSprite.Panel2
                || ClubUiSprites.PanelForRadius(3f) != ClubUiSprites.UiSprite.Panel3)
            {
                failures.Add("② 圆角 1/2/3 未映射到 Panel1/Panel2/Panel3。");
            }

            if (ClubUiSprites.PanelForRadius(9f) != ClubUiSprites.UiSprite.Panel3
                || ClubUiSprites.PanelForRadius(0f) != ClubUiSprites.UiSprite.Panel1)
            {
                failures.Add("② 越界圆角未被夹取到区间端点 ⇒ 误传 9 px 会拿到大圆角。");
            }

            // ④ 视图源码零颜色构造
            foreach (string viewFile in ViewSourceFiles)
            {
                string code = StripComments(ReadSourceOrNull(viewFile));
                if (code == null)
                {
                    failures.Add($"④ 读不到视图源码 {viewFile}（源码守卫失效 ⇒ 等于没有守卫）。");
                    continue;
                }

                MatchCollection constructions = Regex.Matches(code, @"new\s+Color(32)?\s*\(");
                if (constructions.Count > 0)
                {
                    failures.Add($"④ {Path.GetFileName(viewFile)} 里有 {constructions.Count} 处颜色构造字面量"
                                 + "（应全部走 ClubUiTokens）—— 第二份色值必然与设计稿分叉。");
                }

                MatchCollection hexLiterals = Regex.Matches(code, "\"#[0-9a-fA-F]{6,8}\"");
                if (hexLiterals.Count > 0)
                {
                    failures.Add($"④ {Path.GetFileName(viewFile)} 里有 {hexLiterals.Count} 处十六进制颜色字符串。");
                }
            }

            // ④ 令牌清册本身自洽：名字唯一，且每条都能"自解析回自己"
            //    （自解析是**真实**检查：它同时走 `ToHexRgba` 与 `TryParseHex` 两条实现路径）
            var seen = new HashSet<string>();
            foreach ((string name, Color32 value) in ClubUiTokens.ColorLedger)
            {
                if (!seen.Add(name))
                {
                    failures.Add($"① 令牌清册里有重名 {name}（清册重复计数会让\"数量核对\"失真）。");
                }

                string hex = ClubUiTokens.ToHexRgba(value);
                if (!ClubUiTokens.TryParseHex(hex, out Color32 back) || back.r != value.r || back.g != value.g
                    || back.b != value.b || back.a != value.a)
                {
                    failures.Add($"① 令牌 {name} 写出 {hex} 后解析不回原值 ⇒ 清册与解析器口径不一致。");
                }
            }

            if (ClubUiTokens.ColorLedger.Length < 16)
            {
                failures.Add($"① 令牌清册只有 {ClubUiTokens.ColorLedger.Length} 条，少于设计稿用到的主色数。");
            }

            // ⑤⑥ 场景侧与贴片像素
            CheckSceneSpriteSlots(failures);

            Assert.IsEmpty(failures, "T124 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T124: 5 主色逐字节 ✓；圆角 [{ClubUiTokens.RadiusMin},{ClubUiTokens.RadiusMax}] 且夹取 ✓；"
                                  + $"清册 {ClubUiTokens.ColorLedger.Length} 条无重名 ✓；"
                                  + $"视图 {ViewSourceFiles.Length} 个文件零颜色字面量 ✓；精灵 {ClubUiSprites.Count} 槽 + "
                                  + $"球贴片 {ClubBallChips.Count} 槽 ✓");
        }

        private static readonly string[] ViewSourceFiles =
        {
            "Assets/_Project/Scripts/Runtime/UI/PracticeHudView.cs",
            "Assets/_Project/Scripts/Runtime/UI/PracticeResultView.cs",
            // 开始界面（首页）：与上面两个视图同一约束 —— 颜色只许来自 `ClubUiTokens`。
            "Assets/_Project/Scripts/Runtime/UI/HomeView.cs",
        };

        /// <summary>
        /// `PracticeHudView` 的源码路径（`T125 ⑤'''` 白盒文本断言用）。
        /// **从 <see cref="ViewSourceFiles"/> 取**（而不是另写一个字符串字面量）⇒ 两处不可能漂移
        /// （"同一个路径写两遍"正是这类白盒断言最易失效的方式）。
        /// </summary>
        private static readonly string PracticeHudViewSourcePath = ViewSourceFiles[0];

        /// <summary>`HomeView` 的源码路径（`T144 ④` 白盒接线断言用）。</summary>
        private const string HomeViewSourcePath = "Assets/_Project/Scripts/Runtime/UI/HomeView.cs";

        private static void CheckSceneSpriteSlots(List<string> failures)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PracticeHudView hud = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (PracticeHudView candidate in root.GetComponentsInChildren<PracticeHudView>(true))
                    {
                        hud = candidate;
                        break;
                    }
                }

                if (hud == null)
                {
                    failures.Add("⑤ 场景里找不到 PracticeHud。");
                    return;
                }

                if (!ClubUiSprites.ValidateSlots(hud.uiSprites, out string spriteError))
                {
                    failures.Add("⑤ 界面精灵槽位不合格：" + spriteError);
                }

                if (!ClubBallChips.ValidateSlots(hud.ballChips, out string chipError))
                {
                    failures.Add("⑥ 球号贴片槽位不合格：" + chipError);
                }

                if (hud.vfxView == null)
                {
                    failures.Add("⑤ 场景 HUD 的 vfxView 未接线 ⇒ 右上 VFX 开关点了没反应（假开关）。");
                }

                if (hud.poolAudio == null)
                {
                    failures.Add("⑤ 场景 HUD 的 poolAudio 未接线 ⇒ 右上 SFX 开关点了没反应（假开关）。");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            CheckBallChipPixels(failures);
        }

        /// <summary>
        /// 读**真实像素**核对球号贴片：① 16 个互不相同；② 花色球（9–15）顶部条带是暖白、
        /// 纯色球（1–8）不是。
        ///
        /// 为什么"顶部条带是不是中性亮色"就能分开两类：花色球的贴片上/下各 22% 是暖白，
        /// 纯色球那一段就是球色本身。**黑 8 号球**的球色接近中性但**很暗** ⇒ 判据必须同时看
        /// "三通道接近"与"够亮"，只看前者会把 8 号误判成花色（这是本判据最容易写错的地方）。
        /// </summary>
        private static void CheckBallChipPixels(List<string> failures)
        {
            var signatures = new HashSet<ulong>();
            var neutral = new bool[ClubBallChips.Count];

            for (int number = 0; number < ClubBallChips.Count; number++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ClubBallChips.AssetPath(number));
                if (sprite == null)
                {
                    failures.Add($"⑥ 球号 {number} 的贴片载入失败。");
                    continue;
                }

                Texture2D texture = sprite.texture;
                if (texture == null || !texture.isReadable)
                {
                    failures.Add($"⑥ 球号 {number} 的贴片不可读（CPU 侧）⇒ 像素判据失效；"
                                 + "导入设置里 `isReadable` 必须为真（`UiSpriteImportPostprocessor`）。");
                    continue;
                }

                Color32[] pixels = texture.GetPixels32();
                signatures.Add(Hash(pixels));

                Rect rect = sprite.rect;
                int x = Mathf.Clamp(Mathf.RoundToInt(rect.x + ClubBallChips.TopBandSampleUv.x * rect.width), 0, texture.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(rect.y + ClubBallChips.TopBandSampleUv.y * rect.height), 0, texture.height - 1);
                Color32 sample = pixels[y * texture.width + x];
                neutral[number] = IsNeutralLight(sample);
            }

            for (int number = 1; number < ClubBallChips.Count; number++)
            {
                bool striped = ClubBallChips.IsStriped(number);
                if (striped != neutral[number])
                {
                    failures.Add($"⑥ 球号 {number} 应为{(striped ? "花色" : "纯色")}球，"
                                 + $"但顶部条带的实测结果相反（花色球的顶部 22% 是暖白、纯色球不是）。");
                }
            }

            if (signatures.Count != ClubBallChips.Count)
            {
                failures.Add($"⑥ 16 个球号贴片只有 {signatures.Count} 种图案（应有 {ClubBallChips.Count} 种）"
                             + " ⇒ 有球号共用了同一张图（旧缺陷\"所有球都是白球材质\"的贴片版）。");
            }
        }

        /// <summary>"中性且够亮"：三通道极差 ≤ 容差 **且** 亮度够高。</summary>
        private static bool IsNeutralLight(Color32 color)
        {
            int max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            int min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            float luma = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            return max - min <= ClubBallChips.NeutralChannelTolerance && luma >= LumaLightThreshold;
        }

        /// <summary>"够亮"的下界。暖白 `#f2efe4` 亮度 ≈ 239；黑八球 ≈ 24 ⇒ 取中间偏上（140）留足余量。</summary>
        private const float LumaLightThreshold = 140f;

        private static ulong Hash(Color32[] pixels)
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < pixels.Length; i++)
            {
                uint packed = (uint)(pixels[i].r | (pixels[i].g << 8) | (pixels[i].b << 16) | (pixels[i].a << 24));
                hash ^= packed;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        private static string ReadSourceOrNull(string relativePath)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            string full = Path.Combine(root, relativePath);
            return File.Exists(full) ? File.ReadAllText(full, Encoding.UTF8) : null;
        }

        /// <summary>
        /// 去掉注释（行注释 + 块注释）。
        /// **为什么必须去**：源码级守卫若把注释也算进去，一句"设计稿是 `#d4b46c`"的说明就会误红；
        /// 反之若为了兼容注释而放宽正则，守卫就抓不到真正写死的色值（守卫强度 = 守卫读的东西，铁律 10）。
        /// **已知局限**：不处理"字符串字面量里含 `//`"的情况（本工程的视图代码里没有这种字符串）。
        /// </summary>
        private static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string withoutBlocks = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var builder = new StringBuilder(withoutBlocks.Length);
            foreach (string line in withoutBlocks.Split('\n'))
            {
                int index = line.IndexOf("//", System.StringComparison.Ordinal);
                builder.Append(index >= 0 ? line.Substring(0, index) : line).Append('\n');
            }

            return builder.ToString();
        }

        // ════════════════ T125 状态契约八态 ═══════════════════════════════════

        /// <summary>
        /// **`T125`：状态语义与设计契约 `data-state` 一一映射，且颜色族不得外借**。
        ///
        /// 断言：
        /// ① 八个视觉状态 ↔ 八个设计 token，**逐字**（`aim`/`ai-turn`/`moving`/`ball-in-hand`/
        ///    `call-pocket`/`foul`/`paused`/`result`），且互不重复；
        /// ② 不可达集合**恰好**是 `{paused}`（P6 两小步按事实收窄：`ai-turn` 与 `call-pocket` 都已接线可达，
        ///    各自的三方出口见 `PracticeHudState.UnreachableStates` 的注释；只剩 `paused` 因为**本卡没有暂停系统**）
        ///    —— 既不许悄悄缩小（做了却没登记），也不许悄悄扩大（少做了却声称"设计没有"）；
        ///    ②' **反证**：把 `ResolveVisualState` 的参数空间穷举，断言"登记为可达的**必须真能产出**、
        ///    登记为不可达的**必须产不出**"（只断集合相等会**真空**：删掉某一支时集合没变、断言仍绿）；
        /// ③ 每个状态都有非空且**互不相同**的标题与说明，图标也互不相同（防复制粘贴时漏改）；
        /// ③' **兜底键 `Esc` 必须在"待放球"文案里出现**（`Esc` 只存在于文案里 ⇒ 删掉它就等于兜底不存在）；
        /// ④ **酒红只给犯规/失败、亮青只给指定袋**：双向断言（该用的必须用、不该用的一个都不许用）；
        /// ⑤ `ResolveVisualState` 的优先级表（结算 &gt; 被拒 &gt; 犯规 &gt; 自由球 &gt; 相位）；
        /// ⑤' **待放球**（`awaitingPlacement`）：进自由球态，且**不得越过结算卡**；
        /// ⑤'' **说明行文案**（`CaptionLine`）：自由球三个子情形（待放球 / 已在台上 / **落点被拒**）各说各的，
        ///     且**"落点被拒"绝不许说成 `"Wait for the table"`**（那一刻台面是静止的 ⇒ 那是**假陈述**），
        ///     并必须**压过** `moving` 档（两个拒绝窗口可能同时开着）；
        /// ⑤''' **视图侧接线**（白盒文本断言，读 `PracticeHudView.cs` 的**磁盘文本**）：
        ///     "检出 `RefusedPlacementCount` 上升沿"与"打开 `_placementRefusedUntil` 窗口"这两行**必须在**；
        /// ⑥ 犯规脉冲是 0.8 s 的一次性 ease-out（端点 + 单调）。
        ///
        /// ⚠ ③' / ⑤' / ⑤'' / ⑤''' 是**补的守缺口**：既有 9 行优先级表走 5 参调用（`awaitingPlacement` 取默认 `false`）
        /// ⇒ `ballInHand || awaitingPlacement` 那条分支、自由球的三个子情形文案、以及**两个拒绝窗口的视图侧接线**，
        /// 过去**一行都没被跑到**（"有守卫 ≠ 守卫会跑"）。
        ///
        /// **注入负对照**：把 `CallPocketSymbol` 改成黄铜 ⇒ ④ 红；
        /// **把 `AiTurn` 加回 `UnreachableStates`**（P6 重钉后的反向）⇒ ② 红；
        /// **把 `ResolveVisualState` 的 `aiTurn` 那一支删掉** ⇒ **②' 红**（登记为可达却产出不了）；
        /// 把两个状态的标题改成一样 ⇒ ③ 红；
        /// 把"待放球"文案里的 `Esc` 删掉 ⇒ ③' 红（兜底键不可发现）；
        /// 把 `ResolveVisualState` 的 `|| awaitingPlacement` 去掉 ⇒ ⑤' 用例 1 红；
        /// 把 `resultOpen` 那一段挪到 `awaitingPlacement` 之后 ⇒ ⑤' 用例 2 红（优先级被越级）；
        /// 把 `CaptionLine` 里 `placementRefused` 那条挪到最后 ⇒ ⑤'' 用例 3 红（落点被拒被 `moving` 压掉）；
        /// 把 `PlacementRefusedDetail` 改成 `"Wait for the table"` ⇒ ⑤'' 假陈述守卫红；
        /// **删掉 `_placementRefusedUntil = now + FoulBannerSeconds;` 那一行 ⇒ ⑤''' 红**
        /// （**已实测**：`INJ2.xml` mtime 18:56:04、`total=231 / passed=230 / failed=1`、
        /// 失败名恰为 `T125_StatusContract_MapsDesignStates`、消息含"视图侧接线缺失"；
        /// 逐条红名单见规格 §12.2.3 的 ⑤''' 行，本批注入总表见 §12.2.9）；
        /// 删掉 `drive.RefusedPlacementCount > _lastPlacementRefusedSeen` 那一行 ⇒ ⑤''' 红（同一条断言的另一半）。
        /// </summary>
        [Test]
        public void T125_StatusContract_MapsDesignStates()
        {
            var failures = new List<string>();
            var states = (HudVisualState[])System.Enum.GetValues(typeof(HudVisualState));

            // ① token 逐字、唯一
            var expectedTokens = new[]
            {
                "aim", "ai-turn", "moving", "ball-in-hand", "call-pocket", "foul", "paused", "result",
            };

            if (states.Length != expectedTokens.Length)
            {
                failures.Add($"① 视觉状态 {states.Length} 个，设计契约是 {expectedTokens.Length} 个。");
            }

            var tokens = new HashSet<string>();
            foreach (HudVisualState state in states)
            {
                string token = PracticeHudState.StateToken(state);
                if (!tokens.Add(token))
                {
                    failures.Add($"① token \"{token}\" 重复。");
                }

                if (System.Array.IndexOf(expectedTokens, token) < 0)
                {
                    failures.Add($"① token \"{token}\" 不在设计契约的八态里。");
                }
            }

            foreach (string expected in expectedTokens)
            {
                if (!tokens.Contains(expected))
                {
                    failures.Add($"① 设计契约的 \"{expected}\" 没有对应状态。");
                }
            }

            // ② 不可达集合恰好一项（**P6 两小步按事实重钉**）
            //    ⚠ 旧断言是三项（`{ai-turn, call-pocket, paused}`），理由写的是
            //    「35-A 没有 AI / 规则引擎 / 暂停」—— **三条里两条已是假陈述**（AI 与规则引擎都已落地）。
            //    重钉依据（**不是删掉**）：
            //    · `ai-turn`：真值源 `PhysicsTestDrive.IsAiTurn`、施加口 `SetTurn` / `ApplyVerdict`、
            //      状态条出口 `ResolveVisualState(..., aiTurn)`（由 `PracticeHudView` 喂）；
            //    · `call-pocket`：**D3 已按用户要求取消**（黑 8 任意袋有效）⇒ 产品侧出口
            //      （`IsEightBallShot` / `TryCallPocket` / `CalledPocket` / `RefusedCallPocketCount`）已全部删除
            //      ⇒ 该状态**重新变为不可达**（契约先行：枚举/配色/图标仍在）。
            var unreachable = new HashSet<HudVisualState>(PracticeHudState.UnreachableStates);
            var expectedUnreachable = new HashSet<HudVisualState>
            {
                // D3 已按用户要求取消（黑 8 任意袋有效）⇒ `call-pocket` 重回不可达；`paused` 本卡无暂停系统。
                HudVisualState.CallPocket, HudVisualState.Paused,
            };

            foreach (HudVisualState state in expectedUnreachable)
            {
                if (!unreachable.Contains(state))
                {
                    failures.Add($"② {state} 应当登记为\"本卡不可达\"（`paused`：本卡没有暂停系统）。");
                }
            }

            foreach (HudVisualState state in unreachable)
            {
                if (!expectedUnreachable.Contains(state))
                {
                    failures.Add($"② {state} 被登记成不可达，但它在本卡是可触发的 ⇒ 登记与实际不符"
                                 + "（P6 起 `ai-turn` 与 `call-pocket` 都已可达，不得再被登记成不可达）。");
                }
            }

            // ②' **反证**：把 `ResolveVisualState` 的**参数空间穷举**一遍，收集**实际产出**的状态集合，
            //     断言 `产出 ∪ 不可达 == 八态` 且 `产出 ∩ 不可达 == ∅`。
            //     ⇒ 两向都会红：**登记为可达却产出不了**（漏接线）与**登记为不可达却真能产出**（登记过期）。
            //     ⚠ 这一条是"能把 `aiTurn` / `callPocket` 那一支删掉就打红"的**机器形态** —— 只断集合相等的话，
            //       删分支不会让集合断言变红（集合还是那些项），判据就**真空**了。
            var phases = (ShotPhase[])System.Enum.GetValues(typeof(ShotPhase));
            var produced = new HashSet<HudVisualState>();
            foreach (ShotPhase phase in phases)
            {
                foreach (bool foul in new[] { false, true })
                {
                    foreach (bool refused in new[] { false, true })
                    {
                        foreach (bool hand in new[] { false, true })
                        {
                            foreach (bool resultOpen in new[] { false, true })
                            {
                                foreach (bool placement in new[] { false, true })
                                {
                                    foreach (bool aiTurn in new[] { false, true })
                                    {
                                        {
                                            produced.Add(PracticeHudState.ResolveVisualState(
                                                phase, foul, refused, hand, resultOpen, placement, aiTurn));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (HudVisualState state in states)
            {
                bool declaredReachable = !unreachable.Contains(state);
                bool actuallyProduced = produced.Contains(state);

                if (declaredReachable && !actuallyProduced)
                {
                    failures.Add($"②' {state} 被登记为**可达**，但 `ResolveVisualState` 的参数穷举"
                                 + "**产出不了它** ⇒ 登记里那句\"可达\"是空话（接线漏了）。");
                }

                if (!declaredReachable && actuallyProduced)
                {
                    failures.Add($"②' {state} 被登记为**不可达**，但参数穷举**真的产出了它**"
                                 + " ⇒ 登记过期（不可达集合必须按事实缩小）。");
                }
            }

            // ③ 文案与图标：非空、互不相同
            var titles = new HashSet<string>();
            var details = new HashSet<string>();
            var icons = new HashSet<ClubUiSprites.UiSprite>();

            foreach (HudVisualState state in states)
            {
                string title = PracticeHudState.StateTitle(state);
                string detail = PracticeHudState.StateDetail(state);

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(detail))
                {
                    failures.Add($"③ {state} 的标题/说明为空。");
                }

                if (!titles.Add(title))
                {
                    failures.Add($"③ 标题 \"{title}\" 被多个状态共用 ⇒ 状态切换看不出来。");
                }

                if (!details.Add(detail))
                {
                    failures.Add($"③ 说明 \"{detail}\" 被多个状态共用。");
                }

                if (!icons.Add(ClubUiSprites.ForState(state)))
                {
                    failures.Add($"③ {state} 的图标与别的状态相同（复制粘贴漏改）。");
                }
            }

            // ③' **兜底键必须可发现**（原始派单第 ③ 条的硬要求）：
            //     `Esc`（放回开球点）**只**存在于文案里 —— 界面上没有任何别的替代物
            //     ⇒ 不把这条写进判据的话，将来改文案时"悄悄删掉兜底提示"不会有任何东西红，
            //        而后果是**兜底形同不存在**（玩家不会去猜一个没写在界面上的键）。
            if (!PracticeHudState.BallInHandPendingDetail.Contains("Esc"))
            {
                failures.Add("③ 自由球\"待放球\"文案里找不到 `Esc` ⇒ 兜底键不可发现（形同不存在）。");
            }

            // ④ 颜色族纪律（双向）
            foreach (HudVisualState state in states)
            {
                bool isFoul = state == HudVisualState.Foul;
                bool isCall = state == HudVisualState.CallPocket;

                if (isCall && (!InFamily(ClubUiTokens.CyanFamily, PracticeHudState.EdgeColor(state))
                               || !InFamily(ClubUiTokens.CyanFamily, PracticeHudState.SymbolColor(state))))
                {
                    failures.Add("④ call-pocket 必须用亮青（设计说明 §一：亮青只用于指定球袋）。");
                }

                if (!isCall
                    && (InFamily(ClubUiTokens.CyanFamily, PracticeHudState.EdgeColor(state))
                        || InFamily(ClubUiTokens.CyanFamily, PracticeHudState.SymbolColor(state))))
                {
                    failures.Add($"④ {state} 借用了亮青 —— 亮青只许出现在 call-pocket。");
                }

                if (isFoul && (!InFamily(ClubUiTokens.ClaretFamily, PracticeHudState.EdgeColor(state))
                               || !InFamily(ClubUiTokens.ClaretFamily, PracticeHudState.SymbolColor(state))))
                {
                    failures.Add("④ foul 必须用酒红（设计说明 §一：酒红只用于犯规和失败）。");
                }

                if (!isFoul
                    && (InFamily(ClubUiTokens.ClaretFamily, PracticeHudState.EdgeColor(state))
                        || InFamily(ClubUiTokens.ClaretFamily, PracticeHudState.SymbolColor(state))))
                {
                    failures.Add($"④ {state} 借用了酒红 —— 酒红只许出现在犯规（与结算卡的负局）。");
                }
            }

            // ⑤ 优先级表
            var priority = new (string Case, ShotPhase Phase, bool Foul, bool Refused, bool BallInHand, bool Result, HudVisualState Want)[]
            {
                ("结算压倒一切", ShotPhase.Aim, true, true, true, true, HudVisualState.Result),
                ("被拒 > 犯规", ShotPhase.Aim, true, true, false, false, HudVisualState.Moving),
                ("犯规 > 自由球", ShotPhase.Aim, true, false, true, false, HudVisualState.Foul),
                ("自由球 > 相位", ShotPhase.Aim, false, false, true, false, HudVisualState.BallInHand),
                ("瞄准", ShotPhase.Aim, false, false, false, false, HudVisualState.Aim),
                ("蓄力仍算瞄准", ShotPhase.Charging, false, false, false, false, HudVisualState.Aim),
                ("出杆中算运动", ShotPhase.Shooting, false, false, false, false, HudVisualState.Moving),
                ("球在滚算运动", ShotPhase.Moving, false, false, false, false, HudVisualState.Moving),
                ("静止过渡算运动", ShotPhase.Settle, false, false, false, false, HudVisualState.Moving),
            };

            foreach ((string name, ShotPhase phase, bool foul, bool refused, bool hand, bool result, HudVisualState want) in priority)
            {
                HudVisualState actual = PracticeHudState.ResolveVisualState(phase, foul, refused, hand, result);
                if (actual != want)
                {
                    failures.Add($"⑤ {name}：应为 {want}，实际 {actual}。");
                }
            }

            // ⑤' **待放球**（"全场自由球"：白球入袋后由玩家点台面任意合法位置放下，`Esc` 才放回开球点）
            //     —— `awaitingPlacement` 是本轮**新增**的优先级输入。为什么单列两张：
            //     上面 9 行走的是 **5 参**调用（`awaitingPlacement` 取默认 `false`）⇒
            //     `ballInHand || awaitingPlacement` 这条分支**一行都没被跑到** ——
            //     正是本项目反复在清的"**有守卫 ≠ 守卫会跑**"（谁把 `|| awaitingPlacement` 删掉都不会红）。
            //     **注入负对照**：① 去掉 `|| awaitingPlacement` ⇒ "待放球 ⇒ 进自由球态"红；
            //                   ② 把 `resultOpen` 那一段挪到它之后 ⇒ "结算卡压过待放球"红。
            (string Case, HudVisualState Want, bool ResultOpen)[] awaitingPlacementCases =
            {
                ("待放球 ⇒ 进自由球态", HudVisualState.BallInHand, false),
                ("结算卡压过待放球（优先级不被越级）", HudVisualState.Result, true),
            };

            foreach ((string name, HudVisualState want, bool resultOpen) in awaitingPlacementCases)
            {
                HudVisualState actual = PracticeHudState.ResolveVisualState(
                    ShotPhase.Aim, false, false, false, resultOpen, awaitingPlacement: true);
                if (actual != want)
                {
                    failures.Add($"⑤' {name}：应为 {want}，实际 {actual}。");
                }
            }

            // ⑤'' **说明行文案**（`PracticeHudState.CaptionLine`，纯函数）：自由球的三个子情形
            //      —— 这三者**共用同一档状态与配色**（`ball-in-hand`），差别**只**在说明那句话，
            //      所以"哪句优先"必须被判据钉住，否则它只活在视图里、没人守。
            //      **注入负对照**：把 `CaptionLine` 里 `placementRefused` 那条挪到最后 ⇒ 用例 3 红；
            //                     删掉 `state == BallInHand && awaitingPlacement` 那条 ⇒ 用例 1 红。
            (string Case, HudVisualState State, bool Awaiting, bool Refused, string Want)[] captionCases =
            {
                ("待放球 ⇒ 提示怎么放（含 Esc 兜底）",
                    HudVisualState.BallInHand, true, false, PracticeHudState.BallInHandPendingDetail),
                ("白球已回到台上 ⇒ 只说球在台上（不谎称位置）",
                    HudVisualState.BallInHand, false, false, PracticeHudState.BallInHandDetail),
                ("落点被拒 ⇒ 说落点那句",
                    HudVisualState.BallInHand, true, true, PracticeHudState.PlacementRefusedDetail),
                ("出杆被拒 ⇒ 既有那句逐字不变",
                    HudVisualState.Moving, false, false, PracticeHudState.RefusedDetail),
            };

            foreach ((string name, HudVisualState st, bool awaiting, bool refused, string want) in captionCases)
            {
                string caption = PracticeHudState.CaptionLine(st, awaiting, refused);
                if (caption != want)
                {
                    failures.Add($"⑤'' {name}：应为 \"{want}\"，实际 \"{caption}\"。");
                }
            }

            // ⑤'' **假陈述守卫**（独立于上表）：落点被拒那一刻**台面是静止的**
            //      （正因为静止才允许放球）⇒ 此时若显示"出杆被拒"那句 `"Wait for the table"`，就是**假陈述**。
            //      单列一条是为了：将来谁把两句的顺序调换、或把 `PlacementRefusedDetail` 改成同一句，这里都会红。
            string placementRefusedCaption = PracticeHudState.CaptionLine(HudVisualState.BallInHand, true, true);
            if (placementRefusedCaption == PracticeHudState.RefusedDetail)
            {
                failures.Add("⑤'' 落点被拒显示了出杆被拒那句 `\"" + PracticeHudState.RefusedDetail
                             + "\"` ⇒ 那一刻台面是静止的，这是**假陈述**。");
            }

            // ⑤'' 落点被拒必须**压过** `moving` 档（两个拒绝窗口可能同时开着：刚被拒就又去出杆）
            //      ⇒ 玩家**刚做的那个动作**要拿到解释，不能被另一条更早的提示盖掉。
            if (PracticeHudState.CaptionLine(HudVisualState.Moving, true, true)
                != PracticeHudState.PlacementRefusedDetail)
            {
                failures.Add("⑤'' 落点被拒被 `moving` 档的说明盖掉了 ⇒ 玩家刚被拒的那一次点击没有任何解释。");
            }

            // ⑤''' **视图侧接线**（白盒文本断言，与 `T137④②` 读 `PlayViewCamera` 源码同法）
            //
            // 为什么必须白盒：`_placementRefusedUntil` 的**赋值**只活在 `PracticeHudView.Refresh()` 的轮询体里，
            // 而 EditMode 跑不到那条运行期路径 ⇒ 纯函数判据（`CaptionLine`）**只能守"哪句文案"**；
            // 删掉那行赋值 ⇒ 纯函数判据**照旧全绿**，而"落点被拒的可见反馈"会**静默失效**
            // （正是 §"绿而空"那一族：`TotalPredictSteps` 踩过的同型）。
            //
            // ⚠ **必须断言"使用形态"、不能只断言符号名**：写 `Contains("_placementRefusedUntil")`
            //    会命中**字段声明**（`PracticeHudView.cs:136`）⇒ 删掉赋值行**仍然绿**（`TotalPredictSteps` 的坑）。
            // ⚠ **刻意不用** `AssetDatabase.LoadAssetAtPath`：它会命中内存缓存造成**假 PASS**
            //    ⇒ 必须读**磁盘文本**。
            // **注入负对照（已实测）**：
            //    删掉 `_placementRefusedUntil = now + FoulBannerSeconds;` 那一行 ⇒ 本条必红；还原 ⇒ 复跑绿。
            //    **证据（可回读）**：`%TEMP%\billiards_batch\verify\INJ2.xml`，mtime **18:56:04**，
            //    `total=231 / passed=230 / failed=1`，失败名恰为 `T125_StatusContract_MapsDesignStates`。
            //    **逐条红名单**见规格 **§12.2.3**（⑤''' 行）；**本批注入总表**见 **§12.2.9**。
            //    ⚠ 注：本处早先写的是"见规格 §5 第 7 条" —— 那是**坏引用**（§5 只有 1–6 条；
            //      规格里另一个"第 7 条"在 §11，讲的是 `g6-accumulate`，与本处无关）⇒ 已改指 §12.2.3。
            string viewSource = StripComments(ReadSourceOrNull(PracticeHudViewSourcePath));
            if (string.IsNullOrEmpty(viewSource))
            {
                failures.Add("⑤''' 读不到 `PracticeHudView.cs` 的**磁盘文本** ⇒ 白盒接线守卫失效（等于没有守卫）。");
            }
            else
            {
                foreach (string usage in new[]
                         {
                             // 落点被拒的**上升沿检测**（少了它 ⇒ 窗口永远不会被打开）
                             "drive.RefusedPlacementCount > _lastPlacementRefusedSeen",
                             // 落点被拒窗口的**打开赋值**（少了它 ⇒ 窗口恒停在 0，提示永远不出现）
                             "_placementRefusedUntil = now + FoulBannerSeconds",
                         })
                {
                    if (!viewSource.Contains(usage))
                    {
                        failures.Add($"⑤''' 视图侧接线缺失：「{usage}」—— 删掉它，落点被拒的可见反馈会"
                                     + "**静默失效**，而纯函数侧的判据仍会全绿（\"有守卫 ≠ 守卫会跑\"）。");
                    }
                }
            }

            // ⑥ 犯规脉冲
            if (!Mathf.Approximately(PracticeHudState.FoulPulseSeconds, 0.8f))
            {
                failures.Add($"⑥ 犯规边线脉冲应为 0.8 s（设计说明 §9），实际 {PracticeHudState.FoulPulseSeconds:R}。");
            }

            float b0 = PracticeHudState.FoulPulseBlend(0f);
            float bHalf = PracticeHudState.FoulPulseBlend(PracticeHudState.FoulPulseSeconds * 0.5f);
            float bEnd = PracticeHudState.FoulPulseBlend(PracticeHudState.FoulPulseSeconds);
            float bAfter = PracticeHudState.FoulPulseBlend(PracticeHudState.FoulPulseSeconds * 3f);

            if (!Mathf.Approximately(b0, 0f) || !Mathf.Approximately(bEnd, 1f) || !Mathf.Approximately(bAfter, 1f))
            {
                failures.Add($"⑥ 脉冲端点应为 0 → 1 并停在 1（一次性、不循环），实际 {b0:R} / {bEnd:R} / {bAfter:R}。");
            }

            if (!(bHalf > b0 && bHalf < bEnd))
            {
                failures.Add($"⑥ 脉冲中点应严格落在两端之间，实际 {bHalf:R}。");
            }

            for (float t = 0f; t <= PracticeHudState.FoulPulseSeconds * 1.5f; t += 0.05f)
            {
                if (PracticeHudState.FoulPulseBlend(t) < PracticeHudState.FoulPulseBlend(t - 0.05f) - 1e-6f)
                {
                    failures.Add($"⑥ 脉冲在 t={t:R} 处回落（应单调不减）。");
                    break;
                }
            }

            Assert.IsEmpty(failures, "T125 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T125: 八态 token 逐字对应 ✓；不可达 {PracticeHudState.UnreachableStates.Length} 态登记一致 ✓；"
                                  + "标题/说明/图标互异 ✓；兜底键 Esc 可发现 ✓；酒红仅犯规、亮青仅指定袋 ✓；"
                                  + "优先级 9 例 + 待放球 2 例 ✓；"
                                  + "说明行 3 子情形 + 落点被拒非假陈述 ✓；脉冲 0.8 s ✓");
        }

        // ════════════════ P5-B：赛前屏（AI 对战入口）═══════════════════════════

        /// <summary>
        /// **`T144`：赛前屏（P5-B）—— 默认档 / 三档唯一选中 / 对手回合**不许静默** / 入口无假陈述**。
        ///
        /// 断言四组：
        /// ① **默认档 = `Club`**（用户拍板）**且**与 Runtime 侧默认档 `EightBallAi.DefaultDifficulty` **同源**
        ///    （两处默认值不得漂移）；
        /// ② **三档可切换 + 选中态唯一**：`Tiers` 次序 = 参考稿 `casual|club|expert` 逐项同序；
        ///    `Select` 每一档后 `Selected`/`ActiveIndex` 都对，且**恰好一格**命中 `ActiveIndex`；
        /// ③ **对手回合点击 ⇒ 可见提示**（用户要求"不许静默"）：读**可判定出口**
        ///    `MatchTurnNotice.BlockedCount` / `LastBlockedReason`（**不读像素**），并断言可见文案
        ///    走 `CaptionLine(..., opponentBlocked: true)` = `OpponentBlockedDetail`，且**不等于**常驻态说明
        ///    （否则等于"没提示"）；边沿语义（`ConsumePending` 一次 true、再一次 false）也钉住；
        /// ④ **无假陈述**：首页/赛前屏上出现的可点控件都必须**真接了动作**（白盒文本断言，读**磁盘文本**）：
        ///    `AI Match` 行可点 + 有回调、`Start match` 必须走 `MatchSeams.TryStartMatch`
        ///    （**未接线不得假装开局**）、`Back to club` 有回调、三格各挂 `Button`。
        ///
        /// **怎么打死它（注入，均已实测；① / ②' 是 QA 修复轮补的两条）**：
        /// ① 把 `EightBallAi.DefaultDifficulty` 改成 `Casual`（`p7-default-drift`）⇒ ① 的**两条独立字面量**都红；
        /// ② 把 `Tiers` 的次序调换成 `Club|Casual|Expert`（`p7-tier-order`）⇒ ② 红；
        /// ②' 把 `TierLabel(Casual)` 的返回值改成 `"Rookie"`（`p7-tier-name`）⇒ ② 的档位名/对手名**字面量**红；
        /// ③ 把 `MatchTurnNotice.RequestBlock` 改成**静默 `return`** ⇒ ③ 红（计数恒 0 = 静默无反馈）；
        /// ④ 把 `AI Match` 那行的 `primary: true` 去掉 ⇒ ④ 红（那意味着又变回压暗的假入口）。
        /// </summary>
        [Test]
        public void T144_MatchIntro_DefaultTier_TierSwitch_OpponentTurnNotice_AndNoFalseAffordance()
        {
            var failures = new List<string>();

            // ── ① 默认档 ──
            // ⚠ **QA 修复轮（A1）：两条独立的字面量期望，不是"赋进去的 == 赋进去的"**。
            //   旧断言是 `Selected == EightBallAi.DefaultDifficulty`，而 `ResetToDefault()` 的实现
            //   就是 `Selected = EightBallAi.DefaultDifficulty`（`MatchSetup.cs`）⇒ 恒真，
            //   **抓不到它声称防的"两处默认漂移"**。现在把**契约值 `Club`（用户拍板）写死在断言里**：
            //   任一处（UI 侧 `MatchSetup` / Runtime 侧 `EightBallAi.DefaultDifficulty`）被改动，都各自红。
            MatchSetup.ResetToDefault();
            if (MatchSetup.Selected != AiDifficulty.Club)
            {
                failures.Add($"T144① UI 侧默认档应为 `Club`（用户拍板），实际 {MatchSetup.Selected}。");
            }

            if (EightBallAi.DefaultDifficulty != AiDifficulty.Club)
            {
                failures.Add("T144① Runtime 侧默认档 `EightBallAi.DefaultDifficulty` 应为 `Club`"
                             + $"（两处默认值不得漂移），实际 {EightBallAi.DefaultDifficulty}。");
            }

            // ── ② 三档可切换 + 选中态唯一（次序 = 参考稿 casual|club|expert）──
            if (MatchSetup.Tiers.Length != 3
                || MatchSetup.Tiers[0] != AiDifficulty.Casual
                || MatchSetup.Tiers[1] != AiDifficulty.Club
                || MatchSetup.Tiers[2] != AiDifficulty.Expert)
            {
                failures.Add("T144② 三档次序必须为 `Casual | Club | Expert`（与参考稿 `data-home-difficulty` 逐项同序）。");
            }

            for (int i = 0; i < MatchSetup.Tiers.Length; i++)
            {
                AiDifficulty tier = MatchSetup.Tiers[i];
                MatchSetup.Select(tier);

                if (MatchSetup.Selected != tier)
                {
                    failures.Add($"T144② 选中 {tier} 后 `Selected` 应为 {tier}，实际 {MatchSetup.Selected}。");
                }

                if (MatchSetup.ActiveIndex != i)
                {
                    failures.Add($"T144② {tier} 的 `ActiveIndex` 应为 {i}，实际 {MatchSetup.ActiveIndex}。");
                }

                int hits = 0;
                for (int j = 0; j < MatchSetup.Tiers.Length; j++)
                {
                    if (MatchSetup.IndexOf(MatchSetup.Tiers[j]) == MatchSetup.ActiveIndex)
                    {
                        hits++;
                    }
                }

                if (hits != 1)
                {
                    failures.Add($"T144② 选中态必须**唯一**，实测 {hits} 格命中 `ActiveIndex`。");
                }
            }

            // 档位名与对手名：**字面量**（QA 修复轮 A2）。
            // ⚠ 旧断言写成 `OpponentName ≡ TierLabel + 后缀`，两边都读自己的实现 ⇒ 恒真；
            //   而这两串文案**短且稳定**（参考稿 `data-home-difficulty` / `<difficulty> AI`）⇒ 直接写死。
            var expectedTierNames = new (AiDifficulty Tier, string Label, string Opponent)[]
            {
                (AiDifficulty.Casual, "Casual", "Casual AI"),
                (AiDifficulty.Club, "Club", "Club AI"),
                (AiDifficulty.Expert, "Expert", "Expert AI"),
            };

            foreach ((AiDifficulty tier, string label, string opponent) in expectedTierNames)
            {
                if (PracticeHudState.TierLabel(tier) != label)
                {
                    failures.Add($"T144② {tier} 的档位名应为 \"{label}\"，"
                                 + $"实际 \"{PracticeHudState.TierLabel(tier)}\"。");
                }

                if (PracticeHudState.OpponentName(tier) != opponent)
                {
                    failures.Add($"T144② {tier} 的对手名应为 \"{opponent}\"，"
                                 + $"实际 \"{PracticeHudState.OpponentName(tier)}\"。");
                }
            }

            MatchSetup.ResetToDefault();

            // ── ③ 对手回合点击 ⇒ 可见提示（读可判定出口，不读像素）──
            MatchTurnNotice.Reset();
            if (MatchTurnNotice.HasFeedback)
            {
                failures.Add("T144③ 起点不应有反馈（`Reset` 后计数应为 0）。");
            }

            MatchTurnNotice.RequestBlock(MatchTurnNotice.ReasonOpponentTurn);

            if (MatchTurnNotice.BlockedCount != 1)
            {
                failures.Add("T144③ **不许静默**：被拒一次后 `BlockedCount` 应为 1"
                             + $"（实测 {MatchTurnNotice.BlockedCount} —— 静默实现会让它恒为 0）。");
            }

            if (MatchTurnNotice.LastBlockedReason != MatchTurnNotice.ReasonOpponentTurn)
            {
                failures.Add($"T144③ `LastBlockedReason` 应为 {MatchTurnNotice.ReasonOpponentTurn}，"
                             + $"实际 {MatchTurnNotice.LastBlockedReason}。");
            }

            if (!MatchTurnNotice.HasFeedback)
            {
                failures.Add("T144③ 被拒后 `HasFeedback` 应为 true（可判定出口就该能看出\"有反馈\"）。");
            }

            if (!MatchTurnNotice.ConsumePending())
            {
                failures.Add("T144③ 第一次 `ConsumePending()` 应为 true（视图靠它开提示窗）。");
            }

            if (MatchTurnNotice.ConsumePending())
            {
                failures.Add("T144③ 第二次 `ConsumePending()` 应为 false（**边沿**语义：重复帧不得重复弹）。");
            }

            string blockedCaption = PracticeHudState.CaptionLine(HudVisualState.AiTurn, false, false,
                opponentBlocked: true);
            if (blockedCaption != PracticeHudState.OpponentBlockedDetail)
            {
                failures.Add($"T144③ 被拒时的可见文案应为 `{PracticeHudState.OpponentBlockedDetail}`，"
                             + $"实际 `{blockedCaption}`。");
            }

            if (blockedCaption == PracticeHudState.StateDetail(HudVisualState.AiTurn))
            {
                failures.Add("T144③ 被拒提示**不得等于**常驻态说明（`AiTurnDetail`）—— 那等于\"没有新提示\"。");
            }

            MatchTurnNotice.Reset();

            // ── ④ 无假陈述：可点控件必须真接了动作（白盒文本断言，读磁盘文本）──
            string homeSource = StripComments(ReadSourceOrNull(HomeViewSourcePath));
            if (string.IsNullOrEmpty(homeSource))
            {
                failures.Add("T144④ 读不到 `HomeView.cs` 的**磁盘文本** ⇒ 白盒接线守卫失效（等于没有守卫）。");
            }
            else
            {
                foreach (string usage in new[]
                         {
                             // `AI Match`：可点 + 有回调 + **主按钮款式**（恢复参考稿的上亮下暗）
                             "true, OnStartMatch, primary: true",
                             // `Back to club`：可点 + 有回调
                             "OnBackToClub, primary: false",
                             // `Start match`：绑到开局
                             "HomeLayout.MatchIntroStartButton, OnBeginMatch",
                             // 三格各挂 `Button`（否则"三档可切换"在真机上点不动 = 假控件）
                             "button.onClick.AddListener(() => OnSelectTier(captured));",
                             // 开局必须走接缝（未接线时**不假装开局**）
                             "MatchSeams.TryStartMatch(MatchSetup.Selected)",
                         })
                {
                    if (!homeSource.Contains(usage))
                    {
                        failures.Add($"T144④ 接线缺失：「{usage}」—— 界面显示为可点却没接行为 = **假陈述**。");
                    }
                }
            }

            // ── (g) 对战 HUD：两侧剩余球 chip（**视图级**：建树后读 activeSelf 集合）──
            //  为什么必须读视图：用户要的是"进一颗消一颗"这个**行为**，只驱动纯函数等于没测。
            //  ⚠ 建树方式照 `T95b` 的先例（EditMode 下 `AddComponent` 不触发 `Awake` ⇒ 显式 `EnsureBuilt`）。
            var hudGo = new GameObject("T144HudProbe");
            try
            {
                var hud = hudGo.AddComponent<PracticeHudView>();
                hud.EnsureBuilt();

                // ① 开放球局 ⇒ 两侧零 chip
                var none = new int[0];
                hud.ApplyMatchHud(true, GroupState.OpenTable, GroupState.OpenTable, false, none, none);
                if (hud.VisibleGroupChips(true).Length != 0 || hud.VisibleGroupChips(false).Length != 0)
                {
                    failures.Add("T144(g) 开放球局时两侧球号 chip 必须**全不可见**（用户：一开始是空的）。");
                }

                // ④ 对战模式 ⇒ 练习那排（`POCKETED` / `BALLS LEFT`）不可见
                if (hud.PracticeBallRowVisible)
                {
                    failures.Add("T144(g) 对战模式下 `BALLS LEFT` + 15 颗球行必须不可见（两种模式 UI 要不一样）。");
                }

                // ② 定组后 ⇒ 玩家侧可见号码集合 == 传入集合（顺序无关）
                int[] yours = { 1, 3, 5 };
                int[] opp = { 10, 12 };
                hud.ApplyMatchHud(true, GroupState.Solid, GroupState.Stripe, false, yours, opp);
                if (!SameNumberSet(hud.VisibleGroupChips(true), yours)
                    || !SameNumberSet(hud.VisibleGroupChips(false), opp))
                {
                    failures.Add("T144(g) 定组后两侧可见号码必须等于 `*RemainingNumbers`，实测 我方 ["
                                 + string.Join(",", hud.VisibleGroupChips(true)) + "] / 对手 ["
                                 + string.Join(",", hud.VisibleGroupChips(false)) + "]。");
                }

                // ③ 推进一杆打掉 3 号 ⇒ 只有 3 号消失，其余不变
                int[] after = { 1, 5 };
                hud.ApplyMatchHud(true, GroupState.Solid, GroupState.Stripe, false, after, opp);
                int[] shownAfter = hud.VisibleGroupChips(true);
                // ⚠ **同时断言"可见个数"**（只数 `activeSelf`、不读缓存）：这是 P11 注入 `p11-hud-stale`
                //   暴露的教训 —— 原判据只看"缓存里的号码集合"，而缓存与可见性**同源更新** ⇒
                //   "只增不减"的注入被视为正常。个数断言能把它逼出来（3 vs 2）。
                if (System.Array.IndexOf(shownAfter, 3) >= 0
                    || !SameNumberSet(shownAfter, after)
                    || !SameNumberSet(hud.VisibleGroupChips(false), opp)
                    || hud.VisibleChipCount(true) != after.Length
                    || hud.VisibleChipCount(false) != opp.Length)
                {
                    failures.Add("T144(g) 进一颗必须**只**消对应那颗（3 号）：期望 我方 "
                                 + after.Length + " 个 / 对手 " + opp.Length + " 个，实测 我方 "
                                 + hud.VisibleChipCount(true) + " 个 [" + string.Join(",", shownAfter)
                                 + "] / 对手 " + hud.VisibleChipCount(false) + " 个。");
                }

                // ⑤ 练习模式 ⇒ 球行恢复可见、两侧 chip 全隐藏（防"顺手改坏"）
                hud.ApplyMatchHud(false, GroupState.OpenTable, GroupState.OpenTable, false, none, none);
                if (!hud.PracticeBallRowVisible)
                {
                    failures.Add("T144(g) 练习模式下 `BALLS LEFT` + 球行必须**仍然可见**。");
                }

                if (hud.VisibleGroupChips(true).Length != 0 || hud.VisibleGroupChips(false).Length != 0)
                {
                    failures.Add("T144(g) 练习模式下两侧对战球号 chip 必须全隐藏。");
                }

                // ⑥ 开放球局 ⇒ **组别小标签也不可见**（零字形；不只是 chip）
                hud.ApplyMatchHud(true, GroupState.OpenTable, GroupState.OpenTable, false, none, none);
                if (!string.IsNullOrEmpty(hud.YourGroupTagText) || !string.IsNullOrEmpty(hud.OppGroupTagText))
                {
                    failures.Add("T144(g) 开放球局时组别小标签必须是**空串**（用户：一开始是空的，不写待定）。");
                }

                // ⑦ 定组后 ⇒ 标签 == `Solids`/`Stripes`；⑦' 中栏 = `RACK 1` + 轮次（真信息）
                hud.ApplyMatchHud(true, GroupState.Solid, GroupState.Stripe, true, yours, opp);
                if (hud.YourGroupTagText != PracticeHudState.SolidGroupTag
                    || hud.OppGroupTagText != PracticeHudState.StripeGroupTag)
                {
                    failures.Add("T144(g) 定组后组别标签应为 `Solids`/`Stripes`，实测 "
                                 + hud.YourGroupTagText + " / " + hud.OppGroupTagText + "。");
                }

                if (hud.CenterRackText != PracticeHudState.RackLabel
                    || hud.CenterTurnText != PracticeHudState.OpponentTurnLabel)
                {
                    failures.Add("T144(g) 中栏应显示 `RACK 1` + 当前轮次（AI 回合 = `AI`），实测 "
                                 + hud.CenterRackText + " / " + hud.CenterTurnText + "。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hudGo);
            }

                // 这里另建一个探针（⑧ 之后已不在原来的 try 内）—— 与前面同一条建树路径。
                var rectGo = new GameObject("T144RectProbe");
                var hudForRect = rectGo.AddComponent<PracticeHudView>();
                hudForRect.EnsureBuilt();
                hudForRect.ApplyMatchHud(true, GroupState.Solid, GroupState.Stripe, false,
                    new[] { 1, 3, 5 }, new[] { 10, 12 });

                // **版式判据必须在参考分辨率下量**：`EnsureBuilt` 建的 Canvas 默认不是 1920×1080
                // （实测整条 bar 只有 ~200 单位宽 ⇒ 46 宽的头像占 23%，会造出**生产中不存在**的重叠）。
                Canvas judgeCanvas = hudForRect.GetComponentInChildren<Canvas>(true);
                if (judgeCanvas != null)
                {
                    var judgeScaler = judgeCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                    judgeScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    judgeScaler.scaleFactor = 1f;
                    ((RectTransform)judgeCanvas.transform).sizeDelta = new Vector2(1920f, 1080f);
                    Canvas.ForceUpdateCanvases();
                }


                // ── ⑨ 前提自证：必须在 **1920×1080** 下量版式 ──
                if (judgeCanvas != null)
                {
                    Vector2 canvasSize = ((RectTransform)judgeCanvas.transform).rect.size;
                    TestContext.WriteLine($"T144canvas {canvasSize.x:F1}x{canvasSize.y:F1}");
                    if (Mathf.Abs(canvasSize.x - 1920f) > 0.5f || Mathf.Abs(canvasSize.y - 1080f) > 0.5f)
                    {
                        failures.Add($"⑨ 前提不成立：版式判据必须在参考分辨率 1920×1080 下量，实测 "
                                     + $"{canvasSize.x:F1}×{canvasSize.y:F1} ⇒ 量出来的重叠可能是**假阳性**。");
                    }
                }

                // ── ⑨ 不重叠（**同一块内**的三行两两）+ ⑩ 名字单行 + 打印各块 rect ──

                RectTransform board = hudForRect.ScoreboardForJudge;
                string[] leftNames = { "Avatar", "PlayerName", "PlayerGroupTag", "YourGroupChips" };
                string[] rightNames = { "OppGroupChips", "OppName", "OppGroupTag", "OppAvatar" };
                foreach (string[] block in new[] { leftNames, rightNames })
                {
                    for (int a = 0; a < block.Length; a++)
                    {
                        for (int b = a + 1; b < block.Length; b++)
                        {
                            RectTransform ra = FindDeep(board, block[a]);
                            RectTransform rb = FindDeep(board, block[b]);
                            if (ra == null || rb == null || !ra.gameObject.activeSelf || !rb.gameObject.activeSelf)
                            {
                                continue;
                            }

                            if (WorldRect(ra).Overlaps(WorldRect(rb)))
                            {
                                failures.Add($"T144(g) ⑨ 「{block[a]}」与「{block[b]}」的矩形**相交**（同一块内不许叠）。");
                            }
                        }
                    }
                }

                // ⑩ 名字必须**单行**（关了换行还不够：还要看实际单行高）
                foreach (string nameNode in new[] { "PlayerName", "OppName" })
                {
                    var text = FindDeep(board, nameNode)?.GetComponent<TMPro.TextMeshProUGUI>();
                    if (text == null)
                    {
                        continue;
                    }

                    if (text.enableWordWrapping)
                    {
                        failures.Add($"T144(g) ⑩ 「{nameNode}」仍开启自动换行（实机截图里被折成两行）。");
                    }

                    if (text.preferredHeight > text.fontSize * 1.35f)
                    {
                        failures.Add($"T144(g) ⑩ 「{nameNode}」的 preferredHeight={text.preferredHeight:F1} 明显超过单行高"
                                     + $"（fontSize={text.fontSize:F1}）⇒ 实际已被折行。");
                    }
                }

                // **供 team-lead 判版式**：把各块的实际 rect 打出来（不参与断言）
                foreach (string node in new[] { "Avatar", "PlayerName", "PlayerGroupTag", "YourGroupChips",
                                                "ScoreLabel", "ScoreValue", "OppGroupChips", "OppName",
                                                "OppGroupTag", "OppAvatar" })
                {
                    RectTransform r = FindDeep(board, node);
                    if (r == null)
                    {
                        TestContext.WriteLine($"T144rect {node}: <不存在>");
                        continue;
                    }

                    Rect w = WorldRect(r);
                    TestContext.WriteLine($"T144rect {node}: active={r.gameObject.activeSelf} "
                                          + $"x={w.x:F1} y={w.y:F1} w={w.width:F1} h={w.height:F1}");
                }

                UnityEngine.Object.DestroyImmediate(rectGo);

            // ⑧ **防假陈述守卫**：参考图里那三样在本工程**没有接线**（一局定胜负 / D3 已撤销 / 局中切模式），
            //   视图源码里一律不许出现（这是"防止照抄参考图"的守卫，不是文案测试）。
            foreach (string forbidden in new[] { "BEST OF THREE", "of 3", "Call a pocket" })
            {
                string hudSource = StripComments(ReadSourceOrNull(PracticeHudViewSourcePath));
                if (hudSource != null && hudSource.Contains(forbidden))
                {
                    failures.Add($"T144(g) 视图里出现「{forbidden}」—— 参考图有、本工程**未接线**（假陈述）。");
                }
            }

            Assert.IsEmpty(failures, "T144 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T144: 默认档 Club（与 Runtime 同源）✓；三档切换且选中唯一 ✓；"
                                  + "对手回合被拒有可见提示 + 可判定出口（计数/原因/边沿）✓；"
                                  + "入口接线无假陈述（AI Match 可点、Start match 走接缝、Back 有回调、三格挂 Button）✓");
        }

        private static bool InFamily(Color32[] family, Color32 color)
        {
            for (int i = 0; i < family.Length; i++)
            {
                if (family[i].r == color.r && family[i].g == color.g
                    && family[i].b == color.b && family[i].a == color.a)
                {
                    return true;
                }
            }

            return false;
        }

        // ════════════════ T126 犯规环 / 结算环 ═════════════════════════════════

        /// <summary>
        /// **`T126`：补的两类反馈（犯规环 / 结算环）时长与配色合设计，且槽预算不溢出**。
        ///
        /// 断言：
        /// ① 犯规环 **0.38 s**、酒红（与 `ClubUiTokens.FoulEdge` **逐字节相同** —— 界面与球上的
        ///    犯规色只能是同一个值）；
        /// ② 落袋环 **0.29 s**、黑八延长 **0.45 s**、结算环 **0.45 s**；
        /// ③ 结算环比落袋外环**更细**（半径更小、峰值更淡）—— 否则两环糊成一块，看不出"结算"这层信息；
        /// ④ 槽预算：`SlotCapacity`(48) ≥ 最坏并发（含犯规环 + 结算环），顶点上界 = 48 × 38；
        /// ⑤ **源码级**：`ScratchCount` 增量确实生成了犯规环、黑八落袋确实生成了结算环
        ///    （EditMode 跑不到运行期触发路径 ⇒ 这是本项唯一可机械化的形态）。
        ///
        /// **注入负对照**：把犯规环时长改 0.6 ⇒ ① 红；把犯规色改黄铜 ⇒ ① 红；
        /// 删掉 `SpawnResultRing()` 的调用 ⇒ ⑤ 红。
        /// </summary>
        [Test]
        public void T126_FoulAndResultFeedback_MatchDesign()
        {
            var failures = new List<string>();

            // ① 犯规环
            if (!Mathf.Approximately(PoolVfxView.FoulRingDurationSeconds, 0.38f)
                || !Mathf.Approximately(PoolVfxView.FoulRingLayer.Duration, 0.38f))
            {
                failures.Add($"① 犯规环应为 0.38 s（设计说明 §VFX），实际 常量 {PoolVfxView.FoulRingDurationSeconds:R} / "
                             + $"层 {PoolVfxView.FoulRingLayer.Duration:R}。");
            }

            if (PoolVfxView.FoulRingLayer.Name != "foul.ring")
            {
                failures.Add($"① 犯规环的层名应为 \"foul.ring\"，实际 \"{PoolVfxView.FoulRingLayer.Name}\"。");
            }

            Color32 foulRing = PoolVfxView.FoulRingColor;
            Color32 foulToken = ClubUiTokens.FoulEdge;
            if (foulRing.r != foulToken.r || foulRing.g != foulToken.g || foulRing.b != foulToken.b)
            {
                failures.Add($"① 犯规环色 {ClubUiTokens.ToHexRgb(foulRing)} ≠ 设计犯规色 "
                             + $"{ClubUiTokens.ToHexRgb(foulToken)}（同一个设计色必须在界面与 VFX 两处一致）。");
            }

            if (InFamily(ClubUiTokens.CyanFamily, foulRing))
            {
                failures.Add("① 犯规环用了亮青 —— 亮青只许给指定球袋。");
            }

            // ② 落袋 / 黑八 / 结算
            if (!Mathf.Approximately(PoolVfxView.PocketRingDurationSeconds, 0.29f)
                || !Mathf.Approximately(PoolVfxView.PocketOuterLayer.Duration, 0.29f))
            {
                failures.Add($"② 落袋环应为 0.29 s（设计说明 §VFX），实际 常量 {PoolVfxView.PocketRingDurationSeconds:R} / "
                             + $"层 {PoolVfxView.PocketOuterLayer.Duration:R}。");
            }

            if (!Mathf.Approximately(PoolVfxView.EightBallPocketDurationSeconds, 0.45f))
            {
                failures.Add($"② 黑八落袋应延长到 0.45 s，实际 {PoolVfxView.EightBallPocketDurationSeconds:R}。");
            }

            if (!Mathf.Approximately(PoolVfxView.ResultRingLayer.Duration, 0.45f))
            {
                failures.Add($"② 结算环应为 0.45 s（与黑八落袋同一次事件），实际 {PoolVfxView.ResultRingLayer.Duration:R}。");
            }

            if (PoolVfxView.EightBallPocketDurationSeconds <= PoolVfxView.PocketRingDurationSeconds)
            {
                failures.Add("② 黑八的时长必须**长于**普通落袋（否则\"延长\"没有发生）。");
            }

            // ③ 结算环更细
            if (!(PoolVfxView.ResultRingLayer.Radius < PoolVfxView.PocketOuterLayer.Radius))
            {
                failures.Add($"③ 结算环半径 {PoolVfxView.ResultRingLayer.Radius:R} 未小于落袋外环 "
                             + $"{PoolVfxView.PocketOuterLayer.Radius:R} ⇒ 两环糊成一块。");
            }

            if (!(PoolVfxView.ResultRingLayer.PeakAlpha < PoolVfxView.PocketOuterLayer.PeakAlpha))
            {
                failures.Add("③ 结算环峰值不透明度未低于落袋外环 ⇒ 观感上分不出\"细环\"。");
            }

            if (PoolVfxView.EightBallIndex != 8)
            {
                failures.Add($"② 八号球的球表索引应为 8（0 = 白球），实际 {PoolVfxView.EightBallIndex}。");
            }

            // ④ 槽预算
            if (PoolVfxView.SlotCapacity != 48)
            {
                failures.Add($"④ 槽容量应为设计稿的 48，实际 {PoolVfxView.SlotCapacity}。");
            }

            int worst = PoolVfxView.MaxConcurrentSlots();
            if (worst > PoolVfxView.SlotCapacity)
            {
                failures.Add($"④ 最坏并发 {worst} 槽 > 容量 {PoolVfxView.SlotCapacity}"
                             + " ⇒ 溢出只会静默丢特效（实机表现为\"有时候没特效\"）。");
            }

            int expectVerts = 48 * (32 + 3 * 2);
            if (PoolVfxView.MaxVerticesPerFrame() != expectVerts)
            {
                failures.Add($"④ 单帧顶点上界应为 {expectVerts}（48 × 38），实际 {PoolVfxView.MaxVerticesPerFrame()}。");
            }

            // ⑤ 源码级触发链
            string vfxSource = StripComments(ReadSourceOrNull("Assets/_Project/Scripts/Runtime/Physics/PoolVfxView.cs"));
            if (vfxSource == null)
            {
                failures.Add("⑤ 读不到 PoolVfxView 源码（源码守卫失效）。");
            }
            else
            {
                if (!Regex.IsMatch(vfxSource, @"drive\.ScratchCount\s*>\s*_lastScratch"))
                {
                    failures.Add("⑤ 犯规环没有由 `ScratchCount` 的上升沿驱动 ⇒ 白球落袋时不会出环。");
                }

                if (!Regex.IsMatch(vfxSource, @"SpawnFoulRing\s*\(\s*\)"))
                {
                    failures.Add("⑤ 找不到 SpawnFoulRing 的调用点。");
                }

                if (!Regex.IsMatch(vfxSource, @"SpawnResultRing\s*\(\s*\)"))
                {
                    failures.Add("⑤ 找不到 SpawnResultRing 的调用点（黑八落袋不会出结算环）。");
                }

                if (!Regex.IsMatch(vfxSource, @"_lastPocketWasEight"))
                {
                    failures.Add("⑤ 结算环没有接到\"落袋的是黑八\"这个判据上。");
                }
            }

            Assert.IsEmpty(failures, "T126 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T126: 犯规环 {PoolVfxView.FoulRingLayer.Duration:R}s（{ClubUiTokens.ToHexRgb(PoolVfxView.FoulRingColor)}）✓；"
                                  + $"落袋 {PoolVfxView.PocketOuterLayer.Duration:R}s / 黑八+结算 {PoolVfxView.EightBallPocketDurationSeconds:R}s ✓；"
                                  + $"最坏并发 {worst} ≤ {PoolVfxView.SlotCapacity} 槽、顶点上界 {expectVerts} ✓；触发链源码级 ✓");
        }

        // ════════════════ T146 P6：一局定胜负的胜/负结算 + 流程收口 ══════════════

        /// <summary>`PhysicsTestDrive` 结算出口的驱动夹具要用的资产路径（与 `T145` 同一份）。</summary>
        private const string DriveTableConfigPath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>`PracticeResultView` 的源码路径（`T146` 白盒接线断言用；**从 <see cref="ViewSourceFiles"/> 取**，不另写字面量）。</summary>
        private static readonly string PracticeResultViewSourcePath = ViewSourceFiles[1];

        /// <summary>`MatchResultCopy` 的源码路径（`T146(b)` 的"配色单一真源"断言用）。</summary>
        private const string MatchResultCopySourcePath =
            "Assets/_Project/Scripts/Runtime/UI/MatchResultCopy.cs";

        /// <summary>`PracticeHudLayout` 的源码路径（`T148(d)` 的"字号倍率单点"断言用）。</summary>
        private const string PracticeHudLayoutSourcePath =
            "Assets/_Project/Scripts/Runtime/UI/PracticeHudLayout.cs";

        private const string PointerAimControllerSourcePath =
            "Assets/_Project/Scripts/Runtime/Physics/PointerAimController.cs";

        private const string PointerPowerControllerSourcePath =
            "Assets/_Project/Scripts/Runtime/Physics/PointerPowerController.cs";

        /// <summary>
        /// **`T146`：一局定胜负的结算卡 —— 两形态**必须真的不同**、结算出口**以 Runtime 为准**、
        /// 三条流程收口**逐条可判定**、界面无假陈述**。
        ///
        /// ── 断言八组 ────────────────────────────────────────────────────────────
        /// **(a) 形态映射**：`ShotTurn.Player ⇒ Win`、`ShotTurn.Ai ⇒ Lose`，且**穷举** `ShotTurn`
        ///     （新增枚举值不得落到 `None` = 悄悄没有形态）。
        /// **(b) 两形态的关键差异项真的不同**：标题不同 + 徽章不同（**两处独立文案**）+ 四个配色 accessor
        ///     逐对**不同**、负局那一侧必须落**酒红族**、胜局那一侧不得借酒红，
        ///     **且配色只有单一真源**（源码级：视图四个场合都经 `MatchResultCopy.<Xxx>Color(win)`、
        ///     四个 accessor 各自引具名令牌）。⚠ 这一组是"两形态不是只差一个词"的**机器形态**。
        /// **(c) "为什么"一行穷举**：逐 `ShotOutcomeKind` 值都要有**非空**且**互不相同**的原因；
        ///     且"能成为结算原因"的集合**恰好**是那 4 种结局杆（新增枚举值 ⇒ 这里必红）。
        /// **(d) 三条流程收口**：① 面板打开 ⇒ 出杆输入让位（**复用既有 `IsOpen` 单点**，源码级）；
        ///     ② 面板打开 ⇒ **"对手回合被拒"提示不得触发**（纯函数 + 视图侧接线）；
        ///     ③ **关掉卡后不自动进下一局** —— 由 Runtime 的出口**行为**证明：消费掉"待展示"之后
        ///     `MatchOver` **仍为真**，且**只有显式 `StartMatch`** 才把它清回假。
        /// **(e) 无假陈述**：`Play again` 真的走 `MatchSeams`、`Back to club` 真的走 `HomeSeams`
        ///     （未接线时 `TryShow()` 返回 false）、次键标签随形态改写（**显示与行为一致**）、
        ///     赛前屏不得再写"三局两胜"（本工程是一局定胜负）。
        /// **(f) "只弹一次"以 Runtime 闸门为准 + 结算卡的白盒接线**：`drive.TryConsumeSettlement()`、
        ///     `MatchResultCopy.FormFor(drive.Winner)`、次键标签改写、`CurrentForm` 出口。
        ///     （`drive.EndReason` 已**不再**接线：用户要求删掉结算卡的"解释行"⇒ `T146(f)` 对应断言作废。）
        /// **(g) 结算边界记一局**：记录与"导出按钮"**解耦** —— 历史 **+1 且只 +1 且累积**
        ///     （旧口径把两者绑在按钮上、且从空历史起算 ⇒ **AI 对局永不写历史 + 会把累积历史覆盖成单条**
        ///     = 首页三项统计静默做死）；"一次结算恰好一条"由**记录口在结算卡里只出现 2 次**（声明 1 + 调用 1）钉住。
        /// **(h) `ai-turn` 真接线**：`PracticeHudView` 必须把 `drive.IsAiTurn` 喂给状态求解
        ///     （少了它，对手回合期间状态条回落成 `aim` ⇒ 嘴上说"轮到你了" = 假陈述）。
        ///
        /// **怎么打死它（注入，均已实测）**：① 把 `MatchResultCopy.FormFor` 的两个返回值**互换** ⇒ (a) 红；
        /// ② 把 `PointerPowerController` 的 `PracticeResultView.IsOpen` 从让位门里**删掉** ⇒ (d①) 红；
        /// ③ 把结算卡的"弹一次"闸门**去掉**（不再问 `TryConsumeSettlement`）⇒ (f) 红；
        /// ④ 把**结算边界的记录调用删掉** ⇒ (f)(g) 红；
        /// ⑤ 把记录口改成"从空历史起算"（复现覆盖缺陷）⇒ (g) 红（累积那条）；
        /// ⑥ 把 `ResolveVisualState` 的 `aiTurn` 那一支删掉 ⇒ `T125②'` 红（登记为可达却产出不了）；
        /// ⑦ 把视图顶部色条绕过 accessor 直接引令牌（`p7-color-single-source`）⇒ (b) **单一真源**红；
        /// ⑧ 把 `MatchResultCopy.FormFor` 改成无条件返回 `Win`（`p7-win-cause`）⇒ (a) + (c) 红。
        ///
        /// ── 盲带（如实）────────────────────────────────────────────────────────
        /// · **不判"两个面板不会同时出现"** —— 单卡结构下那是**恒真**（同一棵树只有一个 `_card`），
        ///   写出来等于没有证据；本族把火力放在"**能被改坏的那一刀**"（形态映射 / 让位 / 闸门）。
        /// · 真实鼠标点击的端到端属手动验收（与 `T102` 同口径）：判据只锁**行为出口**与**白盒接线**。
        /// </summary>
        [Test]
        public void T146_MatchResult_TwoForms_FlowClosure_AndNoFalseAffordance()
        {
            var failures = new List<string>();

            // ── (a) 形态映射（穷举）──
            if (MatchResultCopy.FormFor(ShotTurn.Player) != MatchResultForm.Win)
            {
                failures.Add("T146(a) 玩家胜 ⇒ 形态应为 `Win`，实际 "
                             + $"{MatchResultCopy.FormFor(ShotTurn.Player)}。");
            }

            if (MatchResultCopy.FormFor(ShotTurn.Ai) != MatchResultForm.Lose)
            {
                failures.Add("T146(a) 对手胜 ⇒ 形态应为 `Lose`，实际 "
                             + $"{MatchResultCopy.FormFor(ShotTurn.Ai)}。");
            }

            foreach (ShotTurn turn in (ShotTurn[])System.Enum.GetValues(typeof(ShotTurn)))
            {
                MatchResultForm form = MatchResultCopy.FormFor(turn);
                if (form != MatchResultForm.Win && form != MatchResultForm.Lose)
                {
                    failures.Add($"T146(a) `ShotTurn.{turn}` 没有对应形态（得到 {form}）"
                                 + " ⇒ 新增的轮次方在结算卡上没有形态。");
                }
            }

            if (!MatchResultCopy.IsWin(MatchResultForm.Win) || MatchResultCopy.IsWin(MatchResultForm.Lose)
                || MatchResultCopy.IsWin(MatchResultForm.None))
            {
                failures.Add("T146(a) `IsWin` 只对 `Win` 为真（`Lose` / `None` 都应为假）。");
            }

            // ── (b) 两形态的关键差异项真的不同 ──
            string titleWin = MatchResultCopy.Title(MatchResultForm.Win);
            string titleLose = MatchResultCopy.Title(MatchResultForm.Lose);
            string badgeWin = MatchResultCopy.Badge(MatchResultForm.Win);
            string badgeLose = MatchResultCopy.Badge(MatchResultForm.Lose);

            if (string.IsNullOrWhiteSpace(titleWin) || string.IsNullOrWhiteSpace(titleLose))
            {
                failures.Add("T146(b) 两形态的标题都必须非空。");
            }

            if (titleWin == titleLose)
            {
                failures.Add($"T146(b) 两形态的标题相同（\"{titleWin}\"）⇒ 玩家看不出输赢。");
            }

            if (string.IsNullOrWhiteSpace(badgeWin) || string.IsNullOrWhiteSpace(badgeLose) || badgeWin == badgeLose)
            {
                failures.Add($"T146(b) 两形态的徽章必须**非空且不同**，实际 \"{badgeWin}\" / \"{badgeLose}\""
                             + "（派单：两形态要在**标题 + 配色 + 文案**三处都不同，不是只差一个词）。");
            }

            // 两形态**都不得**复用练习局的既有句子（否则"比赛结算"与"练习清台"在卡上无法区分）
            foreach (string practiceCopy in new[]
                     {
                         PracticeSession.TitleTableCleared, PracticeSession.TitleRackEnded,
                         PracticeSession.ReasonTableCleared, PracticeSession.ReasonRackEnded,
                         MatchResultCopy.BadgePractice,
                     })
            {
                if (titleWin == practiceCopy || titleLose == practiceCopy
                    || badgeWin == practiceCopy || badgeLose == practiceCopy)
                {
                    failures.Add($"T146(b) 比赛结算复用了练习局的文案 \"{practiceCopy}\""
                                 + " ⇒ 两种来源在卡上分不出来。");
                }
            }

            // ── 四对配色：**两形态不同 + 负局落酒红族 + 单一真源**（QA 修复轮 A3）──
            // ⚠ 旧断言是 `MatchResultCopy.EdgeColor(true) == ClubUiTokens.ResultWinEdge` —— 期望值取的
            //   正是 accessor 返回的**同一个令牌** ⇒ **X == X（恒真）**。
            // ⚠ **不许把颜色 hex 写进测试**（那会造第二真源；"令牌 ↔ 设计稿"由 `T125④` 负责）。
            //   ⇒ 改成**源码级"只认单一真源"**：视图四个场合都必须经 `MatchResultCopy.<Xxx>Color(win)`；
            //     四个 accessor 必须各自引**具名令牌**（`ResultWinEdge` / `ResultLoseEdge` / …）。
            //   并**保留** `win != lose`（两形态真不同）与 `ClaretFamily`（酒红只给失败）两条**行为**断言。
            var colorPairs = new (string Name, Color32 Win, Color32 Lose)[]
            {
                ("顶部色条", MatchResultCopy.EdgeColor(true), MatchResultCopy.EdgeColor(false)),
                ("徽章图标", MatchResultCopy.IconColor(true), MatchResultCopy.IconColor(false)),
                ("徽章环", MatchResultCopy.IconEdgeColor(true), MatchResultCopy.IconEdgeColor(false)),
                ("徽章 chip 边线", MatchResultCopy.BadgeEdgeColor(true), MatchResultCopy.BadgeEdgeColor(false)),
            };

            foreach ((string name, Color32 win, Color32 lose) in colorPairs)
            {
                if (Same(win, lose))
                {
                    failures.Add($"T146(b) {name}：两形态用了同一个色 ⇒ 形态只差在文字上。");
                }

                if (!InFamily(ClubUiTokens.ClaretFamily, lose))
                {
                    failures.Add($"T146(b) {name}：负局必须落在**酒红族**（设计说明 §一：酒红只用于犯规和失败）。");
                }

                if (InFamily(ClubUiTokens.ClaretFamily, win))
                {
                    failures.Add($"T146(b) {name}：胜局借用了酒红 —— 酒红只许给犯规与失败。");
                }
            }

            // 单一真源（源码级）。⚠ **盲带（如实）**：这里**不判**"哪个 accessor 对应哪个视觉部位"
            // （那要读像素/读控件）⇒ 只判"**没有第二个真源**"：视图不自己二选一、accessor 引具名令牌。
            string resultViewSourceForColor = StripComments(ReadSourceOrNull(PracticeResultViewSourcePath));
            string copySourceForColor = StripComments(ReadSourceOrNull(MatchResultCopySourcePath));

            if (string.IsNullOrEmpty(resultViewSourceForColor) || string.IsNullOrEmpty(copySourceForColor))
            {
                failures.Add("T146(b) 读不到 `PracticeResultView.cs` / `MatchResultCopy.cs` 的磁盘文本"
                             + " ⇒ 配色单一真源守卫失效。");
            }
            else
            {
                foreach (string call in new[]
                         {
                             "MatchResultCopy.EdgeColor(win)", "MatchResultCopy.IconColor(win)",
                             "MatchResultCopy.IconEdgeColor(win)", "MatchResultCopy.BadgeEdgeColor(win)",
                         })
                {
                    if (!resultViewSourceForColor.Contains(call))
                    {
                        failures.Add($"T146(b) 视图没有经 `{call}` 取色 ⇒ 配色出现了**第二个真源**"
                                     + "（改令牌会漏改视图）。");
                    }
                }

                foreach (string token in new[]
                         {
                             "ClubUiTokens.ResultWinEdge", "ClubUiTokens.ResultLoseEdge", "ClubUiTokens.Accent",
                             "ClubUiTokens.ResultLoseIcon", "ClubUiTokens.ResultLoseIconEdge",
                             "ClubUiTokens.LineBadge", "ClubUiTokens.ResultLoseBadgeEdge",
                         })
                {
                    if (!copySourceForColor.Contains(token))
                    {
                        failures.Add($"T146(b) `MatchResultCopy` 里找不到具名令牌 `{token}`"
                                     + " ⇒ 形态配色不再来自令牌表。");
                    }
                }
            }

            // ── (d) 三条流程收口 ──

            // ① 面板打开 ⇒ 出杆输入让位（**复用既有单点**：两个输入控制器都读 `PracticeResultView.IsOpen`）
            foreach (string path in new[] { PointerAimControllerSourcePath, PointerPowerControllerSourcePath })
            {
                string gate = StripComments(ReadSourceOrNull(path));
                if (string.IsNullOrEmpty(gate))
                {
                    failures.Add($"T146(d①) 读不到 {Path.GetFileName(path)} 的磁盘文本 ⇒ 输入让位守卫失效。");
                }
                else if (!gate.Contains("PracticeResultView.IsOpen"))
                {
                    failures.Add($"T146(d①) {Path.GetFileName(path)} 的输入让位门里找不到 `PracticeResultView.IsOpen`"
                                 + " ⇒ 结算卡打开期间仍能瞄准/蓄力出杆，会把已结束的一局打乱。");
                }
            }

            // ② 面板打开 ⇒ "对手回合被拒"提示不得触发（纯函数优先级 + 视图侧接线）
            if (PracticeHudState.OpponentNoticeAllowed(true))
            {
                failures.Add("T146(d②) 结算面板打开时**不得**触发\"对手回合被拒\"提示"
                             + "（那一刻出杆已被整体让位，两套反馈只会互相打架）。");
            }

            if (!PracticeHudState.OpponentNoticeAllowed(false))
            {
                failures.Add("T146(d②) 面板没开时对手回合提示必须放行 —— 否则 P5-B 的\"不许静默\"被这条优先级吃掉。");
            }

            string hudSource = StripComments(ReadSourceOrNull(PracticeHudViewSourcePath));
            if (string.IsNullOrEmpty(hudSource))
            {
                failures.Add("T146(d②) 读不到 `PracticeHudView.cs` 的磁盘文本 ⇒ 优先级接线守卫失效。");
            }
            else
            {
                if (!hudSource.Contains("PracticeHudState.OpponentNoticeAllowed(PracticeResultView.IsOpen)"))
                {
                    failures.Add("T146(d②) `PracticeHudView` 里没有把 `OpponentNoticeAllowed(PracticeResultView.IsOpen)`"
                                 + " 用在消费口上 ⇒ 纯函数只是声明了、没人用（"
                                 + "\"有守卫 ≠ 守卫会跑\"）。");
                }

                // (h) `ai-turn` 的**真接线**：喂的是 `PhysicsTestDrive.IsAiTurn`，不是从球位/相位反推。
                //     ⇒ 少了这一参，对手回合期间状态条会回落成 `aim`（"轮到你了"= 假陈述）。
                // ⚠ **必须断整行赋值**，不能只 `Contains("drive.IsAiTurn")`：P6 第 4 小段之后
                //   `drive.IsAiTurn` 在视图里**出现两次**（这里 + `callPocket` 那行）⇒ 只查子串的话，
                //   把这一行改成恒真仍会绿（实测踩过：注入 `aiTurn = drive != null` 时全绿）。
                if (!hudSource.Contains("bool aiTurn = drive != null && drive.IsAiTurn;"))
                {
                    failures.Add("T146(h) `PracticeHudView` 没有把 `drive.IsAiTurn` 喂给状态求解"
                                 + "（要求整行 `bool aiTurn = drive != null && drive.IsAiTurn;`）"
                                 + " ⇒ 对手回合期间状态条回落成 `aim`，或轮次判定被改成恒真。");
                }
            }

            // ③ 关掉卡后不自动进下一局（**行为**：Runtime 出口）
            PhysicsTestDrive drive = null;
            GameObject driveGo = null;
            IMatchHost previousHost = MatchSeams.Host;
            try
            {
                driveGo = new GameObject("T146Drive");
                drive = driveGo.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = AssetDatabase.LoadAssetAtPath<TableConfig>(DriveTableConfigPath);

                if (drive.tableConfig == null)
                {
                    failures.Add($"T146(d③) 载不到 {DriveTableConfigPath} ⇒ 结算出口无法驱动（判据失效）。");
                }
                else
                {
                    drive.Initialize();
                    drive.StartMatch(AiDifficulty.Club);

                    if (drive.MatchOver)
                    {
                        failures.Add("T146(d③) 新开一局时 `MatchOver` 必须为 false。");
                    }

                    if (drive.TryConsumeSettlement())
                    {
                        failures.Add("T146(d③) 新开一局不得带着上一局的\"待展示\"（否则新局一开局就弹结算卡）。");
                    }

                    drive.ApplyVerdict(new RuleVerdict
                    {
                        gameOver = true,
                        winner = ShotTurn.Player,
                        nextTurn = ShotTurn.Player,
                        outcome = ShotOutcomeKind.LegalEightBallPotted,
                    });

                    if (!drive.MatchOver || drive.Winner != ShotTurn.Player
                        || drive.EndReason != ShotOutcomeKind.LegalEightBallPotted)
                    {
                        failures.Add("T146(d③) 结局杆之后 `MatchOver` / `Winner` / `EndReason` 必须由规则层结论"
                                     + $"原样填上，实测 {drive.MatchOver} / {drive.Winner} / {drive.EndReason}。");
                    }

                    if (!drive.TryConsumeSettlement())
                    {
                        failures.Add("T146(d③) 第一次 `TryConsumeSettlement()` 应为 true（视图靠它弹卡）。");
                    }

                    if (!drive.MatchOver)
                    {
                        failures.Add("T146(d③) 结算被取走后 `MatchOver` 仍须为 true ⇒ 没有任何东西自动开新局；"
                                     + "要再来一局必须**显式** `StartMatch`（用户已砍掉 BEST OF THREE）。");
                    }

                    if (drive.TryConsumeSettlement())
                    {
                        failures.Add("T146(d③) 消费后不得自动补一次\"待展示\"（否则关掉卡它会自己再弹）。");
                    }

                    drive.StartMatch(AiDifficulty.Club);
                    if (drive.MatchOver)
                    {
                        failures.Add("T146(d③) `StartMatch` 是唯一能开新局的口 ⇒ 它必须把 `MatchOver` 清回 false。");
                    }
                }
            }
            finally
            {
                if (drive != null)
                {
                    drive.UnregisterMatchHost();
                }

                MatchSeams.Host = previousHost;
                if (driveGo != null)
                {
                    Object.DestroyImmediate(driveGo);
                }
            }

            // ── (f) "只弹一次"以 Runtime 闸门为准 + (e)(f) 视图侧白盒接线 ──
            string resultSource = StripComments(ReadSourceOrNull(PracticeResultViewSourcePath));
            if (string.IsNullOrEmpty(resultSource))
            {
                failures.Add("T146(f) 读不到 `PracticeResultView.cs` 的磁盘文本 ⇒ 白盒接线守卫失效。");
            }
            else
            {
                foreach ((string Usage, string Why) in new (string, string)[]
                         {
                             ("drive.TryConsumeSettlement()",
                                 "结算的\"只弹一次\"必须以 Runtime 的显式闸门为准（UI 自己记的状态守不住）"),
                             ("MatchResultCopy.FormFor(drive.Winner)",
                                 "形态必须由 Runtime 的胜者解出（不许 UI 自己定谁赢）"),
                             // ⚠ **已作废**：原有一条 `("drive.EndReason", "\"为什么\"一行必须取规则层的结局枚举")`
                             //   —— **用户第四轮要求删除结算卡那句解释**（"不管输赢，这句都不要"）
                             //   ⇒ 那一行与它的渲染/文案都已删除，这条断言于是变成"要求存在一个我们**故意删掉**的东西"，
                             //   属**假要求**，随行删除。★ 这是**因用户要求而作废**，不是放宽。
                             ("if (drive.MatchOver)",
                                 "本局结束后练习局的\"清台/重摆\"判定必须整体让位（否则会自己开下一局）"),
                             ("MatchResultCopy.EdgeColor(win)",
                                 "配色只能来自 `MatchResultCopy` 的单点（视图里二选一会让形态配色无人守）"),
                             ("public static MatchResultForm CurrentForm",
                                 "必须留下**可判定的形态出口**（判据不许读像素）"),
                             ("MatchSeams.TryStartMatch(MatchSetup.Selected)",
                                 "`Play again` 必须**真的**再开一局（未接线时不许假装已开局）"),
                             ("HomeSeams.TryShow()",
                                 "`Back to club` 必须**真的**回首页（未接线时不许假装已回去）"),
                             ("_secondaryLabel.text = form == MatchResultForm.None ? ExportButtonLabel : BackToClubButtonLabel",
                                 "次键标签必须随形态改写 —— 键上写的必须就是它此刻做的事"),
                             ("RecordRackAtSettlement(form)",
                                 "**结算边界**必须记一局（记录与\"导出按钮\"解耦）—— 否则 AI 对局永远不写历史"),
                             ("PracticeSessionStore.TryAppendRecordToDisk(record, out string error)",
                                 "记录口必须是**累积**写盘（先读磁盘再追加），不是\"从空历史起算\"的覆盖式写入"),
                             ("PracticeSessionStore.TryExportHistory(out string error)",
                                 "`Export data` 只导出**已有**历史、不再追加记录（防双记）"),
                         })
                {
                    if (!resultSource.Contains(Usage))
                    {
                        failures.Add($"T146(f) 结算卡接线缺失：「{Usage}」—— {Why}。");
                    }
                }

                // "一次结算恰好一条"：记录口在**结算卡**里只准出现 **2 次**（声明 1 + 调用 1）。
                // ⇒ 少一次 = 漏接线（统计不涨）；多一次 = **双记**（一局算两局）。
                int recordSites = Regex.Matches(resultSource, "RecordRackAtSettlement").Count;
                if (recordSites != 2)
                {
                    failures.Add($"T146(g) 结算边界的记录口在 `PracticeResultView.cs` 里应出现 **2 次**"
                                 + $"（声明 1 + 调用 1），实测 {recordSites} ⇒ 要么漏接线、要么双记。");
                }
            }

            string homeSource = StripComments(ReadSourceOrNull(HomeViewSourcePath));
            if (string.IsNullOrEmpty(homeSource))
            {
                failures.Add("T146(e) 读不到 `HomeView.cs` 的磁盘文本 ⇒ 回首页接缝守卫失效。");
            }
            else if (!homeSource.Contains("HomeSeams.Show = Show"))
            {
                failures.Add("T146(e) 首页没有把 `Show` 注册进 `HomeSeams.Show` ⇒ 结算卡上的 `Back to club`"
                             + "点下去回不去（显示为可点 = 假陈述）。");
            }

            // `Back to club` 的**行为**：未接线 ⇒ false（不假装）；接线 ⇒ 真的被调到一次
            HomeSeams.Reset();
            if (HomeSeams.HasHost)
            {
                failures.Add("T146(e) `HomeSeams.Reset()` 之后不应有接线（静态状态会跨用例残留）。");
            }

            if (HomeSeams.TryShow())
            {
                failures.Add("T146(e) 未接线时 `TryShow()` 必须返回 false ⇒ 调用方据此保持卡片并提示，不许假装已回到首页。");
            }

            int shownCount = 0;
            HomeSeams.Show = () => shownCount++;
            if (!HomeSeams.TryShow() || shownCount != 1)
            {
                failures.Add($"T146(e) 接线后 `TryShow()` 应返回 true 且**恰好**调到一次，实测 {shownCount} 次。");
            }

            HomeSeams.Reset();

            // 赛前屏不得再写"三局两胜"（本工程是一局定胜负 ⇒ 那是假陈述）
            if (PracticeHudState.MatchIntroFormatTitle.Contains("THREE")
                || PracticeHudState.MatchIntroFormatDetail.ToUpperInvariant().Contains("TWO RACKS"))
            {
                failures.Add("T146(e) 赛前屏仍写着\"三局两胜\"（"
                             + $"{PracticeHudState.MatchIntroFormatTitle} / {PracticeHudState.MatchIntroFormatDetail}"
                             + "），而本工程是**一局定胜负** ⇒ 假陈述。");
            }

            // ── (g) 结算边界**记一局**：历史真的 +1、只 +1、且**累积** ──────────────
            // 起因（team-lead 的裁定）：`Export data` 是那份历史的**唯一运行期写入口**，而首页三项统计读它
            // ⇒ 若记录仍绑在按钮上，**比赛形态（次键是 `Back to club`）打完一局 AI 对战统计不会涨**
            //   （与"统计恒 0"是同一个病）。故记录必须挪到**结算边界**、且**一次结算恰好一条**。
            //
            // ⚠ 本组**会写磁盘**（`PracticeSessionStore.ExportPath`，在仓库外——`T101 (e)` 守这条）；
            //    用例结束**必须还原**（原内容或"原本不存在"这个事实），否则会污染首页统计与后续用例。
            string historyPath = PracticeSessionStore.ExportPath;
            string originalHistory = File.Exists(historyPath) ? File.ReadAllText(historyPath) : null;
            try
            {
                int before = PracticeSessionStore.ReadHistoryFromDisk().Count;

                PracticeSessionRecord first = PracticeSessionStore.NewRecord(
                    new PracticeSessionSnapshot { shots = 3, pots = 1, fouls = 0, racks = 1 },
                    PracticeSessionStore.ModeMatch);

                if (!PracticeSessionStore.TryAppendRecordToDisk(first, out string writeError))
                {
                    failures.Add($"T146(g) 结算边界的记录口写盘失败：{writeError}");
                }

                int afterFirst = PracticeSessionStore.ReadHistoryFromDisk().Count;
                if (afterFirst != before + 1)
                {
                    failures.Add($"T146(g) 一次结算应让历史 **+1**，实测 {before} → {afterFirst}"
                                 + "（结算不落历史 ⇒ 首页三项统计不会涨）。");
                }

                // 同一条记录（同 `id`）再并一次 ⇒ **只 +1**（"一次结算恰好一条"的存储层兜底）
                PracticeSessionStore.TryAppendRecordToDisk(first, out _);
                if (PracticeSessionStore.ReadHistoryFromDisk().Count != before + 1)
                {
                    failures.Add("T146(g) 同一条记录重复并入不得加两条（一次结算恰好一条，不许双记）。");
                }

                // 第二条**不同**记录 ⇒ 必须**累积**到 +2
                // （旧口径 `AppendToHistory(string.Empty, record)` 会把它**覆盖**成单条历史
                //   ⇒ 首页 `RACKS` 永远最多 1 = 统计静默做死）
                PracticeSessionRecord second = PracticeSessionStore.NewRecord(
                    new PracticeSessionSnapshot { shots = 5, pots = 2, fouls = 1, racks = 1 },
                    PracticeSessionStore.ModePractice);
                PracticeSessionStore.TryAppendRecordToDisk(second, out _);

                int afterSecond = PracticeSessionStore.ReadHistoryFromDisk().Count;
                if (afterSecond != before + 2)
                {
                    failures.Add($"T146(g) 两局的记录应当**累积**（期望 {before + 2}），实测 {afterSecond}"
                                 + " ⇒ 历史被\"覆盖成单条\"，首页的 `RACKS` 永远最多 1。");
                }

                // 首页口径：三项统计是对历史的**求和**（把这条钉住，免得有人改成"只看最后一条"）
                int rackSum = 0;
                foreach (PracticeSessionRecord record in PracticeSessionStore.ReadHistoryFromDisk())
                {
                    rackSum += record.racks;
                }

                if (rackSum < 2)
                {
                    failures.Add("T146(g) 首页 `RACKS` 读的是历史里 `racks` 的**求和** ⇒ 两局至少该是 2。");
                }
            }
            catch (System.Exception exception)
            {
                failures.Add("T146(g) 历史读写抛出异常：" + exception.Message);
            }
            finally
            {
                try
                {
                    if (originalHistory == null)
                    {
                        if (File.Exists(historyPath))
                        {
                            File.Delete(historyPath);
                        }
                    }
                    else if (!File.Exists(historyPath) || File.ReadAllText(historyPath) != originalHistory)
                    {
                        File.WriteAllText(historyPath, originalHistory);
                    }
                }
                catch (System.Exception exception)
                {
                    failures.Add("T146(g) 历史文件**还原失败**（会污染后续用例）：" + exception.Message);
                }
            }

            Assert.IsEmpty(failures, "T146 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T146: 形态映射穷举 ✓；两形态标题/徽章/四配色逐对不同且负局落酒红族 ✓；"
                                  // 用户第四轮：结算卡不再显示"为什么"那一行 ⇒ (c) 整组连同这句自述一起删除。
                                  + "出杆让位 / 对手回合提示优先级 / 不自动进下一局 三条收口 ✓；"
                                  + "Play again 走 MatchSeams、Back to club 走 HomeSeams（未接线不假装）✓；"
                                  + "结算边界记一局（+1、只 +1、且累积；首页口径 = 历史求和）✓；"
                                  + "`ai-turn` 由 `drive.IsAiTurn` 真接线 ✓");
        }

        /// <summary>两个 `Color32` 是否逐通道相同（判据里比色值，不做浮点容差 —— 令牌是 8 位整数量化值）。</summary>
        /// <summary>判据用：递归按名字找子节点。</summary>
        private static RectTransform FindDeep(RectTransform root, string name)
        {
            if (root == null) return null;
            RectTransform[] all = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }

            return null;
        }

        /// <summary>判据用：RectTransform 的**世界空间 AABB**（重叠检测用，规避 pivot/anchor 差异）。</summary>
        private static Rect WorldRect(RectTransform r)
        {
            Vector3 a = r.TransformPoint(new Vector3(r.rect.xMin, r.rect.yMin, 0f));
            Vector3 b = r.TransformPoint(new Vector3(r.rect.xMax, r.rect.yMax, 0f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        /// <summary>两个号码集合是否相同（**顺序无关**；用于"可见 chip 集合 == 剩余球号"）。</summary>
        private static bool SameNumberSet(int[] actual, int[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
            {
                return false;
            }

            return new HashSet<int>(actual).SetEquals(expected);
        }

        // ════════════════ T148 全局字号倍率（单点）+ 关键块不挤 ═══════════════

        /// <summary>
        /// **`T148`：字号整体放大（`FontScale`）—— 单点、真的生效、关键块不挤。**
        ///
        /// 起因（用户实机反馈之二）：**"字体太小，看不清"**。要求是**整体放大**，且**不许逐个字号手调**
        /// （那会散成"这块大一点、那块忘了调"的半新半旧界面 —— 与 35-A 重做前"令牌散落两处视图"同一类病）。
        ///
        /// ── ⚠ 一个必须先说清的**实测事实**（它决定了实现路径）─────────────────────
        /// `PracticeHudLayout.RenderedFontPixels` **不是渲染路径**：全库只有一个调用者，是判据
        /// （`PracticeHudTests.T95 (e)` 的可读下限）。真正写字号的**唯一**地方是
        /// `PracticeHudView.CreateText` 的 `text.fontSize = size`。
        /// ⇒ 倍率乘在**那里**（才叫整体放大），`RenderedFontPixels` 乘**同一个常量**（否则它预测的
        ///   像素高与实渲染不一致，可读性判据就成了假陈述）。⇒ 两处引用同一常量 ⇒ 倍率仍只有一个真源。
        ///
        /// ── 判据六组 ────────────────────────────────────────────────────────────
        /// (a) `FontScale > 1`（本轮目标就是放大）；
        /// (b) **真的乘上了**：参考分辨率 1920×1080（`CanvasScale = 1`）下预测像素高 **>** 设计字号；
        /// (c) **单调**：设计字号 ↑ ⇒ 预测像素高 ↑；
        /// (d) **单点**：`* FontScale` 在换算点里恰好 1 次、`* PracticeHudLayout.FontScale` 在视图里恰好 1 次、
        ///     且 `.fontSize =` 全 UI 只有 1 处；
        /// (e) **关键块不挤**：**18 个**容器的带高 ≥ **放大后**的字号（状态条说明行 / 选袋 6 格 / 赛前屏三档与标题赛制行 /
        ///     结算卡标题·徽章·"为什么"行）。⚠ 这是"**单行 band 装得下字号**"的**下界**，不是 leading 保证
        ///     （TMP 没有 mask ⇒ 超出不会自动裁切，只会压到邻行 ⇒ 这条守的就是"不压邻行"）；
        /// (f) 结算卡标题带**不与**下面"为什么"行重叠（放大后重算过的缝）。
        ///
        /// (g) **绝对像素可读下限**（阈值来自用户**三轮**反馈，不是审美）：
        ///     **三轮反馈：1.28 不够 → 1.6 不够 → 1.9 才够**（用户原话"小，但很清楚"）。
        ///     1080p 下正文 `12 × 1.9 = 22.8` ≥ **22 px**、标签 `9 × 1.9 = 17.1` ≥ **16 px**。
        ///     ⚠ **阈值必须随契约上调**：旧的 18/13 在"回调到 1.5"（正文正好 18 px）时**仍然绿**
        ///       —— 那正是"回调换绿"的滑坡，所以下限写 22/16。
        ///
        /// (h) **首页不吃倍率**（用户原话：「我没说改开始主页字体，现在字体排版都乱了」）：
        ///     `HomeView` 里 `applyGlobalScale: false` 的出现次数 **≥ 4**（三处散点 + `CreateHomeText` 单点）
        ///     ⇒ 一旦有人把首页改回吃倍率，这条立刻红（否则"首页保持原版式"就只是口头约定）。
        ///
        /// **怎么打死它**：① 把 `CreateText` 的乘号**乘两次**（`p9-font-scale-twice`）⇒ (d) 红；
        /// ② 把赛前屏标题带高改回 1.28 时代的 50（`p9-title-band-shrink`）⇒ **(e) 赛前屏标题** 红
        ///    —— 证明**新版式也确实被守**，不是"改了就算"。
        /// ⚠ 如实标注：`(a)(b)(c)` 是取值级断言，**(d) 才是"单点"那一刀**；`(e)(f)` 守的是版式预算，`(g)` 守可读下限。
        /// </summary>
        [Test]
        public void T148_FontScale_SinglePoint_AppliedOnce_AndKeyAreasFit()
        {
            var failures = new List<string>();

            // ── (a) 倍率 > 1 ──
            if (!(PracticeHudLayout.FontScale > 1f))
            {
                failures.Add($"T148(a) `FontScale` 应 > 1（本轮目标是整体放大），实测 {PracticeHudLayout.FontScale:R}。");
            }

            // ── (b) 真的乘上了（参考分辨率 = 1920×1080，CanvasScale = 1）──
            const int refW = 1920;
            const int refH = 1080;
            float labelPx = PracticeHudLayout.RenderedFontPixels(PracticeHudLayout.LabelFontSize, refW, refH);
            if (!(labelPx > PracticeHudLayout.LabelFontSize))
            {
                failures.Add($"T148(b) 参考分辨率下 {PracticeHudLayout.LabelFontSize:R} px 的标签应被放大到 **>** "
                             + $"{PracticeHudLayout.LabelFontSize:R} px，实测 {labelPx:R} ⇒ 倍率没乘进换算点。");
            }

            // ── (c) 单调 ──
            float smallPx = PracticeHudLayout.RenderedFontPixels(9f, refW, refH);
            float midPx = PracticeHudLayout.RenderedFontPixels(PracticeHudLayout.LabelFontSize, refW, refH);
            float hugePx = PracticeHudLayout.RenderedFontPixels(ClubUiTokens.FontResultTitle, refW, refH);
            if (!(smallPx < midPx && midPx < hugePx))
            {
                failures.Add($"T148(c) 预测像素高必须对设计字号**严格单调**，实测 9→{smallPx:R}、"
                             + $"{PracticeHudLayout.LabelFontSize:R}→{midPx:R}、"
                             + $"{ClubUiTokens.FontResultTitle:R}→{hugePx:R}。");
            }

            // ── (d) 单点：只准乘两次（换算点 1 次 + CreateText 1 次）──
            string layoutSource = StripComments(ReadSourceOrNull(PracticeHudLayoutSourcePath));
            string viewSource = StripComments(ReadSourceOrNull(PracticeHudViewSourcePath));

            if (string.IsNullOrEmpty(layoutSource) || string.IsNullOrEmpty(viewSource))
            {
                failures.Add("T148(d) 读不到 `PracticeHudLayout.cs` / `PracticeHudView.cs` 的磁盘文本 ⇒ 单点守卫失效。");
            }
            else
            {
                int inLayout = Regex.Matches(layoutSource, @"\*\s*FontScale").Count;
                //  视图侧改成数 `PracticeHudLayout.FontScale` 的**出现次数**（含"乘"与"三元取舍"两种写法）：
                //  首页已改为 `applyGlobalScale: false`（用户要求保持原字号）⇒ 不再是纯 `*` 形态；
                //  "乘两次"的注入仍会把它变成 2 ⇒ 判别力不变（实测注入后报 `实测 2`）。
                // ⚠ **这不是放宽**：口径从"数 `* FontScale`"变"数标识符出现次数"，只允许 `CreateText` 里**唯一一处**
                //   三元取舍；**不许**把 `? FontScale : …` 这种形态堆到视图别处（否则计数会 >1 立刻红）。
                int inView = Regex.Matches(viewSource, @"PracticeHudLayout\.FontScale").Count;
                int writes = Regex.Matches(viewSource, @"\.fontSize\s*=").Count;

                if (inLayout != 1)
                {
                    failures.Add($"T148(d) 换算点里 `* FontScale` 应恰好 **1** 次，实测 {inLayout}"
                                 + $" —— 乘两次会变成 {PracticeHudLayout.FontScale * PracticeHudLayout.FontScale:R} 倍。");
                }

                if (inView != 1)
                {
                    failures.Add($"T148(d) `PracticeHudView` 里 `* PracticeHudLayout.FontScale` 应恰好 **1** 次"
                                 + $"（那是全 UI 唯一的字号写入点），实测 {inView}。");
                }

                if (writes != 1)
                {
                    failures.Add($"T148(d) 全 UI 应只有 **1** 处写 `.fontSize =`，实测 {writes} 处"
                                 + " —— 多出来的那一处就是漏乘倍率的第二种字号来源。");
                }
            }

            // ── (e) 关键块：band 高 ≥ 放大后的字号 ──
            //  ⚠ 单位换算：`HomeLayout.Px(...)` 返回的是**归一化比例**（x 除 `ReferenceWidth`、y/高除
            //    `ReferenceHeight`，见 `HomeLayout.Px`）⇒ 必须 ×`ReferenceHeight` 才回到设计 px。
            //    （首版漏了这一步 ⇒ 带高算成 0.046 px、整组假红；**这是判据的单位错，不是产品缺陷**。）
            //  ⚠ 两个比例（状态条 0.60、主行 0.46、说明行 0.42）取自 `PracticeHudView.BuildStatusBar`；
            //    （选袋条那一块已随 D3 取消删除，18 块 → 17 块。）
            const float homeRefH = HomeLayout.ReferenceHeight;
            float statusBarH = PracticeHudLayout.StatusZone.height * PracticeHudLayout.ReferenceHeight * 0.60f;
            var bands = new (string Name, float Band, float Font)[]
            {
                ("状态条说明行", statusBarH * 0.42f, ClubUiTokens.FontCaption),
                ("状态条主行", statusBarH * 0.46f, ClubUiTokens.FontBody),
                ("赛前屏标题", HomeLayout.MatchIntroTitle.height * homeRefH, ClubUiTokens.FontResultTitle),
                ("赛前屏小标", HomeLayout.MatchIntroEyebrow.height * homeRefH, ClubUiTokens.FontBody),
                ("赛前屏赛制标题", HomeLayout.MatchIntroFormatTitle.height * homeRefH, ClubUiTokens.FontHomeAction),
                ("赛前屏赛制说明", HomeLayout.MatchIntroFormatDetail.height * homeRefH, ClubUiTokens.FontSmall),
                ("赛前屏档位小标", HomeLayout.MatchIntroTierLabel.height * homeRefH, ClubUiTokens.FontMicro),
                ("赛前屏三档格",
                    HomeLayout.MatchIntroTierSegment.height * homeRefH - ClubUiTokens.SegmentPadding * 2f,
                    ClubUiTokens.FontBodySmall),
                ("结算卡标题", PracticeResultLayout.TitleLocal.height * PracticeResultLayout.CardHeightPx,
                    ClubUiTokens.FontResultTitle),
                ("结算卡徽章", PracticeResultLayout.BadgeLocal.height * PracticeResultLayout.CardHeightPx,
                    ClubUiTokens.FontCaption),
                // ↓ 本轮的 +6（12 → 18）：用户点名"结算卡字体也不够大" ⇒ 把卡内**四处**全纳入
                //   （标题/徽章/"为什么"上面已有），再加两个键的文案、统计数值、统计标签；
                //   另两块是核算时发现同样临界的**赛前屏己方名/对手名**（12 px 字号、带高 18 ⇒ 19.2 装不下），
                //   不纳入就又是一条"测空气"的盲区，故一并纳入（如实说明：多出的 2 块是我加的，不是你点名的）。
                ("结算卡主键文案", PracticeResultLayout.PlayAgainButtonLocal.height * PracticeResultLayout.CardHeightPx,
                    ClubUiTokens.FontBody),
                ("结算卡次键文案", PracticeResultLayout.ExportButtonLocal.height * PracticeResultLayout.CardHeightPx,
                    ClubUiTokens.FontBody),
                ("结算卡统计数值",
                    PracticeResultLayout.StatsLocal.height * PracticeResultLayout.CardHeightPx * 0.58f,
                    ClubUiTokens.FontStatValue),
                ("结算卡统计标签",
                    PracticeResultLayout.StatsLocal.height * PracticeResultLayout.CardHeightPx * 0.32f,
                    ClubUiTokens.FontMicro),
                ("赛前屏己方名", HomeLayout.MatchIntroPlayerName.height * homeRefH, ClubUiTokens.FontBody),
                ("赛前屏对手名", HomeLayout.MatchIntroOpponentName.height * homeRefH, ClubUiTokens.FontBody),
            };

            foreach ((string name, float band, float font) in bands)
            {
                float need = font * PracticeHudLayout.FontScale;
                if (band < need)
                {
                    failures.Add($"T148(e) {name}：容器带高 {band:R} px 装不下放大后的字号 {need:R} px"
                                 + $"（设计 {font:R} × {PracticeHudLayout.FontScale:R}）"
                                 + " ⇒ **加容器高/减行距**，别把倍率调回去。");
                }
            }


            // ── (g) 绝对像素可读下限（阈值来自"用户两轮反馈看不清"）──
            //  1080p（CanvasScale = 1）：正文 12 px × 1.6 = **19.2 px** ≥ 18 px；标签 9 px × 1.6 = **14.4 px** ≥ 13 px。
            //  ⚠ 第一版 1.28 时正文只有 15.4 px、标签 11.5 px，用户实机**仍说看不清** ⇒ 这两个数是"够大"的**下界**，
            //    不是审美取值；要调只能调**上调**，不许为了别的判据变绿而回调。
            //  三轮反馈（1.28 → 1.6 → 1.9）定下的下限：正文 ≥ 22 px、标签 ≥ 16 px。
            //  ⚠ 不写成 18/13：那会让"倍率回调到 1.5（正文 18 px）"仍然绿 ⇒ 抓不到滑坡。
            float bodyPx = PracticeHudLayout.RenderedFontPixels(ClubUiTokens.FontBody, refW, refH);
            float captionPx = PracticeHudLayout.RenderedFontPixels(ClubUiTokens.FontCaption, refW, refH);
            const float minBodyPx = 22f;
            const float minCaptionPx = 16f;

            if (bodyPx < minBodyPx)
            {
                failures.Add($"T148(g) 1080p 下正文只有 {bodyPx:R} px（< {minBodyPx:R} px）"
                             + " ⇒ 倍率太小（用户已两次反馈看不清）。");
            }

            if (captionPx < minCaptionPx)
            {
                failures.Add($"T148(g) 1080p 下标签只有 {captionPx:R} px（< {minCaptionPx:R} px）"
                             + " ⇒ 倍率太小（用户已两次反馈看不清）。");
            }

            // ── (h) 首页不吃倍率（用户："我没说改开始主页字体"）──
            string homeSource = StripComments(ReadSourceOrNull(HomeViewSourcePath));
            if (string.IsNullOrEmpty(homeSource))
            {
                failures.Add("T148(h) 读不到 `HomeView.cs` 的磁盘文本 ⇒ 首页免倍率守卫失效。");
            }
            else
            {
                // ⚠ **恰好 4 处**（不是"≥ 4"）：只断下界的话，将来在 HUD 里也偷偷 opt-out 同样能过
                //   —— 而"只有首页可以不吃倍率"才是用户的要求（HUD 正是要放大的那部分）。
                // ⚠ **恰好 3 处**（原 4 处）：用户第四轮要求**删掉首页 `OPPONENT` 那一行**
                //   （`Casual|Club|Expert` 三个 chip 整块移除，难度选择只留在赛前屏）⇒ opt-out 从 4 → 3。
                //   这是**因用户要求而重钉**，不是放宽：三处散点 = 桌号 / 两个主键标签 / `CreateHomeText` 单点。
                int optOut = Regex.Matches(homeSource, @"applyGlobalScale: *false").Count;
                if (optOut != 3)
                {
                    failures.Add($"T148(h) **首页应恰好 3 处** `applyGlobalScale: false`（桌号 / 两个主键标签 / `CreateHomeText` 单点；"
                                 + "原第 4 处是已按用户要求删除的首页难度 chip），"
                                 + $"实测 {optOut} ⇒ 多一处是别处偷偷退出倍率、少一处是首页会被带歪。");
                }

                // **其它视图一律不许 opt-out**（第二个文件能退出倍率 = 静默削弱"整体放大"）。
                foreach (string otherView in new[] { ViewSourceFiles[0], ViewSourceFiles[1] })
                {
                    string otherCode = StripComments(ReadSourceOrNull(otherView));
                    if (string.IsNullOrEmpty(otherCode))
                    {
                        failures.Add($"T148(h) 读不到 {Path.GetFileName(otherView)} ⇒ 该守卫失效。");
                        continue;
                    }

                    int leaks = Regex.Matches(otherCode, @"applyGlobalScale: *false").Count;
                    if (leaks != 0)
                    {
                        failures.Add($"T148(h) {Path.GetFileName(otherView)} 里出现了 {leaks} 处 `applyGlobalScale: false`"
                                     + " ⇒ 只有**首页**可以不吃倍率，HUD / 结算卡必须吃。");
                    }
                }
            }

            Assert.IsEmpty(failures, "T148 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T148: FontScale = {PracticeHudLayout.FontScale:R}（参考 1920×1080，CanvasScale = 1）✓；"
                                  + $"换算点放大生效（{PracticeHudLayout.LabelFontSize:R} → {labelPx:R} px）✓；单调 ✓；"
                                  + "单点：换算点 1 次 + `CreateText` 1 次 + `.fontSize` 唯一写入点 ✓；"
                                  + $"{bands.Length} 个关键块的带高都装得下放大后的字号 ✓；"
                                  + "结算卡标题与\"为什么\"行不重叠 ✓");
        }

        /// <summary>
        /// **`T156`：设置面板**（`SET` 按钮 / 打开关闭 / 音量夹紧 / 三处输入门）。
        /// ⚠ 为什么音量要连点：单次 `+0.1` 不能证明**夹紧**；必须点到上限再点一次，断言仍为 1。
        /// </summary>
        [Test]
        public void T156_SettingsPanel_ButtonOpenClose_VolumeClamp_AndInputGates()
        {
            var failures = new List<string>();
            float originalVolume = AudioListener.volume;

            var hudGo = new GameObject("T156HudProbe");
            try
            {
                var hud = hudGo.AddComponent<PracticeHudView>();
                hud.EnsureBuilt();

                // ① 按钮存在且**真的可点**（防"假可点态"的反面：钉死它真接了行为）
                Transform toggle = hud.SettingsToggleForJudge;
                if (toggle == null)
                {
                    failures.Add("T156① 找不到设置按钮 `Brand/SettingsToggle`。");
                }
                else
                {
                    var button = toggle.GetComponent<UnityEngine.UI.Button>();
                    if (button == null || button.onClick.GetPersistentEventCount() == 0)
                    {
                        // 运行时 `AddListener` 不计入 persistent 事件 ⇒ 用 `onClick` 是否为空判断不可靠，
                        // 因此这里只断"有 Button 组件"；接线本身由 ② 的行为断言证明。
                        if (button == null)
                        {
                            failures.Add("T156① 设置按钮上没有 `Button` 组件 ⇒ 点不动。");
                        }
                    }

                    // 内容两种合法形态：**齿轮位图**（`Glyph` 有 sprite）或**文本兜底**（`Label` == `SET`）。
                    // 二者必须至少有其一 —— 否则就是个空按钮（看得见、点下去也不知道是什么）。
                    var label = toggle.Find("Label")?.GetComponent<TMPro.TextMeshProUGUI>();
                    var glyph = toggle.Find("Glyph")?.GetComponent<UnityEngine.UI.Image>();
                    bool hasGear = glyph != null && glyph.sprite != null;
                    bool hasText = label != null && label.text == PracticeHudState.ButtonSettings;
                    if (!hasGear && !hasText)
                    {
                        failures.Add("T156① 设置按钮既没有齿轮位图、也没有 `SET` 文案 ⇒ 空按钮。");
                    }
                }

                // ② 打开 / 关闭同步 `IsSettingsOpen`
                hud.OnOpenSettings();
                if (!PracticeHudView.IsSettingsOpen)
                {
                    failures.Add("T156② `OnOpenSettings()` 之后 `IsSettingsOpen` 应为 true。");
                }

                hud.CloseSettings();
                if (PracticeHudView.IsSettingsOpen)
                {
                    failures.Add("T156② `CloseSettings()` 之后 `IsSettingsOpen` 应为 false。");
                }

                // ③ 音量：连点到上限仍为 1（**夹紧**），连点到下限仍为 0；文本与数值同步
                AudioListener.volume = 0.5f;
                for (int i = 0; i < 20; i++)
                {
                    hud.AdjustVolume(0.1f);
                }

                if (Mathf.Abs(AudioListener.volume - 1f) > 1e-4f)
                {
                    failures.Add($"T156③ 音量上限应夹到 1，实测 {AudioListener.volume:R}。");
                }

                string expectTop = Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
                if (hud.VolumeTextForJudge != expectTop)
                {
                    failures.Add($"T156③ 百分比文本应为 `{expectTop}`，实测 `{hud.VolumeTextForJudge}`。");
                }

                for (int i = 0; i < 25; i++)
                {
                    hud.AdjustVolume(-0.1f);
                }

                if (Mathf.Abs(AudioListener.volume) > 1e-4f)
                {
                    failures.Add($"T156③ 音量下限应夹到 0，实测 {AudioListener.volume:R}。");
                }

                // ④ 三处输入门都在（源码级：防止以后有人删掉其中一处，面板开着就点穿）
                string physicsDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".",
                    "Assets/_Project/Scripts/Runtime/Physics");
                (string file, string name)[] gates =
                {
                    ("PointerAimController.cs", "瞄准"),
                    ("PointerPowerController.cs", "蓄力"),
                    ("AimAssistController.cs", "瞄准线"),
                };
                foreach ((string file, string name) in gates)
                {
                    string path = Path.Combine(physicsDir, file);
                    if (!File.Exists(path))
                    {
                        failures.Add($"T156④ 找不到 `{file}`（被扫文件缺失 ⇒ 本项无从判定）。");
                        continue;
                    }

                    string text = File.ReadAllText(path);
                    if (!text.Contains("PracticeHudView.IsSettingsOpen"))
                    {
                        failures.Add($"T156④ `{file}`（{name}）缺少 `PracticeHudView.IsSettingsOpen` 这道门 ⇒ 面板打开时会被点穿。");
                    }
                }
            }
            finally
            {
                AudioListener.volume = originalVolume;
                Object.DestroyImmediate(hudGo);
                PracticeHudView.CloseSettingsForJudge();
            }

            Assert.IsEmpty(failures, "T156（设置面板）失败：\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// **`T158`：操作引导**（首次进入弹一次 / 三处输入门 / 文案非空 / 存档键带版本）。
        /// 引导开着还能瞄准或出杆，就是"模态门漏接"——这条守的正是那个。
        /// </summary>
        [Test]
        public void T158_Onboarding_StepsGatedAndVersioned()
        {
            var failures = new List<string>();

            if (PracticeHudState.OnboardingStepCount < 3)
            {
                failures.Add($"T158 引导步数过少（{PracticeHudState.OnboardingStepCount}），至少覆盖 瞄准/出杆/放球。");
            }

            for (int i = 0; i < PracticeHudState.OnboardingStepCount; i++)
            {
                if (string.IsNullOrWhiteSpace(PracticeHudState.OnboardingTitle(i)))
                {
                    failures.Add($"T158 第 {i} 步标题为空。");
                }

                if (string.IsNullOrWhiteSpace(PracticeHudState.OnboardingBody(i)))
                {
                    failures.Add($"T158 第 {i} 步正文为空。");
                }
            }

            if (!PracticeHudState.OnboardingSeenKey.Contains(".v"))
            {
                failures.Add("T158 存档键必须带版本号（改引导内容时可让老玩家再看一次）。");
            }

            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            (string file, string label)[] gated =
            {
                ("Assets/_Project/Scripts/Runtime/Physics/PointerAimController.cs", "瞄准"),
                ("Assets/_Project/Scripts/Runtime/Physics/PointerPowerController.cs", "蓄力/出杆"),
                ("Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs", "瞄准线"),
            };

            foreach ((string file, string label) in gated)
            {
                string path = Path.Combine(root, file);
                if (!File.Exists(path))
                {
                    failures.Add($"T158 找不到 `{file}`（被扫文件缺失 ⇒ 无从判定）。");
                    continue;
                }

                if (!File.ReadAllText(path).Contains("PracticeHudView.IsOnboardingOpen"))
                {
                    failures.Add($"T158 `{file}`（{label}）缺少 `IsOnboardingOpen` 门 ⇒ 引导开着还能操作。");
                }
            }

            string home = Path.Combine(root, "Assets/_Project/Scripts/Runtime/UI/HomeView.cs");
            if (File.Exists(home))
            {
                string text = File.ReadAllText(home);
                int hits = 0;
                int at = 0;
                while ((at = text.IndexOf("ShowOnboardingIfFirstTime", at, System.StringComparison.Ordinal)) >= 0)
                {
                    hits++;
                    at += 5;
                }

                if (hits < 2)
                {
                    failures.Add($"T158 进入打球的入口应**两处**都触发引导（练习 + 对战），实际只找到 {hits} 处。");
                }
            }
            else
            {
                failures.Add("T158 找不到 `HomeView.cs`。");
            }

            Assert.IsEmpty(failures, "T158（操作引导）失败：\n" + string.Join("\n", failures));
        }

    }
}
