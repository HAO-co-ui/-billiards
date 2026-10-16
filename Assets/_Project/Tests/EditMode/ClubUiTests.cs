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
        /// ② 不可达集合**恰好**是 `{ai-turn, call-pocket, paused}`（本卡无 AI / 无规则引擎 / 无暂停）
        ///    —— 既不许悄悄缩小（做了却没登记），也不许悄悄扩大（少做了却声称"设计没有"）；
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
            //    · `call-pocket`：真值源 `PhysicsTestDrive.IsEightBallShot`（单点口径）、
            //      写入口 `TryCallPocket` / `ClearCalledPocket`、读出口 `CalledPocket`、
            //      被拒出口 `RefusedCallPocketCount`、状态条出口 `ResolveVisualState(..., callPocket)`。
            var unreachable = new HashSet<HudVisualState>(PracticeHudState.UnreachableStates);
            var expectedUnreachable = new HashSet<HudVisualState>
            {
                HudVisualState.Paused,
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
                                        foreach (bool call in new[] { false, true })
                                        {
                                            produced.Add(PracticeHudState.ResolveVisualState(
                                                phase, foul, refused, hand, resultOpen, placement, aiTurn, call));
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
        /// **怎么打死它**：① 把 `MatchSetup.ResetToDefault` 改成 `Casual` ⇒ ① 红；
        /// ② 把 `Tiers` 的次序调换成 `Club|Casual|Expert` ⇒ ② 红；
        /// ③ 把 `MatchTurnNotice.RequestBlock` 改成**静默 `return`** ⇒ ③ 红（计数恒 0 = 静默无反馈）；
        /// ④ 把 `AI Match` 那行的 `primary: true` 去掉 ⇒ ④ 红（那意味着又变回压暗的假入口）。
        /// </summary>
        [Test]
        public void T144_MatchIntro_DefaultTier_TierSwitch_OpponentTurnNotice_AndNoFalseAffordance()
        {
            var failures = new List<string>();

            // ── ① 默认档 ──
            MatchSetup.ResetToDefault();
            if (MatchSetup.Selected != AiDifficulty.Club)
            {
                failures.Add($"T144① 默认档应为 `Club`（用户拍板），实际 {MatchSetup.Selected}。");
            }

            if (MatchSetup.Selected != EightBallAi.DefaultDifficulty)
            {
                failures.Add("T144① UI 默认档必须 = `EightBallAi.DefaultDifficulty` —— 两处默认值不得漂移。");
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

            // 档位名与对手名：与参考稿逐字（`Club` / `Club AI`）
            if (PracticeHudState.TierLabel(AiDifficulty.Club) != PracticeHudState.HomeDifficultyClub
                || PracticeHudState.OpponentName(AiDifficulty.Club) != PracticeHudState.HomeDifficultyClub
                                                                       + PracticeHudState.OpponentNameSuffix)
            {
                failures.Add("T144② 档位名/对手名必须复用同一套文案（`Club` / `Club AI`）。");
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

        private const string PointerAimControllerSourcePath =
            "Assets/_Project/Scripts/Runtime/Physics/PointerAimController.cs";

        private const string PointerPowerControllerSourcePath =
            "Assets/_Project/Scripts/Runtime/Physics/PointerPowerController.cs";

        /// <summary>
        /// **`T146`：一局定胜负的结算卡 —— 两形态**必须真的不同**、结算出口**以 Runtime 为准**、
        /// 三条流程收口**逐条可判定**、界面无假陈述**。
        ///
        /// ── 断言六组 ────────────────────────────────────────────────────────────
        /// **(a) 形态映射**：`ShotTurn.Player ⇒ Win`、`ShotTurn.Ai ⇒ Lose`，且**穷举** `ShotTurn`
        ///     （新增枚举值不得落到 `None` = 悄悄没有形态）。
        /// **(b) 两形态的关键差异项真的不同**：标题不同 + 徽章不同（**两处独立文案**）+ 四个配色令牌
        ///     逐对不同；且**负局那一侧必须落在酒红族**、**胜局那一侧不得借酒红**
        ///     （设计说明 §一：酒红只用于犯规和失败）。⚠ 这一组是"两形态不是只差一个词"的**机器形态**。
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
        ///     `MatchResultCopy.FormFor(drive.Winner)`、`drive.EndReason`、次键标签改写、`CurrentForm` 出口。
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
        /// ⑥ 把 `ResolveVisualState` 的 `aiTurn` 那一支删掉 ⇒ `T125②'` 红（登记为可达却产出不了）。
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

            // 四对配色逐对（胜 = 黄铜族；负 = 酒红族且 == 设计稿的那几个值）
            var colorPairs = new (string Name, Color32 Win, Color32 Lose, Color32 ExpectedWin, Color32 ExpectedLose)[]
            {
                ("顶部色条", MatchResultCopy.EdgeColor(true), MatchResultCopy.EdgeColor(false),
                    ClubUiTokens.ResultWinEdge, ClubUiTokens.ResultLoseEdge),
                ("徽章图标", MatchResultCopy.IconColor(true), MatchResultCopy.IconColor(false),
                    ClubUiTokens.Accent, ClubUiTokens.ResultLoseIcon),
                ("徽章环", MatchResultCopy.IconEdgeColor(true), MatchResultCopy.IconEdgeColor(false),
                    ClubUiTokens.ResultWinEdge, ClubUiTokens.ResultLoseIconEdge),
                ("徽章 chip 边线", MatchResultCopy.BadgeEdgeColor(true), MatchResultCopy.BadgeEdgeColor(false),
                    ClubUiTokens.LineBadge, ClubUiTokens.ResultLoseBadgeEdge),
            };

            foreach ((string name, Color32 win, Color32 lose, Color32 expectedWin, Color32 expectedLose) in colorPairs)
            {
                if (Same(win, lose))
                {
                    failures.Add($"T146(b) {name}：两形态用了同一个色 ⇒ 形态只差在文字上。");
                }

                if (!Same(win, expectedWin))
                {
                    failures.Add($"T146(b) {name}：胜局应为 {ClubUiTokens.ToHexRgba(expectedWin)}，"
                                 + $"实际 {ClubUiTokens.ToHexRgba(win)}。");
                }

                if (!Same(lose, expectedLose))
                {
                    failures.Add($"T146(b) {name}：负局应为 {ClubUiTokens.ToHexRgba(expectedLose)}，"
                                 + $"实际 {ClubUiTokens.ToHexRgba(lose)}。");
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

            // ── (c) "为什么"一行：穷举、非空、互不相同 ──
            var endingOutcomes = new List<ShotOutcomeKind>();
            var reasons = new Dictionary<ShotOutcomeKind, string>();

            foreach (ShotOutcomeKind kind in (ShotOutcomeKind[])System.Enum.GetValues(typeof(ShotOutcomeKind)))
            {
                string reason = MatchResultCopy.Reason(kind);
                if (string.IsNullOrWhiteSpace(reason))
                {
                    failures.Add($"T146(c) `ShotOutcomeKind.{kind}` 没有对应的\"为什么\"一行"
                                 + " ⇒ 新增结局会静默显示空副标题。");
                }

                reasons[kind] = reason;

                if (MatchResultCopy.IsEndingOutcome(kind))
                {
                    endingOutcomes.Add(kind);
                }
            }

            var expectedEnding = new HashSet<ShotOutcomeKind>
            {
                ShotOutcomeKind.LegalEightBallPotted,
                ShotOutcomeKind.EightBallPottedTooEarly,
                ShotOutcomeKind.EightBallWrongPocket,
                ShotOutcomeKind.EightBallOffTable,
            };

            if (endingOutcomes.Count != expectedEnding.Count
                || !expectedEnding.SetEquals(new HashSet<ShotOutcomeKind>(endingOutcomes)))
            {
                failures.Add("T146(c) \"能成为结算原因\"的集合应为 "
                             + $"{string.Join("/", expectedEnding)}，实际 {string.Join("/", endingOutcomes)}。");
            }

            // 结局原因两两不同（否则两件不同的违规在卡上说同一句 = 玩家分不出为什么输）
            var seenReasons = new Dictionary<string, ShotOutcomeKind>();
            foreach (ShotOutcomeKind kind in expectedEnding)
            {
                if (seenReasons.TryGetValue(reasons[kind], out ShotOutcomeKind other))
                {
                    failures.Add($"T146(c) `{kind}` 与 `{other}` 的\"为什么\"一行相同（\"{reasons[kind]}\"）"
                                 + " ⇒ 两种不同的输法在卡上无法分辨。");
                }
                else
                {
                    seenReasons[reasons[kind]] = kind;
                }
            }

            // 胜局的唯一原因必须是"合法进黑 8"（规则层的结论与卡上的解释必须同源）
            if (reasons[ShotOutcomeKind.LegalEightBallPotted] != MatchResultCopy.ReasonLegalEightBall
                || MatchResultCopy.IsEndingOutcome(ShotOutcomeKind.EightBallRespot)
                || MatchResultCopy.IsEndingOutcome(ShotOutcomeKind.Ongoing))
            {
                failures.Add("T146(c) 结局归类与文案不自洽（合法进黑 8 是唯一的胜因；复位 / 进行中不是结局）。");
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
                if (!hudSource.Contains("drive.IsAiTurn"))
                {
                    failures.Add("T146(h) `PracticeHudView` 没有把 `drive.IsAiTurn` 喂给状态求解"
                                 + " ⇒ 对手回合期间状态条回落成 `aim`（嘴上说\"轮到你了\"）。");
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
                             ("drive.EndReason",
                                 "\"为什么\"一行必须取规则层的结局枚举（不许 UI 从球位反推）"),
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
                                  + $"\"为什么\"穷举 {endingOutcomes.Count} 种结局且互不相同 ✓；"
                                  + "出杆让位 / 对手回合提示优先级 / 不自动进下一局 三条收口 ✓；"
                                  + "Play again 走 MatchSeams、Back to club 走 HomeSeams（未接线不假装）✓；"
                                  + "结算边界记一局（+1、只 +1、且累积；首页口径 = 历史求和）✓；"
                                  + "`ai-turn` 由 `drive.IsAiTurn` 真接线 ✓");
        }

        /// <summary>两个 `Color32` 是否逐通道相同（判据里比色值，不做浮点容差 —— 令牌是 8 位整数量化值）。</summary>
        private static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        // ════════════════ T147 P6-3：指定袋（call-pocket）的 UI 半边 ══════════════

        /// <summary>
        /// **`T147`：指定袋选袋（`call-pocket`）的 UI 半边** —— 进入时机 / 显示 / 被拒不许静默。
        ///
        /// ── 用户口径（逐条落成断言）──────────────────────────────────────────────
        /// · **只在打黑 8 那一杆**要指定袋 ⇒ (a) 两向都断：`callPocket: true ⇒ CallPocket`；
        ///   **普通杆（`false`）在任何相位/窗口组合下都不得进入 `call-pocket`**；
        /// · **明确显示已指定哪个袋** ⇒ (b) 六个袋号**逐号可分辨**、未指定时是"请你选"、
        ///   已指定时是"已指定 · pocket N (kind)"，两句**必须不同**；
        /// · **未指定 ⇒ 出杆被拒 + 可见反馈（不许静默）** ⇒ (c) 行为级：`IsEightBallShot && CalledPocket < 0`
        ///   时 `Launch()` 必须被拒且 `RefusedCallPocketCount` **+1**；视图必须**消费这个上升沿**开窗口；
        ///   且"被拒那一句"与"请你选袋"**不是同一句**（否则等于没反馈）。
        ///
        /// ── 白盒接线（读磁盘文本、去注释）─────────────────────────────────────────
        /// (d) 进入条件必须来自 `drive.IsEightBallShot`（**与 Runtime 共用的单点口径**，不许在这里拿
        ///     球号/球位/剩余数再算一遍）；写只走 `TryCallPocket` / `ClearCalledPocket`；
        ///     读只读 `CalledPocket`；被拒上升沿必须被消费。
        ///
        /// **怎么打死它（注入，均已实测）**：① 把进入条件改成**恒真**（`drive.IsEightBallShot` → `true`）
        /// ⇒ (d) 红；② 把**被拒上升沿的消费删掉** ⇒ (d) 红（玩家扣了扳机没有任何反馈）。
        ///
        /// ── 盲带（如实）────────────────────────────────────────────────────────
        /// · **3D 袋口高亮没做**（它要"桌上袋口 → 屏幕"的投影与场景接线，属表现层增量）⇒ 选袋条与桌上
        ///   袋口的对应只能靠"点了哪个亮哪个"。
        /// · **真实鼠标点击的端到端**属手动验收（与 `T102` 同口径）：判据锁的是纯函数 + 几何 + 接线 + 行为出口。
        /// </summary>
        [Test]
        public void T147_CallPocket_EntryLabelRefusal_AndNoSilence()
        {
            var failures = new List<string>();

            // ── (a) 进入时机（两向）──
            if (PracticeHudState.ResolveVisualState(ShotPhase.Aim, false, false, false, false, false, false, true)
                != HudVisualState.CallPocket)
            {
                failures.Add("T147(a) `callPocket: true` 时状态必须是 `call-pocket`。");
            }

            var phases = (ShotPhase[])System.Enum.GetValues(typeof(ShotPhase));
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
                                        HudVisualState state = PracticeHudState.ResolveVisualState(
                                            phase, foul, refused, hand, resultOpen, placement, aiTurn, false);
                                        if (state == HudVisualState.CallPocket)
                                        {
                                            failures.Add("T147(a) **普通杆**（`callPocket: false`）不得进入 "
                                                         + "`call-pocket`（否则整个游戏都在叫你选袋）。");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // ── (b) "已指定哪个袋"要逐号可分辨 ──
            string none = PracticeHudState.CalledPocketLine(-1, false);
            if (none != PracticeHudState.CallPocketDetail)
            {
                failures.Add("T147(b) `calledPocket < 0` 时应输出\"请你选袋\"那一句。");
            }

            var lines = new HashSet<string>();
            for (int i = 0; i < PracticeHudLayout.CallPocketSlots; i++)
            {
                string corner = PracticeHudState.CalledPocketLine(i, true);
                string side = PracticeHudState.CalledPocketLine(i, false);

                if (string.IsNullOrWhiteSpace(corner) || corner == none)
                {
                    failures.Add($"T147(b) 袋号 {i + 1} 的\"已指定\"行不得为空、也不得等于\"请你选袋\"那句。");
                }

                if (!corner.Contains((i + 1).ToString()))
                {
                    failures.Add($"T147(b) 袋号 {i + 1} 的\"已指定\"行里找不到它的号数（玩家看不出指定了哪个）。");
                }

                if (corner == side || !lines.Add(corner) || !lines.Add(side))
                {
                    failures.Add($"T147(b) 袋号 {i + 1} 的\"已指定\"行与别的组合重复 ⇒ 分不出指定了哪个袋。");
                }

                if (!corner.Contains(PracticeHudState.PocketKindLabel(true))
                    || !side.Contains(PracticeHudState.PocketKindLabel(false)))
                {
                    failures.Add($"T147(b) 袋号 {i + 1} 的\"已指定\"行里没有种类名（corner / side）。");
                }
            }

            if (lines.Count != PracticeHudLayout.CallPocketSlots * 2)
            {
                failures.Add($"T147(b) 6 个袋 × 2 种应有 {PracticeHudLayout.CallPocketSlots * 2} 条互不相同的"
                             + $"\"已指定\"行，实测 {lines.Count}。");
            }

            // ── (b2) 选袋条的几何：**必须落在球桌主区之外** ─────────────────────
            //  球桌投影取 `T95 (d)` 钉住的 16:9 打球口径那组值（`x[0.0499,0.9501] y[0.0940,0.7647]`）。
            //  ⚠ **口径一变必须回来重推**（与 `T95 (d)` 同纪律）：那里会先红，别在这里调数字。
            var table = new Rect(0.049899f, 0.093984f, 0.950101f - 0.049899f, 0.764702f - 0.093984f);
            if (PracticeHudLayout.CallPocketZone.yMin <= table.yMax)
            {
                failures.Add("T147(b2) 选袋条整体应在球桌主区**上方**"
                             + $"（实测 yMin={PracticeHudLayout.CallPocketZone.yMin:R} ≤ 球桌 yMax={table.yMax:R}）"
                             + " —— 不靠\"它是瞬态件\"豁免 HUD 的几何契约。");
            }

            for (int i = 0; i < PracticeHudLayout.CallPocketSlots; i++)
            {
                Rect r = PracticeHudLayout.CallPocketButtonRect(i, PracticeHudLayout.CallPocketSlots);
                if (r.width <= 0f || r.height <= 0f)
                {
                    failures.Add($"T147(b2) 第 {i + 1} 个袋号按钮的矩形为空。");
                    continue;
                }

                if (r.xMin < 0f || r.xMax > 1f || r.yMin < 0f || r.yMax > 1f)
                {
                    failures.Add($"T147(b2) 第 {i + 1} 个袋号按钮越出屏幕。");
                }

                if (r.Overlaps(table))
                {
                    failures.Add($"T147(b2) 第 {i + 1} 个袋号按钮与**球桌主区**相交 "
                                 + $"x[{r.xMin:R},{r.xMax:R}] y[{r.yMin:R},{r.yMax:R}]。");
                }
            }

            if (PracticeHudLayout.CallPocketButtonRect(-1, PracticeHudLayout.CallPocketSlots).width != 0f
                || PracticeHudLayout.CallPocketButtonRect(PracticeHudLayout.CallPocketSlots,
                        PracticeHudLayout.CallPocketSlots).width != 0f
                || PracticeHudLayout.CallPocketButtonRect(0, 0).width != 0f)
            {
                failures.Add("T147(b2) 越界 / 零槽位的按钮矩形应为**空矩形**（不对越界位置作任何主张）。");
            }

            // ── (c) 未指定 ⇒ 出杆被拒 + 不许静默 ─────────────────────────────
            if (PracticeHudState.CallPocketRefusedDetail == PracticeHudState.CallPocketDetail)
            {
                failures.Add("T147(c) \"被拒那一句\"与\"请你选袋\"那句相同 ⇒ 玩家扣了扳机等于什么都没看到。");
            }

            if (string.IsNullOrWhiteSpace(PracticeHudState.CallPocketRefusedDetail)
                || string.IsNullOrWhiteSpace(PracticeHudState.CallPocketPickerHint))
            {
                failures.Add("T147(c) 选袋提示 / 被拒提示不得为空。");
            }

            PhysicsTestDrive callDrive = null;
            GameObject callGo = null;
            try
            {
                callGo = new GameObject("T147Drive");
                callDrive = callGo.AddComponent<PhysicsTestDrive>();
                callDrive.tableConfig = AssetDatabase.LoadAssetAtPath<TableConfig>(DriveTableConfigPath);

                if (callDrive.tableConfig == null)
                {
                    failures.Add($"T147(c) 载不到 {DriveTableConfigPath} ⇒ 拒杆判据失效。");
                }
                else
                {
                    callDrive.Initialize();

                    // **前提：整盘静止**（`Launch()` 的第一道门控就是 `TableAtRest`；刚 `Initialize()` 还没满
                    // `RestStableFrames` 帧 ⇒ 会**先**被那道门拒，而它留的痕是 `RefusedLaunchCount`、
                    // **不是**本判据要的 `RefusedCallPocketCount`）。
                    // ⚠ 这是**前提**、不是被测行为：少了它判据会因为"门控顺序"而红，**归因就错了**
                    //   （实测踩过：`0 → 0` + 指定袋后仍被拒 —— 两个红都指向同一句"前提没量在干净起点上"）。
                    for (int i = 0; i < 240 && !callDrive.Simulator.TableAtRest; i++)
                    {
                        callDrive.Advance(PoolSimulator.FixedDeltaTime);
                    }

                    if (!callDrive.Simulator.TableAtRest)
                    {
                        failures.Add("T147(c) 前提不成立：推进 240 步后台面仍未静止"
                                     + " ⇒ `Launch()` 会被 `TableAtRest` 那道门先拒，本判据失去判别力。");
                    }

                    // 造"这一杆就是打黑 8"的局面：**只走 Runtime 的公开状态**（UI 侧不许反过来推）。
                    callDrive.ruleState = new RuleState
                    {
                        groupState = GroupState.Solid, solidRemaining = 0, stripeRemaining = 7,
                        calledPocket = -1, isBreak = false,
                    };

                    if (!callDrive.IsEightBallShot)
                    {
                        failures.Add("T147(c) 前提不成立：`Solid` + `solidRemaining = 0` 时应 `IsEightBallShot == true`。");
                    }

                    callDrive.ClearCalledPocket();
                    int refusedBefore = callDrive.RefusedCallPocketCount;
                    float speed = callDrive.Launch().magnitude;

                    if (speed > 0f)
                    {
                        failures.Add("T147(c) **打黑 8 却未指定袋** ⇒ 出杆必须被拒（实测打出去了）"
                                     + " —— 用户口径：未指定 ⇒ 不出杆。");
                    }

                    if (callDrive.RefusedCallPocketCount != refusedBefore + 1)
                    {
                        failures.Add("T147(c) 被拒必须留痕（`RefusedCallPocketCount` +1），实测 "
                                     + $"{refusedBefore} → {callDrive.RefusedCallPocketCount}"
                                     + " —— 没有留痕就没有\"不许静默\"的出口。");
                    }

                    // 正的那一向：指定合法袋后，同一局面必须放行
                    if (!callDrive.TryCallPocket(0) || callDrive.CalledPocket != 0)
                    {
                        failures.Add("T147(c) 指定袋号 0 应写入成功（`TryCallPocket` 是唯一写入口）。");
                    }
                    else if (callDrive.Launch().magnitude <= 0f)
                    {
                        failures.Add("T147(c) 已指定袋 ⇒ 打黑 8 那一杆必须放行（实测仍被拒）。");
                    }

                    // 取消指定
                    callDrive.ClearCalledPocket();
                    if (callDrive.CalledPocket != -1)
                    {
                        failures.Add("T147(c) `ClearCalledPocket` 之后应回到 -1（未指定）。");
                    }
                }
            }
            catch (System.Exception exception)
            {
                failures.Add("T147(c) 拒杆判据抛出异常：" + exception.Message);
            }
            finally
            {
                Object.DestroyImmediate(callGo);
            }

            // ── (d) 视图侧白盒接线 ──
            string hudSource = StripComments(ReadSourceOrNull(PracticeHudViewSourcePath));
            if (string.IsNullOrEmpty(hudSource))
            {
                failures.Add("T147(d) 读不到 `PracticeHudView.cs` 的磁盘文本 ⇒ 接线守卫失效。");
            }
            else
            {
                foreach ((string Usage, string Why) in new (string, string)[]
                         {
                             ("drive.IsEightBallShot",
                                 "进入条件必须来自 Runtime 的**单点口径**（在 UI 里再算一遍必然分叉）"),
                             ("drive.TryCallPocket(index)",
                                 "选袋只能走**唯一写入口** `TryCallPocket`（绕开就绕开了非法索引校验）"),
                             ("drive.ClearCalledPocket()",
                                 "取消指定要走 `ClearCalledPocket`（不许直接改 `CalledPocket`）"),
                             ("drive.CalledPocket",
                                 "选袋条的可见性/高亮只读 `CalledPocket`（本类不自己记\"选了哪个\"）"),
                             ("drive.RefusedCallPocketCount > _lastCallPocketRefusedSeen",
                                 "被拒的上升沿**必须被消费**并开一个可见窗口（否则扣了扳机没有任何反馈）"),
                             ("bool callPocket = drive != null && drive.IsEightBallShot && !drive.IsAiTurn;",
                                 "进入条件必须**同时**要求\"这是打黑 8 那杆\"与\"轮到玩家\""
                                 + "（缺前者 ⇒ 普通杆也叫你选袋；缺后者 ⇒ 对手回合弹选袋条）"),
                         })
                {
                    if (!hudSource.Contains(Usage))
                    {
                        failures.Add($"T147(d) 选袋接线缺失：「{Usage}」—— {Why}。");
                    }
                }
            }

            Assert.IsEmpty(failures, "T147 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T147: 进入时机两向 ✓；6 袋 × 2 种\"已指定\"行互不相同 ✓；"
                                  + "选袋条几何在球桌主区之外（且越界给空矩形）✓；未指定 ⇒ 被拒 + 留痕 + 已指定放行 ✓；"
                                  + "视图侧：`IsEightBallShot` / `TryCallPocket` / `ClearCalledPocket` / "
                                  + "`CalledPocket` / 被拒上升沿 接线 ✓；3D 袋口高亮**未做**（登记盲带）");
        }
    }
}
