using System.IO;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **灰盒接线 / 漂移 / 出厂口径 三类守卫** + **瞄准指示物跟随口径**。
    ///
    /// 背景（`B4` 事故）：`PhysicsTuning.asset` 一度**从未被运行时读取**，且**初速没有资产字段** ⇒
    /// 测试全绿而灰盒实际用的是**场景里序列化的旧值**（`maxLaunchSpeed: 8`）。
    /// ⇒ 本文件守三件：**接线**（`T41`）、**场景漂移**（`T43`）、**出厂口径冻结**（`T44`）。
    /// </summary>
    public class GreyboxTuningWiringTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";

        private static PhysicsTestDrive FindDrive(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                PhysicsTestDrive drive = go.GetComponentInChildren<PhysicsTestDrive>(true);
                if (drive != null)
                {
                    return drive;
                }
            }

            return null;
        }

        // ══════════════════ T41 接线守卫（全部已接线参数）══════════════════════

        /// <summary>
        /// 灰盒场景必须**序列化** `PhysicsTuning` 资产，且 `Initialize()` 后**每一个已接线参数**的生效值 == 资产值
        /// （含 `B4` 新增的 `min/maxLaunchSpeed`，以及不可直接观测的 `rest*`/`pocketCaptureRadiusScale` —— 后三者经
        /// `PoolSimulator` 的只读出口取得）。
        /// **可被打死**：把场景里的 `tuning` 置空、或把任一参数从资产链上摘掉 ⇒ FAIL。
        /// </summary>
        [Test]
        public void T41_GreyboxScene_BindsTuningAsset_AndAllWiredParamsMatchAsset()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string sceneFull = Path.Combine(root, ScenePath);
            Assert.IsTrue(File.Exists(sceneFull), "T41：灰盒场景缺失 " + sceneFull);

            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "T41：找不到 " + TuningPath);
            string guid = AssetDatabase.AssetPathToGUID(TuningPath);
            Assert.IsFalse(string.IsNullOrEmpty(guid), "T41：取不到 guid。");
            Assert.IsTrue(File.ReadAllText(sceneFull).Contains(guid),
                "T41：灰盒场景**未序列化** `PhysicsTuning` 资产 ⇒ `tuning` 运行时为 null、资产形同虚设。");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = FindDrive(scene);
                Assert.IsNotNull(drive, "T41：场景内找不到 PhysicsTestDrive。");
                Assert.IsNotNull(drive.tuning, "T41：灰盒的 `tuning` 为空 ⇒ 走回落路径，资产不生效。");

                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T41：Initialize 后应有内核。");

                PhysicsParams p = drive.Simulator.Params;
                Assert.AreEqual(tuning.ballRestitution, p.ballRestitution, 1e-6f, "T41：生效 e_ball ≠ 资产。");
                Assert.AreEqual(tuning.ballTangentialFriction, p.ballTangentialFriction, 1e-6f, "T41：生效 μ_ball ≠ 资产。");
                Assert.AreEqual(tuning.cushionRestitution, p.cushionRestitution, 1e-6f, "T41：生效库边 e ≠ 资产。");
                Assert.AreEqual(tuning.cushionTangentialFriction, p.cushionTangentialFriction, 1e-6f, "T41：生效库边 μ ≠ 资产。");
                Assert.AreEqual(tuning.rollingDeceleration, p.rollingDeceleration, 1e-6f, "T41：生效滚阻 ≠ 资产。");

                Assert.AreEqual(tuning.restSpeedThreshold, drive.Simulator.RestSpeedThresholdValue, 1e-9f,
                    "T41：生效 restSpeedThreshold ≠ 资产（不可观测参数，经只读出口比对）。");
                Assert.AreEqual(tuning.restStableFrames, drive.Simulator.RestStableFramesValue,
                    "T41：生效 restStableFrames ≠ 资产。");
                Assert.AreEqual(tuning.pocketCaptureRadiusScale, drive.Simulator.PocketCaptureRadiusScaleValue, 1e-6f,
                    "T41：生效 pocketCaptureRadiusScale ≠ 资产。");

                // ★ `B4` 的核心：初速两项**必须**由资产提供
                Assert.AreEqual(tuning.minLaunchSpeed, drive.minLaunchSpeed, 1e-6f,
                    "T41：生效 minLaunchSpeed ≠ 资产（`B4`：初速未资产化）。");
                Assert.AreEqual(tuning.maxLaunchSpeed, drive.maxLaunchSpeed, 1e-6f,
                    "T41：生效 maxLaunchSpeed ≠ 资产（`B4`：初速未资产化）。");

                TestContext.WriteLine($"T41: tuning 接线 OK；e_ball={p.ballRestitution} cushion=({p.cushionRestitution},{p.cushionTangentialFriction}) "
                                      + $"a={p.rollingDeceleration} rest=({drive.Simulator.RestSpeedThresholdValue},{drive.Simulator.RestStableFramesValue}) "
                                      + $"scale={drive.Simulator.PocketCaptureRadiusScaleValue} launch=[{drive.minLaunchSpeed},{drive.maxLaunchSpeed}] ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T43 场景漂移守卫（B4 的机械复现）═══════════════════

        /// <summary>
        /// **已提交场景**里 `PhysicsTestDrive` 的**序列化值**必须 == **代码默认值**
        /// （即"代码改了默认、场景却是旧值"这种漂移会被拦下 —— `B4` 事故正是此形）。
        ///
        /// **可被打死**：把场景里 `maxLaunchSpeed` 改回 `8`（不动代码）⇒ FAIL。**只核至少初速两项 + 已接线物理参数字段**。
        /// </summary>
        [Test]
        public void T43_GreyboxScene_SerializedValues_MatchCodeDefaults()
        {
            var probeGo = new GameObject("T43Probe");
            try
            {
                var probe = probeGo.AddComponent<PhysicsTestDrive>();   // 新建实例 = **代码默认值**（未 Initialize）

                Assert.IsNotNull(probe, "T43：无法取代码默认值。");

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                try
                {
                    PhysicsTestDrive drive = FindDrive(scene);
                    Assert.IsNotNull(drive, "T43：场景内找不到 PhysicsTestDrive。");

                    Assert.AreEqual(probe.maxLaunchSpeed, drive.maxLaunchSpeed, 1e-6f,
                        $"T43：场景序列化的 maxLaunchSpeed（{drive.maxLaunchSpeed}）≠ 代码默认（{probe.maxLaunchSpeed}）⇒ **场景漂移**（`B4` 同款）。");
                    Assert.AreEqual(probe.minLaunchSpeed, drive.minLaunchSpeed, 1e-6f,
                        $"T43：场景序列化的 minLaunchSpeed（{drive.minLaunchSpeed}）≠ 代码默认（{probe.minLaunchSpeed}）。");
                    Assert.AreEqual(probe.rollingDeceleration, drive.rollingDeceleration, 1e-6f,
                        $"T43：场景序列化的 rollingDeceleration（{drive.rollingDeceleration}）≠ 代码默认（{probe.rollingDeceleration}）。");
                    Assert.AreEqual(probe.cushionRestitution, drive.cushionRestitution, 1e-6f,
                        $"T43：场景序列化的 cushionRestitution（{drive.cushionRestitution}）≠ 代码默认（{probe.cushionRestitution}）。");
                    Assert.AreEqual(probe.cushionTangentialFriction, drive.cushionTangentialFriction, 1e-6f,
                        $"T43：场景序列化的 cushionTangentialFriction（{drive.cushionTangentialFriction}）≠ 代码默认（{probe.cushionTangentialFriction}）。");
                    Assert.AreEqual(probe.ballRestitution, drive.ballRestitution, 1e-6f,
                        $"T43：场景序列化的 ballRestitution（{drive.ballRestitution}）≠ 代码默认（{probe.ballRestitution}）。");
                    Assert.AreEqual(probe.ballTangentialFriction, drive.ballTangentialFriction, 1e-6f,
                        $"T43：场景序列化的 ballTangentialFriction（{drive.ballTangentialFriction}）≠ 代码默认（{probe.ballTangentialFriction}）。");

                    TestContext.WriteLine($"T43: 场景序列化值 == 代码默认 ✓（launch=[{drive.minLaunchSpeed},{drive.maxLaunchSpeed}] a={drive.rollingDeceleration}）");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            finally
            {
                Object.DestroyImmediate(probeGo);
            }
        }

        // ══════════════════ T44 出厂口径冻结（B3）══════════════════════════════

        /// <summary>
        /// **出厂口径冻结**：`PhysicsTuning.asset` 的**每一个**字段值 == 台账口径
        /// （`0.8 / 0.9 / 0.9 / 0.95 / 1.0 / 1.0 / 1e-3 / 8 / 4.5 / 0.5`）。
        /// 改值**必须显式重钉本表**（与 `T37` 基线同规）—— 否则"测试全绿而手感被悄悄改掉"。
        /// **可被打死**：改资产任一字段 ⇒ FAIL。
        /// </summary>
        [Test]
        public void T44_FactoryConfig_MatchesLedger()
        {
            var t = AssetDatabase.LoadAssetAtPath<Billiards.Physics.PhysicsTuning>(TuningPath);
            Assert.IsNotNull(t, "T44：找不到 " + TuningPath);

            Assert.AreEqual(0.8f, t.cushionRestitution, 1e-6f, "T44：cushionRestitution 偏离台账。");
            Assert.AreEqual(0.9f, t.cushionTangentialFriction, 1e-6f, "T44：cushionTangentialFriction 偏离台账。");
            Assert.AreEqual(0.9f, t.rollingDeceleration, 1e-6f, "T44：rollingDeceleration 偏离台账。");
            Assert.AreEqual(0.95f, t.ballRestitution, 1e-6f, "T44：ballRestitution 偏离台账。");
            Assert.AreEqual(1f, t.ballTangentialFriction, 1e-6f, "T44：ballTangentialFriction 偏离台账。");
            Assert.AreEqual(1f, t.pocketCaptureRadiusScale, 1e-6f, "T44：pocketCaptureRadiusScale 偏离台账。");
            Assert.AreEqual(1e-3f, t.restSpeedThreshold, 1e-9f, "T44：restSpeedThreshold 偏离台账。");
            Assert.AreEqual(8, t.restStableFrames, "T44：restStableFrames 偏离台账。");
            Assert.AreEqual(4.5f, t.maxLaunchSpeed, 1e-6f, "T44：maxLaunchSpeed 偏离台账。");
            Assert.AreEqual(0.5f, t.minLaunchSpeed, 1e-6f, "T44：minLaunchSpeed 偏离台账。");

            TestContext.WriteLine("T44: 出厂口径冻结表全部吻合（0.8/0.9/0.9/0.95/1.0/1.0/1e-3/8/4.5/0.5）✓");
        }

        // ══════════════════ T42 瞄准指示物跟随口径 ═════════════════════════════

        /// <summary>
        /// **指示物跟随口径**（8 Ball Pool）：
        /// ① 全静止时**可见**，且朝向 == **独立闭式**算得的瞄准方向（**不得**读 `AimDirectionUnity` —— 同源即恒真，T42 第一版教训）；
        /// ② 位置 == 白球位置 + dir·(len/2)（同样用独立闭式）；
        /// ③ **球运动期间隐藏**（不得粘着滚动中的白球乱跑）。
        /// **可被打死**：朝向写死 ⇒ ①FAIL；去掉隐藏逻辑 ⇒ ③FAIL。
        /// </summary>
        [Test]
        public void T42_AimIndicator_FollowsRules()
        {
            var go = new GameObject("T42Drive");
            try
            {
                var drive = go.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = AssetDatabase.LoadAssetAtPath<Billiards.Table.TableConfig>(TablePath);
                Assert.IsNotNull(drive.tableConfig, "T42：需要 TableConfig.asset。");
                drive.Initialize();

                // 推进到"全员静止"（TableAtRest 需连续 8 步）
                for (int i = 0; i < 12; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                drive.aimDegrees = 30f;
                drive.SyncAimIndicator();

                Assert.IsNotNull(drive.aimIndicator, "T42：指示物未创建。");
                Assert.IsNotNull(drive.aimIndicator.GetComponent<MeshRenderer>(), "T42：指示物必须可渲染。");
                Assert.IsNull(drive.aimIndicator.GetComponent<Collider>(), "T42：指示物不得带碰撞体（R5-1）。");
                Assert.IsTrue(drive.aimIndicator.gameObject.activeSelf, "T42：全静止时指示物应**可见**。");

                float rad = 30f * Mathf.Deg2Rad;

                // 独立闭式：物理方向 `(cos θ, sin θ)` 经唯一转换链的**线性部分** ⇒ 世界 `(−cos θ, 0, −sin θ)`
                var expectedDir = new Vector3(-Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
                Assert.Less(Vector3.Angle(expectedDir, drive.aimIndicator.forward), 0.1f,
                    "T42：静止时指示物朝向与独立算得的**世界**方向不符（约定：世界 = (−cos θ, 0, −sin θ)）。");

                Vector3 cue = Billiards.Core.PoolGeometry.PhysicsBallToUnity(drive.Simulator.Balls[0].position);
                Vector3 expectedPos = cue + expectedDir * (drive.aimIndicatorLength * 0.5f);
                Assert.Less((expectedPos - drive.aimIndicator.position).magnitude, 1e-4f,
                    "T42：指示物位置应 == 白球位置 + dir·(len/2)（独立闭式）。");

                // ★ **经验交叉校验**：显示方向与**仿真位移**走**不同路径** ⇒ 任一侧的符号/约定再翻都会红。
                //   （这一条正是为"整类约定性错误"（如 180° 反向）加的牙：闭式段只证"实现 == 我写的约定"，
                //     经验段证"约定 == 白球真实走向"。）
                Vector3 shownDir = drive.aimIndicator.forward;   // 用户看到的方向（**Launch 前**捕获）
                Vector3 p0 = drive.Simulator.Balls[0].position;

                drive.power = 1f;
                drive.Launch();
                for (int i = 0; i < 3; i++)                      // 3 步 ≈ 0.22 m：不触库、不触球
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Vector3 moved = Billiards.Core.PoolGeometry.PhysicsBallToUnity(drive.Simulator.Balls[0].position)
                                - Billiards.Core.PoolGeometry.PhysicsBallToUnity(p0);
                Assert.Greater(moved.magnitude, 0.1f,
                    "T42：位移过小 ⇒ 经验交叉校验无意义（用例应保证不触库/不触球）。");
                Assert.Less(Vector3.Angle(shownDir, moved.normalized), 1f,
                    $"T42：指示物方向（显示路径）与白球世界位移方向（仿真路径）夹角 {Vector3.Angle(shownDir, moved.normalized):F3}° ⇒ 约定不一致。");

                // ③ 运动中隐藏
                drive.SyncAimIndicator();
                Assert.IsFalse(drive.aimIndicator.gameObject.activeSelf,
                    "T42：球运动期间指示物应**隐藏**（不得粘着滚动中的白球乱跑）。");

                TestContext.WriteLine("T42: 静止可见+朝向/位置独立吻合；运动中隐藏 ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
