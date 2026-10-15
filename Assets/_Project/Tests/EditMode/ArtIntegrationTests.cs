using System.Collections.Generic;
using System.Globalization;
using Billiards.Core;
using Billiards.EditorTools;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **美术接入判据族**（Day 16 后追加，编号续 `T121`：`T122`–`T123`）。
    ///
    /// 起因（用户实机反馈）：**「台球不是 15 个球加 1 个白球吗？你还没给我弄全」+「美术资源你也没给我用上」**。
    /// 实测根因两条：
    /// ① `GreyboxSceneSetup.BuildRackVisuals` 写死 `RackPositions(footSpot, 3)`、且**每一颗都套白球材质** `Ball_00.mat`
    ///    ⇒ 场上只有 **4 颗**且**全白**；
    /// ② 球/杆一律用**基元**（`CreatePrimitive(Sphere)` / 圆柱），美术包的 `Ball_NN.fbx`、`PoolCue_TipOrigin.fbx`
    ///    虽已导入却从未使用。
    ///
    /// 本族把这两条钉死：**球数必须等于"白球 + `RackBallCount`"**、**逐号材质**、**不得残留基元**、
    /// **杆必须是美术模型且尺寸约定（杆尖原点 / +Z / 1.4 m）成立**。
    /// </summary>
    public class ArtIntegrationTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        // ── T122 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T122`：可视球与内核球一一对应、逐号材质、不得残留基元**。
        ///
        /// 断言：
        /// ① 可视球数 **== 白球 + `PhysicsTestDrive.RackBallCount`(= 15) == 16**（这一格正是"没弄全"的守卫）；
        /// ② `drive.Simulator.Balls.Count` 与可视球数**相等**（可视与内核不许各说各话）；
        /// ③ 第 `i` 颗球的可视物材质名为 **`Ball_{i:00}`**（0 = 白球；旧实现对每颗都用 `Ball_00` ⇒ 全白）；
        /// ④ **不是基元球**（网格名不得含 `Sphere`）；
        /// ⑤ 球及其子物体上**没有碰撞体**（自研物理铁律）。
        ///
        /// **注入负对照**：把 `RackBallCount` 改回 3 ⇒ ① 红；把逐号取材质改回 `CueBallMaterialPath` ⇒ ③ 红。
        /// **盲带**：只锁"数量/材质/网格来源"，**不锁观感**（号数是否看得清、花色是否好看属目视）。
        /// </summary>
        [Test]
        public void T122_BallVisuals_MatchKernelCount_AndUsePerNumberMaterial()
        {
            var failures = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = FindOne<PhysicsTestDrive>(scene);
                Assert.IsNotNull(drive, "T122：场景里找不到 PhysicsTestDrive。");
                Transform[] visuals = drive.ballVisuals;
                Assert.IsNotNull(visuals, "T122：drive.ballVisuals 未接线。");

                int expected = 1 + PhysicsTestDrive.RackBallCount;
                if (visuals.Length != expected)
                {
                    failures.Add($"① 可视球数 {visuals.Length} ≠ 白球 + {PhysicsTestDrive.RackBallCount} = {expected}"
                                 + "（旧实现写死 3 ⇒ 场上只有 4 颗）。");
                }

                drive.Initialize();   // EditMode 下场景对象不跑 Awake ⇒ 显式建内核以比对球数
                Assert.IsNotNull(drive.Simulator, "T122：Initialize 后应有内核。");
                if (drive.Simulator.Balls.Count != visuals.Length)
                {
                    failures.Add($"② 内核球数 {drive.Simulator.Balls.Count} ≠ 可视球数 {visuals.Length}"
                                 + "（会出现「看不见的球」或「没有内核的球」）。");
                }

                for (int i = 0; i < visuals.Length; i++)
                {
                    if (visuals[i] == null)
                    {
                        failures.Add($"③ 第 {i} 颗球可视物为空。");
                        continue;
                    }

                    var renderer = visuals[i].GetComponentInChildren<MeshRenderer>(true);
                    string expectedMaterial = string.Format(CultureInfo.InvariantCulture, "Ball_{0:00}", i);
                    if (renderer == null || renderer.sharedMaterial == null)
                    {
                        failures.Add($"③ 第 {i} 颗球没有渲染器/材质。");
                    }
                    else if (renderer.sharedMaterial.name != expectedMaterial)
                    {
                        failures.Add($"③ 第 {i} 颗球材质为 {renderer.sharedMaterial.name}，应为 {expectedMaterial}"
                                     + "（旧实现对每颗都套 Ball_00 ⇒ 全白）。");
                    }

                    MeshFilter meshFilter = visuals[i].GetComponentInChildren<MeshFilter>(true);
                    if (meshFilter != null && meshFilter.sharedMesh != null
                        && meshFilter.sharedMesh.name.Contains("Sphere"))
                    {
                        failures.Add($"④ 第 {i} 颗球仍是**基元球**（网格 {meshFilter.sharedMesh.name}）—— 美术模型未接入。");
                    }

                    Collider collider = visuals[i].GetComponentInChildren<Collider>(true);
                    if (collider != null)
                    {
                        failures.Add($"⑤ 第 {i} 颗球带碰撞体 {collider.GetType().Name}（自研物理铁律：可视物不得带物理组件）。");
                    }
                }

                Assert.IsEmpty(failures, "T122 失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T122: 球 {visuals.Length} 颗（= 白球 + {PhysicsTestDrive.RackBallCount}）；"
                                      + $"逐号材质 Ball_00..Ball_{visuals.Length - 1:00}；无基元球、无碰撞体 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ── T123 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T123`：球杆必须是美术模型，且"杆尖 = 杆根原点、杆身 +Z、长 1.4 m"在**装配后**成立**。
        ///
        /// 为什么这三条值钱：`CueStickController` 的整个坐标系（`_stick.position = 杆尖`、
        /// `_stick.rotation = LookRotation(StickDirection)`、命中帧贴球）**都建立在它们之上**；
        /// 若模型的原点/朝向不同（例如杆心为原点、或杆身沿 −Z），杆会在**球心、库边、袋口**处全部错位。
        ///
        /// ── 首版为什么红（这条判据抓到的东西，写下来免得后人重复踩）─────────────────
        /// 首版按美术包 README 的"杆身 +Z"直接断言 `renderer.localBounds` 的 Z 向 —— 实测报
        /// 「杆长 **0.0319 m** ≠ 1.4 m、横向 **1.4 m**」⇒ 长轴根本不在 Z 上。
        /// 用离线探针 `Docs/Tools/fbx_axis_probe.py` 直解 FBX 得到：
        /// 局部 bbox `min=(-0.016148, **-1.400000**, -0.015760)`、`max=(0.015415, **-0.000000**, 0.016188)`
        /// ⇒ **长轴 = Y、杆身朝 −Y、杆尖在原点**（README 的 +Z 是 Blender 源工程口径，FBX 已做 Z-up→Y-up 转换）。
        ///
        /// ── 因此本判据现在的形态（实现驱动，不是重述设计公式）────────────────────
        /// ① `stickInstance` 是**杆根**（名字 = `CueStickRoot`）、其下有一个 `CueModel` 子物体；
        /// ② 子模型带**修正旋转** `CueModelLocalRotation`（把 −Y 映到 +Z）；
        /// ③ **在杆根局部空间里**量装配后的实际包围盒（把网格 `localBounds` 的 8 个角经
        ///    `杆根.worldToLocalMatrix × 渲染器.localToWorldMatrix` 变换后再取 AABB）：
        ///    杆尖 `min.z ≈ 0`、Z 向长 `≈ 1.4`、横向 `max(x,y) ≤ 0.04`；
        /// ④ 无碰撞体。
        ///
        /// ③ 刻意**不**用 `renderer.localBounds` 本身 —— 那是**网格空间**的包围盒，不含模型节点的旋转，
        /// 正是首版误判的成因（局部量对、装配后错）。这也符合本案既有教训：
        /// 几何类判据必须**采样真实对象**，不能只重述设计公式。
        ///
        /// **横向上限 0.04 m 的来处**：实测球杆最大直径 **31.9 mm**（杆尾），取 40 mm 留余量；
        /// 若真拿错模型（如整张桌子），横向会是米级 ⇒ 判据仍有判别力。
        ///
        /// **注入负对照**：把 `CueModelLocalRotation` 改回 `identity` ⇒ ③ 红（长轴变 Y、横向 1.4 m）；
        /// 删掉 `cueStick.stickInstance` 的接线 ⇒ ① 红。
        /// **盲带**：只锁几何约定，**不锁材质/贴图观感**；"模型缺失时的基元兜底"由源码审查覆盖（`EnsureStick` 私有）。
        /// </summary>
        [Test]
        public void T123_CueModel_MatchesTipOriginConvention()
        {
            var failures = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                CueStickController stick = FindOne<CueStickController>(scene);
                Assert.IsNotNull(stick, "T123：场景里找不到 CueStickController。");

                if (stick.stickInstance == null)
                {
                    failures.Add("① `stickInstance` 为空 —— 场景未接入美术包杆模型（会退回基元圆柱）。");
                    Assert.IsEmpty(failures, "T123 失败：\n" + string.Join("\n", failures));
                }

                Transform root = stick.stickInstance;
                if (root.name != GreyboxSceneSetup.CueStickRootObjectName)
                {
                    failures.Add($"① 杆根名为 {root.name}，应为 {GreyboxSceneSetup.CueStickRootObjectName}"
                                 + "（控制器要写绝对位姿的那一层）。");
                }

                Transform modelTransform = root.Find(GreyboxSceneSetup.CueModelObjectName);
                if (modelTransform == null)
                {
                    failures.Add($"① 杆根下找不到子物体 {GreyboxSceneSetup.CueModelObjectName}。");
                    Assert.IsEmpty(failures, "T123 失败：\n" + string.Join("\n", failures));
                }

                // ② 修正旋转（把网格的 −Y 杆身映到杆根局部空间的 +Z）
                float angle = Quaternion.Angle(modelTransform.localRotation, GreyboxSceneSetup.CueModelLocalRotation);
                if (angle > 0.01f)
                {
                    failures.Add($"② 模型局部旋转与修正旋转相差 {angle:R}°"
                                 + "（网格长轴是 Y，不修正则杆装反/横躺）。");
                }

                var renderer = modelTransform.GetComponentInChildren<MeshRenderer>(true);
                Assert.IsNotNull(renderer, "T123：杆模型没有渲染器。");

                // ③ 在**杆根局部空间**量装配后的包围盒
                Bounds local = renderer.localBounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Vector3 min = Vector3.positiveInfinity;
                Vector3 max = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 p = toRoot.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                Vector3 size = max - min;
                float tipGap = Mathf.Abs(min.z);
                float length = size.z;
                float lateral = Mathf.Max(size.x, size.y);

                if (tipGap > 0.02f)
                {
                    failures.Add($"③ 杆尖不在杆根原点：装配后局部包围盒 min.z = {tipGap:R} m（应 ≈ 0，容差 0.02）。");
                }

                if (Mathf.Abs(length - CueStickController.StickLength) > 0.02f)
                {
                    failures.Add($"③ 杆长 {length:R} m ≠ 约定 {CueStickController.StickLength:R} m（容差 0.02）。");
                }

                if (lateral > MaxCueLateralMeters)
                {
                    failures.Add($"③ 杆横向尺寸 {lateral:R} m 过大（> {MaxCueLateralMeters:R}）"
                                 + "—— 长轴没落到 Z 上，或拿错了模型。");
                }

                // ④ 无碰撞体
                Collider collider = modelTransform.GetComponentInChildren<Collider>(true);
                if (collider != null)
                {
                    failures.Add($"④ 杆模型带碰撞体 {collider.GetType().Name}（可视物不得带物理组件）。");
                }

                Assert.IsEmpty(failures, "T123 失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T123: 杆根 {root.name} ⇒ 装配后（杆根局部空间）min.z={min.z:R}、"
                                      + $"长 {length:R} m、横向 {lateral:R} m、修正旋转 {angle:R}°、无碰撞体 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>球杆横向尺寸上限（m）。实测最大直径 31.9 mm（杆尾）⇒ 取 40 mm 留余量。</summary>
        private const float MaxCueLateralMeters = 0.04f;

        private static T FindOne<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
