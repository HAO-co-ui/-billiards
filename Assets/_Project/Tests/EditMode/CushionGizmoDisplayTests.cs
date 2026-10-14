using System.Collections.Generic;
using System.IO;
using Billiards.Core;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **`CushionGizmo` 绘制坐标必须过显示层**（`PlayfieldView`）的判据（`T71`）。
    ///
    /// ── 为什么需要（本判据的证据动机）────────────────────────────────────────
    /// Day 8 切片 A 把球可视物挂到 `PlayfieldRoot`（`localScale = PoolConstants.PlayfieldVisualScale`）下，
    /// 但 `CushionGizmo` 原本用 <see cref="PoolGeometry"/> 的**规范世界**坐标直接 `Gizmos.DrawLine`——
    /// 结果 Scene 视图里同一颗球**既有 1.2× 的网格球、又有 1.0× 的线框球且错开 20%**，库边线框也停在 1.0×。
    /// 根因：显示层只放大了一半。修法：绘制坐标一律经 <see cref="PlayfieldView.DisplayFromCanonical"/>。
    ///
    /// ── 编号避让（必须保留此理由）──────────────────────────────────────────────
    /// 已占用：`T41`–`T44`（`GreyboxTuningWiringTests`）、`T45`–`T49`（`PointerAimTests`）、
    /// `T50`–`T53`（Day 8 **切片 B**：拖拽瞄准 / 微调粒度 / 视口适配，**下一单**）、
    /// `T54`–`T70`（台呢滚阻两段模型 / 出杆族）。故取其后**首个空位** **`T71`**
    ///（全仓 grep `T71` 无命中，实测）。
    ///
    /// ── 期望值来源（一律在被测对象之外）────────────────────────────────────────
    /// ① 强制口径：<see cref="PlayfieldView.DisplayFromCanonical"/>（唯一显示映射点）；
    /// ② **引擎原语**：`Transform.TransformPoint` / `Transform.lossyScale`（不读本类任何实现）；
    /// ③ **独立闭式**：场景根的 `pos=0 / rot=id` ⇒ 逐分量 ×`PoolConstants.PlayfieldVisualScale`。
    /// 三路独立 ⇒ 同源恒真不成立。另加**合成根**（1.7× + 平移 + 绕 Y 旋转）杀"硬编码 1.2"。
    ///
    /// **盲带（如实声明）**：本判据断言的是**绘制用纯函数** `DisplayPoint` / `DisplayScale` 的取值；
    /// 无法直接观测 `Gizmos.DrawLine`（引擎内部、不可捕获）⇒ "`OnDrawGizmos` 是否确实调用这两个函数"
    /// 只能由**注入负对照**（把绘制点改回不过显示层 ⇒ 本判据 FAIL）与源码复核共同覆盖。
    ///
    /// **注入负对照**：`DisplayPoint` 改为 `return canonicalWorldPoint;`（即不经 `PlayfieldView`）⇒ 本判据 FAIL。
    /// </summary>
    public class CushionGizmoDisplayTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        private const string PlayfieldRootName = "PlayfieldRoot";
        private const string GizmoSourcePath = "Assets/_Project/Scripts/Runtime/Physics/CushionGizmo.cs";

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

        private static CushionGizmo FindGizmo(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                CushionGizmo gizmo = go.GetComponentInChildren<CushionGizmo>(true);
                if (gizmo != null)
                {
                    return gizmo;
                }
            }

            return null;
        }

        [Test]
        public void T71_CushionGizmoDrawPoints_FollowDisplayLayer()
        {
            var failures = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform root = FindByName(scene, PlayfieldRootName);
                CushionGizmo gizmo = FindGizmo(scene);
                Assert.IsNotNull(root, "T71：场景内找不到 PlayfieldRoot（需由 GreyboxSceneSetup 生成）。");
                Assert.IsNotNull(gizmo, "T71：场景内找不到 CushionGizmo。");

                // (0) 场景接线：CushionGizmo 必须真的拿到 PlayfieldRoot（否则绘制静默退化为 1.0×）
                Assert.IsNotNull(gizmo.playfieldRoot,
                    "T71：CushionGizmo.playfieldRoot 未接线 ⇒ 绘制退化为 scale = 1、与球可视物错位。");
                Assert.AreSame(root, gizmo.playfieldRoot,
                    "T71：CushionGizmo.playfieldRoot 未指向场景里的 PlayfieldRoot。");

                float scale = PoolConstants.PlayfieldVisualScale;

                Vector3[] canonicalPoints =
                {
                    new Vector3(0f, 0f, 0f),
                    PoolGeometry.PhysicsToUnity(Vector2.zero),
                    PoolGeometry.PhysicsToUnity(new Vector2(0.4f, 0.25f)),
                    PoolGeometry.PhysicsToUnity(new Vector2(-PoolConstants.ClothHalfLength, PoolConstants.ClothHalfWidth)),
                    PoolGeometry.PhysicsBallToUnity(new Vector2(1.0f, -0.5f)),
                };

                float maxViaPlayfieldView = 0f;
                float maxViaTransformPoint = 0f;
                float maxViaNumeric = 0f;

                foreach (Vector3 canonical in canonicalPoints)
                {
                    Vector3 drawPoint = CushionGizmo.DisplayPoint(root, canonical);

                    Vector3 expectedPlv = PlayfieldView.DisplayFromCanonical(root, canonical);   // ① 强制口径
                    Vector3 expectedEng = root.TransformPoint(canonical);                        // ② 引擎原语
                    Vector3 expectedNum = new Vector3(canonical.x * scale, canonical.y * scale, canonical.z * scale); // ③ 独立闭式

                    maxViaPlayfieldView = Mathf.Max(maxViaPlayfieldView, (drawPoint - expectedPlv).magnitude);
                    maxViaTransformPoint = Mathf.Max(maxViaTransformPoint, (drawPoint - expectedEng).magnitude);
                    maxViaNumeric = Mathf.Max(maxViaNumeric, (drawPoint - expectedNum).magnitude);
                }

                if (!(maxViaPlayfieldView <= 1e-6f))
                {
                    failures.Add($"绘制点 ≠ PlayfieldView 显示映射点（最大差 {maxViaPlayfieldView:E3} m）⇒ 绘制未过显示层。");
                }

                if (!(maxViaTransformPoint <= 1e-6f))
                {
                    failures.Add($"绘制点 ≠ 引擎 TransformPoint（最大差 {maxViaTransformPoint:E3} m）。");
                }

                if (!(maxViaNumeric <= 1e-6f))
                {
                    failures.Add($"绘制点 ≠ 规范点逐分量 ×{scale}（最大差 {maxViaNumeric:E3} m）⇒ 显示尺度不对。");
                }

                // (2) 球线框的**显示半径** == BallRadius × 显示尺度
                float drawRadius = CushionGizmo.DisplayScale(root) * PoolConstants.BallRadius;
                float expectedRadiusEng = root.lossyScale.y * PoolConstants.BallRadius;
                float expectedRadiusNum = scale * PoolConstants.BallRadius;

                if (!(Mathf.Abs(drawRadius - expectedRadiusEng) <= 1e-6f))
                {
                    failures.Add($"球线框显示半径 {drawRadius:F6} m ≠ 引擎 lossyScale×BallRadius {expectedRadiusEng:F6} m。");
                }

                if (!(Mathf.Abs(drawRadius - expectedRadiusNum) <= 1e-6f))
                {
                    failures.Add($"球线框显示半径 {drawRadius:F6} m ≠ {scale}×BallRadius {expectedRadiusNum:F6} m。");
                }

                // (3) 一般性：非 1.2 / 带平移 + 绕 Y 旋转的**合成根** ⇒ 杀"硬编码 1.2"
                var synth = new GameObject("T71SynthRoot");
                try
                {
                    synth.transform.position = new Vector3(0.3f, -0.15f, 0.22f);
                    synth.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                    synth.transform.localScale = Vector3.one * 1.7f;

                    float synthScale = CushionGizmo.DisplayScale(synth.transform);
                    if (!(Mathf.Abs(synthScale - 1.7f) <= 1e-5f))
                    {
                        failures.Add($"合成根显示尺度 {synthScale:F6} ≠ 1.7（DisplayScale 未跟随根缩放 ⇒ 疑似硬编码）。");
                    }

                    Vector3 synthCanonical = new Vector3(0.2f, 0.05f, -0.3f);
                    Vector3 synthGot = CushionGizmo.DisplayPoint(synth.transform, synthCanonical);
                    Vector3 synthExpected = synth.transform.TransformPoint(synthCanonical);
                    if (!((synthGot - synthExpected).magnitude <= 1e-6f))
                    {
                        failures.Add($"合成根绘制点 {synthGot} ≠ TransformPoint {synthExpected}（平移/旋转未被正确纳入）。");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(synth);
                }

                // (4) 退化：playfieldRoot == null ⇒ scale = 1、坐标原样返回（不得抛异常）
                Vector3 probe = new Vector3(1.1f, 0.2f, -0.4f);
                if (!(CushionGizmo.DisplayPoint(null, probe) == probe))
                {
                    failures.Add("DisplayPoint(null, p) 应原样返回 p（scale = 1 退化）。");
                }

                if (!(Mathf.Abs(CushionGizmo.DisplayScale(null) - 1f) <= 1e-9f))
                {
                    failures.Add("DisplayScale(null) 应为 1（scale = 1 退化）。");
                }

                // (5) 源码守卫：本类**不得手写固定倍数**（跳过注释行，避免文档里的说明文字误伤）
                foreach (string line in File.ReadAllLines(GizmoSourcePath))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("///") || trimmed.StartsWith("//"))
                    {
                        continue;
                    }

                    if (trimmed.Contains("1.2"))
                    {
                        failures.Add($"CushionGizmo.cs 代码行出现字面量 1.2：`{trimmed.Trim()}` ⇒ 违反'不得手写固定倍数'。");
                        break;
                    }
                }

                Assert.IsEmpty(failures, "T71 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine(
                    $"T71: 绘制点 == 显示映射点（max {maxViaPlayfieldView:E2} m）/ == TransformPoint（max {maxViaTransformPoint:E2} m）"
                    + $" / == ×{scale}（max {maxViaNumeric:E2} m）；球线框显示半径 {drawRadius:F6} m ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
