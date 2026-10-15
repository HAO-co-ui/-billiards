using System.Collections.Generic;
using System.IO;
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
                Assert.AreEqual(tuning.rollingDecelerationSlide, p.rollingDecelerationSlide, 1e-6f,
                    "T41：生效滑动段滚阻 a_slide ≠ 资产（台呢两段模型的新参数未接进内核？）。");
                Assert.AreEqual(tuning.rollingDecelerationRoll, p.rollingDecelerationRoll, 1e-6f,
                    "T41：生效滚动段滚阻 a_roll ≠ 资产（台呢两段模型的新参数未接进内核？）。");
                Assert.AreEqual(tuning.rollingTransitionSpeed, p.rollingTransitionSpeed, 1e-6f,
                    "T41：生效过渡速度 v_transition ≠ 资产（台呢两段模型的新参数未接进内核？）。");

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
                                      + $"a=({p.rollingDecelerationSlide},{p.rollingDecelerationRoll},vt={p.rollingTransitionSpeed}) "
                                      + $"rest=({drive.Simulator.RestSpeedThresholdValue},{drive.Simulator.RestStableFramesValue}) "
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
        ///
        /// **`T62`（原地扩面）**：断言面已扩到台呢两段模型的**三个**滚动字段
        /// （`rollingDecelerationSlide` / `rollingDecelerationRoll` / `rollingTransitionSpeed`）——
        /// 这三项正是**最可能要调**的手感入口，不覆盖就等于"守卫恰好在最该守的地方是瞎的"。
        /// ⚠ 注入负对照**不得写死注入值**（若写死值与出厂值相等，注入成**空动作**、会得到"守卫有效"的假象）：
        /// 必须**先读真实出厂值**，再取一个必然不同的数（如 `出厂值 + 0.37`）。
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
                    Assert.AreEqual(probe.rollingDecelerationSlide, drive.rollingDecelerationSlide, 1e-6f,
                        $"T62：场景序列化的 rollingDecelerationSlide（{drive.rollingDecelerationSlide}）≠ 代码默认（{probe.rollingDecelerationSlide}）⇒ 台呢两段模型的手感入口上出现了“场景漂移”（`B4` 同款）。");
                    Assert.AreEqual(probe.rollingDecelerationRoll, drive.rollingDecelerationRoll, 1e-6f,
                        $"T62：场景序列化的 rollingDecelerationRoll（{drive.rollingDecelerationRoll}）≠ 代码默认（{probe.rollingDecelerationRoll}）⇒ 场景漂移。");
                    Assert.AreEqual(probe.rollingTransitionSpeed, drive.rollingTransitionSpeed, 1e-6f,
                        $"T62：场景序列化的 rollingTransitionSpeed（{drive.rollingTransitionSpeed}）≠ 代码默认（{probe.rollingTransitionSpeed}）⇒ 场景漂移。");
                    Assert.AreEqual(probe.cushionRestitution, drive.cushionRestitution, 1e-6f,
                        $"T43：场景序列化的 cushionRestitution（{drive.cushionRestitution}）≠ 代码默认（{probe.cushionRestitution}）。");
                    Assert.AreEqual(probe.cushionTangentialFriction, drive.cushionTangentialFriction, 1e-6f,
                        $"T43：场景序列化的 cushionTangentialFriction（{drive.cushionTangentialFriction}）≠ 代码默认（{probe.cushionTangentialFriction}）。");
                    Assert.AreEqual(probe.ballRestitution, drive.ballRestitution, 1e-6f,
                        $"T43：场景序列化的 ballRestitution（{drive.ballRestitution}）≠ 代码默认（{probe.ballRestitution}）。");
                    Assert.AreEqual(probe.ballTangentialFriction, drive.ballTangentialFriction, 1e-6f,
                        $"T43：场景序列化的 ballTangentialFriction（{drive.ballTangentialFriction}）≠ 代码默认（{probe.ballTangentialFriction}）。");

                    TestContext.WriteLine($"T43/T62: 场景序列化值 == 代码默认 ✓（launch=[{drive.minLaunchSpeed},{drive.maxLaunchSpeed}] "
                                          + $"a=({drive.rollingDecelerationSlide},{drive.rollingDecelerationRoll},vt={drive.rollingTransitionSpeed})）");
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
        /// （`0.8 / 0.9 / 0.5 / 0.25 / 2.0 / 0.95 / 1.0 / 1.0 / 1e-3 / 8 / 4.5 / 0.5`，**共 12 项**）。
        /// 表项数 = **10 − 1 + 3**：旧单值滚阻字段删 1、台呢两段模型加 3（滑动段 / 滚动段 / 过渡速度）。
        /// 改值**必须显式重钉本表**（与 `T37` 基线同规）—— 否则"测试全绿而手感被悄悄改掉"。
        /// **可被打死**：改资产任一字段 ⇒ FAIL（含把 `rollingDecelerationRoll` 改 `0.05`）。
        /// </summary>
        [Test]
        public void T44_FactoryConfig_MatchesLedger()
        {
            var t = AssetDatabase.LoadAssetAtPath<Billiards.Physics.PhysicsTuning>(TuningPath);
            Assert.IsNotNull(t, "T44：找不到 " + TuningPath);

            Assert.AreEqual(0.8f, t.cushionRestitution, 1e-6f, "T44：cushionRestitution 偏离台账。");
            Assert.AreEqual(0.9f, t.cushionTangentialFriction, 1e-6f, "T44：cushionTangentialFriction 偏离台账。");
            // 台呢两段模型：出厂 = **非退化两段档**（滑动段 0.5 / 滚动段 0.25 / 过渡速度 2.0；用户拍板档 ④）。
            // ⇒ 减速度随速度变（高速段扣得多、低速段扣得少）。退化档（两段同值）仍是显式逃生舱，由 `T54` 钉等价。
            Assert.AreEqual(0.5f, t.rollingDecelerationSlide, 1e-6f, "T44：rollingDecelerationSlide 偏离台账。");
            Assert.AreEqual(0.25f, t.rollingDecelerationRoll, 1e-6f, "T44：rollingDecelerationRoll 偏离台账。");
            Assert.AreEqual(2.0f, t.rollingTransitionSpeed, 1e-6f, "T44：rollingTransitionSpeed 偏离台账。");
            Assert.AreEqual(0.95f, t.ballRestitution, 1e-6f, "T44：ballRestitution 偏离台账。");
            Assert.AreEqual(1f, t.ballTangentialFriction, 1e-6f, "T44：ballTangentialFriction 偏离台账。");
            Assert.AreEqual(1f, t.pocketCaptureRadiusScale, 1e-6f, "T44：pocketCaptureRadiusScale 偏离台账。");
            Assert.AreEqual(1e-3f, t.restSpeedThreshold, 1e-9f, "T44：restSpeedThreshold 偏离台账。");
            Assert.AreEqual(8, t.restStableFrames, "T44：restStableFrames 偏离台账。");
            Assert.AreEqual(4.5f, t.maxLaunchSpeed, 1e-6f, "T44：maxLaunchSpeed 偏离台账。");
            Assert.AreEqual(0.5f, t.minLaunchSpeed, 1e-6f, "T44：minLaunchSpeed 偏离台账。");

            TestContext.WriteLine("T44: 出厂口径冻结表全部吻合（0.8/0.9/0.5/0.25/2.0/0.95/1.0/1.0/1e-3/8/4.5/0.5，共 12 项）✓");
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
                Assert.IsTrue(drive.aimIndicator.gameObject.activeSelf, "T42：全静止时指示物应**激活**（跟随口径的对象语义）。");

                // (d) **出厂渲染隐藏**：方向指示已由瞄准预测线承担 ⇒ showAimIndicator 出厂 false ⇒ 渲染器不可见。
                //     对象仍激活、位置仍更新（①②③ 的语义对象不变），仅渲染可见性关闭。
                Assert.IsFalse(drive.aimIndicator.GetComponent<MeshRenderer>().enabled,
                    "T42：showAimIndicator 出厂为 false ⇒ 渲染器应不可见（用户不应看到多余的柱状指示物）。");

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

        // ══════════════════ T42b 球可视物纯滚动自转（"滑行"→"滚动"）════════════

        /// <summary>
        /// **球可视物必须按位移做纯滚动自转**（纯滚动/无滑动 ⇒ 转角 = 弧长 / 半径）。
        ///
        /// ── 编号避让（必须保留此理由）──────────────────────────────────────────
        /// 本文件已占 `T41`/`T42`/`T43`/`T44`；Day 8 的 spec 已把 `T45`–`T53` 划给"相机与指针瞄准"
        /// ⇒ 本单**不得**占用上述任一编号，故取 **`T42b`**（既不与既有冲突，也不越界进 Day 8 区间）。
        ///
        /// ── 判据（均可被打死）──────────────────────────────────────────────────
        /// ① **单步闭式（带符号）**：从已知初态推进**恰好一步** ⇒ 朝向必须 == 独立闭式
        ///    `AngleAxis(|d|/r·Rad2Deg, (up × d).normalized)`（`d` = 该步世界位移）。
        ///    用 `|Quaternion.Dot(实际, 闭式)| ≈ 1` 判定 —— 该式**带符号**，故同时分辨
        ///    "漏转"（Dot ≈ cos(θ/2)）、"反向/轴反"（Dot ≈ cos θ）、"轴错"。
        /// ② **平移必然伴随旋转**：出杆后推进若干步 ⇒ `Quaternion.Angle(初始, 当前) > 阈值`。
        /// ③ **转角闭合式**：逐帧**弦长累加**的累计路径 / 半径 == 逐帧**朝向增量**累加的累计转角
        ///    （球会撞库 ⇒ 必须逐帧弦长，不能用首末直线距离）。
        /// ④ **不空转**：静止球连续推进若干帧 ⇒ 朝向**不变**。
        /// ⑤ `ballVisual`（单球兼容字段）分支同样自转（不能只改 `ballVisuals` 分支）。
        ///
        /// ── 实测注入反例（原文见交接报告）─────────────────────────────────────
        /// · 注入①（跳过自转那一行）⇒ ①`|Dot|≈cos(θ/2)<0.999` FAIL；②③ FAIL（转角恒 0）。
        /// · 注入②（转角取反 / 轴取反）⇒ ①`|Dot|≈cos θ (≈0.26)<0.999` FAIL。
        ///
        /// ── ⚠ 前置条件（**不是放宽断言**）────────────────────────────────────
        /// `Launch()` 现在**门控 `PoolSimulator.TableAtRest`**（"整盘未静止不许出杆"）。
        /// 本用例原先是"`Initialize()` 之后**立刻** `Launch()`" —— 那时 `RestStableFrames` 尚未满足，
        /// 出杆**会被拒绝**。故两处 `Launch()` 之前都**先推进到静止**（`RestStableFrames` + 2 步）。
        /// 这两步把"什么时候才能出杆"从**隐含约定**变成**显式前置条件**：
        /// 断言一条没删、阈值一处没动，只是把**被门控挡住的那次出杆**换成了合法出杆。
        /// </summary>
        [Test]
        public void T42b_BallVisuals_Roll_ByDisplacement()
        {
            const float r = Billiards.Core.PoolConstants.BallRadius;

            // ── ①②③ 数组分支（ballVisuals）+ 闭合式 ────────────────────────────
            var driveGo = new GameObject("T42bDrive");
            var visualGo = new GameObject("T42bBallVisual");
            try
            {
                var drive = driveGo.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = AssetDatabase.LoadAssetAtPath<Billiards.Table.TableConfig>(TablePath);
                Assert.IsNotNull(drive.tableConfig, "T42b：需要 TableConfig.asset。");

                drive.loadRackForBallBallDemo = false;            // 单球：路径简单、判据可控
                drive.ballVisuals = new[] { visualGo.transform };  // 走数组分支
                drive.ballVisual = null;
                drive.Initialize();

                Assert.IsNotNull(drive.Simulator, "T42b：Initialize 后应有内核。");
                Assert.AreEqual(1, drive.Simulator.Balls.Count, "T42b：单球模式应恰好 1 颗球。");

                visualGo.transform.rotation = Quaternion.identity;   // 已知初态，便于与闭式直接比对
                drive.aimDegrees = 180f;                             // 世界走向 ≈ +x（不朝袋口）
                drive.power = 1f;                                    // 初速 4.5 m/s（单步 ≈ 0.0375 m）

                // ★ 前置条件显式化（不是放宽断言）：`Launch()` 现在门控 `PoolSimulator.TableAtRest`
                // ⇒ 必须先推进到静止（`RestStableFrames` + 2 步余量）才可能出杆成功。
                // 推进期间球静止 ⇒ `SyncBallVisual` 的位移为 0 ⇒ **不自转**，故不影响下面的自转闭式。
                for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.IsTrue(drive.Simulator.TableAtRest,
                    "T42b：前提 —— 推进 RestStableFrames + 2 步后整盘应静止（否则出杆会被门控拒绝）。");
                drive.Launch();

                Assert.Less(Quaternion.Angle(Quaternion.identity, visualGo.transform.rotation), 1e-3f,
                    "T42b：出杆首帧不应自转（无可靠上一帧位置 ⇒ 只落位）。");

                // ① 单步闭式（带符号 —— 同时分辨漏转/反向/轴错）
                Vector3 p0 = Billiards.Core.PoolGeometry.PhysicsBallToUnity(drive.Simulator.Balls[0].position);
                drive.Advance(PoolSimulator.FixedDeltaTime);
                Vector3 p1 = Billiards.Core.PoolGeometry.PhysicsBallToUnity(drive.Simulator.Balls[0].position);
                Vector3 d1 = p1 - p0;

                Vector3 axis1 = Vector3.Cross(Vector3.up, d1).normalized;
                float angle1Deg = d1.magnitude / r * Mathf.Rad2Deg;
                Quaternion expected1 = Quaternion.AngleAxis(angle1Deg, axis1);
                float dot1 = Mathf.Abs(Quaternion.Dot(visualGo.transform.rotation, expected1));
                // 单步 ≈ 75°，净角无歧义 ⇒ ② 用它（避免长程净角绕圈后"看似没转"的假阴性）
                Quaternion rotAfterOneStep = visualGo.transform.rotation;

                // ③ 长程：逐帧弦长累加 vs 逐帧朝向增量累加
                Quaternion prevRot = visualGo.transform.rotation;
                Vector3 prevPos = p1;
                float path = 0f;
                float angleSum = 0f;
                const int steps = 90;
                for (int i = 0; i < steps; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                    Vector3 now = Billiards.Core.PoolGeometry.PhysicsBallToUnity(drive.Simulator.Balls[0].position);
                    path += (now - prevPos).magnitude;          // 逐帧弦长（球会撞库 ⇒ 不得用首末直线）
                    prevPos = now;
                    Quaternion rot = visualGo.transform.rotation;
                    // 每步只绕**一个世界轴** `Rotate` 一次 ⇒ `prevRot⁻¹ × rot` 的转角 == 该步施加的转角，可加。
                    angleSum += Quaternion.Angle(prevRot, rot);
                    prevRot = rot;
                }

                float expectedAngle = path / r * Mathf.Rad2Deg;
                float tol = Mathf.Max(1f, expectedAngle * 0.005f);

                // ★ NUnit（Unity 的 `com.unity.ext.nunit` 3.5 分支）**不支持 `Assert.Multiple`**
                //   （实测 `Assert` 无 `Multiple` 成员 ⇒ `CS0117`）。改用**手工汇总**：三条判据各自求值、
                //   把失败项收齐后一次报告 ⇒ 单次注入能同时暴露它影响的**全部**判据，
                //   而不是像默认快速失败那样只看到第一条（那无法证明 ②③ 也能各自红）。
                var failures = new System.Collections.Generic.List<string>();
                if (!(d1.magnitude > 0.005f))
                {
                    failures.Add("① 前提：单步位移过小 ⇒ 闭式检查无意义。");
                }

                if (!(dot1 > 0.999f))
                {
                    failures.Add($"① 单步自转与独立闭式不符（|Dot|={dot1:F4}；相同=1，漏转≈{Mathf.Cos(angle1Deg * 0.5f * Mathf.Deg2Rad):F4}，"
                                 + $"反向/轴反≈{Mathf.Abs(Mathf.Cos(angle1Deg * Mathf.Deg2Rad)):F4}）⇒ 漏转 / 反向 / 轴错。");
                }

                if (!(Quaternion.Angle(Quaternion.identity, rotAfterOneStep) > 20f))
                {
                    failures.Add("② 球滚了 3.7 cm 而朝向几乎没变 ⇒ 出现'滑行'。");
                }

                if (!(path > 0.3f))
                {
                    failures.Add("③ 前提：累计路径过小 ⇒ 闭合式检查无意义。");
                }

                if (!(Mathf.Abs(angleSum - expectedAngle) < tol))
                {
                    failures.Add($"③ 累计转角 {angleSum:F3}° ≠ 累计路径/半径 {expectedAngle:F3}°（弦长 {path:F6} m，容差 {tol:F3}°）。");
                }

                Assert.IsEmpty(failures, "T42b 失败判据：\n" + string.Join("\n", failures));

                // ④ 不空转：静止球推进多帧 ⇒ 朝向不变
                drive.Initialize();                              // 重新装载到静止初态（球停在开球点）
                visualGo.transform.rotation = Quaternion.identity;
                for (int i = 0; i < 30; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.Less(Quaternion.Angle(Quaternion.identity, visualGo.transform.rotation), 1e-3f,
                    "T42b：静止球不应自转（无条件 Rotate / 数值噪声抖动造成的假旋转）。");

                TestContext.WriteLine($"T42b: 单步闭式 |Dot|={dot1:F5}；累计路径 {path:F4} m ⇒ 期望 {expectedAngle:F1}° / 实测 {angleSum:F1}°；静止 30 帧朝向不变 ✓");
            }
            finally
            {
                Object.DestroyImmediate(driveGo);
                Object.DestroyImmediate(visualGo);
            }

            // ── ⑤ 单球兼容字段 `ballVisual` 分支同样自转 ─────────────────────────
            var drive2Go = new GameObject("T42bDrive2");
            var visual2Go = new GameObject("T42bBallVisual2");
            try
            {
                var drive2 = drive2Go.AddComponent<PhysicsTestDrive>();
                drive2.tableConfig = AssetDatabase.LoadAssetAtPath<Billiards.Table.TableConfig>(TablePath);
                drive2.loadRackForBallBallDemo = false;
                drive2.ballVisuals = null;                       // 只走单球分支
                drive2.ballVisual = visual2Go.transform;
                drive2.Initialize();

                visual2Go.transform.rotation = Quaternion.identity;
                drive2.aimDegrees = 180f;
                drive2.power = 1f;

                // ★ 前置条件显式化：同 T42b 主分支 —— 先推进到静止，`Launch()` 才会被门控放行。
                for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
                {
                    drive2.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.IsTrue(drive2.Simulator.TableAtRest,
                    "T42b(⑤)：前提 —— 推进 RestStableFrames + 2 步后整盘应静止。");
                drive2.Launch();
                drive2.Advance(PoolSimulator.FixedDeltaTime);

                Assert.Greater(Quaternion.Angle(Quaternion.identity, visual2Go.transform.rotation), 20f,
                    "T42b：`ballVisual` 单球分支未自转（只改了 ballVisuals 分支）。");
            }
            finally
            {
                Object.DestroyImmediate(drive2Go);
                Object.DestroyImmediate(visual2Go);
            }
        }

        // ══════════════════ T103 球桌可视物在场 ════════════════════════════════

        /// <summary>
        /// **灰盒场景必须有「看得见的球桌」**（用户实机反馈"场景全黑、看不到球袋在哪"后补的守卫）。
        ///
        /// 背景（本判据存在的理由）：`PhysicsGreybox` **此前从未建过桌模型** —— 库边全靠
        /// `CushionGizmo` 的 `Gizmos.DrawLine` 画，而 **`Gizmos` 只在编辑器 Scene 视图可见**，
        /// Game 视图里就是黑底加几颗球。这类"组件在、但游戏里根本看不见"的缺陷，
        /// 编译与纯逻辑判据都抓不到 ⇒ 必须由**场景结构判据**兜。
        ///
        /// **可被打死**：把桌实例从场景删掉 ⇒ 红；把地面挪出可视根（桌会浮空）⇒ (c) 红；
        /// 把袋网材质从 submesh 1 摘掉 ⇒ (b) 红。
        /// </summary>
        [Test]
        public void T103_GreyboxScene_HasVisibleTable()
        {
            var failures = new List<string>();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform table = null;
                Transform floor = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == TableVisualBuilder.TableObjectName)
                        {
                            table = child;
                        }
                        else if (child.name == TableVisualBuilder.FloorObjectName)
                        {
                            floor = child;
                        }
                    }
                }

                var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.BodyMaterialPath);
                var netsMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.NetsMaterialPath);
                var floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.FloorMaterialPath);

                // (a) 桌在场，且有可渲染网格
                if (table == null)
                {
                    failures.Add($"(a) 场景里找不到球桌实例「{TableVisualBuilder.TableObjectName}」⇒ Game 视图会是一片黑底。");
                }
                else
                {
                    MeshRenderer[] renderers = table.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length == 0)
                    {
                        failures.Add("(a) 球桌实例下没有任何 MeshRenderer ⇒ 建了但看不见。");
                    }

                    // (b) 袋网材质必须在 submesh 1 上（模型的子网格约定：0 = 桌体、1 = 袋网）
                    bool sawNetSubmesh = false;
                    foreach (MeshRenderer renderer in renderers)
                    {
                        Material[] materials = renderer.sharedMaterials;
                        if (materials == null || materials.Length == 0)
                        {
                            failures.Add($"(b) 「{renderer.name}」没有指派材质。");
                            continue;
                        }

                        for (int i = 0; i < materials.Length; i++)
                        {
                            if (materials[i] == null)
                            {
                                failures.Add($"(b) 「{renderer.name}」子网格 {i} 的材质为空 ⇒ 该面会显示成品红/默认材质。");
                                continue;
                            }

                            if (i == 1)
                            {
                                sawNetSubmesh = true;
                                if (materials[i] != netsMaterial)
                                {
                                    failures.Add($"(b) 「{renderer.name}」子网格 1（袋网）应用 "
                                                 + $"{MaterialVisualSetup.NetsMaterialPath}，实际 {materials[i].name}。");
                                }
                            }
                            else if (materials[i] != bodyMaterial)
                            {
                                failures.Add($"(b) 「{renderer.name}」子网格 {i}（桌体）应用 "
                                             + $"{MaterialVisualSetup.BodyMaterialPath}，实际 {materials[i].name}。");
                            }
                        }
                    }

                    if (!sawNetSubmesh)
                    {
                        failures.Add("(b) 没有任何网格带 submesh 1 ⇒ 袋网材质没落上（袋口会与桌体同色，看不出袋在哪）。");
                    }

                    // (c) 地面在场、材质正确、且**与桌同一缩放口径**（否则桌会浮空）。
                    //     ⚠ 判据形式是被实测修正过的：最初断言"地面与桌的父级相同"——**那是错的**，
                    //     地面挂在 `Room` 下、桌挂在 `PlayfieldRoot` 下，层级形状不同但缩放口径一致。
                    //     真正的不变量是**世界坐标层面**的：桌腿底与地面齐平，且两者被同一倍数缩放。
                    if (floor == null)
                    {
                        failures.Add("(c) 场景里找不到地面（Floor）。");
                    }
                    else
                    {
                        var floorRenderer = floor.GetComponent<MeshRenderer>();
                        if (floorRenderer == null || floorRenderer.sharedMaterial != floorMaterial)
                        {
                            failures.Add($"(c) 地面材质应为 {MaterialVisualSetup.FloorMaterialPath}，"
                                         + $"实际 {(floorRenderer == null || floorRenderer.sharedMaterial == null ? "null" : floorRenderer.sharedMaterial.name)}。");
                        }

                        float floorWorldY = floor.position.y;
                        float legBottomWorldY = PoolConstants.TableLegBottomY * table.lossyScale.y;
                        if (Mathf.Abs(floorWorldY - legBottomWorldY) > 1e-4f)
                        {
                            failures.Add($"(c) 地面世界高度 {floorWorldY:R} ≠ 桌腿底世界高度 {legBottomWorldY:R}"
                                         + $"（= {PoolConstants.TableLegBottomY:R} × 桌缩放 {table.lossyScale.y:R}）"
                                         + " ⇒ 桌子会浮在地面上方（或陷进地面）。");
                        }

                        if (Mathf.Abs(floor.lossyScale.y - table.lossyScale.y) > 1e-6f)
                        {
                            failures.Add($"(c) 地面缩放 {floor.lossyScale.y:R} ≠ 桌缩放 {table.lossyScale.y:R}"
                                         + " ⇒ 两者不在同一显示口径下，迟早错位。");
                        }
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            Assert.IsEmpty(failures, "T103 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T103: 球桌实例在场、子网格材质正确（桌体 / 袋网）、地面同父且材质正确 ✓");
        }
    }
}
