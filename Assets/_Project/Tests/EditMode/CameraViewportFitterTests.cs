using System.Collections.Generic;
using System.Globalization;
using Billiards.Core;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **视口适配**：`T52`（窄窗不裁切球桌）+ `T53`（16:9 基准是不动点）。
    ///
    /// ── 为什么不放进 `PointerAimTests.cs` ─────────────────────────────────────
    /// 同 `PointerAimControllerTests`：`PointerAimTests.cs` 的 `T45`–`T49` 是**切片一已入库证据**，不得改动。
    ///
    /// ── ⚠ 本单最重要的实测发现（详见 `Docs/35-A_Day8_相机与指针瞄准_执行记录.md`）──────
    /// spec §4.1 假定 `baseVerticalFovDeg = 40f`（= 场景现值）、`maxVerticalFovDeg = 60f`。**实测二者都不成立**：
    ///  · **40° 装不下**：内库四角（物理 `(±1.27, ±0.635)` m）经 `×1.2` 显示缩放 ⇒ `viewport.x` 达 `1.103`、
    ///    `viewport.y_min = −0.044`（要 `[0.02, 0.98]`）。根因 = **切片 A 的 1.2 显示缩放**把桌子推出 Day 3 相机的水平视锥。
    ///  · **60° 罩不住最窄档**：`3/4` 档在"保持水平 FOV"规则下**几何上需要 ≈ 98° 垂直 FOV**。
    ///  ⇒ 按实测把 `baseVerticalFovDeg` 定为 **52**、`maxVerticalFovDeg` 定为 **100**、`minSupportedAspect = 3/4`。
    /// **本判据的门槛、边距、比例集合一律未改**（不是"放宽"）。
    ///
    /// ── 期望值来源 ───────────────────────────────────────────────────────────
    /// `PoolConstants`（内库半长/半宽，契约常量）+ **引擎原语** `Transform.TransformPoint` / `Camera.WorldToViewportPoint`
    /// + 场景**序列化**值（相机 / 适配器）。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · 只覆盖 **≥ `minSupportedAspect`** 的比例；更窄的超域降级（letterbox）**本单不做**。
    /// · **不判**"桌子是否太小看不清"（排期只要求"不裁切"）。
    /// · 不覆盖"相机位姿被改动"后的回归（每次从**场景序列化值**读相机与适配器参数）。
    /// </summary>
    public class CameraViewportFitterTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        /// <summary>边距（spec §5 `T52`：`m = 0.02`）。</summary>
        private const float Margin = 0.02f;

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

        private static Camera FindCamera(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Camera cam = go.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    return cam;
                }
            }

            return null;
        }

        private static CameraViewportFitter FindFitter(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                CameraViewportFitter fitter = go.GetComponentInChildren<CameraViewportFitter>(true);
                if (fitter != null)
                {
                    return fitter;
                }
            }

            return null;
        }

        /// <summary>内库四角（世界坐标）。用**引擎原语** `TransformPoint`（不经 `PlayfieldView`）⇒ 与被测适配器不同源。</summary>
        private static Vector3[] InnerCushionCorners(Transform playfieldRoot)
        {
            float hx = PoolConstants.ClothHalfLength;
            float hy = PoolConstants.ClothHalfWidth;
            return new[]
            {
                playfieldRoot.TransformPoint(PoolGeometry.PhysicsToUnity(new Vector2(hx, hy))),
                playfieldRoot.TransformPoint(PoolGeometry.PhysicsToUnity(new Vector2(-hx, hy))),
                playfieldRoot.TransformPoint(PoolGeometry.PhysicsToUnity(new Vector2(hx, -hy))),
                playfieldRoot.TransformPoint(PoolGeometry.PhysicsToUnity(new Vector2(-hx, -hy))),
            };
        }

        // ══════════════════ T52 窄窗不裁切球桌（验收判据之二）══════════════════════

        /// <summary>
        /// 对宽高比集合 `{16/9, 16/10, 3/2, 4/3, 1/1, 3/4}`，按适配器规则设定 `cam.aspect` / `cam.fieldOfView` 后，
        /// **内库四角**必须全部落在 `viewport ∈ [0.02, 0.98]`（且 `viewport.z > 0`，在相机前方）。
        ///
        /// **期望值来源**：`PoolConstants`（内库）+ 引擎投影（相机来自场景**序列化**值）。
        /// **判别力来源**：直接投影**球桌在物理上可玩的边界**（内库四角）—— 裁切即 FAIL。
        /// **注入负对照**：① 规则退回"固定垂直 FOV" ⇒ `4/3` / `1/1` / `3/4` 档四角 `viewport.x` 越界 ⇒ FAIL；
        /// ② `maxVerticalFovDeg` 收到 `60` ⇒ `3/4` 档越界 ⇒ FAIL。
        /// </summary>
        [Test]
        public void T52_NarrowWindow_DoesNotClipTable()
        {
            var failures = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Camera cam = FindCamera(scene);
                Transform root = FindByName(scene, "PlayfieldRoot");
                CameraViewportFitter fitter = FindFitter(scene);

                Assert.IsNotNull(cam, "T52：场景内找不到相机。");
                Assert.IsNotNull(root, "T52：场景内找不到 PlayfieldRoot。");
                Assert.IsNotNull(fitter, "T52：场景内找不到 CameraViewportFitter（视口适配未接线 ⇒ 窄窗必裁切）。");
                Assert.IsNotNull(fitter.targetCamera, "T52：CameraViewportFitter.targetCamera 未接线。");
                Assert.AreSame(cam, fitter.targetCamera, "T52：适配器未指向场景相机。");

                float baseAspect = fitter.baseAspect;
                float baseFov = fitter.baseVerticalFovDeg;

                Vector3[] corners = InnerCushionCorners(root);

                float[] aspects = { 16f / 9f, 16f / 10f, 3f / 2f, 4f / 3f, 1f, 3f / 4f };

                float savedAspect = cam.aspect;
                float savedFov = cam.fieldOfView;
                int covered = 0;
                var detail = new List<string>();
                try
                {
                    foreach (float aspect in aspects)
                    {
                        if (aspect < fitter.minSupportedAspect - 1e-6f)
                        {
                            failures.Add($"比例 {aspect:F4} < minSupportedAspect {fitter.minSupportedAspect:F4} ⇒ 超出支持域"
                                         + "（本单声明降级；若该比例应在域内，须上调 minSupportedAspect 或调整取景）。");
                            continue;
                        }

                        cam.aspect = aspect;
                        float fov = CameraViewportFitter.ResolveVerticalFovDeg(
                            aspect, baseAspect, baseFov, fitter.maxVerticalFovDeg);
                        cam.fieldOfView = fov;
                        covered++;

                        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                        foreach (Vector3 corner in corners)
                        {
                            Vector3 v = cam.WorldToViewportPoint(corner);
                            if (!(v.z > 0f))
                            {
                                failures.Add($"比例 {aspect:F4}：内库角不在相机前方（viewport.z = {v.z:F3}）。");
                                continue;
                            }

                            if (!(v.x >= Margin && v.x <= 1f - Margin))
                            {
                                failures.Add($"比例 {aspect:F4}：内库角 viewport.x = {v.x:F4} 越界 [ {Margin}, {1f - Margin} ]"
                                             + $"（垂直 FOV 用了 {fov:F2}°）⇒ 横向裁切。");
                            }

                            if (!(v.y >= Margin && v.y <= 1f - Margin))
                            {
                                failures.Add($"比例 {aspect:F4}：内库角 viewport.y = {v.y:F4} 越界 [ {Margin}, {1f - Margin} ]"
                                             + $"（垂直 FOV 用了 {fov:F2}°）⇒ 纵向裁切。");
                            }

                            minX = Mathf.Min(minX, v.x);
                            maxX = Mathf.Max(maxX, v.x);
                            minY = Mathf.Min(minY, v.y);
                            maxY = Mathf.Max(maxY, v.y);
                        }

                        detail.Add($"aspect={aspect.ToString("F4", CultureInfo.InvariantCulture)}"
                                   + $" fov={fov.ToString("F2", CultureInfo.InvariantCulture)}"
                                   + $" x=[{minX:F4},{maxX:F4}] y=[{minY:F4},{maxY:F4}]");
                    }
                }
                finally
                {
                    cam.aspect = savedAspect;
                    cam.fieldOfView = savedFov;
                }

                if (covered != aspects.Length)
                {
                    failures.Add($"前提：只有 {covered}/{aspects.Length} 档进入支持域（其余被 minSupportedAspect 判为超域）。");
                }

                Assert.IsEmpty(failures, "T52 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T52: {covered} 档全部四角落进 [ {Margin}, {1f - Margin} ] ✓\n  "
                                      + string.Join("\n  ", detail));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T53 「16:9 基准」是不动点 ═════════════════════════════

        /// <summary>
        /// `cam.aspect == 16/9` 时，适配器产出的 `fieldOfView` **必须 == 基准垂直 FOV == 场景序列化 FOV**（容差 1e-4）；
        /// 且**更宽**的比例下也**不改** FOV（显示更多，不裁切）。
        ///
        /// **期望值来源**：场景序列化 FOV + 契约（`baseAspect = 16/9`）。
        /// **判别力来源**：防"适配器在宽窗也开始乱改 FOV"（会让"基准"名不副实）。
        /// **注入负对照**：规则写成"宽窄都改 FOV"（恒走窄窗公式）⇒ 16:9 时 FOV ≠ 基准 ⇒ FAIL。
        /// </summary>
        [Test]
        public void T53_SixteenNineBaseline_IsNoOp()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Camera cam = FindCamera(scene);
                CameraViewportFitter fitter = FindFitter(scene);

                Assert.IsNotNull(cam, "T53：场景内找不到相机。");
                Assert.IsNotNull(fitter, "T53：场景内找不到 CameraViewportFitter。");
                Assert.IsNotNull(fitter.targetCamera, "T53：CameraViewportFitter.targetCamera 未接线。");

                Assert.AreEqual(16f / 9f, fitter.baseAspect, 1e-6f,
                    "T53：基准比例应为 16:9。");

                // 场景序列化 FOV == 适配器基准（防"相机 40 / 适配器 52"这类漂移）
                Assert.AreEqual(cam.fieldOfView, fitter.baseVerticalFovDeg, 1e-4f,
                    $"T53：场景序列化 FOV {cam.fieldOfView:F4} ≠ 适配器基准 {fitter.baseVerticalFovDeg:F4}（两者必须一致）。");

                // 纯函数：基准比例（及更宽）下产出 == 基准 FOV
                float atBaseline = CameraViewportFitter.ResolveVerticalFovDeg(
                    fitter.baseAspect, fitter.baseAspect, fitter.baseVerticalFovDeg, fitter.maxVerticalFovDeg);
                Assert.AreEqual(fitter.baseVerticalFovDeg, atBaseline, 1e-4f,
                    "T53：16:9 基准下纯函数产出 ≠ 基准 FOV（基准不是不动点）。");

                float wider = CameraViewportFitter.ResolveVerticalFovDeg(
                    21f / 9f, fitter.baseAspect, fitter.baseVerticalFovDeg, fitter.maxVerticalFovDeg);
                Assert.AreEqual(fitter.baseVerticalFovDeg, wider, 1e-4f,
                    "T53：更宽的比例下不应改 FOV（应显示更多而非裁切）。");

                // 行为：16:9 下 Apply() 落在基准上（先故意扰动 FOV）
                float savedAspect = cam.aspect;
                float savedFov = cam.fieldOfView;
                try
                {
                    cam.aspect = fitter.baseAspect;
                    cam.fieldOfView = 33f;   // 扰动：no-op 的适配器应把它拉回基准
                    fitter.Apply();
                    Assert.AreEqual(fitter.baseVerticalFovDeg, cam.fieldOfView, 1e-4f,
                        "T53：16:9 下 Apply() 未产出基准 FOV（基准档不是 no-op）。");
                }
                finally
                {
                    cam.aspect = savedAspect;
                    cam.fieldOfView = savedFov;
                }

                TestContext.WriteLine($"T53: baseAspect={fitter.baseAspect:R} 基准 FOV={fitter.baseVerticalFovDeg:R}"
                                      + $"（== 场景序列化 FOV {cam.fieldOfView:R}）；16:9 / 21:9 均 == 基准 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
