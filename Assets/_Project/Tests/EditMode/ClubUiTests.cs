using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Billiards.EditorTools;
using Billiards.Physics;
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
        };

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
        /// ④ **酒红只给犯规/失败、亮青只给指定袋**：双向断言（该用的必须用、不该用的一个都不许用）；
        /// ⑤ `ResolveVisualState` 的优先级表（结算 &gt; 被拒 &gt; 犯规 &gt; 自由球 &gt; 相位）；
        /// ⑥ 犯规脉冲是 0.8 s 的一次性 ease-out（端点 + 单调）。
        ///
        /// **注入负对照**：把 `CallPocketSymbol` 改成黄铜 ⇒ ④ 红；
        /// 把 `AiTurn` 从 `UnreachableStates` 删掉 ⇒ ② 红；把两个状态的标题改成一样 ⇒ ③ 红。
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

            // ② 不可达集合恰好三项
            var unreachable = new HashSet<HudVisualState>(PracticeHudState.UnreachableStates);
            var expectedUnreachable = new HashSet<HudVisualState>
            {
                HudVisualState.AiTurn, HudVisualState.CallPocket, HudVisualState.Paused,
            };

            foreach (HudVisualState state in expectedUnreachable)
            {
                if (!unreachable.Contains(state))
                {
                    failures.Add($"② {state} 应当登记为\"本卡不可达\"（35-A 没有 AI / 规则引擎 / 暂停）。");
                }
            }

            foreach (HudVisualState state in unreachable)
            {
                if (!expectedUnreachable.Contains(state))
                {
                    failures.Add($"② {state} 被登记成不可达，但它在本卡是可触发的 ⇒ 登记与实际不符。");
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
                                  + "标题/说明/图标互异 ✓；酒红仅犯规、亮青仅指定袋 ✓；优先级 9 例 ✓；脉冲 0.8 s ✓");
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
    }
}
