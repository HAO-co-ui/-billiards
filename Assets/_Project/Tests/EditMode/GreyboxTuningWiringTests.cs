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
    /// **灰盒"物理调参接线"守卫**（`D7-T02` 阻塞项 ①-5）。
    ///
    /// 背景：`PhysicsTuning.asset` 一度**从未被运行时读取**（`tuning` 赋值点 0 处、场景未序列化它）
    /// ⇒ 灰盒恒走回落路径 ⇒「唯一可调入口」**名不副实**，用户在 ④ 里**不知道自己在测哪套参数**。
    ///
    /// 本判据守两件：
    ///  · **① 场景确实序列化了该资产**（按 guid 在场景文本里查证）；
    ///  · **② 生效参数 == 资产值**（打开场景取 `PhysicsTestDrive`，`Initialize()` 后比对 `Simulator.Params`）。
    ///
    /// **可被打死**：把 `GreyboxSceneSetup` 里的 `drive.tuning = …` 那行去掉并重建场景
    /// （或直接把场景里的 `tuning` 引用置空）⇒ 本判据 FAIL。
    /// </summary>
    public class GreyboxTuningWiringTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";

        [Test]
        public void T41_GreyboxScene_BindsTuningAsset_AndEffectiveParamsMatchAsset()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string sceneFull = Path.Combine(root, ScenePath);
            Assert.IsTrue(File.Exists(sceneFull), "T41：灰盒场景缺失 " + sceneFull);

            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "T41：找不到 " + TuningPath);

            string guid = AssetDatabase.AssetPathToGUID(TuningPath);
            Assert.IsFalse(string.IsNullOrEmpty(guid), "T41：取不到 PhysicsTuning 的 guid。");
            Assert.IsTrue(File.ReadAllText(sceneFull).Contains(guid),
                "T41：灰盒场景**未序列化** `PhysicsTuning` 资产 ⇒ `tuning` 运行时为 null、资产形同虚设。");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    drive = go.GetComponentInChildren<PhysicsTestDrive>(true);
                    if (drive != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(drive, "T41：场景内找不到 PhysicsTestDrive。");
                Assert.IsNotNull(drive.tuning, "T41：灰盒的 `tuning` 为空 ⇒ 走回落路径，资产不生效。");

                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T41：Initialize 后应有内核。");

                PhysicsParams effective = drive.Simulator.Params;
                Assert.AreEqual(tuning.ballRestitution, effective.ballRestitution, 1e-6f,
                    "T41：生效 e_ball 与资产值不一致 ⇒ 接线未生效。");
                Assert.AreEqual(tuning.cushionRestitution, effective.cushionRestitution, 1e-6f,
                    "T41：生效库边 e 与资产值不一致。");
                Assert.AreEqual(tuning.cushionTangentialFriction, effective.cushionTangentialFriction, 1e-6f,
                    "T41：生效库边 μ 与资产值不一致。");

                TestContext.WriteLine(
                    $"T41: tuning 已接线；生效 e_ball={effective.ballRestitution} "
                    + $"cushion=({effective.cushionRestitution},{effective.cushionTangentialFriction}) == 资产 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
