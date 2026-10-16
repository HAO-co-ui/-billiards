using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **瞄准辅助表现层**：`T79`（绘制过显示层）/ `T80`（虚线可判定）/ `T81`（方向单点 + 180° 守卫）/
    /// `T82`（开关：关掉即不算）—— Day 9 切片 B。
    ///
    /// ── 期望值来源 ───────────────────────────────────────────────────────────
    /// · **甲** = 独立闭式 / 引擎原语（`T79a` 三路、`T80b` 段数闭式、`T81` 契约式）；
    /// · **乙** = 契约常量 / 场景序列化值 / 只读出口（`T79c`、`T80c/d`、`T82`）；
    /// · **乙-系数选定** = `T80d` 的 `MinGapWorld`（实测填值）。**丙 = 0**。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · 无法捕获 `Gizmos` / `LineRenderer` 的**实际**顶点 ⇒ "是否真的调用 `DrawPoint`"由**源码守卫**补。
    /// · 观感（虚线好不好看）不作验收；真实帧率 / 输入设备级触发留 Manual。
    /// </summary>
    public class AimAssistViewTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string ViewSourcePath = "Assets/_Project/Scripts/Runtime/Physics/AimAssistView.cs";
        private const string ControllerSourcePath = "Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs";

        /// <summary>逐行剥掉 `//` 之后注释（`T80(a2)` 白盒守卫用；命名带后缀避免与既有助手重名）。</summary>
        private static string StripLineCommentsT80(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            return sb.ToString();
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        /// <summary>去掉注释行后的代码（源码守卫用；跳过 `//` 与 `///` 行）。</summary>
        private static string CodeWithoutCommentLines(string path)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//"))
                {
                    continue;
                }

                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        // ══════════════════ T79 绘制坐标过显示层 ═════════════════════════════════

        /// <summary>
        /// **`AimAssistView.DrawPoint` 必须过显示层单点**：对幽灵球 / 路径两端 / 目标出射线端点，
        /// 与三路期望比对 —— ① `PlayfieldView.DisplayFromCanonical`（强制口径）
        /// ② 引擎原语 `Transform.TransformPoint` ③ 独立闭式 `canonical × PlayfieldVisualScale`（仅 1.2 根）；
        /// 并加**合成根**（1.7× + 平移 + 绕 Y 旋转）杀"硬编码倍数"，以及**源码守卫**（代码行无字面量 `1.2`）。
        ///
        /// 期望值来源：**甲**（三路独立 ⇒ 同源恒真不成立）。**盲带**：抓不到 `Gizmos` 实际顶点。
        /// **判别力**：Day 8 切片 A 刚因"直画规范世界坐标"被修过一次（`T71`）⇒ 本条防重犯。
        /// **注入负对照**：`DrawPoint` 改成 `return canonicalWorldPoint;` ⇒ **(a) FAIL**；代码行写死 `1.2` ⇒ **(b) FAIL**。
        /// </summary>
        [Test]
        public void T79_DrawPoints_GoThroughDisplayLayer()
        {
            var failures = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform root = FindByName(scene, "PlayfieldRoot");
                Assert.IsNotNull(root, "T79：场景内找不到 PlayfieldRoot。");

                float scale = PoolConstants.PlayfieldVisualScale;

                // 采样点：幽灵球 / 路径两端 / 目标出射线端点（物理平面量）
                Vector2[] physicsPoints =
                {
                    new Vector2(0f, 0f),
                    new Vector2(-0.40f, 0.30f),
                    new Vector2(0.51f, 0f),
                    new Vector2(0.0999f, -0.2445f),
                };

                float maxPlv = 0f;
                float maxEngine = 0f;
                float maxNumeric = 0f;

                foreach (Vector2 phys in physicsPoints)
                {
                    Vector3 canonicalGround = PoolGeometry.PhysicsToUnity(phys);

                    Vector3 got = AimAssistView.DrawPoint(root, canonicalGround);
                    Vector3 ePlv = PlayfieldView.DisplayFromCanonical(root, canonicalGround);
                    Vector3 eEngine = root.TransformPoint(canonicalGround);
                    Vector3 eNumeric = new Vector3(
                        canonicalGround.x * scale, canonicalGround.y * scale, canonicalGround.z * scale);

                    maxPlv = Mathf.Max(maxPlv, (got - ePlv).magnitude);
                    maxEngine = Mathf.Max(maxEngine, (got - eEngine).magnitude);
                    maxNumeric = Mathf.Max(maxNumeric, (got - eNumeric).magnitude);
                }

                if (!(maxPlv <= 1e-6f))
                {
                    failures.Add($"(a) 绘制点 ≠ PlayfieldView 显示映射点（最大差 {maxPlv:E3} m）⇒ 绘制未过显示层。");
                }

                if (!(maxEngine <= 1e-6f))
                {
                    failures.Add($"(a) 绘制点 ≠ 引擎 TransformPoint（最大差 {maxEngine:E3} m）。");
                }

                if (!(maxNumeric <= 1e-6f))
                {
                    failures.Add($"(a) 绘制点 ≠ 规范点逐分量 ×{scale}（最大差 {maxNumeric:E3} m）。");
                }

                // 合成根（非 1.2 / 带平移 + 绕 Y 旋转）⇒ 杀"硬编码倍数"
                var synth = new GameObject("T79SynthRoot");
                try
                {
                    synth.transform.position = new Vector3(0.3f, -0.15f, 0.22f);
                    synth.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                    synth.transform.localScale = Vector3.one * 1.7f;

                    float synthScale = AimAssistView.DrawScale(synth.transform);
                    if (!(Mathf.Abs(synthScale - 1.7f) <= 1e-5f))
                    {
                        failures.Add($"(a) 合成根显示尺度 {synthScale:F6} ≠ 1.7（`DrawScale` 未跟随根缩放）。");
                    }

                    Vector3 canonical = PoolGeometry.PhysicsToUnity(new Vector2(0.2f, -0.3f));
                    Vector3 gotSynth = AimAssistView.DrawPoint(synth.transform, canonical);
                    Vector3 expSynth = synth.transform.TransformPoint(canonical);
                    if (!((gotSynth - expSynth).magnitude <= 1e-6f))
                    {
                        failures.Add($"(a) 合成根绘制点 {gotSynth} ≠ TransformPoint {expSynth}（平移/旋转未被纳入）。");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(synth);
                }

                // (b) 源码守卫：代码行不得出现字面量 1.2
                string code = CodeWithoutCommentLines(ViewSourcePath);
                if (code.Contains("1.2"))
                {
                    failures.Add("(b) `AimAssistView.cs` 代码行出现字面量 `1.2` ⇒ 违反\"不得手写固定倍数\"。");
                }

                // (c) 退化
                if (!(Mathf.Abs(AimAssistView.DrawScale(null) - 1f) <= 1e-9f))
                {
                    failures.Add("(c) `DrawScale(null)` 应为 1（scale 1 退化）。");
                }

                Vector3 probe = new Vector3(1.1f, 0.2f, -0.4f);
                if (!(AimAssistView.DrawPoint(null, probe) == probe))
                {
                    failures.Add("(c) `DrawPoint(null, p)` 应原样返回 p。");
                }

                Assert.IsEmpty(failures, "T79 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T79: 绘制点 == 显示映射（max {maxPlv:E2}）/ == TransformPoint（max {maxEngine:E2}）"
                                      + $" / == ×{scale}（max {maxNumeric:E2}）；合成根 1.7× 通过；源码无字面量 1.2 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T80 虚线"清晰、不糊"的可判定形式 ══════════════════════

        /// <summary>
        /// **四条数值化判据**（禁主观词）：① 间隙非零；② 段数闭式 `max(1, ceil(L/(dash+gap)))` 且除末段外每段恰为 `dash`；
        /// ③ 世界段长 == 规范段长 × `DrawScale(root)`；④ 世界间隙 ≥ `MinGapWorld`（屏幕可辨下限，实测填值）。
        ///
        /// 期望值来源：**甲**（段数闭式）＋ **乙**（`DrawScale` / 契约常量）＋ **乙-系数选定**（`MinGapWorld`）。
        /// **盲带**：不判观感。**注入负对照**：`gapLength = 0` ⇒ **(a)(b) FAIL**；段长不乘 `DrawScale` ⇒ **(c) FAIL**。
        /// </summary>
        [Test]
        public void T80_DashSegments_AreDecidable()
        {
            var failures = new List<string>();

            // (a) **实线口径**（2026-10-16 用户截图反馈："线都断了" ⇒ 白线改实线）
            //   ① **新口径**：白线走**实线**（整条折线 = **一个** `LineRenderer`，见 `AimAssistView.SyncPathLine`）
            //      ⇒ 间隙**不再参与**；`PredictionGapLength` 取 **0** 作为"实线"的**可判定表征**。
            //   ② **旧口径的设计意图（保留可读）**：`gap > 0` 守的是"**别把线画成一坨糊**" ——
            //      当时是**亚像素间隙逼近连续**（`0.002 × DrawScale 1.2 = 0.0024 m ≈ 1.4 px` @596.04 px/米），
            //      即"接近连续瞄准线"，**不是**"必须看到虚线段"。⇒ 用户这次要求走到**极限情形 = 实线**。
            //   ③ **若将来要改回虚线 ⇒ 只改这一处**：`AimAssistView.PredictionGapLength`（取 > 0）
            //      ＋ 绘制路径从 `SyncPathLine`（单折线）回到按弧长铺段（`AppendPolylineDashSegments`）。
            if (!(AimAssistView.PredictionGapLength == 0f))
            {
                failures.Add($"(a) 实线口径：`PredictionGapLength` 应为 0（实测 {AimAssistView.PredictionGapLength:R}）。见本条注释 ①②③。");
            }

            // (a2) **白盒实线守卫**（读磁盘文本 + 剥注释；与 `⑤'''`/④' 同法）：
            //   白线的绘制路径**必须**是"整条折线"那条（`SyncPathLine(`），且**不得**再走按弧长铺段的旧路径。
            //   **怎么打死它**：把绘制改回 `AppendPolylineDashSegments(...)` ⇒ 本条红。
            string viewSrc = StripLineCommentsT80(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), ViewSourcePath)));
            if (!viewSrc.Contains("SyncPathLine(prediction, path)"))
            {
                failures.Add("(a2) 白线未走实线路径：源码里找不到 `SyncPathLine(prediction, path)` 调用。");
            }

            // ⚠⚠ **本条第一版踩了一个"已记录教训的复发"（如实记录）**：它判的是**方法名**
            //   `Contains("AppendPolylineDashSegments(")` ⇒ **命中的是方法定义**（那个公开纯函数本轮**刻意保留**、
            //   且 `T80(b)` 直接测它）⇒ **恒红**（基线也红 ⇒ 一度把 INJ-A 的红名单污染成 2 条）。
            //   同族上一处 = **批次 B 的 `T133` 空转守卫**（`Contains("SampleScenePeaks(")` 命中**定义**、
            //   `Contains("TotalPredictSteps")` 命中**属性声明**）⇒ 结论早已是"**源码级守卫必须写使用/调用形态**"，
            //   而**它在规格里、却在新地方又犯一次** ⇒ 故本处补**机械化对策**（见规格 §12.3 ⑥）。
            // ⚠ 判**旧调用点**（`path, PredictionDashLength`），**不是**判方法名 ——
            //   公开纯函数 `AppendPolylineDashSegments` / `BuildDashSegments` 仍保留（`T80(b)` 直接测它们），
            //   它们的方法**定义**里也含这个名字 ⇒ 只判名字会**恒红**（本条第一版就踩了这个坑，已修）。
            if (viewSrc.Contains("AppendPolylineDashSegments(path, PredictionDashLength"))
            {
                failures.Add("(a2) 白线仍走按弧长铺段的旧路径（旧调用点 `AppendPolylineDashSegments(path, PredictionDashLength…` 仍在）⇒ 实线口径未生效。");
            }

            float dash = AimAssistView.PredictionDashLength;
            float gap = AimAssistView.PredictionGapLength;
            if (!(dash > 0f))
            {
                failures.Add($"(a) 段长 {dash:R} 不为正。");
            }

            // (b) 段数闭式 + 段长上界/等值
            Vector2 from = new Vector2(-0.4f, 0.1f);
            float[] lengths = { 0.02f, 0.085f, 0.1f, 0.2f, 0.5f, 1.0f, 2.5f };
            var detail = new List<string>();

            foreach (float len in lengths)
            {
                Vector2 to = from + new Vector2(len, 0f);
                float L = (to - from).magnitude;   // 与 SUT 同口径：L 是**端点距**（不是构造用的 `len`，二者可差 1 ulp）
                IReadOnlyList<(Vector2 a, Vector2 b)> segs = AimAssistView.BuildDashSegments(from, to, dash, gap);

                int expectedCount = Mathf.Max(1, Mathf.CeilToInt(L / (dash + gap)));
                if (segs.Count != expectedCount)
                {
                    failures.Add($"(b) L={L:R}：段数 {segs.Count} ≠ 闭式 {expectedCount}。");
                    continue;
                }

                for (int i = 0; i < segs.Count; i++)
                {
                    float segLen = (segs[i].b - segs[i].a).magnitude;
                    if (!(segLen <= dash + 1e-6f))
                    {
                        failures.Add($"(b) L={L:R} 第 {i} 段长 {segLen:R} > dash {dash:R}。");
                    }

                    if (i < segs.Count - 1 && !(Mathf.Abs(segLen - dash) <= 1e-6f))
                    {
                        failures.Add($"(b) L={L:R} 第 {i} 段（非末段）长 {segLen:R} ≠ dash {dash:R}。");
                    }

                    // 端点必须落在原线段上（同向、共线）
                    float along = Vector2.Dot(segs[i].a - from, (to - from).normalized);
                    if (!(along >= -1e-6f))
                    {
                        failures.Add($"(b) L={L:R} 第 {i} 段起点越过线段起点（along={along:R}）。");
                    }
                }

                // (b2) **实线（gap = 0）⇒ 相邻段必须首尾相接**（逐位）⇒ 视觉连续（这是"实线"的正面表征）
                if (gap == 0f)
                {
                    for (int i = 0; i + 1 < segs.Count; i++)
                    {
                        if (segs[i].b != segs[i + 1].a)
                        {
                            failures.Add($"(b2) L={L:R}：间隙为 0 但第 {i} 段末点 ≠ 第 {i + 1} 段起点 ⇒ 线不连续。");
                            break;
                        }
                    }
                }

                float lastEnd = Vector2.Dot(segs[segs.Count - 1].b - from, (to - from).normalized);
                if (!(lastEnd <= L + 1e-6f))
                {
                    failures.Add($"(b) L={L:R} 末段终点越过线段终点（{lastEnd:R} > {L:R}）。");
                }

                detail.Add($"L={L:R}⇒n={segs.Count}");
            }

            // (c) 世界尺度成比例
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform root = FindByName(scene, "PlayfieldRoot");
                Assert.IsNotNull(root, "T80：场景内找不到 PlayfieldRoot。");

                float drawScale = AimAssistView.DrawScale(root);
                if (!(Mathf.Abs(drawScale - PoolConstants.PlayfieldVisualScale) <= 1e-6f))
                {
                    failures.Add($"(c) `DrawScale(root)` = {drawScale:R} ≠ PlayfieldVisualScale {PoolConstants.PlayfieldVisualScale:R}。");
                }

                // 单段虚线的**世界**段长 == 规范段长 × DrawScale（逐段核对前若干段）
                var segs = AimAssistView.BuildDashSegments(new Vector2(0f, 0f), new Vector2(0.3f, 0.2f), dash, gap);
                for (int i = 0; i < segs.Count; i++)
                {
                    Vector3 wa = AimAssistView.DrawPoint(root, new Vector3(segs[i].a.x, 0f, segs[i].a.y));
                    Vector3 wb = AimAssistView.DrawPoint(root, new Vector3(segs[i].b.x, 0f, segs[i].b.y));
                    float worldLen = (wb - wa).magnitude;
                    float canonicalLen = (segs[i].b - segs[i].a).magnitude;
                    if (!(Mathf.Abs(worldLen - canonicalLen * drawScale) <= 1e-6f))
                    {
                        failures.Add($"(c) 第 {i} 段世界长 {worldLen:R} ≠ 规范长 {canonicalLen:R} × 尺度 {drawScale:R}。");
                    }
                }

                // (d) 屏幕可辨下限 —— ⚠ **实线口径下不适用**（无间隙）：仅当 `gap > 0`（将来改回虚线）时才判。
                //   历史：该条守的是"如果画虚线，间隙至少 1 px（不然看不出是虚线、只见抖动）"。
                float worldGap = gap * drawScale;
                if (gap > 0f)
                {
                    if (!(worldGap >= AimAssistView.MinGapWorld))
                    {
                        failures.Add($"(d) 世界间隙 {worldGap:R} m < MinGapWorld {AimAssistView.MinGapWorld:R} m（屏幕 <1 px）。");
                    }
                }
                else
                {
                    detail.Add("实线⇒(d) 世界间隙不适用（无间隙）");
                }

                string gapNote = gap > 0f
                    ? $"gap={gap:R} > 0、世界间隙 {worldGap:R} m ≥ MinGapWorld {AimAssistView.MinGapWorld:R} m"
                    : "**gap=0 ⇒ 实线口径**（(d) 世界间隙不适用；(a2) 白盒守卫：白线走 SyncPathLine）";
                TestContext.WriteLine($"T80: {gapNote}；{string.Join(" ", detail)}；DrawScale={drawScale:R} ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            Assert.IsEmpty(failures, "T80 失败判据：\n" + string.Join("\n", failures));
        }

        // ══════════════════ T81 方向单点 + 180° 反向回归守卫 ══════════════════════

        /// <summary>
        /// **世界方向只由 `PoolGeometry.PhysicsDirectionToUnity` 导出**（预测侧不得自成一派）：
        /// ① ≥3 个非轴向方向下 `WorldDirection(d)` 与契约式、与 `PointerAimSolver.WorldDirectionFromPhysics(d)` 逐分量一致；
        /// ② 定值锚定 `(cos30°, sin30°) → (−cos30°, 0, −sin30°)`（**不是**旧口径 `(cos30°, 0, sin30°)`）；
        /// ③ 源码守卫：两个新文件代码行不得手写 `(−x, −z)` 类映射。
        ///
        /// 期望值来源：**甲**（`PoolGeometry` 契约式）。**盲带**：不覆盖输入设备级拖拽。
        /// **判别力**：`T47` 是 180° 反向的回归守卫；本条把**预测侧**也钉到同一入口。
        /// **注入负对照**：方向写成 `(cosθ, 0, sinθ)`（旧口径）⇒ **(a)(b) FAIL**（偏差 180°）。
        /// </summary>
        [Test]
        public void T81_WorldDirection_IsSinglePoint_AndNotReversed()
        {
            var failures = new List<string>();

            Vector2[] physicsDirections =
            {
                new Vector2(Mathf.Cos(20f * Mathf.Deg2Rad), Mathf.Sin(20f * Mathf.Deg2Rad)),
                new Vector2(Mathf.Cos(115f * Mathf.Deg2Rad), Mathf.Sin(115f * Mathf.Deg2Rad)),
                new Vector2(Mathf.Cos(250f * Mathf.Deg2Rad), Mathf.Sin(250f * Mathf.Deg2Rad)),
                new Vector2(Mathf.Cos(337f * Mathf.Deg2Rad), Mathf.Sin(337f * Mathf.Deg2Rad)),
            };

            foreach (Vector2 d in physicsDirections)
            {
                Vector3 got = AimAssistView.WorldDirection(d);
                Vector3 expected = PoolGeometry.PhysicsDirectionToUnity(d);
                Vector3 viaSolver = PointerAimSolver.WorldDirectionFromPhysics(d);

                if ((got - expected).magnitude > 1e-6f)
                {
                    failures.Add($"(a) `WorldDirection({d})` = {got} ≠ PoolGeometry 契约式 {expected}。");
                }

                if ((got - viaSolver).magnitude > 1e-6f)
                {
                    failures.Add($"(a) `WorldDirection({d})` = {got} ≠ PointerAimSolver.WorldDirectionFromPhysics {viaSolver}。");
                }
            }

            // (b) 定值锚定：30° ⇒ (−cos30°, 0, −sin30°)
            float c30 = Mathf.Cos(30f * Mathf.Deg2Rad);
            float s30 = Mathf.Sin(30f * Mathf.Deg2Rad);
            Vector3 anchored = AimAssistView.WorldDirection(new Vector2(c30, s30));
            var expectedAnchored = new Vector3(-c30, 0f, -s30);
            var reversedOld = new Vector3(c30, 0f, s30);

            if ((anchored - expectedAnchored).magnitude > 1e-6f)
            {
                failures.Add($"(b) `WorldDirection(cos30°, sin30°)` = {anchored} ≠ (−cos30°, 0, −sin30°) = {expectedAnchored}"
                             + $"（偏差 {(anchored - expectedAnchored).magnitude:E3}；若 == {reversedOld} 则是 180° 反向的旧口径）。");
            }

            if ((anchored - reversedOld).magnitude < 0.5f)
            {
                failures.Add($"(b) 实测方向 {anchored} 恰为**旧口径** `(cos30°, 0, sin30°)` ⇒ 180° 反向（`5189d4d` 的缺陷回归）。");
            }

            // (c) 源码守卫：不得手写 (−x, −z) 类映射
            foreach (string path in new[] { ViewSourcePath, ControllerSourcePath })
            {
                string code = CodeWithoutCommentLines(path);
                string name = Path.GetFileName(path);
                if (code.Contains("new Vector3(-") || code.Contains("-d.x") || code.Contains("-physDir"))
                {
                    failures.Add($"(c) `{name}` 代码行出现手写负向映射（第二处方向转换点，违铁律 16）。");
                }
            }

            Assert.IsEmpty(failures, "T81 失败判据：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T81: {physicsDirections.Length} 个方向 == PoolGeometry 契约式 == PointerAimSolver；"
                                  + $"30° ⇒ {anchored} ✓");
        }

        // ══════════════════ T82 开关：关掉即不算、可观测、恢复 ═════════════════════

        /// <summary>
        /// **四条**：① 关闭期间 `PredictionComputeCount` **逐位不变**（关闭即不算，而非"只藏不算"）；
        /// ② 关闭期间 `Prediction` 逐位不变；③ `drive.PredictionEnabled` == `drive.enableShotPrediction`（唯一读点）；
        /// ④ 重新开启后计数**增长**（恢复计算，非空转）。
        /// 另加**源码守卫**：控制器不得调用内核写入口（`Step` / `PlaceBall` / `LoadBalls` / `ResetDiagnostics`）。
        ///
        /// 期望值来源：**乙**（新增诊断量的**变化/不变**）＋ 契约（开关语义）。**盲带**：不覆盖真实 UI 点击。
        /// **注入负对照**：开关只用于"不绘制"而仍调 `Predict` ⇒ **(a) FAIL**；开关写反 ⇒ **(c) FAIL**。
        /// </summary>
        [Test]
        public void T82_PredictionSwitch_OffMeansNoCompute()
        {
            var failures = new List<string>();

            var driveGo = new GameObject("T82Drive");
            var assistGo = new GameObject("T82Assist");
            try
            {
                var drive = driveGo.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
                Assert.IsNotNull(drive.tableConfig, "T82：需要 TableConfig.asset。");
                drive.loadRackForBallBallDemo = true;
                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T82：Initialize 后应有内核。");

                for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.IsTrue(drive.Simulator.TableAtRest, "T82：前提 —— 推进后应静止。");

                var assist = assistGo.AddComponent<AimAssistController>();
                assist.drive = drive;

                // ── 开启：应重算，且结果非空转（真的发生首接触）──
                drive.enableShotPrediction = true;
                bool recomputed = assist.RefreshPrediction();
                if (!recomputed)
                {
                    failures.Add("前提：开启后第一次 `RefreshPrediction()` 应重算（返回 true）。");
                }

                int n1 = assist.PredictionComputeCount;
                if (n1 != 1)
                {
                    failures.Add($"前提：开启后计数应为 1，实测 {n1}。");
                }

                ShotPrediction before = assist.Prediction;
                if (before.StepsSimulated <= 0 || !before.HasFirstContact)
                {
                    failures.Add($"前提：预测应真的跑过并发生首接触（steps={before.StepsSimulated}, has={before.HasFirstContact}）。");
                }

                // 脏标记：同状态重复调用不应再重算
                assist.RefreshPrediction();
                if (assist.PredictionComputeCount != n1)
                {
                    failures.Add($"脏标记失效：同状态重复调用竟又重算（{n1} → {assist.PredictionComputeCount}）。");
                }

                // ── 关闭：多次调用，计数与 Prediction 逐位不变 ──
                drive.enableShotPrediction = false;
                int n2Before = assist.PredictionComputeCount;
                string predictionBefore = Fingerprint(assist.Prediction);
                for (int i = 0; i < 5; i++)
                {
                    assist.RefreshPrediction();
                }

                int n2 = assist.PredictionComputeCount;
                if (n2 != n2Before)
                {
                    failures.Add($"(a) 关闭期间 `PredictionComputeCount` 变了：{n2Before} → {n2}（关闭仍在算）。");
                }

                if (Fingerprint(assist.Prediction) != predictionBefore)
                {
                    failures.Add("(b) 关闭期间 `Prediction` 被改写（应逐位不变）。");
                }

                // ── (c) 可观测：唯一读点真实反映字段 ──
                if (drive.PredictionEnabled != drive.enableShotPrediction)
                {
                    failures.Add($"(c) `drive.PredictionEnabled` {drive.PredictionEnabled} ≠ `enableShotPrediction` {drive.enableShotPrediction}"
                                 + "（开关读点写反 / 另存副本）。");
                }

                if (drive.PredictionEnabled)
                {
                    failures.Add("(c) 关闭后 `PredictionEnabled` 竟仍为 true。");
                }

                // ── ④ 重新开启：计数增长 ──
                drive.enableShotPrediction = true;
                assist.RefreshPrediction();
                int n3 = assist.PredictionComputeCount;
                if (!(n3 > n2))
                {
                    failures.Add($"(d) 重新开启后计数未增长（{n2} → {n3}）⇒ 恢复计算失败（空转）。");
                }

                if (drive.PredictionEnabled != true)
                {
                    failures.Add("(c) 重新开启后 `PredictionEnabled` 竟不为 true。");
                }

                // ── 源码守卫：控制器不得调内核写入口 ──
                string code = CodeWithoutCommentLines(ControllerSourcePath);
                foreach (string forbidden in new[] { ".Step(", "PlaceBall(", "LoadBalls(", "ResetDiagnostics(" })
                {
                    if (code.Contains(forbidden))
                    {
                        failures.Add($"源码守卫：`AimAssistController.cs` 出现内核写入口 `{forbidden}`"
                                     + "（预测必须零扰动，`T72` 的同族）。");
                    }
                }

                Assert.IsEmpty(failures, "T82 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T82: 开启 n1={n1}（steps={before.StepsSimulated}，has={before.HasFirstContact}）；"
                                      + $"关闭 5 次仍 n2={n2} 且 Prediction 逐位不变；重开 n3={n3} ✓");
            }
            finally
            {
                Object.DestroyImmediate(assistGo);
                Object.DestroyImmediate(driveGo);
            }
        }

        private static string Fingerprint(in ShotPrediction p)
        {
            return p.HasFirstContact + "|"
                 + p.GhostBall.x.ToString("R", CultureInfo.InvariantCulture) + ","
                 + p.GhostBall.y.ToString("R", CultureInfo.InvariantCulture) + "|"
                 + p.TargetOutgoingDirection.x.ToString("R", CultureInfo.InvariantCulture) + ","
                 + p.TargetOutgoingDirection.y.ToString("R", CultureInfo.InvariantCulture) + "|"
                 + p.StepsSimulated.ToString(CultureInfo.InvariantCulture) + "|"
                 + p.BallBallContactCount.ToString(CultureInfo.InvariantCulture) + "|"
                 + p.TotalContactCount.ToString(CultureInfo.InvariantCulture);
        }

        // ════════════ T85 Game 视图标记线顶点过显示层（X/Z 双取反锁死）════════════

        /// <summary>
        /// **Game 视图标记线顶点必须过 `PoolGeometry` 的 X/Z 双取反**：`MarkerWorldPoint(root, p)` 应等于
        /// `root.TransformPoint((−px, h, −py))`（**手写双取反 + 引擎根变换**做独立期望，不复用被测链的任何函数）；
        /// 并与"直写 `(px, h, +py)`"的镜像版**必须不同**（判别力锚）。
        /// **历史缺陷**：Game 视图虚线曾直写 `(px, h, +py)`（漏双取反）⇒ 线画在真实路径绕桌面中心
        /// 旋转 180° 的镜像位置（用户实测截图定位）。本判据的独立期望让"退回直写"必然红。
        /// 注：`PhysicsToUnity` 经四元数合成，与手写式的浮点噪声 ~1e-7 ⇒ 比对用 1e-4 容差
        /// （缺陷偏差是镜像级 ≈ 2×|坐标|，判别裕度 4 个数量级）。
        /// </summary>
        [Test]
        public void T85_GameViewMarkerPoints_FollowDisplayPipeline()
        {
            var rootGo = new GameObject("T85_PlayfieldRoot");
            rootGo.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

            try
            {
                Transform root = rootGo.transform;
                var p = new Vector2(0.4f, 0.3f);   // 非对称点：x、y 皆非零 ⇒ 直写版与正确版必然不同

                Vector3 actual = AimAssistView.MarkerWorldPoint(root, p);

                // 独立期望（手写双取反 + 球心高度 + 引擎根变换；不复用被测链的任何函数）
                Vector3 expected = root.TransformPoint(new Vector3(-p.x, Billiards.Core.PoolConstants.BallRadius, -p.y));

                // 直写镜像版（历史缺陷形态）
                Vector3 mirrored = root.TransformPoint(new Vector3(p.x, Billiards.Core.PoolConstants.BallRadius, p.y));

                var failures = new List<string>();
                if (!(Mathf.Abs(actual.x - expected.x) <= 1e-4f
                      && Mathf.Abs(actual.y - expected.y) <= 1e-4f
                      && Mathf.Abs(actual.z - expected.z) <= 1e-4f))
                {
                    failures.Add($"(a) MarkerWorldPoint({p.x:R},{p.y:R}) = {V(actual)} 不等于双取反口径 {V(expected)}"
                                 + " ⇒ Game 视图顶点未过 `PoolGeometry`（X/Z 双取反）。");
                }

                if (V(actual) == V(mirrored))
                {
                    failures.Add($"(b) 判别力锚失效：actual == 直写镜像版 {V(mirrored)} ⇒ 本判据打不死\"漏双取反\"。");
                }

                Assert.IsEmpty(failures, "T85 失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T85: MarkerWorldPoint = {V(actual)}；与手写双取反+根变换一致（≤1e-4）；与直写镜像版不同 ✓");
            }
            finally
            {
                Object.DestroyImmediate(rootGo);
            }
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("R", CultureInfo.InvariantCulture) + ", "
                 + v.y.ToString("R", CultureInfo.InvariantCulture) + ", "
                 + v.z.ToString("R", CultureInfo.InvariantCulture) + ")";
        }
        /// <summary>
        /// **`T139`：黄线（目标球出射）/ 蓝线（白球偏转）已按用户反馈缩短，且语义关系未变。**
        ///
        /// 用户实机反馈："黄色和蓝色线也稍微缩短一点" ⇒ 两条**纯方向指示线**的**长度常量**各缩短约 26–27%。
        /// ⚠ 缩短**不改变语义**：黄 = 目标球去向、蓝 = 白球偏转去向，长度只是「画多长」，与预测计算无关
        /// （两处绘制 —— Gizmos 与运行时 `LineRenderer` —— 读**同一对常量** ⇒ 只改常量即可）。
        ///
        /// 断言：① 就是批准的新值（`0.40` / `0.26`）；② **黄 > 蓝**（保留「主次分明」的原有关系）；
        /// ③ 两者都 ∈ (0, 1) m（规范米量级合理）。
        ///
        /// **怎么打死它**：把两者写成相等（黄不再比蓝长）⇒ ② 红；把任一个改回旧值 ⇒ ① 红。
        /// </summary>
        [Test]
        public void T139_TargetAndCueDeflectLines_AreShortened_AndKeepPriority()
        {
            Assert.AreEqual(0.40f, AimAssistView.TargetLineLength, 1e-6f,
                "T139①：黄线（目标球出射）应为主理人批准的新值 0.40（原 0.55）。");
            Assert.AreEqual(0.26f, AimAssistView.CueDeflectLineLength, 1e-6f,
                "T139①：蓝线（白球偏转）应为主理人批准的新值 0.26（原 0.35）。");

            Assert.Greater(AimAssistView.TargetLineLength, AimAssistView.CueDeflectLineLength,
                "T139②：**黄线必须仍比蓝线长**（保序：主次分明）—— 用户要的是「缩短」，不是「改主次」。");

            Assert.Greater(AimAssistView.TargetLineLength, 0f);
            Assert.Greater(AimAssistView.CueDeflectLineLength, 0f);
            Assert.Less(AimAssistView.TargetLineLength, 1f);
            Assert.Less(AimAssistView.CueDeflectLineLength, 1f);

            TestContext.WriteLine($"T139: 黄 {AimAssistView.TargetLineLength:R} > 蓝 {AimAssistView.CueDeflectLineLength:R}"
                                  + "（均已缩短、主次关系保持）✓");
        }


    }
}
