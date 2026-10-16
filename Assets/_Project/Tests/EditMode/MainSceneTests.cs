using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Billiards.Core;
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
    /// **正式游戏入口 `Main.unity` 的判据族**（编号续 `T126`：`T127`–`T128`）。
    ///
    /// 背景：`Main.unity` 过去是**空壳**（1 个 MeshRenderer + 1 个 Camera + 1 个 Light、**0 个 MonoBehaviour**）
    /// ⇒ 用它出包 = **黑屏死包**，出包入口一度被迫改用灰盒场景（与 `SCN-02` / `D4-BLD-01` 互斥）。
    /// 现在生成器把**玩法层**写进 `Main.unity`（复用既有桌 / 地面 / 相机 / 光），它是**唯一出包场景**。
    ///
    /// · **`T127`**：`Main.unity` **不是空壳** —— 玩法宿主齐备、球可视物 16 颗、**恰 1 台相机 / 恰 1 个
    ///   `AudioListener`**（不得出现两套相机、两个监听器）、`PlayfieldRoot` 缩放仍 = 1（材质视觉验收前提）。
    /// · **`T128`**：生成器的**幂等** —— 玩法层已在场时重复 `ApplyMainScene()` **不改动场景文件**（sha1 不变）。
    ///
    /// **注入负对照（生成物类判据必须"改码 → 重跑生成 → 断言"）**：写进规格 §注入。
    /// </summary>
    public class MainSceneTests
    {
        private const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string GreyboxScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? ".";

        // ── T127 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T127`：`Main.unity` 是**可玩入口**（不是空壳）**。七组断言（任一失败即红）：
        /// ① 玩法层根 <see cref="GreyboxSceneSetup.MainGameplayRootName"/> 在场；
        /// ② 玩法宿主齐备（驱动器 / HUD / 瞄准辅助 / 球杆 / 结算 / **首页** / EventSystem）；
        /// ③ 球可视物 == 16（非空转证据）；
        /// ④ 场景**恰 1 台相机**、**恰 1 个 `AudioListener`**（"两套相机 / 两个监听器"的守卫）；
        /// ⑤ `PlayfieldRoot` 的 `localScale` == 1（材质视觉验收 `SCN-02` 的前提，不得被玩法层改坏）；
        /// ⑥ 正式态 **`showDebugOverlay == false`**（诊断文本默认关闭）；
        /// ⑦ 关键接线非空（`tableConfig` / `tuning` / `titleFont` / 相机引用）。
        /// </summary>
        [Test]
        public void T127_MainScene_IsPlayableEntry_NotShell()
        {
            var failures = new List<string>();

            Assert.IsTrue(File.Exists(Path.Combine(ProjectRoot, MainScenePath)),
                "T127：Main 场景缺失：" + MainScenePath);

            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject playfieldRoot = null;
                GameObject gameplayRoot = null;
                int cameraCount = 0;
                int listenerCount = 0;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == GreyboxSceneSetup.PlayfieldRootObjectName)
                    {
                        playfieldRoot = root;
                    }

                    if (root.name == GreyboxSceneSetup.MainGameplayRootName)
                    {
                        gameplayRoot = root;
                    }

                    cameraCount += root.GetComponentsInChildren<Camera>(true).Length;
                    listenerCount += root.GetComponentsInChildren<AudioListener>(true).Length;
                }

                // ① 玩法层根
                if (gameplayRoot == null)
                {
                    failures.Add($"① 场景里找不到玩法层根 `{GreyboxSceneSetup.MainGameplayRootName}` ⇒ Main 是空壳。");
                }

                // ② 玩法宿主
                if (gameplayRoot != null)
                {
                    foreach (string required in new[]
                             {
                                 GreyboxSceneSetup.DriveObjectName,
                                 GreyboxSceneSetup.PracticeHudObjectName,
                                 GreyboxSceneSetup.AimAssistObjectName,
                                 GreyboxSceneSetup.CueStickControllerObjectName,
                                 GreyboxSceneSetup.PracticeResultObjectName,
                                 GreyboxSceneSetup.HomeObjectName,
                                 GreyboxSceneSetup.EventSystemObjectName,
                                 GreyboxSceneSetup.PointerAimControllerObjectName,
                                 GreyboxSceneSetup.PointerPowerControllerObjectName,
                                 GreyboxSceneSetup.PoolAudioObjectName,
                                 GreyboxSceneSetup.PoolVfxObjectName,
                                 GreyboxSceneSetup.GizmoObjectName,
                             })
                    {
                        bool found = false;
                        foreach (Transform child in gameplayRoot.transform)
                        {
                            if (child.name == required)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            failures.Add($"② 玩法层缺宿主 `{required}`。");
                        }
                    }
                }

                // ③ 球可视物 + ⑦ 关键接线
                PhysicsTestDrive drive = gameplayRoot != null
                    ? gameplayRoot.GetComponentInChildren<PhysicsTestDrive>(true)
                    : null;
                if (drive == null)
                {
                    failures.Add("③ 找不到 PhysicsTestDrive（玩法层缺失）。");
                }
                else
                {
                    if (drive.ballVisuals == null || drive.ballVisuals.Length != PhysicsTestDrive.RackBallCount + 1)
                    {
                        failures.Add($"③ 球可视物应为 {PhysicsTestDrive.RackBallCount + 1} 颗（白球 + 球堆），"
                                     + $"实际 {(drive.ballVisuals == null ? 0 : drive.ballVisuals.Length)} ⇒ 场景空转。");
                    }

                    if (drive.tableConfig == null)
                    {
                        failures.Add("⑦ drive.tableConfig 未接线（运行期内核会停用）。");
                    }

                    if (drive.tuning == null)
                    {
                        failures.Add("⑦ drive.tuning 未接线（'唯一可调入口'名不副实）。");
                    }

                    // ⑥ 正式态：调试叠加层关闭
                    if (drive.showDebugOverlay)
                    {
                        failures.Add("⑥ 正式入口的 showDebugOverlay 应为 false（诊断文本不得出现在正式态）。");
                    }
                }

                PracticeHudView hud = gameplayRoot != null
                    ? gameplayRoot.GetComponentInChildren<PracticeHudView>(true)
                    : null;
                if (hud == null)
                {
                    failures.Add("⑦ 找不到 PracticeHudView（HUD 宿主缺失）。");
                }
                else if (hud.titleFont == null || hud.bodyFont == null)
                {
                    failures.Add("⑦ HUD 字体未接线（titleFont / bodyFont 其一为空）。");
                }

                AimAssistController assist = gameplayRoot != null
                    ? gameplayRoot.GetComponentInChildren<AimAssistController>(true)
                    : null;
                if (assist == null)
                {
                    failures.Add("⑦ 找不到 AimAssistController（瞄准辅助缺失）。");
                }
                else
                {
                    if (assist.drive != drive)
                    {
                        failures.Add("⑦ AimAssistController.drive 未指向本场景的 PhysicsTestDrive。");
                    }

                    if (assist.aimCamera == null)
                    {
                        failures.Add("⑦ AimAssistController.aimCamera 未接线。");
                    }
                }

                // ④ 恰 1 台相机 / 恰 1 个 AudioListener（"两套相机 / 两个监听器"守卫）
                if (cameraCount != 1)
                {
                    failures.Add($"④ 场景应有恰 1 台相机，实际 {cameraCount}（两套相机 ⇒ 取景/输入会串）。");
                }

                if (listenerCount != 1)
                {
                    failures.Add($"④ 场景应有恰 1 个 AudioListener，实际 {listenerCount}"
                                 + "（0 ⇒ 全场景无声；>1 ⇒ Unity 警告 + 音量异常）。");
                }

                // ⑤ PlayfieldRoot 缩放 == 契约常量（day3 的 SCN-02 / SCN-04 前提；1.2 是**有意**的玩法口径）
                if (playfieldRoot == null)
                {
                    failures.Add($"⑤ 场景里找不到 `{GreyboxSceneSetup.PlayfieldRootObjectName}`。");
                }
                else
                {
                    Vector3 s = playfieldRoot.transform.localScale;
                    float expected = PoolConstants.PlayfieldVisualScale;
                    bool scaleOk = Mathf.Abs(s.x - expected) <= 1e-6f
                                   && Mathf.Abs(s.y - expected) <= 1e-6f
                                   && Mathf.Abs(s.z - expected) <= 1e-6f;
                    if (!scaleOk)
                    {
                        failures.Add($"⑤ Main 的 PlayfieldRoot 缩放应为 {expected}（= PoolConstants.PlayfieldVisualScale，"
                                     + $"材质视觉验收 SCN-02 前提），实际 {s} ⇒ 会同时打红 SCN-02 与 SCN-04。");
                    }
                }

                Assert.IsEmpty(failures, "T127 失败：\n" + string.Join("\n", failures));
                TestContext.WriteLine("T127: Main 玩法宿主齐备、球 16 颗、相机 1 / AudioListener 1、"
                                      + $"PlayfieldRoot 缩放 {PoolConstants.PlayfieldVisualScale}（契约常量）、showDebugOverlay=false ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ── T132 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T132`：`Main.unity` 的显示层缩放 + "像素↔物理"拾取链**（**实现驱动**：在 Main 场景里取真实对象，
        /// 不重述公式、不读灰盒）。
        ///
        /// 为什么需要它：视觉端 1.2 与拾取端反缩放必须同步，而既有的 `T45`/`T46`（`PointerAimTests.cs`）
        /// **只读灰盒场景**。`Main.unity` 现在是**正式游戏入口** ⇒ 它这一侧必须有守卫，
        /// 否则"Main 上漏缩放 / 拾取端自成一派"没人守。
        ///
        /// **两半的判别力不同（如实登记，不得含糊）**：
        ///  · **(a) 缩放锚定**：Main 的 `PlayfieldRoot.localScale` 逐分量 == `PoolConstants.PlayfieldVisualScale`
        ///    ⇒ 抓"视觉根缩放漂移"。**这一半是缩放的守卫**。
        ///  · **(b) 像素↔物理互逆**：取 Main 的球可视物**台呢切点**世界位 → 相机投屏 → 既有
        ///    `PointerAimSolver.TryPickPhysicsPoint` 反解 → 比模拟器白球位 ⇒ 抓"拾取端漏反缩放 / 自成一派"
        ///    （这正是 `T46` 当年抓"漏 X/Z 双取反"的形态）。
        ///  ⚠ **(b) 对缩放是结构不变的** —— 反解用的是**同一个根** ⇒ 根缩放取任何值 (b) 都自洽。
        ///    ⇒ **(b) 抓不到缩放错**（那是 (a) 与 day3 `SCN-02` 的职责）。**不得**把 (b) 当作缩放的守卫。
        ///
        /// **盲带**：同 `T46` —— 只覆盖白球；切点像素避开"球心高经俯角"的系统性偏移，但
        /// "视觉端漏 1.2"对切点不敏感（由 (a) 补）。
        /// </summary>
        [Test]
        public void T132_MainVisualScale_AndPickupRoundTrip()
        {
            var failures = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject playfieldRoot = null;
                Camera cam = null;
                PhysicsTestDrive drive = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == GreyboxSceneSetup.PlayfieldRootObjectName)
                    {
                        playfieldRoot = root;
                    }

                    if (cam == null)
                    {
                        cam = root.GetComponentInChildren<Camera>(true);
                    }

                    if (drive == null)
                    {
                        drive = root.GetComponentInChildren<PhysicsTestDrive>(true);
                    }
                }

                Assert.IsNotNull(playfieldRoot, "T132：Main 里找不到 " + GreyboxSceneSetup.PlayfieldRootObjectName + "。");
                Assert.IsNotNull(cam, "T132：Main 里找不到相机。");
                Assert.IsNotNull(drive, "T132：Main 里找不到 PhysicsTestDrive。");

                // ── (a) 缩放锚定（这一半才是缩放的守卫）──
                Vector3 s = playfieldRoot.transform.localScale;
                float expected = PoolConstants.PlayfieldVisualScale;
                bool anchored = Mathf.Abs(s.x - expected) <= 1e-6f
                                && Mathf.Abs(s.y - expected) <= 1e-6f
                                && Mathf.Abs(s.z - expected) <= 1e-6f;
                if (!anchored)
                {
                    failures.Add($"(a) Main 的 PlayfieldRoot 缩放 {s} ≠ PlayfieldVisualScale {expected}"
                                 + " ⇒ 视觉端缩放漂移（拾取端会与它不同步）。");
                }

                // ── (b) 像素 ↔ 物理互逆（实现驱动）──
                EnsurePixelRect(cam);
                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T132：Initialize 后应有内核。");
                Assert.IsNotNull(drive.ballVisuals, "T132：球可视物未接线。");
                Assert.Greater(drive.ballVisuals.Length, 0, "T132：球可视物数组为空。");

                Transform ball0 = drive.ballVisuals[0];
                Assert.IsNotNull(ball0, "T132：白球可视物为空。");
                Vector2 cuePhys = drive.Simulator.Balls[0].position;

                // 台呢切点（显示世界）= 渲染球心 − 世界半径；半径真源 = 物理常量 × 视根世界缩放
                // （与 T46 同口径：可视物真模型的半径烘在网格里，不能拿 lossyScale×0.5 顶替）。
                float ballWorldRadius = PoolConstants.BallRadius * playfieldRoot.transform.lossyScale.y;
                Vector3 ballGround = ball0.position + Vector3.down * ballWorldRadius;

                Vector3 screen = cam.WorldToScreenPoint(ballGround);
                if (!(screen.z > 0f))
                {
                    failures.Add("(b) 白球不在相机前方（screen.z ≤ 0）⇒ 无法做投影。");
                }
                else if (!PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot.transform,
                             new Vector2(screen.x, screen.y), out Vector2 pPhys))
                {
                    failures.Add("(b) 拾取白球切点像素失败（TryPickPhysicsPoint 返回 false）。");
                }
                else
                {
                    float err = (pPhys - cuePhys).magnitude;
                    if (!(err < 2e-4f))
                    {
                        failures.Add($"(b) 屏幕反解物理位 {pPhys} 与模拟器位 {cuePhys} 误差 {err:F6} m 超容差 2e-4 m"
                                     + "（Main 上的拾取反缩放 / 坐标链被破坏）。");
                    }
                    else
                    {
                        TestContext.WriteLine($"T132: (a) 缩放锚定 {s} ✓；(b) 反解误差 {err:F6} m ✓");
                    }
                }

                Assert.IsEmpty(failures, "T132 失败：\n" + string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>批处理下相机 `pixelRect` 可能为 0 ⇒ 投影退化；与 `PointerAimTests` 同口径补一个像素矩形。</summary>
        private static void EnsurePixelRect(Camera cam)
        {
            if (cam.pixelWidth <= 0 || cam.pixelHeight <= 0)
            {
                cam.pixelRect = new Rect(0f, 0f, 1280f, 720f);
            }
        }

        // ── T128 ─────────────────────────────────────────────────────────

        /// <summary>
        /// **`T128`：玩法层生成是幂等的** —— 玩法层已在场时重复 `ApplyMainScene()` **不改动场景文件**
        /// （sha1 逐字节不变；这是本工程对"代码生成物"的幂等定义）。
        ///
        /// **为什么先断言前提**：若 Main 尚无玩法层，`ApplyMainScene()` 会**真的重建并写盘** ⇒
        /// 测试就产生了仓库改动（违 R3-5）。故"提交态 Main 必须已含玩法层"是**本测试的前置条件**，
        /// 不满足 ⇒ **红**（而不是偷偷写盘）。
        /// </summary>
        [Test]
        public void T128_MainGameplayLayer_ApplyIsIdempotent()
        {
            Assert.IsTrue(GreyboxSceneSetup.MainHasGameplayLayer(),
                "T128 前提不成立：提交态 Main.unity 必须已含玩法层（"
                + GreyboxSceneSetup.MainGameplayRootName + "）。请先跑重建入口。");

            string full = Path.Combine(ProjectRoot, MainScenePath);
            string before = Sha1(full);

            GreyboxSceneSetup.ApplyMainScene();

            string after = Sha1(full);
            Assert.AreEqual(before, after,
                "T128：重复 ApplyMainScene() 改动了场景文件（幂等被破坏）⇒ 每次跑批都会产生 git 噪音、"
                + "且手工改动会在重跑中丢失。");

            TestContext.WriteLine($"T128: ApplyMainScene() 幂等 ✓（sha1 {before} 不变）");
        }

        private static string Sha1(string path)
        {
            using (var sha = SHA1.Create())
            {
                byte[] bytes = File.ReadAllBytes(path);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
