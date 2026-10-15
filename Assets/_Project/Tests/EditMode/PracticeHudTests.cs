using System.Collections.Generic;
using Billiards.Core;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Billiards.Tests
{
    /// <summary>
    /// **最小 HUD 判据** —— `T95`（布局不变量）/ `T96`（状态与文案映射）/ `T97`（字体资产与覆盖）。
    ///
    /// 期望值来源：**甲**（`T95 (d)` 的投影期望值来自**外部工具独立复算**，见
    /// `Docs/35-A_Day13_最小HUD_spec.md` §3.3；它不调用 Unity，也不读被测实现）＋
    /// **乙**（`T96` 的文案/开关为契约常量）＋ **丙**（无）。
    ///
    /// **盲带（如实）**：本文件只保证**几何不变量**与**字符存在**。文字在块内的实际像素溢出、
    /// 颜色观感、间距舒适度**不在**覆盖内（批处理 `-nographics` 无渲染）；`Shoot` 的**点击接线**
    /// 也不在本单（按钮只做显示态）。
    ///
    /// **注入负对照（三组，均已实测红，且逐字节还原）**：
    /// ① `StatusZone` 的 `y` 上界改 `1.02` ⇒ `T95 (a)` 红；
    /// ② `StatusZone` 整体下移到 `y = 0.60` ⇒ `T95 (b)` 红（压到球桌主区）；
    /// ③ `CountersZone` 的 `x` 上界改 `0.40` ⇒ `T95 (c)` 红（与状态条重叠）；
    /// ④ `ShootEnabled` 对 `Moving` 返回 `true` ⇒ `T96 (b)` 红；
    /// ⑤ 标题字体的字符表清空（改指 `Inter-SemiBold` 之外的空资产不可行）⇒ 见 §"负对照说明"。
    /// </summary>
    public class PracticeHudTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        private const float BaseAspect = 16f / 9f;
        private const float BaseFov = GreyboxSceneSetup.CameraBaseVerticalFovDeg;
        private const float MaxFov = 100f;

        private static readonly Vector3 CameraPosition = GreyboxSceneSetup.CameraPosition;
        private static readonly Vector3 CameraForward =
            (GreyboxSceneSetup.CameraLookTarget - GreyboxSceneSetup.CameraPosition).normalized;

        /// <summary>六档宽高比（**含排期要求的两档** + 16:10 / 4:3 / 21:9 / 5:4 四档加严）。</summary>
        private static readonly (string Name, int Width, int Height)[] Aspects =
        {
            ("1280x720", 1280, 720),
            ("1920x1080", 1920, 1080),
            ("1920x1200", 1920, 1200),
            ("1024x768", 1024, 768),
            ("2560x1080", 2560, 1080),
            ("1024x1280", 1024, 1280),
        };

        // ════════════════ T95 HUD 布局不变量（六档宽高比）═══════════════════════

        /// <summary>
        /// 六档逐档校验：三块**不越界**、**不压球桌主区**、**互不重叠**；并对三档给**外部复算的期望值**
        /// （`x/y` 包围盒，容差 `1e-4`）；再做字号下限与**相机漂移守**。
        ///
        /// **可被打死**：`StatusZone.y` 抬到 `1.02` ⇒ (a) 红；整体下移到压住球桌 ⇒ (b) 红；
        /// `CountersZone` 加宽到 `0.40` ⇒ (c) 红；把 `PlayfieldVisualScale` 改掉 ⇒ (d) 红。
        /// </summary>
        [Test]
        public void T95_HudLayout_WithinBandAndClearOfTable()
        {
            var failures = new List<string>();
            var rootGo = new GameObject("T95PlayfieldRoot");
            rootGo.transform.localScale = Vector3.one * PoolConstants.PlayfieldVisualScale;

            try
            {
                PracticeHudRects hud = PracticeHudLayout.Resolve();
                var blocks = new (string Name, Rect R)[]
                {
                    ("计数组", hud.counters),
                    ("状态条", hud.status),
                    ("力度块", hud.power),
                };

                foreach ((string name, int width, int height) in Aspects)
                {
                    float aspect = (float)width / height;
                    float vfov = PracticeHudLayout.ResolveVerticalFovDeg(aspect, BaseAspect, BaseFov, MaxFov);
                    Rect table = PracticeHudLayout.TableViewportRect(
                        rootGo.transform, CameraPosition, CameraForward, vfov, aspect);

                    // (a) 无溢出
                    foreach ((string blockName, Rect r) in blocks)
                    {
                        if (r.xMin < 0f || r.xMax > 1f || r.yMin < 0f || r.yMax > 1f)
                        {
                            failures.Add($"(a) {name}：{blockName} 越出屏幕 —— "
                                         + $"x[{r.xMin:R},{r.xMax:R}] y[{r.yMin:R},{r.yMax:R}]。");
                        }
                    }

                    // (b) 不遮球桌主区
                    foreach ((string blockName, Rect r) in blocks)
                    {
                        if (r.Overlaps(table))
                        {
                            failures.Add($"(b) {name}：{blockName} 与球桌主区相交 —— "
                                         + $"块 x[{r.xMin:R},{r.xMax:R}] y[{r.yMin:R},{r.yMax:R}]，"
                                         + $"球桌 x[{table.xMin:R},{table.xMax:R}] y[{table.yMin:R},{table.yMax:R}]。");
                        }
                    }

                    // (c) 互不重叠
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        for (int j = i + 1; j < blocks.Length; j++)
                        {
                            if (blocks[i].R.Overlaps(blocks[j].R))
                            {
                                failures.Add($"(c) {name}：{blocks[i].Name} 与 {blocks[j].Name} 重叠。");
                            }
                        }
                    }

                    TestContext.WriteLine(
                        $"{name}: vfov={vfov:F6}° 球桌 x[{table.xMin:F6},{table.xMax:F6}] "
                        + $"y[{table.yMin:F6},{table.yMax:F6}]");
                }

                // (d) 与外部工具复算的期望值对齐（甲类）
                AssertProjection(rootGo.transform, 16f / 9f,
                    0.049899f, 0.950101f, 0.093984f, 0.764702f, "16:9", failures);
                AssertProjection(rootGo.transform, 4f / 3f,
                    0.049899f, 0.950101f, 0.195488f, 0.698526f, "4:3", failures);
                AssertProjection(rootGo.transform, 2560f / 1080f,
                    0.162425f, 0.837575f, 0.093984f, 0.764702f, "21:9", failures);

                // (e) 可读下限（1280×720 = 排期要求的小档）
                const int smallW = 1280;
                const int smallH = 720;
                float labelPx = PracticeHudLayout.RenderedFontPixels(
                    PracticeHudLayout.LabelFontSize, smallW, smallH);
                if (labelPx < PracticeHudLayout.MinReadablePixels)
                {
                    failures.Add($"(e) 1280×720 下标签字号仅 {labelPx:R} px，低于下限 "
                                 + $"{PracticeHudLayout.MinReadablePixels:R} px。");
                }

                float scale = PracticeHudLayout.CanvasScale(smallW, smallH);
                float buttonW = PracticeHudLayout.ShootButtonWidth * scale;
                float buttonH = PracticeHudLayout.ShootButtonHeight * scale;
                if (buttonW < 90f || buttonH < 28f)
                {
                    failures.Add($"(e) 1280×720 下 Shoot 按钮仅 {buttonW:R}×{buttonH:R} px（需 ≥ 90×28）。");
                }

                // (e') 内容必须装得进自己的分区（设计域像素，1080 基准）
                float powerZoneWidthRef = PracticeHudLayout.PowerZone.width * PracticeHudLayout.ReferenceWidth;
                if (PracticeHudLayout.ShootButtonWidth > powerZoneWidthRef)
                {
                    failures.Add($"(e') Shoot 按钮设计宽 {PracticeHudLayout.ShootButtonWidth:R} px 超出力度块 "
                                 + $"{powerZoneWidthRef:R} px。");
                }

                Assert.IsEmpty(failures, "T95 失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T95: 六档不越界/不压桌/不重叠 ✓；标签 {labelPx:F4} px；"
                                      + $"按钮 {buttonW:F2}×{buttonH:F2} px @1280×720 ✓");
            }
            finally
            {
                Object.DestroyImmediate(rootGo);
            }
        }

        /// <summary>
        /// **相机漂移守**：`T95 (d)` 的期望值把相机位姿当作**前提** —— 前提本身必须被守住，
        /// 否则 (d) 就变成对空气断言。本项从**已提交场景**回读主相机位姿与可视野缩放，
        /// 与 `GreyboxSceneSetup` 的公开常量逐项比对（口径同 `T43` 的场景回读式漂移守）。
        ///
        /// **可被打死**：改场景里相机的 `m_LocalPosition`（不动代码）⇒ 红。
        /// </summary>
        [Test]
        public void T95b_GreyboxScene_CameraPose_MatchesLayoutPremise()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Camera camera = null;
                Transform playfieldRoot = null;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                    {
                        if (candidate.CompareTag("MainCamera"))
                        {
                            camera = candidate;
                            break;
                        }
                    }

                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == GreyboxSceneSetup.PlayfieldRootObjectName)
                        {
                            playfieldRoot = child;
                            break;
                        }
                    }
                }

                Assert.IsNotNull(camera, "T95b：场景里找不到 tag = MainCamera 的相机。");
                Assert.IsNotNull(playfieldRoot, "T95b：场景里找不到 " + GreyboxSceneSetup.PlayfieldRootObjectName + "。");

                Vector3 expectedForward = (GreyboxSceneSetup.CameraLookTarget - GreyboxSceneSetup.CameraPosition).normalized;

                Assert.AreEqual(GreyboxSceneSetup.CameraPosition.x, camera.transform.position.x, 1e-6f,
                    $"T95b：相机 x = {camera.transform.position.x:R} ≠ 代码常量 {GreyboxSceneSetup.CameraPosition.x:R} ⇒ 取景漂移（HUD 布局前提失效）。");
                Assert.AreEqual(GreyboxSceneSetup.CameraPosition.y, camera.transform.position.y, 1e-6f,
                    $"T95b：相机 y = {camera.transform.position.y:R} ≠ 代码常量 {GreyboxSceneSetup.CameraPosition.y:R}。");
                Assert.AreEqual(GreyboxSceneSetup.CameraPosition.z, camera.transform.position.z, 1e-6f,
                    $"T95b：相机 z = {camera.transform.position.z:R} ≠ 代码常量 {GreyboxSceneSetup.CameraPosition.z:R}。");

                float forwardDot = Vector3.Dot(camera.transform.forward, expectedForward);
                Assert.Greater(forwardDot, 0.999999f,
                    $"T95b：相机朝向与 CameraLookTarget 推出的期望方向不一致（dot = {forwardDot:R}）。");

                Assert.AreEqual(GreyboxSceneSetup.CameraBaseVerticalFovDeg, camera.fieldOfView, 1e-4f,
                    $"T95b：相机 fieldOfView = {camera.fieldOfView:R} ≠ 代码常量 {GreyboxSceneSetup.CameraBaseVerticalFovDeg:R}。");

                Assert.AreEqual(PoolConstants.PlayfieldVisualScale, playfieldRoot.localScale.x, 1e-6f,
                    $"T95b：PlayfieldRoot 缩放 x = {playfieldRoot.localScale.x:R} ≠ {PoolConstants.PlayfieldVisualScale:R} ⇒ "
                    + "球桌投影期望值（T95 (d)）失效。");
                Assert.AreEqual(playfieldRoot.localScale.x, playfieldRoot.localScale.y, 1e-6f, "T95b：缩放非各向同性（y）。");
                Assert.AreEqual(playfieldRoot.localScale.x, playfieldRoot.localScale.z, 1e-6f, "T95b：缩放非各向同性（z）。");

                TestContext.WriteLine($"T95b: 相机 pose {camera.transform.position} fov {camera.fieldOfView} ✓；"
                                      + $"PlayfieldRoot scale {playfieldRoot.localScale.x:R} ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ════════════════ T96 状态与文案映射 ═══════════════════════════════════

        /// <summary>
        /// `ShotPhase` → 状态条文案 / `Shoot` 可用性；力度 → 百分比；计数 → `n / m`。
        /// **可被打死**：`ShootEnabled` 对 `Moving` 返回 `true` ⇒ (b) 红。
        /// </summary>
        [Test]
        public void T96_StateAndCopy_Mapping()
        {
            var failures = new List<string>();

            // (a) 玩家可操作的两态
            foreach (ShotPhase phase in new[] { ShotPhase.Aim, ShotPhase.Charging })
            {
                if (PracticeHudState.StateLabel(phase) != PracticeHudState.StateAim)
                {
                    failures.Add($"(a) {phase} 应显示 \"{PracticeHudState.StateAim}\"，"
                                 + $"实际 \"{PracticeHudState.StateLabel(phase)}\"。");
                }
            }

            if (!PracticeHudState.ShootEnabled(ShotPhase.Aim))
            {
                failures.Add("(a) Aim 下 Shoot 应可用（规格：**仅 Aim 为 true**）。");
            }

            if (PracticeHudState.ShootEnabled(ShotPhase.Charging))
            {
                failures.Add("(a) Charging 下 Shoot 应**禁用**（蓄力中已在拉杆，避免二次出杆语义）。");
            }

            // (b) 球未停的三态
            foreach (ShotPhase phase in new[] { ShotPhase.Shooting, ShotPhase.Moving, ShotPhase.Settle })
            {
                if (PracticeHudState.StateLabel(phase) != PracticeHudState.StateMoving)
                {
                    failures.Add($"(b) {phase} 应显示 \"{PracticeHudState.StateMoving}\"，"
                                 + $"实际 \"{PracticeHudState.StateLabel(phase)}\"。");
                }

                if (PracticeHudState.ShootEnabled(phase))
                {
                    failures.Add($"(b) {phase} 下 Shoot 必须**禁用**（球未停不可出杆）。");
                }
            }

            // (c) 力度百分比
            var powerCases = new (float Power, string Expected)[]
            {
                (0f, "0%"), (0.45f, "45%"), (1f, "100%"), (0.456f, "46%"),
                (-0.3f, "0%"), (1.7f, "100%"),
            };
            foreach ((float power, string expected) in powerCases)
            {
                string actual = PracticeHudState.PowerLabel(power);
                if (actual != expected)
                {
                    failures.Add($"(c) 力度 {power:R} 应显示 \"{expected}\"，实际 \"{actual}\"。");
                }
            }

            // (d) 计数
            if (PracticeHudState.PocketedLabel(0, 15) != "0 / 15")
            {
                failures.Add($"(d) PocketedLabel(0,15) 应为 \"0 / 15\"，实际 \"{PracticeHudState.PocketedLabel(0, 15)}\"。");
            }

            if (PracticeHudState.PocketedLabel(7, 7) != "7 / 7")
            {
                failures.Add($"(d) PocketedLabel(7,7) 应为 \"7 / 7\"，实际 \"{PracticeHudState.PocketedLabel(7, 7)}\"。");
            }

            Assert.IsEmpty(failures, "T96 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T96: 五相位 → 两态 / Shoot 门 / 力度 6 例 / 计数 2 例 ✓");
        }

        // ════════════════ T97 字体资产与字符覆盖 ═══════════════════════════════

        /// <summary>
        /// 5 个 TTF + 5 个 TMP FontAsset 在场；**源 TTF 的 `cmap` 必须覆盖 HUD 全部文案字符**；
        /// 字体分工（家族）正确；且每个资产确实由**同名的那个 TTF** 建成（引用相等，防接错字体文件）。
        ///
        /// ── 为什么判据落在「源 TTF 的 cmap」而不是「TMP 的字符表」（实测教训）────────
        /// 本单第一版判据查的是 `TMP_FontAsset.characterTable`，**红**。实测定位：
        /// `TryAddCharacters` 的预热结果**无法跨进程保留** —— 构建进程内存 95 条、磁盘 95 条，
        /// 而下一次 `-runTests` 进程跑完，文件回到 6.4 KB / 空表 / 图集 1×1。
        /// 但对 **`AtlasPopulationMode.Dynamic`** 的资产这**本来就不是缺陷**：动态资产的字形是
        /// **运行期按需从 `sourceFontFile` 补**的，预烘字符表只是预热优化。
        /// ⇒ 真正决定"能不能渲染出这些字"的是 **①资产在场 ②`sourceFontFile` 指向正确 ③源 TTF 的 cmap 覆盖**。
        /// 故判据改为直接读 `.ttf` 字节、解析 `cmap` 表（**读产物本身**，且不依赖 TMP 的内部状态）。
        ///
        /// **可被打死**：删任一 TTF / 任一 FontAsset ⇒ 红；把标题资产改指 Inter ⇒ (d) 红；
        /// 把源字体的 cmap 换成不含 `A` 的子集 ⇒ (c) 红。
        /// </summary>
        [Test]
        public void T97_FontAssets_PresentAndCoverHudCopy()
        {
            var failures = new List<string>();
            var loaded = new Dictionary<string, TMP_FontAsset>();
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

            // (a) 源 TTF + (b) FontAsset（含源引用非空）
            foreach ((string ttf, string asset) in HudAssetBuilder.FontTable)
            {
                string ttfPath = HudAssetBuilder.FontsDirectory + "/" + ttf;
                string assetPath = HudAssetBuilder.FontsDirectory + "/" + asset;

                var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
                if (font == null)
                {
                    failures.Add($"(a) 源字体缺失或不可载入：{ttfPath}");
                }

                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                if (fontAsset == null)
                {
                    failures.Add($"(b) TMP FontAsset 缺失：{assetPath}");
                    continue;
                }

                loaded[asset] = fontAsset;

                if (fontAsset.sourceFontFile == null)
                {
                    failures.Add($"(b) {asset} 的 sourceFontFile 为空 ⇒ 动态资产运行期无处补字形。");
                }

                // (e) 资产必须由同名 TTF 建成
                if (font != null && fontAsset.sourceFontFile != font)
                {
                    failures.Add($"(e) {asset} 的源字体不是 {ttf}"
                                 + $"（实际 {(fontAsset.sourceFontFile == null ? "null" : fontAsset.sourceFontFile.name)}）。");
                }
            }

            // (c) 源 TTF 的 cmap 覆盖 HUD 全部文案
            string copy = PracticeHudState.AllDisplayCopy();
            foreach ((string ttf, string asset) in HudAssetBuilder.FontTable)
            {
                string ttfPath = HudAssetBuilder.FontsDirectory + "/" + ttf;
                string full = System.IO.Path.Combine(projectRoot ?? ".", ttfPath);

                if (!TryReadCmapCodePoints(full, out HashSet<int> codePoints, out string error))
                {
                    failures.Add($"(c) 解析 {ttf} 的 cmap 失败：{error}");
                    continue;
                }

                var missing = new List<string>();
                foreach (char c in copy)
                {
                    if (!codePoints.Contains(c))
                    {
                        missing.Add("0x" + ((int)c).ToString("X2"));
                    }
                }

                if (missing.Count > 0)
                {
                    failures.Add($"(c) {ttf}（供 {asset}）的 cmap 缺 {missing.Count} 个码位："
                                 + string.Join(",", missing) + " ⇒ HUD 文案会缺字。");
                }
            }

            // (d) 字体分工：Barlow Condensed = 标题族；Inter = 正文族；两族必须来自不同源字体
            Font titleSource = loaded.TryGetValue(FileName(HudAssetBuilder.TitleFontAssetPath), out TMP_FontAsset tf)
                ? tf.sourceFontFile : null;
            Font bodySource = loaded.TryGetValue(FileName(HudAssetBuilder.BodyFontAssetPath), out TMP_FontAsset bf)
                ? bf.sourceFontFile : null;

            if (titleSource == null || !titleSource.name.Contains("Barlow"))
            {
                failures.Add($"(d) 标题字体应来自 Barlow Condensed，实际 "
                             + $"{(titleSource == null ? "null" : titleSource.name)}。");
            }

            if (bodySource == null || !bodySource.name.Contains("Inter"))
            {
                failures.Add($"(d) 正文字体应来自 Inter，实际 {(bodySource == null ? "null" : bodySource.name)}。");
            }

            if (titleSource != null && bodySource != null && titleSource == bodySource)
            {
                failures.Add("(d) 标题与正文指向**同一个**源字体 ⇒ 分工失效。");
            }

            Assert.IsEmpty(failures, "T97 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T97: 5 TTF + 5 FontAsset ✓；文案 {copy.Length} 字符在 5 个源 TTF 的 cmap 中全覆盖 ✓；"
                                  + $"标题族 {titleSource?.name} / 正文族 {bodySource?.name} ✓");
        }

        // ════════════════ 辅助 ═══════════════════════════════════════════════

        private static string FileName(string assetPath)
        {
            return assetPath.Substring(assetPath.LastIndexOf('/') + 1);
        }

        // ════════════════ TTF `cmap` 读取（小端；format 4）══════════════════════

        /// <summary>
        /// 读 `.ttf` 的 **cmap format 4** 子表，返回**真正映射到非零字形**的码位集合。
        ///
        /// 为什么自己解析而不问 TMP：这条判据要验的是**字体产物本身**（铁律 10：守卫强度 =
        /// 守卫读取的东西）。TMP 的字符表对 Dynamic 资产只是运行期预热缓存，不是真相。
        ///
        /// **支持域（如实）**：只解析 `cmap` **format 4**（BMP）。本工程 5 个 TTF 实测均只有
        /// `(0,3,4)` 与 `(3,1,4)` 两个 format-4 子表 ⇒ 够用。遇到 format 12（UCS-4）子表时
        /// 本函数**不会**去读它 ⇒ 若将来换字体成非 BMP 方案，需扩面（届时会表现为"缺码位"红，不会静默放过）。
        /// </summary>
        private static bool TryReadCmapCodePoints(string fullPath, out HashSet<int> codePoints, out string error)
        {
            codePoints = new HashSet<int>();
            error = null;

            if (!System.IO.File.Exists(fullPath))
            {
                error = "文件不存在 " + fullPath;
                return false;
            }

            byte[] data = System.IO.File.ReadAllBytes(fullPath);
            if (data.Length < 12)
            {
                error = "文件过短";
                return false;
            }

            int tableCount = UInt16(data, 4);
            int cmap = -1;
            for (int i = 0; i < tableCount; i++)
            {
                int record = 12 + i * 16;
                if (record + 16 > data.Length)
                {
                    break;
                }

                if (data[record] == 'c' && data[record + 1] == 'm' && data[record + 2] == 'a' && data[record + 3] == 'p')
                {
                    cmap = (int)UInt32(data, record + 8);
                    break;
                }
            }

            if (cmap < 0 || cmap + 4 > data.Length)
            {
                error = "无 cmap 表";
                return false;
            }

            int subtableCount = UInt16(data, cmap + 2);
            int format4 = -1;
            for (int i = 0; i < subtableCount; i++)
            {
                int record = cmap + 4 + i * 8;
                if (record + 8 > data.Length)
                {
                    break;
                }

                int platform = UInt16(data, record);
                int encoding = UInt16(data, record + 2);
                int offset = (int)UInt32(data, record + 4);
                int at = cmap + offset;
                if (at + 2 > data.Length)
                {
                    continue;
                }

                if (UInt16(data, at) != 4)
                {
                    continue;
                }

                if (platform == 3 && encoding == 1)
                {
                    format4 = at;
                    break;
                }

                if (format4 < 0)
                {
                    format4 = at;   // 兜底：(0,3,4) 等
                }
            }

            if (format4 < 0)
            {
                error = "无 format 4 子表";
                return false;
            }

            int segCountX2 = UInt16(data, format4 + 6);
            int segCount = segCountX2 / 2;
            int endCodes = format4 + 14;
            int startCodes = endCodes + segCountX2 + 2;   // +2 = reservedPad
            int idDeltas = startCodes + segCountX2;
            int idRangeOffsets = idDeltas + segCountX2;

            if (idRangeOffsets + segCountX2 > data.Length)
            {
                error = "format 4 段表越界";
                return false;
            }

            for (int i = 0; i < segCount; i++)
            {
                int end = UInt16(data, endCodes + i * 2);
                int start = UInt16(data, startCodes + i * 2);
                int delta = Int16(data, idDeltas + i * 2);
                int rangeOffset = UInt16(data, idRangeOffsets + i * 2);
                int rangeOffsetAddress = idRangeOffsets + i * 2;

                if (start > end)
                {
                    continue;
                }

                for (int c = start; c <= end; c++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                    {
                        glyph = (c + delta) & 0xFFFF;
                    }
                    else
                    {
                        int at = rangeOffsetAddress + rangeOffset + (c - start) * 2;
                        if (at < 0 || at + 2 > data.Length)
                        {
                            continue;
                        }

                        glyph = UInt16(data, at);
                        if (glyph != 0)
                        {
                            glyph = (glyph + delta) & 0xFFFF;
                        }
                    }

                    if (glyph != 0)
                    {
                        codePoints.Add(c);
                    }
                }
            }

            if (codePoints.Count == 0)
            {
                error = "cmap 解析出 0 个码位";
                return false;
            }

            return true;
        }

        // ⚠ **TTF/OpenType 全表用大端**（`sfntVersion = 0x00010000`、`numTables = 0x0011`）。
        // 写成小端会把 `numTables` 读成 4352、扫遍垃圾后报"无 cmap 表"（实测踩过）。
        private static int UInt16(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        private static int Int16(byte[] data, int offset)
        {
            int value = UInt16(data, offset);
            return value >= 0x8000 ? value - 0x10000 : value;
        }

        private static long UInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static void AssertProjection(Transform playfieldRoot, float aspect,
                                             float expXMin, float expXMax, float expYMin, float expYMax,
                                             string label, List<string> failures)
        {
            // ⚠ 期望值来自**外部工具独立复算**（规格 §3.3），不是从被测实现取的数。
            const float tolerance = 1e-4f;
            float vfov = PracticeHudLayout.ResolveVerticalFovDeg(aspect, BaseAspect, BaseFov, MaxFov);
            Rect table = PracticeHudLayout.TableViewportRect(playfieldRoot, CameraPosition, CameraForward, vfov, aspect);

            Check("xMin", table.xMin, expXMin);
            Check("xMax", table.xMax, expXMax);
            Check("yMin", table.yMin, expYMin);
            Check("yMax", table.yMax, expYMax);

            void Check(string field, float actual, float expected)
            {
                if (Mathf.Abs(actual - expected) > tolerance)
                {
                    failures.Add($"(d) {label} 球桌投影 {field} = {actual:R}，期望 {expected:R}"
                                 + $"（外部复算，容差 {tolerance:R}）。");
                }
            }
        }
    }
}
